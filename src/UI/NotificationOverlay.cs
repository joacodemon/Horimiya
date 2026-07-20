using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Horimiya.Config;

namespace Horimiya.UI
{
    public class NotificationOverlay : Form
    {
        public enum NotificationType
        {
            Success,
            Error,
            Warning,
            Info
        }

        private static List<NotificationOverlay> _activeNotifications = new List<NotificationOverlay>();

        private string _title;
        private string _message;
        private NotificationType _type;
        private int _positionIndex;
        private IntPtr _targetWindow = IntPtr.Zero;
        private System.Windows.Forms.Timer _timer;
        private double _targetOpacity = 0.0;
        private int _lifeTimeMs = 4000;
        private int _elapsedMs = 0;
        private int _fadeTimerInterval = 16;
        private int _targetY;
        private bool _isInitialPosition = true;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_HWNDPARENT = -8;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020 | 0x00000008 | 0x08000000 | 0x00000080;
                return cp;
            }
        }

        private NotificationOverlay(string title, string message, NotificationType type, int position)
        {
            _title = title;
            _message = message;
            _type = type;
            _positionIndex = position;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;

            Opacity = 0.0;
            DoubleBuffered = true;

            Size = new Size(280, 58);

            _timer = new System.Windows.Forms.Timer { Interval = _fadeTimerInterval };
            _timer.Tick += OnTick;
        }

        private static bool IsMinecraftFocused(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            System.Text.StringBuilder titleBuffer = new System.Text.StringBuilder(256);
            Horimiya.Utils.Win32.GetWindowText(hwnd, titleBuffer, 256);
            string title = titleBuffer.ToString().ToLower();
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

        public static void Show(string title, string message, NotificationType type = NotificationType.Info, int position = 0)
        {
            // Always show — no focus check needed. We appear over everything including fullscreen.
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].BeginInvoke(new Action(() => Show(title, message, type, position)));
                return;
            }

            var overlay = new NotificationOverlay(title, message, type, position);
            overlay._targetWindow = FindTargetWindow();
            _activeNotifications.Add(overlay);
            overlay.Show();

            // Force topmost immediately so it layers above a fullscreen Minecraft window
            if (overlay.IsHandleCreated)
                SetWindowPos(overlay.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

            overlay._targetOpacity = 0.95;
            RecalculatePositions();
            overlay._timer.Start();
        }

        private static void RecalculatePositions()
        {
            int paddingX = 16;
            int paddingY = 16;
            int spacing = 8;

            _activeNotifications.RemoveAll(n => n.IsDisposed || n._targetOpacity == 0.0 && n.Opacity <= 0.05);

            for (int i = 0; i < _activeNotifications.Count; i++)
            {
                var n = _activeNotifications[i];
                Rectangle bounds = GetTargetBounds(n._targetWindow);

                int x = paddingX;
                int y = paddingY;

                switch (n._positionIndex)
                {
                    case 0: // Bottom Left
                        x = bounds.Left + paddingX;
                        y = bounds.Bottom - paddingY - n.Height - (i * (n.Height + spacing));
                        break;
                    case 1: // Bottom Right
                        x = bounds.Right - paddingX - n.Width;
                        y = bounds.Bottom - paddingY - n.Height - (i * (n.Height + spacing));
                        break;
                    case 2: // Top Left
                        x = bounds.Left + paddingX;
                        y = bounds.Top + paddingY + (i * (n.Height + spacing));
                        break;
                    case 3: // Top Right
                        x = bounds.Right - paddingX - n.Width;
                        y = bounds.Top + paddingY + (i * (n.Height + spacing));
                        break;
                }

                if (n._isInitialPosition)
                {
                    n.Location = new Point(x, y + 12);
                    n._targetY = y;
                    n._isInitialPosition = false;
                }
                else
                {
                    n.Location = new Point(x, n.Location.Y);
                    n._targetY = y;
                }
            }
        }

        private static Rectangle GetTargetBounds(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                RECT windowRect;
                if (GetWindowRect(hwnd, out windowRect))
                {
                    int width = windowRect.Right - windowRect.Left;
                    int height = windowRect.Bottom - windowRect.Top;
                    return new Rectangle(windowRect.Left, windowRect.Top, width, height);
                }
            }

            return Screen.PrimaryScreen.Bounds;
        }

        private static IntPtr FindTargetWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (IsMinecraftLikeWindow(hwnd)) return hwnd;
            return IntPtr.Zero;
        }

        private static bool IsMinecraftLikeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            var title = new System.Text.StringBuilder(256);
            Horimiya.Utils.Win32.GetWindowText(hwnd, title, 256);
            string titleLower = title.ToString().ToLowerInvariant();
            if (titleLower.Contains("minecraft") || titleLower.Contains("lunar") || titleLower.Contains("cheatbreaker") ||
                titleLower.Contains("labymod") || titleLower.Contains("badlion") || titleLower.Contains("feather") ||
                titleLower.Contains("pvplounge") || titleLower.Contains("salwyrr")) return true;

            var cls = new System.Text.StringBuilder(256);
            Horimiya.Utils.Win32.GetClassName(hwnd, cls, 256);
            string classLower = cls.ToString().ToLowerInvariant();
            return classLower.Contains("lwjgl") || classLower.Contains("java") || classLower.Contains("minecraft");
        }

        private void OnTick(object sender, EventArgs e)
        {
            ForceTopMost();
            RecalculatePositions();

            _elapsedMs += _fadeTimerInterval;

            // Slide animation
            if (Location.Y != _targetY)
            {
                int diff = _targetY - Location.Y;
                int step = diff / 4;
                if (Math.Abs(diff) < 4) step = Math.Sign(diff);
                Location = new Point(Location.X, Location.Y + step);
            }

            // Fade in/out
            if (_elapsedMs >= _lifeTimeMs)
            {
                _targetOpacity = 0.0;
            }

            if (Opacity > _targetOpacity)
            {
                Opacity -= 0.04;
                if (Opacity <= 0)
                {
                    Opacity = 0;
                    _timer.Stop();
                    _activeNotifications.Remove(this);
                    RecalculatePositions();
                    this.Close();
                    return;
                }
            }
            else if (Opacity < _targetOpacity)
            {
                Opacity += 0.06;
                if (Opacity >= _targetOpacity)
                {
                    Opacity = _targetOpacity;
                }
            }

            Invalidate(); // To update the progress bar
        }

        private void ForceTopMost()
        {
            if (this.IsHandleCreated)
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int radius = 8;
            var bgRect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var bgPath = RoundedRectangle(bgRect, radius))
            {
                using (var bgBrush = new SolidBrush(Color.FromArgb(240, 5, 6, 12)))
                    g.FillPath(bgBrush, bgPath);
                using (var borderPen = new Pen(Color.FromArgb(140, 122, 51, 216), 1.1f))
                    g.DrawPath(borderPen, bgPath);
            }

            using (var accentPath = RoundedRectangle(new Rectangle(0, 5, 3, Height - 10), 2))
            using (var accentBrush = new SolidBrush(Color.FromArgb(255, 122, 51, 216)))
                g.FillPath(accentBrush, accentPath);

            string titleUpper = _title.ToUpper();
            using (var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(255, 122, 51, 216)))
            {
                g.DrawString(titleUpper, titleFont, titleBrush, new PointF(10f, 7f));
            }

            using (var msgFont = new Font("Segoe UI", 8.8f, FontStyle.Regular))
            using (var msgBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                string msg = _message;
                if (msg.Length > 42) msg = msg.Substring(0, 39) + "...";
                g.DrawString(msg, msgFont, msgBrush, new PointF(10f, 24f));
            }

            float progress = 1f - (float)_elapsedMs / _lifeTimeMs;
            if (progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;

            int barH = 2;
            int barY = Height - barH - 1;
            int barX = 10;
            int barMaxW = Width - 20;
            int barW = (int)(barMaxW * progress);

            if (barW > 0)
            {
                using (var barBrush = new LinearGradientBrush(
                    new Point(barX, barY), new Point(barX + barMaxW, barY),
                    Color.FromArgb(255, 123, 52, 216),
                    Color.FromArgb(90, 70, 30, 140)))
                {
                    g.FillRectangle(barBrush, barX, barY, barW, barH);
                }
            }
        }

        private GraphicsPath RoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int x = rect.X; int y = rect.Y; int w = rect.Width; int h = rect.Height; int r = radius;
            path.AddArc(x, y, r, r, 180, 90);
            path.AddArc(x + w - r, y, r, r, 270, 90);
            path.AddArc(x + w - r, y + h - r, r, r, 0, 90);
            path.AddArc(x, y + h - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}