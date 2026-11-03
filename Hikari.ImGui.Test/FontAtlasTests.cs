// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.IO;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the font-atlas surface with concrete state assertions: the nine canonical glyph-range
/// tables, file and compressed-stream font loading, the inline <see cref="ImFont"/> members, the
/// input-data and font clearing paths, the Alpha8 and RGBA32 texture accessors, the custom-rectangle
/// and custom-glyph APIs, and the texture-data lifecycle fields.
/// </summary>
/// <remarks>
/// <para>
/// The tests never attach a context: this surface is manipulated directly on an
/// <see cref="ImFontAtlas"/>, and the file-loading test writes and removes its own temporary face.
/// </para>
/// </remarks>
[TestFixture]
public class FontAtlasTests
{
    /// <summary>Decompresses the embedded ProggyClean face into a temporary file and returns its path.</summary>
    /// <returns>Path to a temporary TrueType file owned by the caller.</returns>
    private static string WriteTemporaryProggyCleanFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hikari-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, ImGuiDefaultFontData.DecompressProggyClean());
        return path;
    }

    /// <summary>Asserts that a range table ends with the canonical zero terminator.</summary>
    /// <param name="ranges">Range table to inspect.</param>
    private static void AssertTerminated(uint[] ranges)
    {
        Assert.That(ranges.Length, Is.GreaterThan(0));
        Assert.That(ranges[^1], Is.EqualTo(0u), "Every canonical range table ends with a zero terminator.");
    }

    #region Glyph range tables

    [Test]
    /// <summary>Verifies the small literal range tables against their exact canonical values.</summary>
    public void GlyphRanges_LiteralTables_MatchCanonicalValues()
    {
        ImFontAtlas atlas = new();

        Assert.That(atlas.GetGlyphRangesDefault(), Is.EqualTo(new uint[] { 0x0020, 0x00FF, 0 }));
        Assert.That(atlas.GetGlyphRangesGreek(), Is.EqualTo(new uint[] { 0x0020, 0x00FF, 0x0370, 0x03FF, 0 }));
        Assert.That(atlas.GetGlyphRangesKorean(), Is.EqualTo(new uint[]
        {
            0x0020, 0x00FF, 0x3131, 0x3163, 0xAC00, 0xD7A3, 0xFFFD, 0xFFFD, 0,
        }));
        Assert.That(atlas.GetGlyphRangesChineseFull(), Is.EqualTo(new uint[]
        {
            0x0020, 0x00FF, 0x2000, 0x206F, 0x3000, 0x30FF, 0x31F0, 0x31FF,
            0xFF00, 0xFFEF, 0xFFFD, 0xFFFD, 0x4e00, 0x9FAF, 0,
        }));
        Assert.That(atlas.GetGlyphRangesCyrillic(), Is.EqualTo(new uint[]
        {
            0x0020, 0x00FF, 0x0400, 0x052F, 0x2DE0, 0x2DFF, 0xA640, 0xA69F, 0,
        }));
        Assert.That(atlas.GetGlyphRangesThai(), Is.EqualTo(new uint[]
        {
            0x0020, 0x00FF, 0x2010, 0x205E, 0x0E00, 0x0E7F, 0,
        }));
        Assert.That(atlas.GetGlyphRangesVietnamese(), Is.EqualTo(new uint[]
        {
            0x0020, 0x00FF,
            0x0102, 0x0103, 0x0110, 0x0111, 0x0128, 0x0129, 0x0168, 0x0169,
            0x01A0, 0x01A1, 0x01AF, 0x01B0, 0x1EA0, 0x1EF9,
            0,
        }));
    }

    [Test]
    /// <summary>Verifies the two CJK tables expand to the canonical length and final codepoint.</summary>
    public void GlyphRanges_ChineseSimplifiedAndJapanese_ExpandToCanonicalBounds()
    {
        ImFontAtlas atlas = new();

        // Chinese Simplified Common: 12 base values + 2500 cumulative offsets as single-codepoint
        // pairs + the terminator.
        uint[] chinese = atlas.GetGlyphRangesChineseSimplifiedCommon();
        AssertTerminated(chinese);
        Assert.That(chinese.Length, Is.EqualTo(12 + 2500 * 2 + 1));
        Assert.That(chinese[0], Is.EqualTo(0x0020u));
        Assert.That(chinese[12], Is.EqualTo(0x4E00u), "The first expanded ideograph is the initial base.");
        Assert.That(chinese[13], Is.EqualTo(0x4E00u), "Each expanded offset is a single-codepoint range.");
        Assert.That(chinese[^2], Is.EqualTo(0x9F9Fu), "The cumulative offsets end at U+9F9F.");
        Assert.That(chinese[^3], Is.EqualTo(0x9F9Fu));

        // Japanese: 10 base values + 2999 cumulative offsets + the terminator
        //.
        uint[] japanese = atlas.GetGlyphRangesJapanese();
        AssertTerminated(japanese);
        Assert.That(japanese.Length, Is.EqualTo(10 + 2999 * 2 + 1));
        Assert.That(japanese[10], Is.EqualTo(0x4E00u));
        Assert.That(japanese[11], Is.EqualTo(0x4E00u));
        Assert.That(japanese[^2], Is.EqualTo(0xFA69u), "The cumulative offsets end at U+FA69.");
        Assert.That(japanese[^3], Is.EqualTo(0xFA69u));
    }

    #endregion

    #region File loading

    [Test]
    /// <summary>Loads a face from disk and verifies the resulting font and source state.</summary>
    public void AddFontFromFileTTF_LoadsFaceFromDisk()
    {
        string path = WriteTemporaryProggyCleanFile();
        try
        {
            ImFontAtlas atlas = new();
            ImFont? font = atlas.AddFontFromFileTTF(path, 13.0f);

            Assert.That(font, Is.Not.Null);
            Assert.That(font!.IsLoaded, Is.True);
            Assert.That(font.OwnerAtlas, Is.SameAs(atlas));
            Assert.That(font.Sources.Count, Is.EqualTo(1));
            Assert.That(atlas.Fonts.Count, Is.EqualTo(1));
            Assert.That(atlas.Sources.Count, Is.EqualTo(1));
            Assert.That(font.GetDebugName(), Is.EqualTo(Path.GetFileName(path)));
            Assert.That(font.LegacySize, Is.EqualTo(13.0f));

            // The face is usable: a Latin glyph bakes with a positive advance.
            Assert.That(font.IsGlyphInFont('A'), Is.True);
            Assert.That(font.FindGlyph('A').AdvanceX, Is.GreaterThan(0.0f));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    /// <summary>Verifies a missing font file returns the canonical null result.</summary>
    public void AddFontFromFileTTF_MissingFile_ReturnsNull()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"hikari-missing-{Guid.NewGuid():N}.ttf");
        ImFontAtlas atlas = new();

        Assert.That(atlas.AddFontFromFileTTF(missing, 13.0f), Is.Null);
        Assert.That(atlas.Fonts.Count, Is.EqualTo(0));
        Assert.That(atlas.Sources.Count, Is.EqualTo(0));
    }

    [Test]
    /// <summary>Verifies a malformed base85 compressed stream is rejected.</summary>
    public void AddFontFromMemoryCompressedBase85TTF_MalformedStream_Throws()
    {
        ImFontAtlas atlas = new();

        // Five characters decode to four zero bytes, which is shorter than the stb_compress header.
        Assert.Throws<InvalidOperationException>(
            () => atlas.AddFontFromMemoryCompressedBase85TTF("!!!!!", 13.0f));
    }

    #endregion

    #region Inline ImFont members

    [Test]
    /// <summary>Verifies IsLoaded and GetDebugName report the attachment and source name state.</summary>
    public void ImFont_IsLoadedAndGetDebugName_ReportAttachmentState()
    {
        var detached = new ImFont();
        Assert.That(detached.IsLoaded, Is.False);
        Assert.That(detached.GetDebugName(), Is.EqualTo("<unknown>"));

        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        Assert.That(font.IsLoaded, Is.True);
        Assert.That(font.GetDebugName(), Is.EqualTo("ProggyClean.ttf"));
    }

    [Test]
    /// <summary>Verifies glyph baking marks the 8K page map and that ClearOutputData resets it.</summary>
    public void ImFont_GlyphRangeUnusedAndClearOutputData_TrackBakedPages()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Build();

        // The legacy build preloads the default face's Latin range, so its 8K page is already marked
        // while a CJK page with no baked glyph stays unused.
        Assert.That(font.IsGlyphRangeUnused(0x0041, 0x0041), Is.False);
        Assert.That(font.IsGlyphRangeUnused(0x4E00, 0x4EFF), Is.True);

        // Pack a custom glyph in the CJK page; that marks the page as used.
        int id = atlas.AddCustomRectFontGlyph(font, 0x4E00, 4, 4, 5.0f, new ImVec2(0.0f, 0.0f));
        Assert.That(id, Is.GreaterThanOrEqualTo(0));
        Assert.That(font.IsGlyphRangeUnused(0x4E00, 0x4E00), Is.False,
            "Baking a glyph marks its 8K codepoint page.");

        Assert.That(font.BakedList.Count, Is.GreaterThan(0));
        font.ClearOutputData();

        Assert.That(font.LastBaked, Is.Null);
        Assert.That(font.BakedList.Count, Is.EqualTo(0));
        Assert.That(font.IsGlyphRangeUnused(0x0041, 0x0041), Is.True,
            "ClearOutputData clears the used-page map.");
        Assert.That(font.IsGlyphRangeUnused(0x4E00, 0x4E00), Is.True);
    }

    [Test]
    /// <summary>Verifies the remap table stays inspectable through its canonical internal member.</summary>
    public void ImFont_RemapPairs_RecordInstalledRemaps()
    {
        var font = new ImFont();
        Assert.That(font.RemapPairs.Count, Is.EqualTo(0));

        font.AddRemapChar('a', 'b');
        Assert.That(font.RemapPairs.Count, Is.EqualTo(1));
        Assert.That(font.RemapPairs[0x61], Is.EqualTo(0x62u));
        Assert.That(font.RemapCodepoint('a'), Is.EqualTo(0x62u));
    }

    #endregion

    #region Clearing

    [Test]
    /// <summary>Verifies ClearInputData drops sources and marks the fonts glyph-less.</summary>
    public void ClearInputData_DropsSourcesAndMarksFontsGlyphLess()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Build();
        Assert.That(atlas.Sources.Count, Is.EqualTo(1));

        atlas.ClearInputData();

        Assert.That(atlas.Sources.Count, Is.EqualTo(0));
        Assert.That(atlas.Fonts.Count, Is.EqualTo(1));
        Assert.That(font.Sources.Count, Is.EqualTo(0));
        Assert.That(font.BakedList.Count, Is.EqualTo(0));
        Assert.That((font.Flags & ImFontFlags.NoLoadGlyphs) != 0, Is.True);
    }

    [Test]
    /// <summary>Verifies ClearFonts removes every font and source and resets the builder.</summary>
    public void ClearFonts_RemovesFontsAndSources()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();

        atlas.ClearFonts();

        Assert.That(atlas.Fonts.Count, Is.EqualTo(0));
        Assert.That(atlas.Sources.Count, Is.EqualTo(0));
        Assert.That(atlas.TexIsBuilt, Is.False);

        // The atlas rebuilds from scratch after the clear.
        Assert.That(atlas.Build(), Is.True);
        Assert.That(atlas.Fonts.Count, Is.EqualTo(1));
    }

    [Test]
    /// <summary>Verifies RemoveFont detaches exactly the requested font and its sources.</summary>
    public void RemoveFont_DetachesRequestedFontAndSources()
    {
        ImFontAtlas atlas = new();
        ImFont first = atlas.AddFontDefaultBitmap();
        ImFont second = atlas.AddFontDefaultVector();
        Assert.That(atlas.Sources.Count, Is.EqualTo(2));

        atlas.RemoveFont(first);

        Assert.That(atlas.Fonts.Count, Is.EqualTo(1));
        Assert.That(atlas.Sources.Count, Is.EqualTo(1));
        Assert.That(atlas.Fonts[0], Is.SameAs(second));
        Assert.That(first.IsLoaded, Is.False);
        Assert.That(first.OwnerAtlas, Is.Null);
        Assert.That(second.IsLoaded, Is.True);
    }

    #endregion

    #region Texture format and accessors

    [Test]
    /// <summary>Verifies the Alpha8 accessor produces a single-channel texture and RGBA32 restores four.</summary>
    public void GetTexDataAsAlpha8_AndRGBA32_SelectTheRequestedFormat()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();

        atlas.GetTexDataAsAlpha8(out byte[] alphaPixels, out int alphaWidth, out int alphaHeight);
        Assert.That(atlas.TexDesiredFormat, Is.EqualTo(ImTextureFormat.Alpha8));
        Assert.That(atlas.TexData!.BytesPerPixel, Is.EqualTo(1));
        Assert.That(alphaPixels.Length, Is.EqualTo(alphaWidth * alphaHeight));

        atlas.GetTexDataAsRGBA32(out byte[] rgbaPixels, out int rgbaWidth, out int rgbaHeight);
        Assert.That(atlas.TexDesiredFormat, Is.EqualTo(ImTextureFormat.RGBA32));
        Assert.That(atlas.TexData!.BytesPerPixel, Is.EqualTo(4));
        Assert.That(rgbaPixels.Length, Is.EqualTo(rgbaWidth * rgbaHeight * 4));
    }

    [Test]
    /// <summary>Verifies the legacy SetTexID records the identifier on the atlas and its texture data.</summary>
    public void SetTexID_RecordsIdentifierOnAtlasAndTexture()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.GetTexDataAsRGBA32(out byte[] _, out int _, out int _);

        var id = (IntPtr)0x1234;
        atlas.SetTexID(id);

        Assert.That(atlas.TexID, Is.EqualTo(id));
        Assert.That(atlas.TexData!.TexID, Is.EqualTo(id));
    }

    [Test]
    /// <summary>Verifies the texture-data lifecycle fields round-trip their values.</summary>
    public void ImTextureData_LifecycleFields_RoundTrip()
    {
        var tex = new ImTextureData();
        Assert.That(tex.RefCount, Is.EqualTo(0));
        Assert.That(tex.UnusedFrames, Is.EqualTo(0));
        Assert.That(tex.BackendUserData, Is.Null);
        Assert.That(tex.QueueUserData, Is.Null);

        object backendState = new object();
        object queueState = new object();
        tex.RefCount = 2;
        tex.UnusedFrames = 5;
        tex.BackendUserData = backendState;
        tex.QueueUserData = queueState;

        Assert.That(tex.RefCount, Is.EqualTo(2));
        Assert.That(tex.UnusedFrames, Is.EqualTo(5));
        Assert.That(tex.BackendUserData, Is.SameAs(backendState));
        Assert.That(tex.QueueUserData, Is.SameAs(queueState));
    }

    #endregion

    #region Custom rectangles and glyphs

    [Test]
    /// <summary>Verifies GetCustomRect returns position, size and UVs consistent with the texture scale.</summary>
    public void GetCustomRect_ReturnsUvsDerivedFromTextureScale()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();

        int id = atlas.AddCustomRect(8, 4);
        Assert.That(id, Is.GreaterThanOrEqualTo(0));

        Assert.That(atlas.GetCustomRect(id, out ImFontAtlasRect rect), Is.True);
        Assert.That(rect.W, Is.EqualTo((ushort)8));
        Assert.That(rect.H, Is.EqualTo((ushort)4));
        Assert.That(rect.Uv0.X, Is.EqualTo(rect.X * atlas.TexUvScale.X).Within(1e-5f));
        Assert.That(rect.Uv0.Y, Is.EqualTo(rect.Y * atlas.TexUvScale.Y).Within(1e-5f));
        Assert.That(rect.Uv1.X, Is.EqualTo((rect.X + rect.W) * atlas.TexUvScale.X).Within(1e-5f));
        Assert.That(rect.Uv1.Y, Is.EqualTo((rect.Y + rect.H) * atlas.TexUvScale.Y).Within(1e-5f));

        Assert.That(atlas.GetCustomRect(9999, out _), Is.False, "Unknown identifiers do not resolve.");
    }

    /// <summary>
    /// Verifies that a locked atlas (a renderer without texture updates) returns the closest existing bake
    /// rather than creating one, and that an unlocked atlas still creates a bake at the requested size
    /// </summary>
    [Test]
    public void GetFontBaked_LockedAtlasUsesClosestExistingBake()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Build();
        ImFontBaked existing = font.GetFontBaked(16.0f);

        atlas.Locked = true;
        int countBefore = font.BakedList.Count;
        ImFontBaked closest = font.GetFontBaked(17.0f);
        Assert.That(closest, Is.SameAs(existing));
        Assert.That(font.BakedList.Count, Is.EqualTo(countBefore));

        atlas.Locked = false;
        ImFontBaked created = font.GetFontBaked(40.0f);
        Assert.That(created, Is.Not.SameAs(existing));
        Assert.That(created.Size, Is.EqualTo(40.0f));
    }

    /// <summary>
    /// Verifies that a locked atlas whose font has no bake at all fails rather than creating an upload-less bake,
    /// and that the same request succeeds once a bake exists.
    /// </summary>
    [Test]
    public void GetFontBaked_LockedAtlasWithoutAnyBakeThrows()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Locked = true;

        // Build() is not run, so no baked set exists and the locked atlas cannot substitute one.
        Assert.Throws<InvalidOperationException>(() => font.GetFontBaked(24.0f));
        Assert.That(font.BakedList.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that the bitmap default font snaps horizontally when no configuration template is given, and
    /// keeps the caller's choice when a template is supplied.
    /// </summary>
    [Test]
    public void AddFontDefaultBitmap_SnapsHorizontallyOnlyWithoutTemplate()
    {
        ImFontAtlas defaultAtlas = new();
        ImFont defaultFont = defaultAtlas.AddFontDefaultBitmap();
        Assert.That(defaultFont.Sources[0].PixelSnapH, Is.True, "No template: PixelSnapH must default to true.");

        ImFontAtlas templateAtlas = new();
        ImFont templated = templateAtlas.AddFontDefaultBitmap(new ImFontConfig { PixelSnapH = false });
        Assert.That(templated.Sources[0].PixelSnapH, Is.False, "An explicit template keeps its PixelSnapH value.");
    }

    /// <summary>Verifies a custom glyph is packed and installed into the baked glyph table.</summary>
    public void AddCustomRectFontGlyph_PacksAndInstallsGlyph()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Build();

        const uint codepoint = 0xE000; // Private use area, absent from the default face.
        int id = atlas.AddCustomRectFontGlyph(font, codepoint, 6, 7, 8.0f, new ImVec2(1.0f, 2.0f));
        Assert.That(id, Is.GreaterThanOrEqualTo(0));

        ImFontBaked baked = font.GetFontBaked(font.LegacySize);
        Assert.That(baked.IsGlyphLoaded(codepoint), Is.True);
        ImFontGlyph glyph = baked.FindGlyph(codepoint);
        Assert.That(glyph.Visible, Is.True);
        Assert.That(glyph.Colored, Is.True, "Canonical marks custom-rectangle glyphs colored.");
        Assert.That(glyph.AdvanceX, Is.EqualTo(8.0f));
        Assert.That(glyph.X0, Is.EqualTo(1.0f));
        Assert.That(glyph.Y0, Is.EqualTo(2.0f));
        Assert.That(glyph.X1, Is.EqualTo(7.0f));
        Assert.That(glyph.Y1, Is.EqualTo(9.0f));
        Assert.That(glyph.PackId, Is.EqualTo(id));
    }

    [Test]
    /// <summary>Verifies re-adding a custom glyph replaces the previous one and releases its rectangle.</summary>
    public void AddCustomRectFontGlyph_ReplacesAnExistingGlyph()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Build();

        const uint codepoint = 0xE001;
        int first = atlas.AddCustomRectFontGlyph(font, codepoint, 4, 4, 5.0f, new ImVec2(0.0f, 0.0f));
        int second = atlas.AddCustomRectFontGlyph(font, codepoint, 5, 5, 6.0f, new ImVec2(0.0f, 0.0f));

        Assert.That(second, Is.GreaterThanOrEqualTo(0));
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(atlas.GetCustomRect(first, out _), Is.False,
            "The replaced glyph's rectangle is returned to the packer.");

        ImFontBaked baked = font.GetFontBaked(font.LegacySize);
        ImFontGlyph glyph = baked.FindGlyph(codepoint);
        Assert.That(glyph.PackId, Is.EqualTo(second));
        Assert.That(glyph.AdvanceX, Is.EqualTo(6.0f));
    }

    #endregion
}
