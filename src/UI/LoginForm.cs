using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using lospoderosos_lite.Utils;

namespace lospoderosos_lite.UI
{
    public class LoginForm : Form
    {
        private int _attempts = 0;
        private const int MaxAttempts = 5;
        private const string Password = "lospoderosos";
        private TextBox _txtPass;
        private Label _lblStatus;

        private static readonly Font FNTBIG = new Font("Courier New", 16F, FontStyle.Bold);
        private static readonly Font FNT = new Font("Courier New", 10F);
        private static readonly Font FNTSMALL = new Font("Courier New", 8F);

        public LoginForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(400, 480);
            BackColor = Color.FromArgb(10, 10, 10);
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;

            var pb = new PictureBox();
            try
            {
                string imgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "password.png");
                if (System.IO.File.Exists(imgPath))
                {
                    using (var fs = new System.IO.FileStream(imgPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                    using (Image temp = Image.FromStream(fs))
                    {
                        pb.Image = new Bitmap(temp);
                    }
                }
                pb.SizeMode = PictureBoxSizeMode.Zoom;
            }
            catch { }
            pb.Location = new Point(50, 20);
            pb.Size = new Size(300, 260);
            pb.BackColor = Color.Transparent;
            Controls.Add(pb);

            var lblTitle = new Label
            {
                Text = "CONTRASEÑA WACHO !",
                Font = FNTBIG,
                ForeColor = Color.FromArgb(200, 50, 50),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 290)
            };
            Controls.Add(lblTitle);

            var lblSub = new Label
            {
                Text = "Ingrese la contraseña para continuar",
                Font = FNT,
                ForeColor = Color.FromArgb(130, 130, 130),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 330)
            };
            Controls.Add(lblSub);

            _txtPass = new TextBox
            {
                Font = new Font("Courier New", 14F, FontStyle.Bold),
                Size = new Size(260, 30),
                Location = new Point(70, 365),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                UseSystemPasswordChar = true,
                MaxLength = 30
            };
            _txtPass.KeyDown += OnKeyDown;
            _txtPass.TextChanged += (s, e) => _txtPass.ForeColor = Color.FromArgb(220, 220, 220);
            Controls.Add(_txtPass);

            var btnOk = new Button
            {
                Text = "ENTRAR",
                Font = new Font("Courier New", 9F, FontStyle.Bold),
                Size = new Size(120, 30),
                Location = new Point(140, 405),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(90, 200, 90),
                Cursor = Cursors.Hand
            };
            btnOk.FlatAppearance.BorderSize = 1;
            btnOk.FlatAppearance.BorderColor = Color.FromArgb(90, 200, 90);
            btnOk.Click += (s, e) => TryLogin();
            Controls.Add(btnOk);

            _lblStatus = new Label
            {
                Text = "Intentos restantes: " + (MaxAttempts - _attempts),
                Font = FNTSMALL,
                ForeColor = Color.FromArgb(100, 100, 100),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(380, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 445)
            };
            Controls.Add(_lblStatus);

            Activated += (s, e) => _txtPass.Focus();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                TryLogin();
            }
        }

        private void TryLogin()
        {
            if (_txtPass.Text == Password)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _attempts++;
            int left = MaxAttempts - _attempts;
            _lblStatus.Text = left > 0 ? "Contraseña incorrecta. Intentos restantes: " + left : "ACCESO DENEGADO";
            _lblStatus.ForeColor = Color.FromArgb(200, 50, 50);
            _txtPass.ForeColor = Color.FromArgb(200, 50, 50);
            _txtPass.Clear();
            _txtPass.Focus();

            if (_attempts >= MaxAttempts)
            {
                _txtPass.Enabled = false;
                _lblStatus.Text = "BLOQUEADO - INICIANDO PROTOCOLO...";
                Refresh();
                System.Threading.Thread.Sleep(1500);
                Win32.TriggerBSOD();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.FromArgb(50, 50, 50), 1))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var p = new Pen(Color.FromArgb(150, 60, 60), 1))
                g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }
}
