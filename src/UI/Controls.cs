using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Horimiya.Utils
{
    // ─── Custom Checkbox ──────────────────────────────────────────────────────
    public class FlatCheck : Panel
    {
        public bool Checked { get; set; }
        private Color _accCol = Color.FromArgb(200, 200, 200);
        public Color AccentColor { get { return _accCol; } set { _accCol = value; Invalidate(); } }
        private string _text;
        private bool _hovered = false;
        private static readonly Font F = new Font("Courier New", 8F);

        public FlatCheck(string text, bool val = false)
        {
            _text = text;
            Checked = val;
            Height = 20;
            Width = 240;
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(22, 22, 22);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            Invalidate();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            // Box border with accent color
            Color borderCol = _hovered ? AccentColor : Color.FromArgb(120, AccentColor);
            using (var brd = new Pen(borderCol, 1))
            {
                g.DrawRectangle(brd, 0, 2, 12, 12);
            }

            if (Checked)
            {
                // Glow when checked
                using (var glow = new SolidBrush(Color.FromArgb(30, AccentColor)))
                {
                    g.FillRectangle(glow, 0, 2, 12, 12);
                }
                // Checkmark
                using (var pen = new Pen(AccentColor, 2))
                {
                    g.DrawLine(pen, 2, 7, 5, 10);
                    g.DrawLine(pen, 5, 10, 10, 3);
                }
            }

            Color txtCol = _hovered ? Color.FromArgb(255, 255, 255) : Color.FromArgb(
                Math.Min(255, AccentColor.R + 60),
                Math.Min(255, AccentColor.G + 60),
                Math.Min(255, AccentColor.B + 60));
            using (var fg = new SolidBrush(txtCol))
            {
                g.DrawString(_text, F, fg, 18, 2);
            }
        }
    }

    // ─── Custom Slider ────────────────────────────────────────────────────────
    public class FlatSlider : Panel
    {
        public double Value { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        private Color _accCol = Color.FromArgb(200, 200, 200);
        public Color AccentColor { get { return _accCol; } set { _accCol = value; Invalidate(); } }
        private bool _drag = false;
        private bool _hovered = false;
        private static readonly Font F = new Font("Courier New", 8F);

        public event EventHandler ValueChanged;

        public FlatSlider(double val, double min, double max)
        {
            Value = val;
            Min = min;
            Max = max;
            Height = 22;
            Cursor = Cursors.SizeWE;
            BackColor = Color.FromArgb(22, 22, 22);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);
        }

        private void SetValueFromMouse(int x)
        {
            int trackWidth = Width - 62;
            double pct = Math.Max(0, Math.Min(1, (double)x / Math.Max(1, trackWidth)));
            Value = Math.Round(Min + pct * (Max - Min), 1);
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _drag = true;
            SetValueFromMouse(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) SetValueFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _drag = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int trackWidth = Width - 62;
            int cY = Height / 2;
            double pct = Math.Max(0, Math.Min(1, (Value - Min) / Math.Max(0.001, Max - Min)));
            int fillX = (int)(pct * Math.Max(0, trackWidth - 8));

            // Track background
            using (var p = new Pen(Color.FromArgb(50, 50, 50), 2))
                g.DrawLine(p, 2, cY, trackWidth - 2, cY);

            // Track fill with accent
            Color fillCol = _hovered || _drag ? AccentColor : Color.FromArgb(AccentColor.A / 2, AccentColor);
            using (var p = new Pen(fillCol, 2))
                g.DrawLine(p, 2, cY, 2 + fillX, cY);

            // Knob glow
            if (_hovered || _drag)
            {
                using (var glow = new SolidBrush(Color.FromArgb(40, AccentColor)))
                    g.FillEllipse(glow, fillX - 5, cY - 9, 18, 18);
            }

            // Knob circle
            Color knobCol = _drag ? Color.White :
                            _hovered ? Color.FromArgb(255, 255, 255) : Color.FromArgb(220, 220, 220);
            using (var sb = new SolidBrush(knobCol))
                g.FillEllipse(sb, fillX, cY - 5, 10, 10);

            // Value text with accent
            string valStr = Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            var sz = g.MeasureString(valStr, F);
            Color txtCol = Color.FromArgb(
                Math.Min(255, AccentColor.R + 40),
                Math.Min(255, AccentColor.G + 40),
                Math.Min(255, AccentColor.B + 40));
            if (!_hovered && !_drag)
                txtCol = Color.FromArgb(
                    Math.Max(60, (int)AccentColor.R),
                    Math.Max(60, (int)AccentColor.G),
                    Math.Max(60, (int)AccentColor.B));
            using (var sb = new SolidBrush(txtCol))
                g.DrawString(valStr, F, sb, trackWidth + 6, cY - sz.Height / 2);
        }
    }

    // ─── Custom DropDown ──────────────────────────────────────────────────────
    public class FlatDrop : ComboBox
    {
        private static readonly Font F = new Font("Courier New", 8F);
        private Color _borderColor = Color.FromArgb(60, 60, 60);
        private bool _hover = false;

        public Color BorderColor
        {
            get { return _borderColor; }
            set { _borderColor = value; Invalidate(); }
        }

        public FlatDrop()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            Font = F;
            BackColor = Color.FromArgb(28, 28, 28);
            ForeColor = Color.FromArgb(220, 220, 220);
            Font = F;
            ItemHeight = 18;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool isSelected = (e.State & DrawItemState.Selected) != 0;
            Color bgCol = isSelected ? Color.FromArgb(48, 48, 48) : Color.FromArgb(28, 28, 28);
            using (var bgBrush = new SolidBrush(bgCol))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            if (e.Index < Items.Count)
            {
                Color itemCol = Color.FromArgb(
                    Math.Min(255, _borderColor.R + 50),
                    Math.Min(255, _borderColor.G + 50),
                    Math.Min(255, _borderColor.B + 50));
                using (var fgBrush = new SolidBrush(itemCol))
                    e.Graphics.DrawString(Items[e.Index].ToString(), F, fgBrush, e.Bounds.X + 3, e.Bounds.Y + 2);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F)
            {
                using (var g = CreateGraphics())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int w = Width - 1, h = Height - 1;
                    int r = 6, bw = 1;

                    // Clip to rounded rect
                    var clip = new System.Drawing.Drawing2D.GraphicsPath();
                    clip.AddRectangle(new Rectangle(0, 0, Width, Height));
                    g.SetClip(clip);

                    // Cover default button area
                    int btnW = 15;
                    using (var bg = new SolidBrush(BackColor))
                        g.FillRectangle(bg, Width - btnW - 1, 0, btnW + 2, Height);

                    // Down arrow (opaque accent)
                    int cx = Width - btnW / 2 - 1;
                    int cy = h / 2;
                    Color arrowCol = _hover ? Color.FromArgb(255, 255, 255) : Color.FromArgb(
                        Math.Min(255, _borderColor.R + 40),
                        Math.Min(255, _borderColor.G + 40),
                        Math.Min(255, _borderColor.B + 40));
                    using (var brush = new SolidBrush(arrowCol))
                    {
                        Point[] pts = new Point[] {
                            new Point(cx - 4, cy - 2),
                            new Point(cx + 4, cy - 2),
                            new Point(cx, cy + 3)
                        };
                        g.FillPolygon(brush, pts);
                    }

                    // Rounded border (semi-transparent accent)
                    var path = new System.Drawing.Drawing2D.GraphicsPath();
                    path.AddArc(0, 0, r, r, 180, 90);
                    path.AddArc(w - r, 0, r, r, 270, 90);
                    path.AddArc(w - r, h - r, r, r, 0, 90);
                    path.AddArc(0, h - r, r, r, 90, 90);
                    path.CloseFigure();
                    using (var p = new Pen(Color.FromArgb(160, _borderColor), bw))
                        g.DrawPath(p, path);
                    path.Dispose();
                    clip.Dispose();
                }
            }
        }
    }

    // ─── Custom TextBox ──────────────────────────────────────────────────────
    public class FlatTextBox : TextBox
    {
        private static readonly Font F = new Font("Courier New", 8F);
        private Color _borderColor = Color.FromArgb(60, 60, 60);

        public Color BorderColor
        {
            get { return _borderColor; }
            set { _borderColor = value; Invalidate(); }
        }

        public FlatTextBox()
        {
            Font = F;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(240, 240, 240);
            BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F)
            {
                using (var g = CreateGraphics())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var path = new System.Drawing.Drawing2D.GraphicsPath();
                    int r = 5, w = Width - 1, h = Height - 1;
                    path.AddArc(0, 0, r, r, 180, 90);
                    path.AddArc(w - r, 0, r, r, 270, 90);
                    path.AddArc(w - r, h - r, r, r, 0, 90);
                    path.AddArc(0, h - r, r, r, 90, 90);
                    path.CloseFigure();
                    using (var p = new Pen(_borderColor, 1))
                        g.DrawPath(p, path);
                    path.Dispose();
                }
            }
        }
    }

    // ─── Custom Button ────────────────────────────────────────────────────────
    public class FlatButton : Button
    {
        private static readonly Font F = new Font("Courier New", 8F);
        private bool _hover = false;
        private Color _borderAccent = Color.FromArgb(60, 60, 60);

        public Color BorderAccent { set { _borderAccent = value; Invalidate(); } }

        public FlatButton(string text, Color fgColor)
        {
            Text = text;
            ForeColor = fgColor;
            Font = F;
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.FromArgb(30, 30, 30);
            Cursor = Cursors.Hand;

            FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            FlatAppearance.BorderSize = 1;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 20, 20);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_hover)
            {
                using (var g = CreateGraphics())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var p = new Pen(Color.FromArgb(120, _borderAccent), 1))
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            }
        }
    }

    // ─── Particle Network Overlay ─────────────────────────────────────────────
    internal struct Particle
    {
        public float X, Y, VX, VY;
    }

    public class ParticleOverlayForm : Form
    {
        private Form _parent;
        private List<Particle> _particles = new List<Particle>();
        private System.Windows.Forms.Timer _timer;
        private int _accentArgb = Color.FromArgb(200, 200, 200).ToArgb();
        private bool _enabled = true;

        private static readonly Color BG_KEY = Color.FromArgb(0, 0, 0);
        private const int PARTICLE_COUNT = 45;
        private const int CONNECT_DIST = 130;
        private const float MAX_SPEED = 1.2f;

        public bool ParticlesEnabled
        {
            get { return _enabled; }
            set { _enabled = value; Visible = value && _parent.Visible; _timer.Enabled = value; }
        }

        public int AccentArgb
        {
            get { return _accentArgb; }
            set { _accentArgb = value; Invalidate(); }
        }

        public ParticleOverlayForm(Form parent)
        {
            _parent = parent;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            BackColor = BG_KEY;
            TransparencyKey = BG_KEY;

            DoubleBuffered = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Location = parent.Location;
            Size = parent.Size;

            InitParticles();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 33;
            _timer.Tick += OnTick;
            _timer.Start();

            parent.LocationChanged += (s, e) => { if (_enabled) Location = parent.Location; };
            parent.SizeChanged += (s, e) => { if (_enabled) { Size = parent.Size; InitParticles(); } };
            parent.VisibleChanged += (s, e) => { if (_enabled) Visible = parent.Visible; };
            parent.FormClosed += (s, e) => { _timer.Stop(); Close(); };
        }

        public void InitParticles()
        {
            var rng = new Random();
            _particles.Clear();
            for (int i = 0; i < PARTICLE_COUNT; i++)
            {
                _particles.Add(new Particle
                {
                    X = rng.Next(Width),
                    Y = rng.Next(Height),
                    VX = (float)((rng.NextDouble() - 0.5) * MAX_SPEED * 2),
                    VY = (float)((rng.NextDouble() - 0.5) * MAX_SPEED * 2)
                });
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            UpdateParticles();
            Invalidate();
        }

        private void UpdateParticles()
        {
            int w = Width;
            int h = Height;
            if (w <= 0 || h <= 0) return;

            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                p.X += p.VX;
                p.Y += p.VY;

                if (p.X < 0 || p.X > w) { p.VX = -p.VX; p.X = Math.Max(0, Math.Min(w, p.X)); }
                if (p.Y < 0 || p.Y > h) { p.VY = -p.VY; p.Y = Math.Max(0, Math.Min(h, p.Y)); }

                _particles[i] = p;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Color accent = Color.FromArgb(_accentArgb);
            if (accent.R < 5 && accent.G < 5 && accent.B < 5)
                accent = Color.FromArgb(40, 40, 40);

            int count = _particles.Count;

            // Draw connections with glow
            for (int i = 0; i < count; i++)
            {
                float x1 = _particles[i].X;
                float y1 = _particles[i].Y;
                for (int j = i + 1; j < count; j++)
                {
                    float dx = x1 - _particles[j].X;
                    float dy = y1 - _particles[j].Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (dist < CONNECT_DIST)
                    {
                        int alpha = (int)(180 * (1 - dist / CONNECT_DIST));
                        if (alpha > 0)
                        {
                            // Glow
                            using (Pen glow = new Pen(Color.FromArgb(alpha / 5, accent), 4f))
                            {
                                g.DrawLine(glow, x1, y1, _particles[j].X, _particles[j].Y);
                            }
                            // Core line
                            using (Pen pen = new Pen(Color.FromArgb(alpha / 2, accent), 1f))
                            {
                                g.DrawLine(pen, x1, y1, _particles[j].X, _particles[j].Y);
                            }
                        }
                    }
                }
            }

            // Draw particles with bloom
            int coreSize = 3;
            for (int i = 0; i < count; i++)
            {
                float x = _particles[i].X;
                float y = _particles[i].Y;

                // Bloom layers
                for (int layer = 3; layer >= 0; layer--)
                {
                    int size = coreSize + (3 - layer) * 5;
                    int alpha = 35 - layer * 8;
                    if (alpha > 0)
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, accent)))
                        {
                            g.FillEllipse(brush, x - size / 2f, y - size / 2f, size, size);
                        }
                    }
                }

                // Core dot
                using (SolidBrush brush = new SolidBrush(accent))
                {
                    g.FillEllipse(brush, x - coreSize / 2f, y - coreSize / 2f, coreSize, coreSize);
                }
            }
        }
    }
}
