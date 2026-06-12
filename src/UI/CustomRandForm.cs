using System;
using System.Drawing;
using System.Windows.Forms;
using lospoderosos_lite.Config;
using lospoderosos_lite.Utils;
namespace lospoderosos_lite.UI
{
    public class CustomRandForm : Form
    {
        private AppConfig _cfg;
        private Color _accent;
        private Color BG = Color.FromArgb(10, 10, 10);
        private Color PBG = Color.FromArgb(20, 20, 20);
        private Color TXT = Color.FromArgb(240, 240, 240);
        private Color DIM = Color.FromArgb(130, 130, 130);
        private Font FNT = new Font("Courier New", 8F);
        private Font FNTB = new Font("Courier New", 8.5F, FontStyle.Bold);

        private bool _isDragging = false;
        private Panel _chartPanel;
        private Label _lblStats;

        public CustomRandForm(AppConfig cfg, Color accent)
        {
            _cfg = cfg;
            _accent = accent;
            
            Text = "Manual Randomization Editor";
            Size = new Size(650, 400);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BG;
            DoubleBuffered = true;

            BuildUI();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (var path = RoundedRect(0, 0, Width, Height, 12))
                Region = new Region(path);
        }

        private System.Drawing.Drawing2D.GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(x, y, r, r, 180, 90);
            path.AddArc(x + w - r, y, r, r, 270, 90);
            path.AddArc(x + w - r, y + h - r, r, r, 0, 90);
            path.AddArc(x, y + h - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void BuildUI()
        {
            // Title Bar
            var tb = new Panel { BackColor = Color.FromArgb(8, 8, 8), Bounds = new Rectangle(0, 0, Width, 26) };
            tb.Paint += (s, e) => {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.FromArgb(150, _accent), 1))
                    e.Graphics.DrawLine(p, 0, 25, tb.Width, 25);
            };
            var title = new Label { Text = "Manual Randomization Editor", ForeColor = DIM, Font = FNT, Location = new Point(10, 7), AutoSize = true };
            var btnClose = new Button { Text = "X", ForeColor = DIM, Font = FNT, Bounds = new Rectangle(Width - 26, 0, 26, 26), FlatStyle = FlatStyle.Flat };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            
            tb.Controls.Add(title);
            tb.Controls.Add(btnClose);
            
            tb.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { Utils.Win32.ReleaseCapture(); Utils.Win32.SendMessage(Handle, 0xA1, 2, 0); } };
            title.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { Utils.Win32.ReleaseCapture(); Utils.Win32.SendMessage(Handle, 0xA1, 2, 0); } };

            // Chart Area
            _chartPanel = new Panel { Bounds = new Rectangle(20, 40, 610, 200), BackColor = PBG };
            _chartPanel.Paint += DrawChart;
            _chartPanel.MouseDown += ChartMouseDown;
            _chartPanel.MouseMove += ChartMouseMove;
            _chartPanel.MouseUp += ChartMouseUp;

            // Stats Area
            _lblStats = new Label { ForeColor = TXT, Font = FNT, Location = new Point(20, 250), AutoSize = true };
            UpdateStats();

            // Buttons
            var btnReset = new FlatButton("Reset All", TXT) { Bounds = new Rectangle(20, 350, 100, 30) };
            btnReset.Click += (s, e) => {
                for (int i = 0; i < 25; i++) _cfg.CustomCpsWeights[i] = 0;
                _chartPanel.Invalidate();
                UpdateStats();
            };

            var btnSmooth = new FlatButton("Smooth", TXT) { Bounds = new Rectangle(130, 350, 100, 30) };
            btnSmooth.Click += (s, e) => {
                double[] temp = new double[25];
                for (int i = 0; i < 25; i++) {
                    double sum = _cfg.CustomCpsWeights[i];
                    int count = 1;
                    if (i > 0) { sum += _cfg.CustomCpsWeights[i - 1]; count++; }
                    if (i < 24) { sum += _cfg.CustomCpsWeights[i + 1]; count++; }
                    temp[i] = sum / count;
                }
                _cfg.CustomCpsWeights = temp;
                _chartPanel.Invalidate();
                UpdateStats();
            };
            
            var btnDone = new FlatButton("Save & Close", TXT) { Bounds = new Rectangle(510, 350, 120, 30), BorderAccent = _accent };
            btnDone.Click += (s, e) => Close();

            Controls.Add(tb);
            Controls.Add(_chartPanel);
            Controls.Add(_lblStats);
            Controls.Add(btnReset);
            Controls.Add(btnSmooth);
            Controls.Add(btnDone);

            Paint += (s, e) => {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = RoundedRect(0, 0, Width - 1, Height - 1, 12))
                using (Pen p = new Pen(_accent, 1))
                    e.Graphics.DrawPath(p, path);
            };
        }

        private void DrawChart(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            int numBars = 25;
            float barWidth = (_chartPanel.Width - 40) / (float)numBars;
            float maxH = _chartPanel.Height - 40;

            for (int i = 0; i < numBars; i++)
            {
                float x = 20 + i * barWidth;
                float w = barWidth - 4;
                
                double weight = _cfg.CustomCpsWeights[i];
                float h = (float)(weight * maxH);
                if (h > maxH) h = maxH;
                if (h < 0) h = 0;
                
                float y = _chartPanel.Height - 20 - h;

                // Background track
                using (Brush b = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    g.FillRectangle(b, x, 20, w, maxH);

                // Bar
                using (Brush b = new SolidBrush(weight > 0 ? _accent : DIM))
                    g.FillRectangle(b, x, y, w, h);

                // Label
                SizeF sz = g.MeasureString((i + 1).ToString(), FNT);
                using (Brush b = new SolidBrush(DIM))
                    g.DrawString((i + 1).ToString(), FNT, b, x + (w - sz.Width) / 2, _chartPanel.Height - 18);
                    
                // Value text above bar
                if (weight > 0)
                {
                    string txt = weight.ToString("0.00");
                    SizeF tsz = g.MeasureString(txt, new Font("Courier New", 6F));
                    using (Brush b = new SolidBrush(TXT))
                        g.DrawString(txt, new Font("Courier New", 6F), b, x + (w - tsz.Width) / 2, y - 10);
                }
            }
        }

        private void ChartMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateBar(e.Location);
            }
        }

        private void ChartMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateBar(e.Location);
            }
        }

        private void ChartMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                UpdateStats();
            }
        }

        private void UpdateBar(Point pt)
        {
            int numBars = 25;
            float barWidth = (_chartPanel.Width - 40) / (float)numBars;
            float maxH = _chartPanel.Height - 40;

            int index = (int)((pt.X - 20) / barWidth);
            if (index >= 0 && index < numBars)
            {
                float y = pt.Y;
                float h = _chartPanel.Height - 20 - y;
                double weight = h / maxH;
                
                if (weight < 0) weight = 0;
                if (weight > 1) weight = 1;

                _cfg.CustomCpsWeights[index] = weight;
                _chartPanel.Invalidate();
            }
        }

        private void UpdateStats()
        {
            double sumWeights = 0;
            double expectedValue = 0;
            
            for (int i = 0; i < 25; i++)
            {
                sumWeights += _cfg.CustomCpsWeights[i];
                expectedValue += (i + 1) * _cfg.CustomCpsWeights[i];
            }

            if (sumWeights > 0)
            {
                double avgCps = expectedValue / sumWeights;
                
                // Calculate Variance
                double variance = 0;
                for (int i = 0; i < 25; i++)
                {
                    double p = _cfg.CustomCpsWeights[i] / sumWeights;
                    variance += p * Math.Pow((i + 1) - avgCps, 2);
                }
                _lblStats.Text = string.Format("Statistics:\nAverage CPS: {0:F2}\nVariance: {1:F2}\nTotal Weight: {2:F2}", avgCps, variance, sumWeights);
            }
            else
            {
                _lblStats.Text = "Statistics:\nAverage CPS: 0.00\nVariance: 0.00\nTotal Weight: 0.00\n(Draw bars on the graph above to set CPS probabilities)";
            }
        }
    }
}
