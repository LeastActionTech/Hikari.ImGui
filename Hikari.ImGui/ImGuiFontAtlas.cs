// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Hikari.ImGui;

#region Texture API (ImTextureRect, ImTextureData)

// ImTextureFormat and ImTextureStatus are defined once in ImGuiTypes.cs and reused here.

/// <summary>
/// Pixel rectangle within a texture. Mirrors canonical <c>ImTextureRect</c>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImTextureRect
{
    /// <summary>Left coordinate of the rectangle.</summary>
    public ushort X;

    /// <summary>Top coordinate of the rectangle.</summary>
    public ushort Y;

    /// <summary>Rectangle width.</summary>
    public ushort W;

    /// <summary>Rectangle height.</summary>
    public ushort H;
}

/// <summary>
/// Rectangle reserved in the atlas texture by a custom-rectangle request, with UV coordinates valid
/// for the atlas's current texture. Mirrors canonical <c>ImFontAtlasRect</c>
/// </summary>
/// <remarks>
/// Canonical returns this from <c>ImFontAtlas::GetCustomRect()</c> and warns that the values may not
/// be cached because a later repack can move the rectangle and invalidate the UVs; callers must
/// re-query after <see cref="ImFontAtlas"/> rebuilds the texture. Canonical's opaque
/// <c>ImFontAtlasRectId</c> typedef is represented in this port by a plain <see cref="int"/> using
/// <see cref="ImFontAtlas.RectIdInvalid"/> as the invalid value, which matches how canonical treats
/// it.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct ImFontAtlasRect
{
    /// <summary>Left coordinate of the rectangle in the current texture.</summary>
    public ushort X;

    /// <summary>Top coordinate of the rectangle in the current texture.</summary>
    public ushort Y;

    /// <summary>Rectangle width in pixels.</summary>
    public ushort W;

    /// <summary>Rectangle height in pixels.</summary>
    public ushort H;

    /// <summary>Top-left UV coordinate in the current texture.</summary>
    public ImVec2 Uv0;

    /// <summary>Bottom-right UV coordinate in the current texture.</summary>
    public ImVec2 Uv1;
}

/// <summary>
/// Lightweight reference to a texture, either as a backend identifier or through an
/// <see cref="ImTextureData"/>. Mirrors canonical <c>ImTextureRef</c>
/// </summary>
public struct ImTextureRef
{
    /// <summary>Backend texture identifier used when no <see cref="ImTextureData"/> is attached.</summary>
    public IntPtr TexID;

    /// <summary>CPU-side texture data, or <see langword="null"/> for a backend-owned identifier.</summary>
    public ImTextureData? TexData;

    /// <summary>Initializes a reference from a backend identifier.</summary>
    /// <param name="texId">Backend texture identifier.</param>
    public ImTextureRef(IntPtr texId)
    {
        TexID = texId;
        TexData = null;
    }

    /// <summary>Initializes a reference from CPU-side texture data.</summary>
    /// <param name="texData">Texture data that owns the pixels.</param>
    public ImTextureRef(ImTextureData texData)
    {
        TexID = texData.TexID;
        TexData = texData;
    }

    /// <summary>
    /// Resolves the effective backend texture identifier.
    /// </summary>
    /// <returns>The identifier stored on the texture data when present, otherwise <see cref="TexID"/>.</returns>
    public readonly IntPtr GetTexID() => TexData != null ? TexData.TexID : TexID;
}

/// <summary>
/// CPU-side pixel storage and lifecycle state for one texture. Mirrors canonical
/// <c>ImTextureData</c>. Renderer backends read the
/// pixels, upload them, and record the resulting identifier with <see cref="SetTexID"/>.
/// </summary>
public sealed class ImTextureData
{
    /// <summary>Sequential index used for debugging; unique per atlas.</summary>
    public int UniqueID { get; set; }

    /// <summary>Current lifecycle state; modify through <see cref="SetStatus"/>.</summary>
    public ImTextureStatus Status { get; private set; } = ImTextureStatus.Destroyed;

    /// <summary>Backend texture identifier; modify through <see cref="SetTexID"/>.</summary>
    public IntPtr TexID { get; private set; }

    /// <summary>Pixel format of <see cref="Pixels"/>.</summary>
    public ImTextureFormat Format { get; private set; } = ImTextureFormat.RGBA32;

    /// <summary>Texture width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Texture height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Bytes per pixel: four for RGBA32 and one for Alpha8.</summary>
    public int BytesPerPixel { get; private set; } = 4;

    /// <summary>Whole texture pixel buffer, or <see langword="null"/> before creation.</summary>
    public byte[]? Pixels { get; private set; }

    /// <summary>Bounding box encompassing all ever-updated pixels.</summary>
    public ImTextureRect UsedRect { get; set; }

    /// <summary>Bounding box encompassing queued updates.</summary>
    public ImTextureRect UpdateRect { get; set; }

    /// <summary>Whether the pixel data carries color rather than white plus alpha.</summary>
    public bool UseColors { get; set; }

    /// <summary>Set when the texture is queued for destruction on the next frame.</summary>
    public bool WantDestroyNextFrame { get; set; }

    /// <summary>
    /// Convenience storage owned by the renderer backend. Mirrors canonical
    /// <c>ImTextureData::BackendUserData</c>., which lets a
    /// backend attach its own resource state to the texture without a side table.
    /// </summary>
    public object? BackendUserData { get; set; }

    /// <summary>
    /// Convenience storage for a staged or multi-threaded rendering texture queue. Mirrors canonical
    /// <c>ImTextureData::QueueUserData</c>., where a non-null
    /// value signals that the texture is still referenced by the queue.
    /// </summary>
    public object? QueueUserData { get; set; }

    /// <summary>
    /// Count of successive frames in which the texture was not used. Mirrors canonical
    /// <c>ImTextureData::UnusedFrames</c>.; canonical keeps this
    /// above zero whenever the status is <see cref="ImTextureStatus.WantDestroy"/> so a backend can
    /// release resources safely.
    /// </summary>
    public int UnusedFrames { get; set; }

    /// <summary>
    /// Number of contexts currently using the texture, used during backend shutdown. Mirrors canonical
    /// <c>ImTextureData::RefCount</c>.
    /// </summary>
    /// <remarks>
    /// Canonical declares this as <c>unsigned short</c>; the port uses <see cref="int"/> so reference
    /// counting arithmetic and diagnostics do not require narrowing casts. The value is bounded by the
    /// number of live contexts, which is far below the 16-bit ceiling in practice.
    /// </remarks>
    public int RefCount { get; set; }

    /// <summary>
    /// Regions whose pixels changed since the backend last observed this texture. Mirrors canonical
    /// <c>ImTextureData::Updates</c>. A backend uploads each
    /// rectangle in turn while the status is <see cref="ImTextureStatus.WantUpdates"/>; the list is
    /// empty for a full <see cref="ImTextureStatus.WantCreate"/> upload.
    /// </summary>
    /// <returns>The queued update rectangles, oldest first.</returns>
    public List<ImTextureRect> Updates { get; } = new();

    /// <summary>
    /// Allocates the pixel buffer for the given format and size and marks the texture as needing
    /// creation. Mirrors canonical <c>ImTextureData::Create</c>.
    /// </summary>
    /// <param name="format">Pixel format to allocate.</param>
    /// <param name="w">Texture width in pixels; must be positive.</param>
    /// <param name="h">Texture height in pixels; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is not positive.</exception>
    public void Create(ImTextureFormat format, int w, int h)
    {
        if (w <= 0)
            throw new ArgumentOutOfRangeException(nameof(w));
        if (h <= 0)
            throw new ArgumentOutOfRangeException(nameof(h));

        Format = format;
        Width = w;
        Height = h;
        BytesPerPixel = format == ImTextureFormat.RGBA32 ? 4 : 1;
        Pixels = new byte[w * h * BytesPerPixel];
        Updates.Clear();
        UsedRect = default;
        // The update rectangle starts at the maximum coordinate so the first queued upload
        // establishes the true origin.
        UpdateRect = new ImTextureRect { X = 0xFFFF, Y = 0xFFFF, W = 0, H = 0 };
        UseColors = false;
        Status = ImTextureStatus.WantCreate;
    }

    /// <summary>Releases the CPU-side pixel buffer and marks the texture destroyed.</summary>
    public void DestroyPixels()
    {
        Pixels = null;
        Updates.Clear();
        Status = ImTextureStatus.Destroyed;
    }

    /// <summary>
    /// Queues a changed pixel rectangle for the backend to upload and widens the update and used
    /// bounds. Mirrors canonical <c>ImTextureDataQueueUpload</c>
    ///. While the texture is awaiting
    /// creation the whole surface is uploaded anyway, so no partial rectangle is recorded.
    /// </summary>
    /// <param name="x">Left coordinate of the changed rectangle.</param>
    /// <param name="y">Top coordinate of the changed rectangle.</param>
    /// <param name="w">Width of the changed rectangle.</param>
    /// <param name="h">Height of the changed rectangle.</param>
    public void QueueUpload(int x, int y, int w, int h)
    {
        var request = new ImTextureRect
        {
            X = (ushort)x,
            Y = (ushort)y,
            W = (ushort)w,
            H = (ushort)h,
        };

        int newX1 = Math.Max(UpdateRect.W == 0 ? 0 : UpdateRect.X + UpdateRect.W, request.X + request.W);
        int newY1 = Math.Max(UpdateRect.H == 0 ? 0 : UpdateRect.Y + UpdateRect.H, request.Y + request.H);
        UpdateRect = new ImTextureRect
        {
            X = (ushort)Math.Min(UpdateRect.X, request.X),
            Y = (ushort)Math.Min(UpdateRect.Y, request.Y),
            W = (ushort)(newX1 - Math.Min(UpdateRect.X, request.X)),
            H = (ushort)(newY1 - Math.Min(UpdateRect.Y, request.Y)),
        };
        int usedX1 = Math.Max(UsedRect.W == 0 ? 0 : UsedRect.X + UsedRect.W, request.X + request.W);
        int usedY1 = Math.Max(UsedRect.H == 0 ? 0 : UsedRect.Y + UsedRect.H, request.Y + request.H);
        UsedRect = new ImTextureRect
        {
            X = (ushort)Math.Min(UsedRect.X, request.X),
            Y = (ushort)Math.Min(UsedRect.Y, request.Y),
            W = (ushort)(usedX1 - Math.Min(UsedRect.X, request.X)),
            H = (ushort)(usedY1 - Math.Min(UsedRect.Y, request.Y)),
        };

        // No need to queue if the backend will already upload everything for a create.
        if (Status == ImTextureStatus.OK || Status == ImTextureStatus.WantUpdates)
        {
            Status = ImTextureStatus.WantUpdates;
            Updates.Add(request);
        }
    }

    /// <summary>Returns the total pixel buffer size in bytes.</summary>
    /// <returns><see cref="Width"/> times <see cref="Height"/> times <see cref="BytesPerPixel"/>.</returns>
    public int GetSizeInBytes() => Width * Height * BytesPerPixel;

    /// <summary>Returns the byte distance between consecutive pixel rows.</summary>
    /// <returns><see cref="Width"/> times <see cref="BytesPerPixel"/>.</returns>
    public int GetPitch() => Width * BytesPerPixel;

    /// <summary>Records the backend texture identifier after the texture is created or updated.</summary>
    /// <param name="texId">Backend texture identifier.</param>
    public void SetTexID(IntPtr texId) => TexID = texId;

    /// <summary>
    /// Records a backend-reported lifecycle state. Mirrors the canonical behavior of reverting to
    /// <see cref="ImTextureStatus.WantCreate"/> when destroyed while pixels still exist.
    /// </summary>
    /// <param name="status">New lifecycle state.</param>
    public void SetStatus(ImTextureStatus status)
    {
        Status = status;
        if (status == ImTextureStatus.Destroyed && !WantDestroyNextFrame && Pixels != null)
            Status = ImTextureStatus.WantCreate;
    }

    /// <summary>Returns a reference to this texture.</summary>
    /// <returns>An <see cref="ImTextureRef"/> pointing at this texture data.</returns>
    public ImTextureRef GetTexRef()
    {
        return new ImTextureRef(this);
    }
}

#endregion

#region ImFontConfig

/// <summary>
/// Input description for one font source. Mirrors canonical <c>ImFontConfig</c>
///. with the fields the pure C# loader honors.
/// </summary>
/// <remarks>
/// The canonical <c>Name[40]</c> character buffer is represented as a managed string, and the
/// canonical <c>GlyphRanges</c> and <c>GlyphExcludeRanges</c> pointers are represented as nullable
/// arrays; both changes preserve the documented contract while removing native pointer lifetime
/// hazards. The canonical documentation requires glyph-range array data to persist for the font's
/// lifetime; the port copies the exclusion list when a source is attached so a caller may reuse or
/// mutate its array afterwards.
/// </remarks>
public sealed class ImFontConfig
{
    /// <summary>Debug name of the source font.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>TTF or OTF file image, or <see langword="null"/> before a source is attached.</summary>
    public byte[]? FontData { get; set; }

    /// <summary>Size of <see cref="FontData"/> in bytes.</summary>
    public int FontDataSize { get; set; }

    /// <summary>Whether the atlas owns <see cref="FontData"/> and may release it.</summary>
    public bool FontDataOwnedByAtlas { get; set; } = true;

    /// <summary>Whether this source merges into the previous font instead of creating a new one.</summary>
    public bool MergeMode { get; set; }

    /// <summary>When true, glyph advances are snapped to whole pixels.</summary>
    public bool PixelSnapH { get; set; }

    /// <summary>
    /// Whether canonical would align the scaled vertical glyph offset to pixel boundaries,
    /// defaulting to <see langword="true"/>. Mirrors canonical <c>ImFontConfig::PixelSnapV</c>,
    /// which is declared only while obsolete functions are enabled and is marked as obsoleted.
    /// </summary>
    /// <remarks>
    /// Canonical Dear ImGui declares this field but never reads it: no reference to
    /// <c>PixelSnapV</c> remains in the draw list implementation, and the release notes record that the
    /// behavior was removed because post-rescale <c>GlyphOffset</c> is always rounded.
    /// The port models the field so the configuration
    /// structure matches canonical, and deliberately attaches no behavior to it: changing the
    /// value cannot affect rasterization, metrics or layout.
    /// </remarks>
    public bool PixelSnapV { get; set; } = true;

    /// <summary>Horizontal oversampling: zero selects the canonical automatic value.</summary>
    public sbyte OversampleH { get; set; }

    /// <summary>Vertical oversampling: zero selects the canonical automatic value.</summary>
    public sbyte OversampleV { get; set; }

    /// <summary>Explicit ellipsis codepoint, or zero to use the font default.</summary>
    public uint EllipsisChar { get; set; }

    /// <summary>Output size in pixels for the rasterizer.</summary>
    public float SizePixels { get; set; }

    /// <summary>Inclusive Unicode ranges to load as codepoint pairs, or <see langword="null"/> for the default range.</summary>
    public uint[]? GlyphRanges { get; set; }

    /// <summary>
    /// Inclusive Unicode ranges to exclude from this source, as zero-terminated codepoint pairs, or
    /// <see langword="null"/> when the source contributes every glyph its face contains. Mirrors
    /// canonical <c>ImFontConfig::GlyphExcludeRanges</c>., which
    /// exists so overlapping glyphs can be attributed to exactly one source when several faces are
    /// merged.
    /// </summary>
    /// <remarks>
    /// The exclusion list is consulted only while resolving a glyph from a source
    ///.; it does not change what the face contains.
    /// The list shares the <see cref="GlyphRanges"/> representation: two values per range, both
    /// inclusive, terminated by a zero pair. <see cref="ImFontAtlas.AddFont"/> replaces the array with
    /// a private copy so the source never aliases caller memory.
    /// </remarks>
    public uint[]? GlyphExcludeRanges { get; set; }

    /// <summary>Per-source glyph offset in pixels, applied before baking.</summary>
    public ImVec2 GlyphOffset { get; set; } = ImVec2.Zero;

    /// <summary>Minimum glyph advance in pixels; zero disables the clamp.</summary>
    public float GlyphMinAdvanceX { get; set; }

    /// <summary>Maximum glyph advance in pixels.</summary>
    public float GlyphMaxAdvanceX { get; set; } = float.MaxValue;

    /// <summary>Extra advance added to every glyph in pixels.</summary>
    public float GlyphExtraAdvanceX { get; set; }

    /// <summary>Index of the font inside a TrueType collection.</summary>
    public uint FontNo { get; set; }

    /// <summary>Loader-specific flags; unused by the default stb_truetype loader.</summary>
    public uint FontLoaderFlags { get; set; }

    /// <summary>Linear brightness multiplier applied to rasterized coverage.</summary>
    public float RasterizerMultiply { get; set; } = 1.0f;

    /// <summary>DPI scale multiplier applied at rasterization time.</summary>
    public float RasterizerDensity { get; set; } = 1.0f;

    /// <summary>Extra rasterizer scale applied on top of <see cref="SizePixels"/>.</summary>
    public float ExtraSizeScale { get; set; } = 1.0f;

    /// <summary>Font flags for this source.</summary>
    public ImFontFlags Flags { get; set; } = ImFontFlags.None;

    /// <summary>
    /// Cached TrueType parser for this source, initialized on the first glyph bake. Mirrors the
    /// canonical per-source loader state <c>ImFontConfig::FontLoaderData</c> and the loader's
    /// <c>ImGui_ImplStbTrueType_FontSrcData</c>.
    /// Each source keeps its own parser so a merged source can rasterize codepoints the first source
    /// does not contain.
    /// </summary>
    internal ImStbTrueTypeFont? StbFont;

    /// <summary>
    /// Font-unit-to-pixel scale for this source, including <see cref="ExtraSizeScale"/> and, for a
    /// merged source, the canonical relative-size factor
    /// </summary>
    internal float StbScaleFactor;

    /// <summary>Whether <see cref="StbFont"/> initialization was attempted for this source.</summary>
    internal bool StbInitialized;

    /// <summary>Target font that this source feeds, assigned during baking.</summary>
    public ImFont? DstFont { get; set; }

    /// <summary>
    /// Creates a shallow copy of this configuration. Canonical <c>AddFont*</c> entry points copy a
    /// configuration template by value before altering it; the managed port performs the equivalent copy here so a
    /// caller-supplied instance is never mutated or compounded across calls.
    /// </summary>
    /// <returns>A new configuration with the same field values; array and object references are shared.</returns>
    public ImFontConfig Clone() => new()
    {
        Name = Name,
        FontData = FontData,
        FontDataSize = FontDataSize,
        FontDataOwnedByAtlas = FontDataOwnedByAtlas,
        MergeMode = MergeMode,
        PixelSnapH = PixelSnapH,
        PixelSnapV = PixelSnapV,
        OversampleH = OversampleH,
        OversampleV = OversampleV,
        EllipsisChar = EllipsisChar,
        SizePixels = SizePixels,
        GlyphRanges = GlyphRanges,
        GlyphExcludeRanges = GlyphExcludeRanges,
        GlyphOffset = GlyphOffset,
        GlyphMinAdvanceX = GlyphMinAdvanceX,
        GlyphMaxAdvanceX = GlyphMaxAdvanceX,
        GlyphExtraAdvanceX = GlyphExtraAdvanceX,
        FontNo = FontNo,
        FontLoaderFlags = FontLoaderFlags,
        RasterizerMultiply = RasterizerMultiply,
        RasterizerDensity = RasterizerDensity,
        ExtraSizeScale = ExtraSizeScale,
        Flags = Flags,
        DstFont = DstFont,
    };
}

#endregion

#region ImFontGlyph

/// <summary>
/// Rendering data for one glyph. Mirrors canonical <c>ImFontGlyph</c>
///. The canonical 1-bit/1-bit/4-bit/26-bit bitfields
/// are flattened into separate fields, which is semantically identical on the managed runtime.
/// </summary>
public struct ImFontGlyph
{
    /// <summary>Set when the glyph carries color and should ignore tinting.</summary>
    public bool Colored;

    /// <summary>Set when the glyph has visible pixels; clears the early-out test for whitespace.</summary>
    public bool Visible;

    /// <summary>Index of the source font that supplied this glyph.</summary>
    public uint SourceIdx;

    /// <summary>Unicode codepoint this glyph represents.</summary>
    public uint Codepoint;

    /// <summary>Horizontal distance from the current cursor position to the next glyph.</summary>
    public float AdvanceX;

    /// <summary>Left edge of the glyph quad relative to the cursor.</summary>
    public float X0;

    /// <summary>Top edge of the glyph quad relative to the cursor.</summary>
    public float Y0;

    /// <summary>Right edge of the glyph quad relative to the cursor.</summary>
    public float X1;

    /// <summary>Bottom edge of the glyph quad relative to the cursor.</summary>
    public float Y1;

    /// <summary>Left texture coordinate of the glyph quad.</summary>
    public float U0;

    /// <summary>Top texture coordinate of the glyph quad.</summary>
    public float V0;

    /// <summary>Right texture coordinate of the glyph quad.</summary>
    public float U1;

    /// <summary>Bottom texture coordinate of the glyph quad.</summary>
    public float V1;

    /// <summary>Atlas rectangle identifier, or -1 when the glyph has no rectangle.</summary>
    public int PackId;
}

#endregion

#region ImFontBaked

/// <summary>
/// Baked glyph set and metrics for one font at one pixel size and rasterizer density. Mirrors
/// canonical <c>ImFontBaked</c>. An instance is created
/// lazily the first time its size is requested, and glyphs are added on demand as text at that size
/// is measured or rendered.
/// </summary>
/// <remarks>
/// The lookup tables use two sentinels from canonical
///.: <see cref="GlyphIndexUnused"/> means the
/// codepoint has not been requested yet, while <see cref="GlyphIndexNotFound"/> means the source
/// font has no such glyph and the fallback advance applies. This distinction lets a lazy bake avoid
/// repeating a failed lookup while still growing the table for later codepoints.
/// </remarks>
public sealed class ImFontBaked
{
    /// <summary>Lookup value for a codepoint that has not been requested yet.</summary>
    public const int GlyphIndexUnused = -1;

    /// <summary>Lookup value for a codepoint the source font does not contain.</summary>
    public const int GlyphIndexNotFound = -2;

    /// <summary>Advance width per codepoint, or -1 when the codepoint has not been baked.</summary>
    /// <returns>The advance table, sized to the baked codepoint span.</returns>
    public float[] IndexAdvanceX = Array.Empty<float>();

    /// <summary>Advance used when a codepoint has no baked glyph.</summary>
    public float FallbackAdvanceX;

    /// <summary>Pixel size this baked set was rasterized at.</summary>
    public float Size;

    /// <summary>Rasterizer density this baked set was created for.</summary>
    public float RasterizerDensity = 1.0f;

    /// <summary>Codepoint-to-glyph index lookup using the <c>GlyphIndex*</c> sentinels.</summary>
    /// <returns>The lookup table, sized to the baked codepoint span.</returns>
    public int[] IndexLookup = Array.Empty<int>();

    /// <summary>All baked glyphs.</summary>
    /// <returns>The glyph list, indexed by the lookup table and by <see cref="FallbackGlyphIndex"/>.</returns>
    public readonly List<ImFontGlyph> Glyphs = new();

    /// <summary>Index of the fallback glyph in <see cref="Glyphs"/>, or -1 before it is set up.</summary>
    public int FallbackGlyphIndex = -1;

    /// <summary>Distance from the text baseline up to the top of the line, in pixels.</summary>
    public float Ascent;

    /// <summary>Distance from the text baseline down to the bottom of the line, in pixels.</summary>
    public float Descent;

    /// <summary>Total surface in pixels occupied by this set's packed glyph rectangles.</summary>
    public int MetricsTotalSurface;

    /// <summary>Owning font, or <see langword="null"/> before baking.</summary>
    public ImFont? OwnerFont;

    /// <summary>
    /// Whether the atlas has discarded this baked set. A discarded set keeps its identity fields but
    /// loses its glyph output, and the atlas skips it when evacuating idle bakes so it is never
    /// discarded twice. Mirrors canonical <c>ImFontBaked::WantDestroy</c>
    /// </summary>
    public bool WantDestroy;

    /// <summary>
    /// Value of <see cref="ImFontAtlas.FrameCount"/> when this baked set was last fetched through
    /// <see cref="ImFont.GetFontBaked"/>. The atlas treats a set whose
    /// <c>LastUsedFrame + unusedFrames</c> has fallen behind the current frame count as idle and
    /// returns its packed rectangles to the packer. Mirrors canonical <c>ImFontBaked::LastUsedFrame</c>
    /// </summary>
    public int LastUsedFrame;

    /// <summary>
    /// Stable identifier for this baked set, derived from the owning font's identity together with
    /// the set's size and rasterizer density. Mirrors canonical <c>ImFontBaked::BakedId</c>,
    /// computed by <c>ImFontAtlasBakedGetId</c> and stored when the baked storage is created.
    /// </summary>
    /// <remarks>
    /// Because the value is a pure function of the font, size and density, it does not change while
    /// the bake lives and two bakes at different sizes (or densities) receive different values. It
    /// is the key canonical would use in the atlas baked map; this port looks bakes up by size and
    /// density instead, so the field is currently informational.
    /// </remarks>
    public uint BakedId { get; internal set; }

    /// <summary>
    /// When true, a missing codepoint is reported without installing the fallback glyph. Mirrors
    /// canonical <c>ImFontBaked::LoadNoFallback</c> and is set only around a
    /// <see cref="FindGlyphNoFallback"/> query.
    /// </summary>
    internal bool LoadNoFallback;

    /// <summary>Atlas that owns the texture receiving newly baked glyphs, or null before baking.</summary>
    private ImFontAtlas? Atlas => OwnerFont?.OwnerAtlas;

    /// <summary>
    /// Finds a glyph, baking it on demand and returning the fallback glyph when the source font has
    /// no such codepoint. Mirrors canonical <c>ImFontBaked::FindGlyph</c>
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns>The matching glyph, the fallback glyph, or a synthetic invisible glyph when the
    /// atlas is unavailable or has no fallback.</returns>
    public ImFontGlyph FindGlyph(uint c)
    {
        if (c < (uint)IndexLookup.Length)
        {
            int i = IndexLookup[c];
            if (i == GlyphIndexNotFound)
                return GetFallbackGlyph(c);
            if (i != GlyphIndexUnused)
                return Glyphs[i];
        }
        int loaded = Atlas?.LoadGlyph(this, c, onlyAdvanceX: false) ?? -1;
        if (loaded >= 0)
            return Glyphs[loaded];
        return GetFallbackGlyph(c);
    }

    /// <summary>
    /// Finds a glyph without installing or applying the fallback. Mirrors canonical
    /// <c>ImFontBaked::FindGlyphNoFallback</c>.
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns>The matching glyph, or <see langword="null"/> when the source font does not have it.</returns>
    public ImFontGlyph? FindGlyphNoFallback(uint c)
    {
        if (c < (uint)IndexLookup.Length)
        {
            int i = IndexLookup[c];
            if (i == GlyphIndexNotFound)
                return null;
            if (i != GlyphIndexUnused)
                return Glyphs[i];
        }
        LoadNoFallback = true;
        int loaded = Atlas?.LoadGlyph(this, c, onlyAdvanceX: false) ?? -1;
        LoadNoFallback = false;
        return loaded >= 0 ? Glyphs[loaded] : null;
    }

    /// <summary>
    /// Returns the advance width of a codepoint, baking it on demand. Mirrors canonical
    /// <c>ImFontBaked::GetCharAdvance</c> and the advance-only layout mode
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns>The advance width in pixels at <see cref="Size"/>.</returns>
    public float GetCharAdvance(uint c)
    {
        if (c < (uint)IndexAdvanceX.Length)
        {
            // Missing glyphs inside the table already store the fallback advance.
            float x = IndexAdvanceX[c];
            if (x >= 0.0f)
                return x;
        }
        return Atlas?.LoadGlyphAdvance(this, c) ?? FallbackAdvanceX;
    }

    /// <summary>
    /// Reports whether the codepoint has a baked glyph without triggering a bake. Mirrors canonical
    /// <c>ImFontBaked::IsGlyphLoaded</c>.
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns><see langword="true"/> when <see cref="IndexLookup"/> names a loaded glyph.</returns>
    public bool IsGlyphLoaded(uint c) => c < (uint)IndexLookup.Length && IndexLookup[c] >= 0;

    /// <summary>
    /// Grows both lookup tables so they cover a newly requested codepoint. Mirrors canonical
    /// <c>ImFontBaked_BuildGrowIndex</c>
    /// </summary>
    /// <param name="newSize">Minimum number of codepoint entries the tables must hold.</param>
    internal void GrowIndex(int newSize)
    {
        if (newSize <= IndexLookup.Length)
            return;

        int oldLength = IndexLookup.Length;
        Array.Resize(ref IndexLookup, newSize);
        Array.Resize(ref IndexAdvanceX, newSize);
        for (int i = oldLength; i < newSize; i++)
        {
            IndexLookup[i] = GlyphIndexUnused;
            IndexAdvanceX[i] = -1.0f;
        }
    }

    /// <summary>
    /// Releases everything this baked set produced: glyph output, advance tables and cached vertical
    /// metrics. Identity fields (size, density and owning font) are preserved so a discarded set
    /// remains inspectable. Mirrors canonical <c>ImFontBaked::ClearOutputData</c>
    /// </summary>
    internal void ClearOutputData()
    {
        FallbackAdvanceX = 0.0f;
        Glyphs.Clear();
        IndexAdvanceX = Array.Empty<float>();
        IndexLookup = Array.Empty<int>();
        FallbackGlyphIndex = -1;
        Ascent = 0.0f;
        Descent = 0.0f;
        MetricsTotalSurface = 0;
    }

    /// <summary>
    /// Resolves the fallback glyph, or a synthetic invisible glyph when none was installed.
    /// </summary>
    /// <param name="c">Codepoint the synthetic glyph should report.</param>
    /// <returns>The fallback glyph or an invisible placeholder.</returns>
    private ImFontGlyph GetFallbackGlyph(uint c)
    {
        if (FallbackGlyphIndex >= 0 && FallbackGlyphIndex < Glyphs.Count)
            return Glyphs[FallbackGlyphIndex];
        return new ImFontGlyph { Codepoint = c, AdvanceX = FallbackAdvanceX, PackId = -1 };
    }
}

#endregion

#region ImFont

/// <summary>
/// A loaded font that can measure and render text at any requested size. Mirrors canonical
/// <c>ImFont</c>.
/// </summary>
public sealed class ImFont
{
    /// <summary>Most recently requested baked glyph set, used as a one-entry cache.</summary>
    public ImFontBaked? LastBaked { get; internal set; }

    /// <summary>Atlas that owns this font, or <see langword="null"/> before it is added.</summary>
    public ImFontAtlas? OwnerAtlas { get; internal set; }

    /// <summary>
    /// Baked sets created for this font, one per requested (size, density) pair. Mirrors the
    /// canonical builder's baked pool filtered by owner font
    /// </summary>
    /// <returns>The baked sets in creation order.</returns>
    internal readonly List<ImFontBaked> BakedList = new();

    /// <summary>Lookup of baked sets by rounded size and density; avoids rescanning
    /// <see cref="BakedList"/> for repeated requests.</summary>
    private readonly Dictionary<BakedSizeKey, ImFontBaked> _bakedBySize = new();

    /// <summary>
    /// Key for the per-size baked-set cache. Canonical hashes the same two values
    /// </summary>
    private readonly struct BakedSizeKey : IEquatable<BakedSizeKey>
    {
        /// <summary>Rounded pixel size.</summary>
        public readonly float Size;

        /// <summary>Rasterizer density.</summary>
        public readonly float Density;

        /// <summary>Creates a cache key for one size and density.</summary>
        /// <param name="size">Rounded pixel size.</param>
        /// <param name="density">Rasterizer density.</param>
        public BakedSizeKey(float size, float density)
        {
            Size = size;
            Density = density;
        }

        /// <summary>Compares two keys by size and density.</summary>
        /// <param name="other">Key to compare against.</param>
        /// <returns><see langword="true"/> when both components are equal.</returns>
        public bool Equals(BakedSizeKey other) => Size == other.Size && Density == other.Density;

        /// <summary>Compares this key against an arbitrary object.</summary>
        /// <param name="obj">Object to compare against.</param>
        /// <returns><see langword="true"/> when the object is an equal key.</returns>
        public override bool Equals(object? obj) => obj is BakedSizeKey other && Equals(other);

        /// <summary>Returns a hash combining size and density.</summary>
        /// <returns>A hash code suitable for the baked-set dictionary.</returns>
        public override int GetHashCode() => HashCode.Combine(Size, Density);
    }

    /// <summary>Font flags.</summary>
    public ImFontFlags Flags { get; set; } = ImFontFlags.None;

    /// <summary>Current rasterizer density of the font.</summary>
    public float CurrentRasterizerDensity { get; set; } = 1.0f;

    /// <summary>
    /// Identifier unique within the owning atlas, assigned when the font is created. Mirrors
    /// canonical <c>ImFont::FontId</c>., which is drawn from
    /// the atlas <c>FontNextUniqueID</c> counter on add
    /// </summary>
    /// <remarks>
    /// Merged sources join an existing font and therefore do not allocate a new identifier. The
    /// value seeds each baked set's <see cref="ImFontBaked.BakedId"/> and is stable for the life of
    /// the font.
    /// </remarks>
    public uint FontId { get; internal set; }

    /// <summary>Font source descriptions merged into this font.</summary>
    /// <returns>The source list for this font.</returns>
    public readonly List<ImFontConfig> Sources = new();

    /// <summary>
    /// Codepoint used for ellipsis rendering, or zero before a source resolves it. Canonical leaves
    /// the field zero on construction and fills it in <c>ImFontAtlasBuildSetupFontSpecialGlyphs</c>
    ///.; the default bitmap font explicitly requests
    /// U+0085 when it is added.
    /// </summary>
    public uint EllipsisChar { get; set; }

    /// <summary>
    /// Codepoint substituted when a requested glyph is missing, or zero before a source resolves a
    /// usable face character. Canonical leaves the field zero on construction and auto-detects it in
    /// <c>ImFontAtlasBuildSetupFontSpecialGlyphs</c>.
    /// </summary>
    public uint FallbackChar { get; set; }

    /// <summary>
    /// Whether <see cref="EllipsisChar"/> must be synthesized from period glyphs because no source
    /// supplies it. Mirrors canonical <c>ImFont::EllipsisAutoBake</c>
    /// </summary>
    /// <remarks>
    /// The lazy glyph resolver synthesizes the three-period ellipsis only while this flag is set
    ///. Clearing it lets a caller substitute a
    /// different ellipsis codepoint without triggering synthesis, as canonical documents.
    /// </remarks>
    public bool EllipsisAutoBake { get; set; }

    /// <summary>
    /// Sparse codepoint remapping pairs installed by <see cref="AddRemapChar"/>. Each entry maps a
    /// requested codepoint to the codepoint whose glyph must be used for it; the table stays empty
    /// until a remap is requested. Mirrors canonical <c>ImFont::RemapPairs</c>
    ///., an <c>ImGuiStorage</c> keyed by codepoint.
    /// </summary>
    /// <remarks>
    /// A dictionary is used because the port already represents sparse codepoint tables this way. The
    /// table is consulted only while a glyph is lazily resolved, and only when it is non-empty, matching
    /// canonical <c>ImFontAtlas_FontHookRemapCodepoint</c>
    ///. The member is <see langword="internal"/>
    /// rather than private because canonical exposes it as an inspectable font field and the font debug
    /// nodes report it.
    /// </remarks>
    internal readonly Dictionary<uint, uint> RemapPairs = new();

    /// <summary>Legacy uniform font scale.</summary>
    public float Scale { get; set; } = 1.0f;

    /// <summary>
    /// Reference size in pixels captured when the font was added, used by legacy helpers that need a
    /// default size. Mirrors canonical <c>ImFont::LegacySize</c>, assigned from the add-time
    /// <see cref="ImFontConfig.SizePixels"/> and read by the legacy preload and custom-rectangle paths.
    /// </summary>
    public float LegacySize { get; set; } = 13.0f;

    public const int RemapValueGlyphIdxShift = 0;
    public const int RemapValueGlyphIdxMask = 0xFFFF;
    public const int RemapValueFontSrcIdxShift = 16;
    public const int RemapValueFontSrcIdxMask = 0xFF0000;

    /// <summary>
    /// Reports whether any attached source face maps a codepoint to a real glyph, without baking or
    /// packing it. Mirrors canonical <c>ImFont::IsGlyphInFont</c>
    ///., which asks each source's loader whether its
    /// face contains the codepoint.
    /// </summary>
    /// <param name="c">Codepoint to look up.</param>
    /// <returns><see langword="true"/> when at least one source face contains the codepoint.</returns>
    /// <remarks>
    /// The managed loader answers with the same cmap lookup used by rasterization
    /// (<see cref="ImStbTrueTypeFont.FindGlyphIndex"/>), so the result matches what lazy baking would
    /// find. The codepoint is first resolved through <see cref="AddRemapCodepoint"/>, so a remapped codepoint
    /// is reported present when its target is present
    ///. Excluded ranges are deliberately ignored, matching
    /// canonical: exclusion decides which source supplies a requested glyph, not whether the face
    /// contains it.
    /// </remarks>
    public bool IsGlyphInFont(uint c)
    {
        // Canonical remaps the codepoint before asking any source face, so a remapped codepoint whose
        // target exists is reported present.
        c = RemapCodepoint(c, out int srcFontIdx, out int srcGlyphIdx);
        if (OwnerAtlas == null)
            return false;
        if (c == uint.MaxValue)
        {
            if (srcFontIdx >= 0 && srcFontIdx < Sources.Count)
            {
                ImFontConfig source = Sources[srcFontIdx];
                OwnerAtlas.EnsureStbFont(this, source);
                return source.StbFont != null && srcGlyphIdx != 0;
            }
            return false;
        }
        if (c > int.MaxValue)
            return false;
        for (int i = 0; i < Sources.Count; i++)
        {
            ImFontConfig source = Sources[i];
            OwnerAtlas.EnsureStbFont(this, source);
            if (source.StbFont != null && source.StbFont.FindGlyphIndex((int)c) != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Remaps a codepoint to another codepoint. Mirrors canonical <c>ImFont::AddRemapCodepoint</c>
    /// </summary>
    /// <param name="fromCodepoint">Codepoint that callers request.</param>
    /// <param name="toCodepoint">Codepoint whose glyph supplies the requested codepoint.</param>
    public void AddRemapCodepoint(uint fromCodepoint, uint toCodepoint)
    {
        uint data = toCodepoint;
        ImAssert.Check((data & (1u << 31)) == 0);
        RemapPairs[fromCodepoint] = data;
    }

    /// <summary>
    /// Remaps a codepoint to a raw glyph index inside the font. Allows accessing glyphs which are not mapped to codepoints.
    /// Mirrors canonical <c>ImFont::AddRemapCodepointToGlyphIndex</c>.
    /// </summary>
    /// <param name="fromCodepoint">Codepoint that callers request.</param>
    /// <param name="fontSrcIdx">Index in <see cref="Sources"/>, or -1 for last added source.</param>
    /// <param name="glyphIdx">Index in TTF or OTF font file.</param>
    public void AddRemapCodepointToGlyphIndex(uint fromCodepoint, int fontSrcIdx, int glyphIdx)
    {
        if (fontSrcIdx == -1)
            fontSrcIdx = Sources.Count - 1;
        ImAssert.Check(fontSrcIdx >= 0 && fontSrcIdx < Sources.Count && ((fontSrcIdx << RemapValueFontSrcIdxShift) & ~RemapValueFontSrcIdxMask) == 0);
        ImAssert.Check(glyphIdx >= 0 && ((glyphIdx << RemapValueGlyphIdxShift) & ~RemapValueGlyphIdxMask) == 0);
        uint data = ((uint)glyphIdx << RemapValueGlyphIdxShift) | ((uint)fontSrcIdx << RemapValueFontSrcIdxShift) | (1u << 31);
        RemapPairs[fromCodepoint] = data;
    }

    /// <summary>
    /// Obsolete alias for <see cref="AddRemapCodepoint"/>.
    /// </summary>
    [Obsolete("Use AddRemapCodepoint instead.")]
    public void AddRemapChar(uint fromCodepoint, uint toCodepoint)
    {
        AddRemapCodepoint(fromCodepoint, toCodepoint);
    }

    /// <summary>
    /// Resolves a requested codepoint through <see cref="RemapPairs"/>. Mirrors canonical
    /// <c>ImFontAtlas_FontHookRemapCodepoint</c>.
    /// </summary>
    /// <param name="c">Codepoint to resolve.</param>
    /// <param name="outFontSrcIdx">Output font source index when mapped to a raw glyph index, or -1.</param>
    /// <param name="outGlyphIdx">Output raw glyph index when mapped, or -1.</param>
    /// <returns>The mapped codepoint, uint.MaxValue if mapped to a raw glyph index, or <paramref name="c"/> unchanged when no remap is installed.</returns>
    internal uint RemapCodepoint(uint c, out int outFontSrcIdx, out int outGlyphIdx)
    {
        outFontSrcIdx = -1;
        outGlyphIdx = -1;
        if (RemapPairs.Count != 0 && RemapPairs.TryGetValue(c, out uint data))
        {
            if ((data & (1u << 31)) != 0)
            {
                outFontSrcIdx = (int)((data & (uint)RemapValueFontSrcIdxMask) >> RemapValueFontSrcIdxShift);
                outGlyphIdx = (int)((data & (uint)RemapValueGlyphIdxMask) >> RemapValueGlyphIdxShift);
                return uint.MaxValue;
            }
            return data;
        }
        return c;
    }

    /// <summary>
    /// Resolves a requested codepoint through <see cref="RemapPairs"/>. Mirrors canonical
    /// <c>ImFontAtlas_FontHookRemapCodepoint</c>.
    /// </summary>
    /// <param name="c">Codepoint to resolve.</param>
    /// <returns>The mapped codepoint, or <paramref name="c"/> unchanged when no remap is installed.</returns>
    internal uint RemapCodepoint(uint c)
    {
        if (RemapPairs.Count != 0 && RemapPairs.TryGetValue(c, out uint data))
        {
            if ((data & (1u << 31)) != 0)
                return uint.MaxValue;
            return data;
        }
        return c;
    }

    /// <summary>
    /// One bit per block of 8192 codepoints, set when a bubble of that block has at least one baked
    /// glyph. Mirrors canonical <c>ImFont::Used8kPagesMap</c>
    ///., which is primarily used to iterate across all used
    /// codepoints without walking the full Unicode space.
    /// </summary>
    /// <remarks>
    /// Canonical sizes the map <c>(IM_UNICODE_CODEPOINT_MAX+1)/8192/8</c> bytes, which is 17 bytes for
    /// the port's 32-bit codepoint range. The map is written when a glyph is appended to a baked set
    /// (<see cref="ImFontAtlas"/>'s <c>AddGlyph</c>) and cleared by <see cref="ClearOutputData"/>.
    /// </remarks>
    internal readonly byte[] Used8kPagesMap = new byte[((int)UnicodeCodepointMax + 1) / 8192 / 8];

    /// <summary>
    /// Whether this font is currently attached to an atlas. Mirrors canonical <c>ImFont::IsLoaded</c>
    ///., which reports <c>OwnerAtlas != NULL</c>.
    /// </summary>
    /// <returns><see langword="true"/> when the font has an owning atlas.</returns>
    public bool IsLoaded => OwnerAtlas != null;

    /// <summary>
    /// Returns the debug name of the font. Mirrors canonical <c>ImFont::GetDebugName</c>
    ///., which reports the first source's
    /// <c>ImFontConfig::Name</c> or the literal <c>"&lt;unknown&gt;"</c> when the font has no source.
    /// </summary>
    /// <returns>The first source name, or <c>"&lt;unknown&gt;"</c> when no source is attached.</returns>
    public string GetDebugName() => Sources.Count != 0 ? Sources[0].Name : "<unknown>";

    /// <summary>
    /// Releases every baked glyph set owned by this font, clears <see cref="Used8kPagesMap"/>, and
    /// drops the <see cref="LastBaked"/> cache. Mirrors canonical <c>ImFont::ClearOutputData</c>
    ///., which is also invoked from the canonical
    /// destructor.
    /// </summary>
    /// <remarks>
    /// Passes an unused-frame count of zero so every bake is released immediately, matching canonical
    /// <c>ImFontAtlasFontDiscardBakes(atlas, this, 0)</c>. Canonical leaves <c>FallbackChar</c> and
    /// <c>EllipsisChar</c> untouched (the corresponding assignment is commented out); this port
    /// reproduces that behavior. A font with no owning atlas simply clears its output state.
    /// </remarks>
    public void ClearOutputData()
    {
        if (OwnerAtlas is ImFontAtlas atlas)
            atlas.FontDiscardBakes(this, 0);
        Array.Clear(Used8kPagesMap, 0, Used8kPagesMap.Length);
        LastBaked = null;
    }

    /// <summary>
    /// Reports whether no baked glyph has been recorded for any codepoint in the inclusive range.
    /// Mirrors canonical <c>ImFont::IsGlyphRangeUnused</c>
    ///., which the font preloading path uses to skip
    /// building ranges that no source needs.
    /// </summary>
    /// <param name="cBegin">First codepoint of the range.</param>
    /// <param name="cLast">Last codepoint of the range, inclusive.</param>
    /// <returns><see langword="true"/> when no block covering the range has a baked glyph.</returns>
    /// <remarks>
    /// The 8192-codepoint block size is intentionally not exposed by the public API, so callers pass
    /// ordinary codepoint ranges such as <c>IsGlyphRangeUnused(0, 255)</c>. Block indices beyond the
    /// map are ignored, exactly as canonical bounds-checks before indexing.
    /// </remarks>
    public bool IsGlyphRangeUnused(uint cBegin, uint cLast)
    {
        uint pageBegin = cBegin / 8192;
        uint pageLast = cLast / 8192;
        for (uint page = pageBegin; page <= pageLast; page++)
        {
            if ((page >> 3) < Used8kPagesMap.Length
                && (Used8kPagesMap[page >> 3] & (1 << (int)(page & 7))) != 0)
                return false;
        }
        return true;
    }

    /// <summary>Largest codepoint the port represents, matching canonical <c>IM_UNICODE_CODEPOINT_MAX</c>.</summary>
    private const uint UnicodeCodepointMax = 0x10FFFF;

    /// <summary>
    /// Returns the baked glyph set for a requested size, creating and caching it on first use.
    /// Mirrors canonical <c>ImFont::GetFontBaked</c>
    ///. Each distinct rounded size receives an
    /// independent <see cref="ImFontBaked"/>, so glyph advances and line metrics are rasterized at
    /// that size rather than scaled from another.
    /// </summary>
    /// <param name="size">Requested size in pixels; rounded to the nearest whole pixel.</param>
    /// <param name="density">Requested rasterizer density; negative selects the current density.</param>
    /// <returns>The baked glyph set for the rounded size and density.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the font has no owning atlas.</exception>
    public ImFontBaked GetFontBaked(float size, float density = -1.0f)
    {
        return GetFontBakedCore(size, density, allowLockedCreation: false);
    }

    /// <summary>
    /// Resolves the baked glyph set, optionally allowing creation while the atlas is locked. Build-time preloads
    /// pass <c>allowLockedCreation: true</c> because they run inside <see cref="ImFontAtlas.Build"/>, which sets
    /// the lock as its own guard before any backend could receive the texture.
    /// </summary>
    /// <param name="size">Requested size in pixels.</param>
    /// <param name="density">Requested rasterizer density; negative selects the current density.</param>
    /// <param name="allowLockedCreation">When <see langword="true"/>, a locked atlas may create a new bake.</param>
    /// <returns>The baked glyph set.</returns>
    internal ImFontBaked GetFontBakedCore(float size, float density, bool allowLockedCreation)
    {
        ImFontAtlas atlas = OwnerAtlas
            ?? throw new InvalidOperationException("Font is not attached to an atlas; add it with ImFontAtlas.AddFont first.");
        if (density < 0.0f)
            density = CurrentRasterizerDensity;

        // Canonical rounds the size before hashing so equivalent requests share one baked set.
        float roundedSize = MathF.Floor(size + 0.5f);
        if (roundedSize <= 0.0f)
            roundedSize = LegacySize > 0.0f ? LegacySize : 13.0f;

        if (LastBaked != null && LastBaked.Size == roundedSize && LastBaked.RasterizerDensity == density)
        {
            LastBaked.LastUsedFrame = atlas.FrameCount;
            return LastBaked;
        }

        var key = new BakedSizeKey(roundedSize, density);
        if (_bakedBySize.TryGetValue(key, out ImFontBaked? cached))
        {
            cached.LastUsedFrame = atlas.FrameCount;
            LastBaked = cached;
            return cached;
        }

        // A locked atlas (its renderer cannot receive new texture data) must not create a bake. Canonical
        // substitutes the closest existing bake instead.
        if (!allowLockedCreation && (atlas.Locked || (Flags & ImFontFlags.LockBakedSizes) != 0))
        {
            ImFontBaked? closest = FindClosestBaked(roundedSize, density);
            if (closest != null)
            {
                closest.LastUsedFrame = atlas.FrameCount;
                LastBaked = closest;
                return closest;
            }
            if (atlas.Locked)
                throw new InvalidOperationException(
                    "Cannot use dynamic font size with a locked ImFontAtlas: the renderer does not support texture updates.");
        }

        ImFontBaked created = atlas.AddBaked(this, roundedSize, density);
        _bakedBySize[key] = created;
        LastBaked = created;
        return created;
    }

    /// <summary>
    /// Finds the existing bake closest to a requested size, preferring the same rasterizer density in a first
    /// pass. Mirrors canonical <c>ImFontAtlasBakedGetClosestMatch</c>.
    /// </summary>
    /// <param name="size">Requested rounded size in pixels.</param>
    /// <param name="density">Requested rasterizer density.</param>
    /// <returns>The chosen bake, or <see langword="null"/> when this font has no usable bake.</returns>
    /// <remarks>
    /// The first pass accepts only bakes at the requested density. The second pass accepts any density. In each
    /// pass a larger bake wins when it is at most twice the requested size and the smaller candidate is also
    /// more than half the requested size; otherwise the closest smaller bake wins. Baked sets marked for
    /// destruction are skipped.
    /// </remarks>
    private ImFontBaked? FindClosestBaked(float size, float density)
    {
        for (int stepN = 0; stepN < 2; stepN++)
        {
            ImFontBaked? closestLarger = null;
            ImFontBaked? closestSmaller = null;
            foreach (ImFontBaked baked in BakedList)
            {
                if (baked.WantDestroy)
                    continue;
                if (stepN == 0 && baked.RasterizerDensity != density)
                    continue;
                if (baked.Size > size && (closestLarger == null || baked.Size < closestLarger.Size))
                    closestLarger = baked;
                if (baked.Size < size && (closestSmaller == null || baked.Size > closestSmaller.Size))
                    closestSmaller = baked;
            }
            if (closestLarger != null
                && (closestSmaller == null || (closestLarger.Size >= size * 2.0f && closestSmaller.Size > size * 0.5f)))
                return closestLarger;
            if (closestSmaller != null)
                return closestSmaller;
        }
        return null;
    }

    /// <summary>
    /// Removes a baked glyph set from this font and releases its output. The atlas calls this only
    /// after it has returned the set's packed rectangles to the packer, so the space is already
    /// available for reuse. Mirrors the font-side portion of canonical <c>ImFontAtlasBakedDiscard</c>
    ///.: the size cache entry is dropped so a later
    /// request at the same size creates a fresh set instead of resurrecting the discarded one.
    /// </summary>
    /// <param name="baked">Baked set to discard; it must belong to this font.</param>
    internal void DiscardBaked(ImFontBaked baked)
    {
        baked.WantDestroy = true;
        baked.ClearOutputData();
        BakedList.Remove(baked);
        _bakedBySize.Remove(new BakedSizeKey(baked.Size, baked.RasterizerDensity));
        if (ReferenceEquals(LastBaked, baked))
            LastBaked = null;
    }

    /// <summary>
    /// Looks up a character's glyph using this font's reference size. Mirrors the legacy
    /// <c>FindGlyph</c> convenience used by existing call sites.
    /// </summary>
    /// <param name="c">Character to look up.</param>
    /// <returns>The matching or fallback glyph.</returns>
    public ImFontGlyph FindGlyph(char c) => GetFontBaked(LegacySize).FindGlyph(c);

    /// <summary>
    /// Returns a character's advance at this font's reference size. Mirrors the legacy
    /// <c>GetCharAdvance</c> convenience.
    /// </summary>
    /// <param name="c">Character to measure.</param>
    /// <returns>The advance width in pixels.</returns>
    public float GetCharAdvance(char c) => GetFontBaked(LegacySize).GetCharAdvance(c);

    /// <summary>
    /// Measures a UTF-16 text run with canonical <c>ImFontCalcTextSizeEx</c> semantics. Mirrors
    /// canonical <c>ImFont::CalcTextSizeA</c>.
    /// </summary>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="maxWidth">Maximum line width; rendering stops before exceeding it.</param>
    /// <param name="wrapWidth">Word-wrap width, or zero to disable wrapping.</param>
    /// <param name="text">Text to measure.</param>
    /// <returns>The measured width and height in pixels.</returns>
    public ImVec2 CalcTextSizeA(float size, float maxWidth, float wrapWidth, ReadOnlySpan<char> text)
        => ImFontCalcTextSizeEx(this, size, maxWidth, wrapWidth, text, out _, out _);

    /// <summary>
    /// Measures a text run and reports how much of it was consumed. Ports
    /// <c>ImFontCalcTextSizeEx</c>.
    /// </summary>
    /// <param name="font">Font to measure with.</param>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="maxWidth">Maximum line width; stops before exceeding it.</param>
    /// <param name="wrapWidth">Word-wrap width, or zero to disable wrapping.</param>
    /// <param name="text">Text to measure.</param>
    /// <param name="remainingChars">Receives the number of characters not consumed by a <paramref name="maxWidth"/> cutoff.</param>
    /// <param name="offset">Receives the cursor offset after the run, allowing for a trailing newline.</param>
    /// <returns>The measured width and height in pixels.</returns>
    public static ImVec2 ImFontCalcTextSizeEx(ImFont font, float size, float maxWidth, float wrapWidth,
        ReadOnlySpan<char> text, out int remainingChars, out ImVec2 offset)
    {
        ImFontBaked baked = font.GetFontBaked(size);
        float lineHeight = size;
        float scale = lineHeight / baked.Size;

        var textSize = ImVec2.Zero;
        float lineWidth = 0.0f;
        bool wrapEnabled = wrapWidth > 0.0f;
        int wrapEol = -1;

        int s = 0;
        int prevS = 0;
        while (s < text.Length)
        {
            if (wrapEnabled)
            {
                // The wrap position is computed lazily so ordinary text pays no extra cost.
                if (wrapEol < 0)
                    wrapEol = font.CalcWordWrapPosition(size, text.Slice(s), wrapWidth - lineWidth);

                if (s >= s + wrapEol)
                {
                    if (textSize.X < lineWidth)
                        textSize.X = lineWidth;
                    textSize.Y += lineHeight;
                    lineWidth = 0.0f;
                    s = WordWrapNextLineStart(text, s);
                    wrapEol = -1;
                    continue;
                }
            }

            prevS = s;
            uint c = DecodeChar(text, ref s);

            if (c == '\n')
            {
                textSize.X = MathF.Max(textSize.X, lineWidth);
                textSize.Y += lineHeight;
                lineWidth = 0.0f;
                continue;
            }
            if (c == '\r')
                continue;

            float charWidth = baked.GetCharAdvance(c) * scale;

            if (lineWidth + charWidth >= maxWidth)
            {
                s = prevS;
                break;
            }

            lineWidth += charWidth;
        }

        if (textSize.X < lineWidth)
            textSize.X = lineWidth;

        offset = new ImVec2(lineWidth, textSize.Y + lineHeight);

        if (lineWidth > 0.0f || textSize.Y == 0.0f)
            textSize.Y += lineHeight;

        remainingChars = text.Length - s;
        return textSize;
    }

    /// <summary>
    /// Finds the word-wrap position for a text run. Ports <c>ImFontCalcWordWrapPositionEx</c>
    /// </summary>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="text">Text starting at the current line position.</param>
    /// <param name="wrapWidth">Remaining horizontal space in pixels.</param>
    /// <param name="flags">Wrap behavior flags.</param>
    /// <returns>The number of characters that fit before wrapping.</returns>
    public int CalcWordWrapPosition(float size, ReadOnlySpan<char> text, float wrapWidth,
        ImDrawTextFlags flags = ImDrawTextFlags.None)
    {
        ImFontBaked baked = GetFontBaked(size);
        float scale = size / baked.Size;

        float lineWidth = 0.0f;
        float blankWidth = 0.0f;
        float unscaledWrap = wrapWidth / scale; // Work in unscaled widths to avoid per-glyph scaling.

        bool keepBlanks = (flags & ImDrawTextFlags.WrapKeepBlanks) != 0;
        int prevType = WcharClassOther;
        int spanEnd = 0;
        float spanWidth = 0.0f;

        int s = 0;
        while (s < text.Length)
        {
            int charStart = s;
            uint c = DecodeChar(text, ref s);
            int nextS = s;

            if (c < 32)
            {
                if (c == '\n')
                    return charStart; // Direct return skips the "wrap width too small" fallback.
                if (c == '\r')
                    continue;
            }

            float charWidth = baked.GetCharAdvance(c);
            int currType = ClassifyChar(c);

            if (currType == WcharClassBlank)
            {
                if (prevType != WcharClassBlank && !keepBlanks)
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth;
                    spanWidth = 0.0f;
                }
                blankWidth += charWidth;
            }
            else
            {
                if (prevType == WcharClassPunct && currType != WcharClassPunct && !(c >= '0' && c <= '9'))
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth + blankWidth;
                    spanWidth = blankWidth = 0.0f;
                }
                else if (prevType == WcharClassBlank && keepBlanks)
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth + blankWidth;
                    spanWidth = blankWidth = 0.0f;
                }
                spanWidth += charWidth;
            }

            if (spanWidth + blankWidth + lineWidth > unscaledWrap)
            {
                if (spanWidth + blankWidth > unscaledWrap)
                    break;
                return spanEnd;
            }

            prevType = currType;
            _ = nextS;
        }

        // A too-small wrap width would otherwise produce zero characters per line and a height
        // discontinuity; force one character to be displayed.
        if (s == 0 && text.Length > 0)
            return Math.Min(CountUtf8BytesFromChar(text), text.Length);
        return s;
    }

    /// <summary>
    /// Emits one glyph's quad into a draw list. Ports <c>ImFont::RenderChar</c>
    /// </summary>
    /// <param name="drawList">Destination draw list.</param>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="pos">Cursor position.</param>
    /// <param name="col">Packed color.</param>
    /// <param name="c">Codepoint to render.</param>
    /// <param name="cpuFineClip">Optional CPU-side clip rectangle.</param>
    public void RenderChar(ImDrawList drawList, float size, ImVec2 pos, uint col, uint c, ImVec4? cpuFineClip = null)
    {
        ImFontBaked baked = GetFontBaked(size);
        ImFontGlyph glyph = baked.FindGlyph(c);
        if (!glyph.Visible)
            return;

        if (glyph.Colored)
            col |= ~0xFF000000u;

        float scale = size >= 0.0f ? size / baked.Size : 1.0f;
        float x = (drawList.Flags & ImDrawFlags.TextNoPixelSnap) == 0 ? (int)pos.X : pos.X;
        float y = (drawList.Flags & ImDrawFlags.TextNoPixelSnap) == 0 ? (int)pos.Y : pos.Y;

        float x1 = x + glyph.X0 * scale;
        float x2 = x + glyph.X1 * scale;
        float y1 = y + glyph.Y0 * scale;
        float y2 = y + glyph.Y1 * scale;
        float u1 = glyph.U0;
        float v1 = glyph.V0;
        float u2 = glyph.U1;
        float v2 = glyph.V1;

        if (cpuFineClip.HasValue)
        {
            ImVec4 clip = cpuFineClip.Value;
            if (x1 > clip.Z || x2 < clip.X)
                return;
            if (x1 < clip.X) { u1 += (1.0f - (x2 - clip.X) / (x2 - x1)) * (u2 - u1); x1 = clip.X; }
            if (y1 < clip.Y) { v1 += (1.0f - (y2 - clip.Y) / (y2 - y1)) * (v2 - v1); y1 = clip.Y; }
            if (x2 > clip.Z) { u2 = u1 + ((clip.Z - x1) / (x2 - x1)) * (u2 - u1); x2 = clip.Z; }
            if (y2 > clip.W) { v2 = v1 + ((clip.W - y1) / (y2 - y1)) * (v2 - v1); y2 = clip.W; }
            if (y1 >= y2)
                return;
        }

        drawList.PrimReserve(6, 4);
        drawList.PrimRectUV(new ImVec2(x1, y1), new ImVec2(x2, y2), new ImVec2(u1, v1), new ImVec2(u2, v2), col);
    }

    /// <summary>
    /// Emits a whole text run into a draw list with canonical line layout, word wrapping and
    /// pixel snapping. Ports <c>ImFont::RenderText</c>
    /// </summary>
    /// <param name="drawList">Destination draw list.</param>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="pos">Top-left cursor position.</param>
    /// <param name="col">Packed color.</param>
    /// <param name="clipRect">Clip rectangle; text below it is skipped.</param>
    /// <param name="text">Text to render.</param>
    /// <param name="wrapWidth">Word-wrap width, or zero to disable wrapping.</param>
    /// <param name="cpuFineClip">When true, quads are clipped precisely in UV space against <paramref name="clipRect"/>.</param>
    public void RenderText(ImDrawList drawList, float size, ImVec2 pos, uint col, ImVec4 clipRect,
        ReadOnlySpan<char> text, float wrapWidth = 0.0f, bool cpuFineClip = false)
    {
        float x = pos.X;
        float y = pos.Y;
        if (y > clipRect.W)
            return;
        if ((drawList.Flags & ImDrawFlags.TextNoPixelSnap) == 0)
        {
            x = (int)x;
            y = (int)y;
        }

        float lineHeight = size;
        ImFontBaked baked = GetFontBaked(size);
        float scale = size / baked.Size;
        float originX = x;
        bool wrapEnabled = wrapWidth > 0.0f;

        // Fast-forward past lines that sit entirely above the clip rectangle.
        int s = 0;
        if (y + lineHeight < clipRect.Y)
        {
            while (y + lineHeight < clipRect.Y && s < text.Length)
            {
                int lineEnd = IndexOfNewline(text, s);
                if (wrapEnabled)
                {
                    int wrap = CalcWordWrapPosition(size, text.Slice(s, lineEnd - s), wrapWidth);
                    s = WordWrapNextLineStart(text, s + wrap);
                }
                else
                {
                    s = lineEnd < text.Length ? lineEnd + 1 : text.Length;
                }
                y += lineHeight;
            }
        }

        if (s >= text.Length)
            return;

        uint colUntinted = col | ~0xFF000000u;
        int wrapEol = -1;

        while (s < text.Length)
        {
            if (wrapEnabled)
            {
                if (wrapEol < 0)
                    wrapEol = CalcWordWrapPosition(size, text.Slice(s), wrapWidth - (x - originX));

                if (s >= s + wrapEol)
                {
                    x = originX;
                    y += lineHeight;
                    if (y > clipRect.W)
                        break;
                    wrapEol = -1;
                    s = WordWrapNextLineStart(text, s);
                    continue;
                }
            }

            uint c = DecodeChar(text, ref s);

            if (c < 32)
            {
                if (c == '\n')
                {
                    x = originX;
                    y += lineHeight;
                    if (y > clipRect.W)
                        break;
                    continue;
                }
                if (c == '\r')
                    continue;
            }

            ImFontGlyph glyph = baked.FindGlyph(c);
            float charWidth = glyph.AdvanceX * scale;

            if (glyph.Visible)
            {
                float x1 = x + glyph.X0 * scale;
                float x2 = x + glyph.X1 * scale;
                float y1 = y + glyph.Y0 * scale;
                float y2 = y + glyph.Y1 * scale;
                if (x1 <= clipRect.Z && x2 >= clipRect.X)
                {
                    float u1 = glyph.U0;
                    float v1 = glyph.V0;
                    float u2 = glyph.U1;
                    float v2 = glyph.V1;

                    if (cpuFineClip)
                    {
                        if (x1 < clipRect.X) { u1 += (1.0f - (x2 - clipRect.X) / (x2 - x1)) * (u2 - u1); x1 = clipRect.X; }
                        if (y1 < clipRect.Y) { v1 += (1.0f - (y2 - clipRect.Y) / (y2 - y1)) * (v2 - v1); y1 = clipRect.Y; }
                        if (x2 > clipRect.Z) { u2 = u1 + ((clipRect.Z - x1) / (x2 - x1)) * (u2 - u1); x2 = clipRect.Z; }
                        if (y2 > clipRect.W) { v2 = v1 + ((clipRect.W - y1) / (y2 - y1)) * (v2 - v1); y2 = clipRect.W; }
                        if (y1 >= y2)
                        {
                            x += charWidth;
                            continue;
                        }
                    }

                    uint glyphCol = glyph.Colored ? colUntinted : col;
                    drawList.PrimReserve(6, 4);
                    drawList.PrimRectUV(new ImVec2(x1, y1), new ImVec2(x2, y2), new ImVec2(u1, v1), new ImVec2(u2, v2), glyphCol);
                }
            }

            x += charWidth;
        }
    }

    #region Text decode and classification helpers

    /// <summary>Character class for ordinary text.</summary>
    private const int WcharClassOther = 2;

    /// <summary>Character class for blanks (space and tab).</summary>
    private const int WcharClassBlank = 0;

    /// <summary>Character class for punctuation that may end a word span.</summary>
    private const int WcharClassPunct = 1;

    /// <summary>
    /// Decodes one UTF-16 code unit or surrogate pair, advancing the cursor. This is the managed
    /// equivalent of the canonical UTF-8 <c>ImTextCharFromUtf8</c> step; <see cref="ImTextCharFromUtf8"/>
    /// implements the byte-oriented canonical routine for callers holding UTF-8 buffers.
    /// </summary>
    /// <remarks>
    /// The method is assembly-visible so that <see cref="ImFontGlyphRangesBuilder"/> can reuse the
    /// same surrogate-combining step instead of duplicating it.
    /// </remarks>
    /// <param name="text">UTF-16 text.</param>
    /// <param name="i">Cursor, advanced past the decoded codepoint.</param>
    /// <returns>The decoded Unicode codepoint.</returns>
    internal static uint DecodeChar(ReadOnlySpan<char> text, ref int i)
    {
        char c = text[i];
        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            uint cp = (uint)char.ConvertToUtf32(c, text[i + 1]);
            i += 2;
            return cp;
        }
        i += 1;
        return c;
    }

    /// <summary>
    /// Classifies a codepoint for word wrapping. Reproduces the canonical classifier built from
    /// <c>" \t"</c> and <c>".,;!?\""</c> plus the CJK separator range
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns>One of the <c>WcharClass</c> constants.</returns>
    private static int ClassifyChar(uint c)
    {
        if (c == ' ' || c == '\t')
            return WcharClassBlank;
        if (c == '.' || c == ',' || c == ';' || c == '!' || c == '?' || c == '"')
            return WcharClassPunct;
        if (c == 0x3000)
            return WcharClassBlank;
        if (c == 0x3001 || c == 0x3002)
            return WcharClassPunct;
        return WcharClassOther;
    }

    /// <summary>
    /// Advances past trailing blanks and a newline to the start of the next wrapped line. Ports
    /// <c>ImTextCalcWordWrapNextLineStart</c>.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <param name="i">Current position.</param>
    /// <param name="flags">Wrap behavior flags.</param>
    /// <returns>The next line start position.</returns>
    private static int WordWrapNextLineStart(ReadOnlySpan<char> text, int i, ImDrawTextFlags flags = ImDrawTextFlags.None)
    {
        if ((flags & ImDrawTextFlags.WrapKeepBlanks) == 0)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                i++;
        }
        if (i < text.Length && text[i] == '\n')
            i++;
        return i;
    }

    /// <summary>Finds the next newline at or after <paramref name="start"/>, or the text length.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="start">Search start index.</param>
    /// <returns>Index of the newline, or the text length.</returns>
    private static int IndexOfNewline(ReadOnlySpan<char> text, int start)
    {
        // MemoryExtensions.IndexOf uses the vectorized runtime search; scanning by hand would serialize
        // a per-character branch on long clipped text.
        int index = text.Slice(start).IndexOf('\n');
        return index < 0 ? text.Length : start + index;
    }

    /// <summary>
    /// Returns the number of UTF-16 units needed to express one codepoint at the cursor. Ports
    /// <c>ImTextCountUtf8BytesFromChar</c>. in
    /// managed terms.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <returns>One for BMP text and two for a surrogate pair.</returns>
    private static int CountUtf8BytesFromChar(ReadOnlySpan<char> text)
        => text.Length >= 2 && char.IsHighSurrogate(text[0]) && char.IsLowSurrogate(text[1]) ? 2 : 1;

    /// <summary>
    /// Decodes one codepoint from UTF-8 bytes. Ports <c>ImTextCharFromUtf8</c>
    ///. for callers holding canonical UTF-8 buffers.
    /// </summary>
    /// <param name="input">UTF-8 byte buffer.</param>
    /// <param name="i">Cursor, advanced past the consumed bytes.</param>
    /// <param name="outChar">Receives the decoded codepoint, or U+FFFD when malformed.</param>
    /// <returns>The number of bytes consumed.</returns>
    public static int ImTextCharFromUtf8(ReadOnlySpan<byte> input, ref int i, out uint outChar)
    {
        int[] lengths = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 3, 3, 4, 0 };
        int[] masks = { 0x00, 0x7f, 0x1f, 0x0f, 0x07 };
        uint[] mins = { 0x400000, 0, 0x80, 0x800, 0x10000 };
        int[] shiftc = { 0, 18, 12, 6, 0 };
        int[] shifte = { 0, 6, 4, 2, 0 };

        int at = i;
        byte first = at < input.Length ? input[at] : (byte)0;
        int len = lengths[first >> 3];
        int wanted = len + (len != 0 ? 0 : 1);

        Span<byte> s = stackalloc byte[4];
        for (int k = 0; k < 4; k++)
            s[k] = at + k < input.Length ? input[at + k] : (byte)0;

        uint value = (uint)(s[0] & masks[len]) << 18;
        value |= (uint)(s[1] & 0x3f) << 12;
        value |= (uint)(s[2] & 0x3f) << 6;
        value |= (uint)(s[3] & 0x3f);
        value >>= shiftc[len];

        int e = 0;
        e = (value < mins[len]) ? 1 << 6 : 0;
        e |= ((value >> 11) == 0x1b) ? 1 << 7 : 0;
        e |= (value > 0x10FFFF) ? 1 << 8 : 0;
        e |= (s[1] & 0xc0) >> 2;
        e |= (s[2] & 0xc0) >> 4;
        e |= s[3] >> 6;
        e ^= 0x2a;
        e >>= shifte[len];

        if (e != 0)
        {
            int available = (s[0] != 0 ? 1 : 0) + (s[1] != 0 ? 1 : 0) + (s[2] != 0 ? 1 : 0) + (s[3] != 0 ? 1 : 0);
            wanted = Math.Min(wanted, available);
            value = 0xFFFD; // U+FFFD replacement character.
        }

        outChar = value;
        i = at + wanted;
        return wanted;
    }

    /// <summary>
    /// Measures UTF-8 text by transcoding it to a managed string and delegating to the canonical
    /// measurement path. Intended for byte-oriented callers and parity tests; the per-frame
    /// rendering path uses <see cref="RenderText"/> with UTF-16 spans and allocates nothing.
    /// </summary>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="maxWidth">Maximum line width.</param>
    /// <param name="wrapWidth">Word-wrap width, or zero to disable wrapping.</param>
    /// <param name="utf8Text">UTF-8 encoded text.</param>
    /// <returns>The measured width and height in pixels.</returns>
    public ImVec2 CalcTextSizeAUtf8(float size, float maxWidth, float wrapWidth, ReadOnlySpan<byte> utf8Text)
    {
        string decoded = Encoding.UTF8.GetString(utf8Text);
        return CalcTextSizeA(size, maxWidth, wrapWidth, decoded.AsSpan());
    }

    #endregion
}

#endregion

#region ImFontGlyphRangesBuilder

/// <summary>
/// Collects the Unicode code points an application wants baked, then coalesces them into the
/// zero-terminated inclusive range representation consumed by <see cref="ImFontConfig.GlyphRanges"/>.
/// Mirrors canonical <c>ImFontGlyphRangesBuilder</c>.,
/// with the builder methods defined.
/// </summary>
/// <remarks>
/// Canonical <c>AddText</c> takes a UTF-8 byte buffer and decodes it through
/// <c>ImTextCharFromUtf8</c>. Managed strings are UTF-16, so the port walks one Unicode scalar
/// value per step through the same surrogate-combining logic that <see cref="ImFont"/> uses for
/// text measurement: a high/low surrogate pair becomes one astral code point, while an unpaired
/// surrogate is recorded as its own code unit value. The bit array itself, including the word and
/// mask layout, is unchanged from canonical.
/// </remarks>
public sealed class ImFontGlyphRangesBuilder
{
    /// <summary>
    /// Largest Unicode scalar value the builder tracks. Mirrors canonical
    /// <c>IM_UNICODE_CODEPOINT_MAX</c> for the <c>IMGUI_USE_WCHAR32</c> build
    /// </summary>
    private const uint UnicodeCodepointMax = 0x10FFFF;

    /// <summary>
    /// One bit per Unicode code point, packed into 32-bit words: bit <c>n</c> lives in
    /// <c>UsedChars[n &gt;&gt; 5]</c> under mask <c>1u &lt;&lt; (n &amp; 31)</c>, where zero means
    /// unused. The array holds <c>(0x10FFFF + 1) / 32 = 34816</c> words, matching the canonical
    /// storage of <c>(IM_UNICODE_CODEPOINT_MAX + 1) / 8</c> bytes
    /// </summary>
    public uint[] UsedChars = new uint[(UnicodeCodepointMax + 1) / 32];

    /// <summary>Creates a builder in which every code point is marked unused.</summary>
    public ImFontGlyphRangesBuilder()
    {
        Clear();
    }

    /// <summary>Marks every code point unused.</summary>
    public void Clear() => Array.Clear(UsedChars);

    /// <summary>Tests whether a code point has been marked used.</summary>
    /// <param name="n">Unicode code point, at most <c>0x10FFFF</c>.</param>
    /// <returns><see langword="true"/> when the bit for <paramref name="n"/> is set.</returns>
    /// <exception cref="IndexOutOfRangeException">
    /// Thrown when <paramref name="n"/> exceeds <c>0x10FFFF</c>; canonical has undefined behavior for
    /// that input because the bit array cannot index it.
    /// </exception>
    public bool GetBit(uint n)
    {
        int offset = (int)(n >> 5);
        uint mask = 1u << (int)(n & 31);
        return (UsedChars[offset] & mask) != 0;
    }

    /// <summary>Marks a code point used.</summary>
    /// <param name="n">Unicode code point, at most <c>0x10FFFF</c>.</param>
    /// <exception cref="IndexOutOfRangeException">
    /// Thrown when <paramref name="n"/> exceeds <c>0x10FFFF</c>; canonical has undefined behavior for
    /// that input because the bit array cannot index it.
    /// </exception>
    public void SetBit(uint n)
    {
        int offset = (int)(n >> 5);
        uint mask = 1u << (int)(n & 31);
        UsedChars[offset] |= mask;
    }

    /// <summary>Marks one code point used; equivalent to <see cref="SetBit"/>.</summary>
    /// <param name="c">Unicode code point to add.</param>
    public void AddChar(uint c) => SetBit(c);

    /// <summary>Marks every Unicode scalar value found in UTF-16 text used.</summary>
    /// <param name="text">Managed text whose scalar values are added.</param>
    public void AddText(ReadOnlySpan<char> text)
    {
        int i = 0;
        while (i < text.Length)
            AddChar(ImFont.DecodeChar(text, ref i));
    }

    /// <summary>Marks every code point covered by a zero-terminated list of inclusive ranges used.</summary>
    /// <param name="ranges">
    /// Inclusive code point pairs terminated by a zero entry, matching the representation used by
    /// <see cref="ImFontAtlas.GetGlyphRangesDefault"/>. Every value is clamped to <c>0x10FFFF</c>.
    /// </param>
    public void AddRanges(ReadOnlySpan<uint> ranges)
    {
        for (int i = 0; i + 1 < ranges.Length && ranges[i] != 0; i += 2)
            for (uint c = ranges[i]; c <= ranges[i + 1] && c <= UnicodeCodepointMax; c++)
                AddChar(c);
    }

    /// <summary>Coalesces the recorded code points into a zero-terminated inclusive range list.</summary>
    /// <returns>
    /// Inclusive code point pairs, one pair per contiguous run, terminated by zero. Returning an array
    /// is the managed equivalent of canonical filling an output <c>ImVector&lt;ImWchar&gt;</c>.
    /// </returns>
    public uint[] BuildRanges()
    {
        List<uint> ranges = new();
        for (uint n = 0; n <= UnicodeCodepointMax; n++)
        {
            if (!GetBit(n))
                continue;
            ranges.Add(n);
            while (n < UnicodeCodepointMax && GetBit(n + 1))
                n++;
            ranges.Add(n);
        }
        ranges.Add(0);
        return ranges.ToArray();
    }
}

#endregion

#region ImFontAtlas

/// <summary>
/// Owns a set of fonts, bakes their glyphs into <see cref="ImFontBaked"/> entries, and packs the
/// rasterized bitmaps into a single CPU-side RGBA32 texture. Mirrors the canonical
/// <c>ImFontAtlas</c> subset used by this port.
/// </summary>
public sealed class ImFontAtlas
{
    #region Constants & configuration

    /// <summary>Padding in pixels between packed glyph rectangles.</summary>
    public int TexGlyphPadding { get; set; } = 1;

    /// <summary>Side length of the reserved solid-white pixel block.</summary>
    private const int WhiteRectSize = 2;

    /// <summary>
    /// Maximum integer line width, in pixels, for which the atlas bakes anti-aliased line UVs.
    /// Mirrors canonical <c>IM_DRAWLIST_TEX_LINES_WIDTH_MAX</c>.
    /// Widths above this limit are stroked with polygon geometry instead of the baked texture.
    /// </summary>
    public const int TexLinesWidthMax = 32;
    /// <summary>
    /// Width threshold up to which detailed sub-pixel line antialiasing tables are computed.
    /// </summary>
    public const int TexLinesDetailedWidth = 4;

    /// <summary>
    /// Number of sub-pixel sample steps per unit width in the line texture table.
    /// </summary>
    public const int TexLinesSampleCount = 4;

    /// <summary>
    /// Total count of detailed line width sample slots baked into the font atlas.
    /// </summary>
    public const int TexLinesDetailedWidthCount = (TexLinesDetailedWidth - 1) * TexLinesSampleCount + 1; // 13

    /// <summary>
    /// Width of the baked line texture region, in pixels. The two extra columns give each line row
    /// one transparent pixel of end-cap space on either side
    /// </summary>
    private const int LinesTextureWidth = TexLinesWidthMax + 2;

    /// <summary>
    /// Height of the baked line texture region, in pixels. The extra row holds the zero-width line
    /// plus detailed sampled lines.
    /// </summary>
    private const int LinesTextureHeight = TexLinesWidthMax + 1 + TexLinesDetailedWidthCount;

    /// <summary>Reference source size used by the embedded default font, in pixels.</summary>
    private const float DefaultFontSize = 13.0f;

    /// <summary>
    /// Expected default font size, in pixels, at or above which canonical selects the scalable
    /// ProggyForever face instead of the ProggyClean bitmap face
    /// </summary>
    private const float DefaultVectorFontThreshold = 15.0f;

    /// <summary>Minimum texture width in pixels; growth never goes below this value.</summary>
    public int TexMinWidth { get; set; } = 512;

    /// <summary>Minimum texture height in pixels; growth never goes below this value.</summary>
    public int TexMinHeight { get; set; } = 128;

    /// <summary>Maximum texture width in pixels; packing fails rather than exceeding it.</summary>
    public int TexMaxWidth { get; set; } = 8192;

    /// <summary>Maximum texture height in pixels; packing fails rather than exceeding it.</summary>
    public int TexMaxHeight { get; set; } = 8192;

    #endregion

    #region Properties & state

    /// <summary>Atlas build flags.</summary>
    public ImFontAtlasFlags Flags { get; set; } = ImFontAtlasFlags.None;

    /// <summary>Backend texture identifier recorded by the renderer backend.</summary>
    public IntPtr TexID { get; set; } = IntPtr.Zero;

    /// <summary>
    /// Pixel format requested for the atlas texture. Mirrors canonical
    /// <c>ImFontAtlas::TexDesiredFormat</c>., which the legacy
    /// <c>GetTexDataAsAlpha8</c> and <c>GetTexDataAsRGBA32</c> accessors set before building.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="ImTextureFormat.RGBA32"/>. Requesting
    /// <see cref="ImTextureFormat.Alpha8"/> halves the texture memory at the cost of monochrome
    /// output; the glyph writer already emits single-channel coverage in that mode. A format change
    /// takes effect on the next texture creation, and the legacy accessors force a rebuild when the
    /// current texture does not match the requested format.
    /// </remarks>
    public ImTextureFormat TexDesiredFormat { get; set; } = ImTextureFormat.RGBA32;

    /// <summary>
    /// Legacy entry point a renderer backend calls after creating the atlas texture, recording the
    /// backend identifier on the atlas and on its texture data. Mirrors canonical
    /// <c>ImFontAtlas::SetTexID</c>., which asserts that the
    /// atlas texture reference is texture-data-backed and then stores the identifier on
    /// <c>ImTextureData::TexID</c>.
    /// </summary>
    /// <param name="texId">Backend texture identifier for the atlas texture.</param>
    /// <remarks>
    /// The port has no <c>ImTextureRef</c> on the atlas, so the identifier is recorded on
    /// <see cref="TexID"/> as well as forwarded to <see cref="ImTextureData.SetTexID"/> when the
    /// texture data exists. Setting it before the first build is allowed, matching canonical's
    /// "may be called before texture creation" note.
    /// </remarks>
    public void SetTexID(IntPtr texId)
    {
        TexID = texId;
        TexData?.SetTexID(texId);
    }

    /// <summary>Current texture width in pixels.</summary>
    public int TexWidth { get; private set; }

    /// <summary>Current texture height in pixels.</summary>
    public int TexHeight { get; private set; }

    /// <summary>Reciprocal of the texture dimensions, used to compute UVs.</summary>
    public ImVec2 TexUvScale { get; private set; }

    /// <summary>UV coordinates at the center of the reserved white block.</summary>
    public ImVec2 TexUvWhitePixel { get; private set; }

    /// <summary>
    /// Baked UV rectangles for textured anti-aliased lines, indexed by integer line width. Entry
    /// <c>n</c> stores <c>(u0, v, u1, v)</c> for a line of width <c>n</c>; widths
    /// 0.<see cref="TexLinesWidthMax"/> are present. The table is empty when
    /// <see cref="Flags"/> includes <see cref="ImFontAtlasFlags.NoBakedLines"/>, which forces the
    /// draw list back to polygon stroking.
    /// </summary>
    /// <returns>The baked line UV rectangle table, or an empty table when lines are not baked.</returns>
    public ImVec4[] TexUvLines { get; private set; } = Array.Empty<ImVec4>();

    /// <summary>Fonts loaded into the atlas; index zero is the default font.</summary>
    /// <returns>The font list owned by this atlas.</returns>
    public readonly List<ImFont> Fonts = new();

    /// <summary>Font source descriptions in load order.</summary>
    /// <returns>The source list in load order.</returns>
    public readonly List<ImFontConfig> Sources = new();

    /// <summary>CPU-side texture data holding the packed glyph pixels.</summary>
    public ImTextureData? TexData { get; private set; }

    /// <summary>Whether the texture has been built for the current font set.</summary>
    public bool TexIsBuilt { get; private set; }

    /// <summary>
    /// Whether the atlas contents are currently locked against modification. Mirrors canonical
    /// <c>ImFontAtlas::Locked</c>, which canonical sets for
    /// the <c>NewFrame().EndFrame()</c> span whenever the renderer backend cannot consume texture
    /// updates and then rejects mutations such as <c>AddFont</c>.
    /// </summary>
    /// <remarks>
    /// This port has no frame or backend wiring yet, so the lock marks the synchronous windows in
    /// which the atlas genuinely rebuilds its own contents: the whole of <see cref="Build"/> and
    /// every texture creation or repack. Attempting to add a font during one of those windows throws
    /// <see cref="InvalidOperationException"/> and leaves the atlas unchanged. The setter is scoped
    /// to the assembly because the atlas owns this state; canonical exposes the raw field.
    /// </remarks>
    public bool Locked { get; internal set; }

    /// <summary>
    /// Next value to store in <see cref="ImTextureData.UniqueID"/>. Mirrors canonical
    /// <c>ImFontAtlas::TexNextUniqueID</c>, which starts at
    /// one and is consumed when a texture is created.
    /// </summary>
    public int TexNextUniqueID { get; private set; } = 1;

    /// <summary>
    /// Next value to store in <see cref="ImFont.FontId"/>. Mirrors canonical
    /// <c>ImFontAtlas::FontNextUniqueID</c>, which starts at
    /// one and is consumed when a font is created.
    /// </summary>
    public int FontNextUniqueID { get; private set; } = 1;

    /// <summary>
    /// Current atlas frame counter, advanced by <see cref="UpdateNewFrame"/>. A baked glyph set is
    /// idle when its <see cref="ImFontBaked.LastUsedFrame"/> plus an unused-frame threshold has fallen
    /// behind this value, at which point the atlas may return the set's packed rectangles to the
    /// packer. Mirrors canonical <c>ImFontAtlasBuilder::FrameCount</c>
    ///., which starts at -1 so an atlas that is never
    /// advanced retains every bake.
    /// </summary>
    /// <returns>The most recent frame counter value passed to <see cref="UpdateNewFrame"/>.</returns>
    public int FrameCount { get; private set; } = -1;

    /// <summary>Skyline packer assigning every atlas region a non-overlapping position.</summary>
    private readonly ImStbrpContext _packContext = new();

    /// <summary>Packed rectangles in packing order; entry N is referenced by a rect entry's target index.</summary>
    private readonly List<ImTextureRect> _rects = new();

    /// <summary>
    /// Persistent identifier table mapping <c>ImFontAtlasRectId</c> values to entries in
    /// <see cref="_rects"/>. Mirrors canonical <c>ImFontAtlasBuilder::RectsIndex</c>
    /// </summary>
    private readonly List<ImFontAtlasRectEntry> _rectEntries = new();

    /// <summary>Head of the free list within <see cref="_rectEntries"/>, or -1 when full.</summary>
    private int _rectsIndexFreeListStart = -1;

    /// <summary>Number of live packed rectangles.</summary>
    private int _rectsPackedCount;

    /// <summary>Total surface in pixels of live packed rectangles including padding.</summary>
    private int _rectsPackedSurface;

    /// <summary>Number of discarded rectangle entries awaiting reuse.</summary>
    private int _rectsDiscardedCount;

    /// <summary>Total surface in pixels released by discarded rectangles.</summary>
    private int _rectsDiscardedSurface;

    /// <summary>Largest rectangle width requested, used as a lower bound when growing the texture.</summary>
    private int _maxRectSizeW;

    /// <summary>Largest rectangle height requested, used as a lower bound when growing the texture.</summary>
    private int _maxRectSizeH;

    /// <summary>Right-most packed bound including padding, used to estimate a compact texture size.</summary>
    private int _maxRectBoundsW;

    /// <summary>Bottom-most packed bound including padding, used to estimate a compact texture size.</summary>
    private int _maxRectBoundsH;

    /// <summary>When true, packing must not resize the texture because a repack is in progress.</summary>
    private bool _lockDisableResize;

    /// <summary>Pack identifier of the reserved white texel block, or -1 before it is added.</summary>
    private int _packIdWhiteRect = -1;

    /// <summary>Pack identifier of the baked line texture region, or -1 before it is added.</summary>
    private int _packIdLinesTexData = -1;

    /// <summary>Whether the packer, white block and line region have been initialized.</summary>
    private bool _builderInitialized;

    private GCHandle _pinnedHandle;
    private bool _isPinned;

    /// <summary>
    /// Draw lists that read this atlas's white texel and line UV tables. Mirrors canonical
    /// <c>ImFontAtlas::DrawListSharedDatas</c>.: the
    /// atlas refreshes every registered list whenever it repacks or re-derives its regions, so a
    /// mid-frame lazy bake cannot leave any list with stale UVs.
    /// </summary>
    /// <remarks>
    /// Registration is permanent for the lifetime of the atlas and holds a strong reference, because
    /// this port has no draw-list teardown hook; lists are long-lived per UI context and are created
    /// in small numbers.
    /// </remarks>
    private readonly List<ImDrawList> _drawLists = new();

    /// <summary>
    /// Returns the canonical default glyph ranges: Basic Latin plus Latin Supplement. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesDefault</c>
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    /// <remarks>
    /// Canonical returns a pointer to static data; the managed port returns a fresh array so callers
    /// cannot corrupt the shared table. The ranges are queried when a source is added and when the
    /// legacy preload walks it, not in a per-frame path, so the allocation is not a concern.
    /// </remarks>
    public uint[] GetGlyphRangesDefault() => new uint[] { 0x0020, 0x00FF, 0 };

    /// <summary>
    /// Returns the canonical Greek ranges: default plus Greek and Coptic. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesGreek</c>.
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    public uint[] GetGlyphRangesGreek() => new uint[]
    {
        0x0020, 0x00FF, // Basic Latin + Latin Supplement
        0x0370, 0x03FF, // Greek and Coptic
        0,
    };

    /// <summary>
    /// Returns the canonical Korean ranges: default plus Korean alphabets and syllables. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesKorean</c>.
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    public uint[] GetGlyphRangesKorean() => new uint[]
    {
        0x0020, 0x00FF, // Basic Latin + Latin Supplement
        0x3131, 0x3163, // Korean alphabets
        0xAC00, 0xD7A3, // Korean characters
        0xFFFD, 0xFFFD, // Invalid
        0,
    };

    /// <summary>
    /// Returns the canonical full Chinese ranges: default plus punctuation, kana, half-width forms
    /// and the full CJK Unified Ideographs block. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesChineseFull</c>.
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    public uint[] GetGlyphRangesChineseFull() => new uint[]
    {
        0x0020, 0x00FF, // Basic Latin + Latin Supplement
        0x2000, 0x206F, // General Punctuation
        0x3000, 0x30FF, // CJK Symbols and Punctuations, Hiragana, Katakana
        0x31F0, 0x31FF, // Katakana Phonetic Extensions
        0xFF00, 0xFFEF, // Half-width characters
        0xFFFD, 0xFFFD, // Invalid
        0x4e00, 0x9FAF, // CJK Ideograms
        0,
    };

    /// <summary>
    /// Returns the canonical commonly used Simplified Chinese ranges: default plus punctuation, kana,
    /// half-width forms and the 2500 most frequently used CJK Unified Ideographs. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesChineseSimplifiedCommon</c>
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    /// <remarks>
    /// Canonical encodes the ideographs as cumulative offsets from U+4E00 so the source stays
    /// compact, then expands them with <c>UnpackAccumulativeOffsetsIntoRanges</c> on first use and
    /// caches the result in a static buffer. The managed port rebuilds the array per call so the
    /// shared offset table stays immutable; the getter runs at font-add and preload time rather
    /// than per frame.
    /// </remarks>
    public uint[] GetGlyphRangesChineseSimplifiedCommon()
    {
        uint[] ranges = new uint[
            ChineseSimplifiedCommonBaseRanges.Length + ChineseSimplifiedCommonOffsets.Length * 2 + 1];
        Array.Copy(ChineseSimplifiedCommonBaseRanges, ranges, ChineseSimplifiedCommonBaseRanges.Length);
        UnpackAccumulativeOffsetsIntoRanges(
            0x4E00, ChineseSimplifiedCommonOffsets, ChineseSimplifiedCommonBaseRanges.Length, ranges);
        return ranges;
    }

    /// <summary>
    /// Base ranges prepended to the expanded Simplified Chinese offsets. Verbatim canonical
    /// <c>base_ranges[]</c>.; the list is not
    /// zero-terminated because the expansion helper appends the terminator.
    /// </summary>
    private static readonly uint[] ChineseSimplifiedCommonBaseRanges = new uint[]
    {
        0x0020, 0x00FF,
        0x2000, 0x206F,
        0x3000, 0x30FF,
        0x31F0, 0x31FF,
        0xFF00, 0xFFEF,
        0xFFFD, 0xFFFD,
    };

    /// <summary>
    /// Cumulative codepoint offsets from U+4E00 for the 2500 commonly used Simplified Chinese
    /// characters. Verbatim canonical <c>accumulative_offsets_from_0x4E00[]</c>
    ///.; entry <c>n</c> both closes the previous
    /// single-codepoint range and advances the running base.
    /// </summary>
    private static readonly short[] ChineseSimplifiedCommonOffsets = new short[]
    {
        0, 1, 2, 4, 1, 1, 1, 1, 2, 1, 3, 2, 1, 2, 2, 1, 1, 1, 1, 1,
        5, 2, 1, 2, 3, 3, 3, 2, 2, 4, 1, 1, 1, 2, 1, 5, 2, 3, 1, 2,
        1, 2, 1, 1, 2, 1, 1, 2, 2, 1, 4, 1, 1, 1, 1, 5, 10, 1, 2, 19,
        2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 5, 1, 6, 3, 2, 1, 2, 2, 1, 1,
        1, 4, 8, 5, 1, 1, 4, 1, 1, 3, 1, 2, 1, 5, 1, 2, 1, 1, 1, 10,
        1, 1, 5, 2, 4, 6, 1, 4, 2, 2, 2, 12, 2, 1, 1, 6, 1, 1, 1, 4,
        1, 1, 4, 6, 5, 1, 4, 2, 2, 4, 10, 7, 1, 1, 4, 2, 4, 2, 1, 4,
        3, 6, 10, 12, 5, 7, 2, 14, 2, 9, 1, 1, 6, 7, 10, 4, 7, 13, 1, 5,
        4, 8, 4, 1, 1, 2, 28, 5, 6, 1, 1, 5, 2, 5, 20, 2, 2, 9, 8, 11,
        2, 9, 17, 1, 8, 6, 8, 27, 4, 6, 9, 20, 11, 27, 6, 68, 2, 2, 1, 1,
        1, 2, 1, 2, 2, 7, 6, 11, 3, 3, 1, 1, 3, 1, 2, 1, 1, 1, 1, 1,
        3, 1, 1, 8, 3, 4, 1, 5, 7, 2, 1, 4, 4, 8, 4, 2, 1, 2, 1, 1,
        4, 5, 6, 3, 6, 2, 12, 3, 1, 3, 9, 2, 4, 3, 4, 1, 5, 3, 3, 1,
        3, 7, 1, 5, 1, 1, 1, 1, 2, 3, 4, 5, 2, 3, 2, 6, 1, 1, 2, 1,
        7, 1, 7, 3, 4, 5, 15, 2, 2, 1, 5, 3, 22, 19, 2, 1, 1, 1, 1, 2,
        5, 1, 1, 1, 6, 1, 1, 12, 8, 2, 9, 18, 22, 4, 1, 1, 5, 1, 16, 1,
        2, 7, 10, 15, 1, 1, 6, 2, 4, 1, 2, 4, 1, 6, 1, 1, 3, 2, 4, 1,
        6, 4, 5, 1, 2, 1, 1, 2, 1, 10, 3, 1, 3, 2, 1, 9, 3, 2, 5, 7,
        2, 19, 4, 3, 6, 1, 1, 1, 1, 1, 4, 3, 2, 1, 1, 1, 2, 5, 3, 1,
        1, 1, 2, 2, 1, 1, 2, 1, 1, 2, 1, 3, 1, 1, 1, 3, 7, 1, 4, 1,
        1, 2, 1, 1, 2, 1, 2, 4, 4, 3, 8, 1, 1, 1, 2, 1, 3, 5, 1, 3,
        1, 3, 4, 6, 2, 2, 14, 4, 6, 6, 11, 9, 1, 15, 3, 1, 28, 5, 2, 5,
        5, 3, 1, 3, 4, 5, 4, 6, 14, 3, 2, 3, 5, 21, 2, 7, 20, 10, 1, 2,
        19, 2, 4, 28, 28, 2, 3, 2, 1, 14, 4, 1, 26, 28, 42, 12, 40, 3, 52, 79,
        5, 14, 17, 3, 2, 2, 11, 3, 4, 6, 3, 1, 8, 2, 23, 4, 5, 8, 10, 4,
        2, 7, 3, 5, 1, 1, 6, 3, 1, 2, 2, 2, 5, 28, 1, 1, 7, 7, 20, 5,
        3, 29, 3, 17, 26, 1, 8, 4, 27, 3, 6, 11, 23, 5, 3, 4, 6, 13, 24, 16,
        6, 5, 10, 25, 35, 7, 3, 2, 3, 3, 14, 3, 6, 2, 6, 1, 4, 2, 3, 8,
        2, 1, 1, 3, 3, 3, 4, 1, 1, 13, 2, 2, 4, 5, 2, 1, 14, 14, 1, 2,
        2, 1, 4, 5, 2, 3, 1, 14, 3, 12, 3, 17, 2, 16, 5, 1, 2, 1, 8, 9,
        3, 19, 4, 2, 2, 4, 17, 25, 21, 20, 28, 75, 1, 10, 29, 103, 4, 1, 2, 1,
        1, 4, 2, 4, 1, 2, 3, 24, 2, 2, 2, 1, 1, 2, 1, 3, 8, 1, 1, 1,
        2, 1, 1, 3, 1, 1, 1, 6, 1, 5, 3, 1, 1, 1, 3, 4, 1, 1, 5, 2,
        1, 5, 6, 13, 9, 16, 1, 1, 1, 1, 3, 2, 3, 2, 4, 5, 2, 5, 2, 2,
        3, 7, 13, 7, 2, 2, 1, 1, 1, 1, 2, 3, 3, 2, 1, 6, 4, 9, 2, 1,
        14, 2, 14, 2, 1, 18, 3, 4, 14, 4, 11, 41, 15, 23, 15, 23, 176, 1, 3, 4,
        1, 1, 1, 1, 5, 3, 1, 2, 3, 7, 3, 1, 1, 2, 1, 2, 4, 4, 6, 2,
        4, 1, 9, 7, 1, 10, 5, 8, 16, 29, 1, 1, 2, 2, 3, 1, 3, 5, 2, 4,
        5, 4, 1, 1, 2, 2, 3, 3, 7, 1, 6, 10, 1, 17, 1, 44, 4, 6, 2, 1,
        1, 6, 5, 4, 2, 10, 1, 6, 9, 2, 8, 1, 24, 1, 2, 13, 7, 8, 8, 2,
        1, 4, 1, 3, 1, 3, 3, 5, 2, 5, 10, 9, 4, 9, 12, 2, 1, 6, 1, 10,
        1, 1, 7, 7, 4, 10, 8, 3, 1, 13, 4, 3, 1, 6, 1, 3, 5, 2, 1, 2,
        17, 16, 5, 2, 16, 6, 1, 4, 2, 1, 3, 3, 6, 8, 5, 11, 11, 1, 3, 3,
        2, 4, 6, 10, 9, 5, 7, 4, 7, 4, 7, 1, 1, 4, 2, 1, 3, 6, 8, 7,
        1, 6, 11, 5, 5, 3, 24, 9, 4, 2, 7, 13, 5, 1, 8, 82, 16, 61, 1, 1,
        1, 4, 2, 2, 16, 10, 3, 8, 1, 1, 6, 4, 2, 1, 3, 1, 1, 1, 4, 3,
        8, 4, 2, 2, 1, 1, 1, 1, 1, 6, 3, 5, 1, 1, 4, 6, 9, 2, 1, 1,
        1, 2, 1, 7, 2, 1, 6, 1, 5, 4, 4, 3, 1, 8, 1, 3, 3, 1, 3, 2,
        2, 2, 2, 3, 1, 6, 1, 2, 1, 2, 1, 3, 7, 1, 8, 2, 1, 2, 1, 5,
        2, 5, 3, 5, 10, 1, 2, 1, 1, 3, 2, 5, 11, 3, 9, 3, 5, 1, 1, 5,
        9, 1, 2, 1, 5, 7, 9, 9, 8, 1, 3, 3, 3, 6, 8, 2, 3, 2, 1, 1,
        32, 6, 1, 2, 15, 9, 3, 7, 13, 1, 3, 10, 13, 2, 14, 1, 13, 10, 2, 1,
        3, 10, 4, 15, 2, 15, 15, 10, 1, 3, 9, 6, 9, 32, 25, 26, 47, 7, 3, 2,
        3, 1, 6, 3, 4, 3, 2, 8, 5, 4, 1, 9, 4, 2, 2, 19, 10, 6, 2, 3,
        8, 1, 2, 2, 4, 2, 1, 9, 4, 4, 4, 6, 4, 8, 9, 2, 3, 1, 1, 1,
        1, 3, 5, 5, 1, 3, 8, 4, 6, 2, 1, 4, 12, 1, 5, 3, 7, 13, 2, 5,
        8, 1, 6, 1, 2, 5, 14, 6, 1, 5, 2, 4, 8, 15, 5, 1, 23, 6, 62, 2,
        10, 1, 1, 8, 1, 2, 2, 10, 4, 2, 2, 9, 2, 1, 1, 3, 2, 3, 1, 5,
        3, 3, 2, 1, 3, 8, 1, 1, 1, 11, 3, 1, 1, 4, 3, 7, 1, 14, 1, 2,
        3, 12, 5, 2, 5, 1, 6, 7, 5, 7, 14, 11, 1, 3, 1, 8, 9, 12, 2, 1,
        11, 8, 4, 4, 2, 6, 10, 9, 13, 1, 1, 3, 1, 5, 1, 3, 2, 4, 4, 1,
        18, 2, 3, 14, 11, 4, 29, 4, 2, 7, 1, 3, 13, 9, 2, 2, 5, 3, 5, 20,
        7, 16, 8, 5, 72, 34, 6, 4, 22, 12, 12, 28, 45, 36, 9, 7, 39, 9, 191, 1,
        1, 1, 4, 11, 8, 4, 9, 2, 3, 22, 1, 1, 1, 1, 4, 17, 1, 7, 7, 1,
        11, 31, 10, 2, 4, 8, 2, 3, 2, 1, 4, 2, 16, 4, 32, 2, 3, 19, 13, 4,
        9, 1, 5, 2, 14, 8, 1, 1, 3, 6, 19, 6, 5, 1, 16, 6, 2, 10, 8, 5,
        1, 2, 3, 1, 5, 5, 1, 11, 6, 6, 1, 3, 3, 2, 6, 3, 8, 1, 1, 4,
        10, 7, 5, 7, 7, 5, 8, 9, 2, 1, 3, 4, 1, 1, 3, 1, 3, 3, 2, 6,
        16, 1, 4, 6, 3, 1, 10, 6, 1, 3, 15, 2, 9, 2, 10, 25, 13, 9, 16, 6,
        2, 2, 10, 11, 4, 3, 9, 1, 2, 6, 6, 5, 4, 30, 40, 1, 10, 7, 12, 14,
        33, 6, 3, 6, 7, 3, 1, 3, 1, 11, 14, 4, 9, 5, 12, 11, 49, 18, 51, 31,
        140, 31, 2, 2, 1, 5, 1, 8, 1, 10, 1, 4, 4, 3, 24, 1, 10, 1, 3, 6,
        6, 16, 3, 4, 5, 2, 1, 4, 2, 57, 10, 6, 22, 2, 22, 3, 7, 22, 6, 10,
        11, 36, 18, 16, 33, 36, 2, 5, 5, 1, 1, 1, 4, 10, 1, 4, 13, 2, 7, 5,
        2, 9, 3, 4, 1, 7, 43, 3, 7, 3, 9, 14, 7, 9, 1, 11, 1, 1, 3, 7,
        4, 18, 13, 1, 14, 1, 3, 6, 10, 73, 2, 2, 30, 6, 1, 11, 18, 19, 13, 22,
        3, 46, 42, 37, 89, 7, 3, 16, 34, 2, 2, 3, 9, 1, 7, 1, 1, 1, 2, 2,
        4, 10, 7, 3, 10, 3, 9, 5, 28, 9, 2, 6, 13, 7, 3, 1, 3, 10, 2, 7,
        2, 11, 3, 6, 21, 54, 85, 2, 1, 4, 2, 2, 1, 39, 3, 21, 2, 2, 5, 1,
        1, 1, 4, 1, 1, 3, 4, 15, 1, 3, 2, 4, 4, 2, 3, 8, 2, 20, 1, 8,
        7, 13, 4, 1, 26, 6, 2, 9, 34, 4, 21, 52, 10, 4, 4, 1, 5, 12, 2, 11,
        1, 7, 2, 30, 12, 44, 2, 30, 1, 1, 3, 6, 16, 9, 17, 39, 82, 2, 2, 24,
        7, 1, 7, 3, 16, 9, 14, 44, 2, 1, 2, 1, 2, 3, 5, 2, 4, 1, 6, 7,
        5, 3, 2, 6, 1, 11, 5, 11, 2, 1, 18, 19, 8, 1, 3, 24, 29, 2, 1, 3,
        5, 2, 2, 1, 13, 6, 5, 1, 46, 11, 3, 5, 1, 1, 5, 8, 2, 10, 6, 12,
        6, 3, 7, 11, 2, 4, 16, 13, 2, 5, 1, 1, 2, 2, 5, 2, 28, 5, 2, 23,
        10, 8, 4, 4, 22, 39, 95, 38, 8, 14, 9, 5, 1, 13, 5, 4, 3, 13, 12, 11,
        1, 9, 1, 27, 37, 2, 5, 4, 4, 63, 211, 95, 2, 2, 2, 1, 3, 5, 2, 1,
        1, 2, 2, 1, 1, 1, 3, 2, 4, 1, 2, 1, 1, 5, 2, 2, 1, 1, 2, 3,
        1, 3, 1, 1, 1, 3, 1, 4, 2, 1, 3, 6, 1, 1, 3, 7, 15, 5, 3, 2,
        5, 3, 9, 11, 4, 2, 22, 1, 6, 3, 8, 7, 1, 4, 28, 4, 16, 3, 3, 25,
        4, 4, 27, 27, 1, 4, 1, 2, 2, 7, 1, 3, 5, 2, 28, 8, 2, 14, 1, 8,
        6, 16, 25, 3, 3, 3, 14, 3, 3, 1, 1, 2, 1, 4, 6, 3, 8, 4, 1, 1,
        1, 2, 3, 6, 10, 6, 2, 3, 18, 3, 2, 5, 5, 4, 3, 1, 5, 2, 5, 4,
        23, 7, 6, 12, 6, 4, 17, 11, 9, 5, 1, 1, 10, 5, 12, 1, 1, 11, 26, 33,
        7, 3, 6, 1, 17, 7, 1, 5, 12, 1, 11, 2, 4, 1, 8, 14, 17, 23, 1, 2,
        1, 7, 8, 16, 11, 9, 6, 5, 2, 6, 4, 16, 2, 8, 14, 1, 11, 8, 9, 1,
        1, 1, 9, 25, 4, 11, 19, 7, 2, 15, 2, 12, 8, 52, 7, 5, 19, 2, 16, 4,
        36, 8, 1, 16, 8, 24, 26, 4, 6, 2, 9, 5, 4, 36, 3, 28, 12, 25, 15, 37,
        27, 17, 12, 59, 38, 5, 32, 127, 1, 2, 9, 17, 14, 4, 1, 2, 1, 1, 8, 11,
        50, 4, 14, 2, 19, 16, 4, 17, 5, 4, 5, 26, 12, 45, 2, 23, 45, 104, 30, 12,
        8, 3, 10, 2, 2, 3, 3, 1, 4, 20, 7, 2, 9, 6, 15, 2, 20, 1, 3, 16,
        4, 11, 15, 6, 134, 2, 5, 59, 1, 2, 2, 2, 1, 9, 17, 3, 26, 137, 10, 211,
        59, 1, 2, 4, 1, 4, 1, 1, 1, 2, 6, 2, 3, 1, 1, 2, 3, 2, 3, 1,
        3, 4, 4, 2, 3, 3, 1, 4, 3, 1, 7, 2, 2, 3, 1, 2, 1, 3, 3, 3,
        2, 2, 3, 2, 1, 3, 14, 6, 1, 3, 2, 9, 6, 15, 27, 9, 34, 145, 1, 1,
        2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 2, 2, 3, 1, 2, 1, 1, 1, 2,
        3, 5, 8, 3, 5, 2, 4, 1, 3, 2, 2, 2, 12, 4, 1, 1, 1, 10, 4, 5,
        1, 20, 4, 16, 1, 15, 9, 5, 12, 2, 9, 2, 5, 4, 2, 26, 19, 7, 1, 26,
        4, 30, 12, 15, 42, 1, 6, 8, 172, 1, 1, 4, 2, 1, 1, 11, 2, 2, 4, 2,
        1, 2, 1, 10, 8, 1, 2, 1, 4, 5, 1, 2, 5, 1, 8, 4, 1, 3, 4, 2,
        1, 6, 2, 1, 3, 4, 1, 2, 1, 1, 1, 1, 12, 5, 7, 2, 4, 3, 1, 1,
        1, 3, 3, 6, 1, 2, 2, 3, 3, 3, 2, 1, 2, 12, 14, 11, 6, 6, 4, 12,
        2, 8, 1, 7, 10, 1, 35, 7, 4, 13, 15, 4, 3, 23, 21, 28, 52, 5, 26, 5,
        6, 1, 7, 10, 2, 7, 53, 3, 2, 1, 1, 1, 2, 163, 532, 1, 10, 11, 1, 3,
        3, 4, 8, 2, 8, 6, 2, 2, 23, 22, 4, 2, 2, 4, 2, 1, 3, 1, 3, 3,
        5, 9, 8, 2, 1, 2, 8, 1, 10, 2, 12, 21, 20, 15, 105, 2, 3, 1, 1, 3,
        2, 3, 1, 1, 2, 5, 1, 4, 15, 11, 19, 1, 1, 1, 1, 5, 4, 5, 1, 1,
        2, 5, 3, 5, 12, 1, 2, 5, 1, 11, 1, 1, 15, 9, 1, 4, 5, 3, 26, 8,
        2, 1, 3, 1, 1, 15, 19, 2, 12, 1, 2, 5, 2, 7, 2, 19, 2, 20, 6, 26,
        7, 5, 2, 2, 7, 34, 21, 13, 70, 2, 128, 1, 1, 2, 1, 1, 2, 1, 1, 3,
        2, 2, 2, 15, 1, 4, 1, 3, 4, 42, 10, 6, 1, 49, 85, 8, 1, 2, 1, 1,
        4, 4, 2, 3, 6, 1, 5, 7, 4, 3, 211, 4, 1, 2, 1, 2, 5, 1, 2, 4,
        2, 2, 6, 5, 6, 10, 3, 4, 48, 100, 6, 2, 16, 296, 5, 27, 387, 2, 2, 3,
        7, 16, 8, 5, 38, 15, 39, 21, 9, 10, 3, 7, 59, 13, 27, 21, 47, 5, 21, 6,
    };

    /// <summary>
    /// Returns the canonical Japanese ranges: default plus punctuation, kana, half-width forms and a
    /// selection of 2999 CJK Unified Ideographs. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesJapanese</c>.
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    /// <remarks>
    /// Uses the same cumulative-offset encoding and expansion as
    /// <see cref="GetGlyphRangesChineseSimplifiedCommon"/>, rebuilt per call.
    /// </remarks>
    public uint[] GetGlyphRangesJapanese()
    {
        uint[] ranges = new uint[JapaneseBaseRanges.Length + JapaneseOffsets.Length * 2 + 1];
        Array.Copy(JapaneseBaseRanges, ranges, JapaneseBaseRanges.Length);
        UnpackAccumulativeOffsetsIntoRanges(0x4E00, JapaneseOffsets, JapaneseBaseRanges.Length, ranges);
        return ranges;
    }

    /// <summary>
    /// Base ranges prepended to the expanded Japanese offsets. Verbatim canonical <c>base_ranges[]</c>
    ///.; the list is not zero-terminated because the
    /// expansion helper appends the terminator.
    /// </summary>
    private static readonly uint[] JapaneseBaseRanges = new uint[]
    {
        0x0020, 0x00FF,
        0x3000, 0x30FF,
        0x31F0, 0x31FF,
        0xFF00, 0xFFEF,
        0xFFFD, 0xFFFD,
    };

    /// <summary>
    /// Cumulative codepoint offsets from U+4E00 for the 2999 selected Japanese ideographs. Verbatim
    /// canonical <c>accumulative_offsets_from_0x4E00[]</c>
    ///.; entry <c>n</c> both closes the previous
    /// single-codepoint range and advances the running base.
    /// </summary>
    private static readonly short[] JapaneseOffsets = new short[]
    {
        0, 1, 2, 4, 1, 1, 1, 1, 2, 1, 3, 3, 2, 2, 1, 5, 3, 5, 7, 5,
        6, 1, 2, 1, 7, 2, 6, 3, 1, 8, 1, 1, 4, 1, 1, 18, 2, 11, 2, 6,
        2, 1, 2, 1, 5, 1, 2, 1, 3, 1, 2, 1, 2, 3, 3, 1, 1, 2, 3, 1,
        1, 1, 12, 7, 9, 1, 4, 5, 1, 1, 2, 1, 10, 1, 1, 9, 2, 2, 4, 5,
        6, 9, 3, 1, 1, 1, 1, 9, 3, 18, 5, 2, 2, 2, 2, 1, 6, 3, 7, 1,
        1, 1, 1, 2, 2, 4, 2, 1, 23, 2, 10, 4, 3, 5, 2, 4, 10, 2, 4, 13,
        1, 6, 1, 9, 3, 1, 1, 6, 6, 7, 6, 3, 1, 2, 11, 3, 2, 2, 3, 2,
        15, 2, 2, 5, 4, 3, 6, 4, 1, 2, 5, 2, 12, 16, 6, 13, 9, 13, 2, 1,
        1, 7, 16, 4, 7, 1, 19, 1, 5, 1, 2, 2, 7, 7, 8, 2, 6, 5, 4, 9,
        18, 7, 4, 5, 9, 13, 11, 8, 15, 2, 1, 1, 1, 2, 1, 2, 2, 1, 2, 2,
        8, 2, 9, 3, 3, 1, 1, 4, 4, 1, 1, 1, 4, 9, 1, 4, 3, 5, 5, 2,
        7, 5, 3, 4, 8, 2, 1, 13, 2, 3, 3, 1, 14, 1, 1, 4, 5, 1, 3, 6,
        1, 5, 2, 1, 1, 3, 3, 3, 3, 1, 1, 2, 7, 6, 6, 7, 1, 4, 7, 6,
        1, 1, 1, 1, 1, 12, 3, 3, 9, 5, 2, 6, 1, 5, 6, 1, 2, 3, 18, 2,
        4, 14, 4, 1, 3, 6, 1, 1, 6, 3, 5, 5, 3, 2, 2, 2, 2, 12, 3, 1,
        4, 2, 3, 2, 3, 11, 1, 7, 4, 1, 2, 1, 3, 17, 1, 9, 1, 24, 1, 1,
        4, 2, 2, 4, 1, 2, 7, 1, 1, 1, 3, 1, 2, 2, 4, 15, 1, 1, 2, 1,
        1, 2, 1, 5, 2, 5, 20, 2, 5, 9, 1, 10, 8, 7, 6, 1, 1, 1, 1, 1,
        1, 6, 2, 1, 2, 8, 1, 1, 1, 1, 5, 1, 1, 3, 1, 1, 1, 1, 3, 1,
        1, 12, 4, 1, 3, 1, 1, 1, 1, 1, 10, 3, 1, 7, 5, 13, 1, 2, 3, 4,
        6, 1, 1, 30, 2, 9, 9, 1, 15, 38, 11, 3, 1, 8, 24, 7, 1, 9, 8, 10,
        2, 1, 9, 31, 2, 13, 6, 2, 9, 4, 49, 5, 2, 15, 2, 1, 10, 2, 1, 1,
        1, 2, 2, 6, 15, 30, 35, 3, 14, 18, 8, 1, 16, 10, 28, 12, 19, 45, 38, 1,
        3, 2, 3, 13, 2, 1, 7, 3, 6, 5, 3, 4, 3, 1, 5, 7, 8, 1, 5, 3,
        18, 5, 3, 6, 1, 21, 4, 24, 9, 24, 40, 3, 14, 3, 21, 3, 2, 1, 2, 4,
        2, 3, 1, 15, 15, 6, 5, 1, 1, 3, 1, 5, 6, 1, 9, 7, 3, 3, 2, 1,
        4, 3, 8, 21, 5, 16, 4, 5, 2, 10, 11, 11, 3, 6, 3, 2, 9, 3, 6, 13,
        1, 2, 1, 1, 1, 1, 11, 12, 6, 6, 1, 4, 2, 6, 5, 2, 1, 1, 3, 3,
        6, 13, 3, 1, 1, 5, 1, 2, 3, 3, 14, 2, 1, 2, 2, 2, 5, 1, 9, 5,
        1, 1, 6, 12, 3, 12, 3, 4, 13, 2, 14, 2, 8, 1, 17, 5, 1, 16, 4, 2,
        2, 21, 8, 9, 6, 23, 20, 12, 25, 19, 9, 38, 8, 3, 21, 40, 25, 33, 13, 4,
        3, 1, 4, 1, 2, 4, 1, 2, 5, 26, 2, 1, 1, 2, 1, 3, 6, 2, 1, 1,
        1, 1, 1, 1, 2, 3, 1, 1, 1, 9, 2, 3, 1, 1, 1, 3, 6, 3, 2, 1,
        1, 6, 6, 1, 8, 2, 2, 2, 1, 4, 1, 2, 3, 2, 7, 3, 2, 4, 1, 2,
        1, 2, 2, 1, 1, 1, 1, 1, 3, 1, 2, 5, 4, 10, 9, 4, 9, 1, 1, 1,
        1, 1, 1, 5, 3, 2, 1, 6, 4, 9, 6, 1, 10, 2, 31, 17, 8, 3, 7, 5,
        40, 1, 7, 7, 1, 6, 5, 2, 10, 7, 8, 4, 15, 39, 25, 6, 28, 47, 18, 10,
        7, 1, 3, 1, 1, 2, 1, 1, 1, 3, 3, 3, 1, 1, 1, 3, 4, 2, 1, 4,
        1, 3, 6, 10, 7, 8, 6, 2, 2, 1, 3, 3, 2, 5, 8, 7, 9, 12, 2, 15,
        1, 1, 4, 1, 2, 1, 1, 1, 3, 2, 1, 3, 3, 5, 6, 2, 3, 2, 10, 1,
        4, 2, 8, 1, 1, 1, 11, 6, 1, 21, 4, 16, 3, 1, 3, 1, 4, 2, 3, 6,
        5, 1, 3, 1, 1, 3, 3, 4, 6, 1, 1, 10, 4, 2, 7, 10, 4, 7, 4, 2,
        9, 4, 3, 1, 1, 1, 4, 1, 8, 3, 4, 1, 3, 1, 6, 1, 4, 2, 1, 4,
        7, 2, 1, 8, 1, 4, 5, 1, 1, 2, 2, 4, 6, 2, 7, 1, 10, 1, 1, 3,
        4, 11, 10, 8, 21, 4, 6, 1, 3, 5, 2, 1, 2, 28, 5, 5, 2, 3, 13, 1,
        2, 3, 1, 4, 2, 1, 5, 20, 3, 8, 11, 1, 3, 3, 3, 1, 8, 10, 9, 2,
        10, 9, 2, 3, 1, 1, 2, 4, 1, 8, 3, 6, 1, 7, 8, 6, 11, 1, 4, 29,
        8, 4, 3, 1, 2, 7, 13, 1, 4, 1, 6, 2, 6, 12, 12, 2, 20, 3, 2, 3,
        6, 4, 8, 9, 2, 7, 34, 5, 1, 18, 6, 1, 1, 4, 4, 5, 7, 9, 1, 2,
        2, 4, 3, 4, 1, 7, 2, 2, 2, 6, 2, 3, 25, 5, 3, 6, 1, 4, 6, 7,
        4, 2, 1, 4, 2, 13, 6, 4, 4, 3, 1, 5, 3, 4, 4, 3, 2, 1, 1, 4,
        1, 2, 1, 1, 3, 1, 11, 1, 6, 3, 1, 7, 3, 6, 2, 8, 8, 6, 9, 3,
        4, 11, 3, 2, 10, 12, 2, 5, 11, 1, 6, 4, 5, 3, 1, 8, 5, 4, 6, 6,
        3, 5, 1, 1, 3, 2, 1, 2, 2, 6, 17, 12, 1, 10, 1, 6, 12, 1, 6, 6,
        19, 9, 6, 16, 1, 13, 4, 4, 15, 7, 17, 6, 11, 9, 15, 12, 6, 7, 2, 1,
        2, 2, 15, 9, 3, 21, 4, 6, 49, 18, 7, 3, 2, 3, 1, 6, 8, 2, 2, 6,
        2, 9, 1, 3, 6, 4, 4, 1, 2, 16, 2, 5, 2, 1, 6, 2, 3, 5, 3, 1,
        2, 5, 1, 2, 1, 9, 3, 1, 8, 6, 4, 8, 11, 3, 1, 1, 1, 1, 3, 1,
        13, 8, 4, 1, 3, 2, 2, 1, 4, 1, 11, 1, 5, 2, 1, 5, 2, 5, 8, 6,
        1, 1, 7, 4, 3, 8, 3, 2, 7, 2, 1, 5, 1, 5, 2, 4, 7, 6, 2, 8,
        5, 1, 11, 4, 5, 3, 6, 18, 1, 2, 13, 3, 3, 1, 21, 1, 1, 4, 1, 4,
        1, 1, 1, 8, 1, 2, 2, 7, 1, 2, 4, 2, 2, 9, 2, 1, 1, 1, 4, 3,
        6, 3, 12, 5, 1, 1, 1, 5, 6, 3, 2, 4, 8, 2, 2, 4, 2, 7, 1, 8,
        9, 5, 2, 3, 2, 1, 3, 2, 13, 7, 14, 6, 5, 1, 1, 2, 1, 4, 2, 23,
        2, 1, 1, 6, 3, 1, 4, 1, 15, 3, 1, 7, 3, 9, 14, 1, 3, 1, 4, 1,
        1, 5, 8, 1, 3, 8, 3, 8, 15, 11, 4, 14, 4, 4, 2, 5, 5, 1, 7, 1,
        6, 14, 7, 7, 8, 5, 15, 4, 8, 6, 5, 6, 2, 1, 13, 1, 20, 15, 11, 9,
        2, 5, 6, 2, 11, 2, 6, 2, 5, 1, 5, 8, 4, 13, 19, 25, 4, 1, 1, 11,
        1, 34, 2, 5, 9, 14, 6, 2, 2, 6, 1, 1, 14, 1, 3, 14, 13, 1, 6, 12,
        21, 14, 14, 6, 32, 17, 8, 32, 9, 28, 1, 2, 4, 11, 8, 3, 1, 14, 2, 5,
        15, 1, 1, 1, 1, 3, 6, 4, 1, 3, 4, 11, 3, 1, 1, 11, 30, 1, 5, 1,
        4, 1, 5, 8, 1, 1, 3, 2, 4, 3, 17, 35, 2, 6, 12, 17, 3, 1, 6, 2,
        1, 1, 12, 2, 7, 3, 3, 2, 1, 16, 2, 8, 3, 6, 5, 4, 7, 3, 3, 8,
        1, 9, 8, 5, 1, 2, 1, 3, 2, 8, 1, 2, 9, 12, 1, 1, 2, 3, 8, 3,
        24, 12, 4, 3, 7, 5, 8, 3, 3, 3, 3, 3, 3, 1, 23, 10, 3, 1, 2, 2,
        6, 3, 1, 16, 1, 16, 22, 3, 10, 4, 11, 6, 9, 7, 7, 3, 6, 2, 2, 2,
        4, 10, 2, 1, 1, 2, 8, 7, 1, 6, 4, 1, 3, 3, 3, 5, 10, 12, 12, 2,
        3, 12, 8, 15, 1, 1, 16, 6, 6, 1, 5, 9, 11, 4, 11, 4, 2, 6, 12, 1,
        17, 5, 13, 1, 4, 9, 5, 1, 11, 2, 1, 8, 1, 5, 7, 28, 8, 3, 5, 10,
        2, 17, 3, 38, 22, 1, 2, 18, 12, 10, 4, 38, 18, 1, 4, 44, 19, 4, 1, 8,
        4, 1, 12, 1, 4, 31, 12, 1, 14, 7, 75, 7, 5, 10, 6, 6, 13, 3, 2, 11,
        11, 3, 2, 5, 28, 15, 6, 18, 18, 5, 6, 4, 3, 16, 1, 7, 18, 7, 36, 3,
        5, 3, 1, 7, 1, 9, 1, 10, 7, 2, 4, 2, 6, 2, 9, 7, 4, 3, 32, 12,
        3, 7, 10, 2, 23, 16, 3, 1, 12, 3, 31, 4, 11, 1, 3, 8, 9, 5, 1, 30,
        15, 6, 12, 3, 2, 2, 11, 19, 9, 14, 2, 6, 2, 3, 19, 13, 17, 5, 3, 3,
        25, 3, 14, 1, 1, 1, 36, 1, 3, 2, 19, 3, 13, 36, 9, 13, 31, 6, 4, 16,
        34, 2, 5, 4, 2, 3, 3, 5, 1, 1, 1, 4, 3, 1, 17, 3, 2, 3, 5, 3,
        1, 3, 2, 3, 5, 6, 3, 12, 11, 1, 3, 1, 2, 26, 7, 12, 7, 2, 14, 3,
        3, 7, 7, 11, 25, 25, 28, 16, 4, 36, 1, 2, 1, 6, 2, 1, 9, 3, 27, 17,
        4, 3, 4, 13, 4, 1, 3, 2, 2, 1, 10, 4, 2, 4, 6, 3, 8, 2, 1, 18,
        1, 1, 24, 2, 2, 4, 33, 2, 3, 63, 7, 1, 6, 40, 7, 3, 4, 4, 2, 4,
        15, 18, 1, 16, 1, 1, 11, 2, 41, 14, 1, 3, 18, 13, 3, 2, 4, 16, 2, 17,
        7, 15, 24, 7, 18, 13, 44, 2, 2, 3, 6, 1, 1, 7, 5, 1, 7, 1, 4, 3,
        3, 5, 10, 8, 2, 3, 1, 8, 1, 1, 27, 4, 2, 1, 12, 1, 2, 1, 10, 6,
        1, 6, 7, 5, 2, 3, 7, 11, 5, 11, 3, 6, 6, 2, 3, 15, 4, 9, 1, 1,
        2, 1, 2, 11, 2, 8, 12, 8, 5, 4, 2, 3, 1, 5, 2, 2, 1, 14, 1, 12,
        11, 4, 1, 11, 17, 17, 4, 3, 2, 5, 5, 7, 3, 1, 5, 9, 9, 8, 2, 5,
        6, 6, 13, 13, 2, 1, 2, 6, 1, 2, 2, 49, 4, 9, 1, 2, 10, 16, 7, 8,
        4, 3, 2, 23, 4, 58, 3, 29, 1, 14, 19, 19, 11, 11, 2, 7, 5, 1, 3, 4,
        6, 2, 18, 5, 12, 12, 17, 17, 3, 3, 2, 4, 1, 6, 2, 3, 4, 3, 1, 1,
        1, 1, 5, 1, 1, 9, 1, 3, 1, 3, 6, 1, 8, 1, 1, 2, 6, 4, 14, 3,
        1, 4, 11, 4, 1, 3, 32, 1, 2, 4, 13, 4, 1, 2, 4, 2, 1, 3, 1, 11,
        1, 4, 2, 1, 4, 4, 6, 3, 5, 1, 6, 5, 7, 6, 3, 23, 3, 5, 3, 5,
        3, 3, 13, 3, 9, 10, 1, 12, 10, 2, 3, 18, 13, 7, 160, 52, 4, 2, 2, 3,
        2, 14, 5, 4, 12, 4, 6, 4, 1, 20, 4, 11, 6, 2, 12, 27, 1, 4, 1, 2,
        2, 7, 4, 5, 2, 28, 3, 7, 25, 8, 3, 19, 3, 6, 10, 2, 2, 1, 10, 2,
        5, 4, 1, 3, 4, 1, 5, 3, 2, 6, 9, 3, 6, 2, 16, 3, 3, 16, 4, 5,
        5, 3, 2, 1, 2, 16, 15, 8, 2, 6, 21, 2, 4, 1, 22, 5, 8, 1, 1, 21,
        11, 2, 1, 11, 11, 19, 13, 12, 4, 2, 3, 2, 3, 6, 1, 8, 11, 1, 4, 2,
        9, 5, 2, 1, 11, 2, 9, 1, 1, 2, 14, 31, 9, 3, 4, 21, 14, 4, 8, 1,
        7, 2, 2, 2, 5, 1, 4, 20, 3, 3, 4, 10, 1, 11, 9, 8, 2, 1, 4, 5,
        14, 12, 14, 2, 17, 9, 6, 31, 4, 14, 1, 20, 13, 26, 5, 2, 7, 3, 6, 13,
        2, 4, 2, 19, 6, 2, 2, 18, 9, 3, 5, 12, 12, 14, 4, 6, 2, 3, 6, 9,
        5, 22, 4, 5, 25, 6, 4, 8, 5, 2, 6, 27, 2, 35, 2, 16, 3, 7, 8, 8,
        6, 6, 5, 9, 17, 2, 20, 6, 19, 2, 13, 3, 1, 1, 1, 4, 17, 12, 2, 14,
        7, 1, 4, 18, 12, 38, 33, 2, 10, 1, 1, 2, 13, 14, 17, 11, 50, 6, 33, 20,
        26, 74, 16, 23, 45, 50, 13, 38, 33, 6, 6, 7, 4, 4, 2, 1, 3, 2, 5, 8,
        7, 8, 9, 3, 11, 21, 9, 13, 1, 3, 10, 6, 7, 1, 2, 2, 18, 5, 5, 1,
        9, 9, 2, 68, 9, 19, 13, 2, 5, 1, 4, 4, 7, 4, 13, 3, 9, 10, 21, 17,
        3, 26, 2, 1, 5, 2, 4, 5, 4, 1, 7, 4, 7, 3, 4, 2, 1, 6, 1, 1,
        20, 4, 1, 9, 2, 2, 1, 3, 3, 2, 3, 2, 1, 1, 1, 20, 2, 3, 1, 6,
        2, 3, 6, 2, 4, 8, 1, 3, 2, 10, 3, 5, 3, 4, 4, 3, 4, 16, 1, 6,
        1, 10, 2, 4, 2, 1, 1, 2, 10, 11, 2, 2, 3, 1, 24, 31, 4, 10, 10, 2,
        5, 12, 16, 164, 15, 4, 16, 7, 9, 15, 19, 17, 1, 2, 1, 1, 5, 1, 1, 1,
        1, 1, 3, 1, 4, 3, 1, 3, 1, 3, 1, 2, 1, 1, 3, 3, 7, 2, 8, 1,
        2, 2, 2, 1, 3, 4, 3, 7, 8, 12, 92, 2, 10, 3, 1, 3, 14, 5, 25, 16,
        42, 4, 7, 7, 4, 2, 21, 5, 27, 26, 27, 21, 25, 30, 31, 2, 1, 5, 13, 3,
        22, 5, 6, 6, 11, 9, 12, 1, 5, 9, 7, 5, 5, 22, 60, 3, 5, 13, 1, 1,
        8, 1, 1, 3, 3, 2, 1, 9, 3, 3, 18, 4, 1, 2, 3, 7, 6, 3, 1, 2,
        3, 9, 1, 3, 1, 3, 2, 1, 3, 1, 1, 1, 2, 1, 11, 3, 1, 6, 9, 1,
        3, 2, 3, 1, 2, 1, 5, 1, 1, 4, 3, 4, 1, 2, 2, 4, 4, 1, 7, 2,
        1, 2, 2, 3, 5, 13, 18, 3, 4, 14, 9, 9, 4, 16, 3, 7, 5, 8, 2, 6,
        48, 28, 3, 1, 1, 4, 2, 14, 8, 2, 9, 2, 1, 15, 2, 4, 3, 2, 10, 16,
        12, 8, 7, 1, 1, 3, 1, 1, 1, 2, 7, 4, 1, 6, 4, 38, 39, 16, 23, 7,
        15, 15, 3, 2, 12, 7, 21, 37, 27, 6, 5, 4, 8, 2, 10, 8, 8, 6, 5, 1,
        2, 1, 3, 24, 1, 16, 17, 9, 23, 10, 17, 6, 1, 51, 55, 44, 13, 294, 9, 3,
        6, 2, 4, 2, 2, 15, 1, 1, 1, 13, 21, 17, 68, 14, 8, 9, 4, 1, 4, 9,
        3, 11, 7, 1, 1, 1, 5, 6, 3, 2, 1, 1, 1, 2, 3, 8, 1, 2, 2, 4,
        1, 5, 5, 2, 1, 4, 3, 7, 13, 4, 1, 4, 1, 3, 1, 1, 1, 5, 5, 10,
        1, 6, 1, 5, 2, 1, 5, 2, 4, 1, 4, 5, 7, 3, 18, 2, 9, 11, 32, 4,
        3, 3, 2, 4, 7, 11, 16, 9, 11, 8, 13, 38, 32, 8, 4, 2, 1, 1, 2, 1,
        2, 4, 4, 1, 1, 1, 4, 1, 21, 3, 11, 1, 16, 1, 1, 6, 1, 3, 2, 4,
        9, 8, 57, 7, 44, 1, 3, 3, 13, 3, 10, 1, 1, 7, 5, 2, 7, 21, 47, 63,
        3, 15, 4, 7, 1, 16, 1, 1, 2, 8, 2, 3, 42, 15, 4, 1, 29, 7, 22, 10,
        3, 78, 16, 12, 20, 18, 4, 67, 11, 5, 1, 3, 15, 6, 21, 31, 32, 27, 18, 13,
        71, 35, 5, 142, 4, 10, 1, 2, 50, 19, 33, 16, 35, 37, 16, 19, 27, 7, 1, 133,
        19, 1, 4, 8, 7, 20, 1, 4, 4, 1, 10, 3, 1, 6, 1, 2, 51, 5, 40, 15,
        24, 43, 22928, 11, 1, 13, 154, 70, 3, 1, 1, 7, 4, 10, 1, 2, 1, 1, 2, 1,
        2, 1, 2, 2, 1, 1, 2, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 3, 2, 1, 1, 1, 1, 2, 1, 1,
    };

    /// <summary>
    /// Returns the canonical Cyrillic ranges: default plus the Cyrillic and Cyrillic Supplement
    /// blocks and the two extended blocks. Mirrors <c>ImFontAtlas::GetGlyphRangesCyrillic</c>
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    public uint[] GetGlyphRangesCyrillic() => new uint[]
    {
        0x0020, 0x00FF, // Basic Latin + Latin Supplement
        0x0400, 0x052F, // Cyrillic + Cyrillic Supplement
        0x2DE0, 0x2DFF, // Cyrillic Extended-A
        0xA640, 0xA69F, // Cyrillic Extended-B
        0,
    };

    /// <summary>
    /// Returns the canonical Thai ranges: default plus Thai punctuation and the Thai block. Mirrors
    /// <c>ImFontAtlas::GetGlyphRangesThai</c>.
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    public uint[] GetGlyphRangesThai() => new uint[]
    {
        0x0020, 0x00FF, // Basic Latin
        0x2010, 0x205E, // Punctuations
        0x0E00, 0x0E7F, // Thai
        0,
    };

    /// <summary>
    /// Returns the canonical Vietnamese ranges: default plus the Latin Extended Additional letters
    /// used by Vietnamese. Mirrors <c>ImFontAtlas::GetGlyphRangesVietnamese</c>
    /// </summary>
    /// <returns>Inclusive codepoint pairs terminated by zero.</returns>
    public uint[] GetGlyphRangesVietnamese() => new uint[]
    {
        0x0020, 0x00FF, // Basic Latin
        0x0102, 0x0103,
        0x0110, 0x0111,
        0x0128, 0x0129,
        0x0168, 0x0169,
        0x01A0, 0x01A1,
        0x01AF, 0x01B0,
        0x1EA0, 0x1EF9,
        0,
    };

    /// <summary>
    /// Expands a compact table of cumulative codepoint offsets into the inclusive start/end range
    /// representation and appends the zero terminator. Mirrors canonical
    /// <c>UnpackAccumulativeOffsetsIntoRanges</c>.
    /// </summary>
    /// <param name="baseCodepoint">Codepoint the first offset is relative to.</param>
    /// <param name="offsets">Cumulative offsets; each entry both closes the previous range and opens the next.</param>
    /// <param name="outputOffset">Index in the destination array where the expanded pairs begin.</param>
    /// <param name="destination">
    /// Destination array sized for the base ranges, twice the offset count and the terminator.
    /// </param>
    /// <remarks>
    /// Each offset yields a single-codepoint range: the canonical encoding stores only the
    /// frequently used characters, so the start and end of a pair are equal. The running base is
    /// advanced by each offset because the table is cumulative, not delta-encoded from a fixed
    /// origin.
    /// </remarks>
    private static void UnpackAccumulativeOffsetsIntoRanges(
        int baseCodepoint, short[] offsets, int outputOffset, uint[] destination)
    {
        int at = outputOffset;
        for (int n = 0; n < offsets.Length; n++, at += 2)
        {
            destination[at] = destination[at + 1] = (uint)(baseCodepoint + offsets[n]);
            baseCodepoint += offsets[n];
        }
        destination[at] = 0;
    }


    /// <summary>
    /// Identifier table entry for one packed rectangle. Mirrors canonical
    /// <c>ImFontAtlasRectEntry</c>. The
    /// generation guards against stale identifiers after a rectangle is discarded and reused.
    /// </summary>
    internal struct ImFontAtlasRectEntry
    {
        /// <summary>Index into <see cref="_rects"/>, or the next free entry when unused.</summary>
        public int TargetIndex;

        /// <summary>Generation counter; increments on every discard so old identifiers fail lookup.</summary>
        public int Generation;

        /// <summary>Whether this entry currently names a live rectangle.</summary>
        public bool IsUsed;
    }

    #endregion

    #region Font loading

    /// <summary>
    /// Loads a default font through the canonical legacy selection heuristic, which prefers the
    /// scalable ProggyForever face and falls back to the ProggyClean bitmap face for small text.
    /// Mirrors canonical <c>ImFontAtlas::AddFontDefault</c>
    /// </summary>
    /// <param name="fontConfig">Optional configuration template; a default configuration is used when null.</param>
    /// <param name="expectedFontSize">
    /// Expected text size in pixels, standing in for the canonical
    /// <c>GetExpectedContextFontSize</c> probe.,
    /// which multiplies <c>Style.FontSizeBase</c> by the main and DPI scale factors. A value of zero
    /// or less means no owner context exists, which canonical resolves to the vector face; a value
    /// of 15 or more selects the vector face; a smaller positive value selects the bitmap face.
    /// Chunk 3 replaces this explicit parameter with style and context wiring once the port has an
    /// <c>ImGuiContext</c>.
    /// </param>
    /// <returns>The loaded font.</returns>
    /// <remarks>
    /// Canonical copies the configuration template by value before altering it; this port clones the
    /// caller's instance for the same reason, so repeated calls with one configuration do not compound
    /// offsets or scales.
    /// </remarks>
    public ImFont AddFontDefault(ImFontConfig? fontConfig = null, float expectedFontSize = 0.0f)
    {
        if (expectedFontSize <= 0.0f || expectedFontSize >= DefaultVectorFontThreshold)
            return AddFontDefaultVector(fontConfig);
        return AddFontDefaultBitmap(fontConfig);
    }

    /// <summary>
    /// Loads the embedded ProggyClean bitmap-oriented font. Mirrors canonical
    /// <c>AddFontDefaultBitmap</c>.
    /// </summary>
    /// <param name="fontConfig">Optional configuration template; a default configuration is used when null.</param>
    /// <returns>The loaded font.</returns>
    public ImFont AddFontDefaultBitmap(ImFontConfig? fontConfig = null)
    {
        ImFontConfig cfg;
        if (fontConfig is null)
        {
            // Without a template the bitmap font snaps horizontally, preventing sub-integer scaling in the
            // lower layers.
            cfg = new ImFontConfig { PixelSnapH = true };
        }
        else
        {
            cfg = fontConfig.Clone();
        }
        if (cfg.SizePixels <= 0.0f)
        {
            cfg.SizePixels = DefaultFontSize;
            cfg.Flags |= ImFontFlags.ImplicitRefSize;
        }
        if (string.IsNullOrEmpty(cfg.Name))
            cfg.Name = "ProggyClean.ttf";
        if (cfg.EllipsisChar == 0)
            cfg.EllipsisChar = 0x0085;
        // Canonical adds +1 pixel of vertical offset per 13 units to align the pixel-clean font.
        cfg.GlyphOffset = new ImVec2(cfg.GlyphOffset.X, cfg.GlyphOffset.Y + 1.0f * (cfg.SizePixels / 13.0f));

        byte[] ttf = ImGuiDefaultFontData.DecompressProggyClean();
        cfg.FontData = ttf;
        cfg.FontDataSize = ttf.Length;
        cfg.FontDataOwnedByAtlas = true;

        return AddFont(cfg);
    }

    /// <summary>
    /// Loads the embedded ProggyForever scalable vector font, which canonical describes as a
    /// minimal face designed to match ProggyClean while scaling cleanly. Mirrors canonical
    /// <c>AddFontDefaultVector</c>.
    /// </summary>
    /// <param name="fontConfig">Optional configuration template; a default configuration is used when null.</param>
    /// <returns>The loaded font.</returns>
    /// <remarks>
    /// A default configuration snaps horizontal advances to whole pixels and uses a 13-pixel
    /// reference size. The face name defaults to <c>ProggyForever.ttf</c>; the rasterizer scale is
    /// multiplied by 1.015 to match ProggyClean's apparent width; and the vertical glyph offset
    /// rises by half a pixel per 16 reference pixels to align the baseline.
    /// </remarks>
    public ImFont AddFontDefaultVector(ImFontConfig? fontConfig = null)
    {
        bool isDefaultConfig = fontConfig == null;
        ImFontConfig cfg = fontConfig is null ? new ImFontConfig() : fontConfig.Clone();
        if (isDefaultConfig)
            cfg.PixelSnapH = true; // Precisely match ProggyClean while preventing sub-integer scaling at lower layers.
        if (cfg.SizePixels <= 0.0f)
        {
            cfg.SizePixels = DefaultFontSize;
            cfg.Flags |= ImFontFlags.ImplicitRefSize;
        }
        if (string.IsNullOrEmpty(cfg.Name))
            cfg.Name = "ProggyForever.ttf";
        cfg.ExtraSizeScale *= 1.015f; // Match ProggyClean's apparent width.
        // Closer match to ProggyClean, and avoids descenders rising too high with the current glyph metrics.
        cfg.GlyphOffset = new ImVec2(cfg.GlyphOffset.X, cfg.GlyphOffset.Y + 0.5f * (cfg.SizePixels / 16.0f));

        byte[] ttf = ImGuiDefaultFontData.DecompressProggyForever();
        cfg.FontData = ttf;
        cfg.FontDataSize = ttf.Length;
        cfg.FontDataOwnedByAtlas = true;

        return AddFont(cfg);
    }

    /// <summary>
    /// Adds a font from an in-memory TrueType image. Mirrors canonical
    /// <c>AddFontFromMemoryTTF</c>.
    /// </summary>
    /// <param name="fontData">TTF/OTF file image; ownership is governed by <paramref name="fontConfig"/>.</param>
    /// <param name="sizePixels">Reference size in pixels.</param>
    /// <param name="fontConfig">Optional configuration template.</param>
    /// <returns>The loaded font.</returns>
    public ImFont AddFontFromMemoryTTF(byte[] fontData, float sizePixels, ImFontConfig? fontConfig = null)
    {
        ImFontConfig cfg = fontConfig is null ? new ImFontConfig() : fontConfig.Clone();
        cfg.FontData = fontData;
        cfg.FontDataSize = fontData.Length;
        if (cfg.SizePixels <= 0.0f)
            cfg.SizePixels = sizePixels > 0.0f ? sizePixels : DefaultFontSize;
        return AddFont(cfg);
    }

    /// <summary>
    /// Adds a font from an stb_compress stream, as produced by the canonical
    /// <c>binary_to_compressed_c</c> tool. Mirrors <c>AddFontFromMemoryCompressedTTF</c>
    /// </summary>
    /// <param name="compressedFontData">stb_compress stream.</param>
    /// <param name="sizePixels">Reference size in pixels.</param>
    /// <param name="fontConfig">Optional configuration template.</param>
    /// <returns>The loaded font.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the compressed stream is malformed.</exception>
    public ImFont AddFontFromMemoryCompressedTTF(byte[] compressedFontData, float sizePixels, ImFontConfig? fontConfig = null)
    {
        byte[]? ttf = ImGuiDefaultFontData.DecompressStb(compressedFontData);
        if (ttf == null)
            throw new InvalidOperationException("Compressed font data is malformed.");
        return AddFontFromMemoryTTF(ttf, sizePixels, fontConfig);
    }

    /// <summary>
    /// Adds a font from a TrueType or OpenType file on disk. Mirrors canonical
    /// <c>ImFontAtlas::AddFontFromFileTTF</c>.
    /// </summary>
    /// <param name="filename">Path to the font file.</param>
    /// <param name="sizePixels">Reference size in pixels.</param>
    /// <param name="fontConfig">Optional configuration template.</param>
    /// <returns>The loaded font, or <see langword="null"/> when the file could not be read.</returns>
    /// <remarks>
    /// Canonical uses <c>ImFileLoadToMemory</c>; this port uses <see cref="System.IO.File"/>, which is
    /// the file-access mechanism established for the managed re-implementation. A read failure returns
    /// <see langword="null"/> exactly as canonical does; when the caller neither supplies a
    /// configuration nor sets <see cref="ImFontFlags.NoLoadError"/> the failure is reported through
    /// <see cref="System.Diagnostics.Debug.WriteLine(string)"/> to stand in for canonical's user-error
    /// assertion, whose release behavior is also to return <see langword="null"/>.
    /// </remarks>
    public ImFont? AddFontFromFileTTF(string filename, float sizePixels, ImFontConfig? fontConfig = null)
    {
        if (Locked)
            throw new InvalidOperationException("Cannot modify a locked ImFontAtlas.");

        byte[]? data = null;
        try
        {
            data = System.IO.File.ReadAllBytes(filename);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            data = null;
        }

        if (data == null || data.Length == 0)
        {
            if (fontConfig is null || (fontConfig.Flags & ImFontFlags.NoLoadError) == 0)
                System.Diagnostics.Debug.WriteLine($"While loading '{filename}': could not load font file.");
            return null;
        }

        ImFontConfig cfg = fontConfig is null ? new ImFontConfig() : fontConfig.Clone();
        cfg.FontData = data;
        cfg.FontDataSize = data.Length;
        if (cfg.SizePixels <= 0.0f)
            cfg.SizePixels = sizePixels > 0.0f ? sizePixels : DefaultFontSize;
        if (string.IsNullOrEmpty(cfg.Name))
            cfg.Name = System.IO.Path.GetFileName(filename);
        return AddFont(cfg);
    }

    /// <summary>
    /// Adds a font from an stb_compress stream encoded as base85 text, as produced by the canonical
    /// <c>binary_to_compressed_c</c> tool with <c>-base85</c>. Mirrors
    /// <c>ImFontAtlas::AddFontFromMemoryCompressedBase85TTF</c>
    /// </summary>
    /// <param name="compressedTtfDataBase85">Base85-encoded stb_compress stream.</param>
    /// <param name="sizePixels">Reference size in pixels.</param>
    /// <param name="fontConfig">Optional configuration template.</param>
    /// <returns>The loaded font.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compressedTtfDataBase85"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the decoded stream is malformed.</exception>
    public ImFont AddFontFromMemoryCompressedBase85TTF(
        string compressedTtfDataBase85, float sizePixels, ImFontConfig? fontConfig = null)
    {
        if (compressedTtfDataBase85 == null)
            throw new ArgumentNullException(nameof(compressedTtfDataBase85));

        byte[] compressed = DecodeBase85(compressedTtfDataBase85);
        return AddFontFromMemoryCompressedTTF(compressed, sizePixels, fontConfig);
    }

    /// <summary>
    /// Decodes the canonical base85 encoding used by <c>binary_to_compressed_c -base85</c>. Mirrors
    /// <c>Decode85Byte</c>/<c>Decode85</c>.
    /// </summary>
    /// <param name="text">Base85 text; each five characters encode four bytes.</param>
    /// <returns>The decoded bytes.</returns>
    /// <remarks>
    /// Canonical walks the NUL-terminated source, writes four bytes per five input characters, and
    /// allocates <c>((length + 4) / 5) * 4</c> bytes; the managed port derives the same output length
    /// from the string length and stops at the final complete group. Trailing characters that do not
    /// fill a group are ignored, matching the canonical allocation bound.
    /// </remarks>
    private static byte[] DecodeBase85(string text)
    {
        int groupCount = (text.Length + 4) / 5;
        byte[] output = new byte[groupCount * 4];

        for (int group = 0; group < groupCount; group++)
        {
            int at = group * 5;
            uint tmp = 0;
            for (int k = 0; k < 5; k++)
            {
                char c = at + k < text.Length ? text[at + k] : '!';
                uint digit = c >= '\\' ? (uint)(c - 36) : (uint)(c - 35);
                tmp += digit * Pow85(k);
            }

            output[group * 4 + 0] = (byte)(tmp & 0xFF);
            output[group * 4 + 1] = (byte)((tmp >> 8) & 0xFF);
            output[group * 4 + 2] = (byte)((tmp >> 16) & 0xFF);
            output[group * 4 + 3] = (byte)((tmp >> 24) & 0xFF);
        }

        return output;
    }

    /// <summary>Returns 85 raised to the given power, the weight canonical applies to the fifth-to-first base85 digit.</summary>
    /// <param name="power">Zero-based digit position, zero through four.</param>
    /// <returns>85 to the power of <paramref name="power"/>.</returns>
    private static uint Pow85(int power)
    {
        uint result = 1;
        for (int i = 0; i < power; i++)
            result *= 85;
        return result;
    }

    /// <summary>
    /// Removes a font and all of its sources from the atlas. Mirrors canonical
    /// <c>ImFontAtlas::RemoveFont</c>.
    /// </summary>
    /// <param name="font">Font to remove; it must belong to this atlas.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="font"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the atlas is <see cref="Locked"/>, or when the font does not belong to this atlas,
    /// which stands in for canonical's <c>IM_ASSERT(removed)</c>.
    /// </exception>
    /// <remarks>
    /// Every baked set owned by the font is discarded first so its packed rectangles return to the
    /// packer, then the sources referencing the font are dropped, the font is detached from the atlas
    /// and the source list is compacted. Canonical also notifies external font bindings; this port's
    /// draw lists hold the atlas rather than an individual font, so there is no per-font binding to
    /// update.
    /// </remarks>
    public void RemoveFont(ImFont font)
    {
        if (font == null)
            throw new ArgumentNullException(nameof(font));
        if (Locked)
            throw new InvalidOperationException("Cannot modify a locked ImFontAtlas.");

        font.ClearOutputData();
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Sources[i].DstFont, font))
                Sources.RemoveAt(i);
        }

        if (!Fonts.Remove(font))
            throw new InvalidOperationException("Font does not belong to this atlas.");

        font.OwnerAtlas = null;
    }

    /// <summary>
    /// Removes every font and source while keeping the texture and builder state, then resets the
    /// builder so the freed rectangles are not left referring to removed fonts. Mirrors canonical
    /// <c>ImFontAtlas::ClearFonts</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the atlas is <see cref="Locked"/>.</exception>
    /// <remarks>
    /// Canonical destroys the builder (<c>ImFontAtlasBuildDestroy</c>) and clears the input data. This
    /// port's builder is a lazily initialized set of rectangles and a skyline packer, so the equivalent
    /// is to clear the rectangle state (<see cref="ClearTexData"/> minus the texture release) and mark
    /// the builder uninitialized, after which the next <see cref="Build"/> recreates it.
    /// </remarks>
    public void ClearFonts()
    {
        if (Locked)
            throw new InvalidOperationException("Cannot modify a locked ImFontAtlas.");

        ClearInputData();
        Fonts.Clear();
        TexIsBuilt = false;
        ResetBuilder();
    }

    /// <summary>
    /// Releases each font's output state, detaches and clears its sources, marks the fonts as
    /// glyph-less, and clears the atlas source list. Mirrors canonical
    /// <c>ImFontAtlas::ClearInputData</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the atlas is <see cref="Locked"/>.</exception>
    /// <remarks>
    /// Canonical also destroys each source's loader-owned data. In this port the source's
    /// <see cref="ImFontConfig.StbFont"/> parser is cached per source and shared by its bakes, so
    /// dropping the atlas source list releases it with the configuration; each font's
    /// <see cref="ImFont.ClearOutputData"/> call already invalidates the baked sets that referenced it.
    /// </remarks>
    public void ClearInputData()
    {
        if (Locked)
            throw new InvalidOperationException("Cannot modify a locked ImFontAtlas.");

        for (int i = 0; i < Fonts.Count; i++)
        {
            ImFont font = Fonts[i];
            font.ClearOutputData();
            font.Sources.Clear();
            font.Flags |= ImFontFlags.NoLoadGlyphs;
        }

        Sources.Clear();
    }

    /// <summary>
    /// Discards the builder's rectangle and packer state without releasing the texture, so the next
    /// build recreates the reserved white and line regions. Mirrors the builder-destroy portion of
    /// canonical <c>ImFontAtlasBuildDestroy</c>.
    /// </summary>
    private void ResetBuilder()
    {
        _builderInitialized = false;
        _rects.Clear();
        _rectEntries.Clear();
        _rectsIndexFreeListStart = RectIdInvalid;
        _packIdWhiteRect = RectIdInvalid;
        _packIdLinesTexData = RectIdInvalid;
        _rectsPackedCount = 0;
        _rectsPackedSurface = 0;
        _rectsDiscardedCount = 0;
        _rectsDiscardedSurface = 0;
        _maxRectSizeW = 0;
        _maxRectSizeH = 0;
        _maxRectBoundsW = 0;
        _maxRectBoundsH = 0;
    }


    /// <param name="fontConfig">Source configuration with font data attached.</param>
    /// <returns>The font that receives the source.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fontConfig"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when no font data is attached.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the atlas is <see cref="Locked"/> because it is rebuilding its contents. Canonical
    /// rejects the same case. since a source added
    /// mid-rebuild would leave the atlas and its bakes inconsistent.
    /// </exception>
    public ImFont AddFont(ImFontConfig fontConfig)
    {
        if (Locked)
            throw new InvalidOperationException("Cannot modify a locked ImFontAtlas.");
        if (fontConfig == null)
            throw new ArgumentNullException(nameof(fontConfig));
        if (fontConfig.FontData == null)
            throw new ArgumentException("Font configuration has no font data.", nameof(fontConfig));

        ImFont font;
        if (fontConfig.MergeMode && Fonts.Count > 0)
        {
            font = Fonts[^1];
            // The merged source changes every baked set's glyph output, so existing sets must be
            // discarded before the new source is appended. Mirrors canonical
            // ImFontAtlasFontDiscardBakes(this, font, 0) at.
            FontDiscardBakes(font, 0);
        }
        else
        {
            // Only a newly created font consumes an identifier; a merged source joins the previous
            // font. Canonical assigns from FontNextUniqueID and captures LegacySize here
            font = new ImFont
            {
                OwnerAtlas = this,
                FontId = (uint)FontNextUniqueID++,
                LegacySize = fontConfig.SizePixels > 0.0f ? fontConfig.SizePixels : DefaultFontSize,
            };
            Fonts.Add(font);
        }

        font.Sources.Add(fontConfig);
        fontConfig.DstFont = font;
        Sources.Add(fontConfig);

        // Canonical copies the exclusion list when a source is stored so the font owns its range data
        // independently of the caller's array. The
        // managed port performs the same defensive copy here; the property is replaced on the stored
        // configuration, whose lifetime is now the font atlas's, not the caller's.
        if (fontConfig.GlyphExcludeRanges != null)
            fontConfig.GlyphExcludeRanges = (uint[])fontConfig.GlyphExcludeRanges.Clone();

        // Canonical resolves the fallback and ellipsis characters whenever a source is attached
        // (ImFontAtlasFontSourceAddToFont at ).
        SetupFontSpecialGlyphs(font, fontConfig);
        return font;
    }

    #endregion

    #region Build and texture access

    /// <summary>Identifier value meaning "no rectangle"; every real identifier has index 0 or above.</summary>
    private const int RectIdInvalid = -1;

    /// <summary>Bit mask isolating the rectangle-entry index inside an identifier.</summary>
    private const int RectIdIndexMask = 0x0007FFFF;

    /// <summary>Bit mask isolating the generation counter inside an identifier.</summary>
    private const int RectIdGenerationMask = 0x3FF00000;

    /// <summary>Bit shift placing the generation counter above the entry index.</summary>
    private const int RectIdGenerationShift = 20;

    /// <summary>Tab advance multiplier; mirrors canonical <c>IM_TABSIZE</c>.</summary>
    private const int TabSize = 4;

    /// <summary>
    /// Unicode replacement character. Mirrors canonical <c>IM_UNICODE_CODEPOINT_INVALID</c>, the
    /// fallback candidate tried after the font's current fallback value.
    /// </summary>
    private const uint InvalidCodepoint = 0xFFFD;

    /// <summary>
    /// Font size at or above which text layout loads advances only and defers rasterization until
    /// the glyph is drawn. Mirrors canonical <c>IMGUI_FONT_SIZE_THRESHOLD_FOR_LOADADVANCEXONLYMODE</c>
    /// </summary>
    private const float AdvanceOnlySizeThreshold = 128.0f;

    /// <summary>
    /// Initializes the packer, reserves the white texel block and the baked line region, then
    /// preloads every source glyph range at each font's reference size. This is the canonical legacy
    /// build path used when the renderer does not support incremental textures.
    /// </summary>
    /// <returns>Always <see langword="true"/> for the supported font set.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the atlas is already <see cref="Locked"/>, matching the canonical legacy build
    /// entry point, which rejects a locked atlas.
    /// </exception>
    /// <remarks>
    /// The default font is added before the lock is taken so the atlas can populate itself, then
    /// <see cref="Locked"/> covers the builder, texture and preload work because none of it may
    /// interleave with adding a source. The lock is released before this method returns; canonical
    /// instead holds the lock across the whole frame when the backend cannot consume texture updates
    /// </remarks>
    public bool Build()
    {
        if (Locked)
            throw new InvalidOperationException("Cannot build a locked ImFontAtlas.");

        if (Fonts.Count == 0)
            AddFontDefault();

        Locked = true;
        try
        {
            // Canonical ImFontAtlasBuildMain clears the texture and builder when the existing texture
            // format differs from TexDesiredFormat., so a
            // legacy GetTexDataAsAlpha8/GetTexDataAsRGBA32 call after an earlier build recreates the
            // texture in the requested format.
            if (TexData != null && TexData.Format != TexDesiredFormat)
            {
                ClearTextureState();
                ResetBuilder();
            }

            EnsureBuilder();
            PreloadAllGlyphs();
            TexIsBuilt = true;
        }
        finally
        {
            Locked = false;
        }
        return true;
    }

    /// <summary>
    /// Advances the atlas to a new frame: records the frame counter and drops every font's one-entry
    /// baked-set cache so baked pointers do not cross frames. Mirrors the frame bookkeeping of
    /// canonical <c>ImFontAtlasUpdateNewFrame</c>
    /// that idle-bake eviction depends on, including its protection against being advanced twice for
    /// one frame; texture upload and legacy-build handling remain the caller's
    /// responsibility in this port.
    /// </summary>
    /// <param name="frameCount">
    /// Frame counter for the new frame. It must be strictly greater than <see cref="FrameCount"/> so a
    /// bake cannot be treated as idle before it has been unused for the requested number of frames.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="frameCount"/> is not greater than <see cref="FrameCount"/>, which
    /// means the atlas was advanced twice for one frame or the counter was rewound. Canonical guards
    /// the same condition. because bake ages are derived
    /// from the frame counter.
    /// </exception>
    public void UpdateNewFrame(int frameCount)
    {
        if (frameCount <= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount,
                $"The atlas frame count must increase: it is already {FrameCount}.");
        }

        FrameCount = frameCount;
        for (int i = 0; i < Fonts.Count; i++)
            Fonts[i].LastBaked = null;
    }

    /// <summary>
    /// Releases idle baked sets and shrinks the texture toward the smallest surface that still holds
    /// the remaining rectangles. Mirrors canonical <c>ImFontAtlas::CompactCache</c>
    ///., which delegates to
    /// <c>ImFontAtlasTextureCompact</c>. Hosts call this when memory should be reclaimed outside the
    /// packing path; the packing path instead reclaims automatically through
    /// <c>ImFontAtlasTextureMakeSpace</c>.
    /// </summary>
    public void CompactCache()
    {
        EnsureBuilder();
        TextureCompact();
    }

    /// <summary>
    /// Creates the initial texture and packer state if they do not exist yet. Safe to call
    /// repeatedly; the white texel block and line region are reserved exactly once per build.
    /// Mirrors canonical <c>ImFontAtlasBuildInit</c>.
    /// </summary>
    private void EnsureBuilder()
    {
        if (_builderInitialized)
            return;

        if (TexData == null || TexData.Pixels == null)
        {
            // Canonical starts at the power-of-two minimum size and grows towards a square.
            CreateTexture(UpperPowerOfTwo(TexMinWidth), UpperPowerOfTwo(TexMinHeight));
        }

        PackInit();

        // Mark the builder live before reserving regions so the rectangle API does not recurse.
        _builderInitialized = true;
        BuildUpdateTexData();
        UpdateDrawListsSharedData();
    }

    /// <summary>
    /// Loads every codepoint in each source's glyph ranges at the font's reference size. Ports the
    /// canonical legacy preload. so behavior at the
    /// 13 px reference size is unchanged by lazy per-size baking.
    /// </summary>
    private void PreloadAllGlyphs()
    {
        for (int fontIndex = 0; fontIndex < Fonts.Count; fontIndex++)
        {
            ImFont font = Fonts[fontIndex];
            if (font.Sources.Count == 0)
                continue;

            // Canonical preloads at the font's captured legacy size
            ImFontBaked baked = font.GetFontBakedCore(font.LegacySize, -1.0f, allowLockedCreation: true);

            if (font.FallbackChar != 0)
                baked.FindGlyph(font.FallbackChar);
            if (font.EllipsisChar != 0)
                baked.FindGlyph(font.EllipsisChar);

            for (int sourceIndex = 0; sourceIndex < font.Sources.Count; sourceIndex++)
            {
                ImFontConfig source = font.Sources[sourceIndex];
                uint[] ranges = source.GlyphRanges ?? GetGlyphRangesDefault();
                for (int r = 0; r + 1 < ranges.Length && !(ranges[r] == 0 && ranges[r + 1] == 0); r += 2)
                {
                    for (uint codepoint = ranges[r]; codepoint <= ranges[r + 1] && codepoint <= 0xFFFF; codepoint++)
                        baked.FindGlyph(codepoint);
                }
            }
        }
    }

    /// <summary>
    /// Creates the baked glyph set for one font, size and density, initializes its vertical metrics
    /// and blank glyphs, and registers it on the font. Mirrors canonical <c>ImFontAtlasBakedAdd</c>
    ///. and the stb loader's
    /// <c>FontBakedInit</c>.
    /// </summary>
    /// <param name="font">Font that owns the new baked set.</param>
    /// <param name="size">Rounded pixel size.</param>
    /// <param name="density">Rasterizer density.</param>
    /// <returns>The newly created baked glyph set.</returns>
    internal ImFontBaked AddBaked(ImFont font, float size, float density)
    {
        EnsureBuilder();

        var baked = new ImFontBaked
        {
            Size = size,
            RasterizerDensity = density,
            BakedId = ImFontAtlasBakedGetId(font.FontId, size, density),
            OwnerFont = font,
            LastUsedFrame = FrameCount,
        };
        baked.GrowIndex(256);

        InitBakedMetrics(font, baked);
        SetupBlanks(baked);
        font.BakedList.Add(baked);
        return baked;
    }

    /// <summary>
    /// CRC32c lookup table used to derive baked identifiers, equivalent to canonical
    /// <c>GCrc32LookupTable</c> for the default (non-legacy) variant
    ///. Building it from the reflected polynomial
    /// produces exactly the canonical constants while keeping the derivation visible.
    /// </summary>
    private static readonly uint[] Crc32cTable = BuildCrc32cTable();

    /// <summary>
    /// Fills the CRC32c table by repeated reduction with the reflected polynomial.
    /// </summary>
    /// <returns>The 256-entry table used by <see cref="HashData"/>.</returns>
    private static uint[] BuildCrc32cTable()
    {
        const uint reflectedPolynomial = 0x82F63B78u;
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1u) != 0 ? (crc >> 1) ^ reflectedPolynomial : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    /// <summary>
    /// Derives the stable identifier canonical stores in <see cref="ImFontBaked.BakedId"/>. Mirrors
    /// <c>ImFontAtlasBakedGetId</c>., which hashes
    /// the font identifier together with the baked size and rasterizer density, and
    /// <c>ImHashData</c>., which consumes the bytes of
    /// that struct in memory order with a zero seed.
    /// </summary>
    /// <param name="fontId">Identifier of the owning font.</param>
    /// <param name="bakedSize">Rounded baked pixel size.</param>
    /// <param name="rasterizerDensity">Rasterizer density of the bake.</param>
    /// <returns>The deterministic baked identifier.</returns>
    private static uint ImFontAtlasBakedGetId(uint fontId, float bakedSize, float rasterizerDensity)
    {
        // Canonical hashes a { ImGuiID, float, float } struct, so preserve that exact 12-byte layout.
        Span<uint> values = stackalloc uint[3];
        values[0] = fontId;
        values[1] = BitConverter.SingleToUInt32Bits(bakedSize);
        values[2] = BitConverter.SingleToUInt32Bits(rasterizerDensity);
        return HashData(MemoryMarshal.AsBytes(values));
    }

    /// <summary>
    /// Computes the seed-zero CRC32c value canonical <c>ImHashData</c> would return for a byte run.
    /// </summary>
    /// <param name="data">Bytes to hash in memory order.</param>
    /// <returns>The hashed value.</returns>
    private static uint HashData(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu; // Canonical starts from ~seed with seed == 0.
        for (int i = 0; i < data.Length; i++)
            crc = (crc >> 8) ^ Crc32cTable[(crc & 0xFF) ^ data[i]];
        return ~crc;
    }

    /// <summary>
    /// Initializes ascent and descent for a newly created baked set from the font's first source.
    /// Mirrors the stb loader's <c>FontBakedInit</c>
    /// </summary>
    /// <param name="font">Font being baked.</param>
    /// <param name="baked">Baked set receiving the vertical metrics.</param>
    private void InitBakedMetrics(ImFont font, ImFontBaked baked)
    {
        if (font.Sources.Count == 0)
            return;
        ImFontConfig source = font.Sources[0];
        if (source.MergeMode)
            return;

        EnsureStbFont(font, source);
        if (source.StbFont == null)
            return;

        float extraScale = source.ExtraSizeScale != 0.0f ? source.ExtraSizeScale : 1.0f;
        float scaleForLayout = source.StbScaleFactor * baked.Size / extraScale;
        source.StbFont.GetFontVMetrics(out int unscaledAscent, out int unscaledDescent, out _);
        baked.Ascent = MathF.Ceiling(unscaledAscent * scaleForLayout);
        baked.Descent = MathF.Floor(unscaledDescent * scaleForLayout);
    }

    /// <summary>
    /// Lazily initializes the TrueType parser and font-unit scale for one source. Mirrors the
    /// canonical <c>FontSrcInit</c> cached on the source
    ///., including its merge-mode handling: a
    /// merged source with no explicit size inherits the first source's reference size and scales its
    /// own size relative to it, so glyphs merged from a differently sized source align with the
    /// base face.
    /// </summary>
    /// <param name="font">Font whose source is initialized.</param>
    /// <param name="source">Source supplying the font image.</param>
    internal void EnsureStbFont(ImFont font, ImFontConfig source)
    {
        if (source.StbInitialized)
            return;
        source.StbInitialized = true;

        byte[]? data = source.FontData;
        if (data == null)
            return;

        int offset = ImStbTrueTypeFont.GetFontOffsetForIndex(data, (int)source.FontNo);
        if (offset < 0)
            return;

        ImStbTrueTypeFont? stb = ImStbTrueTypeFont.Init(data, offset);
        if (stb == null)
            return;

        float scale = stb.ScaleForPixelHeight(1.0f);
        if (source.MergeMode)
        {
            float referenceSize = font.Sources.Count > 0 ? font.Sources[0].SizePixels : 0.0f;
            if (source.SizePixels == 0.0f)
                source.SizePixels = referenceSize;
            if (source.SizePixels != 0.0f && referenceSize != 0.0f)
                scale *= source.SizePixels / referenceSize;
        }

        float extraScale = source.ExtraSizeScale != 0.0f ? source.ExtraSizeScale : 1.0f;
        source.StbFont = stb;
        source.StbScaleFactor = scale * extraScale;
    }

    /// <summary>
    /// Marks the space glyph invisible and installs a tab glyph whose advance spans
    /// <see cref="TabSize"/> spaces. Mirrors canonical <c>ImFontAtlasBuildSetupFontBakedBlanks</c>
    /// </summary>
    /// <param name="baked">Baked set to configure.</param>
    private void SetupBlanks(ImFontBaked baked)
    {
        ImFontGlyph? space = baked.FindGlyphNoFallback(' ');
        if (space.HasValue && space.Value.Codepoint == ' ' && baked.IndexLookup[' '] >= 0)
        {
            int index = baked.IndexLookup[' '];
            ImFontGlyph glyph = baked.Glyphs[index];
            glyph.Visible = false;
            baked.Glyphs[index] = glyph;
        }

        if (space.HasValue && baked.FindGlyphNoFallback('\t') == null)
        {
            var tab = new ImFontGlyph { Codepoint = '\t', AdvanceX = space.Value.AdvanceX * TabSize };
            AddGlyph(baked, null, tab);
        }
    }

    /// <summary>
    /// Resolves the fallback and ellipsis characters for a font when a source is attached, and marks
    /// the ellipsis for synthesis when no source supplies one. Ports canonical
    /// <c>ImFontAtlasBuildSetupFontSpecialGlyphs</c>,
    /// which canonical invokes from <c>ImFontAtlasFontSourceAddToFont</c> for every
    /// source, including merged ones.
    /// </summary>
    /// <param name="font">Font whose special glyphs are resolved.</param>
    /// <param name="source">Source being attached; its explicit ellipsis is the first candidate.</param>
    /// <remarks>
    /// Detection is one-shot per font: a non-zero <see cref="ImFont.FallbackChar"/> or
    /// <see cref="ImFont.EllipsisChar"/>, including one resolved for an earlier source, suppresses
    /// re-detection, matching canonical. A candidate is selected only when a source face actually
    /// contains it (<see cref="ImFont.IsGlyphInFont"/>); when no ellipsis candidate exists the font
    /// requests the legacy U+0085 and sets <see cref="ImFont.EllipsisAutoBake"/> so the lazy resolver
    /// builds a three-period glyph instead.
    /// </remarks>
    private void SetupFontSpecialGlyphs(ImFont font, ImFontConfig source)
    {
        // Fallback candidates in canonical order: the current value, the replacement character, '?'
        // and the space. Only a candidate the face contains may become the fallback.
        if (font.FallbackChar == 0)
        {
            uint[] fallbackCandidates = { font.FallbackChar, InvalidCodepoint, '?', ' ' };
            for (int i = 0; i < fallbackCandidates.Length; i++)
            {
                if (fallbackCandidates[i] != 0 && font.IsGlyphInFont(fallbackCandidates[i]))
                {
                    font.FallbackChar = fallbackCandidates[i];
                    break;
                }
            }
        }

        // Ellipsis candidates in canonical order: the source's explicit request, U+2026 (the preferred
        // horizontal ellipsis) and the legacy U+0085. The first candidate the face contains wins.
        if (font.EllipsisChar == 0)
        {
            uint[] ellipsisCandidates = { source.EllipsisChar, 0x2026, 0x0085 };
            for (int i = 0; i < ellipsisCandidates.Length; i++)
            {
                if (ellipsisCandidates[i] != 0 && font.IsGlyphInFont(ellipsisCandidates[i]))
                {
                    font.EllipsisChar = ellipsisCandidates[i];
                    break;
                }
            }
        }

        // No source supplies the ellipsis: defer to the three-period synthesis at the legacy codepoint.
        if (font.EllipsisChar == 0)
        {
            font.EllipsisChar = 0x0085;
            font.EllipsisAutoBake = true;
        }
    }

    /// <summary>
    /// Reports whether a source accepts a codepoint for lazy glyph resolution, honoring its exclusion
    /// ranges. Ports canonical <c>ImFontAtlasBuildAcceptCodepointForSource</c>
    /// </summary>
    /// <param name="source">Source whose exclusion list is consulted.</param>
    /// <param name="codepoint">Codepoint being requested.</param>
    /// <returns><see langword="false"/> when the codepoint falls inside an excluded range.</returns>
    /// <remarks>
    /// Canonical documents that the exclusion list is a small array; the managed port walks the
    /// zero-terminated pairs directly, exactly as the canonical routine does.
    /// </remarks>
    private static bool AcceptCodepointForSource(ImFontConfig source, uint codepoint)
    {
        uint[]? excludeRanges = source.GlyphExcludeRanges;
        if (excludeRanges == null)
            return true;
        for (int i = 0; i + 1 < excludeRanges.Length && excludeRanges[i] != 0; i += 2)
        {
            if (codepoint >= excludeRanges[i] && codepoint <= excludeRanges[i + 1])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Bakes one codepoint on demand, packing and rasterizing it from the first source that contains
    /// it. Ports the stb loader's glyph load and glyph registration,
    /// including the canonical scan across every source of the font so merged sources participate in
    /// lazy baking. The requested codepoint is first resolved through the font's
    /// remap pairs.
    /// </summary>
    /// <param name="baked">Baked set receiving the glyph.</param>
    /// <param name="codepoint">Codepoint to bake.</param>
    /// <param name="onlyAdvanceX">When true, only the advance is recorded and no bitmap is rasterized.</param>
    /// <returns>The index of the new glyph in <see cref="ImFontBaked.Glyphs"/>, or -1 when missing or
    /// when only the advance was loaded.</returns>
    internal int LoadGlyph(ImFontBaked baked, uint codepoint, bool onlyAdvanceX)
    {
        ImFont? font = baked.OwnerFont;
        if (font == null)
            return -1;

        // Canonical refuses to bake new glyphs while the atlas is locked or while the font has opted out of
        // loading, and in both cases lazily installs the fallback so the caller still resolves a visible
        // glyph. Only the opt-out clause is applied
        // here: canonical preloads every configured glyph range during the atlas build, whereas this port
        // bakes glyphs on demand while the atlas can still hold its build or repack lock, so refusing to
        // load under <see cref="Locked"/> would leave ordinary text without glyphs.
        //
        // The password mask depends on the opt-out clause: it swaps the baked lookup tables for an empty
        // set and sets NoLoadGlyphs, so every character must fall through to the installed asterisk
        // fallback instead of loading its real glyph.
        if ((font.Flags & ImFontFlags.NoLoadGlyphs) != 0)
        {
            if (baked.FallbackGlyphIndex == -1 && !baked.LoadNoFallback)
                SetupFallback(baked);
            return -1;
        }

        if (font.Sources.Count == 0)
            return -1;

        // Canonical copies the requested codepoint, applies the font's remap pairs, and only then runs
        // the ellipsis hook and the source scan
        // keeps the requested codepoint while its outline comes from the remap target,
        // so the baked lookup tables remain keyed by what the caller asked for rather than by the target.
        uint sourceCodepoint = codepoint;
        codepoint = font.RemapCodepoint(codepoint, out int srcFontIdx, out int srcGlyphIdx);

        if (codepoint != uint.MaxValue && codepoint > int.MaxValue)
            return NotFound(baked, codepoint);

        for (int sourceIndex = 0; sourceIndex < font.Sources.Count; sourceIndex++)
        {
            ImFontConfig source = font.Sources[sourceIndex];

            int glyphIndex;
            ImStbTrueTypeFont? stbFont;
            if (codepoint != uint.MaxValue)
            {
                // Canonical skips a source whose exclusion ranges claim the codepoint, leaving it to a
                // later source or to the fallback.
                if (!AcceptCodepointForSource(source, codepoint))
                    continue;

                EnsureStbFont(font, source);
                stbFont = source.StbFont;
                if (stbFont == null)
                    continue;

                glyphIndex = stbFont.FindGlyphIndex((int)codepoint);
            }
            else
            {
                if (sourceIndex != srcFontIdx)
                    continue;
                EnsureStbFont(font, source);
                stbFont = source.StbFont;
                if (stbFont == null)
                    continue;

                glyphIndex = srcGlyphIdx;
            }

            if (glyphIndex == 0)
                continue;

            // Glyph advances include ExtraSizeScale (folded into StbScaleFactor); ascent and descent
            // exclude it (see InitBakedMetrics).
            float scaleForLayout = source.StbScaleFactor * baked.Size;
            float rasterizerDensity = source.RasterizerDensity * baked.RasterizerDensity;
            if (rasterizerDensity <= 0.0f)
                rasterizerDensity = 1.0f;

            int oversampleH = source.OversampleH != 0
                ? source.OversampleH
                : (baked.Size * rasterizerDensity > 36.0f || source.PixelSnapH) ? 1 : 2;
            int oversampleV = source.OversampleV != 0 ? source.OversampleV : 1;

            float scaleRasterX = source.StbScaleFactor * baked.Size * rasterizerDensity * oversampleH;
            float scaleRasterY = source.StbScaleFactor * baked.Size * rasterizerDensity * oversampleV;

            stbFont.GetGlyphBitmapBoxSubpixel(glyphIndex, scaleRasterX, scaleRasterY, 0.0f, 0.0f,
                out int x0, out int y0, out int x1, out int y1);
            stbFont.GetGlyphHMetrics(glyphIndex, out int advance, out _);

            if (onlyAdvanceX)
            {
                SetAdvanceOnly(baked, source, sourceCodepoint, advance * scaleForLayout);
                return -1;
            }

            bool visible = x0 != x1 && y0 != y1;
            var glyph = new ImFontGlyph
            {
                // Canonical records the requested codepoint on the glyph it stores; the outline, box and
                // advance above came from the remap target
                Codepoint = sourceCodepoint,
                AdvanceX = advance * scaleForLayout,
                Visible = visible,
                PackId = RectIdInvalid,
                // Canonical records which source supplied the glyph
                SourceIdx = (uint)sourceIndex,
            };

            int width = x1 - x0;
            int height = y1 - y0;
            if (visible && width > 0 && height > 0)
            {
                int rectId = PackAddRect(width, height, RectIdInvalid);
                if (rectId == RectIdInvalid)
                {
                    // The texture could not grow enough to hold the glyph; report it as missing rather
                    // than registering a glyph with no rectangle.
                    return NotFound(baked, codepoint);
                }

                ImTextureRect rect = GetRect(rectId);
                var bitmap = new byte[width * height];
                stbFont.MakeGlyphBitmapSubpixelPrefilter(bitmap, width, height, width,
                    scaleRasterX, scaleRasterY, 0.0f, 0.0f, oversampleH, oversampleV,
                    out float subX, out float subY, glyphIndex);

                float referenceSize = font.Sources[0].SizePixels;
                float offsetsScale = referenceSize > 0.0f ? baked.Size / referenceSize : 1.0f;
                float fontOffX = MathF.Floor(source.GlyphOffset.X * offsetsScale + 0.5f) + subX;
                float fontOffY = MathF.Floor(source.GlyphOffset.Y * offsetsScale + 0.5f) + subY
                    + MathF.Round(baked.Ascent);
                float recipH = 1.0f / (oversampleH * rasterizerDensity);
                float recipV = 1.0f / (oversampleV * rasterizerDensity);

                glyph.X0 = x0 * recipH + fontOffX;
                glyph.Y0 = y0 * recipV + fontOffY;
                glyph.X1 = (x0 + width) * recipH + fontOffX;
                glyph.Y1 = (y0 + height) * recipV + fontOffY;
                glyph.PackId = rectId;

                WriteGlyphBitmap(rect, bitmap, width, height, source);
            }

            return AddGlyph(baked, source, glyph);
        }

        // Canonical synthesizes the ellipsis from the period only when no source supplies the
        // codepoint and the font still requests automatic baking
        if (codepoint == font.EllipsisChar && font.EllipsisAutoBake)
        {
            int synthesized = SetupBakedEllipsis(baked);
            if (synthesized >= 0)
                return onlyAdvanceX ? -1 : synthesized;
        }

        return NotFound(baked, codepoint);
    }

    /// <summary>
    /// Records an advance-only glyph entry. Mirrors canonical
    /// <c>ImFontAtlasBakedAddFontGlyphAdvancedX</c>.
    /// </summary>
    /// <param name="baked">Baked set receiving the advance.</param>
    /// <param name="source">Source supplying clamp, snap and spacing settings.</param>
    /// <param name="codepoint">Codepoint being measured.</param>
    /// <param name="advanceX">Unclamped advance in pixels.</param>
    private static void SetAdvanceOnly(ImFontBaked baked, ImFontConfig source, uint codepoint, float advanceX)
    {
        float referenceSize = baked.OwnerFont?.Sources.Count > 0 ? baked.OwnerFont.Sources[0].SizePixels : 0.0f;
        float offsetsScale = referenceSize > 0.0f ? baked.Size / referenceSize : 1.0f;
        advanceX = Math.Clamp(advanceX,
            source.GlyphMinAdvanceX * offsetsScale, source.GlyphMaxAdvanceX * offsetsScale);
        if (source.PixelSnapH)
            advanceX = MathF.Round(advanceX);
        advanceX += source.GlyphExtraAdvanceX;

        baked.GrowIndex((int)codepoint + 1);
        baked.IndexAdvanceX[codepoint] = advanceX;
    }

    /// <summary>
    /// Loads the advance for a lazily requested codepoint, deferring rasterization for very large
    /// sizes. Mirrors canonical <c>ImFontBaked_BuildLoadGlyphAdvanceX</c>
    /// </summary>
    /// <param name="baked">Baked set being measured.</param>
    /// <param name="codepoint">Codepoint to measure.</param>
    /// <returns>The advance width in pixels.</returns>
    internal float LoadGlyphAdvance(ImFontBaked baked, uint codepoint)
    {
        if (baked.Size >= AdvanceOnlySizeThreshold)
        {
            LoadGlyph(baked, codepoint, onlyAdvanceX: true);
            if (codepoint < (uint)baked.IndexAdvanceX.Length && baked.IndexAdvanceX[codepoint] >= 0.0f)
                return baked.IndexAdvanceX[codepoint];
            return baked.FallbackAdvanceX;
        }

        int index = LoadGlyph(baked, codepoint, onlyAdvanceX: false);
        return index >= 0 ? baked.Glyphs[index].AdvanceX : baked.FallbackAdvanceX;
    }

    /// <summary>
    /// Records a codepoint the source font does not contain, installing the fallback first when
    /// permitted. Mirrors canonical <c>ImFontBaked_BuildLoadGlyph</c> miss handling
    /// </summary>
    /// <param name="baked">Baked set being queried.</param>
    /// <param name="codepoint">Missing codepoint.</param>
    /// <returns>Always -1.</returns>
    private int NotFound(ImFontBaked baked, uint codepoint)
    {
        if (baked.LoadNoFallback)
            return -1;

        if (baked.FallbackGlyphIndex == -1)
            SetupFallback(baked);

        baked.GrowIndex((int)codepoint + 1);
        baked.IndexAdvanceX[codepoint] = baked.FallbackAdvanceX;
        baked.IndexLookup[codepoint] = ImFontBaked.GlyphIndexNotFound;
        return -1;
    }

    /// <summary>
    /// Installs the fallback glyph for a baked set, synthesizing one when the font lacks a usable
    /// fallback character. Mirrors canonical <c>ImFontAtlasBuildSetupFontBakedFallback</c>
    /// </summary>
    /// <param name="baked">Baked set to configure.</param>
    private void SetupFallback(ImFontBaked baked)
    {
        if (baked.FallbackGlyphIndex != -1)
            return;
        ImFont? font = baked.OwnerFont;
        if (font == null)
            return;

        int fallbackIndex = -1;
        if (font.FallbackChar != 0)
        {
            ImFontGlyph? fallback = baked.FindGlyphNoFallback(font.FallbackChar);
            if (fallback.HasValue && font.FallbackChar < (uint)baked.IndexLookup.Length)
                fallbackIndex = baked.IndexLookup[font.FallbackChar];
        }

        if (fallbackIndex < 0)
        {
            ImFontGlyph? space = baked.FindGlyphNoFallback(' ');
            var synthetic = new ImFontGlyph
            {
                Codepoint = 0,
                AdvanceX = space.HasValue ? space.Value.AdvanceX : MathF.Round(baked.Size * 0.40f),
                PackId = RectIdInvalid,
            };
            fallbackIndex = AddGlyph(baked, null, synthetic);
        }

        baked.FallbackGlyphIndex = fallbackIndex;
        baked.FallbackAdvanceX = baked.Glyphs[fallbackIndex].AdvanceX;
    }

    /// <summary>
    /// Synthesizes the ellipsis glyph from three copies of the period glyph when the loaded face has
    /// no ellipsis character. Ports canonical <c>ImFontAtlasBuildSetupFontBakedEllipsis</c>,
    /// which canonical invokes from the glyph load path. The synthesized glyph packs a new rectangle three periods wide
    /// with one pixel of spacing, copies the period pixels into it, and queues the texture update.
    /// </summary>
    /// <param name="baked">Baked set receiving the synthesized glyph.</param>
    /// <returns>The index of the synthesized glyph, or -1 when no period glyph is available or the
    /// combined rectangle cannot be packed.</returns>
    /// <remarks>
    /// The period rectangle is re-resolved after the combined rectangle is added because packing can
    /// repack the atlas and move the period, matching canonical's re-fetch. The
    /// copied pixels are not post-processed again: canonical notes that the period is already
    /// post-processed and a second multiply would damage it.
    /// </remarks>
    private int SetupBakedEllipsis(ImFontBaked baked)
    {
        ImFont? font = baked.OwnerFont;
        if (font == null || font.EllipsisChar == 0)
            return -1;

        // A font whose ellipsis codepoint is itself a period cannot synthesize from that glyph
        // without recursing into this routine; such a configuration resolves through the normal
        // source path instead.
        if (font.EllipsisChar == '.' || font.EllipsisChar == 0xFF0E)
            return -1;

        ImFontGlyph? dotGlyph = baked.FindGlyphNoFallback('.');
        if (dotGlyph == null)
            dotGlyph = baked.FindGlyphNoFallback(0xFF0E);
        if (dotGlyph == null || dotGlyph.Value.PackId == RectIdInvalid)
            return -1;

        int dotRectId = dotGlyph.Value.PackId;
        if (!TryGetRect(dotRectId, out ImTextureRect dotRect))
            return -1;

        const int DotSpacing = 1;
        float dotStep = (dotGlyph.Value.X1 - dotGlyph.Value.X0) + DotSpacing;
        int packId = PackAddRect(dotRect.W * 3 + DotSpacing * 2, dotRect.H, RectIdInvalid);
        if (packId == RectIdInvalid || !TryGetRect(packId, out ImTextureRect ellipsisRect))
            return -1;

        var glyph = new ImFontGlyph
        {
            Codepoint = font.EllipsisChar,
            // Canonical widens the advance past the last dot so an elided string keeps a visible
            // trailing gap.
            AdvanceX = MathF.Max(dotGlyph.Value.AdvanceX, dotGlyph.Value.X0 + dotStep * 3.0f - DotSpacing),
            X0 = dotGlyph.Value.X0,
            Y0 = dotGlyph.Value.Y0,
            X1 = dotGlyph.Value.X0 + dotStep * 3.0f - DotSpacing,
            Y1 = dotGlyph.Value.Y1,
            Visible = true,
            PackId = packId,
        };

        int glyphIndex = AddGlyph(baked, null, glyph);

        if (TexData != null
            && TryGetRect(dotRectId, out dotRect)
            && TryGetRect(packId, out ellipsisRect))
        {
            for (int n = 0; n < 3; n++)
            {
                CopyTextureBlock(TexData, dotRect.X, dotRect.Y, TexData,
                    ellipsisRect.X + (dotRect.W + DotSpacing) * n, ellipsisRect.Y, dotRect.W, dotRect.H);
            }
            TexData.QueueUpload(ellipsisRect.X, ellipsisRect.Y, ellipsisRect.W, ellipsisRect.H);
            TexIsBuilt = false;
        }

        return glyphIndex;
    }

    /// <summary>
    /// Appends a glyph to a baked set, assigns its UVs from its packed rectangle, applies advance
    /// clamping and pixel snapping, and updates the lookup tables. Ports
    /// <c>ImFontAtlasBakedAddFontGlyph</c>.
    /// </summary>
    /// <param name="baked">Baked set receiving the glyph.</param>
    /// <param name="source">Source supplying clamp, snap and spacing settings, or null for synthetic glyphs.</param>
    /// <param name="glyph">Glyph to add.</param>
    /// <returns>The index of the new glyph in <see cref="ImFontBaked.Glyphs"/>.</returns>
    /// <remarks>
    /// The advance is clamped to the source bounds and, when <see cref="ImFontConfig.PixelSnapH"/>
    /// is set, rounded to a whole pixel after any recentering, matching canonical order
    /// </remarks>
    private int AddGlyph(ImFontBaked baked, ImFontConfig? source, ImFontGlyph glyph)
    {
        int index = baked.Glyphs.Count;
        baked.Glyphs.Add(glyph);
        ImFontGlyph stored = baked.Glyphs[index];

        if (stored.PackId != RectIdInvalid && TryGetRect(stored.PackId, out ImTextureRect rect))
        {
            stored.U0 = rect.X * TexUvScale.X;
            stored.V0 = rect.Y * TexUvScale.Y;
            stored.U1 = (rect.X + rect.W) * TexUvScale.X;
            stored.V1 = (rect.Y + rect.H) * TexUvScale.Y;
            baked.MetricsTotalSurface += rect.W * rect.H;
        }

        if (source != null)
        {
            float referenceSize = source.SizePixels;
            if (baked.OwnerFont != null && baked.OwnerFont.Sources.Count > 0)
                referenceSize = baked.OwnerFont.Sources[0].SizePixels;
            float offsetsScale = referenceSize > 0.0f ? baked.Size / referenceSize : 1.0f;

            float advanceX = Math.Clamp(stored.AdvanceX,
                source.GlyphMinAdvanceX * offsetsScale, source.GlyphMaxAdvanceX * offsetsScale);
            if (advanceX != stored.AdvanceX)
            {
                // Recenter the glyph box when the advance was widened or narrowed.
                float charOffX = source.PixelSnapH
                    ? MathF.Truncate((advanceX - stored.AdvanceX) * 0.5f)
                    : (advanceX - stored.AdvanceX) * 0.5f;
                stored.X0 += charOffX;
                stored.X1 += charOffX;
            }

            // Snap to a whole pixel after recentering, exactly as canonical does for sources that
            // request it.
            if (source.PixelSnapH)
                advanceX = MathF.Round(advanceX);

            stored.AdvanceX = advanceX + source.GlyphExtraAdvanceX;
        }

        baked.Glyphs[index] = stored;

        int codepoint = (int)stored.Codepoint;
        baked.GrowIndex(codepoint + 1);
        baked.IndexAdvanceX[codepoint] = stored.AdvanceX;
        baked.IndexLookup[codepoint] = index;

        // Record that the 8K codepoint block containing this glyph now has a baked glyph, so
        // ImFont.IsGlyphRangeUnused can skip ranges no source needs
        if (baked.OwnerFont is ImFont ownerFont)
        {
            int page = codepoint / 8192;
            if ((page >> 3) < ownerFont.Used8kPagesMap.Length)
                ownerFont.Used8kPagesMap[page >> 3] |= (byte)(1 << (page & 7));
        }
        return index;
    }

    /// <summary>
    /// Writes a freshly rasterized coverage bitmap into the current texture, applies the source's
    /// rasterizer multiply, and queues the changed rectangle for the backend. Mirrors
    /// <c>ImFontAtlasBakedSetFontGlyphBitmap</c>.
    /// plus the Alpha8-to-RGBA32 conversion it performs.
    /// </summary>
    /// <param name="rect">Destination rectangle inside the texture.</param>
    /// <param name="bitmap">Single-channel coverage bitmap, one byte per pixel.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="source">Source whose <see cref="ImFontConfig.RasterizerMultiply"/> is applied.</param>
    private void WriteGlyphBitmap(ImTextureRect rect, byte[] bitmap, int width, int height, ImFontConfig source)
    {
        ImTextureData? tex = TexData;
        if (tex?.Pixels == null)
            return;

        int rowStride = TexWidth * tex.BytesPerPixel;
        for (int row = 0; row < height; row++)
        {
            int destination = (rect.Y + row) * rowStride + rect.X * tex.BytesPerPixel;
            int sourceRow = row * width;
            for (int col = 0; col < width; col++)
            {
                // The buffer is pre-filled white plus transparent alpha, so only alpha changes.
                tex.Pixels[destination + col * tex.BytesPerPixel + 3] = bitmap[sourceRow + col];
            }
        }

        // Canonical post-processes the converted block before queueing the upload
        PostProcessTextureBlock(rect, source.RasterizerMultiply);

        tex.QueueUpload(rect.X, rect.Y, rect.W, rect.H);
        TexIsBuilt = false;
    }

    /// <summary>
    /// Applies the canonical post-processing multiply to a texture block that was just written.
    /// Ports <c>ImFontAtlasTextureBlockPostProcess</c>
    ///., which is a no-op at the default factor of
    /// 1.0. The atlas currently allocates its texture in RGBA32, so the alpha channel is the one
    /// scaled; the Alpha8 branch mirrors the canonical single-channel format for textures created in
    /// that format.
    /// </summary>
    /// <param name="rect">Rectangle that was just written.</param>
    /// <param name="multiplyFactor">Rasterizer brightness multiplier.</param>
    private void PostProcessTextureBlock(ImTextureRect rect, float multiplyFactor)
    {
        if (multiplyFactor == 1.0f)
            return;

        ImTextureData? tex = TexData;
        if (tex?.Pixels == null)
            return;

        if (tex.Format == ImTextureFormat.Alpha8)
        {
            for (int row = 0; row < rect.H; row++)
            {
                int rowStart = (rect.Y + row) * tex.GetPitch() + rect.X;
                for (int col = 0; col < rect.W; col++)
                    tex.Pixels[rowStart + col] = MultiplyCoverage(tex.Pixels[rowStart + col], multiplyFactor);
            }
        }
        else if (tex.Format == ImTextureFormat.RGBA32)
        {
            for (int row = 0; row < rect.H; row++)
            {
                int rowStart = (rect.Y + row) * tex.GetPitch() + rect.X * tex.BytesPerPixel;
                for (int col = 0; col < rect.W; col++)
                {
                    int alphaOffset = rowStart + col * tex.BytesPerPixel + 3;
                    tex.Pixels[alphaOffset] = MultiplyCoverage(tex.Pixels[alphaOffset], multiplyFactor);
                }
            }
        }
    }

    /// <summary>
    /// Scales one coverage byte and clamps the result to the byte range. Mirrors
    /// <c>ImFontAtlasTextureBlockPostProcessMultiply</c>
    ///.; canonical leaves a negative factor
    /// undefined, and the managed port clamps it to zero for a well-defined result.
    /// </summary>
    /// <param name="coverage">Coverage byte being scaled.</param>
    /// <param name="multiplyFactor">Brightness multiplier.</param>
    /// <returns>The scaled coverage, clamped to 0 through 255.</returns>
    private static byte MultiplyCoverage(byte coverage, float multiplyFactor)
    {
        int scaled = (int)(coverage * multiplyFactor);
        if (scaled < 0)
            scaled = 0;
        else if (scaled > 255)
            scaled = 255;
        return (byte)scaled;
    }

    /// <summary>
    /// Packs a caller rectangle using the canonical rectangle API. Mirrors
    /// <c>ImFontAtlas::AddCustomRect</c>. and is
    /// used for the white texel block and the baked line region as well as caller content.
    /// </summary>
    /// <param name="width">Rectangle width in pixels; must be positive.</param>
    /// <param name="height">Rectangle height in pixels; must be positive.</param>
    /// <returns>The rectangle identifier, or -1 when it could not be packed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is not positive.</exception>
    public int AddCustomRect(int width, int height)
    {
        if (width <= 0 || width > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(height));

        EnsureBuilder();
        int id = PackAddRect(width, height, RectIdInvalid);
        if (id != RectIdInvalid && TexData != null && TryGetRect(id, out ImTextureRect rect))
            TexData.QueueUpload(rect.X, rect.Y, rect.W, rect.H);
        return id;
    }

    /// <summary>
    /// Resolves a caller rectangle's position, size and UV coordinates for the current texture.
    /// Mirrors <c>ImFontAtlas::GetCustomRect</c>
    /// </summary>
    /// <param name="id">Rectangle identifier returned by <see cref="AddCustomRect"/>.</param>
    /// <param name="rect">Receives the packed rectangle when the identifier is still live.</param>
    /// <returns><see langword="true"/> when the identifier names a live rectangle.</returns>
    /// <remarks>
    /// Canonical computes the UVs from the atlas's current texture scale and warns that they are only
    /// valid until the next repack; the same caveat applies here. The atlas must have been built, so
    /// <see cref="TexUvScale"/> is meaningful; an unbuilt atlas yields zero UVs, which canonical would
    /// reject with an assertion because it requires a positive texture size.
    /// </remarks>
    public bool GetCustomRect(int id, out ImFontAtlasRect rect)
    {
        rect = default;
        if (!TryGetRect(id, out ImTextureRect textureRect))
            return false;

        rect.X = textureRect.X;
        rect.Y = textureRect.Y;
        rect.W = textureRect.W;
        rect.H = textureRect.H;
        rect.Uv0 = new ImVec2(textureRect.X * TexUvScale.X, textureRect.Y * TexUvScale.Y);
        rect.Uv1 = new ImVec2(
            (textureRect.X + textureRect.W) * TexUvScale.X,
            (textureRect.Y + textureRect.H) * TexUvScale.Y);
        return true;
    }

    /// <summary>
    /// Releases a caller rectangle. Mirrors <c>ImFontAtlas::RemoveCustomRect</c>
    /// </summary>
    /// <param name="id">Rectangle identifier to release; unknown identifiers are ignored.</param>
    public void RemoveCustomRect(int id) => PackDiscardRect(id);

    /// <summary>
    /// Packs a custom glyph rectangle for a font at its legacy size and adds the glyph to that
    /// size's baked set. Mirrors canonical <c>ImFontAtlas::AddCustomRectFontGlyph</c>
    /// </summary>
    /// <param name="font">Font that receives the glyph.</param>
    /// <param name="codepoint">Codepoint the glyph represents.</param>
    /// <param name="width">Glyph rectangle width in pixels.</param>
    /// <param name="height">Glyph rectangle height in pixels.</param>
    /// <param name="advanceX">Horizontal advance applied after the glyph.</param>
    /// <param name="offset">Offset of the glyph quad relative to the cursor.</param>
    /// <returns>The rectangle identifier, or -1 when the rectangle could not be packed.</returns>
    public int AddCustomRectFontGlyph(
        ImFont font, uint codepoint, int width, int height, float advanceX, ImVec2 offset)
        => AddCustomRectFontGlyphForSize(font, font.LegacySize, codepoint, width, height, advanceX, offset);

    /// <summary>
    /// Packs a custom glyph rectangle for a font at an explicit size and adds the glyph to that
    /// size's baked set. Mirrors canonical <c>ImFontAtlas::AddCustomRectFontGlyphForSize</c>
    /// </summary>
    /// <param name="font">Font that receives the glyph.</param>
    /// <param name="fontSize">Baked size the glyph belongs to.</param>
    /// <param name="codepoint">Codepoint the glyph represents.</param>
    /// <param name="width">Glyph rectangle width in pixels; positive and at most 0xFFFF.</param>
    /// <param name="height">Glyph rectangle height in pixels; positive and at most 0xFFFF.</param>
    /// <param name="advanceX">Horizontal advance applied after the glyph.</param>
    /// <param name="offset">Offset of the glyph quad relative to the cursor.</param>
    /// <returns>The rectangle identifier, or -1 when the rectangle could not be packed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="font"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is not positive or exceeds 0xFFFF.</exception>
    /// <remarks>
    /// Canonical marks the glyph visible and colored so it ignores tinting, and applies the source's
    /// advance clamping through <c>ImFontAtlasBakedAddFontGlyph</c>; this port does the same by
    /// routing the new glyph through <see cref="AddGlyph"/>. A glyph already loaded for the codepoint
    /// is discarded first so its packed rectangle is released before the replacement is added.
    /// </remarks>
    public int AddCustomRectFontGlyphForSize(
        ImFont font, float fontSize, uint codepoint, int width, int height, float advanceX, ImVec2 offset)
    {
        if (font == null)
            throw new ArgumentNullException(nameof(font));
        if (width <= 0 || width > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(height));

        ImFontBaked baked = font.GetFontBaked(fontSize);

        EnsureBuilder();
        int rectId = PackAddRect(width, height, RectIdInvalid);
        if (rectId == RectIdInvalid)
            return RectIdInvalid;
        if (!TryGetRect(rectId, out ImTextureRect rect))
            return RectIdInvalid;

        // Queue the fresh rectangle so a backend that consumes incremental updates uploads it.
        TexData?.QueueUpload(rect.X, rect.Y, rect.W, rect.H);

        if (baked.IsGlyphLoaded(codepoint))
            DiscardFontGlyph(font, baked, codepoint);

        var glyph = new ImFontGlyph
        {
            Codepoint = codepoint,
            AdvanceX = advanceX,
            X0 = offset.X,
            Y0 = offset.Y,
            X1 = offset.X + rect.W,
            Y1 = offset.Y + rect.H,
            Visible = true,
            Colored = true,
            PackId = rectId,
        };
        AddGlyph(baked, font.Sources.Count > 0 ? font.Sources[0] : null, glyph);
        return rectId;
    }

    /// <summary>
    /// Discards one loaded glyph from a baked set, returning its packed rectangle to the packer and
    /// marking the lookup entry unused. Mirrors canonical <c>ImFontAtlasBakedDiscardFontGlyph</c>
    /// </summary>
    /// <param name="font">Font that owns the baked set; retained for parity with the canonical signature.</param>
    /// <param name="baked">Baked set containing the glyph.</param>
    /// <param name="codepoint">Codepoint whose glyph is discarded.</param>
    /// <remarks>
    /// Canonical asserts that the codepoint is neither the fallback nor the ellipsis character
    /// because replacing those would leave the font's special-glyph bookkeeping inconsistent. The
    /// managed port returns without discarding in that case rather than aborting, which keeps a
    /// caller mistake from tearing down the process while leaving the special glyph intact.
    /// </remarks>
    private void DiscardFontGlyph(ImFont font, ImFontBaked baked, uint codepoint)
    {
        if (font.FallbackChar == codepoint || font.EllipsisChar == codepoint)
            return;

        if (codepoint < (uint)baked.IndexLookup.Length)
        {
            int index = baked.IndexLookup[codepoint];
            if (index >= 0 && index < baked.Glyphs.Count)
            {
                ImFontGlyph glyph = baked.Glyphs[index];
                if (glyph.PackId != RectIdInvalid)
                {
                    PackDiscardRect(glyph.PackId);
                    glyph.PackId = RectIdInvalid;
                    baked.Glyphs[index] = glyph;
                }
            }

            baked.IndexLookup[codepoint] = ImFontBaked.GlyphIndexUnused;
            baked.IndexAdvanceX[codepoint] = baked.FallbackAdvanceX;
        }
    }


    /// <summary>
    /// Discards the idle baked sets of one font, returning each set's packed rectangles to the
    /// packer. A set is idle when its <see cref="ImFontBaked.LastUsedFrame"/> plus
    /// <paramref name="unusedFrames"/> has fallen behind <see cref="FrameCount"/>. Passing zero
    /// discards every set owned by the font regardless of age, which the merge-mode path needs
    /// because a merged source changes the glyph output of sets that may still be in use. Mirrors
    /// canonical <c>ImFontAtlasFontDiscardBakes</c>
    /// </summary>
    /// <param name="font">Font whose idle baked sets are released.</param>
    /// <param name="unusedFrames">
    /// Number of frames a set must have gone unused before it is released; zero releases every set
    /// owned by <paramref name="font"/>.
    /// </param>
    internal void FontDiscardBakes(ImFont font, int unusedFrames)
    {
        for (int bakedIndex = font.BakedList.Count - 1; bakedIndex >= 0; bakedIndex--)
        {
            ImFontBaked baked = font.BakedList[bakedIndex];
            if (baked.LastUsedFrame + unusedFrames > FrameCount)
                continue;
            if (!ReferenceEquals(baked.OwnerFont, font) || baked.WantDestroy)
                continue;
            DiscardBaked(font, baked);
        }
    }

    /// <summary>
    /// Discards idle baked sets across every font and returns their packed rectangles to the packer,
    /// so the freed surface can be reassigned by a later repack or packing retry. Fonts flagged with
    /// <see cref="ImFontFlags.LockBakedSizes"/> are exempt so a caller that locked a font to fixed
    /// sizes keeps every bake alive. Mirrors canonical <c>ImFontAtlasBuildDiscardBakes</c>
    /// </summary>
    /// <param name="unusedFrames">
    /// Number of frames a set must have gone unused before it is released; zero releases every set in
    /// every unlocked font.
    /// </param>
    internal void BuildDiscardBakes(int unusedFrames)
    {
        for (int fontIndex = 0; fontIndex < Fonts.Count; fontIndex++)
        {
            ImFont font = Fonts[fontIndex];
            if ((font.Flags & ImFontFlags.LockBakedSizes) != 0)
                continue;

            for (int bakedIndex = font.BakedList.Count - 1; bakedIndex >= 0; bakedIndex--)
            {
                ImFontBaked baked = font.BakedList[bakedIndex];
                if (baked.LastUsedFrame + unusedFrames > FrameCount)
                    continue;
                if (baked.WantDestroy)
                    continue;
                DiscardBaked(font, baked);
            }
        }
    }

    /// <summary>
    /// Releases one baked glyph set: returns each live glyph rectangle to the packer so its surface
    /// can be reassigned, then removes the set from its font. Mirrors canonical
    /// <c>ImFontAtlasBakedDiscard</c>. Loader-owned
    /// per-source scratch data needs no release here because this port caches the TrueType parser once
    /// per source and shares it across every baked size.
    /// </summary>
    /// <param name="font">Font that owns the set.</param>
    /// <param name="baked">Baked set to release.</param>
    private void DiscardBaked(ImFont font, ImFontBaked baked)
    {
        for (int glyphIndex = 0; glyphIndex < baked.Glyphs.Count; glyphIndex++)
        {
            int packId = baked.Glyphs[glyphIndex].PackId;
            if (packId != RectIdInvalid)
                PackDiscardRect(packId);
        }

        font.DiscardBaked(baked);
    }

    /// <summary>
    /// Initializes (or re-initializes) the skyline packer for the current texture size and resets
    /// the packing statistics. Mirrors canonical <c>ImFontAtlasPackInit</c>
    /// </summary>
    private void PackInit()
    {
        int nodeCount = Math.Max(1, TexWidth / 2);
        _packContext.InitTarget(TexWidth, TexHeight, nodeCount);
        _rectsPackedCount = 0;
        _rectsPackedSurface = 0;
        _maxRectSizeW = 0;
        _maxRectSizeH = 0;
        _maxRectBoundsW = 0;
        _maxRectBoundsH = 0;
    }

    /// <summary>
    /// Packs a rectangle with padding, growing or repacking the texture when it does not fit.
    /// Mirrors canonical <c>ImFontAtlasPackAddRect</c>
    /// </summary>
    /// <param name="w">Rectangle width in pixels.</param>
    /// <param name="h">Rectangle height in pixels.</param>
    /// <param name="overwriteEntryIndex">
    /// Index of an existing rectangle entry to reuse during repacking, or <see cref="RectIdInvalid"/>
    /// to allocate a fresh identifier.
    /// </param>
    /// <returns>The rectangle identifier, or -1 when it could not be packed.</returns>
    private int PackAddRect(int w, int h, int overwriteEntryIndex)
    {
        int padding = TexGlyphPadding;
        _maxRectSizeW = Math.Max(_maxRectSizeW, w);
        _maxRectSizeH = Math.Max(_maxRectSizeH, h);

        var request = new ImStbrpRect[1];
        for (int attemptsRemaining = 3; attemptsRemaining >= 0; attemptsRemaining--)
        {
            request[0].W = w + padding;
            request[0].H = h + padding;
            request[0].X = 0;
            request[0].Y = 0;
            request[0].WasPacked = 0;

            _packContext.PackRects(request, 1);
            if (request[0].WasPacked != 0)
                break;

            if (attemptsRemaining == 0 || _lockDisableResize)
                return RectIdInvalid;

            // Rare path: make room and retry against the new packer target.
            TextureMakeSpace();
        }

        int x = request[0].X;
        int y = request[0].Y;
        _maxRectBoundsW = Math.Max(_maxRectBoundsW, x + w + padding);
        _maxRectBoundsH = Math.Max(_maxRectBoundsH, y + h + padding);
        _rectsPackedCount++;
        _rectsPackedSurface += (w + padding) * (h + padding);

        _rects.Add(new ImTextureRect { X = (ushort)x, Y = (ushort)y, W = (ushort)w, H = (ushort)h });

        if (overwriteEntryIndex != RectIdInvalid)
            return ReuseRectEntry(overwriteEntryIndex);
        return AllocRectEntry(_rects.Count - 1);
    }

    /// <summary>
    /// Allocates a rectangle entry from the free list or by appending. Mirrors canonical
    /// <c>ImFontAtlasPackAllocRectEntry</c>.
    /// </summary>
    /// <param name="rectIndex">Index of the packed rectangle in <see cref="_rects"/>.</param>
    /// <returns>A fresh identifier for the entry.</returns>
    private int AllocRectEntry(int rectIndex)
    {
        int entryIndex;
        ImFontAtlasRectEntry entry;
        if (_rectsIndexFreeListStart < 0)
        {
            _rectEntries.Add(default);
            entryIndex = _rectEntries.Count - 1;
            entry = _rectEntries[entryIndex];
        }
        else
        {
            entryIndex = _rectsIndexFreeListStart;
            entry = _rectEntries[entryIndex];
            _rectsIndexFreeListStart = entry.TargetIndex;
        }

        entry.TargetIndex = rectIndex;
        entry.IsUsed = true;
        _rectEntries[entryIndex] = entry;
        return MakeRectId(entryIndex, entry.Generation);
    }

    /// <summary>
    /// Reuses an existing rectangle entry during repacking, preserving its identifier and
    /// generation. Mirrors canonical <c>ImFontAtlasPackReuseRectEntry</c>
    /// </summary>
    /// <param name="entryIndex">Index of the entry to overwrite.</param>
    /// <returns>The existing identifier with its updated target.</returns>
    private int ReuseRectEntry(int entryIndex)
    {
        ImFontAtlasRectEntry entry = _rectEntries[entryIndex];
        entry.TargetIndex = _rects.Count - 1;
        _rectEntries[entryIndex] = entry;
        return MakeRectId(entryIndex, entry.Generation);
    }

    /// <summary>
    /// Releases a rectangle, clears its packed size so it is skipped during repacking, and returns
    /// its entry to the free list. Mirrors canonical <c>ImFontAtlasPackDiscardRect</c>
    /// </summary>
    /// <param name="id">Rectangle identifier to release.</param>
    private void PackDiscardRect(int id)
    {
        if (!TryGetRect(id, out ImTextureRect rect))
            return;

        int entryIndex = GetRectIdIndex(id);
        ImFontAtlasRectEntry entry = _rectEntries[entryIndex];
        int targetIndex = entry.TargetIndex;

        entry.IsUsed = false;
        entry.TargetIndex = _rectsIndexFreeListStart;
        entry.Generation++;
        if (entry.Generation == 0)
            entry.Generation = 1;
        _rectEntries[entryIndex] = entry;

        _rectsIndexFreeListStart = entryIndex;
        _rectsDiscardedCount++;
        _rectsDiscardedSurface += (rect.W + TexGlyphPadding) * (rect.H + TexGlyphPadding);

        // Leave a tombstone so the cleared rectangle is not packed again by a later repack.
        ImTextureRect cleared = _rects[targetIndex];
        cleared.W = 0;
        cleared.H = 0;
        _rects[targetIndex] = cleared;
    }

    /// <summary>
    /// Resolves a rectangle identifier that is known to be valid. Mirrors canonical
    /// <c>ImFontAtlasPackGetRect</c>.
    /// </summary>
    /// <param name="id">Rectangle identifier.</param>
    /// <returns>The packed rectangle.</returns>
    private ImTextureRect GetRect(int id)
    {
        int entryIndex = GetRectIdIndex(id);
        ImFontAtlasRectEntry entry = _rectEntries[entryIndex];
        return _rects[entry.TargetIndex];
    }

    /// <summary>
    /// Attempts to resolve a rectangle identifier, validating its index and generation. Mirrors
    /// canonical <c>ImFontAtlasPackGetRectSafe</c>
    /// </summary>
    /// <param name="id">Rectangle identifier.</param>
    /// <param name="rect">Receives the rectangle on success.</param>
    /// <returns><see langword="true"/> when the identifier names a live rectangle.</returns>
    private bool TryGetRect(int id, out ImTextureRect rect)
    {
        rect = default;
        if (id == RectIdInvalid)
            return false;

        int entryIndex = GetRectIdIndex(id);
        if (entryIndex < 0 || entryIndex >= _rectEntries.Count)
            return false;

        ImFontAtlasRectEntry entry = _rectEntries[entryIndex];
        if (!entry.IsUsed || entry.Generation != GetRectIdGeneration(id))
            return false;
        if (entry.TargetIndex < 0 || entry.TargetIndex >= _rects.Count)
            return false;

        rect = _rects[entry.TargetIndex];
        return true;
    }

    /// <summary>Builds a rectangle identifier from an entry index and generation counter.</summary>
    /// <param name="index">Rectangle-entry index.</param>
    /// <param name="generation">Entry generation counter.</param>
    /// <returns>The packed identifier.</returns>
    private static int MakeRectId(int index, int generation) => index | (generation << RectIdGenerationShift);

    /// <summary>Extracts the entry index from a rectangle identifier.</summary>
    /// <param name="id">Rectangle identifier.</param>
    /// <returns>The entry index.</returns>
    private static int GetRectIdIndex(int id) => id & RectIdIndexMask;

    /// <summary>Extracts the generation counter from a rectangle identifier.</summary>
    /// <param name="id">Rectangle identifier.</param>
    /// <returns>The generation counter.</returns>
    private static int GetRectIdGeneration(int id) => (id & RectIdGenerationMask) >> RectIdGenerationShift;

    /// <summary>
    /// Creates a fresh texture of the given size, releases any pin on the previous pixels, marks the
    /// previous texture for destruction, and rebinds the atlas UV scale. Mirrors canonical
    /// <c>ImFontAtlasTextureAdd</c> and <c>ImFontAtlasBuildSetTexture</c>.
    /// </summary>
    /// <param name="w">Texture width in pixels.</param>
    /// <param name="h">Texture height in pixels.</param>
    /// <returns>The new texture data.</returns>
    /// <remarks>
    /// Texture creation is one of the windows in which atlas contents cannot be modified, so
    /// <see cref="Locked"/> is held for the duration of the swap and restored to its previous value
    /// afterwards. Restoring rather than clearing keeps a nested creation inside <see cref="Build"/>
    /// or a repack under the single outer lock scope.
    /// </remarks>
    private ImTextureData CreateTexture(int w, int h)
    {
        bool wasLocked = Locked;
        Locked = true;
        try
        {
            ReleasePin();

            ImTextureData? old = TexData;
            var tex = new ImTextureData { UniqueID = TexNextUniqueID++ };
            if (old != null)
                old.WantDestroyNextFrame = true;

            tex.Create(TexDesiredFormat, w, h);
            FillWhiteTransparent(tex);

            TexData = tex;
            TexWidth = w;
            TexHeight = h;
            TexUvScale = new ImVec2(1.0f / w, 1.0f / h);
            TexID = IntPtr.Zero;
            TexIsBuilt = false;
            if (old != null)
                tex.UseColors = old.UseColors;
            return tex;
        }
        finally
        {
            Locked = wasLocked;
        }
    }

    /// <summary>
    /// Fills a freshly created atlas texture with the background the glyph and line writers draw
    /// coverage into. RGBA32 receives opaque white with zero alpha, so untextured geometry samples a
    /// white texel; Alpha8 receives zero coverage, matching canonical's zero-filled alpha texture.
    /// </summary>
    /// <param name="tex">Texture whose pixel buffer is filled.</param>
    private static void FillWhiteTransparent(ImTextureData tex)
    {
        byte[]? pixels = tex.Pixels;
        if (pixels == null)
            return;

        if (tex.BytesPerPixel == 1)
        {
            Array.Clear(pixels, 0, pixels.Length);
            return;
        }

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 0;
        }
    }

    /// <summary>
    /// Re-packs every live rectangle into a texture of the given size, copying pixels and patching
    /// all baked glyph UVs. Mirrors canonical <c>ImFontAtlasTextureRepack</c>
    ///. Falls back to growing the texture when a
    /// rectangle cannot fit.
    /// </summary>
    /// <param name="w">New texture width in pixels.</param>
    /// <param name="h">New texture height in pixels.</param>
    /// <remarks>
    /// A repack rewrites every rectangle position, so the atlas is <see cref="Locked"/> for its whole
    /// duration and the previous lock value is restored on exit; the nested texture creation keeps the
    /// outer lock. A caller that adds a font during the repack would otherwise see a half-moved atlas.
    /// </remarks>
    private void TextureRepack(int w, int h)
    {
        bool wasLocked = Locked;
        Locked = true;
        try
        {
            if (TexData == null)
            {
                CreateTexture(w, h);
                PackInit();
                BuildUpdateTexData();
                UpdateDrawListsSharedData();
                return;
            }

            _lockDisableResize = true;

            ImTextureData oldTex = TexData;
            var oldRects = new List<ImTextureRect>(_rects);
            var oldEntries = new List<ImFontAtlasRectEntry>(_rectEntries);

            CreateTexture(w, h);
            _rects.Clear();
            PackInit();

            for (int entryIndex = 0; entryIndex < _rectEntries.Count; entryIndex++)
            {
                ImFontAtlasRectEntry entry = _rectEntries[entryIndex];
                if (!entry.IsUsed)
                    continue;
                ImTextureRect oldRect = oldRects[entry.TargetIndex];
                if (oldRect.W == 0 && oldRect.H == 0)
                    continue;

                int newId = PackAddRect(oldRect.W, oldRect.H, entryIndex);
                if (newId == RectIdInvalid)
                {
                    // Undo and grow: the target size could not hold the live rectangles.
                    RestoreRepack(oldTex, oldRects, oldEntries);
                    _lockDisableResize = false;
                    TextureGrow();
                    return;
                }

                ImFontAtlasRectEntry moved = _rectEntries[entryIndex];
                ImTextureRect newRect = _rects[moved.TargetIndex];
                CopyTextureBlock(oldTex, oldRect.X, oldRect.Y, TexData, newRect.X, newRect.Y, newRect.W, newRect.H);
            }

            _rectsDiscardedCount = 0;
            _rectsDiscardedSurface = 0;

            PatchGlyphUvs();
            BuildUpdateTexData();
            UpdateDrawListsSharedData();
            _lockDisableResize = false;
        }
        finally
        {
            Locked = wasLocked;
        }
    }

    /// <summary>
    /// Adds a draw list to the set refreshed whenever this atlas repacks or re-derives its white
    /// texel and line regions. Mirrors canonical <c>ImFontAtlasAddDrawListSharedData</c>
    /// </summary>
    /// <param name="drawList">Draw list that reads this atlas; already-registered lists are ignored.</param>
    internal void RegisterDrawList(ImDrawList drawList)
    {
        if (!_drawLists.Contains(drawList))
            _drawLists.Add(drawList);
    }

    /// <summary>
    /// Pushes refreshed atlas UV tables into every registered draw list. Mirrors canonical
    /// <c>ImFontAtlasUpdateDrawListsSharedData</c>,
    /// called from the texture-repack and build-init paths.
    /// </summary>
    private void UpdateDrawListsSharedData()
    {
        for (int i = 0; i < _drawLists.Count; i++)
            _drawLists[i].RefreshAtlasTables(this);
    }

    /// <summary>
    /// Restores the atlas to the pre-repack texture, rectangles and entries after a failed repack.
    /// </summary>
    /// <param name="oldTex">Texture that was active before the repack attempt.</param>
    /// <param name="oldRects">Packed rectangles from before the attempt.</param>
    /// <param name="oldEntries">Rectangle identifier entries from before the attempt.</param>
    private void RestoreRepack(ImTextureData oldTex, List<ImTextureRect> oldRects, List<ImFontAtlasRectEntry> oldEntries)
    {
        TexData?.DestroyPixels();
        _rects.Clear();
        _rects.AddRange(oldRects);
        _rectEntries.Clear();
        _rectEntries.AddRange(oldEntries);

        oldTex.WantDestroyNextFrame = false;
        TexData = oldTex;
        TexWidth = oldTex.Width;
        TexHeight = oldTex.Height;
        TexUvScale = new ImVec2(1.0f / TexWidth, 1.0f / TexHeight);
        PackInit();
    }

    /// <summary>
    /// Grows the texture roughly towards a square and repacks into it. Mirrors canonical
    /// <c>ImFontAtlasTextureGrow</c>.
    /// </summary>
    private void TextureGrow()
    {
        int oldWidth = TexWidth;
        int oldHeight = TexHeight;

        // Grow height before width because width multiplies the packer's node count.
        int newWidth = oldHeight <= oldWidth ? oldWidth : oldWidth * 2;
        int newHeight = oldHeight <= oldWidth ? oldHeight * 2 : oldHeight;

        int padding = TexGlyphPadding;
        newWidth = Math.Max(newWidth, UpperPowerOfTwo(_maxRectSizeW + padding));
        newHeight = Math.Max(newHeight, UpperPowerOfTwo(_maxRectSizeH + padding));
        newWidth = Math.Clamp(newWidth, TexMinWidth, TexMaxWidth);
        newHeight = Math.Clamp(newHeight, TexMinHeight, TexMaxHeight);

        if (newWidth == oldWidth && newHeight == oldHeight)
            return;

        TextureRepack(newWidth, newHeight);
    }

    /// <summary>
    /// Makes room for a rectangle that did not fit: first returns the rectangles of idle baked sets
    /// to the packer, then grows the texture when little surface has been discarded, otherwise
    /// repacks at the same size. Mirrors canonical <c>ImFontAtlasTextureMakeSpace</c>
    ///., including its
    /// <c>ImFontAtlasBuildDiscardBakes(atlas, 2)</c> call, so a stale bake is reclaimed before a
    /// larger surface is allocated.
    /// </summary>
    private void TextureMakeSpace()
    {
        BuildDiscardBakes(2);

        if (_rectsDiscardedSurface < _rectsPackedSurface * 0.20f)
            TextureGrow();
        else
            TextureRepack(TexWidth, TexHeight);
    }

    /// <summary>
    /// Evicts idle baked sets and repacks the remaining rectangles into the smallest texture that
    /// still holds them. Mirrors canonical <c>ImFontAtlasTextureCompact</c>
    ///., whose
    /// <c>ImFontAtlasBuildDiscardBakes(atlas, 1)</c> call releases bakes that have been idle for a
    /// single frame.
    /// </summary>
    private void TextureCompact()
    {
        BuildDiscardBakes(1);

        int oldWidth = TexWidth;
        int oldHeight = TexHeight;
        (int newWidth, int newHeight) = TextureGetSizeEstimate();
        if (_rectsDiscardedCount == 0 && newWidth == oldWidth && newHeight == oldHeight)
            return;

        TextureRepack(newWidth, newHeight);
    }

    /// <summary>
    /// Estimates the smallest power-of-two texture that holds the live packed rectangles. Mirrors
    /// canonical <c>ImFontAtlasTextureGetSizeEstimate</c>
    ///. The estimate ignores discards, so it grows
    /// only to fit the largest single rectangle and the currently packed surface.
    /// </summary>
    /// <returns>The estimated texture width and height in pixels.</returns>
    private (int Width, int Height) TextureGetSizeEstimate()
    {
        int minWidth = UpperPowerOfTwo(TexMinWidth);
        int minHeight = UpperPowerOfTwo(TexMinHeight);
        if (TexData == null)
            return (minWidth, minHeight);

        minWidth = Math.Max(UpperPowerOfTwo(_maxRectSizeW), minWidth);
        minHeight = Math.Max(UpperPowerOfTwo(_maxRectSizeH), minHeight);
        int surfaceApprox = _rectsPackedSurface - _rectsDiscardedSurface;
        int surfaceSqrt = (int)MathF.Sqrt(surfaceApprox);

        int newWidth;
        int newHeight;
        if (minWidth >= minHeight)
        {
            newWidth = Math.Max(minWidth, UpperPowerOfTwo(surfaceSqrt));
            newHeight = Math.Max(minHeight, (surfaceApprox + newWidth - 1) / newWidth);
            if ((Flags & ImFontAtlasFlags.NoPowerOfTwoHeight) == 0)
                newHeight = UpperPowerOfTwo(newHeight);
        }
        else
        {
            newHeight = Math.Max(minHeight, UpperPowerOfTwo(surfaceSqrt));
            if ((Flags & ImFontAtlasFlags.NoPowerOfTwoHeight) == 0)
                newHeight = UpperPowerOfTwo(newHeight);
            newWidth = Math.Max(minWidth, (surfaceApprox + newHeight - 1) / newHeight);
        }

        return (newWidth, newHeight);
    }

    /// <summary>
    /// Reserves the white texel block, paints it, and refreshes <see cref="TexUvWhitePixel"/>.
    /// Mirrors the white-texel portion of canonical <c>ImFontAtlasBuildUpdateTexDataBasic</c>
    ///. The mouse-cursor sheet canonical packs
    /// alongside the white block is not ported because this port has no mouse-cursor rendering.
    /// </summary>
    private void BuildUpdateTexDataBasic()
    {
        bool added = false;
        if (_packIdWhiteRect == RectIdInvalid || !TryGetRect(_packIdWhiteRect, out _))
        {
            _packIdWhiteRect = AddCustomRect(WhiteRectSize, WhiteRectSize);
            added = true;
        }

        if (!TryGetRect(_packIdWhiteRect, out ImTextureRect rect))
            return;

        if (added && TexData?.Pixels != null)
        {
            // The block is a solid white square sampled by all untextured geometry. The alpha
            // channel is the last byte of an RGBA32 texel and the only byte of an Alpha8 texel.
            int alphaOffset = TexData.BytesPerPixel - 1;
            for (int y = 0; y < WhiteRectSize; y++)
            {
                for (int x = 0; x < WhiteRectSize; x++)
                    TexData.Pixels[((rect.Y + y) * TexWidth + rect.X + x) * TexData.BytesPerPixel + alphaOffset] = 255;
            }
            TexData.QueueUpload(rect.X, rect.Y, rect.W, rect.H);
        }

        // Canonical samples the center of the first white pixel.
        TexUvWhitePixel = new ImVec2((rect.X + 0.5f) * TexUvScale.X, (rect.Y + 0.5f) * TexUvScale.Y);
    }

    /// <summary>
    /// Reserves the baked line region, paints the triangular per-width rows, and refreshes
    /// <see cref="TexUvLines"/>. Mirrors canonical <c>ImFontAtlasBuildUpdateTexDataLines</c>
    /// </summary>
    private void BuildUpdateTexDataLines()
    {
        if ((Flags & ImFontAtlasFlags.NoBakedLines) != 0)
        {
            TexUvLines = Array.Empty<ImVec4>();
            return;
        }

        bool added = false;
        if (_packIdLinesTexData == RectIdInvalid || !TryGetRect(_packIdLinesTexData, out _))
        {
            _packIdLinesTexData = AddCustomRect(LinesTextureWidth, LinesTextureHeight);
            added = true;
        }

        if (!TryGetRect(_packIdLinesTexData, out ImTextureRect rect))
            return;

        var uvLines = new ImVec4[TexLinesWidthMax + 1 + TexLinesDetailedWidthCount];
        byte[]? pixels = TexData?.Pixels;
        int bytesPerPixel = TexData?.BytesPerPixel ?? 4;

        // 1. Integer lines: 0.TexLinesWidthMax.
        for (int n = 0; n <= TexLinesWidthMax; n++)
        {
            int y = n;
            int lineWidth = n;
            int padLeft = (rect.W - lineWidth) / 2;
            int padRight = rect.W - (padLeft + lineWidth);

            if (added && pixels != null)
            {
                int rowStart = (rect.Y + y) * TexWidth * bytesPerPixel + rect.X * bytesPerPixel;
                if (bytesPerPixel == 1)
                {
                    for (int i = 0; i < padLeft; i++)
                        pixels[rowStart + i] = 0;
                    for (int i = 0; i < lineWidth; i++)
                        pixels[rowStart + padLeft + i] = 255;
                    for (int i = 0; i < padRight; i++)
                        pixels[rowStart + padLeft + lineWidth + i] = 0;
                }
                else
                {
                    for (int i = 0; i < padLeft; i++)
                    {
                        int p = rowStart + i * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = 0;
                    }
                    for (int i = 0; i < lineWidth; i++)
                    {
                        int p = rowStart + (padLeft + i) * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = 255;
                    }
                    for (int i = 0; i < padRight; i++)
                    {
                        int p = rowStart + (padLeft + lineWidth + i) * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = 0;
                    }
                }
            }

            // Canonical UV: u0 = (x + pad_left - 0.5f) * TexUvScale.X, u1 = (x + pad_left + line_width + 0.5f) * TexUvScale.X
            float uv0X = (rect.X + padLeft - 0.5f) * TexUvScale.X;
            float uv1X = (rect.X + padLeft + lineWidth + 0.5f) * TexUvScale.X;
            float uv0Y = (rect.Y + y) * TexUvScale.Y;
            float uv1Y = (rect.Y + y + 1) * TexUvScale.Y;
            float halfV = (uv0Y + uv1Y) * 0.5f;
            float invLineWidth = (lineWidth > 0) ? (1.0f / (float)lineWidth) : 0.0f;
            uvLines[n] = new ImVec4(uv0X, uv1X, halfV, invLineWidth);
        }

        // 2. Super-sampled line textures: n = 0.TexLinesDetailedWidthCount - 1.
        byte[] ramp = new byte[TexLinesSampleCount];
        for (int n = 0; n < TexLinesSampleCount; n++)
            ramp[n] = (byte)(((float)n / (float)TexLinesSampleCount) * 255.0f);

        for (int n = 0; n < TexLinesDetailedWidthCount; n++)
        {
            int y = TexLinesWidthMax + 1 + n;
            int lineWidth = 1 + n;

            if (added && pixels != null)
            {
                int rowStart = (rect.Y + y) * TexWidth * bytesPerPixel + rect.X * bytesPerPixel;
                int idx = 0;
                if (bytesPerPixel == 1)
                {
                    for (int i = 0; i < TexLinesSampleCount; i++)
                        pixels[rowStart + idx++] = ramp[i];
                    for (int i = 0; i < lineWidth; i++)
                        pixels[rowStart + idx++] = 255;
                    for (int i = TexLinesSampleCount - 1; i >= 0; i--)
                        pixels[rowStart + idx++] = ramp[i];
                    while (idx < rect.W)
                        pixels[rowStart + idx++] = 0;
                }
                else
                {
                    for (int i = 0; i < TexLinesSampleCount; i++)
                    {
                        int p = rowStart + (idx++) * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = ramp[i];
                    }
                    for (int i = 0; i < lineWidth; i++)
                    {
                        int p = rowStart + (idx++) * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = 255;
                    }
                    for (int i = TexLinesSampleCount - 1; i >= 0; i--)
                    {
                        int p = rowStart + (idx++) * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = ramp[i];
                    }
                    while (idx < rect.W)
                    {
                        int p = rowStart + (idx++) * 4;
                        pixels[p] = 255; pixels[p + 1] = 255; pixels[p + 2] = 255; pixels[p + 3] = 0;
                    }
                }
            }

            float uv0X = (rect.X + 0.5f) * TexUvScale.X;
            float uv1X = (rect.X - 0.5f + TexLinesSampleCount * 2 + lineWidth) * TexUvScale.X;
            float uv0Y = (rect.Y + y) * TexUvScale.Y;
            float uv1Y = (rect.Y + y + 1) * TexUvScale.Y;
            float halfV = (uv0Y + uv1Y) * 0.5f;
            float invLineWidth = 1.0f / (1.0f + (float)n / (float)TexLinesSampleCount);
            uvLines[TexLinesWidthMax + 1 + n] = new ImVec4(uv0X, uv1X, halfV, invLineWidth);
        }

        TexUvLines = uvLines;
        if (added)
            TexData?.QueueUpload(rect.X, rect.Y, rect.W, rect.H);
    }

    /// <summary>
    /// Refreshes every cached region after the texture size or layout changed. Mirrors canonical
    /// <c>ImFontAtlasBuildUpdateTexData</c>.
    /// </summary>
    private void BuildUpdateTexData()
    {
        BuildUpdateTexDataBasic();
        BuildUpdateTexDataLines();
    }

    /// <summary>
    /// Rewrites every baked glyph's UV coordinates from its packed rectangle after a repack.
    /// Mirrors the UV patch loop of canonical <c>ImFontAtlasTextureRepack</c>
    /// </summary>
    private void PatchGlyphUvs()
    {
        for (int fontIndex = 0; fontIndex < Fonts.Count; fontIndex++)
        {
            ImFont font = Fonts[fontIndex];
            for (int bakedIndex = 0; bakedIndex < font.BakedList.Count; bakedIndex++)
            {
                List<ImFontGlyph> glyphs = font.BakedList[bakedIndex].Glyphs;
                for (int glyphIndex = 0; glyphIndex < glyphs.Count; glyphIndex++)
                {
                    ImFontGlyph glyph = glyphs[glyphIndex];
                    if (glyph.PackId == RectIdInvalid || !TryGetRect(glyph.PackId, out ImTextureRect rect))
                        continue;
                    glyph.U0 = rect.X * TexUvScale.X;
                    glyph.V0 = rect.Y * TexUvScale.Y;
                    glyph.U1 = (rect.X + rect.W) * TexUvScale.X;
                    glyph.V1 = (rect.Y + rect.H) * TexUvScale.Y;
                    glyphs[glyphIndex] = glyph;
                }
            }
        }
    }

    /// <summary>
    /// Copies a pixel block between two textures of the same format. Mirrors canonical
    /// <c>ImFontAtlasTextureBlockCopy</c>.
    /// </summary>
    /// <param name="srcTex">Source texture.</param>
    /// <param name="srcX">Source left coordinate.</param>
    /// <param name="srcY">Source top coordinate.</param>
    /// <param name="dstTex">Destination texture.</param>
    /// <param name="dstX">Destination left coordinate.</param>
    /// <param name="dstY">Destination top coordinate.</param>
    /// <param name="w">Block width in pixels.</param>
    /// <param name="h">Block height in pixels.</param>
    private static void CopyTextureBlock(ImTextureData srcTex, int srcX, int srcY,
        ImTextureData dstTex, int dstX, int dstY, int w, int h)
    {
        byte[]? src = srcTex.Pixels;
        byte[]? dst = dstTex.Pixels;
        if (src == null || dst == null)
            return;

        int bytesPerPixel = dstTex.BytesPerPixel;
        int srcPitch = srcTex.GetPitch();
        int dstPitch = dstTex.GetPitch();
        for (int row = 0; row < h; row++)
        {
            Buffer.BlockCopy(src, (srcY + row) * srcPitch + srcX * bytesPerPixel,
                dst, (dstY + row) * dstPitch + dstX * bytesPerPixel, w * bytesPerPixel);
        }
    }

    /// <summary>
    /// Retrieves the atlas as a managed RGBA32 pixel array, building it on demand. Mirrors the
    /// canonical legacy <c>GetTexDataAsRGBA32</c> convenience
    /// </summary>
    /// <param name="outPixels">Receives the pixel buffer.</param>
    /// <param name="outWidth">Receives the texture width.</param>
    /// <param name="outHeight">Receives the texture height.</param>
    /// <remarks>
    /// Forces <see cref="TexDesiredFormat"/> to <see cref="ImTextureFormat.RGBA32"/> and rebuilds when
    /// the current texture was created in another format, matching canonical
    /// <c>GetTexDataAsFormat</c>.
    /// </remarks>
    public void GetTexDataAsRGBA32(out byte[] outPixels, out int outWidth, out int outHeight)
    {
        if (TexData?.Pixels == null || TexDesiredFormat != ImTextureFormat.RGBA32)
        {
            TexDesiredFormat = ImTextureFormat.RGBA32;
            Build();
        }

        outPixels = TexData!.Pixels!;
        outWidth = TexWidth;
        outHeight = TexHeight;
    }

    /// <summary>
    /// Retrieves the atlas as a managed single-channel Alpha8 pixel array, building it on demand.
    /// Mirrors the canonical legacy <c>GetTexDataAsAlpha8</c> convenience
    /// </summary>
    /// <param name="outPixels">Receives the single-channel pixel buffer.</param>
    /// <param name="outWidth">Receives the texture width.</param>
    /// <param name="outHeight">Receives the texture height.</param>
    /// <remarks>
    /// Forces <see cref="TexDesiredFormat"/> to <see cref="ImTextureFormat.Alpha8"/> before building.
    /// Canonical also reports the bytes-per-pixel and a raw pointer; the managed overload returns the
    /// buffer directly, and callers can read <see cref="ImTextureData.BytesPerPixel"/> from
    /// <see cref="TexData"/> when they need it.
    /// </remarks>
    public void GetTexDataAsAlpha8(out byte[] outPixels, out int outWidth, out int outHeight)
    {
        if (TexData?.Pixels == null || TexDesiredFormat != ImTextureFormat.Alpha8)
        {
            TexDesiredFormat = ImTextureFormat.Alpha8;
            Build();
        }

        outPixels = TexData!.Pixels!;
        outWidth = TexWidth;
        outHeight = TexHeight;
    }

    /// <summary>
    /// Retrieves the atlas as a pinned native pointer to RGBA32 pixels, building it on demand.
    /// Mirrors the canonical legacy <c>GetTexDataAsRGBA32</c> pointer overload.
    /// </summary>
    /// <remarks>
    /// The pin is released automatically when the texture is reallocated by a repack; callers must
    /// re-acquire the pointer after any operation that can grow the atlas.
    /// </remarks>
    /// <param name="outPixels">Receives a pinned pointer to the pixel buffer.</param>
    /// <param name="outWidth">Receives the texture width.</param>
    /// <param name="outHeight">Receives the texture height.</param>
    public void GetTexDataAsRGBA32(out IntPtr outPixels, out int outWidth, out int outHeight)
    {
        if (TexData?.Pixels == null || TexDesiredFormat != ImTextureFormat.RGBA32)
        {
            TexDesiredFormat = ImTextureFormat.RGBA32;
            Build();
        }

        if (!_isPinned)
        {
            _pinnedHandle = GCHandle.Alloc(TexData!.Pixels, GCHandleType.Pinned);
            _isPinned = true;
        }

        outPixels = _pinnedHandle.AddrOfPinnedObject();
        outWidth = TexWidth;
        outHeight = TexHeight;
    }

    /// <summary>Releases any pin held on the current pixel buffer.</summary>
    private void ReleasePin()
    {
        if (_isPinned)
        {
            _pinnedHandle.Free();
            _isPinned = false;
        }
    }

    /// <summary>
    /// Releases the managed texture buffer and the builder state, so the next build recreates them.
    /// Mirrors canonical <c>ClearTexData</c>.
    /// </summary>
    public void ClearTexData()
    {
        ClearTextureState();
        ResetBuilder();
        FrameCount = -1;
    }

    /// <summary>
    /// Releases the managed texture buffer and its derived UV state without touching the builder, the
    /// font list or the frame counter. Mirrors the texture-release portion of canonical
    /// <c>ImFontAtlas::ClearTexData</c>. and is the
    /// shared step of <see cref="ClearTexData"/> and the format-change reset in <see cref="Build"/>.
    /// </summary>
    private void ClearTextureState()
    {
        ReleasePin();
        TexData?.DestroyPixels();
        TexData = null;
        TexWidth = 0;
        TexHeight = 0;
        TexUvScale = ImVec2.Zero;
        TexUvWhitePixel = ImVec2.Zero;
        TexUvLines = Array.Empty<ImVec4>();
        TexIsBuilt = false;
        TexID = IntPtr.Zero;
    }

    /// <summary>Clears fonts, sources and texture data, mirroring canonical <c>Clear</c>.</summary>
    public void Clear()
    {
        ClearTexData();
        Fonts.Clear();
        Sources.Clear();
    }

    /// <summary>Returns the smallest power of two greater than or equal to a value.</summary>
    /// <param name="value">Value to round up.</param>
    /// <returns>The rounded-up size.</returns>
    private static int UpperPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
            result <<= 1;
        return result;
    }

    #endregion
}

#endregion

#region Font and texture free helpers

/// <summary>
/// Font and texture helpers that canonical exposes as free functions. The implementations live on the
/// types that own the associated data (the font atlas and the font); these entry points restore the
/// canonical owner and signature without duplicating the algorithms.
/// </summary>
public static partial class ImGui
{
    /// <summary>
    /// Returns the byte count of one pixel for a texture format. Mirrors canonical
    /// <c>ImTextureDataGetFormatBytesPerPixel</c>.
    /// </summary>
    /// <param name="format">The texture pixel format to measure.</param>
    /// <returns>One for <see cref="ImTextureFormat.Alpha8"/> and four for
    /// <see cref="ImTextureFormat.RGBA32"/>.</returns>
    /// <remarks>
    /// <see cref="ImTextureData.Create"/> applies the same mapping when it allocates the pixel buffer;
    /// this entry point exposes it for callers that compute row strides or sizes without an allocated
    /// <see cref="ImTextureData"/>. An undefined format is a precondition violation: the canonical
    /// <c>IM_ASSERT(0)</c> is realized as a debug assertion and zero is returned.
    /// </remarks>
    public static int ImTextureDataGetFormatBytesPerPixel(ImTextureFormat format)
    {
        switch (format)
        {
            case ImTextureFormat.Alpha8:
                return 1;
            case ImTextureFormat.RGBA32:
                return 4;
            default:
                ImAssert.Check(false, "Unknown texture format.");
                return 0;
        }
    }

    /// <summary>
    /// Measures a text run with canonical <c>ImFontCalcTextSizeEx</c> semantics. Mirrors canonical
    /// <c>ImFontCalcTextSizeEx</c> (, declared in
    /// </summary>
    /// <param name="font">Font to measure with.</param>
    /// <param name="size">Line height in pixels.</param>
    /// <param name="maxWidth">Maximum line width; measurement stops before exceeding it.</param>
    /// <param name="wrapWidth">Word-wrap width, or zero to disable wrapping.</param>
    /// <param name="text">Text to measure.</param>
    /// <param name="remainingChars">Receives the number of characters not consumed by a
    /// <paramref name="maxWidth"/> cutoff.</param>
    /// <param name="offset">Receives the cursor offset after the run, allowing for a trailing newline.</param>
    /// <returns>The measured width and height in pixels.</returns>
    /// <remarks>
    /// The canonical function is a free function taking an <c>ImFont*</c>; this port realizes it as a
    /// static member of <see cref="ImFont"/> because the font owns the bake and advance caches it reads.
    /// This entry point restores the canonical owner without re-implementing the measurement.
    /// </remarks>
    public static ImVec2 ImFontCalcTextSizeEx(ImFont font, float size, float maxWidth, float wrapWidth,
        ReadOnlySpan<char> text, out int remainingChars, out ImVec2 offset)
    {
        return ImFont.ImFontCalcTextSizeEx(font, size, maxWidth, wrapWidth, text, out remainingChars, out offset);
    }

    /// <summary>
    /// Decodes one UTF-8 codepoint. Mirrors canonical <c>ImTextCharFromUtf8</c>
    /// (.).
    /// </summary>
    /// <param name="input">UTF-8 byte buffer.</param>
    /// <param name="i">Cursor, advanced past the consumed bytes.</param>
    /// <param name="outChar">Receives the decoded codepoint, or U+FFFD when malformed.</param>
    /// <returns>The number of bytes consumed.</returns>
    /// <remarks>
    /// The canonical function takes a raw pointer and an explicit end pointer; the managed
    /// representation is a <see cref="ReadOnlySpan{T}"/> over the same bytes. The decoder is owned by
    /// <see cref="ImFont"/> and is reused here so the canonical owner is available to byte-oriented
    /// callers.
    /// </remarks>
    public static int ImTextCharFromUtf8(ReadOnlySpan<byte> input, ref int i, out uint outChar)
    {
        return ImFont.ImTextCharFromUtf8(input, ref i, out outChar);
    }
}

#endregion
