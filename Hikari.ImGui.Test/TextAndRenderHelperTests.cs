// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical text submission helpers and the low-level render helpers owned by
/// <c>ImGuiWidgets.cs</c>. Every count asserted here is derived from the canonical tessellation rules
/// rather than from a recorded snapshot.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>. /// </remarks>
[TestFixture]
public class TextAndRenderHelperTests : ImGuiTestFixture
{
    /// <summary>Window origin used by the placement-sensitive tests.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);

    /// <summary>Opaque white in canonical ABGR order.</summary>
    private const uint OpaqueWhite = 0xFFFFFFFFu;


    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
    }


    /// <summary>Starts and finishes one full frame so the font atlas and <c>FontSize</c> are resolved.</summary>
    private static void Frame()
    {
        NewTestFrame();
        ImGui.Render();
    }

    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="body">Assertions executed between Begin and End.</param>
    private static void WindowFrame(string name, ImVec2 size, Action body)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin(name);
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Submits <paramref name="draw"/> inside a window and returns the change in the current window's
    /// vertex and index buffer counts caused by the call.
    /// </summary>
    /// <param name="draw">The draw call to measure.</param>
    /// <returns>The vertex and index count deltas.</returns>
    private static (int Vtx, int Idx) MeasureWindowDraw(Action draw)
    {
        int vtx = 0;
        int idx = 0;
        WindowFrame("Measure", new ImVec2(400.0f, 300.0f), () =>
        {
            ImDrawList drawList = _ctxCurrentWindowDrawList();
            int vtxBefore = drawList.VtxBuffer.Count;
            int idxBefore = drawList.IdxBuffer.Count;
            draw();
            vtx = drawList.VtxBuffer.Count - vtxBefore;
            idx = drawList.IdxBuffer.Count - idxBefore;
        });
        return (vtx, idx);

        static ImDrawList _ctxCurrentWindowDrawList() => ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
    }

    #region Text submission helpers

    /// <summary>
    /// Verifies that <see cref="ImGui.TextUnformatted"/> emits exactly one textured quad per visible
    /// glyph (four vertices and six indices each). Canonical walks the glyph range and calls
    /// <c>PrimReserve(6, 4)</c> per visible glyph
    /// (, ).
    /// </summary>
    [Test]
    public void TextUnformatted_EmitsOneQuadPerGlyph()
    {
        (int vtx, int idx) = MeasureWindowDraw(() => ImGui.TextUnformatted("Hello"));

        // "Hello" has no spaces; the space glyph advances without emitting a quad, so five visible
        // glyphs produce 5 * 4 vertices and 5 * 6 indices.
        Assert.That(vtx, Is.EqualTo(20));
        Assert.That(idx, Is.EqualTo(30));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.Text"/>, <see cref="ImGui.TextColored"/>, and
    /// <see cref="ImGui.TextDisabled"/> share the same glyph geometry (color push/pop does not change
    /// the tessellation). Canonical delegates them all to <c>TextV</c>
    /// </summary>
    [Test]
    public void TextVariants_ShareGlyphGeometry()
    {
        (int plainVtx, _) = MeasureWindowDraw(() => ImGui.Text("Hello"));
        (int coloredVtx, _) = MeasureWindowDraw(() => ImGui.TextColored(new ImVec4(1.0f, 0.0f, 0.0f, 1.0f), "Hello"));
        (int disabledVtx, _) = MeasureWindowDraw(() => ImGui.TextDisabled("Hello"));

        Assert.That(plainVtx, Is.EqualTo(20));
        Assert.That(coloredVtx, Is.EqualTo(plainVtx));
        Assert.That(disabledVtx, Is.EqualTo(plainVtx));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TextWrapped"/> reserves the same glyph geometry as plain text
    /// when the run fits on one line. Canonical pushes a wrap position only to drive line breaking, not
    /// to change per-glyph tessellation.
    /// </summary>
    [Test]
    public void TextWrapped_ShortRun_MatchesPlainTextGeometry()
    {
        (int wrappedVtx, _) = MeasureWindowDraw(() => ImGui.TextWrapped("Hello"));
        (int plainVtx, _) = MeasureWindowDraw(() => ImGui.TextUnformatted("Hello"));

        Assert.That(wrappedVtx, Is.EqualTo(plainVtx));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.LabelText"/> renders the value and the label separately: the
    /// value "Bob" (3 glyphs) plus the label "Name" (4 glyphs) is seven glyph quads. Canonical measures
    /// the value and the label with <c>CalcTextSize</c>/<c>FindRenderedTextEnd</c> and renders both
    /// </summary>
    [Test]
    public void LabelText_DrawsValueAndLabelGlyphs()
    {
        (int vtx, int idx) = MeasureWindowDraw(() => ImGui.LabelText("Name", "Bob"));

        Assert.That(vtx, Is.EqualTo(7 * 4));
        Assert.That(idx, Is.EqualTo(7 * 6));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.BulletText"/> draws an eight-segment filled disc (the canonical
    /// font-size threshold selects eight segments below 22 px) followed by the text glyphs. Canonical
    /// calls <c>RenderBullet</c> then <c>RenderText</c>
    /// (, ).
    /// The anti-aliased convex fill emits two vertices per point and a fan plus one fringe quad per
    /// edge: 8 points give 16 vertices and (8 - 2) * 3 + 8 * 6 = 66 indices.
    /// </summary>
    [Test]
    public void BulletText_DrawsBulletDiscAndLabel()
    {
        (int vtx, int idx) = MeasureWindowDraw(() => ImGui.BulletText("Item"));

        // Disc: 16 vertices / 66 indices. "Item": 4 glyphs -> 16 vertices / 24 indices.
        Assert.That(vtx, Is.EqualTo(16 + 4 * 4));
        Assert.That(idx, Is.EqualTo(66 + 4 * 6));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.RenderText"/> hides everything from the first <c>##</c> marker and
    /// emits no geometry when the visible prefix is empty.
    /// </summary>
    [Test]
    public void RenderText_HidesTextAfterDoubleHash()
    {
        (int vtx, int idx) = MeasureWindowDraw(() => ImGui.RenderText(ImGui.GetCursorScreenPos(), "AB##hidden"));

        Assert.That(vtx, Is.EqualTo(2 * 4));
        Assert.That(idx, Is.EqualTo(2 * 6));

        (int emptyVtx, int emptyIdx) = MeasureWindowDraw(() => ImGui.RenderText(ImGui.GetCursorScreenPos(), "##hidden"));

        Assert.That(emptyVtx, Is.EqualTo(0));
        Assert.That(emptyIdx, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.RenderText"/> draws the whole run when hiding is disabled and
    /// honors an already-resolved range end.
    /// </summary>
    [Test]
    public void RenderText_HonorsHideFlagAndResolvedEnd()
    {
        (int rawVtx, _) = MeasureWindowDraw(() => ImGui.RenderText(ImGui.GetCursorScreenPos(), "AB##", null, false));
        Assert.That(rawVtx, Is.EqualTo(4 * 4));

        (int rangedVtx, _) = MeasureWindowDraw(() => ImGui.RenderText(ImGui.GetCursorScreenPos(), "Hello", "He"));
        Assert.That(rangedVtx, Is.EqualTo(2 * 4));
    }

    #endregion

    #region Text measurement

    /// <summary>
    /// Verifies that <see cref="ImGui.FindRenderedTextEnd"/> truncates at the first <c>##</c> marker and
    /// treats a leading <c>##</c> as an empty display run. Canonical scans until a NUL or the first
    /// <c>"##"</c> pair.
    /// </summary>
    [Test]
    public void FindRenderedTextEnd_StopsAtDoubleHash()
    {
        Assert.That(ImGui.FindRenderedTextEnd("Hello"), Is.EqualTo("Hello"));
        Assert.That(ImGui.FindRenderedTextEnd("Label###identifier"), Is.EqualTo("Label"));
        Assert.That(ImGui.FindRenderedTextEnd("##hidden"), Is.EqualTo(string.Empty));
        Assert.That(ImGui.FindRenderedTextEnd("A##B##C"), Is.EqualTo("A"));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.CalcTextSize(string, bool, float)"/> agrees with the current
    /// font's <c>CalcTextSizeA</c>, differing only by the canonical ceiling of the width. Canonical
    /// <c>CalcTextSize</c> delegates to <c>ImFont::CalcTextSizeA</c> and rounds the width up
    /// (, ).
    /// </summary>
    [Test]
    public void CalcTextSize_AgreesWithFontCalcTextSizeA()
    {
        Frame();

        const string text = "Hamburgefonstiv";
        ImFont font = _ctx.Font!;
        float fontSize = _ctx.FontSize;

        ImVec2 raw = font.CalcTextSizeA(fontSize, float.MaxValue, 0.0f, text.AsSpan());
        ImVec2 delegated = ImGui.CalcTextSizeA(fontSize, float.MaxValue, 0.0f, text.AsSpan());
        ImVec2 publicSize = ImGui.CalcTextSize(text, false, 0.0f);

        Assert.That(delegated.X, Is.EqualTo(raw.X).Within(1e-4f));
        Assert.That(delegated.Y, Is.EqualTo(raw.Y).Within(1e-4f));
        // The public helper ceils the width but preserves the height.
        Assert.That(publicSize.X, Is.EqualTo(MathF.Ceiling(raw.X)).Within(1e-4f));
        Assert.That(publicSize.Y, Is.EqualTo(raw.Y).Within(1e-4f));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SeparatorText"/> advances the cursor vertically by the label
    /// height plus the style padding (or the border size when larger) plus the item spacing, resets the
    /// horizontal cursor, and submits the item with identifier zero so it is not a navigation target.
    /// </summary>
    [Test]
    public void SeparatorText_AdvancesCursorAndUsesZeroId()
    {
        WindowFrame("Separator", new ImVec2(400.0f, 300.0f), () =>
        {
            float startY = ImGui.GetCursorPosY();
            float startX = ImGui.GetCursorPosX();
            ImVec2 labelSize = ImGui.CalcTextSize("Section", false);
            ImVec2 padding = _ctx.Style.SeparatorTextPadding;
            float minHeight = MathF.Max(labelSize.Y + padding.Y * 2.0f, _ctx.Style.SeparatorTextBorderSize);

            ImGui.SeparatorText("Section");

            Assert.That(ImGui.GetItemID(), Is.EqualTo(0u));
            Assert.That(ImGui.GetCursorPosY(), Is.EqualTo(startY + minHeight + _ctx.Style.ItemSpacing.Y).Within(1e-4f));
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(startX).Within(1e-4f));
        });
    }

    #endregion

    #region Low-level render helpers

    /// <summary>
    /// Verifies that <see cref="ImGui.RenderArrow"/> emits a single anti-aliased filled triangle:
    /// two vertices per point and three fan indices plus one fringe quad per edge, giving 6 vertices
    /// and 21 indices.
    /// </summary>
    [Test]
    public void RenderArrow_EmitsAntiAliasedTriangle()
    {
        ImFontAtlas atlas = new();
        atlas.Build();
        ImDrawList drawList = new(atlas);

        ImGui.RenderArrow(drawList, new ImVec2(10.0f, 10.0f), OpaqueWhite, ImGuiDir.Right);

        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(6));
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(21));
        Assert.That(drawList.CmdBuffer.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.RenderBullet"/> emits an eight-segment anti-aliased filled disc at
    /// the default font size: 16 vertices and 66 indices. Canonical selects eight segments below 22 px
    /// and uses a radius of one fifth of the font size
    /// </summary>
    [Test]
    public void RenderBullet_EmitsEightSegmentDisc()
    {
        Frame();
        ImFontAtlas atlas = new();
        atlas.Build();
        ImDrawList drawList = new(atlas);

        ImGui.RenderBullet(drawList, new ImVec2(20.0f, 20.0f), OpaqueWhite);

        Assert.That(_ctx.FontSize, Is.LessThan(22.0f));
        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(16));
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(66));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.RenderCheckMark"/> emits the canonical three-point anti-aliased
    /// stroke. A three-point open path yields two segments; the textured one-pixel path stores two
    /// vertices per point and six indices per segment, so 6 vertices and 12 indices
    /// </summary>
    [Test]
    public void RenderCheckMark_EmitsAntiAliasedStroke()
    {
        ImFontAtlas atlas = new();
        atlas.Build();
        ImDrawList drawList = new(atlas);

        ImGui.RenderCheckMark(drawList, new ImVec2(0.0f, 0.0f), OpaqueWhite, 20.0f);

        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(6));
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(12));
    }

    /// <summary>
    /// Verifies the frame geometry with and without a style border. The fill alone is one quad
    /// (4 vertices / 6 indices); a visible border rectangle is an anti-aliased four-point closed
    /// stroke (8 vertices / 24 indices). Canonical draws the shadow rectangle at the offset position
    /// and the border rectangle; the default <c>ImGuiCol_BorderShadow</c> color is fully transparent
    /// (0,0,0,0), so <c>AddRect</c>'s zero-alpha early-out drops it and only the border rectangle is
    /// emitted.
    /// </summary>
    [Test]
    public void RenderFrame_AndBorder_FollowFrameBorderSize()
    {
        (int fillVtx, int fillIdx) = MeasureWindowDraw(() =>
        {
            _ctx.Style.FrameBorderSize = 0.0f;
            ImGui.RenderFrame(new ImVec2(0.0f, 0.0f), new ImVec2(30.0f, 20.0f), OpaqueWhite, true);
        });
        Assert.That(fillVtx, Is.EqualTo(4));
        Assert.That(fillIdx, Is.EqualTo(6));

        (int borderedVtx, int borderedIdx) = MeasureWindowDraw(() =>
        {
            _ctx.Style.FrameBorderSize = 1.0f;
            ImGui.RenderFrame(new ImVec2(0.0f, 0.0f), new ImVec2(30.0f, 20.0f), OpaqueWhite, true);
        });
        Assert.That(borderedVtx, Is.EqualTo(4 + 8));
        Assert.That(borderedIdx, Is.EqualTo(6 + 24));

        (int borderOnlyVtx, int borderOnlyIdx) = MeasureWindowDraw(() =>
        {
            _ctx.Style.FrameBorderSize = 1.0f;
            ImGui.RenderFrameBorder(new ImVec2(0.0f, 0.0f), new ImVec2(30.0f, 20.0f));
        });
        Assert.That(borderOnlyVtx, Is.EqualTo(8));
        Assert.That(borderOnlyIdx, Is.EqualTo(24));
    }

    /// <summary>
    /// Verifies the alpha-checkerboard branch of
    /// <see cref="ImGui.RenderColorRectWithAlphaCheckerboard"/>. An opaque fill is a single quad; a
    /// translucent 20x20 fill with a 10 px grid emits the base quad plus two alternating cells (one
    /// per row, since the inner loop advances by two grid steps), each a sharp quad
    /// </summary>
    [Test]
    public void RenderColorRectWithAlphaCheckerboard_EmitsBaseAndCells()
    {
        (int opaqueVtx, int opaqueIdx) = MeasureWindowDraw(() =>
            ImGui.RenderColorRectWithAlphaCheckerboard(
                ImGui.GetCurrentContext()!.CurrentWindow!.DrawList,
                new ImVec2(0.0f, 0.0f),
                new ImVec2(20.0f, 20.0f),
                OpaqueWhite,
                10.0f,
                ImVec2.Zero));

        Assert.That(opaqueVtx, Is.EqualTo(4));
        Assert.That(opaqueIdx, Is.EqualTo(6));

        (int translucentVtx, int translucentIdx) = MeasureWindowDraw(() =>
            ImGui.RenderColorRectWithAlphaCheckerboard(
                ImGui.GetCurrentContext()!.CurrentWindow!.DrawList,
                new ImVec2(0.0f, 0.0f),
                new ImVec2(20.0f, 20.0f),
                0x80FFFFFFu,
                10.0f,
                ImVec2.Zero));

        // Base quad plus two checker cells: 3 * 4 vertices and 3 * 6 indices.
        Assert.That(translucentVtx, Is.EqualTo(3 * 4));
        Assert.That(translucentIdx, Is.EqualTo(3 * 6));
    }

    #endregion
}
