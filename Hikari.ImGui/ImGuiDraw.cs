// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Hikari.ImGui;

#region Geometry & Command Structures (ImDrawVert, ImDrawCmd, ImDrawData)

/// <summary>
/// Vertex structure matching canonical Dear ImGui ImDrawVert layout (20 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ImDrawVert
{
    /// <summary>Vertex position in screen space.</summary>
    public ImVec2 Pos;
    /// <summary>Texture coordinates sampled for the vertex.</summary>
    public ImVec2 UV;
    /// <summary>Packed vertex color in canonical ABGR byte order.</summary>
    public uint Col;

    /// <summary>Creates a vertex from a position, texture coordinates and packed color.</summary>
    /// <param name="pos">Vertex position in screen space.</param>
    /// <param name="uv">Texture coordinates sampled for the vertex.</param>
    /// <param name="col">Packed vertex color in canonical ABGR byte order.</param>
    public ImDrawVert(ImVec2 pos, ImVec2 uv, uint col)
    {
        Pos = pos;
        UV = uv;
        Col = col;
    }
}

/// <summary>
/// Backend callback invoked in place of a triangle draw for one command. Mirrors the canonical
/// <c>ImDrawCallback</c> signature.: the parent list and the
/// command carrying the callback are supplied so the backend can alter render state or emit custom
/// work before resuming normal geometry submission.
/// </summary>
/// <param name="parentList">Draw list that owns the command carrying the callback.</param>
/// <param name="cmd">Command describing the callback, including user data and clip rectangle.</param>
public delegate void ImDrawCallback(ImDrawList parentList, ImDrawCmd cmd);

/// <summary>
/// Draw command representing a single rendering call from an ImDrawList, matching canonical Dear ImGui ImDrawCmd.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImDrawCmd
{
    /// <summary>Scissor rectangle as (min.X, min.Y, max.X, max.Y).</summary>
    public ImVec4 ClipRect;
    /// <summary>Texture reference bound for this command. Resolve the backend identifier with <see cref="GetTexID"/>.</summary>
    public ImTextureRef TexRef;
    /// <summary>Offset into the shared vertex buffer where this command starts.</summary>
    public uint VtxOffset;
    /// <summary>Offset into the shared index buffer where this command starts.</summary>
    public uint IdxOffset;
    /// <summary>Number of indices to draw.</summary>
    public uint ElemCount;
    /// <summary>Backend callback invoked instead of drawing triangles, or null for a normal command.</summary>
    public ImDrawCallback? UserCallback;
    /// <summary>Opaque pointer passed to <see cref="UserCallback"/>, or zero when no callback data is present.</summary>
    public IntPtr UserCallbackData;
    /// <summary>Size in bytes of the copied callback data, or zero when the callback data is a direct pointer.</summary>
    public int UserCallbackDataSize;
    /// <summary>Offset of the copied callback data within the draw list buffer, or -1 when data is stored directly.</summary>
    public int UserCallbackDataOffset;

    /// <summary>Creates a draw command from a clip rectangle, texture reference and buffer offsets.</summary>
    /// <param name="clipRect">Scissor rectangle as (min.X, min.Y, max.X, max.Y).</param>
    /// <param name="texRef">Texture reference bound for this command.</param>
    /// <param name="vtxOffset">Offset into the shared vertex buffer.</param>
    /// <param name="idxOffset">Offset into the shared index buffer.</param>
    /// <param name="elemCount">Number of indices to draw.</param>
    public ImDrawCmd(ImVec4 clipRect, ImTextureRef texRef, uint vtxOffset, uint idxOffset, uint elemCount)
    {
        ClipRect = clipRect;
        TexRef = texRef;
        VtxOffset = vtxOffset;
        IdxOffset = idxOffset;
        ElemCount = elemCount;
        UserCallback = null;
        UserCallbackData = IntPtr.Zero;
        UserCallbackDataSize = 0;
        UserCallbackDataOffset = -1;
    }

    /// <summary>
    /// Resolves the backend texture identifier associated with this command. Mirrors canonical
    /// <c>ImDrawCmd::GetTexID</c>.; the identifier is read
    /// through the texture reference so atlas-owned texture data stays definitive.
    /// </summary>
    /// <returns>The backend texture identifier to bind while rendering this command.</returns>
    public readonly IntPtr GetTexID()
    {
        return TexRef.GetTexID();
    }
}

/// <summary>
/// All draw data of a frame aggregated for the rendering backend, matching canonical Dear ImGui ImDrawData.
/// </summary>
public class ImDrawData
{
    /// <summary>Whether the aggregate describes a valid frame.</summary>
    public bool Valid { get; set; } = true;
    /// <summary>Frame counter of the emitter, retained for debugging.</summary>
    public int FrameCount { get; set; }
    /// <summary>Number of command lists in the aggregate.</summary>
    public int CmdListsCount => CmdLists.Count;
    /// <summary>Total indices across all command lists.</summary>
    public int TotalIdxCount { get; set; }
    /// <summary>Total vertices across all command lists.</summary>
    public int TotalVtxCount { get; set; }
    /// <summary>Command lists borrowed from the frame.</summary>
    /// <returns>The list of command lists owned by the frame.</returns>
    public List<ImDrawList> CmdLists { get; } = new();
    /// <summary>Top-left position of the viewport in display coordinates.</summary>
    public ImVec2 DisplayPos { get; set; } = ImVec2.Zero;
    /// <summary>Size of the viewport in display coordinates.</summary>
    public ImVec2 DisplaySize { get; set; } = ImVec2.Zero;
    /// <summary>Framebuffer pixels per display unit.</summary>
    public ImVec2 FramebufferScale { get; set; } = ImVec2.One;
    /// <summary>Textures to upload before rendering, or null when the backend manages uploads itself.</summary>
    public List<ImTextureData>? Textures { get; set; }

    /// <summary>Resets the aggregate to an empty, invalid frame.</summary>
    public void Clear()
    {
        Valid = false;
        FrameCount = 0;
        CmdLists.Clear();
        TotalIdxCount = 0;
        TotalVtxCount = 0;
        DisplayPos = ImVec2.Zero;
        DisplaySize = ImVec2.Zero;
        FramebufferScale = ImVec2.One;
        Textures = null;
    }

    /// <summary>
    /// Adds a command list and accumulates its buffer counts. Ports canonical
    /// <c>ImGui::AddDrawListToDrawDataEx</c> (,
    /// declared.): a list with no command buffer, or with a single
    /// empty non-callback command, is skipped so it contributes no draw call and no vertex or index
    /// totals to the aggregate.
    /// </summary>
    /// <param name="drawList">Command list to add.</param>
    public void AddDrawList(ImDrawList drawList)
    {
        drawList.PopUnusedDrawCmd();
        if (drawList.CmdBuffer.Count == 0)
            return;
        if (drawList.CmdBuffer.Count == 1 && drawList.CmdBuffer[0].ElemCount == 0 && drawList.CmdBuffer[0].UserCallback == null)
            return;
        CmdLists.Add(drawList);
        TotalIdxCount += drawList.IdxBuffer.Count;
        TotalVtxCount += drawList.VtxBuffer.Count;
    }

    /// <summary>
    /// Converts every command list from indexed to non-indexed geometry for backends that cannot
    /// render indexed triangles. Ports canonical <c>ImDrawData::DeIndexAllBuffers</c>
    ///. The operation expands vertices, so it is
    /// a deliberate slow path and indexed rendering remains preferred.
    /// </summary>
    public void DeIndexAllBuffers()
    {
        var newVtxBuffer = new List<ImDrawVert>();
        TotalVtxCount = 0;
        TotalIdxCount = 0;
        foreach (ImDrawList drawList in CmdLists)
        {
            if (drawList.IdxBuffer.Count == 0)
                continue;
            newVtxBuffer.Clear();
            for (int j = 0; j < drawList.IdxBuffer.Count; j++)
                newVtxBuffer.Add(drawList.VtxBuffer[drawList.IdxBuffer[j]]);
            drawList.VtxBuffer.Clear();
            drawList.VtxBuffer.AddRange(newVtxBuffer);
            drawList.IdxBuffer.Clear();
            TotalVtxCount += drawList.VtxBuffer.Count;
        }
    }

    /// <summary>Scales every command clip rectangle by a framebuffer scale.</summary>
    /// <param name="fbScale">Framebuffer pixels per display unit.</param>
    public void ScaleClipRects(ImVec2 fbScale)
    {
        for (int i = 0; i < CmdLists.Count; i++)
        {
            var list = CmdLists[i];
            for (int j = 0; j < list.CmdBuffer.Count; j++)
            {
                var cmd = list.CmdBuffer[j];
                cmd.ClipRect = new ImVec4(
                    cmd.ClipRect.X * fbScale.X,
                    cmd.ClipRect.Y * fbScale.Y,
                    cmd.ClipRect.Z * fbScale.X,
                    cmd.ClipRect.W * fbScale.Y
                );
                list.CmdBuffer[j] = cmd;
            }
        }
    }
}

#endregion

#region 2D Draw Pipeline (ImDrawList)

/// <summary>
/// Draw-command list generating 2D geometry (vertices, indices, clipping rects),
/// matching canonical Dear ImGui ImDrawList.
/// </summary>
public class ImDrawList
{
    #region Constants & Configuration

    /// <summary>Number of precomputed arc samples covering the full circle.</summary>
    private const int ArcFastTableSize = 48;
    /// <summary>Sample index that represents a full 360 degree turn.</summary>
    private const int ArcFastSampleMax = ArcFastTableSize;
    /// <summary>Lower clamp for the automatic circle segment count.</summary>
    private const int CircleAutoSegmentMin = 4;
    /// <summary>Upper clamp for the automatic circle segment count.</summary>
    private const int CircleAutoSegmentMax = 512;
    /// <summary>Number of radius buckets in the precomputed circle segment table.</summary>
    private const int CircleSegmentCountsCapacity = 64;
    /// <summary>Default circle tessellation error in pixels.</summary>
    private const float DefaultCircleTessellationMaxError = 0.30f;
    /// <summary>Default curve tessellation tolerance.</summary>
    private const float DefaultCurveTessellationTol = 1.25f;
    /// <summary>Upper bound on the join-normal inverse length squared.</summary>
    private const float FixNormalMaxInvLen2 = 100.0f;
    /// <summary>Full-screen clip rectangle used before any clip rectangle is pushed.</summary>
    private const float ClipRectFullscreenExtent = 8192.0f;

    /// <summary>Precomputed unit-circle sample points covering the full circle.</summary>
    private static readonly ImVec2[] _arcFastVtx = BuildArcFastVtx();

    #endregion

    #region Buffers & State Fields

    /// <summary>
    /// Gets or sets the optional owner name recorded when the draw list was created, used only by the
    /// debug tools to identify the list in the metrics window.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>Command buffer for the frame. The setter exists so a splitter can swap channel storage without copying.</summary>
    /// <returns>The command list accumulated so far.</returns>
    public List<ImDrawCmd> CmdBuffer { get; internal set; } = new();
    /// <summary>Index buffer for the frame. The setter exists so a splitter can swap channel storage without copying.</summary>
    /// <returns>The index list accumulated so far.</returns>
    public List<ushort> IdxBuffer { get; internal set; } = new();
    /// <summary>Vertex buffer for the frame.</summary>
    /// <returns>The vertex list accumulated so far.</returns>
    public List<ImDrawVert> VtxBuffer { get; } = new();
    /// <summary>Draw-list behavior flags.</summary>
    public ImDrawFlags Flags { get; set; } = ImDrawFlags.AALines | ImDrawFlags.AAFill;
    /// <summary>Draw flags stack.</summary>
    public readonly List<ImDrawFlags> _DrawFlagsStack = new(4);
    /// <summary>Anti-aliasing fringe scale factor.</summary>
    public float _FringeScale = 1.0f;
    /// <summary>Inverse fringe scale factor (pixel density).</summary>
    public float _InvFringeScale = 1.0f;
    /// <summary>True if 1/_FringeScale is an integer.</summary>
    public bool _FringeScaleIsInteger = true;
    /// <summary>Shared draw data pointer.</summary>
    public ImDrawListSharedData? _Data { get; set; }
    /// <summary>Index of the next vertex relative to the active vertex-buffer base.</summary>
    public uint _VtxCurrentIdx => (uint)_vtxCurrentIdx;
    /// <summary>
    /// Channel splitter owned by this draw list, matching canonical <c>ImDrawList::_Splitter</c>
    ///. Use <see cref="ChannelsSplit"/>,
    /// <see cref="ChannelsSetCurrent"/> and <see cref="ChannelsMerge"/> to render column or table
    /// items in a batched order; <see cref="Clear()"/> merges an active splitter and resets it.
    /// </summary>
    public ImDrawListSplitter Splitter { get; } = new();

    /// <summary>Active clip rectangle stack, innermost last.</summary>
    public readonly List<ImVec4> _ClipRectStack = new();
    private List<ImVec4> _clipRectStack => _ClipRectStack;
    /// <summary>Active texture reference stack, innermost last.</summary>
    public readonly List<ImTextureRef> _TextureStack = new();
    private List<ImTextureRef> _textureStack => _TextureStack;
    /// <summary>Current stateful path under construction.</summary>
    public readonly List<ImVec2> _Path = new();
    private List<ImVec2> _path => _Path;
    /// <summary>Precomputed circle segment counts indexed by rounded-up radius.</summary>
    private readonly byte[] _circleSegmentCounts = new byte[CircleSegmentCountsCapacity];
    /// <summary>
    /// Baked anti-aliased line UV rectangles borrowed from the bound font atlas, indexed by integer
    /// line width. Empty when the atlas did not bake a line texture, in which case
    /// <see cref="AddPolyline"/> falls back to polygon stroking
    ///.
    /// </summary>
    private ImVec4[] _texUvLines = Array.Empty<ImVec4>();
    /// <summary>Pinned buffers backing callback user data copied through <see cref="AddCallback"/>.</summary>
    private readonly List<GCHandle> _callbackDataHandles = new();
    /// <summary>Reusable join-normal scratch for <see cref="AddPolyline"/> and filled polygon fringes.</summary>
    private ImVec2[] _tempNormals = Array.Empty<ImVec2>();
    /// <summary>Reusable squared length scratch for <see cref="AddPolyline"/>.</summary>
    private float[] _tempSqrLengths = Array.Empty<float>();
    /// <summary>Reusable expanded point scratch for anti-aliased strokes and fills.</summary>
    private ImVec2[] _tempPoints = Array.Empty<ImVec2>();
    /// <summary>Reusable ear-clipping triangulator for <see cref="AddConcavePolyFilled"/>.</summary>
    private readonly ConcaveTriangulator _triangulator = new();

    /// <summary>Clip rectangle shared by the active draw command header. Internal so the splitter can copy it into a channel's trailing command.</summary>
    internal ImVec4 _cmdHeaderClipRect;
    /// <summary>Texture reference shared by the active draw command header. Internal so the splitter can copy it into a channel's trailing command.</summary>
    internal ImTextureRef _cmdHeaderTexRef;
    /// <summary>Vertex-buffer base of the active draw command, used when splitting meshes above 64K vertices. Internal so the splitter can copy it into a channel's trailing command.</summary>
    internal uint _cmdHeaderVtxOffset;
    /// <summary>Index of the next vertex relative to the active vertex-buffer base; resets at each split.</summary>
    private int _vtxCurrentIdx;
    /// <summary>Width of the anti-aliasing fringe in pixels; mirrors _FringeScale.</summary>
    private float _fringeScale => _FringeScale;
    /// <summary>UV coordinates of the atlas white texel, sampled by untextured geometry.</summary>
    private ImVec2 _texUvWhitePixel;
    /// <summary>Font atlas supplying the white texel, default font and backend texture identifier.</summary>
    private ImFontAtlas? _sharedAtlas;
    /// <summary>Radius below which arcs use the precomputed fast table.</summary>
    private float _arcFastRadiusCutoff;
    /// <summary>Maximum circle tessellation error in pixels.</summary>
    private float _circleTessellationMaxError;
    /// <summary>Tessellation tolerance for auto-tessellated Bezier paths.</summary>
    private readonly float _curveTessellationTol = DefaultCurveTessellationTol;

    #endregion

    #region Lifecycle

    /// <summary>Creates a draw list bound to an optional shared font atlas.</summary>
    /// <param name="sharedAtlas">Atlas supplying the white texel, line UVs and default font, or null.</param>
    public ImDrawList(ImFontAtlas? sharedAtlas = null)
    {
        _sharedAtlas = sharedAtlas;
        sharedAtlas?.RegisterDrawList(this);
        RefreshAtlasTables(sharedAtlas);
        SetCircleTessellationMaxError(DefaultCircleTessellationMaxError);
        // No frame has started, so no shared initial flags exist yet; the first frame reset applies them.
        Clear();
    }

    /// <summary>Rebinds the draw list to a new font atlas and white texel.</summary>
    /// <param name="atlas">Atlas supplying the white texel, line UVs and default font.</param>
    public void UpdateAtlas(ImFontAtlas atlas)
    {
        _sharedAtlas = atlas;
        atlas.RegisterDrawList(this);
        RefreshAtlasTables(atlas);
        if (_textureStack.Count == 0)
        {
            _SetTexture(new ImTextureRef(atlas.TexID));
        }
    }

    /// <summary>
    /// Copies the atlas-owned UV tables into the draw list and latches the textured-line flag to
    /// match. Mirrors canonical shared-data wiring
    ///. and the <c>NewFrame</c> flag selection
    ///.: the textured path is used only when the atlas
    /// actually baked lines. The owning atlas calls this whenever it repacks or refreshes its
    /// regions, so a mid-frame lazy bake cannot leave stale UVs here.
    /// </summary>
    /// <param name="atlas">Atlas to read from, or null to clear both tables.</param>
    internal void RefreshAtlasTables(ImFontAtlas? atlas)
    {
        _texUvWhitePixel = atlas?.TexUvWhitePixel ?? ImVec2.Zero;
        _texUvLines = atlas?.TexUvLines ?? Array.Empty<ImVec4>();
        if (_texUvLines.Length > 0)
            Flags |= ImDrawFlags.AALines;
        else
            Flags &= ~ImDrawFlags.AALines;
    }

    /// <summary>
    /// Resets all buffers, stacks and clipping state for a new frame. Ports canonical
    /// <c>ImDrawList::_ResetForNewFrame</c>.: an active
    /// splitter is merged back into the draw list, the vertex, index and command buffers are emptied,
    /// the shared initial flags and fringe scale are restored, all clip, texture, callback and path
    /// stacks are cleared, the splitter is reset, and exactly one empty command is pushed. Unlike the
    /// obsolete full-screen pre-clip this method does not push a clip rectangle; the caller is
    /// expected to push the frame clip rectangle itself, exactly as canonical <c>ImGui::NewFrame</c>
    /// does after resetting its draw lists.
    /// </summary>
    /// <summary>
    /// Resets the draw list to an empty frame with the standalone default flags: anti-aliased lines and fill.
    /// Frame code uses <see cref="Clear(ImDrawFlags)"/> so that the shared style flags apply.
    /// </summary>
    public void Clear() => Clear(ImDrawFlags.AALines | ImDrawFlags.AAFill);

    /// <summary>Canonical alias for <see cref="Clear()"/>.</summary>
    public void _ResetForNewFrame() => Clear();

    /// <summary>
    /// Resets the draw list to an empty frame, applying the frame's shared initial flags.
    /// </summary>
    /// <param name="initialFlags">The frame's shared initial flags.</param>
    public void Clear(ImDrawFlags initialFlags)
    {
        if (Splitter.ChannelCount > 1)
            Splitter.Merge(this);

        FreeCallbackData();
        CmdBuffer.Clear();
        IdxBuffer.Clear();
        VtxBuffer.Clear();
        Flags = initialFlags;
        if (_texUvLines.Length == 0)
            Flags &= ~ImDrawFlags.AALines;
        _DrawFlagsStack.Clear();
        _cmdHeaderClipRect = default;
        _cmdHeaderTexRef = default;
        _cmdHeaderVtxOffset = 0;
        _vtxCurrentIdx = 0;
        _clipRectStack.Clear();
        _textureStack.Clear();
        _path.Clear();
        Splitter.Clear();
        CmdBuffer.Add(new ImDrawCmd(_cmdHeaderClipRect, _cmdHeaderTexRef, 0, 0, 0));
    }

    /// <summary>
    /// Obsolete overload accepting <see cref="ImDrawListFlags"/>.
    /// </summary>
    [Obsolete("Use Clear(ImDrawFlags) instead.")]
    public void Clear(ImDrawListFlags initialFlags) => Clear((ImDrawFlags)initialFlags);

    /// <summary>
    /// Releases all buffers and stacks for a draw list that is no longer used this frame, without
    /// leaving the list ready for new commands. Ports canonical <c>ImDrawList::_ClearFreeMemory</c>
    ///.: every buffer and stack is emptied, the flags
    /// are cleared, and the splitter's channel storage is released.
    /// </summary>
    public void ClearFreeMemory()
    {
        FreeCallbackData();
        CmdBuffer.Clear();
        IdxBuffer.Clear();
        VtxBuffer.Clear();
        Flags = ImDrawFlags.None;
        _DrawFlagsStack.Clear();
        _cmdHeaderClipRect = default;
        _cmdHeaderTexRef = default;
        _cmdHeaderVtxOffset = 0;
        _vtxCurrentIdx = 0;
        _clipRectStack.Clear();
        _textureStack.Clear();
        _path.Clear();
        Splitter.ClearFreeMemory();
    }

    /// <summary>Canonical alias for <see cref="ClearFreeMemory"/>.</summary>
    public void _ClearFreeMemory() => ClearFreeMemory();

    /// <summary>
    /// Pushes draw flags, scoping modifiers to subsequent drawing primitives.
    /// (, )
    /// </summary>
    /// <param name="flags">Flags to enable or disable.</param>
    /// <param name="enabled">True to set flags, false to clear them.</param>
    public void PushDrawFlag(ImDrawFlags flags, bool enabled = true)
    {
        ImAssert.Check((flags & ~ImDrawFlags.AllowInPushScope_) == 0);
        ImAssert.Check((flags & ImDrawFlags.StrokeMask_) == 0 || (flags & ImDrawFlags.StrokeMask_) == ImDrawFlags.StrokeLegacy);
        ImAssert.Check(_DrawFlagsStack.Count < 3, "PushDrawFlag() has a depth limit.");
        _DrawFlagsStack.Add(Flags);
        if (enabled)
            Flags |= flags;
        else
            Flags &= ~flags;
    }

    /// <summary>
    /// Restores draw flags popped from the stack.
    /// (, )
    /// </summary>
    public void PopDrawFlag()
    {
        ImAssert.Check(_DrawFlagsStack.Count > 0, "Calling PopDrawFlag() too many times!");
        Flags = _DrawFlagsStack[^1];
        _DrawFlagsStack.RemoveAt(_DrawFlagsStack.Count - 1);
    }

    /// <summary>
    /// Sets the pixel density / fringe scale for anti-aliasing.
    /// </summary>
    /// <param name="pixelDensity">Pixel density multiplier.</param>
    public void _SetPixelDensity(float pixelDensity)
    {
        ImAssert.Check(pixelDensity > 0.0f);
        _FringeScale = 1.0f / pixelDensity;
        _InvFringeScale = pixelDensity;
        _FringeScaleIsInteger = MathF.Truncate(_InvFringeScale) == _InvFringeScale;
    }

    /// <summary>
    /// Calculates the center-biased offset for <see cref="ImDrawFlags.StrokeCenterBiased"/>.
    /// </summary>
    public float _CalculateCenterBiasedOffset(float thickness)
    {
        float screenThickness = thickness * _InvFringeScale;
        int sThickness = (int)screenThickness;
        return ((float)(sThickness / 2) + (sThickness & 1) * (screenThickness - sThickness)) * _FringeScale;
    }

    /// <summary>
    /// Resolves the stroke position from flags, falling back to the default position.
    /// </summary>
    private static ImDrawFlags _GetStrokePos(ImDrawFlags flags, ImDrawFlags defaultStrokePos)
        => (flags & ImDrawFlags.StrokeMask_) != 0 ? (flags & ImDrawFlags.StrokeMask_) : defaultStrokePos;

    /// <summary>
    /// Starts channel splitting so later geometry can be recorded into separate ordered layers.
    /// Canonical inline <c>ImDrawList::ChannelsSplit</c>.;
    /// nested splitting is not supported and requires a separate <see cref="ImDrawListSplitter"/>.
    /// </summary>
    /// <param name="count">Number of channels to activate, at least one.</param>
    public void ChannelsSplit(int count) => Splitter.Split(this, count);

    /// <summary>
    /// Merges all active channels back into a single command list in channel order. Canonical inline
    /// <c>ImDrawList::ChannelsMerge</c>.
    /// </summary>
    public void ChannelsMerge() => Splitter.Merge(this);

    /// <summary>
    /// Selects the channel that receives subsequent geometry. Canonical inline
    /// <c>ImDrawList::ChannelsSetCurrent</c>.
    /// </summary>
    /// <param name="channelIndex">Zero-based index of the channel to make current.</param>
    public void ChannelsSetCurrent(int channelIndex) => Splitter.SetCurrentChannel(this, channelIndex);

    /// <summary>Releases pinned callback data buffers retained for the frame.</summary>
    private void FreeCallbackData()
    {
        for (int i = 0; i < _callbackDataHandles.Count; i++)
            _callbackDataHandles[i].Free();
        _callbackDataHandles.Clear();
    }

    #endregion

    #region Clip & Texture Stacks

    /// <summary>Pushes a clip rectangle, optionally intersected with the current one.</summary>
    /// <param name="clipRectMin">Top-left corner of the new clip rectangle.</param>
    /// <param name="clipRectMax">Bottom-right corner of the new clip rectangle.</param>
    /// <param name="intersectWithCurrent">When true, intersects with the current clip rectangle.</param>
    public void PushClipRect(ImVec2 clipRectMin, ImVec2 clipRectMax, bool intersectWithCurrent = false)
    {
        // Ports ImDrawList::PushClipRect. Inverted ranges are normalized so
        // the command header never carries a negative-width scissor rectangle.
        ImVec4 cr = new(clipRectMin.X, clipRectMin.Y, clipRectMax.X, clipRectMax.Y);
        if (intersectWithCurrent)
        {
            ImVec4 current = _cmdHeaderClipRect;
            if (cr.X < current.X) cr.X = current.X;
            if (cr.Y < current.Y) cr.Y = current.Y;
            if (cr.Z > current.Z) cr.Z = current.Z;
            if (cr.W > current.W) cr.W = current.W;
        }
        cr.Z = MathF.Max(cr.X, cr.Z);
        cr.W = MathF.Max(cr.Y, cr.W);

        _clipRectStack.Add(cr);
        _cmdHeaderClipRect = cr;
        _OnChangedClipRect();
    }

    /// <summary>Pushes a clip rectangle covering the full drawable area.</summary>
    public void PushClipRectFullScreen()
    {
        PushClipRect(new ImVec2(-ClipRectFullscreenExtent, -ClipRectFullscreenExtent), new ImVec2(ClipRectFullscreenExtent, ClipRectFullscreenExtent), false);
    }

    /// <summary>Pops the current clip rectangle and restores the previous one.</summary>
    public void PopClipRect()
    {
        if (_clipRectStack.Count == 0)
            return;
        _clipRectStack.RemoveAt(_clipRectStack.Count - 1);
        _cmdHeaderClipRect = _clipRectStack.Count == 0
            ? new ImVec4(-ClipRectFullscreenExtent, -ClipRectFullscreenExtent, ClipRectFullscreenExtent, ClipRectFullscreenExtent)
            : _clipRectStack[^1];
        _OnChangedClipRect();
    }

    /// <summary>Returns the minimum corner of the current clip rectangle.</summary>
    /// <returns>The current clip rectangle minimum.</returns>
    public ImVec2 GetClipRectMin()
    {
        ImVec4 cr = _clipRectStack.Count > 0 ? _clipRectStack[^1] : _cmdHeaderClipRect;
        return new ImVec2(cr.X, cr.Y);
    }
    /// <summary>Returns the maximum corner of the current clip rectangle.</summary>
    /// <returns>The current clip rectangle maximum.</returns>
    public ImVec2 GetClipRectMax()
    {
        ImVec4 cr = _clipRectStack.Count > 0 ? _clipRectStack[^1] : _cmdHeaderClipRect;
        return new ImVec2(cr.Z, cr.W);
    }

    /// <summary>Pushes a texture reference onto the draw-list texture stack.</summary>
    /// <param name="texRef">Texture reference to bind for subsequent geometry.</param>
    public void PushTexture(ImTextureRef texRef)
    {
        _textureStack.Add(texRef);
        _cmdHeaderTexRef = texRef;
        _OnChangedTexture();
    }

    /// <summary>Pops the current texture reference and restores the previous one.</summary>
    public void PopTexture()
    {
        if (_textureStack.Count == 0)
            return;
        _textureStack.RemoveAt(_textureStack.Count - 1);
        _cmdHeaderTexRef = _textureStack.Count == 0 ? default : _textureStack[^1];
        _OnChangedTexture();
    }

    /// <summary>Pushes a texture reference; obsolete canonical alias for <see cref="PushTexture"/>.</summary>
    /// <param name="texRef">Texture reference to bind for subsequent geometry.</param>
    public void PushTextureID(ImTextureRef texRef) => PushTexture(texRef);

    /// <summary>Pops the current texture reference; obsolete canonical alias for <see cref="PopTexture"/>.</summary>
    public void PopTextureID()
    {
        PopTexture();
    }

    /// <summary>
    /// Replaces the innermost texture reference without changing stack depth, used when the atlas
    /// texture identifier changes. Ports canonical <c>ImDrawList::_SetTexture</c>
    /// </summary>
    /// <param name="texRef">New texture reference for the innermost stack slot.</param>
    private void _SetTexture(ImTextureRef texRef)
    {
        if (TexRefEquals(_cmdHeaderTexRef, texRef))
            return;
        _cmdHeaderTexRef = texRef;
        if (_textureStack.Count > 0)
            _textureStack[^1] = texRef;
        _OnChangedTexture();
    }

    /// <summary>
    /// Appends a fresh command carrying the active clip rectangle, texture reference and vertex-buffer
    /// base. Ports canonical <c>ImDrawList::AddDrawCmd</c>.
    /// </summary>
    public void AddDrawCmd()
    {
        CmdBuffer.Add(new ImDrawCmd(_cmdHeaderClipRect, _cmdHeaderTexRef, _cmdHeaderVtxOffset, (uint)IdxBuffer.Count, 0));
    }

    /// <summary>
    /// Removes trailing commands that carry no geometry and no callback. Ports canonical
    /// <c>ImDrawList::_PopUnusedDrawCmd</c>.
    /// </summary>
    public void PopUnusedDrawCmd()
    {
        while (CmdBuffer.Count > 0)
        {
            ImDrawCmd curr = CmdBuffer[^1];
            if (curr.ElemCount != 0 || curr.UserCallback != null)
                return;
            CmdBuffer.RemoveAt(CmdBuffer.Count - 1);
        }
    }

    /// <summary>Canonical alias for <see cref="PopUnusedDrawCmd"/>.</summary>
    public void _PopUnusedDrawCmd() => PopUnusedDrawCmd();

    /// <summary>Reacts to a clip-rectangle change by splitting or merging the trailing command.</summary>
    public void _OnChangedClipRect()
    {
        // Ports ImDrawList::_OnChangedClipRect. Commands are only merged when
        // clip rectangle, texture reference, vertex-buffer base and index offsets are contiguous.
        if (CmdBuffer.Count == 0)
        {
            AddDrawCmd();
            return;
        }
        int index = CmdBuffer.Count - 1;
        ImDrawCmd curr = CmdBuffer[index];
        if (curr.ElemCount != 0 && curr.ClipRect != _cmdHeaderClipRect)
        {
            AddDrawCmd();
            return;
        }
        if (curr.ElemCount == 0 && CmdBuffer.Count > 1)
        {
            ImDrawCmd prev = CmdBuffer[index - 1];
            if (HeaderEquals(prev, _cmdHeaderClipRect, _cmdHeaderTexRef, _cmdHeaderVtxOffset) && AreSequentialIdxOffset(prev, curr) && prev.UserCallback == null)
            {
                CmdBuffer.RemoveAt(index);
                return;
            }
        }
        curr.ClipRect = _cmdHeaderClipRect;
        CmdBuffer[index] = curr;
    }

    /// <summary>Reacts to a texture-reference change by splitting or merging the trailing command.</summary>
    public void _OnChangedTexture()
    {
        // Ports ImDrawList::_OnChangedTexture.
        if (CmdBuffer.Count == 0)
        {
            AddDrawCmd();
            return;
        }
        int index = CmdBuffer.Count - 1;
        ImDrawCmd curr = CmdBuffer[index];
        if (curr.ElemCount != 0 && !TexRefEquals(curr.TexRef, _cmdHeaderTexRef))
        {
            AddDrawCmd();
            return;
        }
        if (curr.UserCallback != null)
            return;
        if (curr.ElemCount == 0 && CmdBuffer.Count > 1)
        {
            ImDrawCmd prev = CmdBuffer[index - 1];
            if (HeaderEquals(prev, _cmdHeaderClipRect, _cmdHeaderTexRef, _cmdHeaderVtxOffset) && AreSequentialIdxOffset(prev, curr) && prev.UserCallback == null)
            {
                CmdBuffer.RemoveAt(index);
                return;
            }
        }
        curr.TexRef = _cmdHeaderTexRef;
        CmdBuffer[index] = curr;
    }

    /// <summary>
    /// Reacts to a 64K vertex-buffer split by resetting the working vertex index and updating the
    /// trailing command. Ports canonical <c>ImDrawList::_OnChangedVtxOffset</c>
    /// </summary>
    public void _OnChangedVtxOffset()
    {
        _vtxCurrentIdx = 0;
        if (CmdBuffer.Count == 0)
        {
            AddDrawCmd();
            return;
        }
        int index = CmdBuffer.Count - 1;
        ImDrawCmd curr = CmdBuffer[index];
        if (curr.ElemCount != 0)
        {
            AddDrawCmd();
            return;
        }
        curr.VtxOffset = _cmdHeaderVtxOffset;
        CmdBuffer[index] = curr;
    }

    /// <summary>
    /// Determines whether a command header equals the supplied clip rectangle, texture reference and
    /// vertex-buffer base. Mirrors canonical <c>ImDrawCmd_HeaderCompare</c>.
    /// </summary>
    /// <param name="cmd">Command whose header fields are compared.</param>
    /// <param name="clipRect">Clip rectangle to compare.</param>
    /// <param name="texRef">Texture reference to compare.</param>
    /// <param name="vtxOffset">Vertex-buffer base to compare.</param>
    /// <returns>True when the header fields are equivalent.</returns>
    internal static bool HeaderEquals(ImDrawCmd cmd, ImVec4 clipRect, ImTextureRef texRef, uint vtxOffset)
    {
        return cmd.ClipRect == clipRect && TexRefEquals(cmd.TexRef, texRef) && cmd.VtxOffset == vtxOffset;
    }

    /// <summary>
    /// Determines whether a command continues the index range of the one before it. Mirrors canonical
    /// <c>ImDrawCmd_AreSequentialIdxOffset</c>.
    /// </summary>
    /// <param name="first">Earlier command.</param>
    /// <param name="second">Later command.</param>
    /// <returns>True when the later command starts where the earlier one ends.</returns>
    private static bool AreSequentialIdxOffset(ImDrawCmd first, ImDrawCmd second)
    {
        return first.IdxOffset + first.ElemCount == second.IdxOffset;
    }

    /// <summary>Compares two texture references by backend identifier and texture data identity.</summary>
    /// <param name="a">Left texture reference.</param>
    /// <param name="b">Right texture reference.</param>
    /// <returns>True when both references resolve to the same texture.</returns>
    private static bool TexRefEquals(ImTextureRef a, ImTextureRef b)
    {
        return a.TexID == b.TexID && ReferenceEquals(a.TexData, b.TexData);
    }

    #endregion

    #region Low-Level Primitives

    /// <summary>
    /// Reserves space for indices and vertices in the current command, splitting the vertex buffer
    /// when 16-bit indices would overflow. Ports canonical <c>ImDrawList::PrimReserve</c>
    /// </summary>
    /// <param name="idxCount">Number of indices to reserve.</param>
    /// <param name="vtxCount">Number of vertices to reserve.</param>
    public void PrimReserve(int idxCount, int vtxCount)
    {
        // 16-bit indirection: when the running vertex index would cross 64K and the backend opted in,
        // restart relative indexing and start a new command at the current vertex-buffer end.
        if ((_vtxCurrentIdx + vtxCount >= (1 << 16)) && (Flags & ImDrawFlags.UseVtxOffset) != 0)
        {
            _cmdHeaderVtxOffset = (uint)VtxBuffer.Count;
            _OnChangedVtxOffset();
        }

        if (CmdBuffer.Count == 0)
            AddDrawCmd();

        int index = CmdBuffer.Count - 1;
        ImDrawCmd cmd = CmdBuffer[index];
        cmd.ElemCount += (uint)idxCount;
        CmdBuffer[index] = cmd;
    }

    /// <summary>
    /// Releases indices and vertices reserved by the most recent <see cref="PrimReserve"/> call from
    /// the tail of the command and index/vertex buffers. Ports canonical
    /// <c>ImDrawList::PrimUnreserve</c>. The caller
    /// must unreserve at most what it reserved and must not have emitted other geometry since.
    /// </summary>
    /// <param name="idxCount">Number of indices to release.</param>
    /// <param name="vtxCount">Number of vertices to release.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either count is negative.</exception>
    public void PrimUnreserve(int idxCount, int vtxCount)
    {
        if (idxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(idxCount), "Index count must not be negative.");
        if (vtxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(vtxCount), "Vertex count must not be negative.");
        if (CmdBuffer.Count == 0)
            return;

        int index = CmdBuffer.Count - 1;
        ImDrawCmd cmd = CmdBuffer[index];
        cmd.ElemCount -= (uint)idxCount;
        CmdBuffer[index] = cmd;
        if (vtxCount > 0)
            VtxBuffer.RemoveRange(VtxBuffer.Count - vtxCount, vtxCount);
        if (idxCount > 0)
            IdxBuffer.RemoveRange(IdxBuffer.Count - idxCount, idxCount);
    }

    /// <summary>
    /// Creates an independent copy of this draw list's command, index and vertex buffers for use by
    /// another rendering thread. Ports canonical <c>ImDrawList::CloneOutput</c>
    ///.; the clone is bound to no atlas and keeps only
    /// the buffers and flags, so later edits to either list do not affect the other.
    /// </summary>
    /// <returns>A new draw list owning copies of the command, index and vertex buffers.</returns>
    public ImDrawList CloneOutput()
    {
        ImDrawList clone = new(null);
        clone.CmdBuffer.Clear();
        clone.CmdBuffer.AddRange(CmdBuffer);
        clone.IdxBuffer.Clear();
        clone.IdxBuffer.AddRange(IdxBuffer);
        clone.VtxBuffer.Clear();
        clone.VtxBuffer.AddRange(VtxBuffer);
        clone.Flags = Flags;
        return clone;
    }

    /// <summary>Emits an untextured rectangle using the atlas white texel.</summary>
    /// <param name="a">Top-left corner.</param>
    /// <param name="c">Bottom-right corner.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrimRect(ImVec2 a, ImVec2 c, uint col)
    {
        PrimRectUV(a, c, _texUvWhitePixel, _texUvWhitePixel, col);
    }

    /// <summary>Emits a textured rectangle from two opposite corners and their UVs.</summary>
    /// <param name="a">Top-left corner.</param>
    /// <param name="c">Bottom-right corner.</param>
    /// <param name="uvA">Texture coordinate at <paramref name="a"/>.</param>
    /// <param name="uvC">Texture coordinate at <paramref name="c"/>.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrimRectUV(ImVec2 a, ImVec2 c, ImVec2 uvA, ImVec2 uvC, uint col)
    {
        // Ports ImDrawList::PrimRectUV. Indices are relative to the active
        // vertex-buffer base so 64K splits keep 16-bit offsets valid.
        ushort idx = (ushort)_vtxCurrentIdx;
        IdxBuffer.Add(idx);
        IdxBuffer.Add((ushort)(idx + 1));
        IdxBuffer.Add((ushort)(idx + 2));
        IdxBuffer.Add(idx);
        IdxBuffer.Add((ushort)(idx + 2));
        IdxBuffer.Add((ushort)(idx + 3));

        VtxBuffer.Add(new ImDrawVert(a, uvA, col));
        VtxBuffer.Add(new ImDrawVert(new ImVec2(c.X, a.Y), new ImVec2(uvC.X, uvA.Y), col));
        VtxBuffer.Add(new ImDrawVert(c, uvC, col));
        VtxBuffer.Add(new ImDrawVert(new ImVec2(a.X, c.Y), new ImVec2(uvA.X, uvC.Y), col));
        _vtxCurrentIdx += 4;
    }

    /// <summary>Emits a quad from four corners and their texture coordinates.</summary>
    /// <param name="a">First corner.</param>
    /// <param name="b">Second corner.</param>
    /// <param name="c">Third corner.</param>
    /// <param name="d">Fourth corner.</param>
    /// <param name="uvA">Texture coordinate at <paramref name="a"/>.</param>
    /// <param name="uvB">Texture coordinate at <paramref name="b"/>.</param>
    /// <param name="uvC">Texture coordinate at <paramref name="c"/>.</param>
    /// <param name="uvD">Texture coordinate at <paramref name="d"/>.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PrimQuadUV(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 d, ImVec2 uvA, ImVec2 uvB, ImVec2 uvC, ImVec2 uvD, uint col)
    {
        // Ports ImDrawList::PrimQuadUV.
        ushort idx = (ushort)_vtxCurrentIdx;
        IdxBuffer.Add(idx);
        IdxBuffer.Add((ushort)(idx + 1));
        IdxBuffer.Add((ushort)(idx + 2));
        IdxBuffer.Add(idx);
        IdxBuffer.Add((ushort)(idx + 2));
        IdxBuffer.Add((ushort)(idx + 3));

        VtxBuffer.Add(new ImDrawVert(a, uvA, col));
        VtxBuffer.Add(new ImDrawVert(b, uvB, col));
        VtxBuffer.Add(new ImDrawVert(c, uvC, col));
        VtxBuffer.Add(new ImDrawVert(d, uvD, col));
        _vtxCurrentIdx += 4;
    }

    #endregion

    #region Geometry Helpers

    /// <summary>Builds the precomputed full-circle arc sample table.</summary>
    /// <returns>The sample points in canonical order.</returns>
    private static ImVec2[] BuildArcFastVtx()
    {
        var vtx = new ImVec2[ArcFastTableSize];
        for (int i = 0; i < vtx.Length; i++)
        {
            float a = (i * 2.0f * MathF.PI) / vtx.Length;
            vtx[i] = new ImVec2(MathF.Cos(a), MathF.Sin(a));
        }
        return vtx;
    }

    /// <summary>Recomputes the circle segment cache and arc table cutoff for a new error bound.</summary>
    /// <param name="maxError">Maximum circle tessellation error in pixels; must be positive.</param>
    private void SetCircleTessellationMaxError(float maxError)
    {
        // Ports ImDrawListSharedData::SetCircleTessellationMaxError.
        if (_circleTessellationMaxError == maxError)
            return;
        _circleTessellationMaxError = maxError;
        for (int i = 0; i < _circleSegmentCounts.Length; i++)
        {
            _circleSegmentCounts[i] = (byte)(i > 0
                ? CircleAutoSegmentCalc(i, maxError)
                : ArcFastSampleMax);
        }
        _arcFastRadiusCutoff = maxError / (1.0f - MathF.Cos(MathF.PI / MathF.Max(ArcFastSampleMax, MathF.PI)));
    }

    /// <summary>
    /// Evaluates the canonical automatic circle segment formula. Ports
    /// <c>IM_DRAWLIST_CIRCLE_AUTO_SEGMENT_CALC</c>.
    /// </summary>
    /// <param name="radius">Circle radius in pixels.</param>
    /// <param name="maxError">Maximum chord error in pixels.</param>
    /// <returns>The even, clamped segment count.</returns>
    private static int CircleAutoSegmentCalc(float radius, float maxError)
    {
        float angle = MathF.Acos(1.0f - MathF.Min(maxError, radius) / radius);
        int segments = (int)MathF.Ceiling(MathF.PI / angle);
        segments = ((segments + 1) / 2) * 2; // IM_ROUNDUP_TO_EVEN keeps raster results symmetric
        return Math.Clamp(segments, CircleAutoSegmentMin, CircleAutoSegmentMax);
    }

    /// <summary>
    /// Returns the automatic segment count for a radius, using the cached bucket or the dynamic
    /// formula. Ports canonical <c>ImDrawList::_CalcCircleAutoSegmentCount</c>
    /// </summary>
    /// <param name="radius">Circle radius in pixels.</param>
    /// <returns>The number of circle segments to emit.</returns>
    public int CalcCircleAutoSegmentCount(float radius)
    {
        int radiusIndex = (int)(radius + 0.999f); // Ceil so the cache never understates accuracy
        if (radiusIndex >= 0 && radiusIndex < _circleSegmentCounts.Length)
            return _circleSegmentCounts[radiusIndex];
        return CircleAutoSegmentCalc(radius, _circleTessellationMaxError);
    }

    /// <summary>Canonical alias for <see cref="CalcCircleAutoSegmentCount"/>.</summary>
    public int _CalcCircleAutoSegmentCount(float radius) => CalcCircleAutoSegmentCount(radius);

    /// <summary>Ensures reusable scratch arrays can hold the requested element counts.</summary>
    /// <param name="normalCount">Required join-normal capacity.</param>
    /// <param name="pointCount">Required expanded-point capacity.</param>
    private void EnsureScratchCapacity(int normalCount, int pointCount)
    {
        if (_tempNormals.Length < normalCount)
            _tempNormals = new ImVec2[normalCount];
        if (_tempSqrLengths.Length < normalCount)
            _tempSqrLengths = new float[normalCount];
        if (_tempPoints.Length < pointCount)
            _tempPoints = new ImVec2[pointCount];
    }

    /// <summary>
    /// Computes a fast reciprocal square root. Ports canonical <c>ImRsqrt</c>
    ///., which uses the hardware SSE estimate
    /// <c>_mm_rsqrt_ss</c> when available and otherwise falls back to <c>1.0f / sqrtf(x)</c>.
    /// </summary>
    /// <param name="x">The value to invert; callers guard against zero, where the hardware estimate diverges.</param>
    /// <returns>An approximation of <c>1 / sqrt(x)</c>.</returns>
    /// <remarks>
    /// The x86 path is the raw hardware estimate and matches canonical SSE builds exactly. The ARM
    /// path applies one Newton-Raphson refinement because canonical Dear ImGui has no NEON path and
    /// its ARM fallback is exact <c>1 / sqrt</c>; the refinement keeps the result within rounding of
    /// exact while retaining the SIMD estimate's speed.
    /// </remarks>
    internal static float Rsqrt(float x)
    {
        if (Sse.IsSupported)
            return Sse.ReciprocalSqrtScalar(Vector128.CreateScalarUnsafe(x)).ToScalar();

        if (AdvSimd.IsSupported)
        {
            float estimate = AdvSimd.ReciprocalSquareRootEstimate(Vector64.CreateScalarUnsafe(x)).ToScalar();
            return estimate * (1.5f - (0.5f * x * estimate * estimate));
        }

        return 1.0f / MathF.Sqrt(x);
    }

    /// <summary>Normalizes a 2D vector in place, leaving a zero vector untouched.</summary>
    /// <param name="vx">X component to normalize.</param>
    /// <param name="vy">Y component to normalize.</param>
    private static void NormalizeOverZero(ref float vx, ref float vy)
    {
        // Ports IM_NORMALIZE2F_OVER_ZERO.: a zero-length segment yields (0,0),
        // and non-zero lengths are normalized with the canonical ImRsqrt estimate.
        float d2 = (vx * vx) + (vy * vy);
        if (d2 > 0.0f)
        {
            float invLen = Rsqrt(d2);
            vx *= invLen;
            vy *= invLen;
        }
    }

    /// <summary>Reverses a 2D vector by dividing by its length squared, with a magnitude clamp.</summary>
    /// <param name="vx">X component to transform.</param>
    /// <param name="vy">Y component to transform.</param>
    private static void FixNormal(ref float vx, ref float vy)
    {
        // Ports IM_FIXNORMAL2F.: the inverse-length-squared clamp guards miter
        // joins against exploding on nearly opposite tangents.
        float d2 = (vx * vx) + (vy * vy);
        if (d2 > 0.000001f)
        {
            float invLen2 = 1.0f / d2;
            if (invLen2 > FixNormalMaxInvLen2)
                invLen2 = FixNormalMaxInvLen2;
            vx *= invLen2;
            vy *= invLen2;
        }
    }

    /// <summary>Determines whether three points wind clockwise.</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <param name="c">Third point.</param>
    /// <returns>True when the triangle winds clockwise.</returns>
    private static bool TriangleIsClockwise(ImVec2 a, ImVec2 b, ImVec2 c)
    {
        return ((b.X - a.X) * (c.Y - b.Y)) - ((c.X - b.X) * (b.Y - a.Y)) > 0.0f;
    }

    /// <summary>Determines whether a point lies inside a triangle.</summary>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="p">Point to test.</param>
    /// <returns>True when the point is inside the triangle.</returns>
    private static bool TriangleContainsPoint(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 p)
    {
        bool b1 = ((p.X - b.X) * (a.Y - b.Y) - (p.Y - b.Y) * (a.X - b.X)) < 0.0f;
        bool b2 = ((p.X - c.X) * (b.Y - c.Y) - (p.Y - c.Y) * (b.X - c.X)) < 0.0f;
        bool b3 = ((p.X - a.X) * (c.Y - a.Y) - (p.Y - a.Y) * (c.X - a.X)) < 0.0f;
        return (b1 == b2) && (b2 == b3);
    }

    #endregion

    #region Polyline Stroking

    /// <summary>
    /// Calculates approximation (within 0.001%) distance from the center of the circumscribed circle
    /// to the edge of the polygon, cos(pi / n). Ports canonical <c>ApproxApothem</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ApproxApothem(int numSegments)
    {
        float u = 1.0f / numSegments;
        return ((4.058712f * u * u) - 4.934802f) * u * u + 1.0f;
    }

    /// <summary>
    /// Selects baked or white UVs and fringe scale for line rendering.
    /// Ports canonical <c>ImDrawList::_SelectLineTexture</c>.
    /// </summary>
    private void _SelectLineTexture(float screenThickness, out ImVec2 outUv0, out ImVec2 outUv1, out float outFringe, ImDrawFlags flags)
    {
        if ((flags & ImDrawFlags.AALines) != 0)
        {
            const int texLinesWidthMax = ImFontAtlas.TexLinesWidthMax; // 32
            const int texLinesDetailedWidth = 4;
            const int texLinesSampleCount = 4;

            int textureIdx;
            if (screenThickness <= texLinesDetailedWidth)
                textureIdx = (int)((screenThickness - 1.0f) * texLinesSampleCount + 0.5f) + (texLinesWidthMax + 1);
            else if (screenThickness < (float)texLinesWidthMax - 1.5f)
                textureIdx = (int)(screenThickness + 0.5f);
            else
                textureIdx = texLinesWidthMax - 1;

            if (textureIdx < _texUvLines.Length && _texUvLines.Length > 0)
            {
                ImVec4 texUvs = _texUvLines[textureIdx];
                outUv0 = new ImVec2(texUvs.X, texUvs.Z);
                outUv1 = new ImVec2(texUvs.Y, texUvs.Z);
                outFringe = _fringeScale * screenThickness * texUvs.W;
            }
            else
            {
                outUv0 = _texUvWhitePixel;
                outUv1 = _texUvWhitePixel;
                outFringe = _fringeScale;
            }
        }
        else
        {
            outUv0 = _texUvWhitePixel;
            outUv1 = _texUvWhitePixel;
            outFringe = 0.0f;
        }
    }

    /// <summary>
    /// Strokes a polyline with canonical stroke positioning modes, cap and join styles, and fringe AA geometry.
    /// Ports canonical <c>ImDrawList::AddPolyline</c>.
    /// </summary>
    /// <param name="points">Polyline vertices in screen space.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Stroke thickness in pixels; values below one behave as one.</param>
    /// <param name="flags">Stroke flags; <c>Closed</c> joins the last point back to the first.</param>
    public void AddPolyline(ReadOnlySpan<ImVec2> points, uint col, float thickness, ImDrawFlags flags = ImDrawFlags.None)
    {
        _AddPolyline(points, points.Length, col, thickness, flags, float.MaxValue);
    }

    /// <summary>
    /// Internal polyline implementation with inner stroke offset clamping for closed convex shapes.
    /// Ports canonical <c>ImDrawList::_AddPolyline</c>.
    /// </summary>
    public void _AddPolyline(ReadOnlySpan<ImVec2> points, int pointsCount, uint col, float thickness, ImDrawFlags flags, float maxInnerOffset)
    {
        if (pointsCount < 2 || (col & 0xFF000000u) == 0)
            return;

        float screenThickness = thickness * _InvFringeScale;
        if (screenThickness < 1.0f)
        {
            if (screenThickness < 1.0f / 255.0f)
                return;
            col = ImAlphaMultiply(col, screenThickness);
            screenThickness = 1.0f;
            thickness = _fringeScale;
        }

        flags |= Flags;
        if ((flags & ImDrawFlags.InvalidMask_) != 0)
        {
            ImGui.AssertUserError(false, "Incorrect parameter. Did you swap 'thickness' and 'flags'?");
            return;
        }

        EnsureScratchCapacity(pointsCount, 0);
        ImVec2[] normals = _tempNormals;
        float[] sqrLengths = _tempSqrLengths;

        for (int i = 0; i < pointsCount - 1; i++)
        {
            float dx = points[i + 1].X - points[i].X;
            float dy = points[i + 1].Y - points[i].Y;
            float d2 = dx * dx + dy * dy;
            float invLen = (d2 > 0.0f) ? (1.0f / MathF.Sqrt(d2)) : 0.0f;
            normals[i] = new ImVec2(-dy * invLen, dx * invLen);
            sqrLengths[i] = d2;
        }
        if ((flags & ImDrawFlags.Closed) != 0)
        {
            float dx = points[0].X - points[pointsCount - 1].X;
            float dy = points[0].Y - points[pointsCount - 1].Y;
            float d2 = dx * dx + dy * dy;
            float invLen = (d2 > 0.0f) ? (1.0f / MathF.Sqrt(d2)) : 0.0f;
            normals[pointsCount - 1] = new ImVec2(-dy * invLen, dx * invLen);
            sqrLengths[pointsCount - 1] = d2;
        }
        else
        {
            normals[pointsCount - 1] = normals[pointsCount - 2];
            sqrLengths[pointsCount - 1] = 0.0f;
        }

        _SelectLineTexture(screenThickness, out ImVec2 uv0, out ImVec2 uv1, out float fringe, flags);

        uint colTrans = col & 0x00FFFFFFu;

        ImDrawFlags strokePos = _GetStrokePos(flags, ImDrawFlags.StrokeCenter);
        if (strokePos == ImDrawFlags.StrokeLegacy)
            flags = (flags | ImDrawFlags.JoinMiter) & ~ImDrawFlags.AALineEnds;

        bool closed = (flags & ImDrawFlags.Closed) != 0;
        bool mitersOnly = (flags & ImDrawFlags.JoinMiter) != 0;
        const float polylineMiterAngleLimit = -0.9999619f;
        const float polylineMiterLimit = 4.0f;
        const float miterDistanceLimitSqr = polylineMiterLimit * polylineMiterLimit;

        float thickness0 = (thickness + fringe) * 0.5f;
        if (strokePos == ImDrawFlags.StrokeOutside)
            thickness0 = thickness + fringe * 0.5f;
        else if (strokePos == ImDrawFlags.StrokeCenterBiased)
            thickness0 = _CalculateCenterBiasedOffset(thickness) + fringe * 0.5f;
        else if (strokePos == ImDrawFlags.StrokeInside)
            thickness0 = fringe * 0.5f;
        float thickness1 = (thickness + fringe) - thickness0;

        int maxVertsPerPoint = mitersOnly ? 2 : 4;
        int maxTrisPerPoint = mitersOnly ? 2 : 5;
        int vtxCount;
        int idxCount;
        if (closed)
        {
            vtxCount = pointsCount * maxVertsPerPoint + 2;
            idxCount = (pointsCount * maxTrisPerPoint + 2) * 3;
        }
        else
        {
            vtxCount = (pointsCount - 2) * maxVertsPerPoint + (4 * 2);
            idxCount = ((pointsCount - 2) * maxTrisPerPoint + 2 + (2 * 2)) * 3;
        }

        PrimReserve(idxCount, vtxCount);
        int initialIdxCount = IdxBuffer.Count;

        float ratio = (thickness0 + thickness1 > 0.0f) ? (thickness0 / (thickness0 + thickness1)) : 0.5f;
        ImVec2 uv2 = new ImVec2(uv0.X + (uv1.X - uv0.X) * ratio, uv0.Y);

        if (thickness1 > maxInnerOffset)
        {
            thickness1 = maxInnerOffset;
            float s = (thickness0 + thickness1) / (thickness + fringe);
            float du = uv1.X - uv0.X;
            uv1.X = uv0.X + du * s;
        }

        float halfAa = _fringeScale * 0.5f;
        bool useAaEnds = (flags & (ImDrawFlags.AALines | ImDrawFlags.AALineEnds)) == (ImDrawFlags.AALines | ImDrawFlags.AALineEnds);

        int firstVtxOffset = VtxBuffer.Count;
        int baseIdx = _vtxCurrentIdx;

        ImVec2 pa, pb, dir;
        ImVec2 p1;
        ImVec2 n1;
        float lenSqr1;

        int pointIdx = 0;
        int pointEnd = pointsCount;
        if (!closed)
        {
            pointIdx++;
            pointEnd--;
            p1 = points[0];
            n1 = normals[0];
            lenSqr1 = sqrLengths[0];

            dir = new ImVec2(n1.Y, -n1.X);
            if ((flags & ImDrawFlags.CapSquare) != 0)
            {
                p1.X -= dir.X * thickness * 0.5f;
                p1.Y -= dir.Y * thickness * 0.5f;
            }

            if (!useAaEnds)
            {
                baseIdx = _vtxCurrentIdx;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - n1.X * thickness0, p1.Y - n1.Y * thickness0), uv0, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + n1.X * thickness1, p1.Y + n1.Y * thickness1), uv1, col)); _vtxCurrentIdx++;
            }
            else
            {
                pa = new ImVec2(p1.X - dir.X * halfAa, p1.Y - dir.Y * halfAa);
                pb = new ImVec2(p1.X + dir.X * halfAa, p1.Y + dir.Y * halfAa);

                baseIdx = _vtxCurrentIdx;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pa.X - n1.X * thickness0, pa.Y - n1.Y * thickness0), uv0, colTrans)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pa.X + n1.X * thickness1, pa.Y + n1.Y * thickness1), uv1, colTrans)); _vtxCurrentIdx++;

                int nextBaseIdx = _vtxCurrentIdx;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pb.X - n1.X * thickness0, pb.Y - n1.Y * thickness0), uv0, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pb.X + n1.X * thickness1, pb.Y + n1.Y * thickness1), uv1, col)); _vtxCurrentIdx++;

                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 3));
                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 1));
                baseIdx = nextBaseIdx;
            }
        }
        else
        {
            p1 = points[pointsCount - 1];
            n1 = normals[pointsCount - 1];
            lenSqr1 = sqrLengths[pointsCount - 1];

            baseIdx = _vtxCurrentIdx;
            VtxBuffer.Add(new ImDrawVert(ImVec2.Zero, uv0, col)); _vtxCurrentIdx++;
            VtxBuffer.Add(new ImDrawVert(ImVec2.Zero, uv1, col)); _vtxCurrentIdx++;
        }

        if (mitersOnly)
        {
            while (pointIdx < pointEnd)
            {
                ImVec2 n0 = n1;
                p1 = points[pointIdx];
                n1 = normals[pointIdx];

                float cosTheta = n0.X * n1.X + n0.Y * n1.Y;
                float cosThetaClamped = MathF.Max(polylineMiterAngleLimit, cosTheta);
                float miterScaleFactor = MathF.Min(1000.0f, 1.0f / (1.0f + cosThetaClamped));
                float miterOffsetX = (n0.X + n1.X) * miterScaleFactor;
                float miterOffsetY = (n0.Y + n1.Y) * miterScaleFactor;

                int nextBaseIdx = _vtxCurrentIdx;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - miterOffsetX * thickness0, p1.Y - miterOffsetY * thickness0), uv0, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + miterOffsetX * thickness1, p1.Y + miterOffsetY * thickness1), uv1, col)); _vtxCurrentIdx++;

                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 3));
                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 1));

                baseIdx = nextBaseIdx;
                pointIdx++;
            }
        }
        else
        {
            while (pointIdx < pointEnd)
            {
                ImVec2 n0 = n1;
                float lenSqr0 = lenSqr1;
                p1 = points[pointIdx];
                n1 = normals[pointIdx];
                lenSqr1 = sqrLengths[pointIdx];

                float cosTheta = n0.X * n1.X + n0.Y * n1.Y;
                float sinTheta = n0.Y * n1.X - n0.X * n1.Y;

                float cosThetaClamped = MathF.Max(polylineMiterAngleLimit, cosTheta);
                float miterScaleFactor = MathF.Min(1000.0f, 1.0f / (1.0f + cosThetaClamped));
                float miterOffsetX = (n0.X + n1.X) * miterScaleFactor;
                float miterOffsetY = (n0.Y + n1.Y) * miterScaleFactor;

                float miterDistanceSqr = miterOffsetX * miterOffsetX + miterOffsetY * miterOffsetY;

                if (miterDistanceSqr > miterDistanceLimitSqr)
                {
                    float refThickness = (sinTheta < 0.0f) ? thickness1 : thickness0;
                    float refThicknessSqr = refThickness * refThickness;
                    float limitSqr = MathF.Max(MathF.Max(lenSqr0, lenSqr1), refThicknessSqr);
                    float refMiterDistSqr = miterDistanceSqr * refThicknessSqr;
                    if (refMiterDistSqr > limitSqr)
                    {
                        float scale = MathF.Sqrt(limitSqr / refMiterDistSqr);
                        miterOffsetX *= scale;
                        miterOffsetY *= scale;
                    }

                    float bnX = n0.X + n1.X;
                    float bnY = n0.Y + n1.Y;
                    NormalizeOverZero(ref bnX, ref bnY);

                    float sideOffset = ((n0.X * bnX + n0.Y * bnY) - 1.0f) / (n0.Y * bnX - n0.X * bnY);
                    float sdX = bnY * sideOffset;
                    float sdY = -bnX * sideOffset;

                    if (sinTheta < 0.0f)
                    {
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - (bnX + sdX) * thickness0, p1.Y - (bnY + sdY) * thickness0), uv0, col)); _vtxCurrentIdx++;
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X, p1.Y), uv2, col)); _vtxCurrentIdx++;
                        int nextBaseIdx = _vtxCurrentIdx;
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - (bnX - sdX) * thickness0, p1.Y - (bnY - sdY) * thickness0), uv0, col)); _vtxCurrentIdx++;
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + miterOffsetX * thickness1, p1.Y + miterOffsetY * thickness1), uv1, col)); _vtxCurrentIdx++;

                        IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 5));
                        IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 5)); IdxBuffer.Add((ushort)(baseIdx + 1));

                        IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 5)); IdxBuffer.Add((ushort)(baseIdx + 2));
                        IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 4));
                        IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 4)); IdxBuffer.Add((ushort)(baseIdx + 5));
                        baseIdx = nextBaseIdx;
                    }
                    else
                    {
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + (bnX + sdX) * thickness1, p1.Y + (bnY + sdY) * thickness1), uv1, col)); _vtxCurrentIdx++;
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X, p1.Y), uv2, col)); _vtxCurrentIdx++;
                        int nextBaseIdx = _vtxCurrentIdx;
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - miterOffsetX * thickness0, p1.Y - miterOffsetY * thickness0), uv0, col)); _vtxCurrentIdx++;
                        VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + (bnX - sdX) * thickness1, p1.Y + (bnY - sdY) * thickness1), uv1, col)); _vtxCurrentIdx++;

                        IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 4)); IdxBuffer.Add((ushort)(baseIdx + 2));
                        IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 1));

                        IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 4));
                        IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 4)); IdxBuffer.Add((ushort)(baseIdx + 5));
                        IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 5)); IdxBuffer.Add((ushort)(baseIdx + 2));
                        baseIdx = nextBaseIdx;
                    }
                }
                else
                {
                    int nextBaseIdx = _vtxCurrentIdx;
                    VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - miterOffsetX * thickness0, p1.Y - miterOffsetY * thickness0), uv0, col)); _vtxCurrentIdx++;
                    VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + miterOffsetX * thickness1, p1.Y + miterOffsetY * thickness1), uv1, col)); _vtxCurrentIdx++;

                    IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 3));
                    IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 1));
                    baseIdx = nextBaseIdx;
                }

                pointIdx++;
            }
        }

        if (!closed)
        {
            p1 = points[pointsCount - 1];
            n1 = normals[pointsCount - 1];

            dir = new ImVec2(n1.Y, -n1.X);
            if ((flags & ImDrawFlags.CapSquare) != 0)
            {
                p1.X += dir.X * thickness * 0.5f;
                p1.Y += dir.Y * thickness * 0.5f;
            }

            if (!useAaEnds)
            {
                VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X - n1.X * thickness0, p1.Y - n1.Y * thickness0), uv0, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(p1.X + n1.X * thickness1, p1.Y + n1.Y * thickness1), uv1, col)); _vtxCurrentIdx++;

                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 3));
                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 1));
            }
            else
            {
                pa = new ImVec2(p1.X - dir.X * halfAa, p1.Y - dir.Y * halfAa);
                pb = new ImVec2(p1.X + dir.X * halfAa, p1.Y + dir.Y * halfAa);

                int nextBaseIdx = _vtxCurrentIdx;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pa.X - n1.X * thickness0, pa.Y - n1.Y * thickness0), uv0, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pa.X + n1.X * thickness1, pa.Y + n1.Y * thickness1), uv1, col)); _vtxCurrentIdx++;

                VtxBuffer.Add(new ImDrawVert(new ImVec2(pb.X - n1.X * thickness0, pb.Y - n1.Y * thickness0), uv0, colTrans)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(pb.X + n1.X * thickness1, pb.Y + n1.Y * thickness1), uv1, colTrans)); _vtxCurrentIdx++;

                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 3));
                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 1));
                baseIdx = nextBaseIdx;

                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 2)); IdxBuffer.Add((ushort)(baseIdx + 3));
                IdxBuffer.Add((ushort)(baseIdx + 0)); IdxBuffer.Add((ushort)(baseIdx + 3)); IdxBuffer.Add((ushort)(baseIdx + 1));
            }
        }
        else
        {
            VtxBuffer[firstVtxOffset + 0] = VtxBuffer[(int)_cmdHeaderVtxOffset + baseIdx + 0];
            VtxBuffer[firstVtxOffset + 1] = VtxBuffer[(int)_cmdHeaderVtxOffset + baseIdx + 1];
        }

        int actualIdxAdded = IdxBuffer.Count - initialIdxCount;
        int remainingIdxCount = idxCount - actualIdxAdded;
        if (remainingIdxCount > 0 && CmdBuffer.Count > 0)
        {
            int cmdIdx = CmdBuffer.Count - 1;
            ImDrawCmd cmd = CmdBuffer[cmdIdx];
            cmd.ElemCount -= (uint)remainingIdxCount;
            CmdBuffer[cmdIdx] = cmd;
        }
    }

    #endregion

    #region Polygon Filling

    /// <summary>
    /// Fills a convex polygon with canonical anti-aliased fringe geometry. Ports
    /// <c>ImDrawList::AddConvexPolyFilled</c>.
    /// Filled shapes must be emitted clockwise so the fringe points outward.
    /// </summary>
    /// <param name="points">Convex polygon vertices in screen space.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="flags">Draw flags for anti-aliasing.</param>
    public void AddConvexPolyFilled(ReadOnlySpan<ImVec2> points, uint col, ImDrawFlags flags = ImDrawFlags.None)
    {
        int pointsCount = points.Length;
        if (pointsCount < 3 || (col & 0xFF000000) == 0)
            return;

        ImVec2 uv = _texUvWhitePixel;
        flags |= Flags;

        if ((flags & ImDrawFlags.AAFill) != 0)
        {
            float aaSize = _fringeScale;
            uint colTrans = col & 0x00FFFFFFu;
            int idxCount = (pointsCount - 2) * 3 + pointsCount * 6;
            int vtxCount = pointsCount * 2;
            PrimReserve(idxCount, vtxCount);

            int vtxInner = _vtxCurrentIdx;
            int vtxOuter = _vtxCurrentIdx + 1;

            // Fan the interior against the first vertex; inner vertices occupy even slots.
            for (int i = 2; i < pointsCount; i++)
            {
                IdxBuffer.Add((ushort)vtxInner);
                IdxBuffer.Add((ushort)(vtxInner + ((i - 1) << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (i << 1)));
            }

            EnsureScratchCapacity(pointsCount, 0);
            ImVec2[] tempNormals = _tempNormals;
            for (int i0 = pointsCount - 1, i1 = 0; i1 < pointsCount; i0 = i1++)
            {
                float dx = points[i1].X - points[i0].X;
                float dy = points[i1].Y - points[i0].Y;
                NormalizeOverZero(ref dx, ref dy);
                tempNormals[i0] = new ImVec2(dy, -dx);
            }

            for (int i0 = pointsCount - 1, i1 = 0; i1 < pointsCount; i0 = i1++)
            {
                float dmX = (tempNormals[i0].X + tempNormals[i1].X) * 0.5f;
                float dmY = (tempNormals[i0].Y + tempNormals[i1].Y) * 0.5f;
                FixNormal(ref dmX, ref dmY);
                dmX *= aaSize * 0.5f;
                dmY *= aaSize * 0.5f;

                VtxBuffer.Add(new ImDrawVert(new ImVec2(points[i1].X - dmX, points[i1].Y - dmY), uv, col));
                VtxBuffer.Add(new ImDrawVert(new ImVec2(points[i1].X + dmX, points[i1].Y + dmY), uv, colTrans));

                IdxBuffer.Add((ushort)(vtxInner + (i1 << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (i0 << 1)));
                IdxBuffer.Add((ushort)(vtxOuter + (i0 << 1)));
                IdxBuffer.Add((ushort)(vtxOuter + (i0 << 1)));
                IdxBuffer.Add((ushort)(vtxOuter + (i1 << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (i1 << 1)));
            }

            _vtxCurrentIdx += vtxCount;
        }
        else
        {
            int idxCount = (pointsCount - 2) * 3;
            int vtxCount = pointsCount;
            PrimReserve(idxCount, vtxCount);
            int baseIdx = _vtxCurrentIdx;
            for (int i = 0; i < vtxCount; i++)
                VtxBuffer.Add(new ImDrawVert(points[i], uv, col));
            for (int i = 2; i < pointsCount; i++)
            {
                IdxBuffer.Add((ushort)baseIdx);
                IdxBuffer.Add((ushort)(baseIdx + i - 1));
                IdxBuffer.Add((ushort)(baseIdx + i));
            }
            _vtxCurrentIdx += vtxCount;
        }
    }

    /// <summary>Emits a filled convex polygon.</summary>
    public void AddConvexPolyFilled(ImVec2[] points, int numPoints, uint col, ImDrawFlags flags = ImDrawFlags.None)
        => AddConvexPolyFilled(points.AsSpan(0, numPoints), col, flags);

    /// <summary>
    /// Fills a simple concave polygon by ear-clipping it into triangles, then emits the same
    /// anti-aliased fringe as the convex fill. Ports <c>ImDrawList::AddConcavePolyFilled</c>
    /// </summary>
    /// <param name="points">Simple concave polygon vertices in screen space; no holes or self-intersections.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    public void AddConcavePolyFilled(ReadOnlySpan<ImVec2> points, uint col)
    {
        int pointsCount = points.Length;
        if (pointsCount < 3 || (col & 0xFF000000) == 0)
            return;

        ImVec2 uv = _texUvWhitePixel;
        _triangulator.Init(points);

        if ((Flags & ImDrawFlags.AAFill) != 0)
        {
            float aaSize = _fringeScale;
            uint colTrans = col & 0x00FFFFFFu;
            int idxCount = (pointsCount - 2) * 3 + pointsCount * 6;
            int vtxCount = pointsCount * 2;
            PrimReserve(idxCount, vtxCount);

            int vtxInner = _vtxCurrentIdx;
            int vtxOuter = _vtxCurrentIdx + 1;

            // Interior triangles first; they reference vertices that are appended below.
            Span<int> triangle = stackalloc int[3];
            while (_triangulator.TrianglesLeft > 0)
            {
                _triangulator.GetNextTriangle(triangle);
                IdxBuffer.Add((ushort)(vtxInner + (triangle[0] << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (triangle[1] << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (triangle[2] << 1)));
            }

            EnsureScratchCapacity(pointsCount, 0);
            ImVec2[] tempNormals = _tempNormals;
            for (int i0 = pointsCount - 1, i1 = 0; i1 < pointsCount; i0 = i1++)
            {
                float dx = points[i1].X - points[i0].X;
                float dy = points[i1].Y - points[i0].Y;
                NormalizeOverZero(ref dx, ref dy);
                tempNormals[i0] = new ImVec2(dy, -dx);
            }

            for (int i0 = pointsCount - 1, i1 = 0; i1 < pointsCount; i0 = i1++)
            {
                float dmX = (tempNormals[i0].X + tempNormals[i1].X) * 0.5f;
                float dmY = (tempNormals[i0].Y + tempNormals[i1].Y) * 0.5f;
                FixNormal(ref dmX, ref dmY);
                dmX *= aaSize * 0.5f;
                dmY *= aaSize * 0.5f;

                VtxBuffer.Add(new ImDrawVert(new ImVec2(points[i1].X - dmX, points[i1].Y - dmY), uv, col));
                VtxBuffer.Add(new ImDrawVert(new ImVec2(points[i1].X + dmX, points[i1].Y + dmY), uv, colTrans));

                IdxBuffer.Add((ushort)(vtxInner + (i1 << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (i0 << 1)));
                IdxBuffer.Add((ushort)(vtxOuter + (i0 << 1)));
                IdxBuffer.Add((ushort)(vtxOuter + (i0 << 1)));
                IdxBuffer.Add((ushort)(vtxOuter + (i1 << 1)));
                IdxBuffer.Add((ushort)(vtxInner + (i1 << 1)));
            }

            _vtxCurrentIdx += vtxCount;
        }
        else
        {
            int idxCount = (pointsCount - 2) * 3;
            int vtxCount = pointsCount;
            PrimReserve(idxCount, vtxCount);
            int baseIdx = _vtxCurrentIdx;
            for (int i = 0; i < vtxCount; i++)
                VtxBuffer.Add(new ImDrawVert(points[i], uv, col));

            Span<int> triangle = stackalloc int[3];
            while (_triangulator.TrianglesLeft > 0)
            {
                _triangulator.GetNextTriangle(triangle);
                IdxBuffer.Add((ushort)(baseIdx + triangle[0]));
                IdxBuffer.Add((ushort)(baseIdx + triangle[1]));
                IdxBuffer.Add((ushort)(baseIdx + triangle[2]));
            }
            _vtxCurrentIdx += vtxCount;
        }
    }

    #endregion

    #region High-Level Primitives

    /// <summary>Emits a line segment as an anti-aliased stroke.</summary>
    /// <param name="p1">Start point.</param>
    /// <param name="p2">End point.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddLine(ImVec2 p1, ImVec2 p2, uint col, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        flags |= Flags;
        if (_GetStrokePos(flags, ImDrawFlags.StrokeCenter) == ImDrawFlags.StrokeLegacy)
        {
            Span<ImVec2> points = stackalloc ImVec2[2];
            points[0] = new ImVec2(p1.X + 0.5f, p1.Y + 0.5f);
            points[1] = new ImVec2(p2.X + 0.5f, p2.Y + 0.5f);
            AddPolyline(points, col, thickness, flags);
            return;
        }
        Span<ImVec2> pts = stackalloc ImVec2[2];
        pts[0] = p1;
        pts[1] = p2;
        AddPolyline(pts, col, thickness, flags);
    }

    /// <summary>
    /// Multiplies the alpha channel of a 32-bit packed color by the specified scale factor.
    /// </summary>
    /// <param name="col">The 32-bit packed RGBA/ABGR color.</param>
    /// <param name="alphaMul">The multiplier to apply to the alpha component.</param>
    /// <returns>The resulting 32-bit packed color with the scaled alpha channel.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ImAlphaMultiply(uint col, float alphaMul)
    {
        uint a = (col & 0xFF000000u) >> 24;
        a = (uint)(a * alphaMul);
        return (col & ~0xFF000000u) | (a << 24);
    }

    /// <summary>Emits a horizontal line.</summary>
    /// <param name="minX">Left end X coordinate.</param>
    /// <param name="maxX">Right end X coordinate.</param>
    /// <param name="y">Shared Y coordinate.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddLineH(float minX, float maxX, float y, uint col, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        flags |= Flags;
        ImDrawFlags strokePos = _GetStrokePos(flags, ImDrawFlags.StrokeInside);
        if (strokePos == ImDrawFlags.StrokeLegacy)
        {
            AddLine(new ImVec2(minX + 0.5f, y + 0.5f), new ImVec2(maxX + 0.5f, y + 0.5f), col, thickness, flags);
            return;
        }
        float topY = y;
        if (strokePos == ImDrawFlags.StrokeCenter)
            topY -= thickness * 0.5f;
        else if (strokePos == ImDrawFlags.StrokeCenterBiased)
            topY -= _CalculateCenterBiasedOffset(thickness);
        else if (strokePos == ImDrawFlags.StrokeOutside)
            topY -= thickness;

        bool isTruncated = (float)(int)minX == minX && (float)(int)maxX == maxX && (float)(int)topY == topY && (float)(int)thickness == thickness;
        if (_FringeScaleIsInteger && isTruncated && (flags & ImDrawFlags.CapSquare) == 0)
        {
            if (thickness < _FringeScale)
            {
                if (thickness < (1.0f / 255.0f) * _FringeScale)
                    return;
                col = ImAlphaMultiply(col, thickness * _InvFringeScale);
                thickness = _FringeScale;
            }

            // For pixel aligned case, use simple rectangle.
            PrimReserve(6, 4);
            PrimRect(new ImVec2(minX, topY), new ImVec2(maxX, topY + thickness), col);
        }
        else
        {
            flags = (flags & ~ImDrawFlags.StrokeMask_) | strokePos;
            AddLine(new ImVec2(minX, y), new ImVec2(maxX, y), col, thickness, flags);
        }
    }

    /// <summary>Emits a vertical line.</summary>
    /// <param name="x">Shared X coordinate.</param>
    /// <param name="minY">Top end Y coordinate.</param>
    /// <param name="maxY">Bottom end Y coordinate.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddLineV(float x, float minY, float maxY, uint col, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        flags |= Flags;
        ImDrawFlags strokePos = _GetStrokePos(flags, ImDrawFlags.StrokeInside);
        if (strokePos == ImDrawFlags.StrokeLegacy)
        {
            AddLine(new ImVec2(x + 0.5f, minY + 0.5f), new ImVec2(x + 0.5f, maxY + 0.5f), col, thickness, flags);
            return;
        }
        float leftX = x;
        if (strokePos == ImDrawFlags.StrokeCenter)
            leftX -= thickness * 0.5f;
        else if (strokePos == ImDrawFlags.StrokeCenterBiased)
            leftX -= _CalculateCenterBiasedOffset(thickness);
        else if (strokePos == ImDrawFlags.StrokeOutside)
            leftX -= thickness;

        bool isTruncated = (float)(int)leftX == leftX && (float)(int)minY == minY && (float)(int)maxY == maxY && (float)(int)thickness == thickness;
        if (_FringeScaleIsInteger && isTruncated && (flags & ImDrawFlags.CapSquare) == 0)
        {
            if (thickness < _FringeScale)
            {
                if (thickness < (1.0f / 255.0f) * _FringeScale)
                    return;
                col = ImAlphaMultiply(col, thickness * _InvFringeScale);
                thickness = _FringeScale;
            }

            // For pixel aligned case, use simple rectangle.
            PrimReserve(6, 4);
            PrimRect(new ImVec2(leftX, minY), new ImVec2(leftX + thickness, maxY), col);
        }
        else
        {
            flags = (flags & ~ImDrawFlags.StrokeMask_) | strokePos;
            AddLine(new ImVec2(x, maxY), new ImVec2(x, minY), col, thickness, flags);
        }
    }

    /// <summary>Emits a rectangle outline, optionally rounded.</summary>
    /// <param name="pMin">Top-left corner.</param>
    /// <param name="pMax">Bottom-right corner.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="rounding">Corner rounding radius in pixels; zero draws sharp corners.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="flags">Corner selection flags.</param>
    public void AddRect(ImVec2 pMin, ImVec2 pMax, uint col, float rounding = 0.0f, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((flags & ImDrawFlags.InvalidMask_) != 0)
        {
            ImGui.AssertUserError(false, "Incorrect parameter. Did you swap 'thickness' and 'flags'?");
            return;
        }
        if ((col & 0xFF000000u) == 0)
            return;

        flags |= Flags;
        ImDrawFlags strokePos = _GetStrokePos(flags, ImDrawFlags.StrokeInside);
        if (strokePos == ImDrawFlags.StrokeLegacy)
        {
            if ((flags & ImDrawFlags.AALines) != 0)
                PathRect(pMin + new ImVec2(0.50f, 0.50f), pMax - new ImVec2(0.50f, 0.50f), rounding, flags);
            else
                PathRect(pMin + new ImVec2(0.50f, 0.50f), pMax - new ImVec2(0.49f, 0.49f), rounding, flags);
            PathStroke(col, thickness, ImDrawFlags.Closed | ImDrawFlags.JoinMiter | ImDrawFlags.StrokeCenter | (flags & ~ImDrawFlags.StrokeMask_));
            return;
        }

        bool hasRounding = rounding > 0.0f;
        ImVec2 outerMin = pMin;
        ImVec2 outerMax = pMax;
        float outerRounding = rounding;

        float offset = 0.0f; // Inside
        if (strokePos == ImDrawFlags.StrokeCenter)
            offset = thickness * 0.5f;
        else if (strokePos == ImDrawFlags.StrokeCenterBiased)
            offset = _CalculateCenterBiasedOffset(thickness);
        else if (strokePos == ImDrawFlags.StrokeOutside)
            offset = thickness;

        outerMin.X -= offset;
        outerMin.Y -= offset;
        outerMax.X += offset;
        outerMax.Y += offset;
        outerRounding += hasRounding ? offset : 0.0f;

        float width = outerMax.X - outerMin.X;
        float height = outerMax.Y - outerMin.Y;
        float halfMinDim = MathF.Min(width, height) * 0.5f;
        if (halfMinDim <= thickness)
        {
            AddRectFilled(outerMin, outerMax, col, outerRounding, flags);
            return;
        }

        if ((flags & ImDrawFlags.RoundMask_) == 0)
            flags |= ImDrawFlags.RoundAll;

        if (hasRounding)
        {
            outerRounding = MathF.Min(outerRounding, MathF.Abs(width) * (((flags & ImDrawFlags.RoundTop) == ImDrawFlags.RoundTop) || ((flags & ImDrawFlags.RoundBottom) == ImDrawFlags.RoundBottom) ? 0.5f : 1.0f) - 1.0f);
            outerRounding = MathF.Min(outerRounding, MathF.Abs(height) * (((flags & ImDrawFlags.RoundLeft) == ImDrawFlags.RoundLeft) || ((flags & ImDrawFlags.RoundRight) == ImDrawFlags.RoundRight) ? 0.5f : 1.0f) - 1.0f);
        }

        bool isTruncated = (float)(int)outerMin.X == outerMin.X && (float)(int)outerMin.Y == outerMin.Y &&
                           (float)(int)outerMax.X == outerMax.X && (float)(int)outerMax.Y == outerMax.Y &&
                           (float)(int)outerRounding == outerRounding && (float)(int)thickness == thickness;
        if (_FringeScaleIsInteger && isTruncated)
        {
            int sRounding = (int)(outerRounding * _InvFringeScale);
            int sThickness = (int)(thickness * _InvFringeScale);
            if (sThickness <= 0)
                return;

            if (sRounding <= 0 || (flags & ImDrawFlags.RoundMask_) == ImDrawFlags.RoundNone)
            {
                // Pixel aligned non-rounded rect.
                ImVec2 opaqueUv = _texUvWhitePixel;
                float t = (float)sThickness * _fringeScale;

                PrimReserve(8 * 3, 8);
                ushort idx = (ushort)_vtxCurrentIdx;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMin.X, outerMin.Y), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMin.X + t, outerMin.Y + t), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMax.X, outerMin.Y), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMax.X - t, outerMin.Y + t), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMax.X, outerMax.Y), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMax.X - t, outerMax.Y - t), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMin.X, outerMax.Y), opaqueUv, col)); _vtxCurrentIdx++;
                VtxBuffer.Add(new ImDrawVert(new ImVec2(outerMin.X + t, outerMax.Y - t), opaqueUv, col)); _vtxCurrentIdx++;

                IdxBuffer.Add((ushort)(idx + 0)); IdxBuffer.Add((ushort)(idx + 3)); IdxBuffer.Add((ushort)(idx + 1));
                IdxBuffer.Add((ushort)(idx + 0)); IdxBuffer.Add((ushort)(idx + 2)); IdxBuffer.Add((ushort)(idx + 3));

                IdxBuffer.Add((ushort)(idx + 2)); IdxBuffer.Add((ushort)(idx + 5)); IdxBuffer.Add((ushort)(idx + 3));
                IdxBuffer.Add((ushort)(idx + 2)); IdxBuffer.Add((ushort)(idx + 4)); IdxBuffer.Add((ushort)(idx + 5));

                IdxBuffer.Add((ushort)(idx + 4)); IdxBuffer.Add((ushort)(idx + 7)); IdxBuffer.Add((ushort)(idx + 5));
                IdxBuffer.Add((ushort)(idx + 4)); IdxBuffer.Add((ushort)(idx + 6)); IdxBuffer.Add((ushort)(idx + 7));

                IdxBuffer.Add((ushort)(idx + 6)); IdxBuffer.Add((ushort)(idx + 1)); IdxBuffer.Add((ushort)(idx + 7));
                IdxBuffer.Add((ushort)(idx + 6)); IdxBuffer.Add((ushort)(idx + 0)); IdxBuffer.Add((ushort)(idx + 1));

                return;
            }
        }

        if (!hasRounding || (flags & ImDrawFlags.RoundMask_) == ImDrawFlags.RoundNone)
        {
            Span<ImVec2> points = stackalloc ImVec2[4]
            {
                outerMin,
                new ImVec2(outerMax.X, outerMin.Y),
                outerMax,
                new ImVec2(outerMin.X, outerMax.Y)
            };
            _AddPolyline(points, 4, col, thickness, ImDrawFlags.Closed | ImDrawFlags.JoinMiter | ImDrawFlags.StrokeInside | (flags & ~ImDrawFlags.StrokeMask_), halfMinDim);
            _path.Clear();
        }
        else
        {
            float roundingTl = ((flags & ImDrawFlags.RoundTopLeft) != 0) ? outerRounding : 0.0f;
            float roundingTr = ((flags & ImDrawFlags.RoundTopRight) != 0) ? outerRounding : 0.0f;
            float roundingBr = ((flags & ImDrawFlags.RoundBottomRight) != 0) ? outerRounding : 0.0f;
            float roundingBl = ((flags & ImDrawFlags.RoundBottomLeft) != 0) ? outerRounding : 0.0f;
            PathArcToFast(new ImVec2(outerMin.X + roundingTl, outerMin.Y + roundingTl), roundingTl, 6, 9);
            PathArcToFast(new ImVec2(outerMax.X - roundingTr, outerMin.Y + roundingTr), roundingTr, 9, 12);
            PathArcToFast(new ImVec2(outerMax.X - roundingBr, outerMax.Y - roundingBr), roundingBr, 0, 3);
            PathArcToFast(new ImVec2(outerMin.X + roundingBl, outerMax.Y - roundingBl), roundingBl, 3, 6);

            _AddPolyline(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_path), _path.Count, col, thickness, ImDrawFlags.Closed | ImDrawFlags.JoinMiter | ImDrawFlags.StrokeInside | (flags & ~ImDrawFlags.StrokeMask_), halfMinDim);
            _path.Clear();
        }
    }

    /// <summary>Emits a filled rectangle, optionally rounded.</summary>
    /// <param name="pMin">Top-left corner.</param>
    /// <param name="pMax">Bottom-right corner.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="rounding">Corner rounding radius in pixels; zero draws sharp corners.</param>
    /// <param name="flags">Corner selection flags.</param>
    public void AddRectFilled(ImVec2 pMin, ImVec2 pMax, uint col, float rounding = 0.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        // Ports ImDrawList::AddRectFilled.
        if (rounding < 0.5f || (flags & ImDrawFlags.RoundCornersMask_) == ImDrawFlags.RoundCornersNone)
        {
            PrimReserve(6, 4);
            PrimRect(pMin, pMax, col);
        }
        else
        {
            PathRect(pMin, pMax, rounding, flags);
            PathFillConvex(col);
        }
    }

    /// <summary>Emits a rectangle with a distinct color at each corner.</summary>
    /// <param name="pMin">Top-left corner.</param>
    /// <param name="pMax">Bottom-right corner.</param>
    /// <param name="colUprLeft">Color at the top-left corner.</param>
    /// <param name="colUprRight">Color at the top-right corner.</param>
    /// <param name="colBotRight">Color at the bottom-right corner.</param>
    /// <param name="colBotLeft">Color at the bottom-left corner.</param>
    public void AddRectFilledMultiColor(ImVec2 pMin, ImVec2 pMax, uint colUprLeft, uint colUprRight, uint colBotRight, uint colBotLeft)
    {
        if (((colUprLeft | colUprRight | colBotRight | colBotLeft) & 0xFF000000) == 0)
            return;
        PrimReserve(6, 4);
        ushort idx = (ushort)_vtxCurrentIdx;
        IdxBuffer.Add(idx);
        IdxBuffer.Add((ushort)(idx + 1));
        IdxBuffer.Add((ushort)(idx + 2));
        IdxBuffer.Add(idx);
        IdxBuffer.Add((ushort)(idx + 2));
        IdxBuffer.Add((ushort)(idx + 3));

        VtxBuffer.Add(new ImDrawVert(pMin, _texUvWhitePixel, colUprLeft));
        VtxBuffer.Add(new ImDrawVert(new ImVec2(pMax.X, pMin.Y), _texUvWhitePixel, colUprRight));
        VtxBuffer.Add(new ImDrawVert(pMax, _texUvWhitePixel, colBotRight));
        VtxBuffer.Add(new ImDrawVert(new ImVec2(pMin.X, pMax.Y), _texUvWhitePixel, colBotLeft));
        _vtxCurrentIdx += 4;
    }

    /// <summary>Emits a quad outline as an anti-aliased stroke.</summary>
    /// <param name="p1">First vertex.</param>
    /// <param name="p2">Second vertex.</param>
    /// <param name="p3">Third vertex.</param>
    /// <param name="p4">Fourth vertex.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddQuad(ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4, uint col, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        PathLineTo(p1);
        PathLineTo(p2);
        PathLineTo(p3);
        PathLineTo(p4);
        PathStroke(col, thickness, ImDrawFlags.Closed | flags);
    }

    /// <summary>Emits a filled quad.</summary>
    /// <param name="p1">First vertex.</param>
    /// <param name="p2">Second vertex.</param>
    /// <param name="p3">Third vertex.</param>
    /// <param name="p4">Fourth vertex.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    public void AddQuadFilled(ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4, uint col)
    {
        if ((col & 0xFF000000) == 0)
            return;
        PathLineTo(p1);
        PathLineTo(p2);
        PathLineTo(p3);
        PathLineTo(p4);
        PathFillConvex(col);
    }

    /// <summary>Emits a triangle outline as an anti-aliased stroke.</summary>
    /// <param name="p1">First vertex.</param>
    /// <param name="p2">Second vertex.</param>
    /// <param name="p3">Third vertex.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddTriangle(ImVec2 p1, ImVec2 p2, ImVec2 p3, uint col, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        PathLineTo(p1);
        PathLineTo(p2);
        PathLineTo(p3);
        PathStroke(col, thickness, ImDrawFlags.Closed | flags);
    }

    /// <summary>Emits a filled triangle.</summary>
    /// <param name="p1">First vertex.</param>
    /// <param name="p2">Second vertex.</param>
    /// <param name="p3">Third vertex.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    public void AddTriangleFilled(ImVec2 p1, ImVec2 p2, ImVec2 p3, uint col)
    {
        if ((col & 0xFF000000) == 0)
            return;
        PathLineTo(p1);
        PathLineTo(p2);
        PathLineTo(p3);
        PathFillConvex(col);
    }

    /// <summary>
    /// Renders a crisp 3-point checkmark conforming to canonical Dear ImGui.
    /// </summary>
    /// <param name="pos">Top-left position of the checkmark bounding box.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="sz">Checkmark size in pixels.</param>
    public void RenderCheckMark(ImVec2 pos, uint col, float sz)
    {
        float thickness = MathF.Max(sz / 5.0f, 1.0f);
        sz -= thickness * 0.5f;
        pos += new ImVec2(thickness * 0.25f, thickness * 0.25f);

        float third = sz / 3.0f;
        float bx = pos.X + third;
        float by = pos.Y + sz - third * 0.5f;
        PathLineTo(new ImVec2(bx - third, by - third));
        PathLineTo(new ImVec2(bx, by));
        PathLineTo(new ImVec2(bx + third * 2.0f, by - third * 2.0f));
        PathStroke(col, thickness);
    }

    /// <summary>
    /// Renders an arrow pointing at a specified direction conforming to canonical Dear ImGui.
    /// </summary>
    /// <param name="pos">Triangle tip position.</param>
    /// <param name="halfSz">Half-size of the triangle along each axis.</param>
    /// <param name="direction">Direction the arrow points.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    public void RenderArrowPointingAt(ImVec2 pos, ImVec2 halfSz, ImGuiDir direction, uint col)
    {
        switch (direction)
        {
            case ImGuiDir.Left:
                AddTriangleFilled(new ImVec2(pos.X + halfSz.X, pos.Y - halfSz.Y), new ImVec2(pos.X + halfSz.X, pos.Y + halfSz.Y), pos, col);
                break;
            case ImGuiDir.Right:
                AddTriangleFilled(new ImVec2(pos.X - halfSz.X, pos.Y + halfSz.Y), new ImVec2(pos.X - halfSz.X, pos.Y - halfSz.Y), pos, col);
                break;
            case ImGuiDir.Up:
                AddTriangleFilled(new ImVec2(pos.X + halfSz.X, pos.Y + halfSz.Y), new ImVec2(pos.X - halfSz.X, pos.Y + halfSz.Y), pos, col);
                break;
            case ImGuiDir.Down:
            default:
                AddTriangleFilled(new ImVec2(pos.X - halfSz.X, pos.Y - halfSz.Y), new ImVec2(pos.X + halfSz.X, pos.Y - halfSz.Y), pos, col);
                break;
        }
    }

    /// <summary>Emits a circle outline using the canonical automatic or explicit segment count.</summary>
    /// <param name="center">Circle center.</param>
    /// <param name="radius">Circle radius in pixels.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="numSegments">Segment count; zero selects an automatic count.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddCircle(ImVec2 center, float radius, uint col, int numSegments = 0, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000u) == 0)
            return;

        flags |= Flags;
        ImDrawFlags strokePos = _GetStrokePos(flags, ImDrawFlags.StrokeInside);
        if (strokePos == ImDrawFlags.StrokeLegacy)
        {
            radius -= 0.5f;
            strokePos = ImDrawFlags.StrokeCenter;
        }
        radius = MathF.Max(0.01f, radius);

        float outerRadius = radius;
        if (strokePos == ImDrawFlags.StrokeCenter)
            outerRadius = radius + thickness * 0.5f;
        else if (strokePos == ImDrawFlags.StrokeCenterBiased)
            outerRadius = radius + _CalculateCenterBiasedOffset(thickness);
        else if (strokePos == ImDrawFlags.StrokeInside)
            outerRadius = radius;
        else if (strokePos == ImDrawFlags.StrokeOutside)
            outerRadius = radius + thickness;

        if (outerRadius <= thickness)
        {
            AddCircleFilled(center, outerRadius, col, numSegments);
            return;
        }

        if (numSegments <= 0 && outerRadius <= _arcFastRadiusCutoff)
        {
            int aStep = ArcFastSampleMax / CalcCircleAutoSegmentCount(outerRadius);
            PathArcToFastEx(center, radius, 0, ArcFastSampleMax, aStep);
            _path.RemoveAt(_path.Count - 1);
        }
        else
        {
            if (numSegments <= 0)
                numSegments = CalcCircleAutoSegmentCount(outerRadius);
            numSegments = Math.Clamp(numSegments, 3, CircleAutoSegmentMax);
            float aMax = (MathF.PI * 2.0f) * ((float)numSegments - 1.0f) / (float)numSegments;
            PathArcTo(center, radius, 0.0f, aMax, numSegments - 1);
        }

        if (_path.Count >= 3)
        {
            float unitApothem = ApproxApothem(_path.Count);
            float edgeDist = radius * unitApothem;
            _AddPolyline(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_path), _path.Count, col, thickness, ImDrawFlags.Closed | ImDrawFlags.JoinMiter | strokePos | (flags & ~ImDrawFlags.StrokeMask_), edgeDist);
        }
        _path.Clear();
    }

    /// <summary>Emits a filled circle using the canonical automatic or explicit segment count.</summary>
    /// <param name="center">Circle center.</param>
    /// <param name="radius">Circle radius in pixels.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="numSegments">Segment count; zero selects an automatic count.</param>
    public void AddCircleFilled(ImVec2 center, float radius, uint col, int numSegments = 0)
    {
        if ((col & 0xFF000000) == 0 || radius < 0.5f)
            return;

        // Ports ImDrawList::AddCircleFilled. A filled circle uses the full
        // radius because no stroke inset applies.
        if (numSegments <= 0)
        {
            PathArcToFastEx(center, radius, 0, ArcFastSampleMax, 0);
            _path.RemoveAt(_path.Count - 1);
        }
        else
        {
            numSegments = Math.Clamp(numSegments, 3, CircleAutoSegmentMax);
            float aMax = (MathF.PI * 2.0f) * (numSegments - 1.0f) / numSegments;
            PathArcTo(center, radius, 0.0f, aMax, numSegments - 1);
        }

        PathFillConvex(col);
    }

    /// <summary>Emits a regular polygon outline that honors the requested segment count.</summary>
    /// <param name="center">Polygon center.</param>
    /// <param name="radius">Circumscribed radius in pixels.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="numSegments">Number of polygon sides; values below three are ignored.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddNgon(ImVec2 center, float radius, uint col, int numSegments, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000u) == 0 || numSegments <= 2)
            return;

        flags |= Flags;
        ImDrawFlags strokePos = _GetStrokePos(flags, ImDrawFlags.StrokeInside);
        if (strokePos == ImDrawFlags.StrokeLegacy)
        {
            radius -= 0.5f;
            strokePos = ImDrawFlags.StrokeCenter;
        }
        radius = MathF.Max(0.01f, radius);

        float unitApothem = ApproxApothem(numSegments);
        float edgeDist = radius * unitApothem;

        if (radius < thickness * 2.0f)
        {
            float outerRadius = radius;
            if (strokePos == ImDrawFlags.StrokeCenter)
                outerRadius = radius + thickness * 0.5f;
            else if (strokePos == ImDrawFlags.StrokeCenterBiased)
                outerRadius = radius + _CalculateCenterBiasedOffset(thickness);
            else if (strokePos == ImDrawFlags.StrokeInside)
                outerRadius = radius;
            else if (strokePos == ImDrawFlags.StrokeOutside)
                outerRadius = radius + thickness;

            if (outerRadius * unitApothem <= thickness)
            {
                AddNgonFilled(center, outerRadius, col, numSegments);
                return;
            }
        }

        float aMax = (MathF.PI * 2.0f) * (numSegments - 1.0f) / numSegments;
        PathArcTo(center, radius, 0.0f, aMax, numSegments - 1);
        _AddPolyline(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_path), _path.Count, col, thickness, ImDrawFlags.Closed | ImDrawFlags.JoinMiter | strokePos | (flags & ~ImDrawFlags.StrokeMask_), edgeDist);
        _path.Clear();
    }

    /// <summary>Emits a filled regular polygon that honors the requested segment count.</summary>
    /// <param name="center">Polygon center.</param>
    /// <param name="radius">Circumscribed radius in pixels.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="numSegments">Number of polygon sides; values below three are ignored.</param>
    public void AddNgonFilled(ImVec2 center, float radius, uint col, int numSegments)
    {
        if ((col & 0xFF000000) == 0 || numSegments <= 2)
            return;
        float aMax = (MathF.PI * 2.0f) * (numSegments - 1.0f) / numSegments;
        PathArcTo(center, radius, 0.0f, aMax, numSegments - 1);
        PathFillConvex(col);
    }

    /// <summary>Emits an ellipse outline using the canonical automatic or explicit segment count.</summary>
    /// <param name="center">Ellipse center.</param>
    /// <param name="radius">Horizontal and vertical radii in pixels.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="rot">Rotation of the ellipse axes in radians.</param>
    /// <param name="numSegments">Segment count; zero selects an automatic count.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddEllipse(ImVec2 center, ImVec2 radius, uint col, float rot = 0.0f, int numSegments = 0, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        if (numSegments <= 0)
            numSegments = CalcCircleAutoSegmentCount(MathF.Max(radius.X, radius.Y));
        float aMax = MathF.PI * 2.0f * (numSegments - 1.0f) / numSegments;
        PathEllipticalArcTo(center, radius, rot, 0.0f, aMax, numSegments - 1);
        PathStroke(col, thickness, ImDrawFlags.Closed | flags);
    }

    /// <summary>Emits a filled ellipse using the canonical automatic or explicit segment count.</summary>
    /// <param name="center">Ellipse center.</param>
    /// <param name="radius">Horizontal and vertical radii in pixels.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="rot">Rotation of the ellipse axes in radians.</param>
    /// <param name="numSegments">Segment count; zero selects an automatic count.</param>
    public void AddEllipseFilled(ImVec2 center, ImVec2 radius, uint col, float rot = 0.0f, int numSegments = 0)
    {
        if ((col & 0xFF000000) == 0)
            return;
        if (numSegments <= 0)
            numSegments = CalcCircleAutoSegmentCount(MathF.Max(radius.X, radius.Y));
        float aMax = MathF.PI * 2.0f * (numSegments - 1.0f) / numSegments;
        PathEllipticalArcTo(center, radius, rot, 0.0f, aMax, numSegments - 1);
        PathFillConvex(col);
    }

    /// <summary>Emits an anti-aliased cubic Bezier curve.</summary>
    /// <param name="p1">Start point.</param>
    /// <param name="p2">First control point.</param>
    /// <param name="p3">Second control point.</param>
    /// <param name="p4">End point.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Curve thickness in pixels.</param>
    /// <param name="numSegments">Segment count; zero auto-tessellates within the curve tolerance.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddBezierCubic(ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4, uint col, float thickness, int numSegments = 0, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        PathLineTo(p1);
        PathBezierCubicCurveTo(p2, p3, p4, numSegments);
        PathStroke(col, thickness, flags);
    }

    /// <summary>Emits an anti-aliased quadratic Bezier curve.</summary>
    /// <param name="p1">Start point.</param>
    /// <param name="p2">Control point.</param>
    /// <param name="p3">End point.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Curve thickness in pixels.</param>
    /// <param name="numSegments">Segment count; zero auto-tessellates within the curve tolerance.</param>
    /// <param name="flags">Drawing and stroke position flags.</param>
    public void AddBezierQuadratic(ImVec2 p1, ImVec2 p2, ImVec2 p3, uint col, float thickness, int numSegments = 0, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        PathLineTo(p1);
        PathBezierQuadraticCurveTo(p2, p3, numSegments);
        PathStroke(col, thickness, flags);
    }

    /// <summary>Emits a textured quad displaying the full unit UV range of an image.</summary>
    /// <param name="texRef">Texture reference to bind while drawing.</param>
    /// <param name="pMin">Top-left corner.</param>
    /// <param name="pMax">Bottom-right corner.</param>
    public void AddImage(ImTextureRef texRef, ImVec2 pMin, ImVec2 pMax)
    {
        AddImage(texRef, pMin, pMax, ImVec2.Zero, ImVec2.One, 0xFFFFFFFF);
    }

    /// <summary>Emits a textured quad for a backend image.</summary>
    /// <param name="texRef">Texture reference to bind while drawing.</param>
    /// <param name="pMin">Top-left corner.</param>
    /// <param name="pMax">Bottom-right corner.</param>
    /// <param name="uvMin">Top-left texture coordinate.</param>
    /// <param name="uvMax">Bottom-right texture coordinate.</param>
    /// <param name="col">Packed tint color in canonical ABGR order.</param>
    public void AddImage(ImTextureRef texRef, ImVec2 pMin, ImVec2 pMax, ImVec2 uvMin, ImVec2 uvMax, uint col = 0xFFFFFFFF)
    {
        if ((col & 0xFF000000) == 0)
            return;
        // Ports ImDrawList::AddImage.: the texture is only pushed when it
        // differs from the active one so adjacent images batch into a single command.
        bool pushTexture = !TexRefEquals(texRef, _cmdHeaderTexRef);
        if (pushTexture)
            PushTexture(texRef);

        PrimReserve(6, 4);
        PrimRectUV(pMin, pMax, uvMin, uvMax, col);

        if (pushTexture)
            PopTexture();
    }

    /// <summary>Emits a textured quad displaying the full unit UV range of an image on four corners.</summary>
    /// <param name="texRef">Texture reference to bind while drawing.</param>
    /// <param name="p1">First corner.</param>
    /// <param name="p2">Second corner.</param>
    /// <param name="p3">Third corner.</param>
    /// <param name="p4">Fourth corner.</param>
    public void AddImageQuad(ImTextureRef texRef, ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4)
    {
        AddImageQuad(texRef, p1, p2, p3, p4, ImVec2.Zero, new ImVec2(1, 0), ImVec2.One, new ImVec2(0, 1), 0xFFFFFFFF);
    }

    /// <summary>Emits a textured quad from four corners and their texture coordinates.</summary>
    /// <param name="texRef">Texture reference to bind while drawing.</param>
    /// <param name="p1">First corner.</param>
    /// <param name="p2">Second corner.</param>
    /// <param name="p3">Third corner.</param>
    /// <param name="p4">Fourth corner.</param>
    /// <param name="uv1">Texture coordinate at <paramref name="p1"/>.</param>
    /// <param name="uv2">Texture coordinate at <paramref name="p2"/>.</param>
    /// <param name="uv3">Texture coordinate at <paramref name="p3"/>.</param>
    /// <param name="uv4">Texture coordinate at <paramref name="p4"/>.</param>
    /// <param name="col">Packed tint color in canonical ABGR order.</param>
    public void AddImageQuad(ImTextureRef texRef, ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4, ImVec2 uv1, ImVec2 uv2, ImVec2 uv3, ImVec2 uv4, uint col = 0xFFFFFFFF)
    {
        if ((col & 0xFF000000) == 0)
            return;
        bool pushTexture = !TexRefEquals(texRef, _cmdHeaderTexRef);
        if (pushTexture)
            PushTexture(texRef);

        PrimReserve(6, 4);
        PrimQuadUV(p1, p2, p3, p4, uv1, uv2, uv3, uv4, col);

        if (pushTexture)
            PopTexture();
    }

    /// <summary>Emits a rounded textured rectangle by shading the filled polygon's UVs.</summary>
    /// <param name="texRef">Texture reference to bind while drawing.</param>
    /// <param name="pMin">Top-left corner.</param>
    /// <param name="pMax">Bottom-right corner.</param>
    /// <param name="uvMin">Top-left texture coordinate.</param>
    /// <param name="uvMax">Bottom-right texture coordinate.</param>
    /// <param name="col">Packed tint color in canonical ABGR order.</param>
    /// <param name="rounding">Corner rounding radius in pixels.</param>
    /// <param name="flags">Corner selection flags.</param>
    public void AddImageRounded(ImTextureRef texRef, ImVec2 pMin, ImVec2 pMax, ImVec2 uvMin, ImVec2 uvMax, uint col, float rounding, ImDrawFlags flags = ImDrawFlags.None)
    {
        if ((col & 0xFF000000) == 0)
            return;
        if ((flags & ImDrawFlags.RoundCornersMask_) == 0)
            flags |= ImDrawFlags.RoundCornersAll;

        // Ports ImDrawList::AddImageRounded. Rounded rectangles are produced
        // by generating the rounded polygon and then projecting the image UVs across its bounds.
        if (rounding < 0.5f || (flags & ImDrawFlags.RoundCornersMask_) == ImDrawFlags.RoundCornersNone)
        {
            AddImage(texRef, pMin, pMax, uvMin, uvMax, col);
            return;
        }

        bool pushTexture = !TexRefEquals(texRef, _cmdHeaderTexRef);
        if (pushTexture)
            PushTexture(texRef);

        int vertStart = VtxBuffer.Count;
        PathRect(pMin, pMax, rounding, flags);
        PathFillConvex(col);
        int vertEnd = VtxBuffer.Count;
        ShadeVertsLinearUV(vertStart, vertEnd, pMin, pMax, uvMin, uvMax, true);

        if (pushTexture)
            PopTexture();
    }

    /// <summary>
    /// Reprojects the UVs of a vertex range against a linear mapping between two rectangle corners.
    /// Ports canonical <c>ImGui::ShadeVertsLinearUV</c>.
    /// </summary>
    /// <param name="vertStartIdx">First vertex index in the vertex buffer.</param>
    /// <param name="vertEndIdx">One past the last vertex index.</param>
    /// <param name="a">Rectangle corner mapped to <paramref name="uvA"/>.</param>
    /// <param name="b">Rectangle corner mapped to <paramref name="uvB"/>.</param>
    /// <param name="uvA">Texture coordinate at <paramref name="a"/>.</param>
    /// <param name="uvB">Texture coordinate at <paramref name="b"/>.</param>
    /// <param name="clamp">When true, UVs are clamped to the rectangle defined by the two UV corners.</param>
    public void ShadeVertsLinearUV(int vertStartIdx, int vertEndIdx, ImVec2 a, ImVec2 b, ImVec2 uvA, ImVec2 uvB, bool clamp)
    {
        ImVec2 size = b - a;
        ImVec2 uvSize = uvB - uvA;
        ImVec2 scale = new(
            size.X != 0.0f ? uvSize.X / size.X : 0.0f,
            size.Y != 0.0f ? uvSize.Y / size.Y : 0.0f);

        ImVec2 min = new(MathF.Min(uvA.X, uvB.X), MathF.Min(uvA.Y, uvB.Y));
        ImVec2 max = new(MathF.Max(uvA.X, uvB.X), MathF.Max(uvA.Y, uvB.Y));

        for (int i = vertStartIdx; i < vertEndIdx; i++)
        {
            ImDrawVert vertex = VtxBuffer[i];
            ImVec2 uv = new(
                uvA.X + ((vertex.Pos.X - a.X) * scale.X),
                uvA.Y + ((vertex.Pos.Y - a.Y) * scale.Y));
            if (clamp)
            {
                uv = new ImVec2(
                    Math.Clamp(uv.X, min.X, max.X),
                    Math.Clamp(uv.Y, min.Y, max.Y));
            }
            vertex.UV = uv;
            VtxBuffer[i] = vertex;
        }
    }

    /// <summary>Emits text using the atlas default font at its reference size.</summary>
    /// <param name="pos">Top-left text position.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="text">Text to render.</param>
    public void AddText(ImVec2 pos, uint col, ReadOnlySpan<char> text)
    {
        if ((col & 0xFF000000) == 0 || text.IsEmpty) return;
        var font = _sharedAtlas?.Fonts.Count > 0 ? _sharedAtlas.Fonts[0] : null;
        if (font != null)
        {
            AddText(font, font.LegacySize, pos, col, text);
        }
    }

    /// <summary>Emits text with an explicit font, size, wrap width and optional fine clip.</summary>
    /// <param name="font">Font used to rasterize glyph quads.</param>
    /// <param name="fontSize">Line height in pixels.</param>
    /// <param name="pos">Top-left text position.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="text">Text to render.</param>
    /// <param name="wrapWidth">Word-wrap width, or zero to disable wrapping.</param>
    /// <param name="cpuFineClipRect">Optional CPU-side clip rectangle for precise glyph clipping.</param>
    public void AddText(ImFont font, float fontSize, ImVec2 pos, uint col, ReadOnlySpan<char> text, float wrapWidth = 0f, ImVec4? cpuFineClipRect = null)
    {
        if ((col & 0xFF000000) == 0 || text.IsEmpty) return;

        ImVec4 clip = cpuFineClipRect ?? _cmdHeaderClipRect;
        font.RenderText(this, fontSize, pos, col, clip, text, wrapWidth, cpuFineClipRect.HasValue);
    }

    #endregion

    #region Path Operations

    /// <summary>Clears the current path.</summary>
    public void PathClear()
    {
        _path.Clear();
    }

    /// <summary>Appends a point to the current path.</summary>
    /// <param name="pos">Point to append.</param>
    public void PathLineTo(ImVec2 pos)
    {
        _path.Add(pos);
    }

    /// <summary>Fills the current path as a convex polygon and clears it.</summary>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="flags">Draw flags for anti-aliasing.</param>
    public void PathFillConvex(uint col, ImDrawFlags flags = ImDrawFlags.None)
    {
        // Ports the canonical inline PathFillConvex.
        AddConvexPolyFilled(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_path), col, flags);
        _path.Clear();
    }

    /// <summary>Fills the current path as a concave polygon and clears it.</summary>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    public void PathFillConcave(uint col)
    {
        // Ports the canonical inline PathFillConcave.
        AddConcavePolyFilled(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_path), col);
        _path.Clear();
    }

    /// <summary>Strokes the current path as connected line segments and clears it.</summary>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="thickness">Stroke thickness in pixels.</param>
    /// <param name="flags">Path flags; <c>Closed</c> connects the last point to the first.</param>
    public void PathStroke(uint col, float thickness = 1.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        // Ports the canonical inline PathStroke.: the path is routed through AddPolyline
        // so all strokes share the anti-aliasing and fringe behavior.
        AddPolyline(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_path), col, thickness, flags);
        _path.Clear();
    }

    /// <summary>Strokes the current path using the obsolete flags-first parameter order.</summary>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="flags">Path flags; <c>Closed</c> connects the last point to the first.</param>
    /// <param name="thickness">Stroke thickness in pixels.</param>
    public void PathStroke(uint col, ImDrawFlags flags, float thickness) => PathStroke(col, thickness, flags);

    /// <summary>Appends an arc approximated with straight segments.</summary>
    /// <param name="center">Arc center.</param>
    /// <param name="radius">Arc radius in pixels.</param>
    /// <param name="aMin">Start angle in radians.</param>
    /// <param name="aMax">End angle in radians.</param>
    /// <param name="numSegments">Number of straight segments; zero selects an automatic count.</param>
    public void PathArcTo(ImVec2 center, float radius, float aMin, float aMax, int numSegments = 0)
    {
        // Ports ImDrawList::PathArcTo. Small radii use the precomputed fast
        // table; larger radii fall back to the explicit trigonometric loop.
        if (radius < 0.5f)
        {
            _path.Add(center);
            return;
        }

        if (numSegments > 0)
        {
            PathArcToN(center, radius, aMin, aMax, numSegments);
            return;
        }

        if (radius <= _arcFastRadiusCutoff)
        {
            bool aIsReverse = aMax < aMin;

            float aMinSampleF = ArcFastSampleMax * aMin / (MathF.PI * 2.0f);
            float aMaxSampleF = ArcFastSampleMax * aMax / (MathF.PI * 2.0f);

            int aMinSample = aIsReverse ? (int)MathF.Floor(aMinSampleF) : (int)MathF.Ceiling(aMinSampleF);
            int aMaxSample = aIsReverse ? (int)MathF.Ceiling(aMaxSampleF) : (int)MathF.Floor(aMaxSampleF);
            int aMidSamples = aIsReverse ? Math.Max(aMinSample - aMaxSample, 0) : Math.Max(aMaxSample - aMinSample, 0);

            float aMinSegmentAngle = aMinSample * MathF.PI * 2.0f / ArcFastSampleMax;
            float aMaxSegmentAngle = aMaxSample * MathF.PI * 2.0f / ArcFastSampleMax;
            bool aEmitStart = MathF.Abs(aMinSegmentAngle - aMin) >= 1e-5f;
            bool aEmitEnd = MathF.Abs(aMax - aMaxSegmentAngle) >= 1e-5f;

            if (aEmitStart)
                _path.Add(new ImVec2(center.X + MathF.Cos(aMin) * radius, center.Y + MathF.Sin(aMin) * radius));
            if (aMidSamples > 0)
                PathArcToFastEx(center, radius, aMinSample, aMaxSample, 0);
            if (aEmitEnd)
                _path.Add(new ImVec2(center.X + MathF.Cos(aMax) * radius, center.Y + MathF.Sin(aMax) * radius));
        }
        else
        {
            float arcLength = MathF.Abs(aMax - aMin);
            int circleSegmentCount = CalcCircleAutoSegmentCount(radius);
            int arcSegmentCount = Math.Max((int)MathF.Ceiling(circleSegmentCount * arcLength / (MathF.PI * 2.0f)), 1);
            PathArcToN(center, radius, aMin, aMax, arcSegmentCount);
        }
    }

    /// <summary>Appends an arc divided into an explicit number of equal segments.</summary>
    /// <param name="center">Arc center.</param>
    /// <param name="radius">Arc radius in pixels.</param>
    /// <param name="aMin">Start angle in radians.</param>
    /// <param name="aMax">End angle in radians.</param>
    /// <param name="numSegments">Number of straight segments; both end points are emitted.</param>
    public void PathArcToN(ImVec2 center, float radius, float aMin, float aMax, int numSegments)
    {
        // Ports ImDrawList::_PathArcToN.
        if (radius < 0.5f)
        {
            _path.Add(center);
            return;
        }

        for (int i = 0; i <= numSegments; i++)
        {
            float a = aMin + ((float)i / numSegments) * (aMax - aMin);
            _path.Add(new ImVec2(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius));
        }
    }

    /// <summary>Canonical alias for <see cref="PathArcToN"/>.</summary>
    public void _PathArcToN(ImVec2 center, float radius, float aMin, float aMax, int numSegments) => PathArcToN(center, radius, aMin, aMax, numSegments);

    /// <summary>Appends an arc sampled from the precomputed table using 12 step angles.</summary>
    /// <param name="center">Arc center.</param>
    /// <param name="radius">Arc radius in pixels.</param>
    /// <param name="aMinOf12">Start sample expressed in twelfths of a turn.</param>
    /// <param name="aMaxOf12">End sample expressed in twelfths of a turn.</param>
    public void PathArcToFast(ImVec2 center, float radius, int aMinOf12, int aMaxOf12)
    {
        // Ports ImDrawList::PathArcToFast.
        if (radius < 0.5f)
        {
            _path.Add(center);
            return;
        }
        PathArcToFastEx(center, radius, aMinOf12 * ArcFastSampleMax / 12, aMaxOf12 * ArcFastSampleMax / 12, 0);
    }

    /// <summary>
    /// Appends an arc sampled from the precomputed table between two sample indices.
    /// Ports canonical <c>ImDrawList::_PathArcToFastEx</c>.
    /// </summary>
    /// <param name="center">Arc center.</param>
    /// <param name="radius">Arc radius in pixels.</param>
    /// <param name="aMinSample">First sample index.</param>
    /// <param name="aMaxSample">Last sample index.</param>
    /// <param name="aStep">Step between samples; zero selects an automatic step.</param>
    private void PathArcToFastEx(ImVec2 center, float radius, int aMinSample, int aMaxSample, int aStep)
    {
        if (radius < 0.5f)
        {
            _path.Add(center);
            return;
        }

        if (aStep <= 0)
            aStep = ArcFastSampleMax / CalcCircleAutoSegmentCount(radius);

        // Never step more than a quarter of the circle or the table wraps more than once.
        aStep = Math.Clamp(aStep, 1, ArcFastTableSize / 4);

        int sampleRange = Math.Abs(aMaxSample - aMinSample);
        int aNextStep = aStep;
        bool extraMaxSample = false;
        if (aStep > 1)
        {
            int overstep = sampleRange % aStep;
            if (overstep > 0)
            {
                extraMaxSample = true;
                // Spread the remainder across the first step so the arc does not end in a tiny segment.
                if (sampleRange > 0)
                    aStep -= (aStep - overstep) / 2;
            }
        }

        int sampleIndex = aMinSample;
        if (sampleIndex < 0 || sampleIndex >= ArcFastSampleMax)
        {
            sampleIndex %= ArcFastSampleMax;
            if (sampleIndex < 0)
                sampleIndex += ArcFastSampleMax;
        }

        if (aMaxSample >= aMinSample)
        {
            for (int a = aMinSample; a <= aMaxSample; a += aStep, sampleIndex += aStep, aStep = aNextStep)
            {
                if (sampleIndex >= ArcFastSampleMax)
                    sampleIndex -= ArcFastSampleMax;
                ImVec2 s = _arcFastVtx[sampleIndex];
                _path.Add(new ImVec2(center.X + (s.X * radius), center.Y + (s.Y * radius)));
            }
        }
        else
        {
            for (int a = aMinSample; a >= aMaxSample; a -= aStep, sampleIndex -= aStep, aStep = aNextStep)
            {
                if (sampleIndex < 0)
                    sampleIndex += ArcFastSampleMax;
                ImVec2 s = _arcFastVtx[sampleIndex];
                _path.Add(new ImVec2(center.X + (s.X * radius), center.Y + (s.Y * radius)));
            }
        }

        if (extraMaxSample)
        {
            int normalizedMaxSample = aMaxSample % ArcFastSampleMax;
            if (normalizedMaxSample < 0)
                normalizedMaxSample += ArcFastSampleMax;
            ImVec2 s = _arcFastVtx[normalizedMaxSample];
            _path.Add(new ImVec2(center.X + (s.X * radius), center.Y + (s.Y * radius)));
        }
    }

    /// <summary>Appends an ellipse arc approximated with straight segments.</summary>
    /// <param name="center">Ellipse center.</param>
    /// <param name="radius">Horizontal and vertical radii in pixels.</param>
    /// <param name="rot">Rotation of the ellipse axes in radians.</param>
    /// <param name="aMin">Start angle in radians.</param>
    /// <param name="aMax">End angle in radians.</param>
    /// <param name="numSegments">Number of straight segments; zero selects an automatic count.</param>
    public void PathEllipticalArcTo(ImVec2 center, ImVec2 radius, float rot, float aMin, float aMax, int numSegments = 0)
    {
        // Ports ImDrawList::PathEllipticalArcTo.
        if (numSegments <= 0)
            numSegments = CalcCircleAutoSegmentCount(MathF.Max(radius.X, radius.Y));

        float cosRot = MathF.Cos(rot);
        float sinRot = MathF.Sin(rot);
        for (int i = 0; i <= numSegments; i++)
        {
            float a = aMin + ((float)i / numSegments) * (aMax - aMin);
            ImVec2 point = new(MathF.Cos(a) * radius.X, MathF.Sin(a) * radius.Y);
            ImVec2 rel = new((point.X * cosRot) - (point.Y * sinRot), (point.X * sinRot) + (point.Y * cosRot));
            _path.Add(new ImVec2(rel.X + center.X, rel.Y + center.Y));
        }
    }

    /// <summary>Appends a cubic Bezier curve to the current path.</summary>
    /// <param name="p2">First control point.</param>
    /// <param name="p3">Second control point.</param>
    /// <param name="p4">End point.</param>
    /// <param name="numSegments">Segment count; zero auto-tessellates within the curve tolerance.</param>
    public void PathBezierCubicCurveTo(ImVec2 p2, ImVec2 p3, ImVec2 p4, int numSegments = 0)
    {
        // Ports ImDrawList::PathBezierCubicCurveTo.
        ImVec2 p1 = _path[^1];
        if (numSegments == 0)
        {
            PathBezierCubicCurveToCasteljau(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, p4.X, p4.Y, _curveTessellationTol, 0);
        }
        else
        {
            float tStep = 1.0f / numSegments;
            for (int iStep = 1; iStep <= numSegments; iStep++)
                _path.Add(BezierCubicCalc(p1, p2, p3, p4, tStep * iStep));
        }
    }

    /// <summary>Appends a quadratic Bezier curve to the current path.</summary>
    /// <param name="p2">Control point.</param>
    /// <param name="p3">End point.</param>
    /// <param name="numSegments">Segment count; zero auto-tessellates within the curve tolerance.</param>
    public void PathBezierQuadraticCurveTo(ImVec2 p2, ImVec2 p3, int numSegments = 0)
    {
        // Ports ImDrawList::PathBezierQuadraticCurveTo.
        ImVec2 p1 = _path[^1];
        if (numSegments == 0)
        {
            PathBezierQuadraticCurveToCasteljau(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, _curveTessellationTol, 0);
        }
        else
        {
            float tStep = 1.0f / numSegments;
            for (int iStep = 1; iStep <= numSegments; iStep++)
                _path.Add(BezierQuadraticCalc(p1, p2, p3, tStep * iStep));
        }
    }

    /// <summary>Evaluates a cubic Bezier at a parameter.</summary>
    /// <param name="p1">Start point.</param>
    /// <param name="p2">First control point.</param>
    /// <param name="p3">Second control point.</param>
    /// <param name="p4">End point.</param>
    /// <param name="t">Curve parameter in the range zero to one.</param>
    /// <returns>The interpolated point.</returns>
    private static ImVec2 BezierCubicCalc(ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4, float t)
    {
        float u = 1.0f - t;
        float w1 = u * u * u;
        float w2 = 3 * u * u * t;
        float w3 = 3 * u * t * t;
        float w4 = t * t * t;
        return new ImVec2(
            (w1 * p1.X) + (w2 * p2.X) + (w3 * p3.X) + (w4 * p4.X),
            (w1 * p1.Y) + (w2 * p2.Y) + (w3 * p3.Y) + (w4 * p4.Y));
    }

    /// <summary>Evaluates a quadratic Bezier at a parameter.</summary>
    /// <param name="p1">Start point.</param>
    /// <param name="p2">Control point.</param>
    /// <param name="p3">End point.</param>
    /// <param name="t">Curve parameter in the range zero to one.</param>
    /// <returns>The interpolated point.</returns>
    private static ImVec2 BezierQuadraticCalc(ImVec2 p1, ImVec2 p2, ImVec2 p3, float t)
    {
        float u = 1.0f - t;
        float w1 = u * u;
        float w2 = 2 * u * t;
        float w3 = t * t;
        return new ImVec2(
            (w1 * p1.X) + (w2 * p2.X) + (w3 * p3.X),
            (w1 * p1.Y) + (w2 * p2.Y) + (w3 * p3.Y));
    }

    /// <summary>
    /// Recursively subdivides a cubic Bezier until the control polygon flattens within tolerance.
    /// Ports <c>PathBezierCubicCurveToCasteljau</c>.
    /// </summary>
    /// <param name="x1">Start point X.</param>
    /// <param name="y1">Start point Y.</param>
    /// <param name="x2">First control point X.</param>
    /// <param name="y2">First control point Y.</param>
    /// <param name="x3">Second control point X.</param>
    /// <param name="y3">Second control point Y.</param>
    /// <param name="x4">End point X.</param>
    /// <param name="y4">End point Y.</param>
    /// <param name="tessTol">Squared flatness tolerance.</param>
    /// <param name="level">Current recursion depth; capped at ten.</param>
    private void PathBezierCubicCurveToCasteljau(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, float tessTol, int level)
    {
        float dx = x4 - x1;
        float dy = y4 - y1;
        float d2 = ((x2 - x4) * dy) - ((y2 - y4) * dx);
        float d3 = ((x3 - x4) * dy) - ((y3 - y4) * dx);
        d2 = d2 >= 0 ? d2 : -d2;
        d3 = d3 >= 0 ? d3 : -d3;
        if (((d2 + d3) * (d2 + d3)) < tessTol * ((dx * dx) + (dy * dy)))
        {
            _path.Add(new ImVec2(x4, y4));
        }
        else if (level < 10)
        {
            float x12 = (x1 + x2) * 0.5f, y12 = (y1 + y2) * 0.5f;
            float x23 = (x2 + x3) * 0.5f, y23 = (y2 + y3) * 0.5f;
            float x34 = (x3 + x4) * 0.5f, y34 = (y3 + y4) * 0.5f;
            float x123 = (x12 + x23) * 0.5f, y123 = (y12 + y23) * 0.5f;
            float x234 = (x23 + x34) * 0.5f, y234 = (y23 + y34) * 0.5f;
            float x1234 = (x123 + x234) * 0.5f, y1234 = (y123 + y234) * 0.5f;
            PathBezierCubicCurveToCasteljau(x1, y1, x12, y12, x123, y123, x1234, y1234, tessTol, level + 1);
            PathBezierCubicCurveToCasteljau(x1234, y1234, x234, y234, x34, y34, x4, y4, tessTol, level + 1);
        }
    }

    /// <summary>
    /// Recursively subdivides a quadratic Bezier until the control polygon flattens within tolerance.
    /// Ports <c>PathBezierQuadraticCurveToCasteljau</c>.
    /// </summary>
    /// <param name="x1">Start point X.</param>
    /// <param name="y1">Start point Y.</param>
    /// <param name="x2">Control point X.</param>
    /// <param name="y2">Control point Y.</param>
    /// <param name="x3">End point X.</param>
    /// <param name="y3">End point Y.</param>
    /// <param name="tessTol">Squared flatness tolerance.</param>
    /// <param name="level">Current recursion depth; capped at ten.</param>
    private void PathBezierQuadraticCurveToCasteljau(float x1, float y1, float x2, float y2, float x3, float y3, float tessTol, int level)
    {
        float dx = x3 - x1, dy = y3 - y1;
        float det = ((x2 - x3) * dy) - ((y2 - y3) * dx);
        if ((det * det * 4.0f) < tessTol * ((dx * dx) + (dy * dy)))
        {
            _path.Add(new ImVec2(x3, y3));
        }
        else if (level < 10)
        {
            float x12 = (x1 + x2) * 0.5f, y12 = (y1 + y2) * 0.5f;
            float x23 = (x2 + x3) * 0.5f, y23 = (y2 + y3) * 0.5f;
            float x123 = (x12 + x23) * 0.5f, y123 = (y12 + y23) * 0.5f;
            PathBezierQuadraticCurveToCasteljau(x1, y1, x12, y12, x123, y123, tessTol, level + 1);
            PathBezierQuadraticCurveToCasteljau(x123, y123, x23, y23, x3, y3, tessTol, level + 1);
        }
    }

    /// <summary>Appends a rectangle, optionally rounded, to the current path.</summary>
    /// <param name="rectMin">Top-left corner.</param>
    /// <param name="rectMax">Bottom-right corner.</param>
    /// <param name="rounding">Corner rounding radius in pixels.</param>
    /// <param name="flags">Corner selection flags.</param>
    public void PathRect(ImVec2 rectMin, ImVec2 rectMax, float rounding = 0.0f, ImDrawFlags flags = ImDrawFlags.None)
    {
        // Ports ImDrawList::PathRect. Rounding below half a pixel is
        // downgraded to sharp corners; otherwise the radius is clamped against the shorter span and
        // corners are emitted clockwise from the top-left arc.
        if (rounding >= 0.5f)
        {
            if ((flags & ImDrawFlags.RoundCornersMask_) == 0)
                flags |= ImDrawFlags.RoundCornersAll;

            float widthLimit = MathF.Abs(rectMax.X - rectMin.X);
            float heightLimit = MathF.Abs(rectMax.Y - rectMin.Y);
            bool horizontalRounded = ((flags & ImDrawFlags.RoundCornersTop) == ImDrawFlags.RoundCornersTop) || ((flags & ImDrawFlags.RoundCornersBottom) == ImDrawFlags.RoundCornersBottom);
            bool verticalRounded = ((flags & ImDrawFlags.RoundCornersLeft) == ImDrawFlags.RoundCornersLeft) || ((flags & ImDrawFlags.RoundCornersRight) == ImDrawFlags.RoundCornersRight);
            rounding = MathF.Min(rounding, (widthLimit * (horizontalRounded ? 0.5f : 1.0f)) - 1.0f);
            rounding = MathF.Min(rounding, (heightLimit * (verticalRounded ? 0.5f : 1.0f)) - 1.0f);
        }

        if (rounding < 0.5f || (flags & ImDrawFlags.RoundCornersMask_) == ImDrawFlags.RoundCornersNone)
        {
            PathLineTo(rectMin);
            PathLineTo(new ImVec2(rectMax.X, rectMin.Y));
            PathLineTo(rectMax);
            PathLineTo(new ImVec2(rectMin.X, rectMax.Y));
        }
        else
        {
            float roundingTl = (flags & ImDrawFlags.RoundCornersTopLeft) != 0 ? rounding : 0.0f;
            float roundingTr = (flags & ImDrawFlags.RoundCornersTopRight) != 0 ? rounding : 0.0f;
            float roundingBr = (flags & ImDrawFlags.RoundCornersBottomRight) != 0 ? rounding : 0.0f;
            float roundingBl = (flags & ImDrawFlags.RoundCornersBottomLeft) != 0 ? rounding : 0.0f;
            PathArcToFast(new ImVec2(rectMin.X + roundingTl, rectMin.Y + roundingTl), roundingTl, 6, 9);
            PathArcToFast(new ImVec2(rectMax.X - roundingTr, rectMin.Y + roundingTr), roundingTr, 9, 12);
            PathArcToFast(new ImVec2(rectMax.X - roundingBr, rectMax.Y - roundingBr), roundingBr, 0, 3);
            PathArcToFast(new ImVec2(rectMin.X + roundingBl, rectMax.Y - roundingBl), roundingBl, 3, 6);
        }
    }

    #endregion

    #region Draw Callbacks

    /// <summary>
    /// Registers a backend callback on a new command, replacing triangle submission for that command.
    /// Ports canonical <c>ImDrawList::AddCallback</c>.
    /// When <paramref name="userdataSize"/> is zero the pointer is stored directly; otherwise the
    /// bytes are copied into a pinned buffer and exposed through <c>ImDrawCmd.UserCallbackData</c>.
    /// </summary>
    /// <param name="callback">Callback invoked by the backend instead of drawing triangles.</param>
    /// <param name="userdata">Opaque callback data pointer, or zero when unused.</param>
    /// <param name="userdataSize">Number of bytes to copy from <paramref name="userdata"/>; zero stores the pointer directly.</param>
    public void AddCallback(ImDrawCallback callback, IntPtr userdata = default, int userdataSize = 0)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        if (CmdBuffer.Count == 0)
            AddDrawCmd();

        int index = CmdBuffer.Count - 1;
        ImDrawCmd curr = CmdBuffer[index];
        if (curr.ElemCount != 0)
        {
            AddDrawCmd();
            index = CmdBuffer.Count - 1;
            curr = CmdBuffer[index];
        }

        curr.UserCallback = callback;
        if (userdataSize == 0)
        {
            curr.UserCallbackData = userdata;
            curr.UserCallbackDataSize = 0;
            curr.UserCallbackDataOffset = -1;
        }
        else
        {
            byte[] buffer = new byte[userdataSize];
            Marshal.Copy(userdata, buffer, 0, userdataSize);
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            _callbackDataHandles.Add(handle);
            curr.UserCallbackData = handle.AddrOfPinnedObject();
            curr.UserCallbackDataSize = userdataSize;
            curr.UserCallbackDataOffset = 0;
        }
        CmdBuffer[index] = curr;

        // Force a new command after the callback so later geometry cannot merge into it.
        AddDrawCmd();
    }

    #endregion

    #region Concave Fill Triangulation (ImTriangulator)

    /// <summary>
    /// Ear-clipping triangulator for simple concave polygons. Ports the canonical
    /// <c>ImTriangulator</c>. using index-based
    /// node links instead of pointers. Instances are reused per draw list so the node and stack
    /// arrays are only grown when a larger polygon appears.
    /// </summary>
    private sealed class ConcaveTriangulator
    {
        /// <summary>Classification of a polygon node within the ear-clipping state machine.</summary>
        private enum NodeType
        {
            /// <summary>Node is convex but not yet known to be an ear.</summary>
            Convex,
            /// <summary>Node is a valid ear that can be clipped.</summary>
            Ear,
            /// <summary>Node is reflex and blocks ear clipping.</summary>
            Reflex,
        }

        /// <summary>One linked-list node of the polygon boundary.</summary>
        private struct Node
        {
            /// <summary>Index of the previous boundary node.</summary>
            internal int Prev;
            /// <summary>Index of the next boundary node.</summary>
            internal int Next;
            /// <summary>Original polygon vertex index.</summary>
            internal int Index;
            /// <summary>Vertex position in screen space.</summary>
            internal ImVec2 Pos;
            /// <summary>Current classification of the node.</summary>
            internal NodeType Type;
        }

        /// <summary>Boundary nodes laid out in original polygon order.</summary>
        private Node[] _nodes = Array.Empty<Node>();
        /// <summary>Candidate ear node indices.</summary>
        private int[] _ears = Array.Empty<int>();
        /// <summary>Reflex node indices used for containment tests.</summary>
        private int[] _reflexes = Array.Empty<int>();
        /// <summary>Copied polygon vertices used for node positions.</summary>
        private ImVec2[] _points = Array.Empty<ImVec2>();
        /// <summary>Head of the circular node list.</summary>
        private int _nodeHead;
        /// <summary>Count of ears currently in <see cref="_ears"/>.</summary>
        private int _earCount;
        /// <summary>Count of reflexes currently in <see cref="_reflexes"/>.</summary>
        private int _reflexCount;
        /// <summary>Number of triangles remaining to emit.</summary>
        internal int TrianglesLeft;

        /// <summary>Prepares the triangulator for a polygon, reusing existing buffers where possible.</summary>
        /// <param name="points">Simple concave polygon vertices.</param>
        internal void Init(ReadOnlySpan<ImVec2> points)
        {
            int count = points.Length;
            if (_nodes.Length < count) _nodes = new Node[count];
            if (_ears.Length < count) _ears = new int[count];
            if (_reflexes.Length < count) _reflexes = new int[count];
            if (_points.Length < count) _points = new ImVec2[count];

            for (int i = 0; i < count; i++)
                _points[i] = points[i];

            TrianglesLeft = count - 2;
            _earCount = 0;
            _reflexCount = 0;
            _nodeHead = 0;

            for (int i = 0; i < count; i++)
            {
                _nodes[i].Type = NodeType.Convex;
                _nodes[i].Index = i;
                _nodes[i].Pos = _points[i];
                _nodes[i].Next = i + 1;
                _nodes[i].Prev = i - 1;
            }
            _nodes[0].Prev = count - 1;
            _nodes[count - 1].Next = 0;

            BuildReflexes();
            BuildEars();
        }

        /// <summary>Classifies nodes whose turn is not clockwise as reflex.</summary>
        private void BuildReflexes()
        {
            int n1 = _nodeHead;
            for (int i = TrianglesLeft; i >= 0; i--, n1 = _nodes[n1].Next)
            {
                if (TriangleIsClockwise(_nodes[_nodes[n1].Prev].Pos, _nodes[n1].Pos, _nodes[_nodes[n1].Next].Pos))
                    continue;
                _nodes[n1].Type = NodeType.Reflex;
                PushReflex(n1);
            }
        }

        /// <summary>Classifies convex nodes that contain no reflex vertex as ears.</summary>
        private void BuildEars()
        {
            int n1 = _nodeHead;
            for (int i = TrianglesLeft; i >= 0; i--, n1 = _nodes[n1].Next)
            {
                if (_nodes[n1].Type != NodeType.Convex)
                    continue;
                int prev = _nodes[n1].Prev;
                int next = _nodes[n1].Next;
                if (!IsEar(_nodes[prev].Index, _nodes[n1].Index, _nodes[next].Index, _nodes[prev].Pos, _nodes[n1].Pos, _nodes[next].Pos))
                    continue;
                _nodes[n1].Type = NodeType.Ear;
                _ears[_earCount++] = n1;
            }
        }

        /// <summary>Emits the next ear triangle as original polygon indices.</summary>
        /// <param name="outTriangle">Receives the three original polygon vertex indices.</param>
        internal void GetNextTriangle(Span<int> outTriangle)
        {
            if (_earCount == 0)
            {
                FlipNodeList();
                int node = _nodeHead;
                for (int i = TrianglesLeft; i >= 0; i--, node = _nodes[node].Next)
                    _nodes[node].Type = NodeType.Convex;
                _reflexCount = 0;
                BuildReflexes();
                BuildEars();

                if (_earCount == 0)
                {
                    // Degenerate geometry: fall back to the first triangle, matching the convex fill.
                    _ears[0] = _nodeHead;
                    _earCount = 1;
                }
            }

            int ear = _ears[--_earCount];
            int prev = _nodes[ear].Prev;
            int next = _nodes[ear].Next;
            outTriangle[0] = _nodes[prev].Index;
            outTriangle[1] = _nodes[ear].Index;
            outTriangle[2] = _nodes[next].Index;

            _nodes[next].Prev = prev;
            _nodes[prev].Next = next;
            if (ear == _nodeHead)
                _nodeHead = next;

            ReclassifyNode(prev);
            ReclassifyNode(next);
            TrianglesLeft--;
        }

        /// <summary>Reverses the circular node list so a stuck winding can be retried.</summary>
        private void FlipNodeList()
        {
            int prev = _nodeHead;
            int current = _nodes[_nodeHead].Next;
            _nodes[prev].Next = prev;
            _nodes[prev].Prev = prev;
            while (current != _nodeHead)
            {
                int temp = _nodes[current].Next;
                _nodes[current].Next = prev;
                _nodes[prev].Prev = current;
                _nodes[_nodeHead].Next = current;
                _nodes[current].Prev = _nodeHead;
                prev = current;
                current = temp;
            }
            _nodeHead = prev;
        }

        /// <summary>Tests whether a triangle contains any reflex vertex.</summary>
        /// <param name="i0">First vertex index.</param>
        /// <param name="i1">Second vertex index.</param>
        /// <param name="i2">Third vertex index.</param>
        /// <param name="v0">First vertex position.</param>
        /// <param name="v1">Second vertex position.</param>
        /// <param name="v2">Third vertex position.</param>
        /// <returns>True when no reflex vertex lies inside the triangle.</returns>
        private bool IsEar(int i0, int i1, int i2, ImVec2 v0, ImVec2 v1, ImVec2 v2)
        {
            for (int p = 0; p < _reflexCount; p++)
            {
                int reflex = _reflexes[p];
                if (_nodes[reflex].Index != i0 && _nodes[reflex].Index != i1 && _nodes[reflex].Index != i2)
                {
                    if (TriangleContainsPoint(v0, v1, v2, _nodes[reflex].Pos))
                        return false;
                }
            }
            return true;
        }

        /// <summary>Recomputes a node's classification after neighboring ears are removed.</summary>
        /// <param name="n1">Node index to reclassify.</param>
        private void ReclassifyNode(int n1)
        {
            int n0 = _nodes[n1].Prev;
            int n2 = _nodes[n1].Next;
            NodeType type;
            if (!TriangleIsClockwise(_nodes[n0].Pos, _nodes[n1].Pos, _nodes[n2].Pos))
                type = NodeType.Reflex;
            else if (IsEar(_nodes[n0].Index, _nodes[n1].Index, _nodes[n2].Index, _nodes[n0].Pos, _nodes[n1].Pos, _nodes[n2].Pos))
                type = NodeType.Ear;
            else
                type = NodeType.Convex;

            if (type == _nodes[n1].Type)
                return;
            if (_nodes[n1].Type == NodeType.Reflex)
                RemoveReflex(_nodes[n1].Index);
            else if (_nodes[n1].Type == NodeType.Ear)
                RemoveEar(_nodes[n1].Index);
            if (type == NodeType.Reflex)
                PushReflex(n1);
            else if (type == NodeType.Ear)
                _ears[_earCount++] = n1;
            _nodes[n1].Type = type;
        }

        /// <summary>Adds a node index to the reflex set.</summary>
        /// <param name="nodeIndex">Node index to add.</param>
        private void PushReflex(int nodeIndex) => _reflexes[_reflexCount++] = nodeIndex;

        /// <summary>Removes an ear by original polygon index using an unsorted swap-remove.</summary>
        /// <param name="polygonIndex">Original polygon index of the ear to remove.</param>
        private void RemoveEar(int polygonIndex)
        {
            for (int i = _earCount - 1; i >= 0; i--)
            {
                if (_nodes[_ears[i]].Index == polygonIndex)
                {
                    _ears[i] = _ears[--_earCount];
                    return;
                }
            }
        }

        /// <summary>Removes a reflex by original polygon index using an unsorted swap-remove.</summary>
        /// <param name="polygonIndex">Original polygon index of the reflex to remove.</param>
        private void RemoveReflex(int polygonIndex)
        {
            for (int i = _reflexCount - 1; i >= 0; i--)
            {
                if (_nodes[_reflexes[i]].Index == polygonIndex)
                {
                    _reflexes[i] = _reflexes[--_reflexCount];
                    return;
                }
            }
        }
    }

    #endregion
}

#endregion

#region Draw Channels (ImDrawListSplitter)

/// <summary>
/// Storage for one drawing channel of an <see cref="ImDrawListSplitter"/>: the command and index
/// buffers swapped in and out of the draw list while a channel is current. Mirrors the canonical
/// <c>ImDrawChannel</c>. The vertex buffer is shared by
/// every channel and is therefore not stored here.
/// </summary>
public sealed class ImDrawChannel
{
    /// <summary>Command buffer recorded for this channel.</summary>
    /// <returns>The channel's accumulated draw commands.</returns>
    public List<ImDrawCmd> CmdBuffer { get; internal set; } = new();

    /// <summary>Index buffer recorded for this channel.</summary>
    /// <returns>The channel's accumulated triangle indices.</returns>
    public List<ushort> IdxBuffer { get; internal set; } = new();
}

/// <summary>
/// Splits an <see cref="ImDrawList"/> into ordered channels so geometry from different logical
/// layers can be recorded independently and merged back into one command list in channel order.
/// Ports canonical <c>ImDrawListSplitter</c> (,
/// implementation ). This is the mechanism behind the
/// canonical Columns and Tables APIs.
/// </summary>
/// <remarks>
/// A splitter is owned by one frame-building thread. Only one split/merge pair may be active at a
/// time; nested splitting is rejected and requires a separate splitter instance. Channel buffers are
/// swapped by reference rather than copied, so a channel's storage stays valid until it becomes
/// current again.
/// </remarks>
public class ImDrawListSplitter
{
    /// <summary>Current channel index, matching canonical <c>ImDrawListSplitter::_Current</c>.</summary>
    private int _current;
    /// <summary>Number of active channels, matching canonical <c>ImDrawListSplitter::_Count</c>.</summary>
    private int _count = 1;
    /// <summary>
    /// Channel storage, matching canonical <c>ImDrawListSplitter::_Channels</c>. The list is never
    /// shrunk by <see cref="Clear"/> so channel buffers are reused across frames.
    /// </summary>
    private readonly List<ImDrawChannel> _channels = new();

    /// <summary>Number of active channels. At least one; a value of one means no split is active.</summary>
    public int ChannelCount => _count;

    /// <summary>Index of the channel that receives new geometry.</summary>
    public int CurrentChannel => _current;

    /// <summary>
    /// Allocated channel storage, retained between frames so buffers are reused. The list can be
    /// longer than <see cref="ChannelCount"/> because storage is not shrunk on merge.
    /// </summary>
    /// <returns>The allocated channel storage.</returns>
    public IReadOnlyList<ImDrawChannel> Channels => _channels;

    /// <summary>
    /// Marks the splitter as unsplit while keeping channel storage allocated for reuse. Ports
    /// canonical <c>ImDrawListSplitter::Clear</c>.; channel
    /// buffers are deliberately not cleared.
    /// </summary>
    public void Clear()
    {
        _current = 0;
        _count = 1;
    }

    /// <summary>
    /// Releases all channel storage. Ports canonical <c>ImDrawListSplitter::ClearFreeMemory</c>
    ///. The current channel aliases the owning
    /// draw list's buffers, so its slot is dropped without clearing the aliased buffers.
    /// </summary>
    public void ClearFreeMemory()
    {
        for (int i = 0; i < _channels.Count; i++)
        {
            if (i == _current)
                _channels[i] = new ImDrawChannel();
            else
            {
                _channels[i].CmdBuffer.Clear();
                _channels[i].IdxBuffer.Clear();
            }
        }
        _current = 0;
        _count = 1;
        _channels.Clear();
    }

    /// <summary>
    /// Activates the requested number of channels. Ports canonical
    /// <c>ImDrawListSplitter::Split</c>. Channels
    /// other than the current one are emptied so stale geometry cannot leak between frames.
    /// </summary>
    /// <param name="drawList">Draw list being split; retained for canonical signature parity and not otherwise used.</param>
    /// <param name="channelsCount">Number of channels to activate, at least one.</param>
    /// <exception cref="InvalidOperationException">A split is already active on this splitter.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channelsCount"/> is less than one.</exception>
    public void Split(ImDrawList drawList, int channelsCount)
    {
        if (channelsCount < 1)
            throw new ArgumentOutOfRangeException(nameof(channelsCount), "At least one channel is required.");
        if (_current != 0 || _count > 1)
            throw new InvalidOperationException("Nested channel splitting is not supported; use a separate ImDrawListSplitter instance.");

        int oldCount = _channels.Count;
        while (_channels.Count < channelsCount)
            _channels.Add(new ImDrawChannel());
        _count = channelsCount;

        // Channel zero holds a copy of the draw list's buffers once the current channel moves, so its
        // previous slot is dropped to keep state tidy; the same happens for previously unused slots.
        _channels[0] = new ImDrawChannel();
        for (int i = 1; i < channelsCount; i++)
        {
            if (i < oldCount)
            {
                _channels[i].CmdBuffer.Clear();
                _channels[i].IdxBuffer.Clear();
            }
            else
            {
                _channels[i] = new ImDrawChannel();
            }
        }
    }

    /// <summary>
    /// Merges all channels back into the draw list in channel order, repairing command index offsets
    /// and coalescing adjacent commands that share a header. Ports canonical
    /// <c>ImDrawListSplitter::Merge</c>. After the
    /// call the splitter reports a single channel, and the draw list ends with a non-callback command.
    /// </summary>
    /// <param name="drawList">Draw list receiving the merged command and index buffers.</param>
    public void Merge(ImDrawList drawList)
    {
        if (_count <= 1)
            return;

        SetCurrentChannel(drawList, 0);
        drawList.PopUnusedDrawCmd();

        // Track the trailing command of the merged output so adjacent channels can coalesce. The
        // canonical implementation keeps a pointer; the port keeps the owning buffer and index because
        // commands are value types stored in lists.
        int idxOffset = 0;
        ImDrawCmd lastCmd = default;
        List<ImDrawCmd>? lastCmdOwner = null;
        int lastCmdIndex = -1;
        if (drawList.CmdBuffer.Count > 0)
        {
            lastCmd = drawList.CmdBuffer[^1];
            lastCmdOwner = drawList.CmdBuffer;
            lastCmdIndex = drawList.CmdBuffer.Count - 1;
            idxOffset = (int)(lastCmd.IdxOffset + lastCmd.ElemCount);
        }

        for (int i = 1; i < _count; i++)
        {
            List<ImDrawCmd> channelCmds = _channels[i].CmdBuffer;
            if (channelCmds.Count > 0)
            {
                ImDrawCmd trailing = channelCmds[^1];
                if (trailing.ElemCount == 0 && trailing.UserCallback == null)
                    channelCmds.RemoveAt(channelCmds.Count - 1);
            }

            if (channelCmds.Count > 0 && lastCmdOwner != null)
            {
                ImDrawCmd nextCmd = channelCmds[0];
                if (ImDrawList.HeaderEquals(lastCmd, nextCmd.ClipRect, nextCmd.TexRef, nextCmd.VtxOffset)
                    && lastCmd.UserCallback == null && nextCmd.UserCallback == null)
                {
                    lastCmd.ElemCount += nextCmd.ElemCount;
                    idxOffset += (int)nextCmd.ElemCount;
                    lastCmdOwner[lastCmdIndex] = lastCmd;
                    channelCmds.RemoveAt(0);
                }
            }

            if (channelCmds.Count > 0)
            {
                lastCmd = channelCmds[^1];
                lastCmdOwner = channelCmds;
                lastCmdIndex = channelCmds.Count - 1;
            }

            // Rewrite IdxOffset for every merged command so the backend can index into the combined
            // index buffer without knowing about channels.
            for (int cmdN = 0; cmdN < channelCmds.Count; cmdN++)
            {
                ImDrawCmd cmd = channelCmds[cmdN];
                cmd.IdxOffset = (uint)idxOffset;
                idxOffset += (int)cmd.ElemCount;
                channelCmds[cmdN] = cmd;
            }
        }

        for (int i = 1; i < _count; i++)
        {
            drawList.CmdBuffer.AddRange(_channels[i].CmdBuffer);
            drawList.IdxBuffer.AddRange(_channels[i].IdxBuffer);
        }

        // Ensure a non-callback draw command trails the command buffer, then align its header with
        // the active command header (canonical ImDrawCmd_HeaderCopy) or split it if it differs.
        if (drawList.CmdBuffer.Count == 0 || drawList.CmdBuffer[^1].UserCallback != null)
            drawList.AddDrawCmd();

        ImDrawCmd currCmd = drawList.CmdBuffer[^1];
        if (currCmd.ElemCount == 0)
        {
            currCmd.ClipRect = drawList._cmdHeaderClipRect;
            currCmd.TexRef = drawList._cmdHeaderTexRef;
            currCmd.VtxOffset = drawList._cmdHeaderVtxOffset;
            drawList.CmdBuffer[^1] = currCmd;
        }
        else if (!ImDrawList.HeaderEquals(currCmd, drawList._cmdHeaderClipRect, drawList._cmdHeaderTexRef, drawList._cmdHeaderVtxOffset))
        {
            drawList.AddDrawCmd();
        }

        _count = 1;
    }

    /// <summary>
    /// Makes the requested channel current by swapping its command and index buffers into the draw
    /// list. Ports canonical <c>ImDrawListSplitter::SetCurrentChannel</c>
    ///. If the channel's trailing command uses a
    /// different header than the active one, a new command is started so geometry keeps the correct
    /// clip rectangle and texture.
    /// </summary>
    /// <param name="drawList">Draw list whose buffers are swapped with the channel storage.</param>
    /// <param name="channelIndex">Zero-based channel index to make current.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channelIndex"/> is outside the active channel range.</exception>
    public void SetCurrentChannel(ImDrawList drawList, int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _count)
            throw new ArgumentOutOfRangeException(nameof(channelIndex), "Channel index is outside the active channel range.");
        if (_current == channelIndex)
            return;

        // Swap the draw list's buffers with the current channel's, then load the requested channel.
        // The current channel slot and the draw list alias the same buffers, so assigning to the slot
        // is a no-op in the common case and never leaks the buffer that is about to be replaced.
        _channels[_current].CmdBuffer = drawList.CmdBuffer;
        _channels[_current].IdxBuffer = drawList.IdxBuffer;
        drawList.CmdBuffer = _channels[channelIndex].CmdBuffer;
        drawList.IdxBuffer = _channels[channelIndex].IdxBuffer;
        _current = channelIndex;

        if (drawList.CmdBuffer.Count == 0)
        {
            drawList.AddDrawCmd();
            return;
        }

        ImDrawCmd currCmd = drawList.CmdBuffer[^1];
        if (currCmd.ElemCount == 0)
        {
            currCmd.ClipRect = drawList._cmdHeaderClipRect;
            currCmd.TexRef = drawList._cmdHeaderTexRef;
            currCmd.VtxOffset = drawList._cmdHeaderVtxOffset;
            drawList.CmdBuffer[^1] = currCmd;
        }
        else if (!ImDrawList.HeaderEquals(currCmd, drawList._cmdHeaderClipRect, drawList._cmdHeaderTexRef, drawList._cmdHeaderVtxOffset))
        {
            drawList.AddDrawCmd();
        }
    }
}

#endregion

#region Shared Draw Data Support (ImDrawListSharedData, ImDrawDataBuilder)

/// <summary>
/// The public accessor object returned by <c>GetDrawListSharedData()</c>: it carries the font atlas
/// values and tessellation tolerances that a frame's draw lists share
/// </summary>
/// <remarks>
/// <para>
/// In canonical Dear ImGui this structure owns the shared atlas bindings that every
/// <c>ImDrawList</c> points at. In this port <see cref="ImDrawList"/> is atlas-bound: it captures the
/// white texel, the anti-aliased line UVs, and the initial flags directly from the
/// <see cref="ImFontAtlas"/> when it is constructed. This type therefore exists to satisfy the
/// canonical public accessor and to publish the current font and tessellation settings; it is not
/// required for a draw list to function.
/// </para>
/// <para>
/// The instance is owned by <see cref="ImGuiContext.DrawListSharedData"/> and updated once per frame
/// by the frame owner. It is plain single-threaded mutable state.
/// </para>
/// </remarks>
public sealed class ImDrawListSharedData
{
    /// <summary>
    /// Gets or sets the UV coordinate of the opaque white texel in the atlas.
    /// </summary>
    public ImVec2 TexUvWhitePixel { get; set; }

    /// <summary>
    /// Gets or sets the UV coordinates of the anti-aliased line geometry in the atlas, or
    /// <see langword="null"/> when no atlas is bound.
    /// </summary>
    public ImVec4[]? TexUvLines { get; set; }

    /// <summary>
    /// Gets or sets the current font used by the simplified text overloads, or
    /// <see langword="null"/>.
    /// </summary>
    public ImFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the current font size used by the simplified text overloads.
    /// </summary>
    public float FontSize { get; set; }

    /// <summary>
    /// Gets or sets the tessellation tolerance used by <c>PathBezierCurveTo()</c>.
    /// </summary>
    public float CurveTessellationTol { get; set; } = 1.25f;

    /// <summary>
    /// Gets or sets the maximum circle tessellation error, in pixels of radius, used by the circle
    /// primitives.
    /// </summary>
    public float CircleTessellationMaxError { get; set; } = 0.30f;

    /// <summary>
    /// Gets or sets the fullscreen clip rectangle published through
    /// <c>PushClipRectFullscreen()</c>.
    /// </summary>
    public ImVec4 ClipRectFullscreen { get; set; }

    /// <summary>
    /// Gets or sets the draw-list flags applied at the beginning of the frame.
    /// </summary>
    public ImDrawFlags InitialDrawFlags { get; set; } = ImDrawFlags.AAFill | ImDrawFlags.AALines;

    /// <summary>
    /// Obsolete alias for <see cref="InitialDrawFlags"/>.
    /// </summary>
    [Obsolete("Use InitialDrawFlags instead.")]
    public ImDrawListFlags InitialFlags
    {
        get => (ImDrawListFlags)InitialDrawFlags;
        set => InitialDrawFlags = (ImDrawFlags)value;
    }
}

/// <summary>
/// Temporary storage used while assembling an <see cref="ImDrawData"/> from the frame's draw lists:
/// it points at the global layer vectors and owns one scratch layer
/// </summary>
/// <remarks>
/// <para>
/// Canonical declares exactly two layer slots: slot 0 is the regular layer owned by the draw data
/// and slot 1 is the tooltip layer. This port models each slot as a nullable
/// <see cref="List{T}"/> of <see cref="ImDrawList"/> because C# has no pointer-to-vector type; a
/// slot may alias an external list or be cleared when the frame ends.
/// </para>
/// <para>
/// The canonical revision used by this port does not declare <c>Clear()</c>,
/// <c>ClearFreeMemory()</c>, or <c>GetDrawListLayer()</c> on this structure, so they are not
/// provided here.
/// </para>
/// </remarks>
public sealed class ImDrawDataBuilder
{
    /// <summary>
    /// Gets the two global layer pointers: index 0 is the regular layer and index 1 is the tooltip
    /// layer.
    /// </summary>
    public List<ImDrawList>?[] Layers { get; } = new List<ImDrawList>?[2];

    /// <summary>
    /// Gets the scratch layer used while building the final draw data.
    /// </summary>
    public List<ImDrawList> LayerData1 { get; } = new();
}

#endregion

#region Vertex shading helpers

/// <summary>
/// Vertex post-processing helpers that canonical exposes as free functions for callers that must
/// recolor or reproject a freshly emitted vertex range.
/// </summary>
public static partial class ImGui
{
    /// <summary>
    /// Recolors a vertex range along a linear color gradient while preserving each vertex's alpha.
    /// Mirrors canonical <c>ImGui::ShadeVertsLinearColorGradientKeepAlpha</c>
    /// </summary>
    /// <param name="drawList">The draw list whose vertex buffer is modified in place.</param>
    /// <param name="vertStartIdx">First vertex index of the range.</param>
    /// <param name="vertEndIdx">One past the last vertex index of the range.</param>
    /// <param name="gradientP0">The point on the gradient axis mapped to <paramref name="col0"/>.</param>
    /// <param name="gradientP1">The point on the gradient axis mapped to <paramref name="col1"/>.</param>
    /// <param name="col0">Packed color at <paramref name="gradientP0"/> in canonical ABGR byte order.</param>
    /// <param name="col1">Packed color at <paramref name="gradientP1"/> in canonical ABGR byte order.</param>
    /// <remarks>
    /// <para>
    /// Each vertex's position is projected onto the axis from <paramref name="gradientP0"/> to
    /// <paramref name="gradientP1"/>; the normalized projection selects the interpolated red, green,
    /// and blue channels, clamped to the axis endpoints. The vertex's existing alpha channel is kept,
    /// so the caller-supplied colors only need meaningful RGB components.
    /// </para>
    /// <para>
    /// A degenerate axis (two coincident points) leaves every vertex at the start color because the
    /// reciprocal of the squared axis length is infinite and the projection is zero.
    /// </para>
    /// </remarks>
    public static void ShadeVertsLinearColorGradientKeepAlpha(ImDrawList drawList, int vertStartIdx, int vertEndIdx,
        ImVec2 gradientP0, ImVec2 gradientP1, uint col0, uint col1)
    {
        ImVec2 gradientExtent = gradientP1 - gradientP0;
        float gradientInvLength2 = 1.0f / ((gradientExtent.X * gradientExtent.X) + (gradientExtent.Y * gradientExtent.Y));

        int col0R = (int)(col0 >> 0) & 0xFF;
        int col0G = (int)(col0 >> 8) & 0xFF;
        int col0B = (int)(col0 >> 16) & 0xFF;
        int colDeltaR = ((int)(col1 >> 0) & 0xFF) - col0R;
        int colDeltaG = ((int)(col1 >> 8) & 0xFF) - col0G;
        int colDeltaB = ((int)(col1 >> 16) & 0xFF) - col0B;

        for (int i = vertStartIdx; i < vertEndIdx; i++)
        {
            ImDrawVert vertex = drawList.VtxBuffer[i];
            float d = ((vertex.Pos.X - gradientP0.X) * gradientExtent.X)
                + ((vertex.Pos.Y - gradientP0.Y) * gradientExtent.Y);
            float t = Math.Clamp(d * gradientInvLength2, 0.0f, 1.0f);
            int r = (int)(col0R + colDeltaR * t);
            int g = (int)(col0G + colDeltaG * t);
            int b = (int)(col0B + colDeltaB * t);
            vertex.Col = (uint)(r | (g << 8) | (b << 16)) | (vertex.Col & 0xFF000000u);
            drawList.VtxBuffer[i] = vertex;
        }
    }
}

#endregion
