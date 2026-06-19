using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using lospoderosos_lite.Config;
using lospoderosos_lite.Utils;

// Performance optimization notes:
// - Cached focus/cursor checks to avoid per-tick P/Invoke overhead
// - Async sound to prevent blocking the click thread
// - timeBeginPeriod(1) for accurate Thread.Sleep timing
// - Reduced GDI pixel sampling frequency
// - Pre-allocated INPUT structs to minimize GC pressure

namespace lospoderosos_lite.Modules
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
        private SoundPlayer _soundPlayer;
        private string _cachedSoundPath;

        // Refill state tracking
        private int _refillClickCount = 0;
        private bool _wasBbActive = false;
        private const double REFILL_CPS_MIN = 25.0;
        private const double REFILL_CPS_MAX = 38.0;

        // Custom mode state: streak-based smoothing
        private double _customTargetCps = 15.0; // CPS picked from the weighted distribution
        private double _customCurrentCps = 15.0; // smoothed value drifting toward target
        private int    _customStreakLeft  = 0;    // clicks remaining at current target before re-rolling
private double _stableCps = double.NaN; // smoothed CPS for ultra‑stable calculation

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

        // Sound playback thread to avoid blocking the click thread
        private Thread _soundThread;
        private volatile bool _soundPending = false;
        private volatile bool _soundRunning = false;

        // Reusable StringBuilder for window title checks (avoids GC pressure)
        private readonly StringBuilder _titleBuffer = new StringBuilder(256);

        public volatile bool Clicking = false;

        // ── Live Stats ──
        public double StatLiveCps = 0;
        public double StatAvgCps = 0;
        public double StatInterval = 0;
        public double StatJitter = 0;
        public double StatLast = 0;
        public int StatLate = 0;
        public double StatWorstLate = 0;
        public int StatSamples = 0;
        private long _lastClickFinishTick = 0;
        public Clicker(AppConfig cfg)
        {
            _cfg = cfg;
        }

        // Returns the CPS to aim for this tick. If ForceExactCps is enabled, returns the exact configured AverageCps.
        private double GetCurrentCps()
        {
            if (_cfg.ForceExactCps)
                return _cfg.AverageCps;
            // Otherwise, the jitter/random logic will handle variation. For now we just return the configured average.
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

            // Start background sound thread
            _soundRunning = true;
            _soundThread = new Thread(SoundLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            _soundThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _soundRunning = false;
            Win32.timeEndPeriod(1);
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

            while (_running)
            {
                // Si Clicking esta OFF, dormir
                if (!Clicking)
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(15);
                    _focusCheckTimer.Reset();
                    _cursorCheckTimer.Reset();
                    _wasBbActive = false;
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
                    continue;
                }
                else if (_cfg.Mode != 0) // Toggle o Always
                {
                    // En Toggle/Always el usuario no está sosteniendo el click físico.
                    // XClient necesita ver el LMB presionado para activar el aim assist.
                    // Mandamos un DOWN global para "engañar" al aim assist.
                    if (!globalHoldActive)
                    {
                        Win32.SendLeftDownNative();
                        globalHoldActive = true;
                    }
                }

                // ── RMB-Lock ──
                if (_cfg.RmbLock && Win32.IsRightDown)
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(5);
                    continue;
                }

                // ── Focus check (cached) ──
                IntPtr foregroundWnd = Win32.GetForegroundWindow();
                if (_cfg.OnlyInGame && !CachedIsMinecraftFocused(foregroundWnd))
                {
                    if (globalHoldActive) { Win32.SendLeftUpNative(); globalHoldActive = false; }
                    Thread.Sleep(10);
                    continue;
                }

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

                // Refill check
                bool isRefilling = cursorShown && ((Win32.GetAsyncKeyState(0x10) & 0x8000) != 0);

                // ── Randomization Mode ──
                double targetCps = GetCurrentCps();
                bool isButterfly = false;
                double delayMs;

                if (isRefilling)
                {
                    delayMs = 1000.0 / 20.0;
                }
                else if (_cfg.RandMode == 3)
                {
                    double sumWeights = 0;
                    for (int i = 0; i < 25; i++) sumWeights += _cfg.CustomCpsWeights[i];
                    if (sumWeights > 0 && _customStreakLeft <= 0)
                    {
                        double roll = _rng.NextDouble() * sumWeights;
                        double acc  = 0;
                        for (int i = 0; i < 25; i++)
                        {
                            acc += _cfg.CustomCpsWeights[i];
                            if (roll <= acc) { _customTargetCps = i + 1; break; }
                        }
                        _customStreakLeft = _rng.Next(3, 9);
                    }
                    _customStreakLeft--;
                    _customCurrentCps += (_customTargetCps - _customCurrentCps) * 0.35;
                    double customNoise = NextGaussian() * 0.20;
                    double finalCustomCps = Math.Max(1.0, _customCurrentCps + customNoise);
                    delayMs = 1000.0 / finalCustomCps;
                    delayMs += (_rng.NextDouble() * 3.0 - 1.5);
                }
                else if (_cfg.RandMode == 2)
                {
                    double fl = Math.Floor(targetCps);
                    double roll = _rng.NextDouble();
                    double actualCps;
                    if      (roll < 0.60) actualCps = fl;
                    else if (roll < 0.90) actualCps = fl + 1.0;
                    else if (roll < 0.97) actualCps = fl + 2.0;
                    else                  actualCps = Math.Max(1.0, fl - 1.0);
                    delayMs = 1000.0 / actualCps;
                }
                else if (_cfg.RandMode == 1)
                {
                    double drop = _rng.NextDouble() * 0.5;
                    double butterflyCps = Math.Max(1.0, targetCps - drop);
                    delayMs = 2000.0 / butterflyCps;
                    delayMs += (_rng.NextDouble() * 2.0 - 1.0);
                    isButterfly = true;
                }
                else
                {
                // ----- Versión ultra‑estable de CPS -----
                // Reduce jitter y aplica suavizado exponencial (EMA) para mayor consistencia
                double jitter = NextGaussian() * 0.05; // ruido pequeño
                double rawCps = targetCps + jitter;
                double lower = targetCps * 0.97;
                double upper = targetCps * 1.03;
                rawCps = Math.Max(lower, Math.Min(upper, rawCps));
                rawCps = Math.Max(1.0, rawCps);
                rawCps = Math.Max(targetCps - 1.0, rawCps);
                // EMA smoothing (alpha = 0.1)
                if (double.IsNaN(_stableCps)) _stableCps = rawCps;
                else _stableCps = _stableCps * 0.9 + rawCps * 0.1;
                double finalCps = _stableCps;
                // Calcular delay en milisegundos
                delayMs = 1000.0 / finalCps;
                // Aplicar jitter menor al delay
                delayMs += (_rng.NextDouble() * 0.5 - 0.25); // ±0.25 ms
                // ----- Fin de versión estabilizada -----
                }

                // ── Ping / Latency Compensation (Adaptive) ──
                // El usuario pidió que sin importar el ping (aún con 200ms),
                // si setea 16.7 CPS, el clicker DEBE tirar entre 16 y 17 CPS exactos.
                // Por lo tanto, NO reducimos el target CPS.
                // La optimización para ping alto ahora recae 100% en el "Hold Time" (abajo),
                // donde mantenemos el click presionado por más milisegundos para que 
                // el servidor no lo descarte por culpa de la latencia, pero manteniendo la cadencia.
                
                double pingMs = _cfg.PingMs;
                if (pingMs > 0)
                {
                    double pingFactor = Math.Min(1.0, pingMs / 200.0);
                    // Solo absorbemos un jitter muy leve de red que promedia a 0,
                    // así que no baja ni sube los CPS promedio.
                    double jitterAbsorb = (_rng.NextDouble() * 2.0 - 1.0) * pingFactor * 0.5;
                    delayMs += jitterAbsorb;
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
                    PlayClickSound();
                    Thread.Sleep(microGap);
                    PerformClick(cursorShown, isRefilling, foregroundWnd);
                    PlayClickSound();
                    nextClickTick -= (long)(microGap * Stopwatch.Frequency / 1000.0);
                }
                else if (cursorShown || isRefilling)
                {
                    // Inventario / Refill: click tradicional rapido
                    PerformClick(cursorShown, isRefilling, foregroundWnd);
                    PlayClickSound();
                }
                else
                {
                    // ── IN-GAME: Modo PostMessage (Aim Assist Compatible) ──
                    // Enviamos los clicks directamente a la ventana de Minecraft.
                    // Esto hace un bypass completo de la cola global de Windows.
                    // Resultado: XClient sigue leyendo el mouse FÍSICO del usuario
                    // (con GetAsyncKeyState) para el aim assist, mientras que Minecraft
                    // recibe los clicks rápidos del autoclicker sin enterarse de la diferencia.
                    
                    IntPtr clickLParam = Win32.PostLeftDown(foregroundWnd);
                    
                    // Si el click no se envió (ej. está en la barra de título de la ventana), saltamos.
                    if (clickLParam != IntPtr.Zero)
                    {
                        PlayClickSound();

                        // W-Tap
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

                    // Hold time: PostMessage encola directo en la ventana, 1-2ms basta.
                    // SpinWait(1) en vez de busy-loop puro para reducir contención de CPU
                    // cuando el aim assist está corriendo en paralelo.
                    int holdTime = _rng.Next(1, 3);
                    if (pingMs > 0)
                        holdTime += (int)Math.Ceiling(pingMs * 0.5 * 0.05);
                    long holdTicks = (long)(holdTime * Stopwatch.Frequency / 1000.0);
                    long startTicks = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                    {
                        Thread.SpinWait(1);
                    }

                        // Recalcular posición del cursor para el UP.
                        // Si XClient aim assist movió el cursor durante el hold,
                        // usar la posición vieja del DOWN causaba que Minecraft
                        // descartara el click (posición UP != posición DOWN).
                        Win32.PostLeftUpFresh(foregroundWnd, clickLParam);
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
                        Thread.SpinWait(1);
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

        private void PerformClick(bool inInventory, bool refillMode, IntPtr foregroundWnd)
        {
            if (refillMode && inInventory)
            {
                IntPtr lP = Win32.PostLeftDown(foregroundWnd);
                if (lP != IntPtr.Zero)
                {
                    long refHoldTicks = (long)(_rng.Next(2, 5) * Stopwatch.Frequency / 1000.0);
                    long refStart = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetTimestamp() - refStart < refHoldTicks) { }
                    Win32.PostLeftUp(foregroundWnd, lP);
                }
                return;
            }

            IntPtr lParam = Win32.PostLeftDown(foregroundWnd);
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
                    Thread.SpinWait(1);
                }
                
                Win32.PostLeftUp(foregroundWnd, lParam);

                // W-Tap Logic
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
            }
            else
            {
                long holdTicks = (long)(1.0 * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks) { }
                Win32.PostLeftUp(foregroundWnd, lParam);
            }
        }


        // ── Cached focus check: avoids StringBuilder alloc + P/Invoke every tick ──
        private bool CachedIsMinecraftFocused(IntPtr hwnd)
        {
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
            // Reuse pre-allocated StringBuilder to avoid GC pressure
            _titleBuffer.Clear();
            Win32.GetWindowText(hwnd, _titleBuffer, 256);
            string title = _titleBuffer.ToString().ToLower();

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

        // ── Async sound playback: runs on a dedicated background thread ──
        // This prevents SoundPlayer.Play() from blocking the click thread
        // which was a major source of timing jitter and crosshair teleporting
        private void PlayClickSound()
        {
            if (string.IsNullOrEmpty(_cfg.Sound) || _cfg.Sound == "None") return;
            // Signal the sound thread to play (non-blocking)
            _soundPending = true;
        }

        private void SoundLoop()
        {
            while (_soundRunning)
            {
                if (_soundPending)
                {
                    _soundPending = false;
                    PlayClickSoundInternal();
                }
                Thread.Sleep(5);
            }
        }

        private void PlayClickSoundInternal()
        {
            string soundDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "XVA", "resource");
            string soundPath = Path.Combine(soundDir, _cfg.Sound);

            if (!File.Exists(soundPath))
            {
                string brandingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "lospoderosos", "resource");
                soundPath = Path.Combine(brandingDir, _cfg.Sound);
            }

            if (!File.Exists(soundPath)) return;

            try
            {
                if (_soundPlayer == null || _cachedSoundPath != soundPath)
                {
                    _soundPlayer = new SoundPlayer(soundPath);
                    _soundPlayer.Load();
                    _cachedSoundPath = soundPath;
                }
                _soundPlayer.Play();
            }
            catch
            {
                // Fail silently
            }
        }
    }
}
