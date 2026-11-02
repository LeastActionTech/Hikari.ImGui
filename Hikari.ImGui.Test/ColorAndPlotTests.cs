#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical color widgets and helpers, the plot widgets, and the typing-select
/// helpers owned by <c>ImGuiWidgets.cs</c>: the packed-color and HSV conversions, the color button
/// and editor/picker items, plot scaling and hover sampling, and typing-select prefix matching.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>. The canonical reference is Dear ImGui
/// <c>v1.92.9b-docking</c>; every test cites the canonical file and line range that defines the
/// behavior it pins. Interaction is driven by queueing mouse position and button events before the
/// frame that must observe them.
/// </remarks>
[TestFixture]
public class ColorAndPlotTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);


    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
    }


    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="body">The widget submissions and assertions to run while the window is current.</param>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    private static void WindowFrame(Action body, float width = 600.0f, float height = 400.0f)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(width, height), ImGuiCond.Always);
        ImGui.Begin("Window");
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>Runs one frame with an empty fixed-geometry window so layout state is stable.</summary>
    private static void WarmupFrame()
    {
        WindowFrame(() => { });
    }

    #region Color conversion

    /// <summary>
    /// Verifies that normalized components pack into the canonical <c>IM_COL32</c> byte order with
    /// red least significant and alpha most significant. Canonical
    /// <c>ColorConvertFloat4ToU32</c> (<c>ThirdParty/imgui/imgui.cpp:2945-2955</c>).
    /// </summary>
    [Test]
    public void ColorConvertFloat4ToU32_PacksCanonicalByteOrder()
    {
        Assert.That(ImGui.ColorConvertFloat4ToU32(new ImVec4(1.0f, 0.0f, 0.0f, 1.0f)), Is.EqualTo(0xFF0000FFu));
        Assert.That(ImGui.ColorConvertFloat4ToU32(new ImVec4(0.0f, 1.0f, 0.0f, 1.0f)), Is.EqualTo(0xFF00FF00u));
        Assert.That(ImGui.ColorConvertFloat4ToU32(new ImVec4(0.0f, 0.0f, 1.0f, 0.0f)), Is.EqualTo(0x00FF0000u));
    }

    /// <summary>
    /// Verifies that unpacking a packed color reproduces the byte fractions and that packing the
    /// result returns the original value. Canonical <c>ColorConvertU32ToFloat4</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:2935-2943</c>).
    /// </summary>
    [Test]
    public void ColorConvertU32ToFloat4_RoundTrips()
    {
        const uint packed = 0xFF336699u;
        ImVec4 col = ImGui.ColorConvertU32ToFloat4(packed);

        Assert.That(col.X, Is.EqualTo(0x99 / 255.0f).Within(0.0001f));
        Assert.That(col.Y, Is.EqualTo(0x66 / 255.0f).Within(0.0001f));
        Assert.That(col.Z, Is.EqualTo(0x33 / 255.0f).Within(0.0001f));
        Assert.That(col.W, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(ImGui.ColorConvertFloat4ToU32(col), Is.EqualTo(packed));
    }

    /// <summary>
    /// Verifies the canonical RGB-to-HSV branch network for the three primaries: red is hue 0,
    /// green is hue one third, and blue is hue two thirds, all fully saturated and valued. Canonical
    /// <c>ColorConvertRGBtoHSV</c> (<c>ThirdParty/imgui/imgui.cpp:2957-2977</c>).
    /// </summary>
    [Test]
    public void ColorConvertRGBtoHSV_Primaries()
    {
        ImGui.ColorConvertRGBtoHSV(1.0f, 0.0f, 0.0f, out float h, out float s, out float v);
        Assert.That(h, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(s, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(v, Is.EqualTo(1.0f).Within(0.0001f));

        ImGui.ColorConvertRGBtoHSV(0.0f, 1.0f, 0.0f, out h, out s, out v);
        Assert.That(h, Is.EqualTo(1.0f / 3.0f).Within(0.0001f));
        Assert.That(s, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(v, Is.EqualTo(1.0f).Within(0.0001f));

        ImGui.ColorConvertRGBtoHSV(0.0f, 0.0f, 1.0f, out h, out s, out v);
        Assert.That(h, Is.EqualTo(2.0f / 3.0f).Within(0.0001f));
        Assert.That(s, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(v, Is.EqualTo(1.0f).Within(0.0001f));
    }

    /// <summary>
    /// Verifies the canonical HSV-to-RGB six-sector branch structure for the three primaries.
    /// Canonical <c>ColorConvertHSVtoRGB</c> (<c>ThirdParty/imgui/imgui.cpp:2979-3000</c>).
    /// </summary>
    [Test]
    public void ColorConvertHSVtoRGB_Primaries()
    {
        ImGui.ColorConvertHSVtoRGB(0.0f, 1.0f, 1.0f, out float r, out float g, out float b);
        Assert.That(r, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(g, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(b, Is.EqualTo(0.0f).Within(0.0001f));

        ImGui.ColorConvertHSVtoRGB(1.0f / 3.0f, 1.0f, 1.0f, out r, out g, out b);
        Assert.That(r, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(g, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(b, Is.EqualTo(0.0f).Within(0.0001f));

        ImGui.ColorConvertHSVtoRGB(2.0f / 3.0f, 1.0f, 1.0f, out r, out g, out b);
        Assert.That(r, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(g, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(b, Is.EqualTo(1.0f).Within(0.0001f));
    }

    /// <summary>
    /// Verifies that a fully desaturated hue collapses to the grey <c>(v, v, v)</c> result before
    /// any sector arithmetic runs. Canonical grey shortcut
    /// (<c>ThirdParty/imgui/imgui.cpp:2979-3000</c>).
    /// </summary>
    [Test]
    public void ColorConvertHSVtoRGB_ZeroSaturationIsGrey()
    {
        ImGui.ColorConvertHSVtoRGB(0.5f, 0.0f, 0.4f, out float r, out float g, out float b);

        Assert.That(r, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(g, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(b, Is.EqualTo(0.4f).Within(0.0001f));
    }

    #endregion

    #region Color button and editors

    /// <summary>
    /// Verifies that <see cref="ImGui.ColorButton"/> consumes exactly the requested size and reports
    /// a press only for a click inside that rectangle. Canonical layout and behavior
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6500-6560</c>).
    /// </summary>
    [Test]
    public void ColorButton_ItemRectMatchesRequestedSizeAndPresses()
    {
        WarmupFrame();
        ImRect rect = default;
        WindowFrame(() =>
        {
            ImGui.ColorButton("##c", new ImVec4(1.0f, 0.0f, 0.0f, 1.0f), 0, new ImVec2(30.0f, 20.0f));
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        Assert.That(rect.Max.X - rect.Min.X, Is.EqualTo(30.0f).Within(0.01f));
        Assert.That(rect.Max.Y - rect.Min.Y, Is.EqualTo(20.0f).Within(0.01f));

        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        bool pressed = false;
        WindowFrame(() => pressed = ImGui.ColorButton("##c", new ImVec4(1.0f, 0.0f, 0.0f, 1.0f), 0, new ImVec2(30.0f, 20.0f)));
        Assert.That(pressed, Is.False);

        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(() => pressed = ImGui.ColorButton("##c", new ImVec4(1.0f, 0.0f, 0.0f, 1.0f), 0, new ImVec2(30.0f, 20.0f)));
        Assert.That(pressed, Is.True);
    }

    /// <summary>
    /// Verifies that an untouched <see cref="ImGui.ColorEdit4"/> with no component inputs reports
    /// false and leaves the caller's color untouched, while still emitting its preview geometry.
    /// Canonical <c>ColorEdit4</c> only writes the caller's buffer inside its
    /// <c>value_changed</c> branch (<c>ThirdParty/imgui/imgui_widgets.cpp:5845-6096</c>).
    /// </summary>
    [Test]
    public void ColorEdit4_NoInputsUntouchedReturnsFalseAndLeavesColor()
    {
        WarmupFrame();
        float[] col = { 0.25f, 0.5f, 0.75f, 1.0f };
        bool changed = true;
        int vertices = 0;
        WindowFrame(() =>
        {
            ImDrawList drawList = ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
            int before = drawList.VtxBuffer.Count;
            changed = ImGui.ColorEdit4("##ce", col, ImGuiColorEditFlags.NoInputs);
            vertices = drawList.VtxBuffer.Count - before;
        });

        Assert.That(changed, Is.False);
        Assert.That(col, Is.EqualTo(new[] { 0.25f, 0.5f, 0.75f, 1.0f }));
        Assert.That(vertices, Is.GreaterThan(0));
    }

    /// <summary>
    /// Verifies that an untouched <see cref="ImGui.ColorPicker4"/> reports false, preserves the
    /// caller's color, and emits the wheel and field geometry. Canonical <c>ColorPicker4</c>
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6121-6444</c>).
    /// </summary>
    [Test]
    public void ColorPicker4_UntouchedReturnsFalseAndEmitsGeometry()
    {
        WarmupFrame();
        float[] col = { 0.2f, 0.4f, 0.6f, 1.0f };
        bool changed = true;
        int vertices = 0;
        WindowFrame(
            () =>
            {
                ImDrawList drawList = ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
                int before = drawList.VtxBuffer.Count;
                changed = ImGui.ColorPicker4("##cp", col);
                vertices = drawList.VtxBuffer.Count - before;
            },
            900.0f,
            800.0f);

        Assert.That(changed, Is.False);
        Assert.That(col, Is.EqualTo(new[] { 0.2f, 0.4f, 0.6f, 1.0f }));
        Assert.That(vertices, Is.GreaterThan(100));
    }

    #endregion

    #region Plots

    /// <summary>
    /// Verifies that hovering the middle of a line plot reports the sample index implied by the
    /// normalized cursor position and requests the canonical two-sample tooltip. Canonical plot core
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8955-9071</c>); with five samples the item count is
    /// four, so the midpoint maps to index two.
    /// </summary>
    [Test]
    public void PlotEx_LinesHoverReportsSampleIndexAndTooltip()
    {
        WarmupFrame();
        float[] values = { 0.0f, 1.0f, 2.0f, 3.0f, 4.0f };
        ImRect graph = default;
        WindowFrame(() =>
        {
            ImGui.PlotLines("##pl", values, values.Length, 0, null, 0.0f, 4.0f, new ImVec2(200.0f, 100.0f));
            graph = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        float padX = _ctx.Style.FramePadding.X;
        float padY = _ctx.Style.FramePadding.Y;
        ImVec2 innerMin = new(graph.Min.X + padX, graph.Min.Y + padY);
        ImVec2 innerMax = new(graph.Max.X - padX, graph.Max.Y - padY);
        _ctx.IO.AddMousePosEvent(
            innerMin.X + ((innerMax.X - innerMin.X) * 0.5f),
            innerMin.Y + ((innerMax.Y - innerMin.Y) * 0.5f));

        int hovered = -1;
        WindowFrame(() =>
        {
            hovered = ImGui.PlotEx(
                ImGuiPlotType.Lines, "##pl", (data, i) => values[i], null, values.Length, 0, null, 0.0f, 4.0f,
                new ImVec2(200.0f, 100.0f));
        });

        Assert.That(hovered, Is.EqualTo(2));
        Assert.That(_ctx.TooltipPreviousWindow, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that hovering the middle of a histogram reports the sample index implied by the
    /// normalized cursor position and requests the canonical single-sample tooltip. Canonical plot
    /// core (<c>ThirdParty/imgui/imgui_widgets.cpp:8955-9071</c>); with five samples the midpoint
    /// maps to index two.
    /// </summary>
    [Test]
    public void PlotEx_HistogramHoverReportsSampleIndex()
    {
        WarmupFrame();
        float[] values = { 0.0f, 1.0f, 2.0f, 3.0f, 4.0f };
        ImRect graph = default;
        WindowFrame(() =>
        {
            ImGui.PlotHistogram("##ph", values, values.Length, 0, null, 0.0f, 4.0f, new ImVec2(200.0f, 100.0f));
            graph = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        float padX = _ctx.Style.FramePadding.X;
        float padY = _ctx.Style.FramePadding.Y;
        ImVec2 innerMin = new(graph.Min.X + padX, graph.Min.Y + padY);
        ImVec2 innerMax = new(graph.Max.X - padX, graph.Max.Y - padY);
        _ctx.IO.AddMousePosEvent(
            innerMin.X + ((innerMax.X - innerMin.X) * 0.5f),
            innerMin.Y + ((innerMax.Y - innerMin.Y) * 0.5f));

        int hovered = -1;
        WindowFrame(() =>
        {
            hovered = ImGui.PlotEx(
                ImGuiPlotType.Histogram, "##ph", (data, i) => values[i], null, values.Length, 0, null, 0.0f, 4.0f,
                new ImVec2(200.0f, 100.0f));
        });

        Assert.That(hovered, Is.EqualTo(2));
        Assert.That(_ctx.TooltipPreviousWindow, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that both public plot overloads consume exactly the requested graph size and emit
    /// geometry. Canonical plot sizing and geometry
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8955-9071</c>, wrappers at <c>9088-9104</c>).
    /// </summary>
    [Test]
    public void PlotLines_And_PlotHistogram_UseRequestedGraphSize()
    {
        WarmupFrame();
        float[] values = { 0.0f, 1.0f, 2.0f, 3.0f, 4.0f };
        ImRect lineRect = default;
        int lineVertices = 0;
        WindowFrame(() =>
        {
            ImDrawList drawList = ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
            int before = drawList.VtxBuffer.Count;
            ImGui.PlotLines("##pl", values, values.Length, 0, null, 0.0f, 4.0f, new ImVec2(200.0f, 100.0f));
            lineVertices = drawList.VtxBuffer.Count - before;
            lineRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        Assert.That(lineRect.Max.X - lineRect.Min.X, Is.EqualTo(200.0f).Within(0.01f));
        Assert.That(lineRect.Max.Y - lineRect.Min.Y, Is.EqualTo(100.0f).Within(0.01f));
        Assert.That(lineVertices, Is.GreaterThan(0));

        ImRect histRect = default;
        int histVertices = 0;
        WindowFrame(() =>
        {
            ImDrawList drawList = ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
            int before = drawList.VtxBuffer.Count;
            ImGui.PlotHistogram("##ph", values, values.Length, 0, null, 0.0f, 4.0f, new ImVec2(200.0f, 100.0f));
            histVertices = drawList.VtxBuffer.Count - before;
            histRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        Assert.That(histRect.Max.X - histRect.Min.X, Is.EqualTo(200.0f).Within(0.01f));
        Assert.That(histRect.Max.Y - histRect.Min.Y, Is.EqualTo(100.0f).Within(0.01f));
        Assert.That(histVertices, Is.GreaterThan(0));
    }

    #endregion

    #region Typing select

    /// <summary>
    /// Verifies that queued characters are collected into a typing-select request with a select
    /// request latched for the current frame. Canonical <c>GetTypingSelectRequest</c>
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7672-7711</c>).
    /// </summary>
    [Test]
    public void TypingSelect_CollectsTypedPrefix()
    {
        WarmupFrame();
        _ctx.IO.AddInputCharacter('b');
        _ctx.IO.AddInputCharacter('a');

        ImGuiTypingSelectRequest? request = null;
        WindowFrame(() =>
        {
            request = ImGui.GetTypingSelectRequest(0);
        });

        Assert.That(request, Is.Not.Null);
        Assert.That(request!.SearchBuffer, Is.EqualTo("ba"));
        Assert.That(request.SearchBufferLen, Is.EqualTo(2));
        Assert.That(request.SelectRequest, Is.True);
    }

    /// <summary>
    /// Verifies that the longest case-insensitive leading match wins and that the search stops as
    /// soon as the whole search buffer matches. Canonical <c>TypingSelectFindBestLeadingMatch</c>
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7767-7783</c>) and <c>ImStrimmatchlen</c>
    /// (<c>7713-7719</c>).
    /// </summary>
    [Test]
    public void TypingSelectFindBestLeadingMatch_ReturnsLongestPrefix()
    {
        string[] items = { "alpha", "beta", "barn", "gamma" };
        ImGuiTypingSelectRequest request = new()
        {
            SearchBuffer = "ba",
            SearchBufferLen = 2,
            SelectRequest = true,
        };

        int match = ImGui.TypingSelectFindBestLeadingMatch(request, items.Length, (data, i) => items[i], null);

        Assert.That(match, Is.EqualTo(2));
    }

    #endregion
}
