using System;
using System.Threading;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using System.Windows.Forms;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ImGuiNET;
using Horimiya.Config;
using Horimiya.Modules;
using Horimiya.Utils;

public class ImGuiForm : Form
{
    private OpenTK.GLControl _glControl;
    private ImGuiController _controller;
    
    private AppConfig _cfg;
    private Clicker _clicker;
    private RightClicker _rightClicker;
    private Recorder _recorder;
    private Misc _misc;

    // Binds
    private bool _bindMode = false;
    private int _bindingTarget = 0; // 0: Click, 1: Hide, 2: Destruct
    private bool _waitRelease = false;
    private System.Windows.Forms.Timer _bindTimer;

    // Textures
    private int _texLogo   = 0;
    private int _texSword  = 0;
    private int _texGear   = 0;
    private int _texBg     = 0;
    private int _texEaster = 0;
    private bool _showBg   = true;

    private string _recMacroName = "macro1";
    private string _presetAddName = "server name";
    private string _presetAddServer = "";
    private long _lastAutoSwitchTick = 0;
    private string _lastMatchedPreset = null;
    private float _presetAddCps = 15.0f;

    // ── In-app switch toast ──
    private string _toastServer = "";
    private string _toastCps   = "";
    private float  _toastAlpha  = 0f;    // 0=hidden, 1=fully visible
    private long   _toastShowMs = 0;     // Environment.TickCount when shown
    private const int TOAST_LIFE_MS  = 3500;
    private const int TOAST_FADE_MS  = 600;
    private int _presetAddRand = 2;
    private string _recorderStatus = "Status: Idle | Events: 0";

    private Stopwatch _frameSw = Stopwatch.StartNew();
    private volatile bool _disposed = false;
    private const double TARGET_FRAME_TIME_MS = 1000.0 / 60.0; // 60 FPS cap

    // Cached sound list to avoid filesystem scanning every frame
    private List<string> _cachedSounds = null;
    private long _soundsCacheTimeMs = 0;
    
    private Horimiya.IoT.IoTManager _iot;

    public ImGuiForm(AppConfig cfg, Clicker clicker, RightClicker rightClicker, Recorder recorder, Misc misc)
    {
        _cfg = cfg;
        _clicker = clicker;
        _rightClicker = rightClicker;
        _recorder = recorder;
        _misc = misc;

        _iot = new Horimiya.IoT.IoTManager(_cfg);
        _iot.MessageReceived += OnIotMessageReceived;
        _ = _iot.StartAsync();

        Text = "Horimiya";
        ClientSize = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.None;  // Borderless — custom title bar in ImGui
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        
        // Allow window to be dragged via the top bar area (P/Invoke WM_NCLBUTTONDOWN)
        _glControl_MouseDown_Drag = true;

        _glControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 4));
        _glControl.Dock = DockStyle.Fill;
        Controls.Add(_glControl);

        _bindTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _bindTimer.Tick += BindTick;

        _recorder.StatusChanged += s => { _recorderStatus = s; };
        _misc.ClickBindTriggered += () =>
        { 
            _clicker.Clicking = !_clicker.Clicking; 
        };
        _misc.RightClickBindTriggered += () =>
        { 
            _rightClicker.Clicking = !_rightClicker.Clicking; 
        };
        _misc.HideBindTriggered += () => { 
            if (Visible && WindowState != FormWindowState.Minimized) 
            {
                Hide(); 
            }
            else 
            { 
                Show(); 
                WindowState = FormWindowState.Normal;
                BringToFront(); 
            } 
        };
        _misc.DestructBindTriggered += () => { 
            Close(); 
        };

        Load += OnLoad;
        FormClosing += OnFormClosing;
        
        SetupInputEvents();
    }
    
    // ── Borderless drag support ──
    private bool _glControl_MouseDown_Drag = false;
    private bool _isDragging = false;
    private Point _dragStart;
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
    
    private void StartWindowDrag()
    {
        if (!_isDragging)
        {
            _isDragging = true;
            _dragStart = Cursor.Position;
        }
        else
        {
            var cur = Cursor.Position;
            if (cur != _dragStart)
            {
                this.Location = new Point(
                    this.Location.X + (cur.X - _dragStart.X),
                    this.Location.Y + (cur.Y - _dragStart.Y)
                );
                _dragStart = cur;
            }
        }
    }

    private void SetupInputEvents()
    {
        _glControl.MouseMove += (s, e) => _controller?.SetMousePosition(e.X, e.Y);
        _glControl.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Left) _controller?.SetMouseButton(0, true);
            if (e.Button == MouseButtons.Right) _controller?.SetMouseButton(1, true);
            if (e.Button == MouseButtons.Middle) _controller?.SetMouseButton(2, true);
        };
        _glControl.MouseUp += (s, e) => {
            if (e.Button == MouseButtons.Left) _controller?.SetMouseButton(0, false);
            if (e.Button == MouseButtons.Right) _controller?.SetMouseButton(1, false);
            if (e.Button == MouseButtons.Middle) _controller?.SetMouseButton(2, false);
        };
        _glControl.MouseWheel += (s, e) => _controller?.AddMouseWheel(e.Delta / 120f);
        _glControl.KeyPress += (s, e) => _controller?.AddTypedChar(e.KeyChar);
        _glControl.KeyDown += (s, e) => {
            _controller?.SetKeyDown((int)e.KeyCode, true);
            _controller?.SetModifiers(e.Control, e.Shift, e.Alt);
        };
        _glControl.KeyUp += (s, e) => {
            _controller?.SetKeyDown((int)e.KeyCode, false);
            _controller?.SetModifiers(e.Control, e.Shift, e.Alt);
        };
    }

    private void OnLoad(object sender, EventArgs e)
    {
        // Load icon from embedded logo PNG resource for the taskbar
        try
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ImGuiApp.Resources.logo.png"))
            {
                if (stream != null)
                {
                    var bmp = new Bitmap(stream);
                    // Build a proper multi-resolution icon
                    var icon = BuildIconFromBitmap(bmp);
                    if (icon != null) Icon = icon;
                }
            }
        }
        catch { }
        
        _glControl.MakeCurrent();
        _controller = new ImGuiController(ClientSize.Width, ClientSize.Height);

        _texLogo   = TextureHelper.LoadTextureFromResource("ImGuiApp.Resources.logo.png");
        _texSword  = TextureHelper.LoadTextureFromResource("ImGuiApp.Resources.sword.png");
        _texGear   = TextureHelper.LoadTextureFromResource("ImGuiApp.Resources.gear.png");
        _texBg     = TextureHelper.LoadTextureFromResource("ImGuiApp.Resources.background.png");
        _texEaster = TextureHelper.LoadTextureFromResource("ImGuiApp.Resources.easter.png");

        _glControl.VSync = false;
        
        Application.Idle += RenderLoop;
        LoadTaskbarIcon();
        ApplyTheme();
    }
    
    // Builds a proper Windows Icon from a Bitmap using correct ICO binary format
    private System.Drawing.Icon BuildIconFromBitmap(Bitmap source)
    {
        try
        {
            int[] sizes = { 256, 64, 48, 32, 16 };
            var ms = new MemoryStream();
            var bw = new System.IO.BinaryWriter(ms);
            
            // ICO header
            bw.Write((Int16)0);            // Reserved
            bw.Write((Int16)1);            // Type: 1 = ICO
            bw.Write((Int16)sizes.Length); // Number of images
            
            // Collect PNG data for each size
            var pngData = new List<byte[]>();
            foreach (int sz in sizes)
            {
                var resized = new Bitmap(source, sz, sz);
                var pngMs = new MemoryStream();
                resized.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
                pngData.Add(pngMs.ToArray());
                resized.Dispose();
            }
            
            // Directory entries (each is 16 bytes)
            int dataOffset = 6 + sizes.Length * 16; // 6 = header, 16 = each entry
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                bw.Write((byte)(sz >= 256 ? 0 : sz));  // Width (0 = 256)
                bw.Write((byte)(sz >= 256 ? 0 : sz));  // Height
                bw.Write((byte)0);     // Color count
                bw.Write((byte)0);     // Reserved
                bw.Write((Int16)1);   // Planes
                bw.Write((Int16)32);  // Bit count
                bw.Write((int)pngData[i].Length);
                bw.Write((int)dataOffset);
                dataOffset += pngData[i].Length;
            }
            
            // PNG data
            foreach (var data in pngData) bw.Write(data);
            
            ms.Seek(0, SeekOrigin.Begin);
            return new System.Drawing.Icon(ms);
        }
        catch { return null; }
    }

    // ── Exelon-style color constants ──
    private static readonly Vector4 _colBg       = new Vector4(0.047f, 0.047f, 0.055f, 1f);   // #0C0C0E
    private static readonly Vector4 _colCard     = new Vector4(0.08f, 0.08f, 0.09f, 1f);      // #141417
    private static readonly Vector4 _colCardBrd  = new Vector4(0.12f, 0.12f, 0.14f, 1f);
    private Vector4 _colAccent;
    private Vector4 _colAccentDim;
    private static readonly Vector4 _colText     = new Vector4(0.85f, 0.85f, 0.88f, 1f);
    private static readonly Vector4 _colTextDim  = new Vector4(0.45f, 0.45f, 0.50f, 1f);

    private void ApplyTheme()
    {
        _colAccent = new Vector4(_cfg.AccentR, _cfg.AccentG, _cfg.AccentB, 1f);
        _colAccentDim = new Vector4(_cfg.AccentR, _cfg.AccentG, _cfg.AccentB, 0.25f);
        var s = ImGui.GetStyle();
        s.Colors[(int)ImGuiCol.Text]            = _colText;
        s.Colors[(int)ImGuiCol.TextDisabled]    = _colTextDim;
        s.Colors[(int)ImGuiCol.WindowBg]        = _colBg;
        s.Colors[(int)ImGuiCol.ChildBg]         = new Vector4(0, 0, 0, 0); // transparent so cards draw themselves
        s.Colors[(int)ImGuiCol.Border]          = _colCardBrd;
        s.Colors[(int)ImGuiCol.FrameBg]         = new Vector4(0.10f, 0.10f, 0.12f, 1f);
        s.Colors[(int)ImGuiCol.FrameBgHovered]  = new Vector4(0.14f, 0.14f, 0.16f, 1f);
        s.Colors[(int)ImGuiCol.FrameBgActive]   = new Vector4(0.16f, 0.16f, 0.18f, 1f);
        s.Colors[(int)ImGuiCol.CheckMark]       = _colAccent;
        s.Colors[(int)ImGuiCol.SliderGrab]      = _colAccent;
        s.Colors[(int)ImGuiCol.SliderGrabActive]= new Vector4(0.45f, 0.52f, 0.90f, 1f);
        s.Colors[(int)ImGuiCol.Button]          = new Vector4(0.10f, 0.10f, 0.12f, 1f);
        s.Colors[(int)ImGuiCol.ButtonHovered]   = new Vector4(0.14f, 0.14f, 0.16f, 1f);
        s.Colors[(int)ImGuiCol.ButtonActive]    = new Vector4(0.18f, 0.18f, 0.20f, 1f);
        s.Colors[(int)ImGuiCol.Header]          = _colAccentDim;
        s.Colors[(int)ImGuiCol.HeaderHovered]   = new Vector4(0.36f, 0.42f, 0.80f, 0.35f);
        s.Colors[(int)ImGuiCol.HeaderActive]    = new Vector4(0.36f, 0.42f, 0.80f, 0.45f);
        s.Colors[(int)ImGuiCol.Separator]       = _colCardBrd;
        s.Colors[(int)ImGuiCol.PopupBg]         = _colCard;

        s.WindowRounding  = 12f;
        s.ChildRounding   = 10f;
        s.FrameRounding   = 6f;
        s.GrabRounding    = 6f;
        s.PopupRounding   = 8f;
        s.WindowPadding   = new Vector2(0, 0);
        s.FramePadding    = new Vector2(8, 5);
        s.ItemSpacing     = new Vector2(8, 8);
        s.WindowBorderSize= 0f;
        s.ChildBorderSize = 1f;

        _showBg = false;
        
        // Note: Fonts are loaded in ImGuiController constructor (segoeui.ttf or default)
    }

    private void LoadTaskbarIcon()
    {
        try
        {
            using (var bmp = TextureHelper.LoadBitmapFromResource("ImGuiApp.Resources.logo.png"))
            {
                if (bmp != null)
                {
                    // Resize to 64x64 to prevent GetHicon() from failing on very large images
                    using (var resizedBmp = new Bitmap(bmp, new Size(64, 64)))
                    {
                        this.Icon = Icon.FromHandle(resizedBmp.GetHicon());
                    }
                }
            }
        }
        catch { }
    }

    private void RenderLoop(object sender, EventArgs e)
    {
        while (IsAppIdle())
        {
            if (_disposed || _glControl == null || _glControl.IsDisposed) return;
            if (_frameSw.Elapsed.TotalMilliseconds < 16.0) { Thread.Sleep(1); continue; }
            try
            {
                CheckAutoSwitch();
                _glControl.MakeCurrent();
                GL.ClearColor(_colBg.X, _colBg.Y, _colBg.Z, 1f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                float dt = (float)_frameSw.Elapsed.TotalSeconds;
                if (dt <= 0) dt = 1f / 60f;
                _frameSw.Restart();
                _controller.Update(_glControl.Width, _glControl.Height, dt);
                DrawUI();
                _controller.Render();
                _glControl.SwapBuffers();
            }
            catch (Exception) { return; }
        }
    }

    private void CheckAutoSwitch()
    {
        if (!_cfg.AutoSwitchProfiles) return;
        long now = Environment.TickCount;
        // Use unchecked arithmetic to handle TickCount wraparound correctly
        if (unchecked(now - _lastAutoSwitchTick) < 1000) return;
        _lastAutoSwitchTick = now;

        // Build a combined string of ALL open window titles so we don't
        // require Minecraft to be in the foreground.
        string allTitles = GetAllWindowTitles();

        // ── DEBUG LOG ──
        // (removed)
        // ── END DEBUG ──

        foreach (var pr in _cfg.Presets)
        {
            if (string.IsNullOrEmpty(pr.Server) || pr.Server.Length <= 2) continue;
            string serverLower = pr.Server.ToLower();
            if (allTitles.Contains(serverLower))
            {
                if (_lastMatchedPreset != pr.Name)
                {
                    _lastMatchedPreset = pr.Name;
                    // Apply preset CPS
                    _cfg.AverageCps = pr.Cps;
                    _cfg.RandMode = pr.RandMode;
                    if (_cfg.RandomizationEnabled)
                    {
                        _cfg.MinCps = Math.Max(1.0, pr.Cps - 2.0);
                        _cfg.MaxCps = Math.Min(30.0, pr.Cps + 2.0);
                    }
                    else
                    {
                        _cfg.MinCps = pr.Cps;
                        _cfg.MaxCps = pr.Cps;
                    }
                    _cfg.Save();
                    _clicker.ResetTimingState();
                    ShowSwitchToast(pr.Server.Length > 0 ? pr.Server : pr.Name, pr.Cps, pr.RandModeName());
                    // Also show a system notification overlay
                    Horimiya.UI.NotificationOverlay.Show(
                        "Profile Switched",
                        $"{(pr.Server.Length > 0 ? pr.Server : pr.Name)} — {pr.Cps:F1} CPS ({pr.RandModeName()})",
                        Horimiya.UI.NotificationOverlay.NotificationType.Success,
                        _cfg.NotificationPosition);
                }
                return; // found a match — stop checking
            }
        }
        // No preset matched — reset so next match fires again
        _lastMatchedPreset = null;
    }

    // Returns all visible window titles concatenated (lowercased) for substring matching
    private string GetAllWindowTitles()
    {
        var sb = new System.Text.StringBuilder(8192);
        try
        {
            foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    string t = proc.MainWindowTitle;
                    if (!string.IsNullOrEmpty(t)) sb.Append(t.ToLower()).Append(' ');
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        // Also scan Minecraft log files from all known client paths.
        // This makes detection work with clients like CheatBreaker that
        // don't include the server name in their window title.
        try
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userprofile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var logPaths = new[]
            {
                // Vanilla / most clients
                System.IO.Path.Combine(appdata, ".minecraft", "logs", "latest.log"),
                // CheatBreaker
                System.IO.Path.Combine(appdata, ".cheatbreaker", "logs", "latest.log"),
                System.IO.Path.Combine(appdata, ".cheatbreaker", "game", "logs", "latest.log"),
                // Lunar Client (new path: profiles/<version>/logs/latest.log)
                System.IO.Path.Combine(userprofile, ".lunarclient", "profiles", "1.8", "logs", "latest.log"),
                System.IO.Path.Combine(userprofile, ".lunarclient", "profiles", "1.7", "logs", "latest.log"),
                System.IO.Path.Combine(userprofile, ".lunarclient", "profiles", "1.16", "logs", "latest.log"),
                System.IO.Path.Combine(userprofile, ".lunarclient", "profiles", "1.18", "logs", "latest.log"),
                System.IO.Path.Combine(userprofile, ".lunarclient", "profiles", "1.20", "logs", "latest.log"),
                // Lunar Client (old offline path - kept for compatibility)
                System.IO.Path.Combine(userprofile, ".lunarclient", "offline", "multiver", "logs", "latest.log"),
                System.IO.Path.Combine(userprofile, ".lunarclient", "offline", "1.8.9", "logs", "latest.log"),
                System.IO.Path.Combine(userprofile, ".lunarclient", "offline", "1.16.5", "logs", "latest.log"),
            };

            foreach (var path in logPaths)
            {
                if (!System.IO.File.Exists(path)) continue;
                try
                {
                    // Read the last 8 KB of the log — enough to catch recent "Connecting to" entries
                    // without reading the whole file (which can be several MB).
                    string server = ReadLastServerFromLog(path);
                    if (!string.IsNullOrEmpty(server))
                        sb.Append(server).Append(' ');
                }
                catch { }
            }
        }
        catch { }

        return sb.ToString();
    }

    // Reads the last ~8 KB of a Minecraft log file and extracts the most recent
    // "Connecting to IP, PORT" line, returning just the IP part (lowercased).
    private static string ReadLastServerFromLog(string logPath)
    {
        const int READ_TAIL = 8192;
        try
        {
            using (var fs = new System.IO.FileStream(logPath,
                       System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            {
                long offset = Math.Max(0, fs.Length - READ_TAIL);
                fs.Seek(offset, System.IO.SeekOrigin.Begin);
                var buf = new byte[fs.Length - offset];
                fs.Read(buf, 0, buf.Length);
                string tail = System.Text.Encoding.UTF8.GetString(buf).ToLower();

                // Minecraft logs: "[xx:xx:xx] [client thread/info]: connecting to ip, port"
                // We find the LAST occurrence so we pick the most recent server.
                const string marker = "connecting to ";
                int idx = tail.LastIndexOf(marker);
                if (idx < 0) return null;

                int start = idx + marker.Length;
                // The server IP ends at the comma before the port
                int comma = tail.IndexOf(',', start);
                if (comma < 0) return null;

                string ip = tail.Substring(start, comma - start).Trim();
                return ip; // e.g. "sololegends.net" or "minemen.club"
            }
        }
        catch { return null; }
    }

    private void ShowSwitchToast(string server, double cps, string randMode)
    {
        _toastServer = server;
        _toastCps    = $"{cps:F1} CPS  /  {randMode}";
        _toastAlpha  = 1f;
        _toastShowMs = Environment.TickCount;
    }

    // Called from DrawUI every frame — renders the server-switch toast if active
    private void DrawSwitchToast(float W)
    {
        if (_toastAlpha <= 0f) return;

        long elapsed = Environment.TickCount - _toastShowMs;

        // Fade out during the last TOAST_FADE_MS ms of life
        if (elapsed >= TOAST_LIFE_MS)
        {
            _toastAlpha = 0f;
            return;
        }
        if (elapsed >= TOAST_LIFE_MS - TOAST_FADE_MS)
        {
            float fadeProgress = (elapsed - (TOAST_LIFE_MS - TOAST_FADE_MS)) / (float)TOAST_FADE_MS;
            _toastAlpha = 1f - fadeProgress;
        }

        var dl = ImGui.GetWindowDrawList();

        // Position: right of the logo, vertically centred in the 46px header
        float x0 = 58f;
        float y0 = 8f;

        // Draw server name in accent purple
        var accentCol  = new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, _toastAlpha);
        var dimCol     = new Vector4(0.65f, 0.65f, 0.70f, _toastAlpha * 0.9f);

        // Tiny separator line on the left
        dl.AddRectFilled(
            new Vector2(x0, y0 + 3),
            new Vector2(x0 + 2, y0 + 29),
            ImGui.GetColorU32(accentCol), 1f);

        x0 += 7f;

        ImGui.SetCursorPos(new Vector2(x0, y0 + 1));
        ImGui.TextColored(accentCol, _toastServer);

        ImGui.SetCursorPos(new Vector2(x0, y0 + 18));
        ImGui.SetWindowFontScale(0.82f);
        ImGui.TextColored(dimCol, _toastCps);
        ImGui.SetWindowFontScale(1.0f);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeMessage { public IntPtr hWnd; public uint msg; public IntPtr wParam; public IntPtr lParam; public uint time; public System.Drawing.Point p; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    private bool IsAppIdle() { NativeMessage result; return PeekMessage(out result, IntPtr.Zero, 0, 0, 0) == 0; }

    // ── Tab state ──
    private int _currentTab = 0;
    private readonly string[] _tabNames = { "Left Clicker", "Right Clicker", "Settings" };

    // ── Helper: draw a progress bar ──
    private void DrawBar(string label, float fraction, string valueText, float barW = 220f)
    {
        ImGui.TextColored(_colTextDim, label);
        var dl = ImGui.GetWindowDrawList();
        var p  = ImGui.GetCursorScreenPos();
        float h = 6f;
        // bg
        dl.AddRectFilled(p, new Vector2(p.X + barW, p.Y + h), ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.18f, 1f)), 3f);
        // fill
        float fill = Math.Min(Math.Max(fraction, 0f), 1f) * barW;
        if (fill > 0)
            dl.AddRectFilled(p, new Vector2(p.X + fill, p.Y + h), ImGui.GetColorU32(_colAccent), 3f);
        ImGui.Dummy(new Vector2(barW, h + 4));
    }

    private void DrawUI()
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _isDragging = false;
        
        float W = 560, H = 420;
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(W, H));
        ImGui.Begin("##Main", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus);

        var drawList = ImGui.GetWindowDrawList();

        // ── Custom Top Bar Header Background ──
        drawList.AddRectFilled(new Vector2(0, 0), new Vector2(W, 46), ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1f)));

        // ── Top Left: transparent logo, framed cleanly and aligned with the header ──
        float logoX = 8f;
        float logoY = 4f;
        float logoSize = 36f;
        var logoCenter = new Vector2(logoX + logoSize / 2f, logoY + logoSize / 2f);
        drawList.AddCircleFilled(logoCenter, logoSize / 2f + 2f,
            ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.06f, 1f)), 24);
        drawList.AddCircle(logoCenter, logoSize / 2f + 2f,
            ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.20f, 1f)), 24, 1f);
        ImGui.SetCursorPos(new Vector2(logoX, logoY));
        ImGui.Image((IntPtr)_texLogo, new Vector2(logoSize, logoSize));

        // ── Server-switch toast (top-left, next to logo) ──
        DrawSwitchToast(W);

        // ── Top Right: Window controls (borderless) ──
        {
            const float BTN_W = 28f;
            const float BTN_H = 28f;
            float btnY = 9f;

            // X (close) button
            float closeX = W - BTN_W - 8f;
            ImGui.SetCursorPos(new Vector2(closeX, btnY));
            bool closeHovered = ImGui.IsMouseHoveringRect(
                ImGui.GetWindowPos() + new Vector2(closeX, btnY),
                ImGui.GetWindowPos() + new Vector2(closeX + BTN_W, btnY + BTN_H));
            ImGui.PushStyleColor(ImGuiCol.Button, closeHovered 
                ? new Vector4(0.85f, 0.15f, 0.15f, 0.85f) 
                : new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.1f, 0.1f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(1f, 0f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.85f));
            if (ImGui.Button("×##close", new Vector2(BTN_W, BTN_H))) Close();
            ImGui.PopStyleColor(4);

            // Minimize button
            float minX = closeX - BTN_W - 4f;
            ImGui.SetCursorPos(new Vector2(minX, btnY));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.10f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(1f, 1f, 1f, 0.15f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.80f, 1f));
            if (ImGui.Button("–##min", new Vector2(BTN_W, BTN_H))) WindowState = FormWindowState.Minimized;
            ImGui.PopStyleColor(4);
            
            // Drag zone: left of the tabs
            float dragLeftX = 50f;
            float dragLeftW = (W * 0.5f - 42) - dragLeftX;
            if (dragLeftW > 0)
            {
                ImGui.SetCursorPos(new Vector2(dragLeftX, 0));
                ImGui.InvisibleButton("##dragLeft", new Vector2(dragLeftW, 46f));
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 1f))
                    StartWindowDrag();
            }

            // Drag zone: right of the tabs
            float dragRightX = (W * 0.5f + 8) + 34f;
            float dragRightW = minX - dragRightX - 4f;
            if (dragRightW > 0)
            {
                ImGui.SetCursorPos(new Vector2(dragRightX, 0));
                ImGui.InvisibleButton("##dragRight", new Vector2(dragRightW, 46f));
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 1f))
                    StartWindowDrag();
            }
        }

        // ── Top Center: Tab Buttons (Sword & Gear) ──
        bool isSwordsActive = (_currentTab == 0 || _currentTab == 1);
        bool isGearActive = (_currentTab == 2);

        bool IconTabButton(string id, int tex, Vector2 pos, bool active)
        {
            const float BTN = 34f;
            const float PAD = -2f; // Negative padding to make the icon larger inside the button
            ImGui.SetCursorPos(pos);
            bool clicked = ImGui.InvisibleButton(id, new Vector2(BTN, BTN));
            bool hovered = ImGui.IsItemHovered();
            var p0 = ImGui.GetItemRectMin();
            var p1 = ImGui.GetItemRectMax();

            // No background box, let the black image canvas blend with the black header bar
            uint bgCol = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)); 

            // Icon tint (White = full color neon, Dim = inactive)
            uint tint = active
                ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f))
                : hovered 
                    ? ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.85f, 1f))
                    : ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f));

            if (tex != 0)
            {
                drawList.AddImage((IntPtr)tex,
                    new Vector2(p0.X + PAD, p0.Y + PAD),
                    new Vector2(p1.X - PAD, p1.Y - PAD),
                    Vector2.Zero, Vector2.One, tint);
            }
            return clicked;
        }

        // Swords tab button
        if (IconTabButton("##tabSwords", _texSword, new Vector2(W * 0.5f - 42, 6), isSwordsActive))
        {
            if (_currentTab != 0 && _currentTab != 1)
                _currentTab = 0;
        }

        // Gear tab button
        if (IconTabButton("##tabGear", _texGear, new Vector2(W * 0.5f + 8, 6), isGearActive))
        {
            _currentTab = 2;
        }

        // ── Sub-tab navigation or title ──
        if (_currentTab == 0 || _currentTab == 1)
        {
            ImGui.SetCursorPosY(54);
            float totalSubTabW = 0;
            string[] subTabs = { "Left Clicker", "Right Clicker" };
            float[] subWidths = new float[subTabs.Length];
            for (int i = 0; i < subTabs.Length; i++) { subWidths[i] = ImGui.CalcTextSize(subTabs[i]).X + 20; totalSubTabW += subWidths[i]; }
            float subStartX = (W - totalSubTabW - (subTabs.Length - 1) * 25) * 0.5f;
            ImGui.SetCursorPosX(subStartX);

            for (int i = 0; i < subTabs.Length; i++)
            {
                if (i > 0) ImGui.SameLine(0, 25);
                Vector4 tColor = (i == _currentTab) ? _colAccent : _colTextDim;
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.Text, tColor);
                if (ImGui.Button(subTabs[i] + "##subtab" + i, new Vector2(subWidths[i], 22))) _currentTab = i;
                ImGui.PopStyleColor(4);

                if (i == _currentTab)
                {
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    drawList.AddLine(new Vector2(min.X, max.Y + 2), new Vector2(max.X, max.Y + 2), ImGui.GetColorU32(_colAccent), 2f);
                }
            }
        }
        else if (_currentTab == 2)
        {
            // Center title "s e t t i n g s"
            string stitle = "s e t t i n g s";
            var ts = ImGui.CalcTextSize(stitle);
            ImGui.SetCursorPosY(54);
            ImGui.SetCursorPosX((W - ts.X) * 0.5f);
            ImGui.TextColored(_colText, stitle);
        }

        // Separator line under tabs at Y=80
        drawList.AddLine(new Vector2(0, 81), new Vector2(W, 81), ImGui.GetColorU32(_colCardBrd), 1f);

        // ── Content Area ──
        ImGui.SetCursorPosY(88);

        if (_currentTab == 0) DrawLMBTab(W);
        else if (_currentTab == 1) { try { DrawRMBTab(W); } catch { } }
        else if (_currentTab == 2) DrawSettingsTab(W);

        // ── Bottom dots ──
        float dotY = H - 25;
        float dotStartX = W * 0.5f - 20;
        for (int i = 0; i < 3; i++)
        {
            uint col = (i == _currentTab) ? ImGui.GetColorU32(_colAccent) : ImGui.GetColorU32(_colTextDim);
            drawList.AddCircleFilled(new Vector2(dotStartX + i * 18, dotY), 4f, col);
        }

        ImGui.End();
    }

    private void DrawLMBTab(float W)
    {
        float pad = 15f;
        float cardH = 280f;
        float leftW = (W - pad * 3) * 0.55f;
        float rightW = (W - pad * 3) * 0.45f;

        // ── Left Card ──
        ImGui.SetCursorPos(new Vector2(pad, 92));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _colCard);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 12));
        ImGui.BeginChild("##LMBLeft", new Vector2(leftW, cardH), true);

        // Toggle row
        bool clicking = _clicker.Clicking;
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _colAccent);
        if (ImGui.Checkbox("##ToggleLMB", ref clicking)) {
            _clicker.Clicking = clicking;
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.Text("Toggle");

        ImGui.Dummy(new Vector2(0, 8));

        // ── Randomization toggle ──
        bool randEnabled = _cfg.RandomizationEnabled;
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _colAccent);
        if (ImGui.Checkbox("Randomization##LMB", ref randEnabled))
        {
            _cfg.RandomizationEnabled = randEnabled;
            if (!randEnabled)
            {
                // Snap Min/Max to AverageCps so the clicker stays consistent
                _cfg.MinCps = _cfg.AverageCps;
                _cfg.MaxCps = _cfg.AverageCps;
            }
            _cfg.Save();
        }
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0, 5));

        if (_cfg.RandomizationEnabled)
        {
            // Min CPS Slider
            float minCps = (float)_cfg.MinCps;
            ImGui.TextColored(_colTextDim, "Min CPS");
            ImGui.SameLine(leftW - 60);
            ImGui.Text($"{minCps:F1}");
            ImGui.SetNextItemWidth(leftW - 30);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, _colAccent);
            if (ImGui.SliderFloat("##MinCPS", ref minCps, 1.0f, 30.0f, ""))
            {
                _cfg.MinCps = minCps;
                _cfg.AverageCps = (_cfg.MinCps + _cfg.MaxCps) * 0.5;
                _clicker.ResetTimingState();
            }
            if (_cfg.MinCps > _cfg.MaxCps) { _cfg.MaxCps = _cfg.MinCps; _cfg.AverageCps = _cfg.MinCps; }
            if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
            ImGui.PopStyleColor();

            ImGui.Dummy(new Vector2(0, 5));

            // Max CPS Slider
            float maxCps = (float)_cfg.MaxCps;
            ImGui.TextColored(_colTextDim, "Max CPS");
            ImGui.SameLine(leftW - 60);
            ImGui.Text($"{maxCps:F1}");
            ImGui.SetNextItemWidth(leftW - 30);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, _colAccent);
            if (ImGui.SliderFloat("##MaxCPS", ref maxCps, 1.0f, 30.0f, ""))
            {
                _cfg.MaxCps = maxCps;
                _cfg.AverageCps = (_cfg.MinCps + _cfg.MaxCps) * 0.5;
                _clicker.ResetTimingState();
            }
            if (_cfg.MaxCps < _cfg.MinCps) { _cfg.MinCps = _cfg.MaxCps; _cfg.AverageCps = _cfg.MaxCps; }
            if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
            ImGui.PopStyleColor();
        }
        else
        {
            // Single CPS slider when Randomization is OFF
            float avgCps = (float)_cfg.AverageCps;
            ImGui.TextColored(_colTextDim, "CPS");
            ImGui.SameLine(leftW - 60);
            ImGui.Text($"{avgCps:F1}");
            ImGui.SetNextItemWidth(leftW - 30);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, _colAccent);
            if (ImGui.SliderFloat("##FixedCPS", ref avgCps, 1.0f, 30.0f, ""))
            {
                _cfg.AverageCps = avgCps;
                _cfg.MinCps     = avgCps;
                _cfg.MaxCps     = avgCps;
                _clicker.ResetTimingState();
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
            ImGui.PopStyleColor();
        }

        ImGui.Dummy(new Vector2(0, 5));

        // Click pattern
        ImGui.TextColored(_colTextDim, "Click pattern");
        string[] randModes = { "Jitter", "Butterfly", "NoDelay" };
        int randIdx = _cfg.RandMode;
        if (randIdx >= randModes.Length) randIdx = 0;
        ImGui.SetNextItemWidth(leftW - 30);
        if (ImGui.Combo("##Pattern", ref randIdx, randModes, randModes.Length)) _cfg.RandMode = randIdx;

        ImGui.Dummy(new Vector2(0, 5));

        // Keybind
        string bindText = _cfg.ClickBind == 0 ? "Keybind: [None]" : $"Keybind: [{KeyName(_cfg.ClickBind)}]";
        if (_bindMode && _bindingTarget == 0) bindText = "Keybind: [...]";
        if (ImGui.Button(bindText, new Vector2(leftW - 30, 28))) BeginBind(0);

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        // ── Right Card (Live Stats) ──
        ImGui.SetCursorPos(new Vector2(pad * 2 + leftW, 92));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _colCard);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 12));
        ImGui.BeginChild("##LMBRight", new Vector2(rightW, cardH), true);

        ImGui.TextColored(_colTextDim, "Live CPS");
        ImGui.SetWindowFontScale(2.2f);
        ImGui.TextColored(_colText, $"{_clicker.StatLiveCps:F1}");
        ImGui.SetWindowFontScale(1.0f);

        ImGui.Dummy(new Vector2(0, 8));

        DrawBar($"Avg: {_clicker.StatAvgCps:F1}", (float)(_clicker.StatAvgCps / Math.Max(_cfg.AverageCps * 1.5, 1)), "", rightW - 30);

        ImGui.Dummy(new Vector2(0, 5));

        DrawBar($"Jitter: {_clicker.StatJitter:F1}", (float)(_clicker.StatJitter / 5.0), "", rightW - 30);

        ImGui.Dummy(new Vector2(0, 12));

        // Stable button
        bool isStable = _clicker.StatJitter < 2.0;
        Vector4 stbCol = isStable ? new Vector4(0.1f, 0.7f, 0.3f, 1f) : new Vector4(0.8f, 0.2f, 0.2f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(stbCol.X, stbCol.Y, stbCol.Z, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(stbCol.X, stbCol.Y, stbCol.Z, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(stbCol.X, stbCol.Y, stbCol.Z, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.Border, stbCol);
        ImGui.PushStyleColor(ImGuiCol.Text, stbCol);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 14f);
        ImGui.Button(isStable ? "Stable" : "Unstable", new Vector2(100, 26));
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawRMBTab(float W)
    {
        float pad = 15f;
        float cardH = 280f;
        float leftW = (W - pad * 3) * 0.55f;
        float rightW = (W - pad * 3) * 0.45f;

        // ── Left Card ──
        ImGui.SetCursorPos(new Vector2(pad, 92));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _colCard);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 12));
        ImGui.BeginChild("##RMBLeft", new Vector2(leftW, cardH), true);

        bool clicking = _rightClicker.Clicking;
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _colAccent);
        if (ImGui.Checkbox("##ToggleRMB", ref clicking)) {
            _rightClicker.Clicking = clicking;
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.Text("Toggle");

        ImGui.Dummy(new Vector2(0, 8));

        // ── Randomization toggle (RMB) ──
        bool randEnabledR = _cfg.RandomizationEnabled;
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _colAccent);
        if (ImGui.Checkbox("Randomization##RMB", ref randEnabledR))
        {
            _cfg.RandomizationEnabled = randEnabledR;
            if (!randEnabledR)
            {
                _cfg.RightMinCps = _cfg.RightAverageCps;
                _cfg.RightMaxCps = _cfg.RightAverageCps;
            }
            _cfg.Save();
        }
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0, 5));

        if (_cfg.RandomizationEnabled)
        {
            // Min CPS Slider
            float rightMinCps = (float)_cfg.RightMinCps;
            ImGui.TextColored(_colTextDim, "Min CPS");
            ImGui.SameLine(leftW - 60);
            ImGui.Text($"{rightMinCps:F1}");
            ImGui.SetNextItemWidth(leftW - 30);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, _colAccent);
            if (ImGui.SliderFloat("##RightMinCPS", ref rightMinCps, 1.0f, 30.0f, ""))
            {
                _cfg.RightMinCps = rightMinCps;
                _cfg.RightAverageCps = (_cfg.RightMinCps + _cfg.RightMaxCps) * 0.5;
            }
            if (_cfg.RightMinCps > _cfg.RightMaxCps) { _cfg.RightMaxCps = _cfg.RightMinCps; _cfg.RightAverageCps = _cfg.RightMinCps; }
            if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
            ImGui.PopStyleColor();

            ImGui.Dummy(new Vector2(0, 5));

            // Max CPS Slider
            float rightMaxCps = (float)_cfg.RightMaxCps;
            ImGui.TextColored(_colTextDim, "Max CPS");
            ImGui.SameLine(leftW - 60);
            ImGui.Text($"{rightMaxCps:F1}");
            ImGui.SetNextItemWidth(leftW - 30);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, _colAccent);
            if (ImGui.SliderFloat("##RightMaxCPS", ref rightMaxCps, 1.0f, 30.0f, ""))
            {
                _cfg.RightMaxCps = rightMaxCps;
                _cfg.RightAverageCps = (_cfg.RightMinCps + _cfg.RightMaxCps) * 0.5;
            }
            if (_cfg.RightMaxCps < _cfg.RightMinCps) { _cfg.RightMinCps = _cfg.RightMaxCps; _cfg.RightAverageCps = _cfg.RightMaxCps; }
            if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
            ImGui.PopStyleColor();
        }
        else
        {
            // Single CPS slider
            float avgRCps = (float)_cfg.RightAverageCps;
            ImGui.TextColored(_colTextDim, "CPS");
            ImGui.SameLine(leftW - 60);
            ImGui.Text($"{avgRCps:F1}");
            ImGui.SetNextItemWidth(leftW - 30);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, _colAccent);
            if (ImGui.SliderFloat("##FixedRightCPS", ref avgRCps, 1.0f, 30.0f, ""))
            {
                _cfg.RightAverageCps = avgRCps;
                _cfg.RightMinCps     = avgRCps;
                _cfg.RightMaxCps     = avgRCps;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
            ImGui.PopStyleColor();
        }

        ImGui.Dummy(new Vector2(0, 5));

        ImGui.TextColored(_colTextDim, "Click pattern");
        string[] randModes = { "Jitter", "Butterfly", "NoDelay" };
        int randIdx = _cfg.RightRandMode;
        if (randIdx >= randModes.Length) randIdx = 0;
        ImGui.SetNextItemWidth(leftW - 30);
        if (ImGui.Combo("##PatternRMB", ref randIdx, randModes, randModes.Length)) _cfg.RightRandMode = randIdx;

        ImGui.Dummy(new Vector2(0, 5));

        string bindText = _cfg.RightBind == 0 ? "Keybind: [None]" : $"Keybind: [{KeyName(_cfg.RightBind)}]";
        if (_bindMode && _bindingTarget == 3) bindText = "Keybind: [...]";
        if (ImGui.Button(bindText, new Vector2(leftW - 30, 28))) BeginBind(3);

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        // ── Right Card (Live Stats) ──
        ImGui.SetCursorPos(new Vector2(pad * 2 + leftW, 92));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _colCard);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 12));
        ImGui.BeginChild("##RMBRight", new Vector2(rightW, cardH), true);

        ImGui.TextColored(_colTextDim, "Live CPS");
        ImGui.SetWindowFontScale(2.2f);
        ImGui.TextColored(_colText, $"{_rightClicker.StatLiveCps:F1}");
        ImGui.SetWindowFontScale(1.0f);

        ImGui.Dummy(new Vector2(0, 8));

        DrawBar($"Avg: {_rightClicker.StatAvgCps:F1}", (float)(_rightClicker.StatAvgCps / Math.Max(_cfg.RightAverageCps * 1.5, 1)), "", rightW - 30);

        ImGui.Dummy(new Vector2(0, 5));

        DrawBar($"Jitter: {_rightClicker.StatJitter:F1}", (float)(_rightClicker.StatJitter / 5.0), "", rightW - 30);

        ImGui.Dummy(new Vector2(0, 12));

        bool isStable = _rightClicker.StatJitter < 2.0;
        Vector4 stbCol = isStable ? new Vector4(0.1f, 0.7f, 0.3f, 1f) : new Vector4(0.8f, 0.2f, 0.2f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(stbCol.X, stbCol.Y, stbCol.Z, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(stbCol.X, stbCol.Y, stbCol.Z, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(stbCol.X, stbCol.Y, stbCol.Z, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.Border, stbCol);
        ImGui.PushStyleColor(ImGuiCol.Text, stbCol);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 14f);
        ImGui.Button(isStable ? "Stable" : "Unstable", new Vector2(100, 26));
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawSettingsTab(float W)
    {
        try {
            float H = 420f;
            float pad = 20f;
            float innerW = W - pad * 2;
            float contentH = H - 92f - 10f; // available height below tabs

            ImGui.SetCursorPos(new Vector2(pad, 92));
            ImGui.BeginChild("##SettingsScroll", new Vector2(innerW, contentH), false, ImGuiWindowFlags.NoScrollbar);

            // ── Auto-Switch checkbox ──
            bool autoSwitch = _cfg.AutoSwitchProfiles;
            ImGui.PushStyleColor(ImGuiCol.CheckMark, _colAccent);
            if (ImGui.Checkbox("Auto-Switch Profiles", ref autoSwitch))
            {
                _cfg.AutoSwitchProfiles = autoSwitch;
                _cfg.Save();
            }
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 6));

            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.90f, 0.16f, 0.16f, 0.96f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.00f, 0.24f, 0.24f, 1.00f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.70f, 0.10f, 0.10f, 1.00f));
            ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(1f, 1f, 1f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
            if (ImGui.Button("DESTRUCT", new Vector2(96f, 24f)))
            {
                Close();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
            ImGui.Dummy(new Vector2(0, 10));

            // ── Accent Color card ──
            DrawSettingsCard("Accent Color", innerW, 52f, () =>
            {
                var accent3 = new Vector3(_cfg.AccentR, _cfg.AccentG, _cfg.AccentB);
                ImGui.SetNextItemWidth(innerW - 120);
                if (ImGui.ColorEdit3("##accent", ref accent3, ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.DisplayHex))
                {
                    _cfg.AccentR = accent3.X; _cfg.AccentG = accent3.Y; _cfg.AccentB = accent3.Z;
                    _cfg.Save(); ApplyTheme();
                }
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(_cfg.AccentR, _cfg.AccentG, _cfg.AccentB, 0.30f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(_cfg.AccentR, _cfg.AccentG, _cfg.AccentB, 0.50f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(_cfg.AccentR, _cfg.AccentG, _cfg.AccentB, 0.70f));
                if (ImGui.Button("Reset##ar", new Vector2(48, 22))) { _cfg.AccentR = 0.55f; _cfg.AccentG = 0.25f; _cfg.AccentB = 0.85f; _cfg.Save(); ApplyTheme(); }
                ImGui.PopStyleColor(3);
            });
            ImGui.Dummy(new Vector2(0, 8));

            // ── Add Preset card ──
            DrawSettingsCard("New Preset", innerW, 78f, () =>
            {
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.14f, 1f));
                ImGui.SetNextItemWidth(innerW - 100);
                ImGui.InputTextWithHint("##pname", "preset name...", ref _presetAddName, 64);
                ImGui.Dummy(new Vector2(0, 4));
                ImGui.SetNextItemWidth(innerW - 100);
                ImGui.InputTextWithHint("##pserver", "server IP / hostname...", ref _presetAddServer, 64);
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, 0.25f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, 0.45f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, 0.65f));
                if (ImGui.Button("+##padd", new Vector2(32, 46)))
                {
                    if (!string.IsNullOrWhiteSpace(_presetAddName))
                    {
                        _cfg.Presets.Add(new Horimiya.Config.PresetConfig
                        {
                            Name = _presetAddName,
                            Server = _presetAddServer,
                            Cps = (_cfg.MinCps + _cfg.MaxCps) * 0.5,
                            RandMode = _cfg.RandMode
                        });
                        _cfg.Save();
                        _presetAddName = "server name";
                        _presetAddServer = "";
                    }
                }
                ImGui.PopStyleColor(3);
            });
            ImGui.Dummy(new Vector2(0, 8));

            // ── Presets list ──
            for (int i = 0; i < _cfg.Presets.Count; i++)
            {
                var p = _cfg.Presets[i];
                if (p.IsBuiltIn) continue;

                bool deleteClicked = false;

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.095f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Border,  new Vector4(0.15f, 0.10f, 0.22f, 1f));
                ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding,   10f);
                ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,   new Vector2(12, 8));
                ImGui.BeginChild("##pc" + i, new Vector2(innerW - 4, 54), true, ImGuiWindowFlags.NoScrollbar);

                ImGui.SetWindowFontScale(0.9f);
                ImGui.TextColored(_colText, p.Name ?? "Unknown");
                ImGui.SameLine();
                ImGui.TextColored(_colTextDim, $"  {p.Cps:F1} CPS");
                if (!string.IsNullOrEmpty(p.Server))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.65f, 0.35f, 0.95f, 1f), $"[{p.Server}]");
                }
                ImGui.SetWindowFontScale(1.0f);

                // Load button
                ImGui.SetCursorPos(new Vector2(innerW - 110, 14));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, 0.22f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, 0.38f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(_colAccent.X, _colAccent.Y, _colAccent.Z, 0.55f));
                ImGui.PushStyleColor(ImGuiCol.Text,          _colText);
                bool loadClicked = ImGui.Button("LOAD##l" + i, new Vector2(48, 22));
                ImGui.PopStyleColor(4);
                ImGui.PopStyleVar(2);

                // X button
                ImGui.SetCursorPos(new Vector2(innerW - 36, 14));
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0,0,0,0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.15f, 0.15f, 0.4f));
                ImGui.PushStyleColor(ImGuiCol.Text,          _colTextDim);
                if (ImGui.Button("×##d" + i, new Vector2(22, 22))) deleteClicked = true;
                ImGui.PopStyleColor(3);

                if (loadClicked || (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsItemHovered()))
                {
                    double mid = (p.Cps > 2.0) ? p.Cps : (_cfg.MinCps + _cfg.MaxCps) * 0.5;
                    _cfg.AverageCps = mid;
                    if (_cfg.RandomizationEnabled)
                    {
                        _cfg.MinCps = Math.Max(1.0, mid - 2.0);
                        _cfg.MaxCps = Math.Min(30.0, mid + 2.0);
                    }
                    else
                    {
                        _cfg.MinCps = mid;
                        _cfg.MaxCps = mid;
                    }
                    _cfg.RandMode = p.RandMode;
                    _cfg.Save();
                    _clicker.ResetTimingState();
                }

                ImGui.EndChild();
                ImGui.PopStyleVar(3);
                ImGui.PopStyleColor(2);

                if (deleteClicked) { _cfg.Presets.RemoveAt(i); _cfg.Save(); i--; }
                ImGui.Dummy(new Vector2(0, 6));
            }

            ImGui.EndChild(); // ##SettingsScroll
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("settings_error.txt", ex.ToString());
            _currentTab = 0; // Fallback to safe tab
        }
    }

    // Helper: draws a titled card section (no push/pop leaks)
    private void DrawSettingsCard(string label, float w, float h, System.Action content)
    {
        ImGui.TextColored(_colTextDim, label);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,   _colCard);
        ImGui.PushStyleColor(ImGuiCol.Border,     new Vector4(0.15f, 0.10f, 0.22f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding,   10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,   new Vector2(14, 10));
        ImGui.BeginChild("##sc_" + label, new Vector2(w, h), true);
        content();
        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }


    // Cached version: only rescans filesystem every 5 seconds
    private List<string> GetSoundsCached()
    {
        long now = Environment.TickCount;
        if (_cachedSounds == null || (now - _soundsCacheTimeMs) > 5000)
        {
            _cachedSounds = GetSounds();
            _soundsCacheTimeMs = now;
        }
        return _cachedSounds;
    }

    private List<string> GetSounds()
    {
        var list = new List<string> { "None" };
        string d1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "XVA", "resource");
        if (Directory.Exists(d1))
            foreach (string f in Directory.GetFiles(d1, "*.wav"))
                list.Add(Path.GetFileName(f));
        string d2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Horimiya", "resource");
        if (Directory.Exists(d2))
            foreach (string f in Directory.GetFiles(d2, "*.wav"))
                if (!list.Contains(Path.GetFileName(f)))
                    list.Add(Path.GetFileName(f));
        return list;
    }

    // ── Key binding logic ──────────────────────────────────────────────────────

    private string KeyName(int vk)
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

    private void BeginBind(int target)
    {
        _bindMode = true; 
        _bindingTarget = target;
        _waitRelease = true;
        _bindTimer.Start();
    }

    private int _pressedKey = 0;

    private void BindTick(object s, EventArgs e)
    {
        if (!_bindMode) { _bindTimer.Stop(); _pressedKey = 0; return; }

        if (_waitRelease)
        {
            bool anyDown = false;
            for (int i = 1; i < 7; i++)
                if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0) { anyDown = true; break; }
            if (!anyDown)
                for (int i = 8; i < 256; i++)
                    if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0) { anyDown = true; break; }
            if (!anyDown) _waitRelease = false;
            return;
        }

        if (_pressedKey == 0)
        {
            int[] mouseButtons = { 5, 6, 4, 2, 1 };
            foreach (int mb in mouseButtons)
            {
                if ((Win32.GetAsyncKeyState(mb) & 0x8000) != 0)
                {
                    _pressedKey = mb;
                    return;
                }
            }

            for (int i = 8; i < 256; i++)
            {
                if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0)
                {
                    _pressedKey = i;
                    return;
                }
            }
        }
        else
        {
            if ((Win32.GetAsyncKeyState(_pressedKey) & 0x8000) == 0)
            {
                FinishBind(_pressedKey);
                _pressedKey = 0;
            }
        }
    }

    private void FinishBind(int vk)
    {
        _bindMode = false;
        _bindTimer.Stop();
        if (_bindingTarget == 0) _cfg.ClickBind = vk;
        else if (_bindingTarget == 1) _cfg.HideBind = vk;
        else if (_bindingTarget == 2) _cfg.DestructBind = vk;
        else if (_bindingTarget == 3) _cfg.RightBind = vk;
        else if (_bindingTarget == 4) _cfg.ProfileSwitchBind = vk;
        
        _cfg.Save(); // Ensure the bind is saved to disk immediately
    }

    private void OnIotMessageReceived(object sender, string payload)
    {
        try
        {
            var tempCfg = AppConfig.FromJson(payload);
            foreach(var cp in tempCfg.Presets)
            {
                if(!cp.IsBuiltIn)
                {
                    bool exists = false;
                    for (int i=0; i<_cfg.Presets.Count; i++) {
                        if (_cfg.Presets[i].Name == cp.Name) { exists = true; break; }
                    }
                    if (!exists) _cfg.Presets.Add(cp);
                }
            }
        }
        catch { }
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        // Set disposed flag FIRST so RenderLoop stops immediately
        _disposed = true;
        Application.Idle -= RenderLoop;

        _cfg.Save();
        _bindTimer.Stop();
        _clicker.Stop();
        _rightClicker.Stop();
        _recorder.Stop();
        _misc.Stop();
        _iot.Dispose();
        Win32.StopMouseHook();
        Win32.timeEndPeriod(1); // Restore Windows timer resolution

        _controller?.Dispose();
        if (_texLogo != 0) GL.DeleteTexture(_texLogo);
        if (_texBg != 0) GL.DeleteTexture(_texBg);
        if (_texEaster != 0) GL.DeleteTexture(_texEaster);

        Environment.Exit(0);
    }

    private void DrawLiveStats(double liveCps, double avgCps, double interval, double jitter, double last, int late, double worstLate, int samples)
    {
        // Draw a child window with a subtle translucent background to blend with the GUI image
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.0f, 0.0f, 0.0f, 0.4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 15));
        
        // Let it fill the remaining space or have a fixed height. No border (false).
        if (ImGui.BeginChild("LiveStatsPanel", new Vector2(0, 0), false, ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            ImGui.Text("Live");
            ImGui.Separator();
            
            ImGui.SetWindowFontScale(2.5f);
            ImGui.Text($"{liveCps:F1}");
            ImGui.SetWindowFontScale(1.0f);
            
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Average: {avgCps:F1}");
            ImGui.PopStyleColor(); // white text

            ImGui.Dummy(new Vector2(0, 10)); // spacing

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            ImGui.Text("Stability");
            ImGui.Separator();
            ImGui.PopStyleColor();

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Interval: {interval:F2} ms   Jitter: {jitter:F2} ms");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Last: {last:F2} ms   Late: {late}");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Worst late: {worstLate:F2} ms   Samples: {samples}");
            
            ImGui.Dummy(new Vector2(0, 10)); // spacing

            bool isStable = jitter < 2.0;
            if (samples > 0)
            {
                if (isStable)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.4f, 0.2f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.4f, 0.2f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.4f, 0.2f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.5f, 1f));
                    ImGui.Button("Stable", new Vector2(80, 25));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.1f, 0.1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.1f, 0.1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.1f, 0.1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.4f, 0.4f, 1f));
                    ImGui.Button("Unstable", new Vector2(80, 25));
                }
                ImGui.PopStyleColor(4);
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Waiting for clicks...");
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }
}
