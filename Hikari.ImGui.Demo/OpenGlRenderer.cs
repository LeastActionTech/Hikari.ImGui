// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Hikari.ImGui.Demo;

/// <summary>
/// Lightweight Silk.NET OpenGL rendering backend for Hikari.ImGui.
/// Translates ImDrawData into OpenGL draw calls with orthographic projection and scissor clipping.
/// </summary>
public sealed class OpenGlRenderer : IDisposable
{
    private readonly GL _gl;
    private uint _program;
    private int _uProjLocation;
    private int _uTextureLocation;

    private uint _vao;
    private uint _vbo;
    private uint _ibo;
    private uint _fontTexture;

    private const string VertexShaderSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aUV;
layout (location = 2) in vec4 aCol;

uniform mat4 uProj;

out vec2 Frag_UV;
out vec4 Frag_Color;

void main()
{
    Frag_UV = aUV;
    Frag_Color = aCol;
    gl_Position = uProj * vec4(aPos.xy, 0.0, 1.0);
}
";

    private const string FragmentShaderSource = @"#version 330 core
in vec2 Frag_UV;
in vec4 Frag_Color;

uniform sampler2D uTexture;
out vec4 Out_Color;

void main()
{
    Out_Color = Frag_Color * texture(uTexture, Frag_UV);
}
";

    #region Rendering API

    /// <summary>Creates the OpenGL backend, compiling its shader program and allocating vertex buffers.</summary>
    /// <param name="gl">Silk.NET OpenGL API bound to the current context.</param>
    public OpenGlRenderer(GL gl)
    {
        _gl = gl;
        InitShaders();
        InitBuffers();
    }

    /// <summary>Compiles and links the OpenGL shader programs for UI rendering.</summary>
    private void InitShaders()
    {
        uint vs = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fs = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vs);
        _gl.AttachShader(_program, fs);
        _gl.LinkProgram(_program);

        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_program);
            throw new InvalidOperationException($"GL Program link failed: {infoLog}");
        }

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);

        _uProjLocation = _gl.GetUniformLocation(_program, "uProj");
        _uTextureLocation = _gl.GetUniformLocation(_program, "uTexture");
    }

    /// <summary>Compiles a single OpenGL shader from source.</summary>
    /// <param name="type">The shader stage type.</param>
    /// <param name="source">The GLSL source code.</param>
    /// <returns>The OpenGL shader handle.</returns>
    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"GL Shader compilation failed ({type}): {infoLog}");
        }
        return shader;
    }

    /// <summary>Initializes vertex array, vertex buffer, and index buffer objects.</summary>
    private unsafe void InitBuffers()
    {
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ibo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);

        uint stride = (uint)sizeof(ImDrawVert);

        // location 0: Pos (vec2)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        // location 1: UV (vec2)
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)sizeof(ImVec2));

        // location 2: Col (vec4 / normalized ubyte4)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)(sizeof(ImVec2) * 2));

        _gl.BindVertexArray(0);
    }

    /// <summary>Uploads the font atlas pixels as an RGBA32 texture and records its OpenGL identifier.</summary>
    /// <param name="atlas">Font atlas whose texture data is uploaded.</param>
    public unsafe void CreateFontTexture(ImFontAtlas atlas)
    {
        atlas.GetTexDataAsRGBA32(out byte[] pixels, out int width, out int height);

        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        fixed (byte* ptr = pixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                ptr
            );
        }

        atlas.TexID = (IntPtr)_fontTexture;
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>Submits one frame of draw data as indexed OpenGL triangles with per-command scissor clipping.</summary>
    /// <param name="drawData">Aggregated frame draw data produced by the ImGui port.</param>
    public unsafe void Render(ImDrawData drawData)
    {
        if (drawData.CmdListsCount == 0 || drawData.DisplaySize.X <= 0f || drawData.DisplaySize.Y <= 0f)
            return;

        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0)
            return;

        // Save the host state that this renderer changes so it can be restored on return. Canonical saves
        // and restores the same set.
        bool hostBlend = _gl.IsEnabled(EnableCap.Blend);
        bool hostCullFace = _gl.IsEnabled(EnableCap.CullFace);
        bool hostDepthTest = _gl.IsEnabled(EnableCap.DepthTest);
        bool hostScissor = _gl.IsEnabled(EnableCap.ScissorTest);
        _gl.GetInteger(GetPName.BlendSrcRgb, out int hostSrcRgb);
        _gl.GetInteger(GetPName.BlendDstRgb, out int hostDstRgb);
        _gl.GetInteger(GetPName.BlendSrcAlpha, out int hostSrcAlpha);
        _gl.GetInteger(GetPName.BlendDstAlpha, out int hostDstAlpha);
        _gl.GetInteger(GetPName.BlendEquationRgb, out int hostEquation);
        _gl.GetInteger(GetPName.CurrentProgram, out int hostProgram);
        _gl.GetInteger(GetPName.VertexArrayBinding, out int hostVao);

        // Setup GL state for ImGui rendering
        _gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);
        // Canonical separate alpha blend.: colour uses source alpha, alpha accumulates.
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.ScissorTest);

        _gl.UseProgram(_program);

        // Orthographic projection matching screen pixels
        float left = drawData.DisplayPos.X;
        float right = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float top = drawData.DisplayPos.Y;
        float bottom = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, -1f, 1f);
        _gl.UniformMatrix4(_uProjLocation, 1, false, (float*)&proj);
        _gl.Uniform1(_uTextureLocation, 0);

        _gl.BindVertexArray(_vao);

        ImVec2 clipOff = drawData.DisplayPos;
        ImVec2 clipScale = drawData.FramebufferScale;

        for (int i = 0; i < drawData.CmdLists.Count; i++)
        {
            var cmdList = drawData.CmdLists[i];
            if (cmdList.VtxBuffer.Count == 0 || cmdList.IdxBuffer.Count == 0)
                continue;

            // Upload vertex buffer
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            var vtxSpan = CollectionsMarshal.AsSpan(cmdList.VtxBuffer);
            fixed (ImDrawVert* vtxPtr = vtxSpan)
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(cmdList.VtxBuffer.Count * sizeof(ImDrawVert)),
                    vtxPtr,
                    BufferUsageARB.StreamDraw
                );
            }

            // Upload index buffer
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            var idxSpan = CollectionsMarshal.AsSpan(cmdList.IdxBuffer);
            fixed (ushort* idxPtr = idxSpan)
            {
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    (nuint)(cmdList.IdxBuffer.Count * sizeof(ushort)),
                    idxPtr,
                    BufferUsageARB.StreamDraw
                );
            }

            // Render commands
            for (int j = 0; j < cmdList.CmdBuffer.Count; j++)
            {
                var cmd = cmdList.CmdBuffer[j];

                // A user callback replaces the triangle submission for its command. Canonical checks the
                // callback before the element count.
                if (cmd.UserCallback != null)
                {
                    cmd.UserCallback(cmdList, cmd);
                    continue;
                }

                if (cmd.ElemCount == 0)
                    continue;

                // Scissor test (OpenGL Y origin is bottom-left, scaled by FramebufferScale)
                float clipMinX = (cmd.ClipRect.X - clipOff.X) * clipScale.X;
                float clipMinY = (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                float clipMaxX = (cmd.ClipRect.Z - clipOff.X) * clipScale.X;
                float clipMaxY = (cmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                if (clipMinX < 0f) clipMinX = 0f;
                if (clipMinY < 0f) clipMinY = 0f;
                if (clipMaxX > fbWidth) clipMaxX = fbWidth;
                if (clipMaxY > fbHeight) clipMaxY = fbHeight;
                if (clipMaxX <= clipMinX || clipMaxY <= clipMinY)
                    continue;

                int scissorX = (int)clipMinX;
                int scissorY = (int)((float)fbHeight - clipMaxY);
                int scissorW = (int)(clipMaxX - clipMinX);
                int scissorH = (int)(clipMaxY - clipMinY);

                if (scissorW <= 0 || scissorH <= 0)
                    continue;

                _gl.Scissor(scissorX, scissorY, (uint)scissorW, (uint)scissorH);

                // Bind texture. The identifier is resolved through ImDrawCmd.GetTexID() because the
                // command owns only an ImTextureRef: an atlas-backed reference may be updated by the
                // texture pipeline, so reading the resolved identifier here is the definitive source.
                // Zero means the command carries no explicit texture, so the font texture is bound.
                uint textureId = cmd.GetTexID() != IntPtr.Zero ? (uint)cmd.GetTexID() : _fontTexture;
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, textureId);

                // Draw indexed triangles
                _gl.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)(cmd.IdxOffset * sizeof(ushort)),
                    (int)cmd.VtxOffset
                );
            }
        }

        // Restore the host state captured above.
        _gl.BindVertexArray((uint)hostVao);
        _gl.UseProgram((uint)hostProgram);
        _gl.BlendEquationSeparate((BlendEquationModeEXT)hostEquation, (BlendEquationModeEXT)hostEquation);
        _gl.BlendFuncSeparate((BlendingFactor)hostSrcRgb, (BlendingFactor)hostDstRgb, (BlendingFactor)hostSrcAlpha, (BlendingFactor)hostDstAlpha);
        SetCapability(EnableCap.Blend, hostBlend);
        SetCapability(EnableCap.CullFace, hostCullFace);
        SetCapability(EnableCap.DepthTest, hostDepthTest);
        SetCapability(EnableCap.ScissorTest, hostScissor);
    }

    /// <summary>
    /// Enables or disables one OpenGL capability to match a saved host value.
    /// </summary>
    /// <param name="cap">Capability to set.</param>
    /// <param name="enabled">Whether the capability should be enabled.</param>
    private void SetCapability(EnableCap cap, bool enabled)
    {
        if (enabled) _gl.Enable(cap);
        else _gl.Disable(cap);
    }

    /// <summary>Releases the OpenGL program, buffers, vertex array and font texture owned by this backend.</summary>
    public void Dispose()
    {
        if (_fontTexture != 0) _gl.DeleteTexture(_fontTexture);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_ibo != 0) _gl.DeleteBuffer(_ibo);
        if (_program != 0) _gl.DeleteProgram(_program);
    }

    #endregion
}
