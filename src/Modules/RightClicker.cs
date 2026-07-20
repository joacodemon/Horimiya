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

namespace Horimiya.Modules
{
    public class RightClicker
    {
        private readonly AppConfig _cfg;
        private readonly Random _rng = new Random();
        private Thread _thread;
        private volatile bool _running = false;

        // Performance cache fields
        private bool _lastFocusResult = false;
        private IntPtr _lastFocusHwnd = IntPtr.Zero;
        private Stopwatch _focusCheckTimer = new Stopwatch();
        private const int FOCUS_CHECK_INTERVAL_MS = 100;

        private bool _lastCursorVisible = false;
        private Stopwatch _cursorCheckTimer = new Stopwatch();
        private const int CURSOR_CHECK_INTERVAL_MS = 50;


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
        public RightClicker(AppConfig cfg)
        {
            _cfg = cfg;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            Win32.timeBeginPeriod(1);

            _thread = new Thread(ClickLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            Win32.timeEndPeriod(1);
        }

        private void ClickLoop()
        {
            var sw = new Stopwatch();
            sw.Start();
            long nextClickTick = sw.ElapsedTicks;
            
            double currentJitterCps = _cfg.RightAverageCps;
            double jitterMomentum = 0;

            while (_running)
            {
                if (!Clicking)
                {
                    Thread.Sleep(15);
                    _focusCheckTimer.Reset();
                    _cursorCheckTimer.Reset();
                    continue;
                }

                // ── Mode check ──
                bool shouldClick = false;

                if (_cfg.RightMode == 0) // Hold mode: need physical RMB held
                {
                    shouldClick = Win32.IsRightDown;
                }
                else if (_cfg.RightMode == 1) // Toggle mode
                {
                    shouldClick = true;
                }
                else // Always mode
                {
                    shouldClick = true;
                }

                if (!shouldClick)
                {
                    Thread.Sleep(5);
                    nextClickTick = sw.ElapsedTicks;
                    continue;
                }

                // ── InGame & Menu Checks ──
                bool isMinecraft = IsMinecraftFocused();

                if (_cfg.OnlyInGame && !isMinecraft)
                {
                    Thread.Sleep(50);
                    nextClickTick = sw.ElapsedTicks;
                    continue;
                }

                bool cursorShown = IsCursorVisible();

                if (!_cfg.WorkInMenus && cursorShown)
                {
                    Thread.Sleep(5);
                    nextClickTick = sw.ElapsedTicks;
                    continue;
                }

                // ── Randomization Mode ──
                double targetCps = _cfg.RightAverageCps;
                bool isButterfly = false;

                double delayMs;

                if (_cfg.RightRandMode == 2) // NoDelay
                {
                    double actualCps = targetCps;
                    double roll = _rng.NextDouble();
                    
                    if (roll < 0.35) actualCps = Math.Floor(targetCps);
                    else if (roll < 0.55) actualCps = Math.Ceiling(targetCps);
                    else if (roll > 0.95) actualCps = Math.Floor(targetCps) - 1.0;

                    delayMs = 1000.0 / Math.Max(1.0, actualCps);
                }
                else if (_cfg.RightRandMode == 1) // Butterfly
                {
                    double drop = _rng.NextDouble() * 2.5;
                    double butterflyCps = Math.Max(1.0, targetCps - drop);
                    
                    double cycleCps = butterflyCps / 2.0;
                    delayMs = 1000.0 / Math.Max(0.5, cycleCps);
                    delayMs += (_rng.NextDouble() * 8.0 - 4.0);
                    
                    isButterfly = true;
                }
                else // Jitter (Mode 0)
                {
                    double drift = NextGaussian() * 0.4; 
                    jitterMomentum = (jitterMomentum * 0.75) + drift;
                    currentJitterCps += jitterMomentum;
                    
                    double diff = targetCps - currentJitterCps;
                    currentJitterCps += diff * 0.20;
                    
                    double finalCps = currentJitterCps;
                    if (_rng.NextDouble() < 0.035) 
                    {
                        finalCps *= (0.60 + _rng.NextDouble() * 0.20);
                        jitterMomentum -= 1.5;
                    }
                    else if (_rng.NextDouble() < 0.025)
                    {
                        finalCps *= (1.15 + _rng.NextDouble() * 0.20);
                        jitterMomentum += 1.5;
                    }
                    
                    finalCps = Math.Max(targetCps - 5.0, Math.Min(targetCps + 4.0, finalCps));
                    finalCps = Math.Max(1.0, finalCps);

                    delayMs = 1000.0 / finalCps;
                    delayMs += (_rng.NextDouble() * 12.0 - 6.0);
                }

                // ── Ping / Latency Compensation ──
                // When ping > 0, we pre-shift click timing to account for network delay:
                double pingMs = _cfg.PingMs;
                if (pingMs > 0)
                {
                    double oneWayMs = pingMs * 0.5;
                    double delayReduction = oneWayMs * 0.15;
                    delayMs -= delayReduction;
                }

                delayMs = Math.Max(3.0, delayMs);

                long delayTicks = (long)(delayMs * Stopwatch.Frequency / 1000.0);
                nextClickTick += delayTicks;
                long currentTick = sw.ElapsedTicks;
                if (nextClickTick < currentTick)
                {
                    nextClickTick = currentTick;
                }

                // ── Perform the click ──
                Point dummyPoint;
                if (!cursorShown || Win32.IsCursorInClientArea(Win32.GetForegroundWindow(), out dummyPoint))
                {
                    if (isButterfly)
                    {
                        PerformClick(cursorShown);
                        Thread.SpinWait(50);
                        int microGap = _rng.Next(10, 35);
                        Thread.Sleep(microGap);
                        PerformClick(cursorShown);
                    }
                    else
                    {
                        PerformClick(cursorShown);
                    }
                }

                while (sw.ElapsedTicks < nextClickTick)
                {
                    if (!Clicking || !_running) break;
                    long left = nextClickTick - sw.ElapsedTicks;
                    double leftMs = (double)left / Stopwatch.Frequency * 1000.0;
                    
                    if (leftMs > 3.0)       
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
        }

        private void PerformClick(bool inInventory)
        {
            Win32.SendRightDown();

            if (!inInventory)
            {
                int holdTime = _rng.Next(1, 4);
                if (_rng.NextDouble() < 0.08) holdTime += _rng.Next(2, 6); 
                
                double pingMs = _cfg.PingMs;
                if (pingMs > 0)
                    holdTime += (int)Math.Ceiling(pingMs * 0.5 * 0.05);
                
                long holdTicks = (long)(holdTime * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks)
                {
                    Thread.Sleep(0);
                }
                
                Win32.SendRightUp();
            }
            else
            {
                long holdTicks = (long)(1.0 * Stopwatch.Frequency / 1000.0);
                long startTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - startTicks < holdTicks) { Thread.Sleep(0); }
                Win32.SendRightUp();
            }
        }

        private double NextGaussian()
        {
            double u1 = 1.0 - _rng.NextDouble();
            double u2 = 1.0 - _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        private bool IsMinecraftFocused()
        {
            IntPtr currentHwnd = Win32.GetForegroundWindow();
            System.Drawing.Point dummy;
            if (!Win32.IsCursorInClientArea(currentHwnd, out dummy)) return false;

            if (!_focusCheckTimer.IsRunning || _focusCheckTimer.ElapsedMilliseconds > FOCUS_CHECK_INTERVAL_MS)
            {
                if (currentHwnd != _lastFocusHwnd)
                {
                    _lastFocusHwnd = currentHwnd;
                    Win32.GetWindowText(currentHwnd, _titleBuffer, _titleBuffer.Capacity);
                    string title = _titleBuffer.ToString().ToLower();
                    
                    bool isMc = title.Contains("minecraft") || 
                                       title.Contains("lunar") || 
                                       title.Contains("badlion") || 
                                       title.Contains("labymod") ||
                                       title.Contains("salwyrr") ||
                                       title.Contains("cheatbreaker") ||
                                       title.Contains("feather");
                    _lastFocusResult = isMc;
                }
                _focusCheckTimer.Restart();
            }
            return _lastFocusResult;
        }

        private bool IsCursorVisible()
        {
            if (!_cursorCheckTimer.IsRunning || _cursorCheckTimer.ElapsedMilliseconds > CURSOR_CHECK_INTERVAL_MS)
            {
                Win32.CURSORINFO pci = new Win32.CURSORINFO();
                pci.cbSize = Marshal.SizeOf(typeof(Win32.CURSORINFO));
                if (Win32.GetCursorInfo(ref pci))
                {
                    _lastCursorVisible = pci.flags == 1; // 1 = CURSOR_SHOWING
                }
                _cursorCheckTimer.Restart();
            }
            return _lastCursorVisible;
        }

    }
}
