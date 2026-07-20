using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Horimiya.Auth;

namespace Horimiya.UI
{
    /// <summary>
    /// Login / license authentication form shown before the main UI.
    /// Uses the same dark aesthetic as the rest of the application.
    /// </summary>
    public class LoginForm : Form
    {
        // ── Colors ───────────────────────────────────────────────────────────
        static readonly Color C_BG       = Color.FromArgb(10,  10,  10);
        static readonly Color C_PANEL    = Color.FromArgb(18,  18,  18);
        static readonly Color C_BORDER   = Color.FromArgb(45,  45,  45);
        static readonly Color C_SEP      = Color.FromArgb(35,  35,  35);
        static readonly Color C_TXT      = Color.FromArgb(230, 230, 230);
        static readonly Color C_DIM      = Color.FromArgb(110, 110, 110);
        static readonly Color C_GREEN    = Color.FromArgb(80,  200, 100);
        static readonly Color C_RED      = Color.FromArgb(220, 70,  70);
        static readonly Color C_ACCENT   = Color.FromArgb(140, 60,  220);  // purple
        static readonly Color C_ACCENT2  = Color.FromArgb(80,  140, 255);  // blue

        // ── Fonts ────────────────────────────────────────────────────────────
        static readonly Font F_TITLE  = new Font("Courier New", 14f, FontStyle.Bold);
        static readonly Font F_SUB    = new Font("Courier New", 7.5f);
        static readonly Font F_LABEL  = new Font("Courier New", 7.5f, FontStyle.Bold);
        static readonly Font F_SMALL  = new Font("Courier New", 6.5f);
        static readonly Font F_INPUT  = new Font("Courier New", 9f, FontStyle.Bold);
        static readonly Font F_BTN    = new Font("Courier New", 8.5f, FontStyle.Bold);

        // ── Controls ─────────────────────────────────────────────────────────
        private TextBox    _txtKey;
        private Button     _btnAuth;
        private Label      _lblStatus;
        private Label      _lblHwid;
        private Button     _btnClose;
        private Button     _btnCopyHwid;

        // ── State ─────────────────────────────────────────────────────────────
        private bool   _authenticating = false;
        private int    _dotCount       = 0;
        private System.Windows.Forms.Timer _dotTimer;
        private Image  _logo;

        // ── Drag ─────────────────────────────────────────────────────────────
        private bool  _dragging;
        private Point _dragStart;

        public LoginForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterScreen;
            Size            = new Size(480, 400);
            BackColor       = C_BG;
            ShowInTaskbar   = true;
            TopMost         = true;
            Text            = "Horimiya — Authentication";

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint            |
                     ControlStyles.DoubleBuffer, true);

            LoadLogo();
            BuildControls();
            SetupDotTimer();
        }

        // ── Logo Loading ─────────────────────────────────────────────────────

        private void LoadLogo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("ImGuiApp.Resources.logo.png"))
                    if (s != null) _logo = new Bitmap(s);
            }
            catch { }

            if (_logo == null)
            {
                try
                {
                    string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                    if (File.Exists(p))
                        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                        using (var tmp = Image.FromStream(fs))
                            _logo = new Bitmap(tmp);
                }
                catch { }
            }
        }

        // ── Control Construction ─────────────────────────────────────────────

        private void BuildControls()
        {
            // ── Close button (top-right) ──
            _btnClose = new Button
            {
                Text      = "✕",
                Size      = new Size(28, 24),
                Location  = new Point(Width - 34, 6),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Courier New", 9f, FontStyle.Bold),
                ForeColor = C_DIM,
                BackColor = Color.Transparent,
                Cursor    = Cursors.Hand,
                TabStop   = false
            };
            _btnClose.FlatAppearance.BorderSize   = 0;
            _btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 220, 60, 60);
            _btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_btnClose);

            // ── License Key label ──
            var lblKey = new Label
            {
                Text      = "LICENSE KEY",
                Font      = F_LABEL,
                ForeColor = C_DIM,
                BackColor = Color.Transparent,
                Location  = new Point(60, 228),
                AutoSize  = true
            };
            Controls.Add(lblKey);

            // ── License Key TextBox ──
            _txtKey = new TextBox
            {
                Location     = new Point(60, 246),
                Size         = new Size(360, 26),
                Font         = F_INPUT,
                BackColor    = Color.FromArgb(22, 22, 22),
                ForeColor    = C_DIM,
                BorderStyle  = BorderStyle.FixedSingle,
                Text         = "HMRYA-XXXXX-XXXXX-XXXXX-XXXXX",
                MaxLength    = 30,
                CharacterCasing = CharacterCasing.Upper,
                TabIndex     = 0
            };
            // Simulate placeholder text for .NET 4.8
            _txtKey.GotFocus += (s, ev) =>
            {
                if (_txtKey.Text == "HMRYA-XXXXX-XXXXX-XXXXX-XXXXX")
                {
                    _txtKey.Text = "";
                    _txtKey.ForeColor = C_TXT;
                }
            };
            _txtKey.LostFocus += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(_txtKey.Text))
                {
                    _txtKey.Text = "HMRYA-XXXXX-XXXXX-XXXXX-XXXXX";
                    _txtKey.ForeColor = C_DIM;
                }
            };
            _txtKey.KeyDown += TxtKey_KeyDown;
            Controls.Add(_txtKey);

            // ── Authenticate button ──
            _btnAuth = new Button
            {
                Text      = "AUTHENTICATE",
                Size      = new Size(360, 36),
                Location  = new Point(60, 286),
                FlatStyle = FlatStyle.Flat,
                Font      = F_BTN,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(100, 40, 180),
                Cursor    = Cursors.Hand,
                TabIndex  = 1
            };
            _btnAuth.FlatAppearance.BorderColor            = Color.FromArgb(130, 60, 220);
            _btnAuth.FlatAppearance.BorderSize             = 1;
            _btnAuth.FlatAppearance.MouseOverBackColor     = Color.FromArgb(120, 55, 210);
            _btnAuth.FlatAppearance.MouseDownBackColor     = Color.FromArgb(80, 25, 150);
            _btnAuth.Click += BtnAuth_Click;
            Controls.Add(_btnAuth);

            // ── Status label ──
            _lblStatus = new Label
            {
                Text      = "",
                Font      = F_SMALL,
                ForeColor = C_DIM,
                BackColor = Color.Transparent,
                Location  = new Point(60, 332),
                Size      = new Size(360, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_lblStatus);

            // ── HWID section ──
            var lblHwidTitle = new Label
            {
                Text      = "YOUR HWID",
                Font      = F_SMALL,
                ForeColor = Color.FromArgb(60, 60, 60),
                BackColor = Color.Transparent,
                Location  = new Point(60, 356),
                AutoSize  = true
            };
            Controls.Add(lblHwidTitle);

            _lblHwid = new Label
            {
                Text      = "Calculating...",
                Font      = F_SMALL,
                ForeColor = Color.FromArgb(55, 55, 55),
                BackColor = Color.Transparent,
                Location  = new Point(60, 368),
                Size      = new Size(300, 16)
            };
            Controls.Add(_lblHwid);

            _btnCopyHwid = new Button
            {
                Text      = "Copy",
                Size      = new Size(42, 16),
                Location  = new Point(366, 367),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Courier New", 6f),
                ForeColor = Color.FromArgb(60, 60, 60),
                BackColor = Color.Transparent,
                Cursor    = Cursors.Hand,
                TabStop   = false
            };
            _btnCopyHwid.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
            _btnCopyHwid.FlatAppearance.BorderSize  = 1;
            _btnCopyHwid.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_lblHwid.Text);
                    _btnCopyHwid.Text = "✓";
                    var t = new System.Windows.Forms.Timer { Interval = 1200 };
                    t.Tick += (ts, te) => { _btnCopyHwid.Text = "Copy"; t.Stop(); t.Dispose(); };
                    t.Start();
                }
                catch { }
            };
            Controls.Add(_btnCopyHwid);

            // Load HWID async so WMI doesn't freeze the form
            new Thread(() =>
            {
                string hwid = HwidGenerator.GetHwid();
                if (!IsDisposed)
                    Invoke((Action)(() => _lblHwid.Text = hwid));
            }) { IsBackground = true }.Start();

            // Wire drag to the form itself
            MouseDown += FormMouseDown;
            MouseMove += FormMouseMove;
            MouseUp   += FormMouseUp;
        }

        private void SetupDotTimer()
        {
            _dotTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _dotTimer.Tick += (s, e) =>
            {
                if (!_authenticating) return;
                _dotCount = (_dotCount + 1) % 4;
                _btnAuth.Text = "AUTHENTICATING" + new string('.', _dotCount);
            };
        }

        // ── Authentication Logic ─────────────────────────────────────────────

        private void TxtKey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                BtnAuth_Click(sender, EventArgs.Empty);
            }
        }

        private void BtnAuth_Click(object sender, EventArgs e)
        {
            if (_authenticating) return;

            string key = _txtKey.Text.Trim();
            if (string.IsNullOrEmpty(key) || key == "HMRYA-XXXXX-XXXXX-XXXXX-XXXXX")
            {
                SetStatus("Please enter your license key.", C_RED);
                return;
            }

            // Basic key format check: HMRYA-XXXXX-XXXXX-XXXXX-XXXXX
            if (!key.StartsWith("HMRYA-") || key.Length < 14)
            {
                SetStatus("Invalid key format. Keys start with HMRYA-", C_RED);
                return;
            }

            StartAuthAnimation();

            new Thread(() =>
            {
                var result = AuthManager.Authenticate(key);
                if (!IsDisposed)
                    Invoke((Action)(() => HandleResult(result)));
            }) { IsBackground = true }.Start();
        }

        private void HandleResult(AuthResult result)
        {
            StopAuthAnimation();

            if (result.Success)
            {
                // Show success briefly then close
                _btnAuth.BackColor = Color.FromArgb(40, 160, 70);
                _btnAuth.FlatAppearance.BorderColor = Color.FromArgb(60, 200, 90);
                _btnAuth.Text = "✓  AUTHENTICATED";
                _txtKey.Enabled = false;

                // Save key to a dedicated file next to the exe (simple and reliable)
                try
                {
                    string keyPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "license.key");
                    System.IO.File.WriteAllText(keyPath, _txtKey.Text.Trim());
                } catch { }

                // Also save to config so it syncs with AppData
                var existingCfg = Horimiya.Config.AppConfig.Instance ?? Horimiya.Config.AppConfig.Load("default");
                existingCfg.LicenseKey = _txtKey.Text.Trim();
                existingCfg.Save("default");

                string welcome = $"Welcome, {result.Username}  ·  {result.LicenseTypeDisplay}";
                if (!result.IsLifetime)
                    welcome += $"  ·  Expires {result.ExpiryDisplay}";
                if (result.HwidBound)
                    welcome += "  ·  HWID registered";

                SetStatus(welcome, C_GREEN);

                // Short delay then proceed
                var timer = new System.Windows.Forms.Timer { Interval = 1200 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    DialogResult = DialogResult.OK;
                    Close();
                };
                timer.Start();
            }
            else
            {
                // Shake the form
                ShakeForm();
                _btnAuth.BackColor = Color.FromArgb(100, 40, 180);
                _btnAuth.Text = "AUTHENTICATE";
                SetStatus(result.Message, C_RED);
            }
        }

        // ── Animations ───────────────────────────────────────────────────────

        private void StartAuthAnimation()
        {
            _authenticating = true;
            _btnAuth.Enabled = false;
            _btnAuth.BackColor = Color.FromArgb(70, 30, 140);
            _dotCount = 0;
            _dotTimer.Start();
            SetStatus("Contacting server...", C_DIM);
        }

        private void StopAuthAnimation()
        {
            _authenticating = false;
            _dotTimer.Stop();
            _btnAuth.Enabled = true;
        }

        private void ShakeForm()
        {
            var orig = Location;
            int[] offsets = { -6, 6, -5, 5, -3, 3, -1, 1, 0 };
            var t = new System.Windows.Forms.Timer { Interval = 30 };
            int idx = 0;
            t.Tick += (s, e) =>
            {
                if (idx < offsets.Length)
                    Location = new Point(orig.X + offsets[idx++], orig.Y);
                else
                {
                    Location = orig;
                    t.Stop();
                    t.Dispose();
                }
            };
            t.Start();
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text      = text;
            _lblStatus.ForeColor = color;
        }

        // ── Custom Painting ──────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
            g.Clear(C_BG);

            // Outer border
            using (var p = new Pen(C_BORDER))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            // Inner accent border (top)
            using (var br = new LinearGradientBrush(
                new Point(0, 0), new Point(Width, 0),
                C_ACCENT, C_ACCENT2))
            using (var p = new Pen(br, 2f))
                g.DrawLine(p, 1, 1, Width - 2, 1);

            // Top header background
            using (var br = new SolidBrush(C_PANEL))
                g.FillRectangle(br, 1, 2, Width - 2, 200);

            // Separator below header
            using (var p = new Pen(C_SEP))
                g.DrawLine(p, 30, 202, Width - 30, 202);

            // Logo
            int logoSize = 72;
            int logoX    = (Width - logoSize) / 2;
            int logoY    = 22;
            if (_logo != null)
            {
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(logoX, logoY, logoSize, logoSize);
                    g.SetClip(path);
                    float scale = Math.Max((float)logoSize / _logo.Width, (float)logoSize / _logo.Height);
                    int dw = (int)(_logo.Width  * scale);
                    int dh = (int)(_logo.Height * scale);
                    int dx = logoX + (logoSize - dw) / 2;
                    int dy = logoY + (logoSize - dh) / 2;
                    g.DrawImage(_logo, dx, dy, dw, dh);
                    g.ResetClip();
                }
                using (var p = new Pen(Color.FromArgb(70, 70, 70), 1.5f))
                    g.DrawEllipse(p, logoX, logoY, logoSize, logoSize);
            }

            // Title
            string title = "HORIMIYA";
            var tsz = g.MeasureString(title, F_TITLE);
            using (var br = new SolidBrush(C_TXT))
                g.DrawString(title, F_TITLE, br, (Width - tsz.Width) / 2, logoY + logoSize + 10);

            // Subtitle
            string sub = "License Authentication";
            var ssz = g.MeasureString(sub, F_SUB);
            using (var br = new SolidBrush(C_DIM))
                g.DrawString(sub, F_SUB, br, (Width - ssz.Width) / 2, logoY + logoSize + 34);

            // Author line
            string auth = "by joacodemon";
            var asz = g.MeasureString(auth, F_SMALL);
            using (var br = new SolidBrush(Color.FromArgb(60, 60, 60)))
                g.DrawString(auth, F_SMALL, br, (Width - asz.Width) / 2, logoY + logoSize + 52);

            // Bottom separator
            using (var p = new Pen(C_SEP))
                g.DrawLine(p, 30, Height - 40, Width - 30, Height - 40);
        }

        // ── Drag Support ─────────────────────────────────────────────────────

        private void FormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < 210)
            {
                _dragging  = true;
                _dragStart = e.Location;
            }
        }

        private void FormMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
                Location = new Point(
                    Location.X + e.X - _dragStart.X,
                    Location.Y + e.Y - _dragStart.Y);
        }

        private void FormMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        // ── Cleanup ──────────────────────────────────────────────────────────

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _dotTimer?.Stop();
            _dotTimer?.Dispose();
            _logo?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
