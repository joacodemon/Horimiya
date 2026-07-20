using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Horimiya.Config;
using Horimiya.Utils;

// Performance optimization notes:
// - Cached focus/cursor checks to avoid per-tick P/Invoke overhead
// - Async sound to prevent blocking the click thread
// - timeBeginPeriod(1) for accurate Thread.Sleep timing
// - Reduced GDI pixel sampling frequency
// - Pre-allocated INPUT structs to minimize GC pressure

namespace Horimiya.Modules
{
    [Injectable(true)]
    public class Clicker
    {
        private readonly AppConfig _cfg;

        private readonly Random _rng = new Random();
        private bool _lastInvResult = false;
        private Stopwatch _invCheckTimer = new Stopwatch();
        private Thread _thread;
        private volatile bool _running = false;

        // Refill state tracking
        private int _refillClickCount = 0;
        private bool _wasBbActive = false;
        private const double REFILL_CPS_MIN = 25.0;
        private const double REFILL_CPS_MAX = 38.0;

        private double _stableCps = double.NaN; // smoothed CPS for ultra-stable calculation

        // ── Pacer State ──
        private long m_counter = 0;
        private double m_jitterValue = 0.0;
        private double m_jitterTarget = 0.0;
        private int m_jitterStep = 0;
        private int m_jitterSteps = 12;
        private double m_lastIntervalMs = 0.0;
        private double m_lastDownMs = 0.0;
        private double m_intervalEma = 0.0;
        private double m_downEma = 0.0;

        private double NextUniform(double minV, double maxV) { return minV + _rng.NextDouble() * (maxV - minV); }
        private double NextTriangular(double minV, double maxV) { 
            double u1 = NextUniform(0.0, 1.0); 
            double u2 = NextUniform(0.0, 1.0); 
            return minV + (maxV - minV) * (0.5 * (u1 + u2)); 
        }
        private double NextSmoothedJitter() {
            if (m_jitterStep >= m_jitterSteps) {
                m_jitterTarget = NextUniform(-1.0, 1.0);
                m_jitterSteps = (int)NextUniform(14.0, 26.0); // más pasos = transición más larga y suave
                m_jitterStep = 0;
            }
            m_jitterValue += (m_jitterTarget - m_jitterValue) * 0.10; // más lento = más smooth
            m_jitterStep++;
            return m_jitterValue;
        }
        private double ClampChange(double value, double lastValue, double maxDelta) {
            if (lastValue <= 0.0) return value;
            double delta = value - lastValue;
            if (delta > maxDelta) return lastValue + maxDelta;
            if (delta < -maxDelta) return lastValue - maxDelta;
            return value;
        }

        // ── Performance cache fields ──
        // Cache Minecraft focus check to avoid StringBuilder alloc + GetWindowText every tick
        private bool _lastFocusResult = false;
        private IntPtr _lastFocusHwnd = IntPtr.Zero;
        private Stopwatch _focusCheckTimer = new Stopwatch();
        private const int FOCUS_CHECK_INTERVAL_MS = 100; // Re-check focus every 100ms

        // Cache cursor visibility to reduce P/Invoke calls
        private bool _lastCursorVisible = false;
        private Stopwatch _cursorCheckTimer = new Stopwatch();
        private const int CURSOR_CHECK_INTERVAL_MS = 50; // Re-check cursor every 50ms


        // Reusable StringBuilder for window title checks (avoids GC pressure)
        private readonly StringBuilder _titleBuffer = new StringBuilder(256);

        public volatile bool Clicking = false;
        public volatile bool RightClicking = false;

        // ── Live Stats ──
        public double StatLiveCps = 0;
        public double StatAvgCps = 0;
        public double StatInterval = 0;
        public double StatJitter = 0;
        public double StatLast = 0;
        public int StatLate = 0;
        public double StatWorstLate = 0;
        public int StatSamples = 0;
        public double StatHitRate = 0;
        public int StatTotalHits = 0;
        public int StatTotalMisses = 0;
        private long _lastClickFinishTick = 0;

        // Hit detection: async check scheduling
        private volatile IntPtr _hitCheckHwnd = IntPtr.Zero;
        private volatile bool _hitCheckPending = false;
        
        // Right clicker thread
        private Thread _rightThread;

        public Clicker(AppConfig cfg)
        {
            _cfg = cfg;
        }

        // Call this when switching profiles so EMA/jitter state doesn't bleed across presets.
        public void ResetTimingState()
        {
            m_counter       = 0;
            m_jitterValue   = 0.0;
            m_jitterTarget  = 0.0;
            m_jitterStep    = 0;
            m_intervalEma   = 0.0;
            m_downEma       = 0.0;
            m_lastIntervalMs= 0.0;
            m_lastDownMs    = 0.0;
            _stableCps      = double.NaN;
        }

        // Obsolete, left for compatibility if needed.
        private double GetCurrentCps()
        {
            return _cfg.AverageCps;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            // Set Windows timer resolution to 1ms for accurate Thread.Sleep
            // Without this, Thread.Sleep(1) can sleep up to 15.6ms causing timing jitter
            // which manifests as crosshair teleporting and irregular click timing
            Win32.timeBeginPeriod(1);

            _thread = new Thread(ClickLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
            _thread.Start();

            _rightThread = new Thread(RightClickLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
            _rightThread.Start();
        }

        public void Stop()
        {
            _running = false;
            Win32.timeEndPeriod(1);
        }

        private void ApplyMouseJitter()
        {
            if (_cfg.MouseJitterEnabled)
            {
                int dx = (int)((_rng.NextDouble() - 0.5) * _cfg.MouseJitterStrength * 2.0);
                int dy = (int)((_rng.NextDouble() - 0.5) * _cfg.MouseJitterStrength * 2.0);
                if (dx != 0 || dy != 0)
                    Win32.mouse_event(0x0001, (uint)dx, (uint)dy, 0, 0); // 0x0001 = MOUSEEVENTF_MOVE
            }
        }

        private void ClickLoop()
        {
            var sw = new Stopwatch();
            sw.Start();
            long nextClickTick = sw.ElapsedTicks;
            double currentJitterCps = GetCurrentCps();
            double jitterMomentum = 0;

            // ── Aim-Assist compatible state ────────────────────────────────────
            bool globalHoldActive = false; // Mantiene el LMB "presionado" globalmente para Toggle/Always

            long physicalLeftDownStart = 0;
            bool physicalLeftWasDown = false;

            Stopwatch burstTimer = new Stopwatch();
            burstTimer.Start();
            bool inBurst = false;
            double nextBurstTime = 3000;
            double burstEndTime = 0;

            // Track when we're actively clicking so we can reset the scheduler on re-entry
            bool _wasActiveLastTick = false;

            while (_running)
            {
                bool isPhysicalDown = Win32.IsLeftDown;
                if (isPhysicalDown && !physicalLeftWasDown) { physicalLeftDownStart = sw.ElapsedMilliseconds; }
                else if (!isPhysicalDown) { physicalLeftDownStart = 0; }
                physicalLeftWasDown = isPhysicalDown;


                // Si Clicking esta OFF, dormir
                if (!Clicking)
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(15);
                    _focusCheckTimer.Reset();
                    _cursorCheckTimer.Reset();
                    _wasBbActive = false;
                    _wasActiveLastTick = false;
                    nextClickTick = sw.ElapsedTicks; // reset scheduler to avoid CPS burst on re-enable
                    continue;
                }

                // ── Focus check FIRST (before any global input) ──
                // Must check BEFORE Toggle/Always globalHold so we never send global clicks to desktop.
                IntPtr foregroundWnd = Win32.GetForegroundWindow();
                if (!CachedIsMinecraftFocused(foregroundWnd))
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(10);
                    _wasActiveLastTick = false;
                    nextClickTick = sw.ElapsedTicks; // reset scheduler — prevent CPS spike on refocus
                    continue;
                }

                // ── Mode check ──
                bool shouldClick;
                if (_cfg.Mode == 0) // Hold
                    shouldClick = Win32.IsLeftDown;
                else
                    shouldClick = true;

                if (!shouldClick)
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(1); // reduced sleep to keep high CPS
                    nextClickTick = sw.ElapsedTicks;
                    _wasActiveLastTick = false;
                    continue;
                }
                else if (_cfg.Mode != 0) // Toggle o Always
                {
                    // En Toggle/Always el usuario no está sosteniendo el click físico.
                    // XClient necesita ver el LMB presionado para activar el aim assist.
                    // Solo enviamos el DOWN si Minecraft está en foco (chequeado arriba).
                    if (!globalHoldActive)
                    {
                        Win32.SendLeftDownNative();
                        globalHoldActive = true;
                    }
                }

                // Reset scheduler if we're re-entering active clicking after an idle period
                // This prevents the tick accumulation from causing a CPS burst.
                if (!_wasActiveLastTick)
                {
                    nextClickTick = sw.ElapsedTicks;
                    _wasActiveLastTick = true;
                }

                // ── RMB-Lock ──
                // Keep the main left-click loop alive during blockhit-like situations.
                // The old logic paused the whole clicker whenever RMB was held, which broke attacks.
                bool blockHitSuppressed = _cfg.RmbLock && Win32.IsRightDown;

                // ── Menu / Inventory restriction ──
                bool cursorShown = CachedIsCursorVisible();
                if (cursorShown && !_cfg.WorkInMenus)
                {
                    if (!CachedIsInventoryLikeScreen(foregroundWnd))
                    {
                        if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                        Thread.Sleep(10);
                        continue;
                    }
                }

                // Refill check - Smart Refill Mode
                bool inventoryLikeScreen = cursorShown && CachedIsInventoryLikeScreen(foregroundWnd);
                bool isRefilling = false;
                
                // Smart Refill: auto shift+click when cursor is in bottom half of inventory
                if (_cfg.RefillMode && inventoryLikeScreen && CachedIsMinecraftFocused(foregroundWnd))
                {
                    Win32.RECT rect;
                    if (Win32.GetClientRect(foregroundWnd, out rect))
                    {
                        System.Drawing.Point screenPt;
                        Win32.GetCursorPos(out screenPt);
                        Win32.POINT clientPt = new Win32.POINT { X = screenPt.X, Y = screenPt.Y };
                        Win32.ScreenToClient(foregroundWnd, ref clientPt);
                        
                        int windowHeight = rect.bottom - rect.top;
                        // If cursor is in the bottom 40% of the window (inventory slots area)
                        if (clientPt.Y > windowHeight * 0.60)
                        {
                            isRefilling = true;
                            // Hold shift virtually for shift-click
                            Win32.keybd_event(0x10, 0, 0, 0); // Shift DOWN
                            Thread.Sleep(1);
                        }
                    }
                }

                // ── Advanced Pacer Randomization ──
                double targetCps = GetCurrentCps();

                if (_cfg.BurstEnabled && shouldClick && isPhysicalDown)
                {
                    if (!inBurst && burstTimer.ElapsedMilliseconds >= nextBurstTime)
                    {
                        inBurst = true;
                        burstEndTime = burstTimer.ElapsedMilliseconds + _cfg.BurstDurationMs;
                    }
                    else if (inBurst && burstTimer.ElapsedMilliseconds >= burstEndTime)
                    {
                        inBurst = false;
                        nextBurstTime = burstTimer.ElapsedMilliseconds + _cfg.BurstIntervalMin * 1000.0 + _rng.NextDouble() * Math.Max(0, (_cfg.BurstIntervalMax - _cfg.BurstIntervalMin)) * 1000.0;
                    }

                    if (inBurst)
                    {
                        targetCps = Math.Min(30.0, targetCps + 6.0 + _rng.NextDouble() * 5.0);
                    }
                }
                else if (!isPhysicalDown)
                {
                    burstTimer.Restart();
                    inBurst = false;
                    nextBurstTime = _cfg.BurstIntervalMin * 1000.0 + _rng.NextDouble() * Math.Max(0, (_cfg.BurstIntervalMax - _cfg.BurstIntervalMin)) * 1000.0;
                }

                bool isButterfly = false;
                double delayMs;
                double downMs = 2.0;
                double pingMs = 0;

                if (isRefilling)
                {
                    delayMs = 1000.0 / 20.0;
                }
                else
                {
                    double cpsMin = _cfg.MinCps;
                    double cpsMax = _cfg.MaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    if (_cfg.BurstEnabled && inBurst)
                    {
                        cpsMin = Math.Min(30.0, cpsMin + 6.0);
                        cpsMax = Math.Min(30.0, cpsMax + 6.0);
                    }

                    double cps = NextTriangular(cpsMin, cpsMax);
                    double interval = 1000.0 / cps;

                    pingMs = _cfg.PingMs;
                    double ping = Math.Max(20.0, Math.Min(200.0, pingMs));
                    double pingT = (ping - 20.0) / 180.0;

                    double baseRatio = 0.42;
                    double jitterAmplitude = 0.38;
                    double maxIntervalDelta = 0.28;
                    double intervalSmoothing = 0.12;
                    double downSmoothing = 0.18;

                    if (_cfg.RandMode == 0) // Jitter / Smooth
                    {
                        baseRatio = 0.46;
                        jitterAmplitude = 0.18;       // menos amplitud → menos saltos bruscos
                        maxIntervalDelta = 0.16;      // cambios de intervalo más graduales
                        intervalSmoothing = 0.12;     // EMA más lenta = transiciones suaves
                        downSmoothing = 0.16;
                    }
                    else if (_cfg.RandMode == 2) // NoDelay / Competitive
                    {
                        baseRatio = 0.40;
                        jitterAmplitude = 0.13;
                        maxIntervalDelta = 0.14;
                        intervalSmoothing = 0.09;
                        downSmoothing = 0.13;
                    }
                    else if (_cfg.RandMode == 1) // Butterfly
                    {
                        isButterfly = true;
                        // Butterfly manda 2 clicks por ciclo, así que doblamos el intervalo
                        // para que la CPS efectiva sea la que configuró el usuario (no el doble).
                        interval *= 2.0;
                    }

                    baseRatio += pingT * 0.03;
                    jitterAmplitude *= (1.0 - 0.55 * pingT);

                    double drift = Math.Sin(m_counter * 0.018) * 0.055; // drift más lento y sutil
                    interval += drift;

                    if (m_intervalEma <= 0.0) m_intervalEma = interval;
                    else m_intervalEma = m_intervalEma * (1.0 - intervalSmoothing) + interval * intervalSmoothing;

                    interval = m_intervalEma;
                    interval = ClampChange(interval, m_lastIntervalMs, maxIntervalDelta);

                    double down = interval * baseRatio;
                    double jitter = NextSmoothedJitter() * jitterAmplitude;
                    down += jitter;

                    double minDown = Math.Max(6.5, interval * 0.38); // hold mínimo más largo = más fluido
                    double maxDown = interval - 0.6;
                    if (maxDown < minDown) maxDown = minDown + 0.2;
                    down = Math.Max(minDown, Math.Min(maxDown, down));

                    if (m_downEma <= 0.0) m_downEma = down;
                    else m_downEma = m_downEma * (1.0 - downSmoothing) + down * downSmoothing;

                    down = ClampChange(m_downEma, m_lastDownMs, 0.20); // clamping más suave

                    double gap = interval - down;
                    if (gap < 0.4) {
                        gap = 0.4;
                        down = interval - gap;
                    }

                    m_lastIntervalMs = interval;
                    m_lastDownMs = down;
                    m_counter++;

                    delayMs = interval;
                    downMs = down;
                }

                // ── Ping-Aware Timing / Latency Compensation ──
                // Ajusta dinámicamente el intervalo de clicks según la latencia.
                // Calcula el tiempo de llegada al servidor sumando el ping, y alinea el click
                // para que llegue justo antes del siguiente procesamiento de tick del servidor (50ms),
                // maximizando la consistencia en los combos.
                
                pingMs = _cfg.PingMs;
                if (pingMs > 0)
                {
                    // Asumimos un ciclo de tick de servidor de 50ms
                    double arrivalTimeMs = (sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency) + delayMs + pingMs;
                    double tickRemainder = arrivalTimeMs % 50.0;
                    
                    // Queremos que el paquete llegue justo antes del límite de los 50ms (ej. 48ms)
                    double targetOffset = 48.0;
                    double adjustment = targetOffset - tickRemainder;
                    
                    // Normalizar la diferencia al rango [-25, 25] para encontrar el tick más cercano
                    if (adjustment < -25.0) adjustment += 50.0;
                    if (adjustment > 25.0) adjustment -= 50.0;
                    
                    // Aplicar un ajuste suave (max +/- 2.5ms) para alinearlo sin destruir la consistencia local de CPS
                    adjustment = Math.Max(-2.5, Math.Min(2.5, adjustment));
                    
                    delayMs += adjustment;
                }

                delayMs = Math.Max(3.0, delayMs);
                long delayTicks = (long)(delayMs * Stopwatch.Frequency / 1000.0);
                nextClickTick += delayTicks;
                long currentTick = sw.ElapsedTicks;
                // Allow catching up from small delays to maintain average CPS.
                // Only hard-reset if we've fallen more than 2 full intervals behind.
                // This prevents CPS drops when aim assist causes brief CPU contention.
                if (nextClickTick < currentTick - delayTicks * 2)
                    nextClickTick = currentTick;

                // ── Ejecutar el click ──
                if (isButterfly)
                {
                    // Butterfly: doble-click tradicional (UP+DOWN rapido)
                    int microGap = _rng.Next(4, 13);
                    PerformClick(cursorShown, isRefilling, foregroundWnd);
                    ApplyMouseJitter();
                    Thread.Sleep(microGap);
                    PerformClick(cursorShown, isRefilling, foregroundWnd);
                    ApplyMouseJitter();
                    nextClickTick -= (long)(microGap * Stopwatch.Frequency / 1000.0);
                }
                else if (cursorShown || isRefilling)
                {
                    // Inventario / Refill: click tradicional rapido
                    PerformClick(cursorShown, isRefilling, foregroundWnd);
                    ApplyMouseJitter();
                }
                else
                {
                    // ── IN-GAME: Modo PostMessage (Aim Assist Compatible) ──
                    // Enviamos los clicks directamente a la ventana de Minecraft.
                    // Esto hace un bypass completo de la cola global de Windows.
                    // Resultado: XClient sigue leyendo el mouse FÍSICO del usuario
                    // (con GetAsyncKeyState) para el aim assist, mientras que Minecraft
                    // recibe los clicks rápidos del autoclicker sin enterarse de la diferencia.
                    
                    IntPtr clickLParam = IntPtr.Zero;
                    if (_isCheatbreaker)
                    {
                        Win32.SendLeftDown();
                        clickLParam = (IntPtr)1;
                    }
                    else
                    {
                        clickLParam = Win32.PostLeftDown(foregroundWnd);
                    }
                    
                    // Si el click no se envió (ej. está en la barra de título de la ventana), saltamos.
                    if (clickLParam != IntPtr.Zero)
                    {
                        ApplyMouseJitter();
    
                    // WTap / STap / ShiftTap (Velocity Simulation)
                    if (_cfg.WTapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                    {
                        if (_rng.NextDouble() < 0.45)
                        {
                            Win32.keybd_event(0x57, 0, Win32.KEYEVENTF_KEYUP, 0);
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Thread.Sleep(_rng.Next(10, 30));
                                Win32.keybd_event(0x57, 0, 0, 0);
                            });
                        }
                    }
                    if (_cfg.STapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                    {
                        if (_rng.NextDouble() < 0.45)
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Win32.keybd_event(0x53, 0, 0, 0);
                                Thread.Sleep(_rng.Next(10, 30));
                                Win32.keybd_event(0x53, 0, Win32.KEYEVENTF_KEYUP, 0);
                            });
                        }
                    }
                    if (_cfg.ShiftTapEnabled)
                    {
                        if (_rng.NextDouble() < 0.45)
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Win32.keybd_event(0x10, 0, 0, 0);
                                Thread.Sleep(_rng.Next(10, 30));
                                Win32.keybd_event(0x10, 0, Win32.KEYEVENTF_KEYUP, 0);
                            });
                        }
                    }
                    if (_cfg.MicroStrafing && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0) // Only if pressing W
                    {
                        if (_rng.NextDouble() < 0.35)
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                byte strafeKey = _rng.NextDouble() > 0.5 ? (byte)0x41 : (byte)0x44; // A or D
                                Win32.keybd_event(strafeKey, 0, 0, 0);
                                Thread.Sleep(_rng.Next(15, 40));
                                Win32.keybd_event(strafeKey, 0, Win32.KEYEVENTF_KEYUP, 0);
                            });
                        }
                    }

                    // Hold time uses the precise downMs calculated by the Pacer
                    int holdTime = Math.Max(1, (int)downMs);
                    if (pingMs > 0)
                        holdTime += (int)Math.Ceiling(pingMs * 0.5 * 0.05);
                    long holdTicks = (long)(holdTime * Stopwatch.Frequency / 1000.0);
                    long startTicks = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                    {
                        Thread.Sleep(0);
                    }

                        // Recalcular posición del cursor para el UP.
                        // Si XClient aim assist movió el cursor durante el hold,
                        // usar la posición vieja del DOWN causaba que Minecraft
                        // descartara el click (posición UP != posición DOWN).
                        if (_isCheatbreaker) Win32.SendLeftUp();
                        else Win32.PostLeftUpFresh(foregroundWnd, clickLParam);

                        // Double Click Chance
                        if (_cfg.DoubleClickChance > 0 && _rng.NextDouble() * 100.0 < _cfg.DoubleClickChance)
                        {
                            Thread.Sleep(_rng.Next(2, 6)); // 2-5ms gap
                            if (_isCheatbreaker)
                            {
                                Win32.SendLeftDown();
                                Thread.Sleep(_rng.Next(1, 3));
                                Win32.SendLeftUp();
                            }
                            else
                            {
                                IntPtr dcLParam = Win32.PostLeftDown(foregroundWnd);
                                Thread.Sleep(_rng.Next(1, 3));
                                Win32.PostLeftUpFresh(foregroundWnd, dcLParam);
                            }
                                }

                        // Auto-Blockhit Logic for Aim-Assist Mode
                        if (_cfg.AutoBlockHit && !blockHitSuppressed && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                        {
                            if (_rng.NextDouble() < 0.60)
                            {
                                ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    Thread.Sleep(_rng.Next(5, 20));
                                    if (_isCheatbreaker) Win32.SendRightDown();
                                    else Win32.PostRightDown(foregroundWnd);
                                    
                                    Thread.Sleep(_rng.Next(15, 35));
                                    
                                    if (_isCheatbreaker) Win32.SendRightUp();
                                    else Win32.PostRightUpFresh(foregroundWnd, IntPtr.Zero);
                                });
                            }
                        }
                    }
                }

                // Espera de precision hasta el proximo tick
                while (sw.ElapsedTicks < nextClickTick)
                {
                    if (!Clicking || !_running) break;
                    long left = nextClickTick - sw.ElapsedTicks;
                    double leftMs = (double)left / Stopwatch.Frequency * 1000.0;
                    
                    // Sleep(1) para la mayor parte de la espera, SpinWait(1) para precisión final.
                    // SpinWait(1) en vez de SpinWait(20) para reducir contención de CPU
                    // con el aim assist - evita que los CPS caigan de 20 a 15.
                    if (leftMs > 2.0)       
                        Thread.Sleep(1);
                    else                    
                        Thread.Sleep(0);
                }

                // Update Live Stats
                long nowTicks = sw.ElapsedTicks;
                double actualElapsedMs = (nowTicks - _lastClickFinishTick) * 1000.0 / Stopwatch.Frequency;
                if (_lastClickFinishTick > 0 && actualElapsedMs > 0)
                {
                    StatInterval = delayMs;
                    StatLast = actualElapsedMs;
                    StatLiveCps = 1000.0 / actualElapsedMs;
                    StatJitter = Math.Abs(actualElapsedMs - delayMs);
                    
                    if (actualElapsedMs > delayMs + 1.5)
                    {
                        StatLate++;
                        double lateAmt = actualElapsedMs - delayMs;
                        if (lateAmt > StatWorstLate) StatWorstLate = lateAmt;
                    }
                    StatSamples++;
                    StatAvgCps = (StatAvgCps * (StatSamples - 1) + StatLiveCps) / StatSamples;
                }
                _lastClickFinishTick = nowTicks;
            }

            if (globalHoldActive) Win32.SendLeftUpNative();
        }

        private void RightClickLoop()
        {
            var sw = new Stopwatch();
            sw.Start();
            long nextClickTick = sw.ElapsedTicks;
            bool globalHoldActive = false;

            while (_running)
            {
                if (!RightClicking)
                {
                    if (globalHoldActive) { Win32.SendRightUp(); globalHoldActive = false; }
                    Thread.Sleep(15);
                    nextClickTick = sw.ElapsedTicks; // reset scheduler
                    continue;
                }

                // ── Focus check FIRST (before any global input) ──
                IntPtr foregroundWnd = Win32.GetForegroundWindow();
                if (!CachedIsMinecraftFocused(foregroundWnd))
                {
                    if (globalHoldActive) { Win32.SendRightUp(); globalHoldActive = false; }
                    Thread.Sleep(10);
                    nextClickTick = sw.ElapsedTicks; // prevent CPS spike on refocus
                    continue;
                }

                bool shouldClick;
                if (_cfg.RightMode == 0) // Hold
                    shouldClick = Win32.IsRightDown;
                else
                    shouldClick = true;

                if (!shouldClick)
                {
                    if (globalHoldActive) { Win32.SendRightUp(); globalHoldActive = false; }
                    Thread.Sleep(1);
                    nextClickTick = sw.ElapsedTicks;
                    continue;
                }
                else if (_cfg.RightMode != 0) // Toggle o Always
                {
                    // Solo enviamos el DOWN si Minecraft está en foco (chequeado arriba).
                    if (!globalHoldActive)
                    {
                        Win32.SendRightDown();
                        globalHoldActive = true;
                    }
                }

                bool cursorShown = CachedIsCursorVisible();
                if (cursorShown && !_cfg.WorkInMenus)
                {
                    if (!CachedIsInventoryLikeScreen(foregroundWnd))
                    {
                        if (globalHoldActive) { Win32.SendRightUp(); globalHoldActive = false; }
                        Thread.Sleep(10);
                        continue;
                    }
                }

                double targetCps = _cfg.RightAverageCps;
                double delayMs;
                bool isButterfly = false;

                if (_cfg.RightRandMode == 2) // NoDelay
                {
                    double fl = Math.Floor(targetCps);
                    double roll = _rng.NextDouble();
                    double actualCps;
                    
                    if      (roll < 0.80) actualCps = fl;
                    else if (roll < 0.95) actualCps = fl + 1.0;
                    else                  actualCps = Math.Max(1.0, fl - 1.0);
                    
                    delayMs = 1000.0 / actualCps;
                }
                else if (_cfg.RightRandMode == 1) // Butterfly
                {
                    double cpsMin = _cfg.RightMinCps;
                    double cpsMax = _cfg.RightMaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    double butterflyCps = NextTriangular(cpsMin, cpsMax);
                    delayMs = 2000.0 / butterflyCps;
                    delayMs += (NextGaussian() * 1.5);
                    isButterfly = true;
                }
                else // Jitter
                {
                    // Jitter implementation
                    double cpsMin = _cfg.RightMinCps;
                    double cpsMax = _cfg.RightMaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    double rawCps = NextTriangular(cpsMin, cpsMax);
                    
                    // Temporary local EMA since we don't have _stableCps for right click right now
                    // We can just use the stabilized rawCps directly.
                    delayMs = 1000.0 / rawCps;
                    delayMs += (_rng.NextDouble() * 0.5 - 0.25);
                }

                delayMs = Math.Max(3.0, delayMs);
                long delayTicks = (long)(delayMs * Stopwatch.Frequency / 1000.0);
                nextClickTick += delayTicks;
                long currentTick = sw.ElapsedTicks;
                if (nextClickTick < currentTick - delayTicks * 2)
                    nextClickTick = currentTick;

                if (isButterfly)
                {
                    int microGap = _rng.Next(4, 13);
                    PerformRightClick(cursorShown, foregroundWnd);
                    Thread.Sleep(microGap);
                    PerformRightClick(cursorShown, foregroundWnd);
                    nextClickTick -= (long)(microGap * Stopwatch.Frequency / 1000.0);
                }
                else if (cursorShown)
                {
                    PerformRightClick(cursorShown, foregroundWnd);
                }
                else
                {
                    IntPtr clickLParam = IntPtr.Zero;
                    if (_isCheatbreaker)
                    {
                        Win32.SendRightDown();
                        clickLParam = (IntPtr)1;
                    }
                    else
                    {
                        clickLParam = Win32.PostRightDown(foregroundWnd);
                    }
                    
                    if (clickLParam != IntPtr.Zero)
                    {
                        int holdTime = _rng.Next(1, 3);
                        long holdTicks = (long)(holdTime * Stopwatch.Frequency / 1000.0);
                        long startTicks = Stopwatch.GetTimestamp();
                        while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                        {
                            Thread.Sleep(0);
                        }
                        if (_isCheatbreaker) Win32.SendRightUp();
                        else Win32.PostRightUpFresh(foregroundWnd, clickLParam);
                    }
                }

                while (sw.ElapsedTicks < nextClickTick)
                {
                    if (!RightClicking || !_running) break;
                    long left = nextClickTick - sw.ElapsedTicks;
                    double leftMs = (double)left / Stopwatch.Frequency * 1000.0;
                    if (leftMs > 2.0) Thread.Sleep(1);
                    else Thread.Sleep(0);
                }
            }

            if (globalHoldActive) Win32.SendRightUp();
        }

        private void PerformRightClick(bool cursorShown, IntPtr foregroundWnd)
        {
            IntPtr lParam = IntPtr.Zero;
            if (_isCheatbreaker) { Win32.SendRightDown(); lParam = (IntPtr)1; }
            else { lParam = Win32.PostRightDown(foregroundWnd); }

            if (lParam == IntPtr.Zero) return;

            long holdTicks = (long)(1.0 * Stopwatch.Frequency / 1000.0);
            long startTicks = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - startTicks < holdTicks) { Thread.Sleep(0); }
            if (_isCheatbreaker) Win32.SendRightUp();
            else Win32.PostRightUp(foregroundWnd, lParam);
        }

        private void PerformClick(bool inInventory, bool refillMode, IntPtr foregroundWnd)
        {
            if (refillMode && inInventory)
            {
                IntPtr lP = IntPtr.Zero;
                if (_isCheatbreaker) { Win32.SendLeftDown(); lP = (IntPtr)1; }
                else { lP = Win32.PostLeftDown(foregroundWnd); }

                if (lP != IntPtr.Zero)
                {
                    long refHoldTicks = (long)(_rng.Next(2, 5) * Stopwatch.Frequency / 1000.0);
                    long refStart = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetTimestamp() - refStart < refHoldTicks) { Thread.Sleep(0); }
                    if (_isCheatbreaker) Win32.SendLeftUp();
                    else Win32.PostLeftUp(foregroundWnd, lP);
                }
                return;
            }

            IntPtr lParam = IntPtr.Zero;
            bool blockHitSuppressed = _cfg.RmbLock && Win32.IsRightDown;
            if (_isCheatbreaker) { Win32.SendLeftDown(); lParam = (IntPtr)1; }
            else { lParam = Win32.PostLeftDown(foregroundWnd); }

            if (lParam == IntPtr.Zero) return;

            if (!inInventory)
            {
                int holdTime = _rng.Next(1, 3);
                if (_cfg.PingMs > 0)
                    holdTime += (int)Math.Ceiling(_cfg.PingMs * 0.5 * 0.05);
                long holdTicks = (long)(holdTime * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                {
                    Thread.Sleep(0);
                }
                if (_isCheatbreaker) Win32.SendLeftUp();
                else Win32.PostLeftUp(foregroundWnd, lParam);

            // WTap / STap / ShiftTap (Velocity Simulation)
                if (_cfg.WTapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.45)
                    {
                        Win32.keybd_event(0x57, 0, Win32.KEYEVENTF_KEYUP, 0);
                        ThreadPool.QueueUserWorkItem(_ => {
                            Thread.Sleep(_rng.Next(10, 30));
                            Win32.keybd_event(0x57, 0, 0, 0);
                        });
                    }
                }
                if (_cfg.STapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.45)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Win32.keybd_event(0x53, 0, 0, 0);
                            Thread.Sleep(_rng.Next(10, 30));
                            Win32.keybd_event(0x53, 0, Win32.KEYEVENTF_KEYUP, 0);
                        });
                    }
                }
                if (_cfg.ShiftTapEnabled)
                {
                    if (_rng.NextDouble() < 0.45)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Win32.keybd_event(0x10, 0, 0, 0);
                            Thread.Sleep(_rng.Next(10, 30));
                            Win32.keybd_event(0x10, 0, Win32.KEYEVENTF_KEYUP, 0);
                        });
                    }
                }
                if (_cfg.MicroStrafing && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.35)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            byte strafeKey = _rng.NextDouble() > 0.5 ? (byte)0x41 : (byte)0x44; // A or D
                            Win32.keybd_event(strafeKey, 0, 0, 0);
                            Thread.Sleep(_rng.Next(15, 40));
                            Win32.keybd_event(strafeKey, 0, Win32.KEYEVENTF_KEYUP, 0);
                        });
                    }
                }
                
                // Auto-Blockhit (After left click up, micro tap right click to block)
                if (_cfg.AutoBlockHit && !blockHitSuppressed && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0) // Usually want to block hit when chasing (W down)
                {
                    if (_rng.NextDouble() < 0.60) // 60% chance to blockhit per click
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Thread.Sleep(_rng.Next(5, 20)); // tiny gap between hit and block
                            if (_isCheatbreaker) Win32.SendRightDown();
                            else Win32.PostRightDown(foregroundWnd);
                            
                            Thread.Sleep(_rng.Next(15, 35)); // hold block for extremely short time
                            
                            if (_isCheatbreaker) Win32.SendRightUp();
                            else Win32.PostRightUpFresh(foregroundWnd, IntPtr.Zero);
                        });
                    }
                }
            }
            else
            {
                long holdTicks = (long)(1.0 * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks) { Thread.Sleep(0); }
                if (_isCheatbreaker) Win32.SendLeftUp();
                else Win32.PostLeftUp(foregroundWnd, lParam);
            }
        }


        // The user explicitly requested to force this directly in the clicker, so it always uses mouse_event
        // This ensures the aim assists (both internal and external) work flawlessly.
        private bool _isCheatbreaker => true;

        private bool CachedIsMinecraftFocused(IntPtr hwnd)
        {
            // Verificación instantánea y no en caché: si el mouse sale de la ventana, detenerse.
            System.Drawing.Point p;
            if (!Win32.IsCursorInClientArea(hwnd, out p)) return false;

            // If same window and cache is fresh, return cached result
            if (hwnd == _lastFocusHwnd && _focusCheckTimer.IsRunning 
                && _focusCheckTimer.ElapsedMilliseconds < FOCUS_CHECK_INTERVAL_MS)
            {
                return _lastFocusResult;
            }
            
            _lastFocusResult = IsMinecraftFocused(hwnd);
            _lastFocusHwnd = hwnd;
            _focusCheckTimer.Restart();
            return _lastFocusResult;
        }

        private bool IsMinecraftFocused(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            
            // Safety check: Never click on Desktop or Taskbar
            _titleBuffer.Clear();
            Win32.GetClassName(hwnd, _titleBuffer, 256);
            string className = _titleBuffer.ToString();
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd")
                return false;

            // Reuse pre-allocated StringBuilder to avoid GC pressure
            _titleBuffer.Clear();
            Win32.GetWindowText(hwnd, _titleBuffer, 256);
            string title = _titleBuffer.ToString().ToLower();

            // Strict block list: Never click on our own app or common system windows
            if (title.Contains("Horimiya") || title == "program manager" || title == "")
                return false;

            return title.Contains("minecraft") ||
                   title.Contains("lunar")     ||
                   title.Contains("badlion")   ||
                   title.Contains("labymod")   ||
                   title.Contains("feather")   ||
                   title.Contains("pvplounge") ||
                   title.Contains("az launcher") ||
                   title.Contains("salwyrr")   ||
                   title.Contains("joacodemon") ||
                   title.Contains("cheatbreaker");
        }

        // ── Cached cursor visibility: reduces P/Invoke calls ──
        private bool CachedIsCursorVisible()
        {
            if (_cursorCheckTimer.IsRunning 
                && _cursorCheckTimer.ElapsedMilliseconds < CURSOR_CHECK_INTERVAL_MS)
            {
                return _lastCursorVisible;
            }
            
            _lastCursorVisible = IsCursorVisible();
            _cursorCheckTimer.Restart();
            return _lastCursorVisible;
        }

        private bool IsCursorVisible()
        {
            var ci = new Win32.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(ci);
            if (Win32.GetCursorInfo(ref ci))
            {
                // flags == 0 means cursor is hidden (Minecraft hides cursor in-game)
                // flags != 0 means cursor is visible (menu, inventory, etc.)
                return ci.flags != 0;
            }
            return false;
        }

        private bool CachedIsInventoryLikeScreen(IntPtr hwnd)
        {
            // Increased cache duration: pixel sampling via GDI is very expensive
            // and causes frame drops in Minecraft. 500ms is still responsive enough
            // for inventory detection.
            if (!_invCheckTimer.IsRunning || _invCheckTimer.ElapsedMilliseconds > 500)
            {
                _lastInvResult = IsInventoryLikeScreen(hwnd);
                _invCheckTimer.Restart();
            }
            return _lastInvResult;
        }

        private bool IsInventoryLikeScreen(IntPtr hwnd)
        {
            // Simple heuristic: if cursor is near center of window,
            // it's likely an inventory/chest screen.
            // This is a basic detection - works for most MC versions.
            if (hwnd == IntPtr.Zero) return true; // If we can't check, allow clicking

            try
            {
                Win32.RECT rect;
                if (!Win32.GetClientRect(hwnd, out rect)) return true;

                int w = rect.right - rect.left;
                int h = rect.bottom - rect.top;
                if (w < 100 || h < 100) return true;

                IntPtr hdc = Win32.GetDC(hwnd);
                if (hdc == IntPtr.Zero) return true;

                try
                {
                    int cx = w / 2;
                    int cy = h / 2;

                    // Sample fewer points to reduce GDI overhead (3 instead of 5)
                    // These 3 points still reliably detect inventory screens
                    Point[] checkPoints = new Point[] {
                        new Point(cx - 60, cy - 60),
                        new Point(cx + 60, cy + 60),
                        new Point(cx, cy - 90)
                    };

                    int matchCount = 0;
                    foreach (var p in checkPoints)
                    {
                        uint pixel = Win32.GetPixel(hdc, p.X, p.Y);
                        if (pixel == 0xFFFFFFFF) continue; // Invalid pixel
                        byte r = (byte)(pixel & 0xFF);
                        byte g = (byte)((pixel >> 8) & 0xFF);
                        byte b = (byte)((pixel >> 16) & 0xFF);

                        // Standard Minecraft GUI Gray (198, 198, 198)
                        if (r >= 190 && r <= 210 && g >= 190 && g <= 210 && b >= 190 && b <= 210)
                        {
                            matchCount++;
                        }
                        // Dark theme inventory containers
                        else if (r >= 15 && r <= 50 && g >= 15 && g <= 50 && b >= 15 && b <= 50
                                 && Math.Abs(r - g) < 8 && Math.Abs(g - b) < 8)
                        {
                            matchCount++;
                        }
                    }

                    return matchCount >= 1; // Reduced threshold since we sample fewer points
                }
                finally
                {
                    Win32.ReleaseDC(hwnd, hdc);
                }
            }
            catch
            {
                return true; // If detection fails, allow clicking
            }
        }

        // ── Gaussian RNG (Box-Muller) for Jitter mode ─────────────────────
        private bool _hasSpare = false;
        private double _spare;
        private double NextGaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }
            double u, v, s;
            do
            {
                u = _rng.NextDouble() * 2.0 - 1.0;
                v = _rng.NextDouble() * 2.0 - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);
            s = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _spare = v * s;
            _hasSpare = true;
            return u * s;
        }

    }
}
