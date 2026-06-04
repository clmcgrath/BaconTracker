using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace BaconTracker.App;

public class ImGuiController : IDisposable
{
    private readonly GL _gl;
    private uint _vertexArrayObject;
    private uint _vertexBufferObject;
    private uint _indexBufferObject;
    private uint _shaderProgram;
    private int _attribLocationPosition;
    private int _attribLocationUV;
    private int _attribLocationColor;
    private int _attribLocationProjMtx;
    private uint _fontTexture;
    private int _windowWidth;
    private int _windowHeight;

    public ImGuiController(GL gl, int windowWidth, int windowHeight)
    {
        _gl = gl;
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;

        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        ApplyHearthstoneStyle();

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;

        InitializeDeviceObjects();
    }

    public void Resize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public void Update(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DeltaTime = deltaSeconds;
        io.DisplaySize = new System.Numerics.Vector2(_windowWidth, _windowHeight);

        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private unsafe void InitializeDeviceObjects()
    {
        // Shaders
        string vertexShaderSource = @"#version 300 es
precision mediump float;
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
layout (location = 2) in vec4 Color;
uniform mat4 ProjMtx;
out vec2 Frag_UV;
out vec4 Frag_Color;
void main()
{
    Frag_UV = UV;
    Frag_Color = Color;
    gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
}";

        string fragmentShaderSource = @"#version 300 es
precision mediump float;
in vec2 Frag_UV;
in vec4 Frag_Color;
uniform sampler2D Texture;
layout (location = 0) out vec4 Out_Color;
void main()
{
    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
}";

        uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexShaderSource);
        _gl.CompileShader(vertexShader);
        CheckShaderCompile(vertexShader);

        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentShaderSource);
        _gl.CompileShader(fragmentShader);
        CheckShaderCompile(fragmentShader);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vertexShader);
        _gl.AttachShader(_shaderProgram, fragmentShader);
        _gl.LinkProgram(_shaderProgram);
        CheckProgramLink(_shaderProgram);

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _attribLocationProjMtx = _gl.GetUniformLocation(_shaderProgram, "ProjMtx");
        _attribLocationPosition = _gl.GetAttribLocation(_shaderProgram, "Position");
        _attribLocationUV = _gl.GetAttribLocation(_shaderProgram, "UV");
        _attribLocationColor = _gl.GetAttribLocation(_shaderProgram, "Color");

        // Buffers
        _vertexBufferObject = _gl.GenBuffer();
        _indexBufferObject = _gl.GenBuffer();
        _vertexArrayObject = _gl.GenVertexArray();

        _gl.BindVertexArray(_vertexArrayObject);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBufferObject);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBufferObject);

        uint sizeOfImDrawVert = (uint)Marshal.SizeOf<ImDrawVert>();

        _gl.EnableVertexAttribArray((uint)_attribLocationPosition);
        _gl.VertexAttribPointer((uint)_attribLocationPosition, 2, VertexAttribPointerType.Float, false, sizeOfImDrawVert, (void*)0);

        _gl.EnableVertexAttribArray((uint)_attribLocationUV);
        _gl.VertexAttribPointer((uint)_attribLocationUV, 2, VertexAttribPointerType.Float, false, sizeOfImDrawVert, (void*)8);

        _gl.EnableVertexAttribArray((uint)_attribLocationColor);
        _gl.VertexAttribPointer((uint)_attribLocationColor, 4, VertexAttribPointerType.UnsignedByte, true, sizeOfImDrawVert, (void*)16);

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        BuildFontTexture();
    }

    private unsafe void BuildFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
        int minFilter = (int)TextureMinFilter.Linear;
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in minFilter);
        int magFilter = (int)TextureMagFilter.Linear;
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in magFilter);

        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)pixels);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private unsafe void RenderDrawData(ImDrawDataPtr drawData)
    {
        // Avoid rendering when minimized
        if (drawData.DisplaySize.X <= 0.0f || drawData.DisplaySize.Y <= 0.0f)
            return;

        // Backup GL state
        int lastActiveTexture;
        _gl.GetInteger(GetPName.ActiveTexture, out lastActiveTexture);
        _gl.ActiveTexture(TextureUnit.Texture0);

        int lastProgram;
        _gl.GetInteger(GetPName.CurrentProgram, out lastProgram);
        int lastTexture;
        _gl.GetInteger(GetPName.TextureBinding2D, out lastTexture);
        int lastArrayBuffer;
        _gl.GetInteger(GetPName.ArrayBufferBinding, out lastArrayBuffer);
        int lastVertexArray;
        _gl.GetInteger(GetPName.VertexArrayBinding, out lastVertexArray);
        int lastBlendSrcRgb;
        _gl.GetInteger(GetPName.BlendSrcRgb, out lastBlendSrcRgb);
        int lastBlendDstRgb;
        _gl.GetInteger(GetPName.BlendDstRgb, out lastBlendDstRgb);
        int lastBlendSrcAlpha;
        _gl.GetInteger(GetPName.BlendSrcAlpha, out lastBlendSrcAlpha);
        int lastBlendDstAlpha;
        _gl.GetInteger(GetPName.BlendDstAlpha, out lastBlendDstAlpha);
        int lastBlendEquationRgb;
        _gl.GetInteger(GetPName.BlendEquationRgb, out lastBlendEquationRgb);
        int lastBlendEquationAlpha;
        _gl.GetInteger(GetPName.BlendEquationAlpha, out lastBlendEquationAlpha);
        int lastViewportX = 0, lastViewportY = 0, lastViewportWidth = 0, lastViewportHeight = 0;
        unsafe
        {
            int* lastViewport = stackalloc int[4];
            _gl.GetInteger(GetPName.Viewport, lastViewport);
            lastViewportX = lastViewport[0];
            lastViewportY = lastViewport[1];
            lastViewportWidth = lastViewport[2];
            lastViewportHeight = lastViewport[3];
        }

        int lastScissorBoxX = 0, lastScissorBoxY = 0, lastScissorBoxWidth = 0, lastScissorBoxHeight = 0;
        unsafe
        {
            int* lastScissorBox = stackalloc int[4];
            _gl.GetInteger(GetPName.ScissorBox, lastScissorBox);
            lastScissorBoxX = lastScissorBox[0];
            lastScissorBoxY = lastScissorBox[1];
            lastScissorBoxWidth = lastScissorBox[2];
            lastScissorBoxHeight = lastScissorBox[3];
        }

        bool lastEnableBlend = _gl.IsEnabled(EnableCap.Blend);
        bool lastEnableCullFace = _gl.IsEnabled(EnableCap.CullFace);
        bool lastEnableDepthTest = _gl.IsEnabled(EnableCap.DepthTest);
        bool lastEnableScissorTest = _gl.IsEnabled(EnableCap.ScissorTest);

        // Setup render state
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.ScissorTest);

        _gl.Viewport(0, 0, (uint)_windowWidth, (uint)_windowHeight);

        // Orthographic projection matrix
        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        float[] orthoProjection =
        {
            2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f, 0.0f,
            (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f
        };

        _gl.UseProgram(_shaderProgram);
        _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);

        _gl.BindVertexArray(_vertexArrayObject);

        System.Numerics.Vector2 clipOff = drawData.DisplayPos;
        System.Numerics.Vector2 clipScale = drawData.FramebufferScale;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            // Upload Vertex/Index Buffers
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBufferObject);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>()), (void*)cmdList.VtxBuffer.Data, BufferUsageARB.StreamDraw);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBufferObject);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)), (void*)cmdList.IdxBuffer.Data, BufferUsageARB.StreamDraw);

            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                var cmd = cmdList.CmdBuffer[cmdi];

                if (cmd.UserCallback != IntPtr.Zero)
                {
                    throw new NotImplementedException("User callbacks are not supported");
                }
                else
                {
                    // Project scissor/clipping rectangles into local framebuffer coordinates
                    System.Numerics.Vector4 clipRect;
                    clipRect.X = (cmd.ClipRect.X - clipOff.X) * clipScale.X;
                    clipRect.Y = (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                    clipRect.Z = (cmd.ClipRect.Z - clipOff.X) * clipScale.X;
                    clipRect.W = (cmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                    if (clipRect.X < _windowWidth && clipRect.Y < _windowHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                    {
                        // Apply scissor
                        _gl.Scissor((int)clipRect.X, (int)(_windowHeight - clipRect.W), (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));

                        // Bind texture and draw
                        _gl.BindTexture(TextureTarget.Texture2D, (uint)cmd.TextureId);
                        _gl.DrawElements(PrimitiveType.Triangles, cmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(cmd.IdxOffset * sizeof(ushort)));
                    }
                }
            }
        }

        // Restore GL state
        _gl.UseProgram((uint)lastProgram);
        _gl.BindTexture(TextureTarget.Texture2D, (uint)lastTexture);
        _gl.ActiveTexture((TextureUnit)lastActiveTexture);
        _gl.BindVertexArray((uint)lastVertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)lastArrayBuffer);
        _gl.BlendEquationSeparate((BlendEquationModeEXT)lastBlendEquationRgb, (BlendEquationModeEXT)lastBlendEquationAlpha);
        _gl.BlendFuncSeparate((BlendingFactor)lastBlendSrcRgb, (BlendingFactor)lastBlendDstRgb, (BlendingFactor)lastBlendSrcAlpha, (BlendingFactor)lastBlendDstAlpha);

        if (lastEnableBlend) _gl.Enable(EnableCap.Blend); else _gl.Disable(EnableCap.Blend);
        if (lastEnableCullFace) _gl.Enable(EnableCap.CullFace); else _gl.Disable(EnableCap.CullFace);
        if (lastEnableDepthTest) _gl.Enable(EnableCap.DepthTest); else _gl.Disable(EnableCap.DepthTest);
        if (lastEnableScissorTest) _gl.Enable(EnableCap.ScissorTest); else _gl.Disable(EnableCap.ScissorTest);

        _gl.Viewport(lastViewportX, lastViewportY, (uint)lastViewportWidth, (uint)lastViewportHeight);
        _gl.Scissor(lastScissorBoxX, lastScissorBoxY, (uint)lastScissorBoxWidth, (uint)lastScissorBoxHeight);
    }

    private void CheckShaderCompile(uint shader)
    {
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new Exception($"OpenGL shader compilation failed:\n{infoLog}");
        }
    }

    private void CheckProgramLink(uint program)
    {
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(program);
            throw new Exception($"OpenGL program linking failed:\n{infoLog}");
        }
    }

    private void ApplyHearthstoneStyle()
    {
        var style = ImGui.GetStyle();
        
        // Window & Element Rounding
        style.WindowRounding = 8.0f;
        style.FrameRounding = 5.0f;
        style.PopupRounding = 6.0f;
        style.GrabRounding = 4.0f;
        style.ScrollbarRounding = 12.0f;
        style.ChildRounding = 6.0f;
        
        // Padding & Spacing
        style.WindowPadding = new System.Numerics.Vector2(12, 12);
        style.FramePadding = new System.Numerics.Vector2(8, 6);
        style.ItemSpacing = new System.Numerics.Vector2(8, 8);
        
        // Set Hearthstone / Firestone Dark-Gold Theme Color Palette
        var colors = style.Colors;
        
        // Backgrounds
        colors[(int)ImGuiCol.WindowBg] = new System.Numerics.Vector4(0.09f, 0.08f, 0.07f, 0.88f); // Dark Charcoal/Stone
        colors[(int)ImGuiCol.ChildBg] = new System.Numerics.Vector4(0.07f, 0.06f, 0.05f, 0.50f);
        colors[(int)ImGuiCol.PopupBg] = new System.Numerics.Vector4(0.12f, 0.10f, 0.09f, 0.95f);
        
        // Borders
        colors[(int)ImGuiCol.Border] = new System.Numerics.Vector4(0.77f, 0.61f, 0.33f, 0.40f); // Antique Bronze
        colors[(int)ImGuiCol.BorderShadow] = new System.Numerics.Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        
        // Headers & Title Bars
        colors[(int)ImGuiCol.TitleBg] = new System.Numerics.Vector4(0.15f, 0.12f, 0.09f, 0.90f);
        colors[(int)ImGuiCol.TitleBgActive] = new System.Numerics.Vector4(0.24f, 0.18f, 0.11f, 1.00f); // Warm Brown
        colors[(int)ImGuiCol.TitleBgCollapsed] = new System.Numerics.Vector4(0.09f, 0.08f, 0.07f, 0.60f);
        
        // Buttons
        colors[(int)ImGuiCol.Button] = new System.Numerics.Vector4(0.28f, 0.20f, 0.13f, 0.80f); // Leather/Wood Brown
        colors[(int)ImGuiCol.ButtonHovered] = new System.Numerics.Vector4(0.45f, 0.33f, 0.18f, 1.00f); // Warm Gold-Brown
        colors[(int)ImGuiCol.ButtonActive] = new System.Numerics.Vector4(0.55f, 0.42f, 0.22f, 1.00f); // Amber Highlight
        
        // Frame Backgrounds (Input fields, checkbox bg, combos)
        colors[(int)ImGuiCol.FrameBg] = new System.Numerics.Vector4(0.16f, 0.13f, 0.10f, 0.70f);
        colors[(int)ImGuiCol.FrameBgHovered] = new System.Numerics.Vector4(0.25f, 0.20f, 0.15f, 0.80f);
        colors[(int)ImGuiCol.FrameBgActive] = new System.Numerics.Vector4(0.35f, 0.28f, 0.20f, 0.90f);
        
        // Scrollbar
        colors[(int)ImGuiCol.ScrollbarBg] = new System.Numerics.Vector4(0.07f, 0.06f, 0.05f, 0.60f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new System.Numerics.Vector4(0.35f, 0.28f, 0.20f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new System.Numerics.Vector4(0.45f, 0.35f, 0.25f, 0.90f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new System.Numerics.Vector4(0.55f, 0.42f, 0.30f, 1.00f);
        
        // Header (Selection list highlights)
        colors[(int)ImGuiCol.Header] = new System.Numerics.Vector4(0.38f, 0.28f, 0.15f, 0.70f);
        colors[(int)ImGuiCol.HeaderHovered] = new System.Numerics.Vector4(0.48f, 0.36f, 0.19f, 0.85f);
        colors[(int)ImGuiCol.HeaderActive] = new System.Numerics.Vector4(0.58f, 0.44f, 0.24f, 1.00f);
        
        // Text
        colors[(int)ImGuiCol.Text] = new System.Numerics.Vector4(0.95f, 0.92f, 0.85f, 1.0f); // Warm parchment white
        colors[(int)ImGuiCol.TextDisabled] = new System.Numerics.Vector4(0.60f, 0.55f, 0.48f, 1.00f);
        
        // Separation lines
        colors[(int)ImGuiCol.Separator] = new System.Numerics.Vector4(0.48f, 0.38f, 0.22f, 0.50f);
        colors[(int)ImGuiCol.SeparatorHovered] = new System.Numerics.Vector4(0.58f, 0.46f, 0.26f, 0.75f);
        colors[(int)ImGuiCol.SeparatorActive] = new System.Numerics.Vector4(0.68f, 0.54f, 0.30f, 1.00f);
        
        // Tabs
        colors[(int)ImGuiCol.Tab] = new System.Numerics.Vector4(0.20f, 0.15f, 0.10f, 0.80f);
        colors[(int)ImGuiCol.TabHovered] = new System.Numerics.Vector4(0.38f, 0.28f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.TabSelected] = new System.Numerics.Vector4(0.48f, 0.36f, 0.22f, 1.00f);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vertexBufferObject);
        _gl.DeleteBuffer(_indexBufferObject);
        _gl.DeleteVertexArray(_vertexArrayObject);
        _gl.DeleteTexture(_fontTexture);
        _gl.DeleteProgram(_shaderProgram);
        ImGui.DestroyContext();
    }
}
