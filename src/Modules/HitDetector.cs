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
        private const int RING_SIZE = 30;
        private readonly int[] _ring = new int[RING_SIZE];
        private int _ringPos = 0;
        private int _ringCount = 0;

        // Pixel sampling state
        private readonly Stopwatch _sw = new Stopwatch();
        private long _lastSampleTick = 0;

        // Baseline pixel snapshot (taken before click)
        private uint[] _baselinePixels;
        private const int SAMPLE_POINTS = 12;

        // Pre-computed sample offsets relative to screen center
        // These cover the area where Minecraft hurt particles appear
        private static readonly Point[] SAMPLE_OFFSETS = new Point[]
        {
            // Inner ring: close to crosshair where particles spawn
            new Point(-8, -8),   new Point(8, -8),
            new Point(-8,  8),   new Point(8,  8),
            // Mid ring: slightly further out
            new Point(-20, -15), new Point(20, -15),
            new Point(-20,  15), new Point(20,  15),
            // Outer ring: for knockback motion detection
            new Point(-35, -5),  new Point(35, -5),
            new Point(0, -30),   new Point(0, 25),
        };

        // Hurt particle color thresholds
        // Minecraft hurt particles are bright red: R > 180, G < 80, B < 80
        private const byte HURT_R_MIN = 160;
        private const byte HURT_G_MAX = 100;
        private const byte HURT_B_MAX = 100;

        // Motion detection threshold (pixel color delta)
        private const int MOTION_THRESHOLD = 40;
        private const int MOTION_MIN_CHANGED = 4; // At least 4 of 12 points must change

        public HitDetector(AppConfig cfg)
        {
            _cfg = cfg;
            _sw.Start();
            _baselinePixels = new uint[SAMPLE_POINTS];
        }

        /// <summary>
        /// Call BEFORE sending the click to capture baseline pixel state.
        /// This is fast (~0.1ms) since we only sample 12 pixels via GetPixel.
        /// </summary>
        public void CaptureBaseline(IntPtr hwnd)
        {
            if (!_cfg.HitDetectionEnabled) return;
            if (hwnd == IntPtr.Zero) return;

            Win32.RECT rect;
            if (!Win32.GetClientRect(hwnd, out rect)) return;

            int cx = (rect.right - rect.left) / 2;
            int cy = (rect.bottom - rect.top) / 2;

            IntPtr hdc = Win32.GetDC(hwnd);
            if (hdc == IntPtr.Zero) return;

            try
            {
                for (int i = 0; i < SAMPLE_POINTS; i++)
                {
                    int px = cx + SAMPLE_OFFSETS[i].X;
                    int py = cy + SAMPLE_OFFSETS[i].Y;
                    _baselinePixels[i] = Win32.GetPixel(hdc, px, py);
                }
            }
            finally
            {
                Win32.ReleaseDC(hwnd, hdc);
            }

            _lastSampleTick = _sw.ElapsedTicks;
        }

        /// <summary>
        /// Call ~50-80ms AFTER the click lands to check for hurt particles or motion.
        /// Returns true if a hit was detected.
        /// </summary>
        public bool CheckHit(IntPtr hwnd)
        {
            if (!_cfg.HitDetectionEnabled) return false;
            if (hwnd == IntPtr.Zero) return false;

            Win32.RECT rect;
            if (!Win32.GetClientRect(hwnd, out rect)) return false;

            int cx = (rect.right - rect.left) / 2;
            int cy = (rect.bottom - rect.top) / 2;
            int w = rect.right - rect.left;
            int h = rect.bottom - rect.top;
            if (w < 100 || h < 100) return false;

            IntPtr hdc = Win32.GetDC(hwnd);
            if (hdc == IntPtr.Zero) return false;

            bool hitDetected = false;

            try
            {
                int hurtPixelCount = 0;
                int motionPixelCount = 0;

                for (int i = 0; i < SAMPLE_POINTS; i++)
                {
                    int px = cx + SAMPLE_OFFSETS[i].X;
                    int py = cy + SAMPLE_OFFSETS[i].Y;

                    // Clamp to client area
                    if (px < 0 || py < 0 || px >= w || py >= h) continue;

                    uint pixel = Win32.GetPixel(hdc, px, py);
                    if (pixel == 0xFFFFFFFF) continue; // Invalid

                    byte r = (byte)(pixel & 0xFF);
                    byte g = (byte)((pixel >> 8) & 0xFF);
                    byte b = (byte)((pixel >> 16) & 0xFF);

                    // Check for hurt particle colors (red flash)
                    if (r >= HURT_R_MIN && g <= HURT_G_MAX && b <= HURT_B_MAX)
                    {
                        hurtPixelCount++;
                    }

                    // Check for motion (significant pixel change from baseline)
                    uint baseline = _baselinePixels[i];
                    if (baseline != 0xFFFFFFFF)
                    {
                        byte br = (byte)(baseline & 0xFF);
                        byte bg = (byte)((baseline >> 8) & 0xFF);
                        byte bb = (byte)((baseline >> 16) & 0xFF);

                        int delta = Math.Abs(r - br) + Math.Abs(g - bg) + Math.Abs(b - bb);
                        if (delta > MOTION_THRESHOLD)
                        {
                            motionPixelCount++;
                        }
                    }
                }

                // Hit detected if:
                // - At least 1 hurt particle pixel found, OR
                // - Significant motion detected (knockback) in enough sample points
                hitDetected = (hurtPixelCount >= 1) || (motionPixelCount >= MOTION_MIN_CHANGED);
            }
            finally
            {
                Win32.ReleaseDC(hwnd, hdc);
            }

            // Record result
            RecordResult(hitDetected);
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

            // Update ring buffer
            _ring[_ringPos] = hit ? 1 : 0;
            _ringPos = (_ringPos + 1) % RING_SIZE;
            if (_ringCount < RING_SIZE) _ringCount++;

            // Calculate hit rate from ring buffer (recent N clicks)
            int hits = 0;
            for (int i = 0; i < _ringCount; i++)
                hits += _ring[i];
            HitRate = _ringCount > 0 ? (double)hits / _ringCount * 100.0 : 0.0;
        }

        /// <summary>
        /// Returns a CPS multiplier based on current hit rate.
        /// When hit rate is high (hitting target), returns 1.0 (full CPS).
        /// When hit rate is low (missing), returns a reduced multiplier
        /// to simulate natural human behavior (slower when out of range).
        /// </summary>
        public double GetAdaptiveCpsMultiplier()
        {
            if (!_cfg.AdaptiveCpsEnabled) return 1.0;
            if (_ringCount < 5) return 1.0; // Need at least 5 samples

            // HitRate is 0-100
            // Above 60% hit rate: full CPS
            // Below 60%: scale down linearly
            // At 0% hit rate: use AdaptiveCpsMin / AverageCps ratio
            if (HitRate >= 60.0) return 1.0;

            double minMultiplier = _cfg.AdaptiveCpsMin / Math.Max(1.0, _cfg.AverageCps);
            minMultiplier = Math.Max(0.3, Math.Min(1.0, minMultiplier));

            // Linear interpolation: at 60% = 1.0, at 0% = minMultiplier
            double t = HitRate / 60.0;
            return minMultiplier + t * (1.0 - minMultiplier);
        }

        /// <summary>
        /// Resets all stats. Call when toggling click on/off.
        /// </summary>
        public void Reset()
        {
            TotalHits = 0;
            TotalMisses = 0;
            HitRate = 0.0;
            _ringCount = 0;
            _ringPos = 0;
            LastHitTick = 0;
        }
    }
}
