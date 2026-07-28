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
        // Dedicated RNG for RightClickLoop — System.Random is NOT thread-safe.
        // Using a single _rng from two threads simultaneously corrupts its state.
        private readonly Random _rightRng = new Random(Environment.TickCount ^ 0x5A5A5A5A);
        private bool _lastInvResult = false;
        private Stopwatch _invCheckTimer = new Stopwatch();
        private Thread _thread;
        private volatile bool _running = false;

        // Refill state tracking
        private const double REFILL_CPS_MIN = 25.0;
        private const double REFILL_CPS_MAX = 38.0;

        private double NextUniform(double minV, double maxV) { return minV + _rng.NextDouble() * (maxV - minV); }


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

        // Process name cache: avoids expensive Process.GetProcessById on every focus check
        private readonly System.Collections.Generic.Dictionary<uint, string> _processNameCache
            = new System.Collections.Generic.Dictionary<uint, string>();

        // Cache LoadCursor handles — these are system constants, never change.
        // LoadCursor is a P/Invoke; calling it 3x per cursor check (every 50ms) is wasteful.
        private static readonly IntPtr _hCursorArrow = Win32.LoadCursor(IntPtr.Zero, 32512); // IDC_ARROW
        private static readonly IntPtr _hCursorIBeam = Win32.LoadCursor(IntPtr.Zero, 32513); // IDC_IBEAM
        private static readonly IntPtr _hCursorHand  = Win32.LoadCursor(IntPtr.Zero, 32649); // IDC_HAND

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

        // Right clicker thread
        private Thread _rightThread;

        public Clicker(AppConfig cfg)
        {
            _cfg = cfg;
        }

        // ── Pacer State ──
        private long m_counter = 0;
        private double m_lastIntervalMs = 0.0;
        private double m_lastDownMs = 0.0;

        // Call this when switching profiles so timing state doesn't bleed across presets.
        public void ResetTimingState()
        {
            m_counter        = 0;
            m_lastIntervalMs = 0.0;
            m_lastDownMs     = 0.0;
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

            // ── Aim-Assist compatible state ────────────────────────────────────
            bool globalHoldActive = false; // Mantiene el LMB "presionado" globalmente para Toggle/Always


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

                // Si Clicking esta OFF, dormir
                if (!Clicking)
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(15);
                    _focusCheckTimer.Reset();
                    _cursorCheckTimer.Reset();
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

                // Reset scheduler on re-entry to prevent CPS burst from accumulated ticks.
                if (!_wasActiveLastTick)
                {
                    nextClickTick = sw.ElapsedTicks;
                    _wasActiveLastTick = true;
                }

                // ── Menu / Inventory restriction (Smart-Pause) ──
                // CachedIsCursorVisible() already calls GetCursorInfo internally (cached 50ms).
                bool cursorShown = CachedIsCursorVisible();
                if (cursorShown && !_cfg.WorkInMenus)
                {
                    // Pause in all menus/chat/escape screens when WorkInMenus is off.
                    // cursorShown is only true when a standard Windows cursor (Arrow/IBeam/Hand) is detected,
                    // which reliably identifies menu, chat, and escape screens.
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(10);
                    continue;
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
                            // BUG FIX: Shift DOWN is sent here; MUST be released after the click
                            // Previously Shift was never released, causing it to get stuck
                            Win32.keybd_event(0x10, 0, 0, 0); // Shift DOWN
                            Thread.Sleep(1);
                        }
                    }
                }

                bool isButterfly = false;
                double delayMs;
                double downMs = 2.0;
                // Read ping once here — used consistently throughout this tick
                double pingMs = _cfg.PingMs;

                if (isRefilling)
                {
                    // Fast refill: randomized between REFILL_CPS_MIN and REFILL_CPS_MAX
                    double refillCps = REFILL_CPS_MIN + _rng.NextDouble() * (REFILL_CPS_MAX - REFILL_CPS_MIN);
                    delayMs = 1000.0 / refillCps;
                }
                else
                {
                    double cpsMin = _cfg.MinCps;
                    double cpsMax = _cfg.MaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    // ── Burst Mode state update ──
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
                            nextBurstTime = burstTimer.ElapsedMilliseconds + _cfg.BurstIntervalMin * 1000.0
                                + _rng.NextDouble() * Math.Max(0, (_cfg.BurstIntervalMax - _cfg.BurstIntervalMin)) * 1000.0;
                        }
                    }
                    else if (_cfg.BurstEnabled && !isPhysicalDown)
                    {
                        burstTimer.Restart();
                        inBurst = false;
                        nextBurstTime = _cfg.BurstIntervalMin * 1000.0
                            + _rng.NextDouble() * Math.Max(0, (_cfg.BurstIntervalMax - _cfg.BurstIntervalMin)) * 1000.0;
                    }

                    if (_cfg.BurstEnabled && inBurst)
                    {
                        cpsMin = Math.Min(30.0, cpsMin + 6.0);
                        cpsMax = Math.Min(30.0, cpsMax + 6.0);
                    }


                    double cps = NextUniform(cpsMin, cpsMax);
                    double interval = 1000.0 / cps;

                    if (_cfg.RandMode == 0) // Jitter — legit, human feel
                    {
                        // Wider gaussian noise (±4ms) — more natural rhythm variation than Blatant/Butterfly.
                        double u1 = Math.Max(1e-10, 1.0 - _rng.NextDouble());
                        double u2 = Math.Max(1e-10, 1.0 - _rng.NextDouble());
                        double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                        gauss = Math.Max(-4.0, Math.Min(4.0, gauss * 2.0));

                        interval += gauss;

                        // Natural micro-hesitations: ~6% of clicks drop to 9–13 CPS
                        // (77–111ms interval) — simulates losing the rhythm, the biggest legit signal.
                        if (_rng.NextDouble() < 0.06)
                        {
                            double hesitationCps = 9.0 + _rng.NextDouble() * 4.0; // 9–13 CPS
                            interval = 1000.0 / hesitationCps;
                        }

                        if (interval < 45.0) interval = 45.0 + (_rng.NextDouble() * 1.5);

                        // Down time: 30% with ±3ms variation
                        downMs = Math.Max(4.0, interval * 0.30 + (_rng.NextDouble() - 0.5) * 6.0);
                        if (downMs > interval - 0.5) downMs = interval - 0.5;

                        m_lastIntervalMs = interval;
                        m_lastDownMs = downMs;
                        m_counter++;
                        delayMs = interval;
                        goto skipCommonPacing;
                    }
                    else if (_cfg.RandMode == 2) // NoDelay (MMC Safe)
                    {
                        // NoDelay bypasses the EMA entirely — the whole point is to be
                        // extremely fast and consistent, like a hardware mouse switch.
                        // Going through the EMA erases any difference from other modes.
                        double u1 = Math.Max(1e-10, 1.0 - _rng.NextDouble());
                        double u2 = Math.Max(1e-10, 1.0 - _rng.NextDouble());
                        double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                        // ±0.8ms of gaussian jitter — tight and fast, barely perceptible
                        gauss = Math.Max(-0.8, Math.Min(0.8, gauss * 0.5));
                        interval = (1000.0 / cps) + gauss;
                        interval = Math.Max(3.0, interval);

                        // Very short down time — simulates fast physical switch release (~1-2ms)
                        downMs = 1.5 + _rng.NextDouble() * 1.0;

                        m_lastIntervalMs = interval;
                        m_lastDownMs = downMs;
                        m_counter++;
                        delayMs = interval;
                        goto skipCommonPacing;
                    }
                    else if (_cfg.RandMode == 1) // Butterfly — tighter than Jitter, looser than Blatant
                    {
                        // Single-click mode with consistent timing — sits between Jitter and Blatant.
                        // ±1ms gaussian noise: predictable enough to feel mechanical, loose enough to avoid flags.
                        double u1 = Math.Max(1e-10, 1.0 - _rng.NextDouble());
                        double u2 = Math.Max(1e-10, 1.0 - _rng.NextDouble());
                        double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                        gauss = Math.Max(-1.0, Math.Min(1.0, gauss * 0.7));

                        interval += gauss;
                        if (interval < 45.0) interval = 45.0 + (_rng.NextDouble() * 0.8);

                        // Consistent down time: 28% ±1ms
                        downMs = Math.Max(4.0, interval * 0.28 + (_rng.NextDouble() - 0.5) * 2.0);
                        if (downMs > interval - 0.5) downMs = interval - 0.5;

                        m_lastIntervalMs = interval;
                        m_lastDownMs = downMs;
                        m_counter++;
                        delayMs = interval;
                        goto skipCommonPacing;
                    }
                    else if (_cfg.RandMode == 3) // Blatant — fully mechanical, max CPS, minimal noise
                    {
                        // Always runs at cpsMax — no range variation, maximum aggressiveness.
                        // ±0.1ms noise only — just enough to not be a perfect square wave.
                        interval = 1000.0 / cpsMax;
                        interval += (_rng.NextDouble() * 0.2 - 0.1);
                        interval = Math.Max(3.0, interval);

                        // Fixed down time: always 28%, no noise — robotic by design
                        downMs = interval * 0.28;

                        m_lastIntervalMs = interval;
                        m_lastDownMs = downMs;
                        m_counter++;
                        delayMs = interval;
                        goto skipCommonPacing;
                    }
                    else
                    {
                        // Fallback for any unknown RandMode — behaves like Jitter
                        delayMs = interval;
                        downMs = Math.Max(4.0, interval * 0.30);
                    }
                }
                skipCommonPacing:

                // ── Ping-Aware Timing / Latency Compensation ──
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
                if (cursorShown || isRefilling)
                {
                    // Inventory / Refill click
                    PerformClick(cursorShown, isRefilling, foregroundWnd);
                    ApplyMouseJitter();
                    // BUG FIX: Always release Shift after a refill click tick
                    if (isRefilling) Win32.keybd_event(0x10, 0, Win32.KEYEVENTF_KEYUP, 0);
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
    
                    // WTap / STap / ShiftTap / MicroStrafing (Velocity Simulation)
                    // IMPORTANT: _rng is NOT thread-safe. ALL random values MUST be pre-captured
                    // on this thread before passing to the ThreadPool lambda. Calling _rng inside
                    // the lambda from a pool thread while ClickLoop uses _rng simultaneously
                    // corrupts Random's internal state (returns 0.0 forever → CPS collapses).
                    if (_cfg.WTapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                    {
                        if (_rng.NextDouble() < 0.45)
                        {
                            int wtapSleep = _rng.Next(10, 30); // pre-capture!
                            Win32.keybd_event(0x57, 0, Win32.KEYEVENTF_KEYUP, 0);
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Thread.Sleep(wtapSleep);
                                Win32.keybd_event(0x57, 0, 0, 0);
                            });
                        }
                    }
                    if (_cfg.STapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                    {
                        if (_rng.NextDouble() < 0.45)
                        {
                            int stapSleep = _rng.Next(10, 30); // pre-capture!
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Win32.keybd_event(0x53, 0, 0, 0);
                                Thread.Sleep(stapSleep);
                                Win32.keybd_event(0x53, 0, Win32.KEYEVENTF_KEYUP, 0);
                            });
                        }
                    }
                    if (_cfg.ShiftTapEnabled)
                    {
                        if (_rng.NextDouble() < 0.45)
                        {
                            int shiftSleep = _rng.Next(10, 30); // pre-capture!
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Win32.keybd_event(0x10, 0, 0, 0);
                                Thread.Sleep(shiftSleep);
                                Win32.keybd_event(0x10, 0, Win32.KEYEVENTF_KEYUP, 0);
                            });
                        }
                    }
                    if (_cfg.MicroStrafing && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                    {
                        if (_rng.NextDouble() < 0.35)
                        {
                            // Pre-capture ALL random values before the lambda!
                            byte strafeKey = _rng.NextDouble() > 0.5 ? (byte)0x41 : (byte)0x44;
                            int strafeSleep = _rng.Next(15, 40);
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                Win32.keybd_event(strafeKey, 0, 0, 0);
                                Thread.Sleep(strafeSleep);
                                Win32.keybd_event(strafeKey, 0, Win32.KEYEVENTF_KEYUP, 0);
                            });
                        }
                    }

                    // Hold time: hybrid Sleep(1)+SpinWait — reduces CPU burn vs pure SpinWait.
                    // At 17 CPS with ~17ms hold, pure SpinWait burned ~29% of one core.
                    // Now: Sleep(1) covers most of the hold, SpinWait only for the last 2ms.
                    long holdTicks = (long)(Math.Max(1.0, downMs) * Stopwatch.Frequency / 1000.0);
                    long startTicks = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                    {
                        long holdLeft = holdTicks - (Stopwatch.GetTimestamp() - startTicks);
                        double holdLeftMs = (double)holdLeft / Stopwatch.Frequency * 1000.0;
                        if (holdLeftMs > 2.0) Thread.Sleep(1);
                        else Thread.SpinWait(10);
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
                            // A physical double click bounce happens extremely fast, but the hardware switch
                            // still requires a realistic "down" duration to be read by the anticheat.
                            Thread.Sleep(_rng.Next(1, 4)); // micro-gap between first UP and second DOWN
                            
                            int bounceHoldMs = _rng.Next(6, 14); // Realistic bounce hold time
                            long bounceTicks = (long)(bounceHoldMs * Stopwatch.Frequency / 1000.0);
                            
                            if (_isCheatbreaker)
                            {
                                Win32.SendLeftDown();
                                long bStart = Stopwatch.GetTimestamp();
                                while (Stopwatch.GetTimestamp() - bStart < bounceTicks) { Thread.SpinWait(10); }
                                Win32.SendLeftUp();
                            }
                            else
                            {
                                IntPtr dcLParam = Win32.PostLeftDown(foregroundWnd);
                                long bStart = Stopwatch.GetTimestamp();
                                while (Stopwatch.GetTimestamp() - bStart < bounceTicks) { Thread.SpinWait(10); }
                                Win32.PostLeftUpFresh(foregroundWnd, dcLParam);
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
                    
                    // Sleep(1) para la mayor parte de la espera, SpinWait activo para los últimos 2ms.
                    // Esto garantiza un delay microscópicamente perfecto independiente de CPU lag
                    if (leftMs > 2.0)       
                        Thread.Sleep(1);
                    else                    
                        Thread.SpinWait(10); // Busy wait for the exact microsecond
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
                    // Cap StatSamples to a rolling window of 200 to prevent the average from
                    // becoming completely stale and the division from losing precision over time.
                    if (StatSamples > 200) StatSamples = 200;
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
                    else if (!IsItemUnderCursor(foregroundWnd))
                    {
                        if (globalHoldActive) { Win32.SendRightUp(); globalHoldActive = false; }
                        Thread.Sleep(5);
                        continue;
                    }
                }

                double targetCps = _cfg.RightAverageCps;
                double delayMs;
                bool isButterfly = false;

                if (_cfg.RightRandMode == 2) // NoDelay
                {
                    // Gaussian-centered around the exact target interval (±~1.5ms)
                    // Avoids the staircase/floor pattern that is trivially detectable
                    double baseInterval = 1000.0 / targetCps;
                    double u1 = 1.0 - _rng.NextDouble();
                    double u2 = 1.0 - _rng.NextDouble();
                    double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

                    // Slow breathing sine for organic shape
                    double breath = Math.Sin(Stopwatch.GetTimestamp() * 0.000000012) * 1.1;

                    delayMs = baseInterval + (gauss * 1.4) + breath;

                    // Rare micro-stutter (~4%)
                    if (_rng.NextDouble() < 0.04) delayMs += _rng.NextDouble() * 4.5;
                }
                else if (_cfg.RightRandMode == 1) // Butterfly
                {
                    double cpsMin = _cfg.RightMinCps;
                    double cpsMax = _cfg.RightMaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    double butterflyCps = NextUniform(cpsMin, cpsMax);
                    delayMs = 2000.0 / butterflyCps;
                    delayMs += (NextGaussian() * 1.5);
                    isButterfly = true;
                }
                else if (_cfg.RightRandMode == 3) // Blatant
                {
                    double cpsMin = _cfg.RightMinCps;
                    double cpsMax = _cfg.RightMaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    double rawCps = NextUniform(cpsMin, cpsMax);
                    delayMs = 1000.0 / rawCps;
                    
                    // Ultra consistent timing for blatant mode
                    delayMs += (_rng.NextDouble() * 0.1 - 0.05);
                }
                else if (_cfg.RightRandMode == 4) // Godbridge
                {
                    // Perfectly timed for placing blocks quickly and consistently without shifting
                    // Usually around 19-21 CPS with very specific short hold times
                    delayMs = 48.0 + (_rng.NextDouble() * 2.0);
                    isButterfly = false; // keep single exact clicks
                }
                else // Jitter
                {
                    // Jitter implementation
                    double cpsMin = _cfg.RightMinCps;
                    double cpsMax = _cfg.RightMaxCps;
                    if (cpsMin < 1.0) cpsMin = 1.0;
                    if (cpsMax < cpsMin) cpsMax = cpsMin;
                    if (cpsMax > 30.0) cpsMax = 30.0;
                    
                    double rawCps = NextUniform(cpsMin, cpsMax);
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
                    else Thread.SpinWait(10); // precision spin for last 2ms — avoids Sleep(0) overshoot
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

            // BUG FIX: was 1ms fixed — too short for some clients. Use 2-4ms randomized.
            int holdMs = _rng.Next(2, 5);
            long holdTicks = (long)(holdMs * Stopwatch.Frequency / 1000.0);
            long startTicks = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - startTicks < holdTicks) { Thread.Sleep(0); }
            if (_isCheatbreaker) Win32.SendRightUp();
            else Win32.PostRightUp(foregroundWnd, lParam);
        }

        private void PerformClick(bool inInventory, bool refillMode, IntPtr foregroundWnd)
        {
            if (refillMode && inInventory)
            {
                // Refill uses RIGHT CLICK (shift+right-click moves stack to other container)
                IntPtr rP = IntPtr.Zero;
                if (_isCheatbreaker) { Win32.SendRightDown(); rP = (IntPtr)1; }
                else { rP = Win32.PostRightDown(foregroundWnd); }

                if (rP != IntPtr.Zero)
                {
                    long refHoldTicks = (long)(_rng.Next(1, 3) * Stopwatch.Frequency / 1000.0);
                    long refStart = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetTimestamp() - refStart < refHoldTicks) { Thread.Sleep(0); }
                    if (_isCheatbreaker) Win32.SendRightUp();
                    else Win32.PostRightUpFresh(foregroundWnd, rP);
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

            // WTap / STap / ShiftTap / MicroStrafing (Velocity Simulation)
                // Pre-capture ALL random values — same thread-safety fix as ClickLoop.
                if (_cfg.WTapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.45)
                    {
                        int wtapSleep = _rng.Next(10, 30);
                        Win32.keybd_event(0x57, 0, Win32.KEYEVENTF_KEYUP, 0);
                        ThreadPool.QueueUserWorkItem(_ => {
                            Thread.Sleep(wtapSleep);
                            Win32.keybd_event(0x57, 0, 0, 0);
                        });
                    }
                }
                if (_cfg.STapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.45)
                    {
                        int stapSleep = _rng.Next(10, 30);
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Win32.keybd_event(0x53, 0, 0, 0);
                            Thread.Sleep(stapSleep);
                            Win32.keybd_event(0x53, 0, Win32.KEYEVENTF_KEYUP, 0);
                        });
                    }
                }
                if (_cfg.ShiftTapEnabled)
                {
                    if (_rng.NextDouble() < 0.45)
                    {
                        int shiftSleep = _rng.Next(10, 30);
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Win32.keybd_event(0x10, 0, 0, 0);
                            Thread.Sleep(shiftSleep);
                            Win32.keybd_event(0x10, 0, Win32.KEYEVENTF_KEYUP, 0);
                        });
                    }
                }
                if (_cfg.MicroStrafing && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.35)
                    {
                        byte strafeKey = _rng.NextDouble() > 0.5 ? (byte)0x41 : (byte)0x44;
                        int strafeSleep = _rng.Next(15, 40);
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Win32.keybd_event(strafeKey, 0, 0, 0);
                            Thread.Sleep(strafeSleep);
                            Win32.keybd_event(strafeKey, 0, Win32.KEYEVENTF_KEYUP, 0);
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


        // Automatically compatible with External Aim Assists (Slinky, Drip, XClient).
        // Uses PostMessage by default, which bypasses the Windows mouse queue and keeps the physical
        // VK_LBUTTON state pressed, allowing aim assists to track smoothly while clicking.
        private bool _isCheatbreaker => false;

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
            if (title.Contains("horimiya") || title == "program manager" || title == "")
                return false;

            uint processId;
            Win32.GetWindowThreadProcessId(hwnd, out processId);
            string processName = "";
            // Use cached process name to avoid expensive Process.GetProcessById every focus check
            if (!_processNameCache.TryGetValue(processId, out processName))
            {
                try {
                    using (var proc = System.Diagnostics.Process.GetProcessById((int)processId)) {
                        processName = proc.ProcessName.ToLower();
                    }
                } catch { processName = ""; }
                _processNameCache[processId] = processName;
            }

            // Restrict to known Minecraft processes (javaw, lunar, badlion, feather, etc.)
            if (!processName.Contains("javaw") && !processName.Contains("java") && 
                !processName.Contains("lunar") && !processName.Contains("badlion") && 
                !processName.Contains("cb") && !processName.Contains("cheatbreaker") && 
                !processName.Contains("feather")) {
                return false;
            }

            bool isMc = title.Contains("minecraft") ||
                   title.Contains("lunar")     ||
                   title.Contains("badlion")   ||
                   title.Contains("labymod")   ||
                   title.Contains("feather")   ||
                   title.Contains("pvplounge") ||
                   title.Contains("az launcher") ||
                   title.Contains("salwyrr")   ||
                   title.Contains("joacodemon") ||
                   title.Contains("cheatbreaker");
                   
            if (!isMc && _cfg != null && _cfg.Presets != null)
            {
                foreach (var preset in _cfg.Presets)
                {
                    if (string.IsNullOrWhiteSpace(preset.Server)) continue;
                    string[] servers = preset.Server.ToLower().Split(new string[] { " / " }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var server in servers)
                    {
                        if (title.Contains(server.Trim()))
                        {
                            isMc = true;
                            break;
                        }
                    }
                    if (isMc) break;
                }
            }
            return isMc;
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
                // In some clients, flags == 1 even when the cursor is supposedly hidden.
                // But when the inventory is actually open, the cursor becomes the standard Arrow or IBeam.
                // We check if the current cursor matches any standard Windows cursors.
                if (ci.flags == 0) return false;

                // Use pre-cached cursor handles — LoadCursor is a P/Invoke and these values are constant.
                if (ci.hCursor == _hCursorArrow || ci.hCursor == _hCursorIBeam || ci.hCursor == _hCursorHand)
                {
                    return true;
                }
                
                // If it's visible but not a standard cursor, assume it's a custom game cursor (which might just be the crosshair or hidden).
                // Returning false here allows clicking to proceed in-game even if the client didn't properly hide the cursor.
                return false;
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
                // If GDI check completely fails, default to true to allow clicking in game as fallback
                return true; 
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

        /// <summary>
        /// Samples a grid of pixels around the current cursor position and checks if there
        /// is an item present. Minecraft's empty slots have a uniform dark background (~55,55,55).
        /// Items have varied/colorful pixels with high color variance. Returns true if an item
        /// is likely present under the cursor.
        /// </summary>
        private bool IsItemUnderCursor(IntPtr hwnd)
        {
            try
            {
                System.Drawing.Point screenPt;
                Win32.GetCursorPos(out screenPt);

                IntPtr hdc = Win32.GetDC(IntPtr.Zero); // screen DC
                if (hdc == IntPtr.Zero) return false;

                try
                {
                    // Sample a 5x5 grid of pixels centered on cursor
                    int[] offsets = { -8, -4, 0, 4, 8 };
                    long sumR = 0, sumG = 0, sumB = 0;
                    int count = 0;
                    int[] reds = new int[25];
                    int[] greens = new int[25];
                    int[] blues = new int[25];

                    int idx = 0;
                    foreach (int dy in offsets)
                    {
                        foreach (int dx in offsets)
                        {
                            uint pixel = Win32.GetPixel(hdc, screenPt.X + dx, screenPt.Y + dy);
                            if (pixel == 0xFFFFFFFF) { idx++; continue; }
                            byte r = (byte)(pixel & 0xFF);
                            byte g = (byte)((pixel >> 8) & 0xFF);
                            byte b = (byte)((pixel >> 16) & 0xFF);
                            reds[idx] = r; greens[idx] = g; blues[idx] = b;
                            sumR += r; sumG += g; sumB += b;
                            count++;
                            idx++;
                        }
                    }

                    if (count < 5) return false;

                    double avgR = (double)sumR / count;
                    double avgG = (double)sumG / count;
                    double avgB = (double)sumB / count;

                    // BUG FIX: Loop only up to count (valid pixels), not idx (may include skipped invalid pixels)
                    double varSum = 0;
                    for (int i = 0; i < idx; i++)
                    {
                        if (reds[i] == 0 && greens[i] == 0 && blues[i] == 0) continue;
                        double dr = reds[i] - avgR;
                        double dg = greens[i] - avgG;
                        double db = blues[i] - avgB;
                        varSum += dr * dr + db * db + dg * dg;
                    }
                    double variance = count > 0 ? varSum / count : 0;

                    // Empty Minecraft slot background: dark uniform gray (~55,55,55)
                    // If average is dark and variance is very low -> empty slot, no item
                    bool isDarkBackground = avgR < 80 && avgG < 80 && avgB < 80;
                    bool isLowVariance = variance < 400; // Low variation = uniform color = empty slot

                    // If it's a dark uniform background -> no item
                    if (isDarkBackground && isLowVariance) return false;

                    // Bright or varied pixels -> item is present
                    return true;
                }
                finally
                {
                    Win32.ReleaseDC(IntPtr.Zero, hdc);
                }
            }
            catch
            {
                return false;
            }
        }

    }
}
