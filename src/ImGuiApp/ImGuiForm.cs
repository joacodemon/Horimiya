using System;
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

    private Stopwatch _frameSw = Stopwatch.StartNew();

    // Cached sound list to avoid filesystem scanning every frame
    private List<string> _cachedSounds = null;
    private long _soundsCacheTimeMs = 0;

    public ImGuiForm(AppConfig cfg, Clicker clicker, Recorder recorder, Misc misc)
    {
        _cfg = cfg;
        _clicker = clicker;
        _recorder = recorder;
        _misc = misc;

        for (int i = 0; i < 25; i++)
            _customCpsWeightsFloat[i] = (float)_cfg.CustomCpsWeights[i];

        Text = "Los Poderosos - ImGui UI";
        ClientSize = new Size(950, 470);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _glControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 4));
        _glControl.Dock = DockStyle.Fill;
        Controls.Add(_glControl);

        _bindTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _bindTimer.Tick += BindTick;

        _recorder.StatusChanged += s => { _recorderStatus = s; };
        _misc.ClickBindTriggered += () => { 
            _clicker.Clicking = !_clicker.Clicking; 
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

        // Enable VSync: this blocks SwapBuffers until the monitor refreshes (e.g. 60 or 144 Hz).
        // This gives perfectly smooth FPS without stuttering, and prevents 100% GPU usage.
        _glControl.VSync = true;
        
        Application.Idle += RenderLoop;
        
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var style = ImGui.GetStyle();
        Color accent = Color.FromArgb(_cfg.ColorAccent);
        Vector4 accVec = new Vector4(accent.R / 255f, accent.G / 255f, accent.B / 255f, 1f);
        Vector4 accHoverVec = new Vector4(Math.Min(1f, accent.R / 255f + 0.2f), Math.Min(1f, accent.G / 255f + 0.2f), Math.Min(1f, accent.B / 255f + 0.2f), 1f);
        Vector4 accActiveVec = new Vector4(Math.Max(0f, accent.R / 255f - 0.2f), Math.Max(0f, accent.G / 255f - 0.2f), Math.Max(0f, accent.B / 255f - 0.2f), 1f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.90f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.06f, 0.06f, 0.06f, 0.94f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.30f, 0.30f, 0.30f, 0.50f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.16f, 0.16f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.24f, 0.24f, 0.24f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.30f, 0.30f, 0.30f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.04f, 0.04f, 0.04f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.04f, 0.04f, 0.04f, 1.00f);
        style.Colors[(int)ImGuiCol.CheckMark] = accVec;
        style.Colors[(int)ImGuiCol.SliderGrab] = accVec;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accActiveVec;
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.16f, 0.16f, 0.16f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = accVec;
        style.Colors[(int)ImGuiCol.ButtonActive] = accActiveVec;
        style.Colors[(int)ImGuiCol.Header] = accVec;
        style.Colors[(int)ImGuiCol.HeaderHovered] = accHoverVec;
        style.Colors[(int)ImGuiCol.HeaderActive] = accActiveVec;
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.TabHovered] = accVec;
        style.Colors[(int)ImGuiCol.TabActive] = accActiveVec;
        style.Colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.08f, 0.08f, 0.08f, 1.00f);
        style.Colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);

        style.WindowRounding = 6.0f;
        style.ChildRounding = 6.0f;
        style.FrameRounding = 4.0f;
        style.PopupRounding = 4.0f;
        style.ScrollbarRounding = 4.0f;
        style.GrabRounding = 4.0f;
        style.TabRounding = 4.0f;
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
            
            // SwapBuffers blocks if VSync is true, pacing the loop perfectly
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

    private void DrawUI()
    {
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(950, 470));
        
        ImGui.Begin("Main", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus);

        if (_showBg && _texBg != 0)
        {
            ImGui.GetWindowDrawList().AddImage((IntPtr)_texBg, new Vector2(0, 0), new Vector2(950, 470));
        }

        // Header
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "los poderosos");
        ImGui.Separator();

        if (ImGui.BeginTabBar("Tabs", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("LMB"))
            {
                DrawLMBTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("PRESETS"))
            {
                DrawPresetsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("MISC"))
            {
                DrawMiscTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawLMBTab()
    {
        ImGui.Text("Left Clicker Settings");
        ImGui.Separator();

        ImGui.Columns(2, "lmb_cols", false);
        ImGui.SetColumnWidth(0, 450);

        // Left column
        bool clicking = _clicker.Clicking;
        if (ImGui.Checkbox("Toggle Clicking", ref clicking))
            _clicker.Clicking = clicking;

        ImGui.SameLine(150);
        string bindText = _cfg.ClickBind == 0 ? "Bind: none" : "Bind: " + KeyName(_cfg.ClickBind);
        if (_bindMode && _bindingTarget == 0) bindText = "...press key...";
        if (ImGui.Button(bindText, new Vector2(120, 0))) BeginBind(0);

        ImGui.SameLine(280);
        string[] modes = { "Hold", "Toggle", "Always" };
        int modeIdx = _cfg.Mode;
        ImGui.SetNextItemWidth(100);
        if (ImGui.Combo("Mode", ref modeIdx, modes, modes.Length))
            _cfg.Mode = modeIdx;

        float cps = (float)_cfg.AverageCps;
        ImGui.SetNextItemWidth(300);
        if (ImGui.SliderFloat("Average CPS", ref cps, 1.0f, 50.0f, "%.1f"))
            _cfg.AverageCps = cps;

        bool oig = _cfg.OnlyInGame;
        if (ImGui.Checkbox("Only In Game", ref oig)) _cfg.OnlyInGame = oig;
        
        bool rmb = _cfg.RmbLock;
        if (ImGui.Checkbox("RMB-Lock", ref rmb)) _cfg.RmbLock = rmb;
        
        bool wim = _cfg.WorkInMenus;
        if (ImGui.Checkbox("Work in Menus", ref wim)) _cfg.WorkInMenus = wim;
        


        string[] bbModes = { "Off", "Full", "Sneak" };
        int bbIdx = _cfg.BBMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Break Blocks", ref bbIdx, bbModes, bbModes.Length))
            _cfg.BBMode = bbIdx;

        ImGui.NextColumn();

        // Right column
        List<string> sounds = GetSoundsCached();
        int soundIdx = sounds.IndexOf(_cfg.Sound);
        if (soundIdx < 0) soundIdx = 0;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Click Sound", ref soundIdx, sounds.ToArray(), sounds.Count))
            _cfg.Sound = sounds[soundIdx];

        string[] randModes = { "Jitter", "Butterfly", "NoDelay", "Manual" };
        int randIdx = _cfg.RandMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Randomization", ref randIdx, randModes, randModes.Length))
            _cfg.RandMode = randIdx;

        if (_cfg.RandMode == 3)
        {
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

        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Jitter: legit human-like variance");
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Butterfly: rapid double-click");
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "NoDelay: constant, no randomization");

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
        ImGui.InputText("##macroname", ref _recMacroName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Save")) _recorder.SaveMacro(_recMacroName);
        ImGui.SameLine();
        if (ImGui.Button("Load")) _recorder.LoadMacro(_recMacroName);
    }

    private void DrawPresetsTab()
    {
        ImGui.Text("Server Presets");
        ImGui.Separator();

        ImGui.SetNextItemWidth(150);
        ImGui.InputText("##ps_name", ref _presetAddName, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.InputFloat("CPS", ref _presetAddCps, 0.1f, 1.0f, "%.1f");
        ImGui.SameLine();
        string[] rModes = { "jitter", "butterfly", "nodelay", "manual" };
        ImGui.SetNextItemWidth(100);
        ImGui.Combo("##ps_rmode", ref _presetAddRand, rModes, rModes.Length);
        ImGui.SameLine();
        if (ImGui.Button("+ Add"))
        {
            if (!string.IsNullOrWhiteSpace(_presetAddName))
            {
                _cfg.Presets.Add(new PresetConfig { 
                    Name = _presetAddName, 
                    Server = _presetAddName, 
                    Cps = _presetAddCps, 
                    RandMode = _presetAddRand, 
                    IsBuiltIn = false 
                });
            }
        }

        ImGui.Separator();

        ImGui.BeginChild("presets_list");
        for (int i = 0; i < _cfg.Presets.Count; i++)
        {
            var p = _cfg.Presets[i];
            ImGui.PushID(i);
            ImGui.BeginGroup();
            
            ImGui.Text($"{p.Name} {(p.IsBuiltIn ? "[recommended]" : "")}");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"avg cps: {p.Cps:F1} | rand: {p.RandModeName()}");
            if (ImGui.Button("Load"))
            {
                _cfg.AverageCps = p.Cps;
                _cfg.RandMode = p.RandMode;
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
            }
            ImGui.EndGroup();
            ImGui.PopID();
            ImGui.Separator();
        }
        if (_texEaster != 0)
        {
            ImGui.SameLine();
            ImGui.Image((IntPtr)_texEaster, new Vector2(60, 60));
        }

        ImGui.EndChild();
    }

    private void DrawMiscTab()
    {
        ImGui.Text("Miscellaneous Settings");
        ImGui.Separator();

        ImGui.Columns(3, "misc_cols", false);

        // Col 1: Destruct
        ImGui.Text("Destruct");
        if (ImGui.Button("Flush DNS", new Vector2(200, 0)))
        {
            try { System.Diagnostics.Process.Start("cmd.exe", "/c \"ipconfig /flushdns & pause\""); } catch { }
        }
        if (ImGui.Button("BITS", new Vector2(200, 0)))
        {
            try 
            {
                string script = "@echo off\r\ntitle BITS Monitor\r\necho Monitoring BITS service... (Do not close this window)\r\n:bitch\r\nping 127.0.0.1 -n 2 >nul\r\nsc query BITS | find /I \"STATE\" | find \"STOPPED\" >nul\r\nif %ERRORLEVEL% EQU 0 goto :start\r\ngoto :bitch\r\n\r\n:start\r\necho [!] BITS is stopped. Starting it now...\r\nsc start BITS >nul\r\necho [+] BITS Started.\r\ngoto :bitch";
                string tempBat = Path.Combine(Path.GetTempPath(), "bits_opt.bat");
                File.WriteAllText(tempBat, script);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + tempBat + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
            } 
            catch { }
        }
        
        if (ImGui.Button("Destruct", new Vector2(200, 0))) Close();

        ImGui.NextColumn();

        // Col 2: Hide
        ImGui.Text("Hide");
        bool ht = _cfg.HideTaskbar;
        if (ImGui.Checkbox("Hide from Taskbar", ref ht)) {
            _cfg.HideTaskbar = ht;
            ShowInTaskbar = !ht;
        }

        string hbindText = _cfg.HideBind == 0 ? "Hide Bind: none" : "Hide Bind: " + KeyName(_cfg.HideBind);
        if (_bindMode && _bindingTarget == 1) hbindText = "...press key...";
        if (ImGui.Button(hbindText, new Vector2(200, 0))) BeginBind(1);

        ImGui.NextColumn();

        // Col 3: Visual
        ImGui.Text("Visual Settings");
        bool part = _cfg.ParticleEnabled;
        if (ImGui.Checkbox("Particle Effect", ref part)) _cfg.ParticleEnabled = part;
        
        ImGui.Checkbox("Show Background", ref _showBg);

        Color acc = Color.FromArgb(_cfg.ColorAccent);
        Vector3 accVec = new Vector3(acc.R / 255f, acc.G / 255f, acc.B / 255f);
        if (ImGui.ColorEdit3("Accent Color", ref accVec, ImGuiColorEditFlags.NoInputs))
        {
            _cfg.ColorAccent = Color.FromArgb((int)(accVec.X * 255), (int)(accVec.Y * 255), (int)(accVec.Z * 255)).ToArgb();
            ApplyTheme();
        }

        if (ImGui.Button("Save Config"))
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "default.json"), _cfg.ToJson());
                _recorderStatus = "Saved config to default.json";
            } catch { }
        }

        ImGui.Columns(1);
    }

    // Cached version: only rescans filesystem every 5 seconds
    private List<string> GetSoundsCached()
    {
        long now = _frameSw.ElapsedMilliseconds + _soundsCacheTimeMs;
        if (_cachedSounds == null || (Environment.TickCount - _soundsCacheTimeMs) > 5000)
        {
            _cachedSounds = GetSounds();
            _soundsCacheTimeMs = Environment.TickCount;
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
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        Application.Idle -= RenderLoop;

        _bindTimer.Stop();
        _clicker.Stop();
        _recorder.Stop();
        _misc.Stop();
        Win32.StopMouseHook();
        Win32.timeEndPeriod(1); // Restore Windows timer resolution

        _controller?.Dispose();
        if (_texLogo != 0) GL.DeleteTexture(_texLogo);
        if (_texBg != 0) GL.DeleteTexture(_texBg);
        if (_texEaster != 0) GL.DeleteTexture(_texEaster);
    }
}
