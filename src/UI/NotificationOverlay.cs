using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using lospoderosos_lite.Config;

namespace lospoderosos_lite.UI
{
    public class NotificationOverlay : Form
    {
        private string _prefix = "AutoClicker";
        private string _status = "OFF";
        private Color _color = Color.Red;
        private System.Windows.Forms.Timer _animTimer;
        private double _targetOpacity = 0.0;
        
        private int _targetX = 0;
        private int _targetY = 0;
        private int _baseY = 0;
        private double _currentY = 0;
        
        private int _lifeTimeMs = 1500;
        private System.Diagnostics.Stopwatch _lifeSw = new System.Diagnostics.Stopwatch();

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

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

        public NotificationOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            TransparencyKey = Color.Black; 
            Opacity = 0.0;
            DoubleBuffered = true;

            Size = new Size(180, 36);
            _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _animTimer.Tick += OnAnimTick;
        }

        public void ShowNotification(string prefix, string status, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowNotification(prefix, status, color)));
                return;
            }
            
            _prefix = prefix;
            _status = status;
            _color = color;
            _targetOpacity = 0.95; 

            // Calculate position based on Config
            var screen = Screen.PrimaryScreen.WorkingArea;
            int margin = 20;
            int pos = AppConfig.Instance.NotificationPosition;
            // 0=Bottom Left, 1=Bottom Right, 2=Top Left, 3=Top Right
            
            if (pos == 0) // Bottom Left
            {
                _targetX = margin;
                _targetY = screen.Height - Height - margin;
                _baseY = screen.Height + margin;
            }
            else if (pos == 1) // Bottom Right
            {
                _targetX = screen.Width - Width - margin;
                _targetY = screen.Height - Height - margin;
                _baseY = screen.Height + margin;
            }
            else if (pos == 2) // Top Left
            {
                _targetX = margin;
                _targetY = margin;
                _baseY = -Height - margin;
            }
            else if (pos == 3) // Top Right
            {
                _targetX = screen.Width - Width - margin;
                _targetY = margin;
                _baseY = -Height - margin;
            }
            
            if (Opacity == 0)
            {
                _currentY = _baseY; 
                Location = new Point(_targetX, (int)_currentY);
            }
            
            _lifeSw.Restart();
            Invalidate();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            if (!_animTimer.Enabled) _animTimer.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            bool active = false;
            
            if (_targetOpacity > 0 && _lifeSw.IsRunning)
            {
                if (_lifeSw.ElapsedMilliseconds > _lifeTimeMs)
                {
                    _targetOpacity = 0.0; // Trigger fade out
                }
                active = true;
            }

            if (Opacity > _targetOpacity)
            {
                Opacity -= 0.06;
                if (Opacity <= 0) Opacity = 0;
                active = true;
            }
            else if (Opacity < _targetOpacity)
            {
                Opacity += 0.1;
                if (Opacity >= _targetOpacity) Opacity = _targetOpacity;
                active = true;
            }

            double tgtY = (_targetOpacity > 0) ? _targetY : _baseY;
            if (Math.Abs(_currentY - tgtY) > 0.5)
            {
                _currentY += (tgtY - _currentY) * 0.3; 
                Location = new Point(_targetX, (int)_currentY);
                active = true;
            }

            if (!active && Opacity == 0)
            {
                _animTimer.Stop();
                _lifeSw.Stop();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Make background completely clear (using TransparencyKey)
            e.Graphics.Clear(Color.Black);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = 4; // slight rounding
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();

            // Very dark, almost black background (like the image)
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(240, 20, 20, 20)))
            {
                e.Graphics.FillPath(bgBrush, path);
            }

            // Right accent bar (Status color)
            RectangleF rightBar = new RectangleF(Width - 4, 0, 4, Height - 1);
            GraphicsPath rightPath = new GraphicsPath();
            rightPath.AddLine(rightBar.X, rightBar.Y, rightBar.Right - radius, rightBar.Y);
            rightPath.AddArc(rightBar.Right - radius * 2, rightBar.Y, radius * 2, radius * 2, 270, 90);
            rightPath.AddArc(rightBar.Right - radius * 2, rightBar.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            rightPath.AddLine(rightBar.Right - radius, rightBar.Bottom, rightBar.X, rightBar.Bottom);
            rightPath.CloseFigure();
            using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(255, _color)))
            {
                e.Graphics.FillPath(accentBrush, rightPath);
            }

            // Sans-serif Font (Segoe UI) to match the provided image
            using (Font fBold = new Font("Segoe UI", 12f, FontStyle.Bold))
            {
                // Draw Prefix (AutoClicker)
                int prefixW = TextRenderer.MeasureText(_prefix, fBold).Width;
                TextRenderer.DrawText(e.Graphics, _prefix, fBold, new Point(12, (Height - fBold.Height) / 2), Color.FromArgb(220, 220, 220));

                // Draw Status (ON/OFF)
                TextRenderer.DrawText(e.Graphics, _status, fBold, new Point(12 + prefixW - 4, (Height - fBold.Height) / 2), _color);
                
                // Draw underline under the prefix and status
                int totalW = prefixW - 4 + TextRenderer.MeasureText(_status, fBold).Width;
                using (SolidBrush ulBrush = new SolidBrush(Color.FromArgb(150, _color)))
                {
                    e.Graphics.FillRectangle(ulBrush, new RectangleF(12, Height - 6, totalW - 10, 2));
                }
            }
        }
    }
}
