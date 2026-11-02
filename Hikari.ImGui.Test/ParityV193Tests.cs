#nullable enable
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Regression and parity test suite verifying Dear ImGui v1.93.0-docking features ported in
/// Chunks 1-4.
/// </summary>
[TestFixture]
public class ParityV193Tests : ImGuiTestFixture
{
    #region Version & Metadata Tests

    /// <summary>
    /// Verifies that <see cref="ImGui.GetVersion"/> and <see cref="ImGui.GetVersionNum"/> match
    /// canonical Dear ImGui v1.93.0 (<c>ThirdParty/imgui/imgui.h:23-24</c>).
    /// </summary>
    [Test]
    public void Version_MatchesCanonicalDearImGui193()
    {
        Assert.That(ImGui.GetVersion(), Is.EqualTo("1.93.0"));
        Assert.That(ImGui.ImGuiVersionNum, Is.EqualTo(19300));
    }

    #endregion

    #region Window & Child Flags Setup Tests

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextWindowFlags"/> applies flags to the immediately following
    /// <see cref="ImGui.Begin"/> and clears them for subsequent windows (<c>ThirdParty/imgui/imgui.cpp:9526</c>).
    /// </summary>
    [Test]
    public void WindowFlags_SetNextWindowFlags_AppliedAndCleared()
    {
        NewTestFrame();

        ImGui.SetNextWindowFlags(ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);
        ImGui.Begin("WindowWithNextFlags");
        ImGuiWindow current = ImGui.GetCurrentWindow();
        Assert.That((current.Flags & ImGuiWindowFlags.NoTitleBar) != 0, "NoTitleBar flag was not applied.");
        Assert.That((current.Flags & ImGuiWindowFlags.NoResize) != 0, "NoResize flag was not applied.");
        ImGui.End();

        // Second window without SetNextWindowFlags should not inherit the next-window flags.
        ImGui.Begin("WindowWithoutNextFlags");
        ImGuiWindow second = ImGui.GetCurrentWindow();
        Assert.That((second.Flags & ImGuiWindowFlags.NoTitleBar) == 0, "NoTitleBar flag leaked to subsequent window.");
        Assert.That((second.Flags & ImGuiWindowFlags.NoResize) == 0, "NoResize flag leaked to subsequent window.");
        ImGui.End();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextWindowChildFlags"/> applies child flags to the immediately following
    /// <see cref="ImGui.BeginChild(string, in ImVec2, ImGuiChildFlags, ImGuiWindowFlags)"/> and clears them afterward.
    /// </summary>
    [Test]
    public void ChildFlags_SetNextWindowChildFlags_AppliedAndCleared()
    {
        NewTestFrame();
        ImGui.Begin("ParentWindow");

        ImGui.SetNextWindowChildFlags(ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX);
        ImGui.BeginChild("ChildWithNextFlags", new ImVec2(100.0f, 100.0f));
        ImGuiWindow current = ImGui.GetCurrentWindow();
        Assert.That((current.ChildFlags & ImGuiChildFlags.Borders) != 0, "Borders child flag was not applied.");
        Assert.That((current.ChildFlags & ImGuiChildFlags.ResizeX) != 0, "ResizeX child flag was not applied.");
        ImGui.EndChild();

        // Second child without SetNextWindowChildFlags should not inherit next child flags.
        ImGui.BeginChild("ChildWithoutNextFlags", new ImVec2(100.0f, 100.0f));
        ImGuiWindow second = ImGui.GetCurrentWindow();
        Assert.That((second.ChildFlags & ImGuiChildFlags.Borders) == 0, "Borders child flag leaked.");
        Assert.That((second.ChildFlags & ImGuiChildFlags.ResizeX) == 0, "ResizeX child flag leaked.");
        ImGui.EndChild();

        ImGui.End();
    }

    #endregion

    #region Font Codepoint Remapping Tests

    /// <summary>
    /// Verifies that <see cref="ImFont.AddRemapCodepoint"/> and <see cref="ImFont.RemapCodepoint"/> correctly remap
    /// source codepoints (<c>ThirdParty/imgui/imgui_draw.cpp:6040-6079</c>).
    /// </summary>
    [Test]
    public void Font_AddRemapCodepoint_TranslatesCharacters()
    {
        ImFont font = _ctx.IO.Fonts.Fonts[0];

        font.AddRemapCodepoint((uint)'X', (uint)'A');
        Assert.That(font.RemapCodepoint((uint)'X'), Is.EqualTo((uint)'A'));
        Assert.That(font.RemapCodepoint((uint)'A'), Is.EqualTo((uint)'A'));

        // Obsolete wrapper parity
        font.AddRemapChar('Y', 'B');
        Assert.That(font.RemapCodepoint((uint)'Y'), Is.EqualTo((uint)'B'));
    }

    #endregion

    #region DrawList Flags & Stroke Architecture Tests

    /// <summary>
    /// Verifies that <see cref="ImDrawList.PushDrawFlag"/> and <see cref="ImDrawList.PopDrawFlag"/> properly scope
    /// rendering flags (<c>ThirdParty/imgui/imgui_draw.cpp:722-740</c>).
    /// </summary>
    [Test]
    public void DrawList_PushPopDrawFlag_Scoping()
    {
        ImFontAtlas atlas = new();
        atlas.Build();
        ImDrawList drawList = new(atlas);
        drawList.Clear(ImDrawFlags.AALines | ImDrawFlags.AAFill);

        Assert.That((drawList.Flags & ImDrawFlags.AALines) != 0);
        Assert.That((drawList.Flags & ImDrawFlags.AAFill) != 0);

        // Push disabling AALines
        drawList.PushDrawFlag(ImDrawFlags.AALines, enabled: false);
        Assert.That((drawList.Flags & ImDrawFlags.AALines) == 0);
        Assert.That((drawList.Flags & ImDrawFlags.AAFill) != 0);

        // Push disabling AAFill
        drawList.PushDrawFlag(ImDrawFlags.AAFill, enabled: false);
        Assert.That((drawList.Flags & ImDrawFlags.AALines) == 0);
        Assert.That((drawList.Flags & ImDrawFlags.AAFill) == 0);

        // Pop restores AAFill
        drawList.PopDrawFlag();
        Assert.That((drawList.Flags & ImDrawFlags.AALines) == 0);
        Assert.That((drawList.Flags & ImDrawFlags.AAFill) != 0);

        // Pop restores AALines
        drawList.PopDrawFlag();
        Assert.That((drawList.Flags & ImDrawFlags.AALines) != 0);
        Assert.That((drawList.Flags & ImDrawFlags.AAFill) != 0);
    }

    /// <summary>
    /// Verifies that whole-pixel horizontal and vertical lines in Dear ImGui v1.93 emit exact 4 vertices
    /// without anti-aliasing overhead (<c>ThirdParty/imgui/imgui_draw.cpp:2025-2100</c>).
    /// </summary>
    [Test]
    public void DrawList_AddLineHV_WholePixelFastPath()
    {
        ImDrawList drawList = new();
        drawList.Clear(ImDrawFlags.AALines);

        // Integer horizontal line
        drawList.AddLineH(10.0f, 100.0f, 20.0f, 0xFFFFFFFF, 1.0f);
        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(4), "Whole-pixel AddLineH must emit exactly 4 vertices.");
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(6), "Whole-pixel AddLineH must emit exactly 6 indices.");

        drawList.Clear(ImDrawFlags.AALines);

        // Integer vertical line
        drawList.AddLineV(20.0f, 10.0f, 100.0f, 0xFFFFFFFF, 1.0f);
        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(4), "Whole-pixel AddLineV must emit exactly 4 vertices.");
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(6), "Whole-pixel AddLineV must emit exactly 6 indices.");
    }

    #endregion

    #region Mixed Value Widget Tests

    /// <summary>
    /// Verifies that <see cref="ImGuiItemFlags.MixedValue"/> updates item flags for widgets and enables mixed
    /// state rendering (<c>ThirdParty/imgui/imgui_internal.h:1034</c>).
    /// </summary>
    [Test]
    public void Widgets_MixedValue_CheckboxAndSlider()
    {
        NewTestFrame();
        ImGui.Begin("MixedWidgetsWindow");

        ImGui.PushItemFlag(ImGuiItemFlags.MixedValue, true);

        bool check = false;
        ImGui.Checkbox("TestCheckbox", ref check);
        Assert.That((_ctx.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0,
            "MixedValue flag must be retained on Checkbox LastItemData.");

        float val = 50.0f;
        ImGui.SliderFloat("TestSlider", ref val, 0.0f, 100.0f);
        Assert.That((_ctx.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0,
            "MixedValue flag must be retained on SliderFloat LastItemData.");

        ImGui.PopItemFlag();

        // Next item without MixedValue flag
        bool normalCheck = false;
        ImGui.Checkbox("NormalCheckbox", ref normalCheck);
        Assert.That((_ctx.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) == 0,
            "MixedValue flag must not leak after PopItemFlag.");

        ImGui.End();
    }

    #endregion

    #region Text Filter Exclusion & Parsing Tests

    /// <summary>
    /// Verifies that <see cref="ImGuiTextFilter"/> evaluates exclusions first and properly handles quoted sequences
    /// matching Dear ImGui v1.93 (<c>ThirdParty/imgui/imgui.cpp:3235-3275</c>).
    /// </summary>
    [Test]
    public void TextFilter_ExclusionPrecedenceAndQuotes()
    {
        // Multi-term with inclusion and exclusion: exclusions always take precedence
        var filter = new ImGuiTextFilter("mesh -hidden");
        Assert.That(filter.PassFilter("mesh_visible"), Is.True);
        Assert.That(filter.PassFilter("mesh_hidden"), Is.False);
        Assert.That(filter.PassFilter("other_visible"), Is.False);

        // Quotes for exact match
        var filterQuotes = new ImGuiTextFilter("\"two words\" -excluded");
        Assert.That(filterQuotes.PassFilter("prefix two words suffix"), Is.True);
        Assert.That(filterQuotes.PassFilter("two words but excluded"), Is.False);
        Assert.That(filterQuotes.PassFilter("two words"), Is.True);
    }

    #endregion

    #region Table Lifecycle Tests

    /// <summary>
    /// Verifies that <see cref="ImGuiTable.IsNewTable"/> is true on the first frame a table is activated and false
    /// on subsequent frames (<c>ThirdParty/imgui/imgui_tables.cpp:386</c>).
    /// </summary>
    [Test]
    public void Table_IsNewTable_Lifecycle()
    {
        // Frame 1
        NewTestFrame();
        ImGui.Begin("TableLifecycleWindow");
        bool table1 = ImGui.BeginTable("LifecycleTable", 2);
        Assert.That(table1, Is.True);
        Assert.That(_ctx.CurrentTable, Is.Not.Null);
        Assert.That(_ctx.CurrentTable!.IsNewTable, Is.True, "Table must have IsNewTable == true on its first frame.");
        ImGui.EndTable();
        ImGui.End();

        // Frame 2
        NewTestFrame();
        ImGui.Begin("TableLifecycleWindow");
        bool table2 = ImGui.BeginTable("LifecycleTable", 2);
        Assert.That(table2, Is.True);
        Assert.That(_ctx.CurrentTable, Is.Not.Null);
        Assert.That(_ctx.CurrentTable!.IsNewTable, Is.False, "Table must have IsNewTable == false on subsequent frames.");
        ImGui.EndTable();
        ImGui.End();
    }

    /// <summary>
    /// Verifies that <see cref="ImDrawList.AddPolyline"/> honors stroke positioning modes (Inside vs Outside)
    /// and cap flags (CapSquare), shifting and expanding geometry by exact thickness amounts
    /// (<c>ThirdParty/imgui/imgui_draw.cpp:1030-1036, 1091-1095</c>).
    /// </summary>
    [Test]
    public void DrawList_Polyline_StrokePositioningAndCaps()
    {
        var atlas = new ImFontAtlas();
        atlas.Build();
        var drawList = new ImDrawList(atlas);
        drawList.Clear(ImDrawFlags.None); // No AA for exact vertex position checks

        Span<ImVec2> pts = stackalloc ImVec2[2] { new(10.0f, 10.0f), new(100.0f, 10.0f) };

        // 1. StrokeCenter: thickness 10.0f -> top Y is 5.0, bottom Y is 15.0
        drawList.Clear(ImDrawFlags.None);
        drawList.AddPolyline(pts, 0xFFFFFFFF, 10.0f, ImDrawFlags.StrokeCenter);
        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(drawList.VtxBuffer[0].Pos.Y, Is.EqualTo(5.0f));
        Assert.That(drawList.VtxBuffer[1].Pos.Y, Is.EqualTo(15.0f));

        // 2. StrokeInside: thickness 10.0f -> top Y is 10.0, bottom Y is 20.0
        drawList.Clear(ImDrawFlags.None);
        drawList.AddPolyline(pts, 0xFFFFFFFF, 10.0f, ImDrawFlags.StrokeInside);
        Assert.That(drawList.VtxBuffer[0].Pos.Y, Is.EqualTo(10.0f));
        Assert.That(drawList.VtxBuffer[1].Pos.Y, Is.EqualTo(20.0f));

        // 3. StrokeOutside: thickness 10.0f -> top Y is 0.0, bottom Y is 10.0
        drawList.Clear(ImDrawFlags.None);
        drawList.AddPolyline(pts, 0xFFFFFFFF, 10.0f, ImDrawFlags.StrokeOutside);
        Assert.That(drawList.VtxBuffer[0].Pos.Y, Is.EqualTo(0.0f));
        Assert.That(drawList.VtxBuffer[1].Pos.Y, Is.EqualTo(10.0f));

        // 4. CapSquare: extends start point backward by half thickness (-5.0) and end point forward (+5.0)
        drawList.Clear(ImDrawFlags.None);
        drawList.AddPolyline(pts, 0xFFFFFFFF, 10.0f, ImDrawFlags.StrokeCenter | ImDrawFlags.CapSquare);
        Assert.That(drawList.VtxBuffer[0].Pos.X, Is.EqualTo(5.0f));  // 10.0 - 5.0
        Assert.That(drawList.VtxBuffer[2].Pos.X, Is.EqualTo(105.0f)); // 100.0 + 5.0
    }

    [Test]
    public void GeometryAndMathHelpers_ParityVerification()
    {
        // 1. Bézier calculations
        ImVec2 bQuad = ImGui.ImBezierQuadraticCalc(new(0, 0), new(50, 100), new(100, 0), 0.5f);
        Assert.That(bQuad.X, Is.EqualTo(50.0f));
        Assert.That(bQuad.Y, Is.EqualTo(50.0f));

        ImVec2 bCubic = ImGui.ImBezierCubicCalc(new(0, 0), new(0, 100), new(100, 100), new(100, 0), 0.5f);
        Assert.That(bCubic.X, Is.EqualTo(50.0f));
        Assert.That(bCubic.Y, Is.EqualTo(75.0f));

        // 2. Line closest point
        ImVec2 ptOnLine = ImGui.ImLineClosestPoint(new(0, 0), new(100, 0), new(50, 50));
        Assert.That(ptOnLine.X, Is.EqualTo(50.0f));
        Assert.That(ptOnLine.Y, Is.EqualTo(0.0f));

        // 3. Triangle contains point
        Assert.That(ImGui.ImTriangleContainsPoint(new(0, 0), new(100, 0), new(50, 100), new(50, 50)), Is.True);
        Assert.That(ImGui.ImTriangleContainsPoint(new(0, 0), new(100, 0), new(50, 100), new(150, 50)), Is.False);

        // 4. Alpha blend colors
        uint blended = ImGui.ImAlphaBlendColors(0xFF000000, 0x80FFFFFF); // 50% white over black
        uint r = blended & 0xFF;
        Assert.That(r, Is.InRange(126u, 129u));

        // 5. Popup flags mouse button extraction
        Assert.That(ImGui.GetMouseButtonFromPopupFlags(ImGuiPopupFlags.MouseButtonLeft), Is.EqualTo(ImGuiMouseButton.Left));
        Assert.That(ImGui.GetMouseButtonFromPopupFlags(ImGuiPopupFlags.MouseButtonRight), Is.EqualTo(ImGuiMouseButton.Right));
        Assert.That(ImGui.GetMouseButtonFromPopupFlags(ImGuiPopupFlags.MouseButtonMiddle), Is.EqualTo(ImGuiMouseButton.Middle));
    }

    #endregion
}
