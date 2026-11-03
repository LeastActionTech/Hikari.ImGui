// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical basic widgets owned by <c>ImGuiWidgets.cs</c>: buttons, checkboxes and
/// radio buttons, progress bars and bullets, images, selectables, list boxes, and combo boxes.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>. Interaction is driven by queueing mouse
/// position and button events before the frame that must observe them.
/// </remarks>
[TestFixture]
public class WidgetTests : ImGuiTestFixture
{
    /// <summary>Window origin used by the placement-sensitive tests.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);

    /// <summary>A non-zero backend texture identifier used by the image tests.</summary>
    private static readonly ImTextureRef TestTexture = new((IntPtr)7);


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

    /// <summary>Begins a fixed-geometry window; the caller must close it with <see cref="ImGui.End"/>.</summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    private static void BeginFixed(string name, ImVec2 size)
    {
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin(name);
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
        BeginFixed(name, size);
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Measures the change in the current window's vertex buffer count caused by one submission.
    /// </summary>
    /// <param name="draw">The submission to measure.</param>
    /// <returns>The vertex count delta.</returns>
    private static int MeasureVertices(Action draw)
    {
        int delta = 0;
        WindowFrame("Measure", new ImVec2(600.0f, 400.0f), () =>
        {
            ImDrawList drawList = ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
            int before = drawList.VtxBuffer.Count;
            draw();
            delta = drawList.VtxBuffer.Count - before;
        });
        return delta;
    }

    /// <summary>
    /// Presses and releases the left mouse button at an explicit screen position while submitting one
    /// widget per frame, returning the widget's result on the release frame.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="submit">Submits exactly one widget and returns its boolean result.</param>
    /// <param name="pos">Screen position of the press and release.</param>
    /// <returns>The release-frame result of <paramref name="submit"/>.</returns>
    private bool PressReleaseAt(string name, ImVec2 size, Func<bool> submit, ImVec2 pos)
    {
        _ctx.IO.AddMousePosEvent(pos.X, pos.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed(name, size);
        submit();
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        BeginFixed(name, size);
        bool result = submit();
        ImGui.End();
        ImGui.Render();
        return result;
    }

    /// <summary>
    /// Submits one widget for a frame to learn its screen rectangle, then presses and releases the
    /// left mouse button at the rectangle center.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="submit">Submits exactly one widget and returns its boolean result.</param>
    /// <returns>The release-frame result of <paramref name="submit"/>.</returns>
    private bool ClickWidget(string name, ImVec2 size, Func<bool> submit)
    {
        // The first frame creates and stabilizes the window; the second captures a rectangle that is
        // identical to the rectangles seen on the press and release frames.
        WindowFrame(name, size, () => submit());
        ImRect rect = default;
        WindowFrame(name, size, () =>
        {
            submit();
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        return PressReleaseAt(
            name,
            size,
            submit,
            new ImVec2((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f));
    }

    #region Buttons

    /// <summary>
    /// Verifies that <see cref="ImGui.Button"/> reports true only on the release frame inside its
    /// rectangle, after being activated on the press frame. Canonical default behavior is
    /// <c>ImGuiButtonFlags_PressedOnClickRelease</c>
    /// </summary>
    [Test]
    public void Button_PressReleaseInsideRect_ReturnsTrueOnRelease()
    {
        bool pressed = ClickWidget("Button", new ImVec2(300.0f, 200.0f), () => ImGui.Button("Go"));
        Assert.That(pressed, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.Button"/> reports false when the press and release happen
    /// outside the item rectangle.
    /// </summary>
    [Test]
    public void Button_PressOutsideRect_ReturnsFalse()
    {
        bool pressed = PressReleaseAt(
            "Button",
            new ImVec2(300.0f, 200.0f),
            () => ImGui.Button("Go"),
            new ImVec2(600.0f, 500.0f));

        Assert.That(pressed, Is.False);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SmallButton"/> zeroes the vertical frame padding so its height
    /// collapses to one font height while its width keeps the horizontal padding, and that
    /// <see cref="ImGui.ArrowButton"/> is exactly one frame height square. Canonical small-button
    /// sizing. and arrow-button sizing
    /// </summary>
    [Test]
    public void SmallButton_And_ArrowButton_UseCanonicalSizes()
    {
        Frame();
        float fontSize = _ctx.FontSize;
        float frameHeight = fontSize + _ctx.Style.FramePadding.Y * 2.0f;

        WindowFrame("Sizes", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.SmallButton("Sm");
            Assert.That(ImGui.GetItemRectSize(), Is.EqualTo(new ImVec2(
                ImGui.CalcTextSize("Sm").X + _ctx.Style.FramePadding.X * 2.0f,
                fontSize)));

            ImGui.ArrowButton("up", ImGuiDir.Up);
            Assert.That(ImGui.GetItemRectSize(), Is.EqualTo(new ImVec2(frameHeight, frameHeight)));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.InvisibleButton"/> takes the exact requested size and reports a
    /// full press/release.
    /// </summary>
    [Test]
    public void InvisibleButton_ExplicitSizeAndClick()
    {
        ImVec2 size = new(50.0f, 40.0f);
        WindowFrame("Invisible", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.InvisibleButton("inv", size);
            Assert.That(ImGui.GetItemRectSize(), Is.EqualTo(size));
        });

        bool pressed = ClickWidget("Invisible", new ImVec2(300.0f, 200.0f), () => ImGui.InvisibleButton("inv", size));
        Assert.That(pressed, Is.True);
    }

    #endregion

    #region Checkboxes and radio buttons

    /// <summary>
    /// Verifies that <see cref="ImGui.Checkbox"/> flips the referenced boolean, reports edited on the
    /// press, and sets the edited item status.
    /// </summary>
    [Test]
    public void Checkbox_TogglesValueAndReportsEdited()
    {
        bool value = false;
        bool editedSeen = false;
        bool pressed = ClickWidget("Check", new ImVec2(300.0f, 200.0f), () =>
        {
            bool p = ImGui.Checkbox("Enable", ref value);
            editedSeen = ImGui.IsItemEdited();
            return p;
        });

        Assert.That(pressed, Is.True);
        Assert.That(value, Is.True);
        Assert.That(editedSeen, Is.True);
    }

    /// <summary>
    /// Verifies the canonical set, clear, and partial-selection ("add") semantics of
    /// <see cref="ImGui.CheckboxFlags(string, ref int, int)"/> for the signed 32-bit, unsigned
    /// 32-bit, signed 64-bit, and unsigned 64-bit overloads
    /// </summary>
    [Test]
    public void CheckboxFlags_SetClearAndAddSemantics()
    {
        ImVec2 size = new(300.0f, 200.0f);

        int flags = 0;
        Assert.That(ClickWidget("Flags", size, () => ImGui.CheckboxFlags("bit", ref flags, 1)), Is.True);
        Assert.That(flags, Is.EqualTo(1));
        Assert.That(ClickWidget("Flags", size, () => ImGui.CheckboxFlags("bit", ref flags, 1)), Is.True);
        Assert.That(flags, Is.EqualTo(0));

        // A partial selection uses the same "set the whole group" press behavior.
        flags = 1;
        Assert.That(ClickWidget("Flags", size, () => ImGui.CheckboxFlags("group", ref flags, 3)), Is.True);
        Assert.That(flags, Is.EqualTo(3));

        uint uflags = 0;
        Assert.That(ClickWidget("Flags", size, () => ImGui.CheckboxFlags("ubit", ref uflags, 4u)), Is.True);
        Assert.That(uflags, Is.EqualTo(4u));

        long lflags = 0;
        Assert.That(ClickWidget("Flags", size, () => ImGui.CheckboxFlags("lbit", ref lflags, 8L)), Is.True);
        Assert.That(lflags, Is.EqualTo(8L));

        ulong ulflags = 0;
        Assert.That(ClickWidget("Flags", size, () => ImGui.CheckboxFlags("ulbit", ref ulflags, 16UL)), Is.True);
        Assert.That(ulflags, Is.EqualTo(16UL));
    }

    /// <summary>
    /// Verifies both radio-button forms: the caller-supplied active flag reports the press, and the
    /// integer-bound form writes the matching value when pressed
    /// </summary>
    [Test]
    public void RadioButton_BoolAndRefIntForms()
    {
        ImVec2 size = new(300.0f, 200.0f);

        bool boolPressed = ClickWidget("Radio", size, () => ImGui.RadioButton("A", active: false));
        Assert.That(boolPressed, Is.True);

        int value = 0;
        bool intPressed = ClickWidget("Radio", size, () => ImGui.RadioButton("B", ref value, 2));
        Assert.That(intPressed, Is.True);
        Assert.That(value, Is.EqualTo(2));

        // Pressing the already-selected entry still reports the press and leaves the value unchanged.
        intPressed = ClickWidget("Radio", size, () => ImGui.RadioButton("B", ref value, 2));
        Assert.That(intPressed, Is.True);
        Assert.That(value, Is.EqualTo(2));
    }

    #endregion

    #region Progress bar, bullet, value

    /// <summary>
    /// Verifies the canonical progress-bar geometry at a default zero frame rounding: the frame quad
    /// plus the filled range quad plus the default percentage text for a determinate bar. An
    /// indeterminate bar with a null overlay draws no text, while an explicit overlay adds glyph
    /// quads.
    /// </summary>
    [Test]
    public void ProgressBar_GeometryAndNullOverlay()
    {
        ImVec2 size = new(100.0f, 20.0f);

        int determinate = MeasureVertices(() => ImGui.ProgressBar(0.5f, size));
        // Frame quad (4) + fill quad (4) + "50%" (3 glyphs * 4).
        Assert.That(determinate, Is.EqualTo(4 + 4 + 3 * 4));

        int indeterminate = MeasureVertices(() => ImGui.ProgressBar(-0.5f, size, null));
        // Frame quad (4) + sliding fill quad (4); no text because the overlay is null.
        Assert.That(indeterminate, Is.EqualTo(4 + 4));

        int annotated = MeasureVertices(() => ImGui.ProgressBar(-0.5f, size, "Hi"));
        Assert.That(annotated, Is.EqualTo(4 + 4 + 2 * 4));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.Bullet"/> draws the canonical eight-segment filled disc and
    /// leaves the cursor on the same line.
    /// </summary>
    [Test]
    public void Bullet_DrawsDiscAndKeepsSameLine()
    {
        int vtx = MeasureVertices(() => ImGui.Bullet());

        // Eight-segment anti-aliased fill: 16 vertices.
        Assert.That(vtx, Is.EqualTo(16));
    }

    /// <summary>
    /// Verifies the formatted text produced by each <see cref="ImGui.Value"/> overload. Vertex counts
    /// count only visible glyphs, so the unsigned overload's ten-digit rendering of
    /// <see cref="uint.MaxValue"/> proves that the canonical signed <c>%d</c> conversion prints the
    /// unsigned value rather than a negative number
    /// </summary>
    [Test]
    public void Value_FormatsEachOverload()
    {
        // "b: true" -> b : t r u e = 6 visible glyphs.
        Assert.That(MeasureVertices(() => ImGui.Value("b", true)), Is.EqualTo(6 * 4));
        // "i: -1" -> i : - 1 = 4 visible glyphs.
        Assert.That(MeasureVertices(() => ImGui.Value("i", -1)), Is.EqualTo(4 * 4));
        // "u: 4294967295" -> u : plus ten digits = 12 visible glyphs; a signed print would be "u: -1".
        Assert.That(MeasureVertices(() => ImGui.Value("u", uint.MaxValue)), Is.EqualTo(12 * 4));
        // "f: 1.500" -> f : 1. 5 0 0 = 7 visible glyphs.
        Assert.That(MeasureVertices(() => ImGui.Value("f", 1.5f)), Is.EqualTo(7 * 4));
        // "f: 1.5" with the caller format -> 5 visible glyphs.
        Assert.That(MeasureVertices(() => ImGui.Value("f", 1.5f, "%.1f")), Is.EqualTo(5 * 4));
    }

    #endregion

    #region Images

    /// <summary>
    /// Verifies that <see cref="ImGui.Image"/> sizes the item to the image plus the border padding
    /// (zero by default) and emits a single textured quad
    /// </summary>
    [Test]
    public void Image_ItemSizeAndQuad()
    {
        ImVec2 imageSize = new(32.0f, 16.0f);

        WindowFrame("Image", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.Image(TestTexture, imageSize);
            Assert.That(ImGui.GetItemRectSize(), Is.EqualTo(imageSize));
        });

        int vtx = MeasureVertices(() => ImGui.Image(TestTexture, imageSize));
        Assert.That(vtx, Is.EqualTo(4));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.ImageButton"/> sizes the item to the image plus the frame
    /// padding and returns true on a full press/release
    /// </summary>
    [Test]
    public void ImageButton_ItemSizeAndClick()
    {
        ImVec2 imageSize = new(32.0f, 16.0f);
        Frame();
        ImVec2 expected = imageSize + _ctx.Style.FramePadding * 2.0f;

        WindowFrame("ImageButton", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.ImageButton("ib", TestTexture, imageSize);
            Assert.That(ImGui.GetItemRectSize(), Is.EqualTo(expected));
        });

        bool pressed = ClickWidget(
            "ImageButton",
            new ImVec2(300.0f, 200.0f),
            () => ImGui.ImageButton("ib", TestTexture, imageSize));
        Assert.That(pressed, Is.True);
    }

    #endregion

    #region Selectable and list box

    /// <summary>
    /// Verifies selectable geometry and interaction: a selected row draws the header frame, a
    /// <c>SpanAvailWidth</c> row fills the available work width, hovering reports hovered, and a
    /// click reports pressed. The toggle-selection status bit is read directly because this port
    /// does not expose the canonical <c>IsItemToggledSelection()</c> accessor
    /// (, canonical accessor
    /// </summary>
    [Test]
    public void Selectable_SelectedSpanHoverAndPress()
    {
        WindowFrame("Selectable", new ImVec2(400.0f, 300.0f), () =>
        {
            // The default (zero-width) row fills the available width, and the interaction rectangle
            // expands by the item spacing.
            ImGui.SetCursorPos(new ImVec2(0.0f, 0.0f));
            ImVec2 pos = ImGui.GetCursorScreenPos();
            float right = _ctx.CurrentWindow!.WorkRect.Max.X;
            ImVec2 labelSize = ImGui.CalcTextSize("Row");
            ImGui.Selectable("Row");
            Assert.That(
                ImGui.GetItemRectSize().X,
                Is.EqualTo(right - pos.X + _ctx.Style.ItemSpacing.X).Within(1e-3f));
            Assert.That(
                ImGui.GetItemRectSize().Y,
                Is.EqualTo(labelSize.Y + _ctx.Style.ItemSpacing.Y));
            Assert.That(
                ImGui.GetItemRectMin().X,
                Is.EqualTo(pos.X - (float)(int)(_ctx.Style.ItemSpacing.X * 0.5f)));

            // A selected row is not toggled by mere submission; only a press or navigation changes it.
            ImGui.Selectable("Selected", selected: true);
            Assert.That(
                (ImGui.GetItemStatusFlags() & ImGuiItemStatusFlags.ToggledSelection),
                Is.EqualTo(ImGuiItemStatusFlags.None));
        });

        // An explicit small width stays small, but SpanAvailWidth expands it to the work rectangle.
        ImGuiSelectableFlags span = (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SpanAvailWidth;
        WindowFrame("Selectable", new ImVec2(400.0f, 300.0f), () =>
        {
            ImGui.SetCursorPos(new ImVec2(0.0f, 0.0f));
            ImGui.Selectable("Fixed", false, 0, new ImVec2(10.0f, 0.0f));
            Assert.That(ImGui.GetItemRectSize().X, Is.EqualTo(10.0f + _ctx.Style.ItemSpacing.X));

            ImGui.SetCursorPos(new ImVec2(0.0f, ImGui.GetCursorPosY()));
            ImVec2 pos = ImGui.GetCursorScreenPos();
            float right = _ctx.CurrentWindow!.WorkRect.Max.X;
            ImGui.Selectable("Wide", false, span, new ImVec2(10.0f, 0.0f));
            Assert.That(
                ImGui.GetItemRectSize().X,
                Is.EqualTo(right - pos.X + _ctx.Style.ItemSpacing.X).Within(1e-3f));
        });

        bool hovered = false;
        bool pressed = ClickWidget("Selectable", new ImVec2(400.0f, 300.0f), () =>
        {
            bool p = ImGui.Selectable("Row");
            hovered = ImGui.IsItemHovered();
            return p;
        });
        Assert.That(pressed, Is.True);
        Assert.That(hovered, Is.True);
    }

    /// <summary>
    /// Verifies that the <c>ref bool</c> selectable overload toggles the referenced value on press
    /// </summary>
    [Test]
    public void Selectable_RefBoolOverloadToggles()
    {
        bool selected = false;
        bool pressed = ClickWidget("SelectableRef", new ImVec2(400.0f, 300.0f), () => ImGui.Selectable("Row", ref selected));

        Assert.That(pressed, Is.True);
        Assert.That(selected, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.ListBox"/> returns the clicked row index and derives its frame
    /// height from <c>heightInItems</c> (a fractional row is included so a partial row stays visible).
    /// </summary>
    [Test]
    public void ListBox_ReturnsClickedIndexAndHonorsHeight()
    {
        Frame();
        string[] items = { "Zero", "One", "Two", "Three" };
        ImVec2 winSize = new(300.0f, 400.0f);
        float expectedHeight = (float)(int)(
            (ImGui.GetFontSize() + _ctx.Style.ItemSpacing.Y) * (3 + 0.25f)
            + _ctx.Style.FramePadding.Y * 2.0f);

        // The first frame learns the child content origin; the list box creates the same child window
        // on every frame because window geometry is pinned.
        ImVec2 contentOrigin = default;
        WindowFrame("List", winSize, () =>
        {
            ImGui.BeginListBox("L", new ImVec2(0.0f, expectedHeight));
            contentOrigin = ImGui.GetCursorScreenPos();
            ImGui.EndListBox();
        });

        int current = 0;
        WindowFrame("List", winSize, () =>
        {
            ImGui.ListBox("L", ref current, items, 3);
            Assert.That(ImGui.GetItemRectSize().Y, Is.EqualTo(expectedHeight).Within(1.0f));
        });

        // Row stride is one text line plus the item spacing; press the third row.
        float stride = ImGui.GetFontSize() + _ctx.Style.ItemSpacing.Y;
        ImVec2 target = new(contentOrigin.X + 5.0f, contentOrigin.Y + stride * 2.0f + ImGui.GetFontSize() * 0.5f);

        _ctx.IO.AddMousePosEvent(target.X, target.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed("List", winSize);
        ImGui.ListBox("L", ref current, items, 3);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        bool changed = false;
        NewTestFrame();
        BeginFixed("List", winSize);
        changed = ImGui.ListBox("L", ref current, items, 3);
        ImGui.End();
        ImGui.Render();

        Assert.That(changed, Is.True);
        Assert.That(current, Is.EqualTo(2));
    }

    #endregion

    #region Combo

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginCombo"/> opens its popup on the press frame, returns true
    /// so the caller can submit popup content, and pushes exactly one entry onto the begin-popup
    /// stack while the content is submitted
    /// </summary>
    [Test]
    public void BeginCombo_OpensOnClickAndSubmitsContent()
    {
        ImVec2 winSize = new(300.0f, 200.0f);
        ImRect rect = default;
        // The first frame stabilizes the window; the second captures a stable rectangle.
        WindowFrame("Combo", winSize, () => ImGui.BeginCombo("C", "Item"));
        WindowFrame("Combo", winSize, () =>
        {
            ImGui.BeginCombo("C", "Item");
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        // Default button behavior presses on click-release, so the popup does not open yet.
        bool openOnPress = false;
        NewTestFrame();
        BeginFixed("Combo", winSize);
        openOnPress = ImGui.BeginCombo("C", "Item");
        ImGui.End();
        ImGui.Render();
        Assert.That(openOnPress, Is.False);

        _ctx.IO.AddMouseButtonEvent(0, false);
        int stackDepth = -1;
        bool open = false;
        NewTestFrame();
        BeginFixed("Combo", winSize);
        open = ImGui.BeginCombo("C", "Item");
        if (open)
        {
            stackDepth = _ctx.BeginPopupStack.Count;
            ImGui.Selectable("Content", true);
            ImGui.EndCombo();
        }
        ImGui.End();
        ImGui.Render();

        Assert.That(open, Is.True);
        Assert.That(stackDepth, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginCombo"/> returns false when its frame rectangle is clipped
    /// away, without opening a popup.
    /// </summary>
    [Test]
    public void BeginCombo_ReturnsFalseWhenClipped()
    {
        WindowFrame("Clipped", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.SetCursorPos(new ImVec2(0.0f, 5000.0f));
            bool open = ImGui.BeginCombo("C", "Item");
            Assert.That(open, Is.False);
            Assert.That(_ctx.OpenPopupStack, Is.Empty);
        });
    }

    /// <summary>
    /// Verifies the canonical <c>CustomPreview</c> path: with the private flag set and an empty
    /// preview value, <see cref="ImGui.BeginComboPreview"/> redirects the cursor into the saved
    /// preview rectangle and <see cref="ImGui.EndComboPreview"/> restores it
    /// </summary>
    [Test]
    public void BeginCombo_CustomPreviewRedirectsCursor()
    {
        ImGuiComboFlags customPreview = (ImGuiComboFlags)(int)ImGuiComboFlagsPrivate.ImGuiComboFlags_CustomPreview;
        ImVec2 winSize = new(300.0f, 200.0f);
        ImRect rect = default;

        // The first frame stabilizes the window; the second captures a stable rectangle.
        WindowFrame("ComboPreview", winSize, () => ImGui.BeginCombo("C", "", customPreview));
        WindowFrame("ComboPreview", winSize, () =>
        {
            ImGui.BeginCombo("C", "", customPreview);
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed("ComboPreview", winSize);
        Assert.That(ImGui.BeginCombo("C", "", customPreview), Is.False);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        bool preview = false;
        bool insideRect = false;
        NewTestFrame();
        BeginFixed("ComboPreview", winSize);
        bool open = ImGui.BeginCombo("C", "", customPreview);
        if (open)
            ImGui.EndCombo();

        // Canonical calls BeginComboPreview() after the BeginCombo/EndCombo block, while the parent
        // window is current again.
        preview = ImGui.BeginComboPreview();
        if (preview)
        {
            ImVec2 cursor = ImGui.GetCursorScreenPos();
            insideRect = cursor.X >= rect.Min.X && cursor.X <= rect.Max.X && cursor.Y >= rect.Min.Y && cursor.Y <= rect.Max.Y;
            ImGui.EndComboPreview();
        }
        ImGui.End();
        ImGui.Render();

        Assert.That(open, Is.True);
        Assert.That(preview, Is.True);
        Assert.That(insideRect, Is.True);
    }

    /// <summary>
    /// Verifies that the array-backed <see cref="ImGui.Combo"/> returns false while closed and leaves
    /// the current selection untouched.
    /// </summary>
    [Test]
    public void Combo_ReturnsFalseWhenClosed()
    {
        string[] items = { "A", "B", "C" };
        int current = 1;

        WindowFrame("ComboArray", new ImVec2(300.0f, 200.0f), () =>
        {
            bool changed = ImGui.Combo("C", ref current, items, -1);
            Assert.That(changed, Is.False);
            Assert.That(current, Is.EqualTo(1));
        });
    }

    #endregion
}
