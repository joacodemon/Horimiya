using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace lospoderosos_lite.UI
{
    public class NotificationOverlay : Form
    {
        private Label _lblMsg;
        private Label _lblIcon;
        private System.Windows.Forms.Timer _fadeTimer;
        private double _targetOpacity = 0.0;
        private NotificationType _currentType = NotificationType.Info;
        private int _cornerRadius = 12;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00000020 | 0x00000008 | 0x08000000 | 0x00000080;
                return cp;
            }
        }

        public enum NotificationType
        {
            Success,
            Error,
            Warning,
            Info
        }

        public NotificationOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(20, 20, 20);
            Opacity = 0.0;
            DoubleBuffered = true;

            var screen = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(320, 70);
            Location = new Point(20, screen.Height - 100);

            // Panel contenedor con fondo
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(15, 12, 15, 12)
            };

            // Label para icono
            _lblIcon = new Label
            {
                AutoSize = false,
                Size = new Size(40, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                Dock = DockStyle.Left
            };
            panel.Controls.Add(_lblIcon);

            // Label para mensaje
            _lblMsg = new Label
            {
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 0, 0),
                AutoSize = false
            };
            panel.Controls.Add(_lblMsg);

            Controls.Add(panel);

            _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _fadeTimer.Tick += OnFadeTick;

            Paint += NotificationOverlay_Paint;
        }

        private void NotificationOverlay_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            // Dibujar fondo redondeado con gradiente
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), _cornerRadius))
            {
                // Fondo gradiente
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Point(0, 0), 
                    new Point(0, Height),
                    GetBackgroundColor(_currentType),
                    GetDarkBackgroundColor(_currentType)))
                {
                    e.Graphics.FillPath(brush, path);
                }

                // Borde
                using (Pen pen = new Pen(GetBorderColor(_currentType), 1.5f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            // Línea izquierda de acento
            using (Pen accentPen = new Pen(GetAccentColor(_currentType), 4f))
            {
                e.Graphics.DrawLine(accentPen, 0, 0, 0, Height);
            }
        }

        private GraphicsPath RoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int x = rect.X;
            int y = rect.Y;
            int w = rect.Width;
            int h = rect.Height;
            int r = radius;

            path.AddArc(x, y, r, r, 180, 90);
            path.AddArc(x + w - r, y, r, r, 270, 90);
            path.AddArc(x + w - r, y + h - r, r, r, 0, 90);
            path.AddArc(x, y + h - r, r, r, 90, 90);
            path.CloseFigure();

            return path;
        }

        public void ShowNotification(string message, NotificationType type = NotificationType.Info)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowNotification(message, type)));
                return;
            }

            _currentType = type;
            _lblMsg.Text = message;
            _lblMsg.ForeColor = Color.White;
            _lblIcon.Text = GetIcon(type);
            _lblIcon.ForeColor = GetAccentColor(type);

            Opacity = 0.95;
            _targetOpacity = 0.95;

            if (!_fadeTimer.Enabled) _fadeTimer.Start();

            // Fade out después de 3 segundos
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 3000 };
            t.Tick += (s, e) => { _targetOpacity = 0.0; t.Stop(); t.Dispose(); };
            t.Start();

            Invalidate();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (Opacity > _targetOpacity)
            {
                Opacity -= 0.04;
                if (Opacity <= 0)
                {
                    Opacity = 0;
                    _fadeTimer.Stop();
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
        }

        private Color GetBackgroundColor(NotificationType type) => type switch
        {
            NotificationType.Success => Color.FromArgb(30, 65, 50),
            NotificationType.Error => Color.FromArgb(70, 30, 30),
            NotificationType.Warning => Color.FromArgb(70, 55, 25),
            _ => Color.FromArgb(30, 45, 70)
        };

        private Color GetDarkBackgroundColor(NotificationType type) => type switch
        {
            NotificationType.Success => Color.FromArgb(20, 50, 40),
            NotificationType.Error => Color.FromArgb(55, 20, 20),
            NotificationType.Warning => Color.FromArgb(55, 43, 18),
            _ => Color.FromArgb(20, 35, 60)
        };

        private Color GetBorderColor(NotificationType type) => type switch
        {
            NotificationType.Success => Color.FromArgb(80, 200, 120),
            NotificationType.Error => Color.FromArgb(220, 100, 100),
            NotificationType.Warning => Color.FromArgb(255, 180, 80),
            _ => Color.FromArgb(100, 150, 200)
        };

        private Color GetAccentColor(NotificationType type) => type switch
        {
            NotificationType.Success => Color.FromArgb(100, 220, 150),
            NotificationType.Error => Color.FromArgb(255, 120, 120),
            NotificationType.Warning => Color.FromArgb(255, 200, 100),
            _ => Color.FromArgb(120, 180, 255)
        };

        private string GetIcon(NotificationType type) => type switch
        {
            NotificationType.Success => "✓",
            NotificationType.Error => "✕",
            NotificationType.Warning => "⚠",
            _ => "ℹ"
        };
    }
}