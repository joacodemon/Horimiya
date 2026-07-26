using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Horimiya.Config;
using Horimiya.Utils;

namespace Horimiya.Modules
{
    [Injectable(true)]
    public class HitDetector
    {
        private readonly AppConfig _cfg;

        // ── Stats ──
        public int TotalHits = 0;
        public int TotalMisses = 0;
        public double HitRate = 0.0;
        public long LastHitTick = 0;

        // Ring buffer for recent hit/miss results (1=hit, 0=miss)
        private const int RING_SIZE = 40;
        private readonly int[] _ring = new int[RING_SIZE];
        private int _ringPos = 0;
        private int _ringCount = 0;

        // Pixel sampling state
        private readonly Stopwatch _sw = new Stopwatch();

        // Baseline pixel snapshot (taken before click)
        private uint[] _baselinePixels;

        // ── Sample grid ──
        // 20 points covering a wider radius to catch hurt particles and knockback motion.
        // Uses screen-absolute coords offset from window center.
        private const int SAMPLE_POINTS = 20;
        private static readonly Point[] SAMPLE_OFFSETS = new Point[]
        {
            // Inner ring: right at crosshair where particles first appear
            new Point(-6,  -6),  new Point( 6,  -6),
            new Point(-6,   6),  new Point( 6,   6),
            // Mid ring: typical particle spawn radius
            new Point(-22, -18), new Point(22, -18),
            new Point(-22,  18), new Point(22,  18),
            new Point( 0,  -24), new Point( 0,   24),
            new Point(-24,   0), new Point(24,    0),
            // Outer ring: knockback / entity motion
            new Point(-44,  -8), new Point(44,  -8),
            new Point(-44,   8), new Point(44,   8),
            new Point(-16, -40), new Point(16, -40),
            new Point(-16,  35), new Point(16,  35),
        };

        // ── Hurt particle thresholds ──
        // Minecraft hit particles: vivid red (R high, G low, B low).
        // Tightened to reduce false positives from warm-colored blocks/sky.
        private const byte HURT_R_MIN    = 185; // strong red channel
        private const byte HURT_G_MAX    = 75;  // barely any green
        private const byte HURT_B_MAX    = 75;  // barely any blue
        private const int  HURT_PIXEL_MIN = 2;  // need >= 2 red pixels to call it a hit

        // ── Motion thresholds ──
        // Higher delta avoids triggering on compression artifacts or camera pan.
        private const int MOTION_THRESHOLD   = 65; // per-channel sum delta
        private const int MOTION_MIN_CHANGED = 6;  // at least 6/20 points must change

        // ── Debounce ──
        // Prevent counting the same hit event twice (game flashes for multiple frames).
        private const long DEBOUNCE_TICKS = 150 * 10000L; // 150ms in 100ns ticks
        private long _lastHitRecordedTick = 0;

        public HitDetector(AppConfig cfg)
        {
            _cfg = cfg;
            _sw.Start();
            _baselinePixels = new uint[SAMPLE_POINTS];
        }

        /// <summary>
        /// Call BEFORE sending the click to capture baseline pixel state.
        /// Uses screen DC so coordinates are absolute — works correctly on
        /// high-DPI and scaled displays without needing to convert client coords.
        /// </summary>
        public void CaptureBaseline(IntPtr hwnd)
        {
            if (!_cfg.HitDetectionEnabled) return;
            if (hwnd == IntPtr.Zero) return;

            Win32.RECT clientRect;
            if (!Win32.GetClientRect(hwnd, out clientRect)) return;

            // Convert window client center to screen coordinates via GetWindowRect
            Win32.RECT windowRect;
            if (!Win32.GetWindowRect(hwnd, out windowRect)) return;

            // Client area starts at windowRect top-left + non-client frame (borders/title).
            // The fastest approximation: use the window screen rect center minus half client size.
            int frameW = ((windowRect.right - windowRect.left) - (clientRect.right - clientRect.left)) / 2;
            int frameH = (windowRect.bottom - windowRect.top) - (clientRect.bottom - clientRect.top) - frameW;

            int cx = windowRect.left + frameW + (clientRect.right - clientRect.left) / 2;
            int cy = windowRect.top  + Math.Max(0, frameH) + (clientRect.bottom - clientRect.top) / 2;

            IntPtr hdc = Win32.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return;

            try
            {
                for (int i = 0; i < SAMPLE_POINTS; i++)
                    _baselinePixels[i] = Win32.GetPixel(hdc, cx + SAMPLE_OFFSETS[i].X, cy + SAMPLE_OFFSETS[i].Y);
            }
            finally
            {
                Win32.ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        /// <summary>
        /// Call ~50-80ms AFTER the click lands to check for hurt particles or motion.
        /// Returns true if a hit was detected.
        /// </summary>
        public bool CheckHit(IntPtr hwnd)
        {
            if (!_cfg.HitDetectionEnabled) return false;
            if (hwnd == IntPtr.Zero) return false;

            Win32.RECT clientRect;
            if (!Win32.GetClientRect(hwnd, out clientRect)) return false;

            int w = clientRect.right - clientRect.left;
            int h = clientRect.bottom - clientRect.top;
            if (w < 100 || h < 100) return false;

            Win32.RECT windowRect;
            if (!Win32.GetWindowRect(hwnd, out windowRect)) return false;

            int frameW = ((windowRect.right - windowRect.left) - w) / 2;
            int frameH = (windowRect.bottom - windowRect.top) - h - frameW;

            int cx = windowRect.left + frameW + w / 2;
            int cy = windowRect.top  + Math.Max(0, frameH) + h / 2;

            IntPtr hdc = Win32.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;

            bool hitDetected = false;

            try
            {
                int hurtPixelCount  = 0;
                int motionPixelCount = 0;

                for (int i = 0; i < SAMPLE_POINTS; i++)
                {
                    int px = cx + SAMPLE_OFFSETS[i].X;
                    int py = cy + SAMPLE_OFFSETS[i].Y;

                    uint pixel = Win32.GetPixel(hdc, px, py);
                    if (pixel == 0xFFFFFFFF) continue;

                    byte r = (byte)( pixel        & 0xFF);
                    byte g = (byte)((pixel >>  8) & 0xFF);
                    byte b = (byte)((pixel >> 16) & 0xFF);

                    // ── Hurt particle: vivid red ──
                    if (r >= HURT_R_MIN && g <= HURT_G_MAX && b <= HURT_B_MAX)
                        hurtPixelCount++;

                    // ── Motion vs baseline ──
                    uint baseline = _baselinePixels[i];
                    if (baseline != 0xFFFFFFFF)
                    {
                        byte br = (byte)( baseline        & 0xFF);
                        byte bg = (byte)((baseline >>  8) & 0xFF);
                        byte bb = (byte)((baseline >> 16) & 0xFF);

                        int delta = Math.Abs(r - br) + Math.Abs(g - bg) + Math.Abs(b - bb);
                        if (delta > MOTION_THRESHOLD)
                            motionPixelCount++;
                    }
                }

                hitDetected = (hurtPixelCount >= HURT_PIXEL_MIN) || (motionPixelCount >= MOTION_MIN_CHANGED);
            }
            finally
            {
                Win32.ReleaseDC(IntPtr.Zero, hdc);
            }

            // ── Debounce: skip recording if the same hit event is still active ──
            long nowTick = DateTime.UtcNow.Ticks;
            if (hitDetected && (nowTick - _lastHitRecordedTick) < DEBOUNCE_TICKS)
                return true; // same flash, don't double-count

            RecordResult(hitDetected);
            if (hitDetected) _lastHitRecordedTick = nowTick;

            return hitDetected;
        }

        private void RecordResult(bool hit)
        {
            if (hit)
            {
                TotalHits++;
                LastHitTick = _sw.ElapsedTicks;
            }
            else
            {
                TotalMisses++;
            }

            _ring[_ringPos] = hit ? 1 : 0;
            _ringPos = (_ringPos + 1) % RING_SIZE;
            if (_ringCount < RING_SIZE) _ringCount++;

            int hits = 0;
            for (int i = 0; i < _ringCount; i++)
                hits += _ring[i];
            HitRate = _ringCount > 0 ? (double)hits / _ringCount * 100.0 : 0.0;
        }

        /// <summary>
        /// Returns a CPS multiplier based on current hit rate.
        /// Above 65% hit rate => full CPS. Below => linear reduction.
        /// </summary>
        public double GetAdaptiveCpsMultiplier()
        {
            if (!_cfg.AdaptiveCpsEnabled) return 1.0;
            if (_ringCount < 5) return 1.0;

            const double threshold = 65.0;
            if (HitRate >= threshold) return 1.0;

            double minMultiplier = _cfg.AdaptiveCpsMin / Math.Max(1.0, _cfg.AverageCps);
            minMultiplier = Math.Max(0.3, Math.Min(1.0, minMultiplier));

            double t = HitRate / threshold;
            return minMultiplier + t * (1.0 - minMultiplier);
        }

        /// <summary>Resets all stats. Call when toggling click on/off.</summary>
        public void Reset()
        {
            TotalHits = 0;
            TotalMisses = 0;
            HitRate = 0.0;
            _ringCount = 0;
            _ringPos = 0;
            LastHitTick = 0;
            _lastHitRecordedTick = 0;
        }
    }
}
