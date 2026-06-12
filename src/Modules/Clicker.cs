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

        public Clicker(AppConfig cfg)
        {
            _cfg = cfg;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            // Set Windows timer resolution to 1ms for accurate Thread.Sleep
            // Without this, Thread.Sleep(1) can sleep up to 15.6ms causing timing jitter
            // which manifests as crosshair teleporting and irregular click timing
            Win32.timeBeginPeriod(1);

            _thread = new Thread(ClickLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
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
            // Reset debug counters
            // Jitter state
            double currentJitterCps = _cfg.AverageCps;
            double jitterMomentum = 0;

            // Detect CheatBreaker and disable clicker if running
            // if (_cfg.DetectCheatBreaker && System.Diagnostics.Process.GetProcessesByName("CheatBreaker").Length > 0)
            // {
            //     _running = false;
            //     return; // exit ClickLoop gracefully
            // }
            while (_running)
            {
                // If Clicking is OFF, sleep longer to save CPU
                if (!Clicking)
                {
                    Thread.Sleep(15);
                    // Reset caches when not clicking so they refresh on next activation
                    _focusCheckTimer.Reset();
                    _cursorCheckTimer.Reset();
                    _wasBbActive = false;
                    continue;
                }

                // ── Mode check ──
                // El usuario quiere que siempre funcione manteniendo el click (función de autoclicker)
                bool shouldClick = Win32.IsLeftDown;

                if (!shouldClick)
                {
                    Thread.Sleep(5);
                    nextClickTick = sw.ElapsedTicks;
                    continue;
                }

                // ── RMB-Lock: pause left clicking while RMB is held ──
                if (_cfg.RmbLock && Win32.IsRightDown)
                {
                    Thread.Sleep(5);
                    continue;
                }

                // ── Focus check (cached) ──
                IntPtr foregroundWnd = Win32.GetForegroundWindow();
                if (!CachedIsMinecraftFocused(foregroundWnd))
                {
                    Thread.Sleep(10);
                    continue;
                }

                // ── Menu / Inventory restriction (cached cursor check) ──
                bool cursorShown = CachedIsCursorVisible();
                // El usuario pidió que si hay cursor (estamos en un menú), SOLO funcione en el inventario.
                if (cursorShown)
                {
                    if (!CachedIsInventoryLikeScreen(foregroundWnd))
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                }

                // Integrated Refill check: anytime cursor is shown and SHIFT is held
                bool isRefilling = cursorShown && ((Win32.GetAsyncKeyState(0x10) & 0x8000) != 0);



                // ── Perform the click(s) ──
                // [Removed] Unconditional click moved to mode‑specific block

                // ── Randomization Mode ──
                double targetCps = _cfg.AverageCps;
                bool isButterfly = false;

                double delayMs;

                if (_cfg.RandMode == 3) // ── Manual Custom Randomization ──
                {
                    double sumWeights = 0;
                    for (int i = 0; i < 25; i++) sumWeights += _cfg.CustomCpsWeights[i];

                    double chosenCps = targetCps; // Fallback to target
                    if (sumWeights > 0)
                    {
                        double roll = _rng.NextDouble() * sumWeights;
                        double accumulator = 0;
                        for (int i = 0; i < 25; i++)
                        {
                            accumulator += _cfg.CustomCpsWeights[i];
                            if (roll <= accumulator)
                            {
                                chosenCps = i + 1; // CPS is index + 1
                                break;
                            }
                        }
                    }
                    
                    delayMs = 1000.0 / Math.Max(1.0, chosenCps);
                    // Add micro-noise to prevent purely synthetic constant delays
                    delayMs += (_rng.NextDouble() * 3.0 - 1.5); 
                }
                else if (_cfg.RandMode == 2) // ── NoDelay: constant timing ──
                {
                    double actualCps = targetCps;
                    double roll = _rng.NextDouble();
                    
                    if (roll < 0.35) 
                    {
                         // 35% chance to round down (e.g. 13.5 -> 13.0)
                         actualCps = Math.Floor(targetCps);
                    } 
                    else if (roll < 0.55)
                    {
                         // 20% chance to round up (e.g. 13.5 -> 14.0)
                         actualCps = Math.Ceiling(targetCps);
                    }
                    else if (roll > 0.95)
                    {
                         // 5% chance to drop 1 CPS below floor (e.g. 13.5 -> 12.0)
                         actualCps = Math.Floor(targetCps) - 1.0;
                    }
                    // 40% of the time stays exactly at targetCps

                    delayMs = 1000.0 / Math.Max(1.0, actualCps);
                }
                else if (_cfg.RandMode == 1) // ── Butterfly: double-click ──
                {
                    // Random drop of 0 to 2.5 CPS to simulate "13 14 15" for a 15 target
                    double drop = _rng.NextDouble() * 2.5;
                    double butterflyCps = Math.Max(1.0, targetCps - drop);
                    
                    // Butterfly simulates pressing with two fingers alternating
                    // We send 2 clicks per cycle, so the cycle interval = 2 / butterflyCps
                    double cycleCps = butterflyCps / 2.0;
                    delayMs = 1000.0 / Math.Max(0.5, cycleCps);
                    
                    // Add slight timing noise (±4ms) to make it more human
                    delayMs += (_rng.NextDouble() * 8.0 - 4.0);
                    
                    isButterfly = true;
                }
                else // ── Jitter (Mode 0): legit human-like randomization ──
                {
                    // Humans have momentum in their clicking speed. 
                    // We slowly drift the current CPS rather than randomizing completely from scratch every click.
                    
                    // 1. Momentum drift (Perlin-like 1D random walk)
                    double drift = NextGaussian() * 0.4; 
                    jitterMomentum = (jitterMomentum * 0.75) + drift; // Smooth the momentum
                    
                    // 2. Apply momentum to current CPS
                    currentJitterCps += jitterMomentum;
                    
                    // 3. Spring force to pull it back towards the target CPS
                    double diff = targetCps - currentJitterCps;
                    currentJitterCps += diff * 0.20; // 20% spring back per click
                    
                    // 4. Fatigue / Drop logic (humans randomly drop a click)
                    double finalCps = currentJitterCps;
                    if (_rng.NextDouble() < 0.035) // 3.5% chance to drop heavily (muscle lag)
                    {
                        finalCps *= (0.60 + _rng.NextDouble() * 0.20); // Drop to 60%-80% speed for one click
                        jitterMomentum -= 1.5; // Negatively impact momentum
                    }
                    
                    // 5. Spasm / Twitch logic (humans randomly tense up)
                    else if (_rng.NextDouble() < 0.025)
                    {
                        finalCps *= (1.15 + _rng.NextDouble() * 0.20); // Jump 15%-35% faster
                        jitterMomentum += 1.5;
                    }
                    
                    // Clamp it to reasonable human bounds
                    finalCps = Math.Max(targetCps - 5.0, Math.Min(targetCps + 4.0, finalCps));
                    finalCps = Math.Max(1.0, finalCps);

                    delayMs = 1000.0 / finalCps;

                    // Add high-frequency timing noise (humans can't release exactly on time)
                    delayMs += (_rng.NextDouble() * 12.0 - 6.0); // ±6ms raw jitter
                }

                if (isRefilling && targetCps >= 15.0)
                {
                    _refillClickCount++;
                    if (_refillClickCount >= (8 + _rng.Next(4)))
                    {
                        delayMs += _rng.Next(15, 25); // tiny pause to prevent items sticking without killing CPS
                        _refillClickCount = 0;
                    }
                }

                delayMs = Math.Max(3.0, delayMs);

                long delayTicks = (long)(delayMs * Stopwatch.Frequency / 1000.0);

                // Avanzar el tick objetivo
                nextClickTick += delayTicks;
                long currentTick = sw.ElapsedTicks;
                if (nextClickTick < currentTick)
                {
                    // Si nos atrasamos mucho, reset para evitar rafaga de clicks
                    nextClickTick = currentTick;
                }

                // ── Ejecutar el click ──
                if (isButterfly)
                {
                    // Butterfly: two rapid clicks with a tiny gap
                    PerformClick(cursorShown, isRefilling);
                    PlayClickSound();
                    Thread.SpinWait(50); // ultra-short gap between the two clicks
                    int microGap = _rng.Next(10, 35); // 10-35ms gap for the second click
                    Thread.Sleep(microGap);
                    PerformClick(cursorShown, isRefilling);
                    PlayClickSound();
                }
                else
                {
                    PerformClick(cursorShown, isRefilling);
                    PlayClickSound();
                }

                // Maximum precision sleep (Hybrid Sleep/Yield for 0 latency)
                while (sw.ElapsedTicks < nextClickTick)
                {
                    if (!Clicking || !_running) break;
                    long left = nextClickTick - sw.ElapsedTicks;
                    double leftMs = (double)left / Stopwatch.Frequency * 1000.0;
                    
                    if (leftMs > 2.0) // Only sleep if we have plenty of time
                    {
                        Thread.Sleep(1);
                    }
                    else if (leftMs > 0.1)
                    {
                        // Yield to prevent CPU starvation, which fixes the "sensitivity goes up" bug at high CPS
                        Thread.Sleep(0);
                    }
                    else
                    {
                        // Active spin for only the last 0.1ms guarantees precision without starving the CPU
                        Thread.SpinWait(10);
                    }
                }
            }
        }

        private void PerformClick(bool inInventory, bool refillMode = false)
        {
            if (refillMode && inInventory)
            {
                // Shift-click for refill: hold shift, click, with very fast hold timing
                Win32.SendLeftDown();
                // Use Stopwatch for precise short delay without risking Thread.Sleep inaccuracy
                long refHoldTicks = (long)(_rng.Next(2, 5) * Stopwatch.Frequency / 1000.0);
                long refStart = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - refStart < refHoldTicks) Thread.Sleep(0);
                Win32.SendLeftUp();
                return;
            }

            Win32.SendLeftDown();

            if (!inInventory)
            {
                int holdTime = _rng.Next(1, 3); // Faster hits optimization
                
                // Spin wait replaced with Sleep(0) to prevent CPU starvation and sensitivity bug during clicks
                long holdTicks = (long)(holdTime * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                {
                    Thread.Sleep(0);
                }
                
                Win32.SendLeftUp();

                // W-Tap Logic (Reset sprint to deal more knockback and take less)
                if (_cfg.WTapEnabled && (Win32.GetAsyncKeyState(0x57) & 0x8000) != 0)
                {
                    if (_rng.NextDouble() < 0.45) // ~45% of hits will trigger W-Tap
                    {
                        Win32.keybd_event(0x57, 0, Win32.KEYEVENTF_KEYUP, 0); // Release W
                        // Async W-Tap to prevent blocking the clicker thread at high CPS
                        ThreadPool.QueueUserWorkItem(_ => {
                            Thread.Sleep(_rng.Next(10, 30));
                            Win32.keybd_event(0x57, 0, 0, 0); // Press W again
                        });
                    }
                }
            }
            else
            {
                // If in inventory, very quick release
                long holdTicks = (long)(1.0 * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks) Thread.Sleep(0);
                Win32.SendLeftUp();
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
