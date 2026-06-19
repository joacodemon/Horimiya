using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
// OpenTK dependencies removed (not available in this build environment)
using lospoderosos_lite.Config;
using lospoderosos_lite.Modules;
using lospoderosos_lite.Utils;

namespace lospoderosos_lite.UI
{
    public class MainForm : Form
    {
        // Palette
        static readonly Color BG   = Color.FromArgb(10, 10, 10);
        static readonly Color SBG  = Color.FromArgb(16, 16, 16);
        static readonly Color PBG  = Color.FromArgb(20, 20, 20);
        static readonly Color SEP  = Color.FromArgb(50, 50, 50);
        static readonly Color GRN  = Color.FromArgb(90, 200, 90);

        Color TXT { get { return _txt; } }
        Color DIM { get { return _dim; } }
        Color _txt = Color.FromArgb(240, 240, 240);
        Color _dim = Color.FromArgb(130, 130, 130);
        static readonly Font  FNT  = new Font("Courier New", 8F);
        static readonly Font  FNTB = new Font("Courier New", 8.5F, FontStyle.Bold);

        // Layout constants (content area = 950 - 90 sidebar = 860 wide)
        const int CW = 860;  // content width
        const int LW = 500;  // left box width
        const int RW = 330;  // right box width
        const int RX = 520;  // right box X
        const int BH = 340;  // box height

        readonly AppConfig _cfg;
        readonly Clicker   _clicker;
        readonly Recorder  _recorder;
        readonly Misc      _misc;

        int _tab = 0;
        Panel _pLmb, _pRec, _pMisc, _pPresets;
        Button _bLmb, _bRec, _bMisc, _bPresets;

        // Bind state
        bool _bindMode, _bindHide, _bindDestruct, _waitRelease;
        System.Windows.Forms.Timer _bindTimer;

        // UI refs for sync
        FlatCheck  _chkTgl, _chkOig, _chkRmb, _chkWim, _chkRefill;
        FlatCheck  _chkRpc, _chkParticle;
        FlatSlider _sldrCps, _sldrPing;
        FlatDrop   _dRand;
        FlatDrop   _dMode, _dBB, _dSnd;
        Button     _btnBind, _btnHide, _btnColor, _btnDestructBind;
        Label      _lblRpc, _lblRecSt;
        FlatTextBox _txAppId;
        ParticleOverlayForm _particleOverlay;

        Color _accentColor = Color.FromArgb(0, 180, 255);
        // Drag flag for custom chart interaction
        private bool _isDragging = false;
        // References to custom randomization UI
        private Panel _customPanel;
        private Label _customStats;

        System.Drawing.Drawing2D.GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(x, y, r, r, 180, 90);
            path.AddArc(x + w - r, y, r, r, 270, 90);
            path.AddArc(x + w - r, y + h - r, r, r, 0, 90);
            path.AddArc(x, y + h - r, r, r, 90, 90);    
            path.CloseFigure();
            return path;
        }

        void DrawRoundedBorder(Graphics g, int x, int y, int w, int h, int r, Color c, int width)
        {
            using (var path = RoundedRect(x, y, w, h, r))
            using (var pen = new Pen(c, width))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawPath(pen, path);
            }
        }

        public MainForm(AppConfig cfg, Clicker clicker, Recorder recorder, Misc misc)
        {
            _cfg = cfg; _clicker = clicker; _recorder = recorder; _misc = misc;

            Text = "los poderosisimos";
            Size = new Size(950, 470);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = BG;
            DoubleBuffered  = true;
            KeyPreview      = true;

            _bindTimer = new System.Windows.Forms.Timer();
            _bindTimer.Interval = 20;
            _bindTimer.Tick += BindTick;

            Build();

            _recorder.StatusChanged    += s => SafeSet(_lblRecSt, s);
            _misc.RpcStatusChanged     += ok => SafeInvoke(() => { if (_lblRpc != null) { _lblRpc.Text = ok ? "Discord RPC: Connected" : "Discord RPC: Disconnected"; _lblRpc.ForeColor = ok ? GRN : DIM; } });
            _misc.ClickBindTriggered   += () => SafeInvoke(() => { _chkTgl.Checked = !_chkTgl.Checked; _clicker.Clicking = _chkTgl.Checked; _chkTgl.Invalidate(); });
            _misc.HideBindTriggered    += () => SafeInvoke(() => { if (Visible) Hide(); else { Show(); BringToFront(); } });
            _misc.DestructBindTriggered += () => SafeInvoke(() => { this.Close(); });
            
            LoadBackgroundImage();
            LoadTaskbarIcon();

            _particleOverlay = new ParticleOverlayForm(this);
            _particleOverlay.AccentArgb = _cfg.ColorAccent;
            _particleOverlay.ParticlesEnabled = _cfg.ParticleEnabled;
            this.Shown += (s, e) => {
                _particleOverlay.Location = this.Location;
                _particleOverlay.Size = this.Size;
                if (!_particleOverlay.Visible)
                    _particleOverlay.Show(this);
                _particleOverlay.InitParticles();
            };

            // Apply saved accent color
            _accentColor = Color.FromArgb(_cfg.ColorAccent);
            ApplyAccentToAll(_accentColor);
            Invalidate();
        }

        void RecomputeTextColors()
        {
            _txt = Color.FromArgb(
                (int)(_accentColor.R * 0.3 + 255 * 0.7),
                (int)(_accentColor.G * 0.3 + 255 * 0.7),
                (int)(_accentColor.B * 0.3 + 255 * 0.7));
            _dim = Color.FromArgb(
                Math.Max(60, (int)(_accentColor.R * 0.5)),
                Math.Max(60, (int)(_accentColor.G * 0.5)),
                Math.Max(60, (int)(_accentColor.B * 0.5)));
        }

        void ApplyAccentToAll(Color c)
        {
            Color oldTxt = _txt;
            Color oldDim = _dim;

            _accentColor = c;
            RecomputeTextColors();

            if (_particleOverlay != null)
                _particleOverlay.AccentArgb = c.ToArgb();
            if (_btnColor != null)
            {
                _btnColor.BackColor = c;
                _btnColor.ForeColor = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 128 ? Color.Black : Color.White;
            }
            // Update all controls
            foreach (var ctrl in GetAllControls(this))
            {
                if (ctrl is Label)
                {
                    Label lbl = (Label)ctrl;
                    if (lbl.ForeColor == oldTxt) lbl.ForeColor = _txt;
                    else if (lbl.ForeColor == oldDim) lbl.ForeColor = _dim;
                }
                else if (ctrl is Button)
                {
                    Button btn = (Button)ctrl;
                    if (btn.ForeColor == oldTxt) btn.ForeColor = _txt;
                    else if (btn.ForeColor == oldDim) btn.ForeColor = _dim;
                }
                else if (ctrl is TextBoxBase)
                {
                    TextBoxBase tb = (TextBoxBase)ctrl;
                    if (tb.ForeColor == oldTxt) tb.ForeColor = _txt;
                    else if (tb.ForeColor == oldDim) tb.ForeColor = _dim;
                    if (ctrl is FlatTextBox && _btnColor != null)
                        ((FlatTextBox)ctrl).BorderColor = Color.FromArgb(150, c);
                }
                if (ctrl is FlatSlider) ((FlatSlider)ctrl).AccentColor = c;
                if (ctrl is FlatCheck) ((FlatCheck)ctrl).AccentColor = c;
                if (ctrl is FlatDrop) {
                    ((FlatDrop)ctrl).BorderColor = c;
                    ((FlatDrop)ctrl).ForeColor = Color.FromArgb(
                        Math.Min(255, c.R + 40),
                        Math.Min(255, c.G + 40),
                        Math.Min(255, c.B + 40));
                }
                if (ctrl is FlatTextBox) ((FlatTextBox)ctrl).BorderColor = Color.FromArgb(180, c);
                if (ctrl is FlatButton) ((FlatButton)ctrl).BorderAccent = c;
                if (ctrl is Panel && ctrl != this) ctrl.Invalidate();
            }
            Invalidate();
        }

        IEnumerable<Control> GetAllControls(Control parent)
        {

            foreach (Control c in parent.Controls)
            {
                yield return c;
                foreach (var sub in GetAllControls(c))
                    yield return sub;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (var path = RoundedRect(0, 0, Width, Height, 12))
                Region = new Region(path);
            Win32.StartMouseHook();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated)
            {
                using (var path = RoundedRect(0, 0, Width, Height, 12))
                    Region = new Region(path);
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────
        void Build()
        {
            // ─ NO DOCKING ─ all panels use manual absolute bounds to avoid z-order bugs ─

            // Title bar  (full width, top)
            var tb = new Panel { BackColor = Color.FromArgb(8, 8, 8) };
            tb.SetBounds(0, 0, 950, 26);
            tb.Paint += (s,e) => {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.FromArgb(150, _accentColor), 1))
                    e.Graphics.DrawLine(p, 0, 25, tb.Width, 25);
            };
            var tbLbl = Lbl("los poderosisimos", DIM, 95, 7, FNT);
            tbLbl.MouseDown += Drag; tb.MouseDown += Drag;
            var bX = SysBtn("x", 924, 0);
            bX.FlatAppearance.MouseOverBackColor = Color.FromArgb(190, 30, 30);
            bX.Click += (s,e) => Close();
            var bMin = SysBtn("-", 898, 0);
            bMin.Click += (s,e) => WindowState = FormWindowState.Minimized;
            tb.Controls.AddRange(new Control[] { tbLbl, bX, bMin });

            // Sidebar  (left column, between title bar and footer)
            var sb = new Panel { BackColor = SBG };
            sb.SetBounds(0, 26, 90, 418);
            sb.Paint += (s,e) => {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.FromArgb(80, _accentColor), 1))
                    e.Graphics.DrawLine(p, 89, 0, 89, sb.Height);
            };
            sb.Controls.Add(Lbl("los", DIM, 5, 6, FNT));
            sb.Controls.Add(Lbl("poderosisimos", DIM, 5, 18, FNT));
            _bLmb  = SideBtn("LMB",  42); _bLmb.Click  += (s,e) => SetTab(0);
            _bRec  = SideBtn("REC",  72); _bRec.Click  += (s,e) => SetTab(1);
            _bPresets = SideBtn("PRESETS", 102); _bPresets.Click += (s,e) => SetTab(3);
            _bMisc = SideBtn("MISC",132); _bMisc.Click += (s,e) => SetTab(2);
            sb.Controls.Add(_bLmb); sb.Controls.Add(_bRec); sb.Controls.Add(_bPresets); sb.Controls.Add(_bMisc);
            
            var btnEject = SideBtn("EJECT CLIENT", 380);
            btnEject.Font = new Font("Courier New", 7F, FontStyle.Bold);
            btnEject.ForeColor = Color.FromArgb(200, 50, 50);
            btnEject.Click += (s, e) => Close();
            sb.Controls.Add(btnEject);

            // Footer  (full width, bottom)
            var ft = new Panel { BackColor = Color.FromArgb(8, 8, 8) };
            ft.SetBounds(0, 444, 950, 26);
            ft.Paint += (s,e) => {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.FromArgb(80, _accentColor), 1))
                    e.Graphics.DrawLine(p, 0, 0, ft.Width, 0);
            };
            ft.Controls.Add(Lbl("by joacodemon", DIM, 95, 7, FNT));

            // Content  (right of sidebar, between title bar and footer)
            var ct = new Panel { BackColor = BG };
            ct.SetBounds(90, 26, 860, 418);
            _pLmb  = BuildLMB();     _pLmb.Dock     = DockStyle.Fill; _pLmb.Visible     = true;
            _pRec  = BuildREC();     _pRec.Dock     = DockStyle.Fill; _pRec.Visible     = false;
            _pMisc = BuildMISC();    _pMisc.Dock    = DockStyle.Fill; _pMisc.Visible    = false;
            _pPresets = BuildPRESETS(); _pPresets.Dock = DockStyle.Fill; _pPresets.Visible = false;
            ct.Controls.AddRange(new Control[] { _pMisc, _pRec, _pPresets, _pLmb });

            Controls.AddRange(new Control[] { tb, sb, ft, ct });
            Paint += (s,e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                int w = Width, h = Height;

                // Futuristic background grid
                Color gridCol = Color.FromArgb(18, 18, 18);
                using (var gridPen = new Pen(gridCol, 1))
                {
                    for (int gx = 0; gx < w; gx += 30)
                        g.DrawLine(gridPen, gx, 0, gx, h);
                    for (int gy = 0; gy < h; gy += 30)
                        g.DrawLine(gridPen, 0, gy, w, gy);
                }

                // Rounded glow border
                Color acc = _accentColor;
                for (int i = 4; i >= 0; i--)
                {
                    int alpha = 40 - i * 7;
                    using (var path = RoundedRect(i, i, w - 1 - i * 2, h - 1 - i * 2, 12))
                    using (Pen p = new Pen(Color.FromArgb(Math.Max(0, alpha), acc), i + 1))
                        g.DrawPath(p, path);
                }
                // Main rounded border
                using (var path = RoundedRect(0, 0, w - 1, h - 1, 12))
                using (Pen p = new Pen(acc, 1))
                    g.DrawPath(p, path);
            };
            RefreshSide();
        }

        // ── LMB ───────────────────────────────────────────────────────────────
        Panel BuildLMB()
        {
            var p = new Panel { BackColor = BG };
            p.Controls.Add(Lbl("Left Clicker", TXT, 10, 10, FNTB));
            p.Controls.Add(HSep(10, 30, CW - 20));

            var lft = Box(10, 40, LW, BH);

            // Row 1
            _chkTgl = new FlatCheck("Toggle", false) { Location = new Point(8, 10), Width = 65, BackColor = Color.FromArgb(200, 0, 0) };
        _chkTgl.Click += (s,e) => { _clicker.Clicking = _chkTgl.Checked; _chkTgl.BackColor = _chkTgl.Checked ? Color.FromArgb(0, 200, 0) : Color.FromArgb(200, 0, 0); };

            _btnBind = BoxBtn("Bind: none", TXT, 78, 8, 90, 22);
            _btnBind.Click += (s,e) => BeginBind(false);

            _dMode = new FlatDrop { Location = new Point(175, 9), Size = new Size(78, 18) };
            _dMode.Items.AddRange(new object[] { "Hold", "Toggle", "Always" });
            _dMode.SelectedIndex = _cfg.Mode;
            _dMode.SelectedIndexChanged += (s,e) => _cfg.Mode = _dMode.SelectedIndex;
            lft.Controls.Add(Lbl("Mode", DIM, 262, 12, FNT));

            // Row 2: CPS
            _sldrCps = new FlatSlider(_cfg.AverageCps, 1.0, 50.0)
                { Location = new Point(8, 36), Size = new Size(380, 22) };
            _sldrCps.ValueChanged += (s,e) => _cfg.AverageCps = _sldrCps.Value;
            _sldrCps.MouseUp += (s,e) => _cfg.Save();
            lft.Controls.Add(Lbl("Average CPS", DIM, 395, 42, FNT));

            // Checkboxes
            _chkOig  = new FlatCheck("Only In Game",    _cfg.OnlyInGame)  { Location = new Point(8, 66)  };
            _chkRmb  = new FlatCheck("RMB-Lock",        _cfg.RmbLock)     { Location = new Point(8, 90) };
            _chkWim  = new FlatCheck("Work in Menus",   _cfg.WorkInMenus) { Location = new Point(8, 114) };
            
            _chkOig.Click  += (s,e) => _cfg.OnlyInGame  = _chkOig.Checked;
            _chkRmb.Click  += (s,e) => _cfg.RmbLock     = _chkRmb.Checked;
            _chkWim.Click  += (s,e) => _cfg.WorkInMenus = _chkWim.Checked;

            // Ping (ms) slider
            _sldrPing = new FlatSlider(_cfg.PingMs, 0.0, 200.0)
                { Location = new Point(8, 142), Size = new Size(380, 22) };
            _sldrPing.ValueChanged += (s,e) => _cfg.PingMs = _sldrPing.Value;
            _sldrPing.MouseUp += (s,e) => _cfg.Save();
            lft.Controls.Add(Lbl("Ping (ms)", DIM, 395, 148, FNT));

            lft.Controls.Add(Lbl("Latency compensation for hit registration.", DIM, 8, 210, FNT));
            lft.Controls.AddRange(new Control[] { _chkTgl, _btnBind, _dMode, _sldrCps,
                _chkOig, _chkRmb, _chkWim, _sldrPing });

            // Right box
            var rgt = Box(RX, 40, RW, BH);
            rgt.Controls.Add(Lbl("Click Sound", TXT, 8, 10, FNT));
            _dSnd = new FlatDrop { Size = new Size(RW - 20, 18) };
            LoadSounds();
            _dSnd.SelectedIndexChanged += (s,e) => { if (_dSnd.SelectedItem != null) _cfg.Sound = _dSnd.SelectedItem.ToString(); };
            rgt.Controls.Add(AccentBorderWrap(_dSnd, 7, 27, RW - 18, 20));
            rgt.Controls.Add(Lbl("Place .wav files in", DIM, 8, 52, FNT));
            rgt.Controls.Add(Lbl("%USERPROFILE%\\XVA\\resource", DIM, 8, 66, FNT));
            rgt.Controls.Add(HSep(8, 86, RW - 20));
            rgt.Controls.Add(Lbl("Lite build", TXT, 8, 94, FNTB));
            rgt.Controls.Add(Lbl("Recorder available from REC tab.", DIM, 8, 112, FNT));
            rgt.Controls.Add(HSep(8, 132, RW - 20));
            rgt.Controls.Add(Lbl("Randomization", TXT, 8, 140, FNTB));
            _dRand = new FlatDrop { Size = new Size(RW - 20, 18) };
            _dRand.Items.AddRange(new object[] { "Jitter", "Butterfly", "NoDelay", "Manual" });
            _dRand.SelectedIndex = _cfg.RandMode;
            
            var btnManualEdit = BoxBtn("Edit Custom Randomization", TXT, 8, 300, RW - 20, 22);
            // Highlight when Manual Randomization is active
            btnManualEdit.BackColor = (_cfg.RandMode == 3) ? Color.FromArgb(0, 200, 0) : _accentColor;
            btnManualEdit.Visible = (_cfg.RandMode == 3);
            btnManualEdit.Click += (s, e) => {
                using (var crf = new CustomRandForm(_cfg, _accentColor)) {
                    crf.ShowDialog();
                }
            };
            // Inline custom randomization chart panel (using fields)
            _customPanel = new Panel { Bounds = new Rectangle(8, 340, RW - 20, 140), BackColor = Color.FromArgb(20, 20, 20) };
            _customPanel.Paint += DrawCustomChart;
            _customPanel.MouseDown += CustomChartMouseDown;
            _customPanel.MouseMove += CustomChartMouseMove;
            _customPanel.MouseUp += CustomChartMouseUp;
            _customStats = new Label { ForeColor = TXT, Font = FNT, Location = new Point(8, 490), AutoSize = true };
            _customPanel.Visible = (_cfg.RandMode == 3);
            _customStats.Visible = (_cfg.RandMode == 3);
            UpdateCustomStats();
            rgt.Controls.Add(btnManualEdit);
            rgt.Controls.Add(_customPanel);
            rgt.Controls.Add(_customStats);

            _dRand.SelectedIndexChanged += (s,e) => {
                _cfg.RandMode = _dRand.SelectedIndex;
                // Update button appearance and visibility
                btnManualEdit.Visible = (_cfg.RandMode == 3);
                btnManualEdit.BackColor = (_cfg.RandMode == 3) ? Color.FromArgb(0, 200, 0) : _accentColor;
                // Update visibility of inline custom UI
                _customPanel.Visible = (_cfg.RandMode == 3);
                _customStats.Visible = (_cfg.RandMode == 3);
            };
            rgt.Controls.Add(AccentBorderWrap(_dRand, 7, 158, RW - 18, 20));

            var lblLiveTitle = Lbl("Live", TXT, 8, 184, FNT);
            var lblLiveVal = Lbl("0.0", TXT, 8, 198, new Font("Courier New", 18F, FontStyle.Bold));
            var lblAvgCps = Lbl("Average: 0.0", DIM, 8, 228, FNT);
            
            var lblStabTitle = Lbl("Stability", TXT, 140, 184, FNT);
            var lblStab1 = Lbl("Interval: 0.00 ms   Jitter: 0.00 ms", DIM, 140, 198, FNT);
            var lblStab2 = Lbl("Last: 0.00 ms   Late: 0", DIM, 140, 210, FNT);
            var lblStab3 = Lbl("Worst late: 0.00 ms   Samples: 0", DIM, 140, 222, FNT);
            var lblStabStatus = Lbl("Waiting for clicks...", DIM, 140, 234, FNT);

            var statTimer = new System.Windows.Forms.Timer { Interval = 50 };
            statTimer.Tick += (sender, e) => {
                if (_clicker == null) return;
                lblLiveVal.Text = _clicker.StatLiveCps.ToString("F1");
                lblAvgCps.Text = "Average: " + _clicker.StatAvgCps.ToString("F1");
                lblStab1.Text = string.Format("Interval: {0:F2} ms   Jitter: {1:F2} ms", _clicker.StatInterval, _clicker.StatJitter);
                lblStab2.Text = string.Format("Last: {0:F2} ms   Late: {1}", _clicker.StatLast, _clicker.StatLate);
                lblStab3.Text = string.Format("Worst late: {0:F2} ms   Samples: {1}", _clicker.StatWorstLate, _clicker.StatSamples);
                
                if (_clicker.StatSamples > 0)
                {
                    if (_clicker.StatJitter < 2.0) {
                        lblStabStatus.Text = "[Stable]";
                        lblStabStatus.ForeColor = Color.FromArgb(50, 200, 100);
                    } else {
                        lblStabStatus.Text = "[Unstable]";
                        lblStabStatus.ForeColor = Color.FromArgb(200, 50, 50);
                    }
                }
            };
            statTimer.Start();

            rgt.Controls.AddRange(new Control[] { lblLiveTitle, lblLiveVal, lblAvgCps, lblStabTitle, lblStab1, lblStab2, lblStab3, lblStabStatus });

            p.Controls.AddRange(new Control[] { lft, rgt });
            return p;
        }

        // ── PRESETS ───────────────────────────────────────────────────────────
        Panel BuildPRESETS()
        {
            var p = new Panel { BackColor = BG };
            p.Controls.Add(Lbl("Server Presets", TXT, 10, 10, FNTB));
            p.Controls.Add(Lbl("recommended configs for los poderosisimos members", DIM, 10, 26, FNT));
            p.Controls.Add(HSep(10, 40, CW - 20));

            // Scrollable cards area
            var scroll = new Panel {
                Location = new Point(10, 48),
                Size = new Size(CW - 20, 310),
                AutoScroll = true,
                BackColor = BG
            };
            p.Controls.Add(scroll);

            // Add preset section
            var addBox = Box(10, 364, CW - 20, 42);
            addBox.Controls.Add(Lbl("add custom preset:", DIM, 8, 8, FNT));

            var txName = new FlatTextBox { Text = "server name", Font = FNT, Location = new Point(160, 6), Size = new Size(130, 18) };
            var txCps  = new FlatTextBox { Text = "15.0",        Font = FNT, Location = new Point(300, 6), Size = new Size(55, 18) };

            var dRandAdd = new FlatDrop { Location = new Point(365, 6), Size = new Size(90, 18) };
            dRandAdd.Items.AddRange(new object[] { "jitter", "butterfly", "nodelay", "manual" });
            dRandAdd.SelectedIndex = 2;

            var btnAdd = BoxBtn("+ add", TXT, 464, 5, 60, 20);
            btnAdd.Click += (s, e) => {
                string nm = txName.Text.Trim();
                if (nm.Length == 0 || nm == "server name") return;
                double cps = 13.0;
                double.TryParse(txCps.Text.Trim().Replace(",","."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out cps);
                int rm = dRandAdd.SelectedIndex;
                var pr = new lospoderosos_lite.Config.PresetConfig { Name = nm, Server = nm, Cps = cps, RandMode = rm, IsBuiltIn = false };
                _cfg.Presets.Add(pr);
                txName.Text = "server name";
                txCps.Text  = "15.0";
                RefreshPresetCards(scroll);
            };

            addBox.Controls.Add(txName);
            addBox.Controls.Add(txCps);
            addBox.Controls.Add(AccentBorderWrap(dRandAdd, 365, 5, 92, 20));
            addBox.Controls.Add(btnAdd);
            p.Controls.Add(addBox);

            RefreshPresetCards(scroll);
            return p;
        }

        void RefreshPresetCards(Panel scroll)
        {
            scroll.Controls.Clear();

            const int CARD_W = 400;
            const int CARD_H = 115;
            const int GAP    = 12;
            int col = 0, row = 0;

            foreach (var pr in _cfg.Presets)
            {
                var pr_capture = pr; // closure capture
                int cx = col * (CARD_W + GAP);
                int cy = row * (CARD_H + GAP);

                var card = new Panel {
                    Location  = new Point(cx, cy),
                    Size      = new Size(CARD_W, CARD_H),
                    BackColor = PBG
                };
                card.Paint += (s, e) => {
                    var g = e.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    Color acc = _accentColor;
                    // Glow
                    for (int i = 2; i >= 0; i--) {
                        int alpha = 18 - i * 4;
                        using (var path = RoundedRect(i, i, card.Width - 1 - i * 2, card.Height - 1 - i * 2, 8))
                        using (Pen pen = new Pen(Color.FromArgb(Math.Max(0, alpha), acc), i + 1))
                            g.DrawPath(pen, path);
                    }
                    // Border
                    using (var path = RoundedRect(0, 0, card.Width - 1, card.Height - 1, 8))
                    using (Pen pen = new Pen(Color.FromArgb(pr_capture.IsBuiltIn ? 160 : 100, acc), 1))
                        g.DrawPath(pen, path);
                };

                // Server name title
                card.Controls.Add(Lbl(pr.Name, TXT, 10, 8, FNTB));

                // Built-in tag (right-aligned using Paint to avoid label overlap)
                if (pr.IsBuiltIn) {
                }

                // CPS — single combined label to prevent clipping
                int infoY = pr.IsBuiltIn ? 42 : 30;
                string cpsText = "average cps: " + pr.Cps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                card.Controls.Add(Lbl(cpsText, TXT, 10, infoY, FNT));

                // Rand mode — single combined label
                string randText = "randomization: " + pr.RandModeName();
                card.Controls.Add(Lbl(randText, TXT, 10, infoY + 18, FNT));

                // Load button
                int btnY = infoY + 42;
                var btnLoad = BoxBtn("load preset", TXT, 10, btnY, 120, 22);
                btnLoad.Click += (s2, e2) => {
                    _cfg.AverageCps = pr_capture.Cps;
                    _cfg.RandMode   = pr_capture.RandMode;
                    if (_sldrCps != null) { _sldrCps.Value = _cfg.AverageCps; _sldrCps.Invalidate(); }
                    if (_dRand   != null) { _dRand.SelectedIndex = _cfg.RandMode; }
                    SetTab(0); // Switch to LMB tab so user sees the changes
                    if (_cfg.RandMode == 3) {
                        using (var crf = new CustomRandForm(_cfg, _accentColor)) {
                            crf.ShowDialog();
                        }
                    }
                };
                card.Controls.Add(btnLoad);

                // Server label
                card.Controls.Add(Lbl(pr.Server, DIM, 140, btnY + 3, FNT));

                // Delete button (only for user presets)
                if (!pr.IsBuiltIn) {
                    var btnDel = BoxBtn("x", Color.FromArgb(180, 60, 60), CARD_W - 28, 5, 22, 18);
                    btnDel.Click += (s2, e2) => {
                        _cfg.Presets.Remove(pr_capture);
                        RefreshPresetCards(scroll);
                    };
                    card.Controls.Add(btnDel);
                }

                scroll.Controls.Add(card);

                col++;
                if (col >= 2) { col = 0; row++; }
            }

            // Update scroll content height
            int totalRows = (_cfg.Presets.Count + 1) / 2;
            scroll.AutoScrollMinSize = new Size(0, totalRows * (CARD_H + GAP));
        }


        void LoadSounds()
        {
            _dSnd.Items.Clear();
            _dSnd.Items.Add("None");
            string d1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "XVA", "resource");
            if (Directory.Exists(d1))
                foreach (string f in Directory.GetFiles(d1, "*.wav"))
                    _dSnd.Items.Add(Path.GetFileName(f));
            string d2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "lospoderosos", "resource");
            if (Directory.Exists(d2))
                foreach (string f in Directory.GetFiles(d2, "*.wav"))
                    if (!_dSnd.Items.Contains(Path.GetFileName(f)))
                        _dSnd.Items.Add(Path.GetFileName(f));
            _dSnd.SelectedIndex = 0;
            for (int i = 0; i < _dSnd.Items.Count; i++)
                if (_dSnd.Items[i].ToString() == _cfg.Sound) { _dSnd.SelectedIndex = i; break; }
        }

        // ── REC ───────────────────────────────────────────────────────────────
        Panel BuildREC()
        {
            var p = new Panel { BackColor = BG };
            p.Controls.Add(Lbl("Macro Recorder", TXT, 10, 10, FNTB));
            p.Controls.Add(HSep(10, 30, CW - 20));

            var lft = Box(10, 40, LW, BH);
            _lblRecSt = Lbl("Status: Idle | Events: 0", DIM, 8, 122, FNT);

            var bRec  = BoxBtn("\x25CF REC",  Color.FromArgb(210, 70, 70),  8,   10, 100, 24);
            var bPlay = BoxBtn("\x25B6 PLAY", Color.FromArgb(80, 190, 80), 116, 10, 100, 24);
            var bStop = BoxBtn("\x25A0 STOP", DIM,                          224, 10, 100, 24);
            bRec.Click  += (s,e) => _recorder.StartRecord();
            bPlay.Click += (s,e) => _recorder.StartPlay();
            bStop.Click += (s,e) => _recorder.Stop();

            var chkLp = new FlatCheck("Loop Playback", _recorder.LoopPlayback) { Location = new Point(8, 44) };
            chkLp.Click += (s,e) => _recorder.LoopPlayback = chkLp.Checked;

            lft.Controls.Add(Lbl("Playback Speed:", TXT, 8, 70, FNT));
            var dSpd = new FlatDrop { Size = new Size(150, 18) };
            dSpd.Items.AddRange(new object[] { "0.5x", "0.75x", "1.0x", "1.5x", "2.0x" });
            dSpd.SelectedIndex = 2;
            double[] spds = { 0.5, 0.75, 1.0, 1.5, 2.0 };
            dSpd.SelectedIndexChanged += (s,e) => _recorder.PlaybackSpeed = spds[dSpd.SelectedIndex];

            lft.Controls.Add(Lbl("Save / Load Macro:", TXT, 8, 148, FNT));
            var txN = new FlatTextBox {
                Text = "macro1", Font = FNT, Location = new Point(8, 166), Size = new Size(150, 18)
            };
            var bSav = BoxBtn("Save", TXT, 166, 165, 55, 20);
            var bLod = BoxBtn("Load", TXT, 228, 165, 55, 20);
            bSav.Click += (s,e) => _recorder.SaveMacro(txN.Text);
            bLod.Click += (s,e) => _recorder.LoadMacro(txN.Text);

            lft.Controls.AddRange(new Control[] { bRec, bPlay, bStop, chkLp, AccentBorderWrap(dSpd, 7, 87, 152, 20), _lblRecSt, txN, bSav, bLod });

            // Right notes
            var rgt = Box(RX, 40, RW, BH);
            rgt.Controls.Add(Lbl("REC Notes", TXT, 8, 10, FNTB));
            rgt.Controls.Add(HSep(8, 28, RW - 20));
            string[] notes = {
                "Press REC to start recording clicks.",
                "Press STOP to end the recording.",
                "Press PLAY to replay the recorded macro.",
                "Enable Loop for continuous playback.",
                "",
                "Save macros by entering a name and",
                "clicking Save. Macros are stored in",
                "the 'configs' subfolder.",
                "",
                "Playback speed scales all time intervals.",
                "0.5x = twice as slow. 2.0x = twice as fast."
            };
            int ry = 36;
            foreach (string n in notes) { rgt.Controls.Add(Lbl(n, DIM, 8, ry, FNT)); ry += 16; }

            p.Controls.AddRange(new Control[] { lft, rgt });
            return p;
        }

        // ── MISC ──────────────────────────────────────────────────────────────
        Panel BuildMISC()
        {
            var p = new Panel { BackColor = BG };
            p.Controls.Add(Lbl("misc", TXT, 10, 10, FNTB));
            p.Controls.Add(HSep(10, 30, CW - 20));

            // Column 1: Destruct (y=40, w=250)
            var bDestruct = Box(10, 40, 250, 115);
            bDestruct.Controls.Add(Lbl("destruct", TXT, 10, 8, FNTB));

            var btnFlush = BoxBtn("flush dns", TXT, 10, 30, 230, 20);
            btnFlush.Click += (s, e) => {
                try {
                    System.Diagnostics.Process.Start("cmd.exe", "/c \"ipconfig /flushdns & pause\"");
                } catch { }
            };
            bDestruct.Controls.Add(btnFlush);

            _btnDestructBind = BoxBtn(_cfg.DestructBind == 0 ? "bindable destruct" : "destruct bind: " + KeyName(_cfg.DestructBind), TXT, 10, 55, 230, 22);
            _btnDestructBind.Click += (s, e) => { 
                _bindMode = true; _bindHide = false; _waitRelease = true; 
                _bindDestruct = true; 
                _btnDestructBind.Text = "...press key..."; 
                _bindTimer.Start(); 
            };
            bDestruct.Controls.Add(_btnDestructBind);

            var btnDestruct = BoxBtn("destruct", TXT, 10, 80, 230, 22);
            btnDestruct.Click += (s, e) => { this.Close(); };
            bDestruct.Controls.Add(btnDestruct);

            // Column 1: Hide (y=165, w=250)
            var bHide = Box(10, 165, 250, 105);
            bHide.Controls.Add(Lbl("hide", TXT, 10, 8, FNTB));
            
            var chkHideTask = new FlatCheck("Hide from taskbar", _cfg.HideTaskbar) { Location = new Point(10, 30) };
            chkHideTask.Click += (s, e) => { 
                _cfg.HideTaskbar = chkHideTask.Checked; 
                this.ShowInTaskbar = !_cfg.HideTaskbar; 
            };
            bHide.Controls.Add(chkHideTask);

            var chkStreamer = new FlatCheck("Streamer mode", _cfg.StreamerMode) { Location = new Point(10, 50) };
            chkStreamer.Click += (s, e) => { _cfg.StreamerMode = chkStreamer.Checked; };
            bHide.Controls.Add(chkStreamer);

            _btnHide = BoxBtn(_cfg.HideBind == 0 ? "hide" : "hide bind: " + KeyName(_cfg.HideBind), TXT, 10, 75, 230, 22);
            _btnHide.Click += (s, e) => { _bindDestruct = false; BeginBind(true); };
            bHide.Controls.Add(_btnHide);

            // Column 2: Settings (x=270, y=40, w=270)
            var bSettings = Box(270, 40, 270, 340);
            bSettings.Controls.Add(Lbl("settings", TXT, 10, 8, FNTB));
            
            bSettings.Controls.Add(Lbl("Visual Settings:", TXT, 10, 30, FNTB));
            _chkParticle = new FlatCheck("Particle Effect", _cfg.ParticleEnabled) { Location = new Point(10, 50) };
            _chkParticle.Click += (s,e) => { _cfg.ParticleEnabled = _chkParticle.Checked; if (_particleOverlay != null) _particleOverlay.ParticlesEnabled = _chkParticle.Checked; };
            bSettings.Controls.Add(_chkParticle);

            _btnColor = BoxBtn("Accent Color", TXT, 10, 75, 110, 22);
            _btnColor.Click += (s,e) => {
                using (var cd = new ColorDialog()) {
                    cd.Color = Color.FromArgb(_cfg.ColorAccent);
                    cd.AnyColor = true;
                    cd.FullOpen = true;
                    if (cd.ShowDialog() == DialogResult.OK) {
                        int argb = cd.Color.ToArgb();
                        _cfg.ColorAccent = argb;
                        ApplyAccentToAll(cd.Color);
                    }
                }
            };
            bSettings.Controls.Add(_btnColor);

            // Column 3: About (x=550, y=40, w=290)
            var bAbout = Box(550, 40, 290, 340);
            bAbout.Controls.Add(Lbl("about", TXT, 10, 8, FNTB));

            bAbout.Controls.Add(Lbl("licensed to joacodemon", DIM, 10, 40, FNT));
            bAbout.Controls.Add(Lbl("• expiration date: never", DIM, 15, 60, FNT));

            bAbout.Controls.Add(Lbl("about los poderosisimos", DIM, 10, 100, FNT));
            bAbout.Controls.Add(Lbl("• build version: 2.2.9", DIM, 15, 120, FNT));
            bAbout.Controls.Add(Lbl("• build type: faction", DIM, 15, 140, FNT));

            bAbout.Controls.Add(HSep(10, 200, 270));
            var lblCC = Lbl("los poderosisimos © 2026", TXT, 0, 220, FNT);
            lblCC.Location = new Point((290 - lblCC.Width) / 2, 220);
            bAbout.Controls.Add(lblCC);
            var lblRights = Lbl("all rights reserved", DIM, 0, 240, FNT);
            lblRights.Location = new Point((290 - lblRights.Width) / 2, 240);
            bAbout.Controls.Add(lblRights);

            p.Controls.AddRange(new Control[] { bDestruct, bHide, bSettings, bAbout });
            return p;
        }

        // ── UI Helpers ────────────────────────────────────────────────────────
        Label Lbl(string t, Color c, int x, int y, Font f)
        {
            return new Label { Text = t, Font = f, ForeColor = c, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent };
        }
        Panel HSep(int x, int y, int w)
        {
            return new Panel { Location = new Point(x, y), Size = new Size(w, 1), BackColor = SEP };
        }
        Panel Box(int x, int y, int w, int h)
        {
            var b = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = PBG };
            b.Paint += (s,e) => {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color acc = _accentColor;
                int bw = b.Width, bh = b.Height;
                // Glow
                for (int i = 2; i >= 0; i--)
                {
                    int alpha = 20 - i * 5;
                    using (var path = RoundedRect(i, i, bw - 1 - i * 2, bh - 1 - i * 2, 8))
                    using (Pen p = new Pen(Color.FromArgb(Math.Max(0, alpha), acc), i + 1))
                        g.DrawPath(p, path);
                }
                // Main rounded border
                using (var path = RoundedRect(0, 0, bw - 1, bh - 1, 8))
                using (Pen p = new Pen(Color.FromArgb(150, acc), 1))
                    g.DrawPath(p, path);
            };
            return b;
        }
        Button SysBtn(string t, int x, int y)
        {
            var b = new Button { Text = t, Font = FNT, Size = new Size(26, 26), Location = new Point(x, y),
                FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = DIM, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            return b;
        }
        Button SideBtn(string t, int y)
        {
            var b = new Button { Text = t, Font = FNT, Size = new Size(68, 26), Location = new Point(11, y),
                FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = DIM, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 35, 35);
            return b;
        }
        Button BoxBtn(string t, Color fg, int x, int y, int w, int h)
        {
            return new FlatButton(t, fg) { Size = new Size(w, h), Location = new Point(x, y) };
        }

        private void UpdateCustomBar(Point pt, Panel panel)
        {
            int numBars = 25;
            float barWidth = (panel.Width - 40) / (float)numBars;
            float maxH = panel.Height - 40;
            int index = (int)((pt.X - 20) / barWidth);
            if (index >= 0 && index < numBars)
            {
                float y = pt.Y;
                float h = panel.Height - 20 - y;
                double weight = h / maxH;
                weight = Math.Max(0, Math.Min(1, weight));
                _cfg.CustomCpsWeights[index] = weight;
                panel.Invalidate();
            }
        }

        private void UpdateCustomStats()
        {
            double sum = 0, exp = 0;
            for (int i = 0; i < 25; i++)
            {
                sum += _cfg.CustomCpsWeights[i];
                exp += (i + 1) * _cfg.CustomCpsWeights[i];
            }
            if (sum > 0)
            {
                double avg = exp / sum;
                double var = 0;
                for (int i = 0; i < 25; i++)
                {
                    double p = _cfg.CustomCpsWeights[i] / sum;
                    var += p * Math.Pow((i + 1) - avg, 2);
                }
                _customStats.Text = string.Format("Statistics:\nAverage CPS: {0:F2}\nVariance: {1:F2}\nTotal Weight: {2:F2}", avg, var, sum);
            }
            else
            {
                _customStats.Text = "Statistics:\nAverage CPS: 0.00\nVariance: 0.00\nTotal Weight: 0.00";
            }
        }

        void SetTab(int t)
        {
            _tab = t;
            _pLmb.Visible     = (t == 0);
            _pRec.Visible     = (t == 1);
            _pMisc.Visible    = (t == 2);
            _pPresets.Visible = (t == 3);
            RefreshSide();
        }
        void RefreshSide()
        {
            RefSB(_bLmb,     _tab == 0);
            RefSB(_bRec,     _tab == 1);
            RefSB(_bMisc,    _tab == 2);
            RefSB(_bPresets, _tab == 3);
        }
        void RefSB(Button b, bool on)
        {
            if (b == null) return;
            b.BackColor = on ? Color.FromArgb(35, 35, 35) : SBG;
            b.ForeColor = on ? TXT : DIM;
            if (on)
            {
                b.FlatAppearance.BorderSize  = 1;
                b.FlatAppearance.BorderColor = SEP;
            }
            else
            {
                b.FlatAppearance.BorderSize  = 0;
            }
        }

        private void DrawCustomChart(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int numBars = 25;
            float barWidth = ((Panel)sender).Width - 40;
            barWidth /= numBars;
            float maxH = ((Panel)sender).Height - 40;

            for (int i = 0; i < numBars; i++)
            {
                float x = 20 + i * barWidth;
                float w = barWidth - 4;
                double weight = _cfg.CustomCpsWeights[i];
                float h = (float)(weight * maxH);
                if (h > maxH) h = maxH;
                if (h < 0) h = 0;
                float y = ((Panel)sender).Height - 20 - h;

                using (Brush b = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    g.FillRectangle(b, x, 20, w, maxH);
                using (Brush b = new SolidBrush(weight > 0 ? _accentColor : DIM))
                    g.FillRectangle(b, x, y, w, h);
                SizeF sz = g.MeasureString((i + 1).ToString(), FNT);
                using (Brush b = new SolidBrush(DIM))
                    g.DrawString((i + 1).ToString(), FNT, b, x + (w - sz.Width) / 2, ((Panel)sender).Height - 18);
                if (weight > 0)
                {
                    string txt = weight.ToString("0.00");
                    SizeF tsz = g.MeasureString(txt, new Font("Courier New", 6F));
                    using (Brush b = new SolidBrush(TXT))
                        g.DrawString(txt, new Font("Courier New", 6F), b, x + (w - tsz.Width) / 2, y - 10);
                }
            }
        }

        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { Win32.ReleaseCapture(); Win32.SendMessage(Handle, 0xA1, 2, 0); }
        }
        // Mouse interaction handlers for custom chart
        private void CustomChartMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateCustomBar(e.Location, (Panel)sender);
            }
        }

        private void CustomChartMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateCustomBar(e.Location, (Panel)sender);
            }
        }

        private void CustomChartMouseUp(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                UpdateCustomStats();
            }
        }

        // ── Thread-safe UI updates ────────────────────────────────────────────
        void SafeSet(Label l, string t)
        {
            if (l == null || !l.IsHandleCreated) return;
            if (l.InvokeRequired) l.Invoke(new Action(() => l.Text = t));
            else l.Text = t;
        }
        void SafeInvoke(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) try { Invoke(a); } catch { }
            else a();
        }

        // ── Accent border wrapper for dropdowns ─────────────────────────────
        Panel AccentBorderWrap(Control ctrl, int x, int y, int w, int h)
        {
            var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.Transparent };
            p.Paint += (s, e) => {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(Color.FromArgb(180, _accentColor), 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            ctrl.Location = new Point(1, 1);
            ctrl.Size = new Size(w - 2, h - 2);
            p.Controls.Add(ctrl);
            return p;
        }

        // ── Key binding ───────────────────────────────────────────────────────
        string KeyName(int vk)
        {
            if (vk <= 0) return "none";
            if (vk == 1) return "LClick";
            if (vk == 2) return "RClick";
            if (vk == 4) return "MClick";
            if (vk == 5) return "Side1";
            if (vk == 6) return "Side2";
            if (vk >= 48 && vk <= 57) return ((char)vk).ToString();
            if (vk >= 65 && vk <= 90) return ((char)vk).ToString();
            return ((Keys)vk).ToString();
        }
        void BeginBind(bool hide)
        {
            _bindMode = true; _bindHide = hide; _waitRelease = true;
            if (hide) _btnHide.Text = "...press key...";
            else      _btnBind.Text = "...press key...";
            _bindTimer.Start();
        }
        void BindTick(object s, EventArgs e)
        {
            if (!_bindMode) { _bindTimer.Stop(); return; }

            // Phase 1: Wait for ALL keys/buttons to be released
            if (_waitRelease)
            {
                bool anyDown = false;
                for (int i = 1; i < 7; i++) // Check mouse buttons 1-6
                    if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0) { anyDown = true; break; }
                if (!anyDown)
                    for (int i = 8; i < 256; i++) // Check keyboard keys
                        if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0) { anyDown = true; break; }
                if (!anyDown) _waitRelease = false;
                return;
            }

            // Phase 2: Detect the next key/button press
            // Check mouse buttons first (1=LMB, 2=RMB, 4=MClick, 5=X1/Side1, 6=X2/Side2)
            int[] mouseButtons = { 5, 6, 4, 2, 1 }; // Side buttons first, then middle, right, left
            foreach (int mb in mouseButtons)
            {
                if ((Win32.GetAsyncKeyState(mb) & 0x8000) != 0)
                {
                    FinishBind(mb);
                    return;
                }
            }

            // Check keyboard keys (8-255)
            for (int i = 8; i < 256; i++)
            {
                if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0)
                {
                    FinishBind(i);
                    return;
                }
            }
        }
        void FinishBind(int vk)
        {
            _bindMode = false; _bindTimer.Stop();
            if (_bindDestruct) { _cfg.DestructBind = vk; if (_btnDestructBind != null) _btnDestructBind.Text = "destruct bind: " + KeyName(vk); _bindDestruct = false; }
            else if (_bindHide) { _cfg.HideBind  = vk; if (_btnHide != null) _btnHide.Text = "hide bind: " + KeyName(vk); }
            else           { _cfg.ClickBind = vk; if (_btnBind != null) _btnBind.Text = "Bind: " + KeyName(vk); }
        }

        // ── Config I/O ────────────────────────────────────────────────────────
        void SaveCfg(string name)
        {
            try {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name + ".json"), _cfg.ToJson());
            } catch { }
        }
        void LoadCfg(string name)
        {
            try {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs", name + ".json");
                if (!File.Exists(path)) return;
                var loaded = AppConfig.FromJson(File.ReadAllText(path));
                _cfg.AverageCps = loaded.AverageCps; _cfg.Mode = loaded.Mode;
                _cfg.BBMode = loaded.BBMode; _cfg.OnlyInGame = loaded.OnlyInGame; _cfg.RmbLock = loaded.RmbLock;
                _cfg.WorkInMenus = loaded.WorkInMenus; _cfg.DiscordRpc = loaded.DiscordRpc;
                _cfg.DiscordAppId = loaded.DiscordAppId;
                _cfg.Sound = loaded.Sound;
                _cfg.ClickBind = loaded.ClickBind; _cfg.HideBind = loaded.HideBind;
                _cfg.RandMode = loaded.RandMode;
                _cfg.ColorAccent = loaded.ColorAccent; _cfg.ParticleEnabled = loaded.ParticleEnabled;
                _cfg.RefillMode = loaded.RefillMode;
                _cfg.PingMs = loaded.PingMs;
                if (_particleOverlay != null) {
                    _particleOverlay.AccentArgb = _cfg.ColorAccent;
                    _particleOverlay.ParticlesEnabled = _cfg.ParticleEnabled;
                }
                SyncUI();
            } catch { }
        }
        void SyncUI()
        {
            if (_sldrCps != null) { _sldrCps.Max = 50.0; _sldrCps.Value = _cfg.AverageCps; _sldrCps.Invalidate(); }
            if (_sldrPing != null) { _sldrPing.Value = _cfg.PingMs; _sldrPing.Invalidate(); }
            if (_chkOig  != null) { _chkOig.Checked  = _cfg.OnlyInGame;  _chkOig.Invalidate(); }
            if (_chkRmb  != null) { _chkRmb.Checked  = _cfg.RmbLock;     _chkRmb.Invalidate(); }
            if (_chkWim  != null) { _chkWim.Checked  = _cfg.WorkInMenus; _chkWim.Invalidate(); }
            if (_chkRefill != null) { _chkRefill.Checked = _cfg.RefillMode; _chkRefill.Invalidate(); }
            if (_chkRpc  != null) { _chkRpc.Checked  = _cfg.DiscordRpc;  _chkRpc.Invalidate(); }

            if (_chkParticle != null) { _chkParticle.Checked = _cfg.ParticleEnabled; _chkParticle.Invalidate(); }
            if (_txAppId != null) { _txAppId.Text    = _cfg.DiscordAppId; }
            if (_dMode   != null) _dMode.SelectedIndex = _cfg.Mode;
            if (_dBB     != null) _dBB.SelectedIndex   = _cfg.BBMode;
            if (_dRand != null) _dRand.SelectedIndex = _cfg.RandMode;
            if (_btnColor != null) ApplyAccentToAll(Color.FromArgb(_cfg.ColorAccent));
            if (_btnBind != null) _btnBind.Text = _cfg.ClickBind == 0 ? "Bind: none" : "Bind: " + KeyName(_cfg.ClickBind);
            if (_btnHide != null) _btnHide.Text = _cfg.HideBind  == 0 ? "Click to Bind" : "Key: " + KeyName(_cfg.HideBind);

            if (_cfg.DiscordRpc) _misc.StartRpc(); else _misc.StopRpc();
            LoadSounds();
        }

        void LoadBackgroundImage()
        {
            try
            {
                string imgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "background.png");
                if (File.Exists(imgPath))
                {
                    using (var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read))
                    using (Image temp = Image.FromStream(fs))
                    {
                        this.BackgroundImage = new Bitmap(temp);
                    }
                    this.BackgroundImageLayout = ImageLayout.Stretch;
                }
            }
            catch { }
        }

        void LoadTaskbarIcon()
        {
            try
            {
                string imgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                if (!File.Exists(imgPath))
                    imgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "background.png");
                if (File.Exists(imgPath))
                {
                    using (var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read))
                    using (Image temp = Image.FromStream(fs))
                    using (Bitmap bmp = new Bitmap(temp))
                    using (Bitmap resized = new Bitmap(bmp, new Size(32, 32)))
                    {
                        // stray UI block removed
                        this.Icon = Icon.FromHandle(resized.GetHicon());
                    }
                }
            }
            catch { }
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _bindTimer.Stop();
            _clicker.Stop();
            _recorder.Stop();
            _misc.Stop();
            Win32.StopMouseHook();

            // Closing form removed

            base.OnFormClosing(e);
        }
// stray UI block removed

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(200, 50, 50), 2))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
