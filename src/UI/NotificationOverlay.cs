using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using lospoderosos_lite.Config;

namespace lospoderosos_lite.UI
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
        private System.Windows.Forms.Timer _timer;
        private double _targetOpacity = 0.0;
        private int _lifeTimeMs = 3000;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_HWNDPARENT = -8;

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

            Size = new Size(240, 50);

            _timer = new System.Windows.Forms.Timer { Interval = _fadeTimerInterval };
            _timer.Tick += OnTick;
        }

        public static void Show(string title, string message, NotificationType type = NotificationType.Info, int position = 0)
        {
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].BeginInvoke(new Action(() => Show(title, message, type, position)));
                return;
            }

            var overlay = new NotificationOverlay(title, message, type, position);
            _activeNotifications.Add(overlay);
            overlay.Show();
            
            // Attach to the current foreground window
            IntPtr fgWindow = GetForegroundWindow();
            if (fgWindow != IntPtr.Zero && fgWindow != overlay.Handle)
            {
                if (IntPtr.Size == 8)
                    SetWindowLongPtr(overlay.Handle, GWL_HWNDPARENT, fgWindow);
                else
                    SetWindowLong(overlay.Handle, GWL_HWNDPARENT, fgWindow.ToInt32());
            }

            overlay._targetOpacity = 0.95;
            RecalculatePositions();
            overlay._timer.Start();
        }

        private static void RecalculatePositions()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            int paddingX = 20;
            int paddingY = 20;
            int spacing = 10;

            _activeNotifications.RemoveAll(n => n.IsDisposed || n._targetOpacity == 0.0 && n.Opacity <= 0.05);

            for (int i = 0; i < _activeNotifications.Count; i++)
            {
                var n = _activeNotifications[i];
                int x = paddingX;
                int y = paddingY;

                switch (n._positionIndex)
                {
                    case 0: // Bottom Left
                        x = paddingX;
                        y = screen.Height - paddingY - n.Height - (i * (n.Height + spacing));
                        break;
                    case 1: // Bottom Right
                        x = screen.Width - paddingX - n.Width;
                        y = screen.Height - paddingY - n.Height - (i * (n.Height + spacing));
                        break;
                    case 2: // Top Left
                        x = paddingX;
                        y = paddingY + (i * (n.Height + spacing));
                        break;
                    case 3: // Top Right
                        x = screen.Width - paddingX - n.Width;
                        y = paddingY + (i * (n.Height + spacing));
                        break;
                }

                if (n._isInitialPosition)
                {
                    // Slide up slightly
                    n.Location = new Point(x, y + 20);
                    n._targetY = y;
                    n._isInitialPosition = false;
                }
                else
                {
                    n.Location = new Point(x, n.Location.Y); // Always enforce the correct X position
                    n._targetY = y;
                }
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            ForceTopMost();

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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Background
            int radius = 4;
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(20, 20, 20))) 
                {
                    e.Graphics.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(40, 40, 40), 1f))
                {
                    e.Graphics.DrawPath(borderPen, path);
                }
            }

            // Texts
            using (Font titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(Color.White))
            {
                e.Graphics.DrawString(_title, titleFont, titleBrush, new PointF(12, 6));
            }

            using (Font msgFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            using (SolidBrush msgBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            {
                e.Graphics.DrawString(_message, msgFont, msgBrush, new PointF(12, 22));
            }

            // Progress bar
            int barHeight = 2;
            int barY = Height - barHeight;
            float progress = 1f - (float)_elapsedMs / _lifeTimeMs;
            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;

            int barWidth = (int)((Width - 24) * progress); 

            using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(139, 92, 246))) // Purple
            {
                e.Graphics.FillRectangle(barBrush, 12, barY - 4, barWidth, barHeight);
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