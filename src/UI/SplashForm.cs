using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
// OpenTK using directives removed (not available)

namespace lospoderosos_lite.UI
{
    public class SplashForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private float _opacity = 0f;
        private int _phase = 0; // 0=fade in, 1=hold, 2=fade out
        private int _holdTicks = 0;
        private Image _logo;
        // private GLControl _glControl; // Removed

        private static readonly Font FNTBIG   = new Font("Courier New", 18F, FontStyle.Bold);
        private static readonly Font FNTSUB   = new Font("Courier New", 9F);
        private static readonly Font FNTSMALL = new Font("Courier New", 7F);

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterScreen;
            Size            = new Size(420, 340);
            BackColor       = Color.FromArgb(10, 10, 10);
            ShowInTaskbar   = false;
            TopMost         = true;
            Opacity         = 0;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);
            try
            {
                string imgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                if (System.IO.File.Exists(imgPath))
                {
                    using (var fs = new System.IO.FileStream(imgPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                    using (Image temp = Image.FromStream(fs))
                    {
                        _logo = new Bitmap(temp);
                    }
                }
            }
            catch { }
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 25;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_phase == 0) // Fade in
            {
                _opacity += 0.06f;
                if (_opacity >= 1f) { _opacity = 1f; _phase = 1; }
                Opacity = _opacity;
                Invalidate();
            }
            else if (_phase == 1) // Hold
            {
                _holdTicks++;
                if (_holdTicks > 80) _phase = 2; // ~2 seconds hold
            }
            else // Fade out
            {
                _opacity -= 0.05f;
                if (_opacity <= 0f) { _opacity = 0f; _timer.Stop(); Close(); return; }
                Opacity = _opacity;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.FromArgb(10, 10, 10));

            // Border
            using (var p = new Pen(Color.FromArgb(50, 50, 50)))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            // Logo image
            if (_logo != null)
            {
                int imgSize = 140;
                int imgX = (Width - imgSize) / 2;
                int imgY = 30;

                // Draw circular clip of the image
                // _glControl = new GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 4)); // Removed
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(imgX, imgY, imgSize, imgSize);
                    g.SetClip(path);
                    
                    // Scale and center the image within the circle
                    float scale = Math.Max((float)imgSize / _logo.Width, (float)imgSize / _logo.Height);
                    int drawW = (int)(_logo.Width * scale);
                    int drawH = (int)(_logo.Height * scale);
                    int drawX = imgX + (imgSize - drawW) / 2;
                    int drawY = imgY + (imgSize - drawH) / 2;
                    g.DrawImage(_logo, drawX, drawY, drawW, drawH);
                    g.ResetClip();
                }

                // Circle border
                using (var p = new Pen(Color.FromArgb(80, 80, 80), 2))
                    g.DrawEllipse(p, imgX, imgY, imgSize, imgSize);
            }

            // Title
            string title = "los poderosos";
            var titleSize = g.MeasureString(title, FNTBIG);
            using (var b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                g.DrawString(title, FNTBIG, b, (Width - titleSize.Width) / 2, 185);

            // Subtitle
            string sub = "by joacodemon";
            var subSize = g.MeasureString(sub, FNTSUB);
            using (var b = new SolidBrush(Color.FromArgb(130, 130, 130)))
                g.DrawString(sub, FNTSUB, b, (Width - subSize.Width) / 2, 215);

            // Separator
            using (var p = new Pen(Color.FromArgb(50, 50, 50)))
                g.DrawLine(p, 60, 245, Width - 60, 245);

            // Loading text
            string loadTxt = "Cargando modulos...";
            var loadSize = g.MeasureString(loadTxt, FNTSMALL);
            using (var b = new SolidBrush(Color.FromArgb(80, 80, 80)))
                g.DrawString(loadTxt, FNTSMALL, b, (Width - loadSize.Width) / 2, 258);

            // Version removed
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_logo != null) _logo.Dispose();
            base.OnFormClosed(e);
        }
    }
}
