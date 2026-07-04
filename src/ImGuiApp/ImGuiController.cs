using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ImGuiNET;

public class ImGuiController : IDisposable
{
    private int _vertexArrayObject;
    private int _vertexBufferObject;
    private int _indexBufferObject;
    private int _fontTexture;
    private int _shaderProgram;
    private int _vertexShader;
    private int _fragmentShader;
    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationPosition;
    private int _attribLocationUV;
    private int _attribLocationColor;
    private int _windowWidth;
    private int _windowHeight;
    private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

    // Input state
    private System.Drawing.Point _mousePos;
    private bool[] _mouseDown = new bool[3];
    private float _mouseWheel;
    private readonly List<char> _typedChars = new List<char>();
    private readonly HashSet<int> _keysDown = new HashSet<int>();
    private bool _ctrlDown, _shiftDown, _altDown;

    public ImGuiController(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        if (System.IO.File.Exists(@"C:\Windows\Fonts\segoeui.ttf"))
        {
            // Load Segoe UI for a much cleaner look
            io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 16.0f);
        }
        else
        {
            io.Fonts.AddFontDefault();
        }
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        SetKeyMappings();
        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);
        ImGui.NewFrame();
    }

    // ── Input methods called from ImGuiForm ──────────────────────

    public void SetMousePosition(int x, int y)
    {
        _mousePos = new System.Drawing.Point(x, y);
    }

    public void SetMouseButton(int button, bool down)
    {
        if (button >= 0 && button < 3)
            _mouseDown[button] = down;
    }

    public void AddMouseWheel(float delta)
    {
        _mouseWheel += delta;
    }

    public void AddTypedChar(char c)
    {
        _typedChars.Add(c);
    }

    public void SetKeyDown(int key, bool down)
    {
        if (down) _keysDown.Add(key);
        else _keysDown.Remove(key);
    }

    public void SetModifiers(bool ctrl, bool shift, bool alt)
    {
        _ctrlDown = ctrl;
        _shiftDown = shift;
        _altDown = alt;
    }

    // ── Update & Render ──────────────────────────────────────────

    public void Update(int width, int height, float deltaTime = 1f / 60f)
    {
        _windowWidth = width;
        _windowHeight = height;
        SetPerFrameImGuiData(deltaTime);
        UpdateInput();
    }

    public void Render()
    {
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    private void SetPerFrameImGuiData(float deltaTime)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(
            _windowWidth / _scaleFactor.X,
            _windowHeight / _scaleFactor.Y);
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaTime;
    }

    private void UpdateInput()
    {
        var io = ImGui.GetIO();

        // Mouse position
        io.MousePos = new System.Numerics.Vector2(_mousePos.X, _mousePos.Y);

        // Mouse buttons
        io.MouseDown[0] = _mouseDown[0];
        io.MouseDown[1] = _mouseDown[1];
        io.MouseDown[2] = _mouseDown[2];

        // Mouse wheel
        io.MouseWheel = _mouseWheel;
        _mouseWheel = 0;

        // Typed characters
        foreach (var c in _typedChars)
            io.AddInputCharacter(c);
        _typedChars.Clear();

        // Keys
        for (int i = 0; i < io.KeysDown.Count && i < 512; i++)
            io.KeysDown[i] = _keysDown.Contains(i);

        io.KeyCtrl = _ctrlDown;
        io.KeyShift = _shiftDown;
        io.KeyAlt = _altDown;

        ImGui.NewFrame();
    }

    private void SetKeyMappings()
    {
        var io = ImGui.GetIO();
        io.KeyMap[(int)ImGuiKey.Tab] = (int)Keys.Tab;
        io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Keys.Left;
        io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Keys.Right;
        io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Keys.Up;
        io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Keys.Down;
        io.KeyMap[(int)ImGuiKey.PageUp] = (int)Keys.PageUp;
        io.KeyMap[(int)ImGuiKey.PageDown] = (int)Keys.PageDown;
        io.KeyMap[(int)ImGuiKey.Home] = (int)Keys.Home;
        io.KeyMap[(int)ImGuiKey.End] = (int)Keys.End;
        io.KeyMap[(int)ImGuiKey.Delete] = (int)Keys.Delete;
        io.KeyMap[(int)ImGuiKey.Backspace] = (int)Keys.Back;
        io.KeyMap[(int)ImGuiKey.Enter] = (int)Keys.Enter;
        io.KeyMap[(int)ImGuiKey.Escape] = (int)Keys.Escape;
        io.KeyMap[(int)ImGuiKey.A] = (int)Keys.A;
        io.KeyMap[(int)ImGuiKey.C] = (int)Keys.C;
        io.KeyMap[(int)ImGuiKey.V] = (int)Keys.V;
        io.KeyMap[(int)ImGuiKey.X] = (int)Keys.X;
        io.KeyMap[(int)ImGuiKey.Y] = (int)Keys.Y;
        io.KeyMap[(int)ImGuiKey.Z] = (int)Keys.Z;
    }

    // ── OpenGL Resources ─────────────────────────────────────────

    private void CreateDeviceResources()
    {
        const string vertexSource = @"#version 330 core
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
layout (location = 2) in vec4 Color;
uniform mat4 projection_matrix;
out vec2 frag_uv;
out vec4 frag_color;
void main()
{
    frag_uv = UV;
    frag_color = Color;
    gl_Position = projection_matrix * vec4(Position, 0, 1);
}";
        const string fragmentSource = @"#version 330 core
in vec2 frag_uv;
in vec4 frag_color;
uniform sampler2D texture0;
out vec4 out_color;
void main()
{
    out_color = frag_color * texture(texture0, frag_uv);
}";
        _vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(_vertexShader, vertexSource);
        GL.CompileShader(_vertexShader);
        _fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(_fragmentShader, fragmentSource);
        GL.CompileShader(_fragmentShader);
        _shaderProgram = GL.CreateProgram();
        GL.AttachShader(_shaderProgram, _vertexShader);
        GL.AttachShader(_shaderProgram, _fragmentShader);
        GL.LinkProgram(_shaderProgram);
        _attribLocationTex = GL.GetUniformLocation(_shaderProgram, "texture0");
        _attribLocationProjMtx = GL.GetUniformLocation(_shaderProgram, "projection_matrix");
        _attribLocationPosition = GL.GetAttribLocation(_shaderProgram, "Position");
        _attribLocationUV = GL.GetAttribLocation(_shaderProgram, "UV");
        _attribLocationColor = GL.GetAttribLocation(_shaderProgram, "Color");

        _vertexArrayObject = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArrayObject);
        _vertexBufferObject = GL.GenBuffer();
        _indexBufferObject = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, 10000 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
        GL.BufferData(BufferTarget.ElementArrayBuffer, 2000 * sizeof(ushort), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(_attribLocationPosition);
        GL.EnableVertexAttribArray(_attribLocationUV);
        GL.EnableVertexAttribArray(_attribLocationColor);
        int stride = Marshal.SizeOf<ImDrawVert>();
        GL.VertexAttribPointer(_attribLocationPosition, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(_attribLocationUV, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.VertexAttribPointer(_attribLocationColor, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        // Font texture
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);
        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private void RenderImDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;
        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0) return;

        // Save GL state
        GL.GetInteger(GetPName.CurrentProgram, out int lastProgram);
        GL.GetInteger(GetPName.Texture2D, out int lastTexture);
        GL.GetInteger(GetPName.ArrayBufferBinding, out int lastArrayBuffer);
        bool lastEnableBlend = GL.IsEnabled(EnableCap.Blend);
        bool lastEnableCullFace = GL.IsEnabled(EnableCap.CullFace);
        bool lastEnableDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool lastEnableScissorTest = GL.IsEnabled(EnableCap.ScissorTest);

        // Setup GL state
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        GL.Viewport(0, 0, fbWidth, fbHeight);
        var orthoProjection = Matrix4.CreateOrthographicOffCenter(
            drawData.DisplayPos.X,
            drawData.DisplayPos.X + drawData.DisplaySize.X,
            drawData.DisplayPos.Y + drawData.DisplaySize.Y,
            drawData.DisplayPos.Y,
            -1.0f, 1.0f);
        GL.UseProgram(_shaderProgram);
        GL.Uniform1(_attribLocationTex, 0);
        GL.UniformMatrix4(_attribLocationProjMtx, false, ref orthoProjection);
        GL.BindVertexArray(_vertexArrayObject);

        System.Numerics.Vector2 clipOff = drawData.DisplayPos;
        System.Numerics.Vector2 clipScale = drawData.FramebufferScale;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdListsRange[n];
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>(), cmdList.VtxBuffer.Data, BufferUsageHint.StreamDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, cmdList.IdxBuffer.Size * sizeof(ushort), cmdList.IdxBuffer.Data, BufferUsageHint.StreamDraw);

            int vtxOffset = 0;
            int idxOffset = 0;
            for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero) continue;

                System.Numerics.Vector4 clipRect;
                clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
                clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
                clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                if (clipRect.X < fbWidth && clipRect.Y < fbHeight && clipRect.Z >= 0 && clipRect.W >= 0)
                {
                    GL.Scissor(
                        (int)clipRect.X,
                        (int)(fbHeight - clipRect.W),
                        (int)(clipRect.Z - clipRect.X),
                        (int)(clipRect.W - clipRect.Y));
                    GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                    GL.DrawElementsBaseVertex(
                        PrimitiveType.Triangles,
                        (int)pcmd.ElemCount,
                        DrawElementsType.UnsignedShort,
                        (IntPtr)(idxOffset * sizeof(ushort)),
                        vtxOffset);
                }
                idxOffset += (int)pcmd.ElemCount;
            }
            vtxOffset += cmdList.VtxBuffer.Size;
        }

        // Restore GL state
        GL.UseProgram(lastProgram);
        GL.BindTexture(TextureTarget.Texture2D, lastTexture);
        GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
        if (lastEnableBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
        if (lastEnableCullFace) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        if (lastEnableDepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        if (lastEnableScissorTest) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
    }

    public void Dispose()
    {
        GL.DeleteTexture(_fontTexture);
        GL.DeleteBuffer(_vertexBufferObject);
        GL.DeleteBuffer(_indexBufferObject);
        GL.DeleteVertexArray(_vertexArrayObject);
        GL.DeleteProgram(_shaderProgram);
        GL.DeleteShader(_vertexShader);
        GL.DeleteShader(_fragmentShader);
    }
}
