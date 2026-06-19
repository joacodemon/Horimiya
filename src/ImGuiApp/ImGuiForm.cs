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
using lospoderosos_lite.Config;
using lospoderosos_lite.Modules;
using lospoderosos_lite.Utils;

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
    private int _texLogo = 0;
    private int _texBg = 0;
    private int _texEaster = 0;
    private bool _showBg = true;

    private string _recMacroName = "macro1";
    private string _presetAddName = "server name";
    private float _presetAddCps = 15.0f;
    private int _presetAddRand = 2;
    private string _recorderStatus = "Status: Idle | Events: 0";
    private float[] _customCpsWeightsFloat = new float[25];
    private float[] _presetAddCustomCpsWeightsFloat = new float[25];

    private lospoderosos_lite.UI.NotificationOverlay _notifyOverlay;

    private Stopwatch _frameSw = Stopwatch.StartNew();
    private volatile bool _disposed = false;
    private const double TARGET_FRAME_TIME_MS = 1000.0 / 60.0; // 60 FPS cap

    // Cached sound list to avoid filesystem scanning every frame
    private List<string> _cachedSounds = null;
    private long _soundsCacheTimeMs = 0;
    
    private lospoderosos_lite.IoT.IoTManager _iot;

    public ImGuiForm(AppConfig cfg, Clicker clicker, RightClicker rightClicker, Recorder recorder, Misc misc)
    {
        _cfg = cfg;
        _clicker = clicker;
        _rightClicker = rightClicker;
        _recorder = recorder;
        _misc = misc;

        _iot = new lospoderosos_lite.IoT.IoTManager(_cfg);
        _iot.MessageReceived += OnIotMessageReceived;
        _ = _iot.StartAsync();
        _rightClicker = rightClicker;
        _recorder = recorder;
        _misc = misc;

        for (int i = 0; i < 25; i++)
            _customCpsWeightsFloat[i] = (float)_cfg.CustomCpsWeights[i];

        Text = "Los Poderosisimos";
        ClientSize = new Size(950, 470);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _glControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 4));
        _glControl.Dock = DockStyle.Fill;
        Controls.Add(_glControl);

        _notifyOverlay = new lospoderosos_lite.UI.NotificationOverlay();
        _notifyOverlay.Show(this);

        _bindTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _bindTimer.Tick += BindTick;

        _recorder.StatusChanged += s => { _recorderStatus = s; };
        _misc.ClickBindTriggered += () => { 
            _clicker.Clicking = !_clicker.Clicking; 
            _notifyOverlay.ShowNotification("Left Clicker " + (_clicker.Clicking ? "ON" : "OFF"), _clicker.Clicking ? lospoderosos_lite.UI.NotificationOverlay.NotificationType.Success : lospoderosos_lite.UI.NotificationOverlay.NotificationType.Error);
        };
        _misc.RightClickBindTriggered += () => { 
            _rightClicker.Clicking = !_rightClicker.Clicking; 
            _notifyOverlay.ShowNotification("Right Clicker " + (_rightClicker.Clicking ? "ON" : "OFF"), _rightClicker.Clicking ? lospoderosos_lite.UI.NotificationOverlay.NotificationType.Success : lospoderosos_lite.UI.NotificationOverlay.NotificationType.Error);
        };
        _misc.HideBindTriggered += () => { 
            if (Visible) Hide(); 
            else { Show(); BringToFront(); } 
        };
        _misc.DestructBindTriggered += () => { 
            Close(); 
        };

        Load += OnLoad;
        FormClosing += OnFormClosing;
        
        SetupInputEvents();
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
        LoadTaskbarIcon();
        _glControl.MakeCurrent();
        _controller = new ImGuiController(ClientSize.Width, ClientSize.Height);

        string logoRes = "ImGuiApp.Resources.logo.png";
        string bgRes = "ImGuiApp.Resources.background.png";
        string easterRes = "ImGuiApp.Resources.easter.png";
        _texLogo = TextureHelper.LoadTextureFromResource(logoRes);
        _texBg = TextureHelper.LoadTextureFromResource(bgRes);
        _texEaster = TextureHelper.LoadTextureFromResource(easterRes);

        // Use VSync to pace rendering naturally (prevents 100% CPU/GPU usage)
        _glControl.VSync = false; // Desactivar VSync para evitar limitar a 30 FPS
        
        Application.Idle += RenderLoop;
        
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var style = ImGui.GetStyle();
        // Force MARCELINE pink accent
        _cfg.ColorAccent = Color.FromArgb(255, 30, 86).ToArgb(); 
        Vector4 accVec = new Vector4(1.0f, 0.12f, 0.34f, 1f);
        Vector4 accHoverVec = new Vector4(1.0f, 0.22f, 0.44f, 1f);
        Vector4 accActiveVec = new Vector4(0.8f, 0.05f, 0.24f, 1f);

        // Deep dark theme matching MARCELINE
        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.85f, 0.85f, 0.85f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.06f, 0.06f, 0.06f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.04f, 0.04f, 0.04f, 1.00f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.18f, 0.18f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.24f, 0.24f, 0.24f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.04f, 0.04f, 0.04f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.04f, 0.04f, 0.04f, 1.00f);
        style.Colors[(int)ImGuiCol.CheckMark] = accVec;
        style.Colors[(int)ImGuiCol.SliderGrab] = accVec;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accActiveVec;
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = accHoverVec;
        style.Colors[(int)ImGuiCol.ButtonActive] = accActiveVec;
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.18f, 0.18f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);

        style.WindowRounding = 8.0f;
        style.ChildRounding = 8.0f;
        style.FrameRounding = 6.0f;
        style.PopupRounding = 6.0f;
        style.ScrollbarRounding = 6.0f;
        style.GrabRounding = 6.0f;
        
        // Hide background image to enforce opaque dark theme
        _showBg = false;
    }

    private void LoadTaskbarIcon()
    {
        try
        {
            using (var bmp = TextureHelper.LoadBitmapFromResource("ImGuiApp.Resources.icon.png"))
            {
                if (bmp != null)
                {
                    this.Icon = Icon.FromHandle(bmp.GetHicon());
                }
            }
        }
        catch { }
    }

    private void RenderLoop(object sender, EventArgs e)
    {
        while (IsAppIdle())
        {
            // Cap at ~60 FPS (16.6ms per frame) to prevent 100% CPU/GPU usage
            if (_frameSw.Elapsed.TotalMilliseconds < 16.0)
            {
                Thread.Sleep(1); // Give CPU time to the OS to avoid full system lag
                continue;
            }

            _glControl.MakeCurrent();
            GL.ClearColor(0.05f, 0.05f, 0.05f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            // Calculate actual delta time for smoother ImGui animations
            float dt = (float)_frameSw.Elapsed.TotalSeconds;
            if (dt <= 0) dt = 1f / 60f; // safety
            _frameSw.Restart();

            _controller.Update(_glControl.Width, _glControl.Height, dt);
            
            DrawUI();

            _controller.Render();
            
            _glControl.SwapBuffers();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr hWnd;
        public uint msg;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public System.Drawing.Point p;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    private bool IsAppIdle()
    {
        NativeMessage result;
        return PeekMessage(out result, IntPtr.Zero, 0, 0, 0) == 0;
    }

    private int _currentTab = 0; // 0: Clicker, 1: RMB, 2: Settings

    private void DrawUI()
    {
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(950, 470));
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("Main", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        // Top Header matching MARCELINE
        ImGui.SetCursorPos(new Vector2(20, 15));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.12f, 0.34f, 1f));
        ImGui.SetWindowFontScale(1.4f);
        ImGui.Text("LOS PODEROSISIMOS"); 
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();

        ImGui.SameLine(950 - 150);
        ImGui.SetCursorPos(new Vector2(650, 20));
        ImGui.TextColored(new Vector4(0.8f, 0.2f, 0.2f, 1f), "los poderosisimos by joacodemon");
        
        ImGui.SetCursorPos(new Vector2(10, 45));
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.15f, 0.15f, 0.15f, 1f));
        ImGui.Separator();
        ImGui.PopStyleColor();

        // Main layout container
        ImGui.SetCursorPos(new Vector2(10, 55));
        
        // --- Sidebar ---
        if (ImGui.BeginChild("Sidebar", new Vector2(200, 400), true))
        {
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Clicker");
            ImGui.Dummy(new Vector2(0, 5));
            if (DrawSidebarTab("Left Clicker", _currentTab == 0)) _currentTab = 0;
            if (DrawSidebarTab("Right Clicker", _currentTab == 1)) _currentTab = 1;
            
            ImGui.Dummy(new Vector2(0, 15));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Settings");
            ImGui.Dummy(new Vector2(0, 5));
            if (DrawSidebarTab("Settings", _currentTab == 2)) _currentTab = 2;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // --- Content ---
        if (ImGui.BeginChild("Content", new Vector2(720, 400), true))
        {
            if (_currentTab == 0) DrawLMBTab();
            else if (_currentTab == 1) DrawRMBTab();
            else if (_currentTab == 2) DrawSettingsTab();
        }
        ImGui.EndChild();

        ImGui.End();
    }

    private bool DrawSidebarTab(string label, bool selected)
    {
        bool clicked = false;
        Vector2 pos = ImGui.GetCursorScreenPos();
        Vector2 size = new Vector2(180, 30);
        
        if (ImGui.InvisibleButton(label, size)) clicked = true;
        bool hovered = ImGui.IsItemHovered();
        
        var dl = ImGui.GetWindowDrawList();
        
        // Background
        if (selected) dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.12f, 1f)), 4f);
        else if (hovered) dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.08f, 1f)), 4f);
        
        // Left pink bar for selected
        if (selected)
        {
            dl.AddRectFilled(pos, new Vector2(pos.X + 3, pos.Y + size.Y), ImGui.GetColorU32(new Vector4(1f, 0.12f, 0.34f, 1f)), 4f);
        }
        
        // Text
        dl.AddText(new Vector2(pos.X + 15, pos.Y + 7), ImGui.GetColorU32(new Vector4(0.85f, 0.85f, 0.85f, 1f)), label);
        
        return clicked;
    }

    private bool DrawToggle(string label, ref bool v)
    {
        Vector2 p = ImGui.GetCursorScreenPos();
        var draw_list = ImGui.GetWindowDrawList();

        float height = 20.0f;
        float width = 42.0f;
        float radius = height * 0.5f;

        ImGui.InvisibleButton(label, new Vector2(width, height));
        bool clicked = false;
        if (ImGui.IsItemClicked())
        {
            v = !v;
            clicked = true;
        }

        float t = v ? 1.0f : 0.0f;

        // Background color (vibrant Green / Red)
        uint col_bg = ImGui.GetColorU32(v ? new Vector4(0.0f, 0.85f, 0.0f, 1f) : new Vector4(0.85f, 0.0f, 0.0f, 1f));
        draw_list.AddRectFilled(p, new Vector2(p.X + width, p.Y + height), col_bg, radius);

        // Circle color
        uint col_circle = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));
        float circle_x = p.X + radius + t * (width - radius * 2.0f);
        draw_list.AddCircleFilled(new Vector2(circle_x, p.Y + radius), radius - 1.5f, col_circle);

        ImGui.SameLine();
        string displayLabel = label;
        int hashIdx = label.IndexOf("##");
        if (hashIdx >= 0) displayLabel = label.Substring(0, hashIdx);
        
        // Vertical alignment for the text
        float textYOffset = (height - ImGui.GetTextLineHeight()) * 0.5f;
        Vector2 cursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(cursorPos.X + 2, cursorPos.Y + textYOffset));
        ImGui.Text(displayLabel);
        
        return clicked;
    }

    private void DrawLMBTab()
    {
        ImGui.Columns(2, "lmb_cols", false);
        ImGui.SetColumnWidth(0, 400);

        // --- Left Column ---
        ImGui.SetWindowFontScale(1.2f);
        ImGui.Text("Left Clicker");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.Dummy(new Vector2(0, 5));

        bool clicking = _clicker.Clicking;
        if (DrawToggle("Enabled##LMB", ref clicking))
        {
            _clicker.Clicking = clicking;
            _notifyOverlay.ShowNotification("Left Clicker " + (_clicker.Clicking ? "ON" : "OFF"), _clicker.Clicking ? lospoderosos_lite.UI.NotificationOverlay.NotificationType.Success : lospoderosos_lite.UI.NotificationOverlay.NotificationType.Error);
        }

        ImGui.Dummy(new Vector2(0, 15));
        
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Mode");
        string[] modes = { "Hold", "Toggle", "Always" };
        int modeIdx = _cfg.Mode;
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("##mode", ref modeIdx, modes, modes.Length))
            _cfg.Mode = modeIdx;

        ImGui.Dummy(new Vector2(0, 10));

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Randomization");
        string[] randModes = { "Jitter", "Butterfly", "NoDelay", "Manual" };
        int randIdx = _cfg.RandMode;
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("##rand", ref randIdx, randModes, randModes.Length))
            _cfg.RandMode = randIdx;

        if (_cfg.RandMode == 3)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
            if (ImGui.TreeNode("Edit Custom Randomization"))
            {
                for (int i = 0; i < 25; i++)
                {
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.SliderFloat($"CPS {i+1}", ref _customCpsWeightsFloat[i], 0f, 100f, "%.1f"))
                        _cfg.CustomCpsWeights[i] = _customCpsWeightsFloat[i];
                    if (i % 2 == 0 && i < 24) ImGui.SameLine();
                }
                ImGui.TreePop();
            }
        }

        ImGui.Dummy(new Vector2(0, 10));

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Clicks per Second");
        float cps = (float)_cfg.AverageCps;
        ImGui.SetNextItemWidth(350);
        if (ImGui.SliderFloat("Average", ref cps, 1.0f, 50.0f, "%.1f"))
            _cfg.AverageCps = cps;
        if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();

        ImGui.Dummy(new Vector2(0, 10));

        
        
        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.TreeNode("Advanced Logic"))
        {
            List<string> sounds = GetSoundsCached();
            int soundIdx = sounds.IndexOf(_cfg.Sound);
            if (soundIdx < 0) soundIdx = 0;
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("Click Sound", ref soundIdx, sounds.ToArray(), sounds.Count))
                _cfg.Sound = sounds[soundIdx];

            bool oig = _cfg.OnlyInGame;
            if (ImGui.Checkbox("Only In Game", ref oig)) _cfg.OnlyInGame = oig;
            bool rmb = _cfg.RmbLock;
            if (ImGui.Checkbox("RMB-Lock", ref rmb)) _cfg.RmbLock = rmb;
            bool wim = _cfg.WorkInMenus;
            if (ImGui.Checkbox("Work in Menus", ref wim)) _cfg.WorkInMenus = wim;
            
            string bindText = _cfg.ClickBind == 0 ? "Bind: none" : "Bind: " + KeyName(_cfg.ClickBind);
            if (_bindMode && _bindingTarget == 0) bindText = "...press key...";
            if (ImGui.Button(bindText, new Vector2(120, 0))) BeginBind(0);

            ImGui.TreePop();
        }

        ImGui.NextColumn();

        // --- Right Column ---
        DrawLiveStats(_clicker.StatLiveCps, _clicker.StatAvgCps, _clicker.StatInterval, _clicker.StatJitter, _clicker.StatLast, _clicker.StatLate, _clicker.StatWorstLate, _clicker.StatSamples);

        ImGui.Columns(1);
    }

    private void DrawRMBTab()
    {
        ImGui.Columns(2, "rmb_cols", false);
        ImGui.SetColumnWidth(0, 400);

        // --- Left Column ---
        ImGui.SetWindowFontScale(1.2f);
        ImGui.Text("Right Clicker");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.Dummy(new Vector2(0, 5));

        bool clicking = _rightClicker.Clicking;
        if (DrawToggle("Enabled##RMB", ref clicking))
        {
            _rightClicker.Clicking = clicking;
            _notifyOverlay.ShowNotification("Right Clicker " + (_rightClicker.Clicking ? "ON" : "OFF"), _rightClicker.Clicking ? lospoderosos_lite.UI.NotificationOverlay.NotificationType.Success : lospoderosos_lite.UI.NotificationOverlay.NotificationType.Error);
        }

        ImGui.Dummy(new Vector2(0, 15));
        
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Mode");
        string[] modes = { "Hold", "Toggle", "Always" };
        int modeIdx = _cfg.RightMode;
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("##modeRMB", ref modeIdx, modes, modes.Length))
            _cfg.RightMode = modeIdx;

        ImGui.Dummy(new Vector2(0, 10));

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Randomization");
        string[] randModes = { "Jitter", "Butterfly", "NoDelay", "Manual" };
        int randIdx = _cfg.RightRandMode;
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("##randRMB", ref randIdx, randModes, randModes.Length))
            _cfg.RightRandMode = randIdx;

        ImGui.Dummy(new Vector2(0, 10));

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Clicks per Second");
        float cps = (float)_cfg.RightAverageCps;
        ImGui.SetNextItemWidth(350);
        if (ImGui.SliderFloat("Average##RMB", ref cps, 1.0f, 50.0f, "%.1f"))
            _cfg.RightAverageCps = cps;
        if (ImGui.IsItemDeactivatedAfterEdit()) _cfg.Save();
        
        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.TreeNode("Advanced Logic##RMB"))
        {
            string bindText = _cfg.RightBind == 0 ? "Bind: none" : "Bind: " + KeyName(_cfg.RightBind);
            if (_bindMode && _bindingTarget == 3) bindText = "...press key...";
            if (ImGui.Button(bindText, new Vector2(120, 0))) BeginBind(3);

            ImGui.TreePop();
        }

        ImGui.NextColumn();

        // --- Right Column ---
        DrawLiveStats(_rightClicker.StatLiveCps, _rightClicker.StatAvgCps, _rightClicker.StatInterval, _rightClicker.StatJitter, _rightClicker.StatLast, _rightClicker.StatLate, _rightClicker.StatWorstLate, _rightClicker.StatSamples);

        ImGui.Columns(1);
    }

    private void DrawRECTab()
    {
        ImGui.Text("Macro Recorder");
        ImGui.Separator();

        if (ImGui.Button("⏺ REC", new Vector2(100, 30))) _recorder.StartRecord();
        ImGui.SameLine();
        if (ImGui.Button("▶ PLAY", new Vector2(100, 30))) _recorder.StartPlay();
        ImGui.SameLine();
        if (ImGui.Button("⏹ STOP", new Vector2(100, 30))) _recorder.Stop();

        bool lp = _recorder.LoopPlayback;
        if (ImGui.Checkbox("Loop Playback", ref lp)) _recorder.LoopPlayback = lp;

        string[] spds = { "0.5x", "0.75x", "1.0x", "1.5x", "2.0x" };
        double[] spdVals = { 0.5, 0.75, 1.0, 1.5, 2.0 };
        int spdIdx = Array.IndexOf(spdVals, _recorder.PlaybackSpeed);
        if (spdIdx < 0) spdIdx = 2;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Playback Speed", ref spdIdx, spds, spds.Length))
            _recorder.PlaybackSpeed = spdVals[spdIdx];

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), _recorderStatus);

        ImGui.Separator();
        ImGui.Text("Save / Load Macro:");
        ImGui.SetNextItemWidth(150);
        if (ImGui.Button("Save")) _recorder.SaveMacro(_recMacroName);
        ImGui.SameLine();
        if (ImGui.Button("Load")) _recorder.LoadMacro(_recMacroName);
    }

    private void DrawSettingsTab()
    {
        ImGui.Columns(2, "settings_cols", false);
        ImGui.SetColumnWidth(0, 350);

        // --- Left Column ---
        ImGui.SetWindowFontScale(1.2f);
        ImGui.Text("Settings");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.Dummy(new Vector2(0, 5));

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "General");
        
        bool ht = _cfg.HideTaskbar;
        if (ImGui.Checkbox("Hide from Taskbar", ref ht)) {
            _cfg.HideTaskbar = ht;
            ShowInTaskbar = !ht;
        }

        string hbindText = _cfg.HideBind == 0 ? "Hide Bind: none" : "Hide Bind: " + KeyName(_cfg.HideBind);
        if (_bindMode && _bindingTarget == 1) hbindText = "...press key...";
        if (ImGui.Button(hbindText, new Vector2(200, 0))) BeginBind(1);

        ImGui.Dummy(new Vector2(0, 10));

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Notifications");
        string[] notifPos = { "Bottom Left", "Bottom Right", "Top Left", "Top Right" };
        int notifIdx = _cfg.NotificationPosition;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Position", ref notifIdx, notifPos, notifPos.Length))
            _cfg.NotificationPosition = notifIdx;

        ImGui.Dummy(new Vector2(0, 10));
        
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Visuals");
        bool part = _cfg.ParticleEnabled;
        if (ImGui.Checkbox("Particle Effect", ref part)) _cfg.ParticleEnabled = part;
        
        // Hide background toggle removed to force dark theme
        
        ImGui.Dummy(new Vector2(0, 10));

        if (ImGui.Button("P+ Optimize PC", new Vector2(150, 0)))
        {
            _misc.OptimizePC();
        }

        ImGui.Dummy(new Vector2(0, 10));

        if (ImGui.Button("Save Config", new Vector2(150, 0)))
        {
            _cfg.Save();
        }

        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button("Destruct", new Vector2(150, 0))) Close();

        ImGui.NextColumn();

        // --- Right Column ---
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Server Presets");
        
        ImGui.SetNextItemWidth(150);
        ImGui.InputText("##ps_name", ref _presetAddName, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.InputFloat("CPS", ref _presetAddCps, 0.1f, 1.0f, "%.1f");
        string[] rModes = { "jitter", "butterfly", "nodelay", "manual" };
        ImGui.SetNextItemWidth(100);
        ImGui.Combo("##ps_rmode", ref _presetAddRand, rModes, rModes.Length);
        ImGui.SameLine();
        if (ImGui.Button("+ Add"))
        {
            if (!string.IsNullOrWhiteSpace(_presetAddName))
            {
                var newPreset = new PresetConfig { 
                    Name = _presetAddName, 
                    Server = _presetAddName, 
                    Cps = _presetAddCps, 
                    RandMode = _presetAddRand, 
                    IsBuiltIn = false 
                };
                if (_presetAddRand == 3)
                {
                    newPreset.CustomCpsWeights = new double[25];
                    for(int i = 0; i < 25; i++) newPreset.CustomCpsWeights[i] = _presetAddCustomCpsWeightsFloat[i];
                }
                _cfg.Presets.Add(newPreset);
            }
        }

        if (_presetAddRand == 3)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
            if (ImGui.TreeNode("Edit Custom Randomization##Preset"))
            {
                for (int i = 0; i < 25; i++)
                {
                    ImGui.SetNextItemWidth(150);
                    ImGui.SliderFloat($"CPS {i+1}##Preset", ref _presetAddCustomCpsWeightsFloat[i], 0f, 100f, "%.1f");
                    if (i % 2 == 0 && i < 24) ImGui.SameLine();
                }
                ImGui.TreePop();
            }
        }

        ImGui.Separator();

        // Share Current Config removed
        
        ImGui.BeginChild("presets_list");
        for (int i = 0; i < _cfg.Presets.Count; i++)
        {
            var p = _cfg.Presets[i];
            ImGui.PushID(i);
            ImGui.BeginGroup();
            
            ImGui.Text($"{p.Name}");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"avg cps: {p.Cps:F1} | rand: {p.RandModeName()}");
            if (ImGui.Button("Load"))
            {
                _cfg.AverageCps = p.Cps;
                _cfg.RandMode = p.RandMode;
                if (p.RandMode == 3 && p.CustomCpsWeights != null && p.CustomCpsWeights.Length == 25)
                {
                    for(int w=0; w<25; w++)
                    {
                        _cfg.CustomCpsWeights[w] = p.CustomCpsWeights[w];
                        _customCpsWeightsFloat[w] = (float)p.CustomCpsWeights[w];
                    }
                }
            }
            if (!p.IsBuiltIn)
            {
                ImGui.SameLine();
                if (ImGui.Button("Delete", new Vector2(60, 0)))
                {
                    _cfg.Presets.RemoveAt(i);
                    ImGui.PopID();
                    ImGui.EndGroup();
                    break;
                }
                ImGui.SameLine();
                if (ImGui.Button("Share", new Vector2(60, 0)))
                {
                    string json = "{\"UserPresets\":[{\"Name\":\"" + p.Name + "\",\"Server\":\"" + p.Server + "\",\"Cps\":" + p.Cps.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"RandMode\":" + p.RandMode;
                    if (p.RandMode == 3 && p.CustomCpsWeights != null)
                    {
                        json += ",\"CustomCpsWeights\":[" + string.Join(",", p.CustomCpsWeights) + "]";
                    }
                    json += "}]}";
                    _ = _iot.PublishAsync(json);
                }
            }
            ImGui.EndGroup();
            ImGui.PopID();
            ImGui.Separator();
        }
        ImGui.EndChild();

        ImGui.Columns(1);
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
        string d2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "lospoderosos", "resource");
        if (Directory.Exists(d2))
            foreach (string f in Directory.GetFiles(d2, "*.wav"))
                if (!list.Contains(Path.GetFileName(f)))
                    list.Add(Path.GetFileName(f));
        return list;
    }

    // ── Key binding logic ───────────────────────────────────────────────────

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

    private void BindTick(object s, EventArgs e)
    {
        if (!_bindMode) { _bindTimer.Stop(); return; }

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

        int[] mouseButtons = { 5, 6, 4, 2, 1 };
        foreach (int mb in mouseButtons)
        {
            if ((Win32.GetAsyncKeyState(mb) & 0x8000) != 0)
            {
                FinishBind(mb);
                return;
            }
        }

        for (int i = 8; i < 256; i++)
        {
            if ((Win32.GetAsyncKeyState(i) & 0x8000) != 0)
            {
                FinishBind(i);
                return;
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
        Application.Idle -= RenderLoop;

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
