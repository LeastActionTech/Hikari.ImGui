// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical layout and item primitives: cursor movement, item sizing and clipping,
/// item state transitions, item flags, next-item data, visibility tests, and scrolling.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>. Widgets themselves arrive in Chunk 4, so interaction is exercised through
/// <see cref="ImGui.ButtonBehavior"/> directly.
/// </remarks>
[TestFixture]
public class LayoutAndItemTests : ImGuiTestFixture
{
    /// <summary>Window origin used by the placement-sensitive tests.</summary>
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


    /// <summary>Starts and finishes one full frame so queued input is applied and retired.</summary>
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

    /// <summary>Submits <paramref name="count"/> layout-only items tall enough to overflow a window.</summary>
    /// <param name="count">Number of items to submit.</param>
    private static void FillScrollableContent(int count)
    {
        for (int i = 0; i < count; i++)
            ImGui.Dummy(new ImVec2(50.0f, 20.0f));
    }

    #region Cursor

    /// <summary>
    /// Verifies that <see cref="ImGui.SetCursorPos"/> and <see cref="ImGui.GetCursorPos"/> round-trip
    /// in window-local space, and that <see cref="ImGui.SetCursorScreenPos"/> and
    /// <see cref="ImGui.GetCursorScreenPos"/> round-trip in screen space.
    /// </summary>
    [Test]
    public void Cursor_SetAndGet_RoundTripsInBothSpaces()
    {
        WindowFrame("Cursor", new ImVec2(400.0f, 300.0f), () =>
        {
            ImGui.SetCursorPos(new ImVec2(25.0f, 35.0f));
            Assert.That(ImGui.GetCursorPos(), Is.EqualTo(new ImVec2(25.0f, 35.0f)));
            Assert.That(ImGui.GetCursorScreenPos(), Is.EqualTo(new ImVec2(125.0f, 135.0f)));
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(25.0f));
            Assert.That(ImGui.GetCursorPosY(), Is.EqualTo(35.0f));

            ImGui.SetCursorScreenPos(new ImVec2(200.0f, 220.0f));
            Assert.That(ImGui.GetCursorScreenPos(), Is.EqualTo(new ImVec2(200.0f, 220.0f)));
            Assert.That(ImGui.GetCursorPos(), Is.EqualTo(new ImVec2(100.0f, 120.0f)));
            ImGui.Dummy(new ImVec2(0.0f, 0.0f));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.GetCursorStartPos"/> reports the content origin in
    /// window-local coordinates and is unaffected by later cursor moves.
    /// </summary>
    [Test]
    public void Cursor_GetCursorStartPos_IsContentOrigin()
    {
        WindowFrame("Origin", new ImVec2(400.0f, 300.0f), () =>
        {
            ImVec2 start = ImGui.GetCursorStartPos();
            // Immediately after Begin the cursor sits at the content origin.
            Assert.That(start, Is.EqualTo(ImGui.GetCursorScreenPos() - ImGui.GetWindowPos()));

            ImGui.SetCursorPos(new ImVec2(60.0f, 70.0f));
            Assert.That(ImGui.GetCursorStartPos(), Is.EqualTo(start));
            ImGui.Dummy(new ImVec2(0.0f, 0.0f));
        });
    }

    /// <summary>
    /// Verifies the canonical <see cref="ImGui.NewLine"/> and <see cref="ImGui.Spacing"/> cursor
    /// advances.
    /// </summary>
    [Test]
    public void Cursor_NewLineAndSpacing_AdvanceByCanonicalAmounts()
    {
        WindowFrame("Lines", new ImVec2(400.0f, 300.0f), () =>
        {
            float startY = ImGui.GetCursorPosY();
            float startX = ImGui.GetCursorPosX();

            ImGui.Spacing();
            Assert.That(ImGui.GetCursorPosY(), Is.EqualTo(startY + _ctx.Style.ItemSpacing.Y));
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(startX));
        });

        WindowFrame("Lines", new ImVec2(400.0f, 300.0f), () =>
        {
            float startY = ImGui.GetCursorPosY();

            ImGui.NewLine();
            Assert.That(ImGui.GetCursorPosY(), Is.EqualTo(startY + ImGui.GetFontSize() + _ctx.Style.ItemSpacing.Y));
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(ImGui.GetCursorStartPos().X));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.Indent"/> and <see cref="ImGui.Unindent"/> shift the cursor to
    /// the indented content edge.
    /// </summary>
    [Test]
    public void Cursor_IndentAndUnindent_ShiftToContentEdge()
    {
        WindowFrame("Indent", new ImVec2(400.0f, 300.0f), () =>
        {
            // Canonical Begin() seeds DC.Indent.x with the decoration plus window padding, so the
            // indented content edge is measured relative to the content origin.
            float startX = ImGui.GetCursorStartPos().X;
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(startX));

            ImGui.Indent();
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(startX + _ctx.Style.IndentSpacing));

            ImGui.Unindent();
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(startX));

            ImGui.Indent(10.0f);
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(startX + 10.0f));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.AlignTextToFramePadding"/> makes the next
    /// <see cref="ImGui.NewLine"/> reserve the framed height.
    /// </summary>
    [Test]
    public void Cursor_AlignTextToFramePadding_GrowsLineHeight()
    {
        WindowFrame("Align", new ImVec2(400.0f, 300.0f), () =>
        {
            float startY = ImGui.GetCursorPosY();

            ImGui.AlignTextToFramePadding();
            ImGui.NewLine();

            float expected = startY
                + ImGui.GetFontSize()
                + _ctx.Style.FramePadding.Y * 2.0f
                + _ctx.Style.ItemSpacing.Y;
            Assert.That(ImGui.GetCursorPosY(), Is.EqualTo(expected));
        });
    }

    #endregion

    #region Item sizing and layout

    /// <summary>
    /// Verifies the canonical <see cref="ImGui.ItemSize(ImVec2, float)"/> and
    /// <see cref="ImGui.ItemAdd"/> arithmetic, including the item rectangle and the vertical and
    /// same-line cursor advances.
    /// </summary>
    [Test]
    public void Item_DummyAndSameLine_AdvanceCursorCanonically()
    {
        WindowFrame("Item", new ImVec2(400.0f, 300.0f), () =>
        {
            float indentX = ImGui.GetCursorStartPos().X;

            ImGui.SetCursorPos(new ImVec2(0.0f, 0.0f));
            ImGui.Dummy(new ImVec2(100.0f, 20.0f));

            Assert.That(ImGui.GetItemRectMin(), Is.EqualTo(new ImVec2(100.0f, 100.0f)));
            Assert.That(ImGui.GetItemRectMax(), Is.EqualTo(new ImVec2(200.0f, 120.0f)));
            Assert.That(ImGui.GetItemRectSize(), Is.EqualTo(new ImVec2(100.0f, 20.0f)));

            // The cursor resets to the content left edge and advances one line plus item spacing.
            Assert.That(ImGui.GetCursorPos(), Is.EqualTo(new ImVec2(indentX, 20.0f + _ctx.Style.ItemSpacing.Y)));

            ImGui.SameLine();

            // SameLine resumes after the previous item plus the horizontal item spacing, on its top line.
            Assert.That(
                ImGui.GetCursorPos(),
                Is.EqualTo(new ImVec2(100.0f + _ctx.Style.ItemSpacing.X, 0.0f)));
        });
    }

    /// <summary>
    /// Verifies the canonical <see cref="ImGui.CalcItemSize"/> sentinel semantics: zero substitutes
    /// the default, negative values shrink the available region with a floor of four pixels, and
    /// explicit values pass through.
    /// </summary>
    [Test]
    public void Item_CalcItemSize_HandlesSentinels()
    {
        WindowFrame("CalcSize", new ImVec2(400.0f, 300.0f), () =>
        {
            ImGui.SetCursorPos(new ImVec2(0.0f, 0.0f));
            ImVec2 avail = ImGui.GetContentRegionAvail();
            Assert.That(avail.X, Is.GreaterThan(4.0f));

            Assert.That(
                ImGui.CalcItemSize(new ImVec2(-1.0f, 0.0f), 100.0f, 50.0f),
                Is.EqualTo(new ImVec2(avail.X - 1.0f, 50.0f)));

            // -float.Epsilon is negative, so it selects the available-region branch without shrinking it.
            ImVec2 minusEpsilon = ImGui.CalcItemSize(new ImVec2(-float.Epsilon, 0.0f), 100.0f, 50.0f);
            Assert.That(minusEpsilon.X, Is.EqualTo(avail.X).Within(1e-3f));
            Assert.That(minusEpsilon.Y, Is.EqualTo(50.0f));

            // Negative zero compares equal to zero, so it substitutes the default width.
            Assert.That(
                ImGui.CalcItemSize(new ImVec2(-0.0f, 0.0f), 100.0f, 50.0f),
                Is.EqualTo(new ImVec2(100.0f, 50.0f)));

            Assert.That(
                ImGui.CalcItemSize(new ImVec2(123.0f, 45.0f), 100.0f, 50.0f),
                Is.EqualTo(new ImVec2(123.0f, 45.0f)));

            // The negative branch never returns less than four pixels.
            Assert.That(
                ImGui.CalcItemSize(new ImVec2(-100000.0f, 0.0f), 100.0f, 50.0f).X,
                Is.EqualTo(4.0f));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.CalcItemWidth"/> follows
    /// <see cref="ImGui.PushItemWidth"/>/<see cref="ImGui.PopItemWidth"/> and the queued
    /// <see cref="ImGui.SetNextItemWidth"/> override.
    /// </summary>
    [Test]
    public void Item_CalcItemWidth_FollowsStackAndNextItemOverride()
    {
        WindowFrame("CalcWidth", new ImVec2(400.0f, 300.0f), () =>
        {
            float defaultWidth = ImGui.CalcItemWidth();
            Assert.That(defaultWidth, Is.GreaterThan(0.0f));

            ImGui.SetNextItemWidth(100.0f);
            Assert.That(ImGui.CalcItemWidth(), Is.EqualTo(100.0f));
            // ItemAdd consumes the queued width so the following stack push is observed cleanly.
            ImGui.Dummy(new ImVec2(10.0f, 10.0f));

            ImGui.PushItemWidth(50.0f);
            Assert.That(ImGui.CalcItemWidth(), Is.EqualTo(50.0f));

            ImGui.SetNextItemWidth(120.0f);
            Assert.That(ImGui.CalcItemWidth(), Is.EqualTo(120.0f));
            ImGui.Dummy(new ImVec2(10.0f, 10.0f));

            ImGui.PopItemWidth();
            Assert.That(ImGui.CalcItemWidth(), Is.EqualTo(defaultWidth));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.ItemAdd"/> rejects an item entirely outside the window clip
    /// rectangle, that <see cref="ImGui.IsItemVisible"/> then reports false, and that the rejection
    /// does not advance <c>CursorMaxPos</c>.
    /// </summary>
    [Test]
    public void Item_ItemAdd_ClipRejectedItemDoesNotAdvanceCursorMaxPos()
    {
        WindowFrame("Clip", new ImVec2(200.0f, 200.0f), () =>
        {
            ImVec2 maxBefore = _ctx.CurrentWindow!.DC.CursorMaxPos;
            uint id = ImGui.GetID("offscreen");
            ImRect bb = new(new ImVec2(10000.0f, 10000.0f), new ImVec2(10020.0f, 10020.0f));

            bool added = ImGui.ItemAdd(bb, id);

            Assert.That(added, Is.False);
            Assert.That(ImGui.IsItemVisible(), Is.False);
            Assert.That(_ctx.CurrentWindow!.DC.CursorMaxPos, Is.EqualTo(maxBefore));
        });
    }

    /// <summary>
    /// Verifies <see cref="ImGui.IsRectVisible"/> for rectangles inside and outside the window clip
    /// rectangle.
    /// </summary>
    [Test]
    public void Item_IsRectVisible_SeparatesInsideFromOutside()
    {
        WindowFrame("Visible", new ImVec2(200.0f, 200.0f), () =>
        {
            Assert.That(ImGui.IsRectVisible(new ImVec2(110.0f, 110.0f), new ImVec2(150.0f, 150.0f)), Is.True);
            Assert.That(ImGui.IsRectVisible(new ImVec2(10000.0f, 10000.0f), new ImVec2(10020.0f, 10020.0f)), Is.False);

            ImGui.SetCursorPos(new ImVec2(0.0f, 0.0f));
            Assert.That(ImGui.IsRectVisible(new ImVec2(20.0f, 20.0f)), Is.True);

            ImGui.SetCursorPos(new ImVec2(5000.0f, 5000.0f));
            Assert.That(ImGui.IsRectVisible(new ImVec2(20.0f, 20.0f)), Is.False);
            ImGui.SetCursorPos(new ImVec2(0.0f, 0.0f));
        });
    }

    #endregion

    #region Item state and flags

    /// <summary>
    /// Verifies the full press/release sequence through
    /// <see cref="ImGui.ButtonBehavior"/>, covering hover, activation, the click edge, and
    /// deactivation.
    /// </summary>
    [Test]
    public void Item_ButtonBehavior_PressReleaseSequenceUpdatesItemState()
    {
        // Establish the window geometry and the button rectangle so hover can resolve on the next
        // frame; the mouse must be queued before NewFrame because hover is computed there.
        ImRect bb = default;
        WindowFrame("Button", new ImVec2(300.0f, 200.0f), () =>
        {
            ImVec2 cursor = ImGui.GetCursorScreenPos();
            bb = new ImRect(cursor, cursor + new ImVec2(100.0f, 30.0f));
        });

        _ctx.IO.AddMousePosEvent((bb.Min.X + bb.Max.X) * 0.5f, (bb.Min.Y + bb.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 200.0f), ImGuiCond.Always);
        ImGui.Begin("Button");
        uint id = ImGui.GetID("button");
        ImGui.ItemSize(bb.GetSize());
        ImGui.ItemAdd(bb, id);
        bool pressed = ImGui.ButtonBehavior(bb, id, out bool hovered, out bool held);

        Assert.That(hovered, Is.True);
        Assert.That(held, Is.True);
        Assert.That(pressed, Is.False); // The default press happens on release inside the box.
        Assert.That(ImGui.IsItemHovered(), Is.True);
        Assert.That(ImGui.IsItemActive(), Is.True);
        Assert.That(ImGui.IsItemActivated(), Is.True);
        Assert.That(ImGui.IsItemClicked(ImGuiMouseButton.Left), Is.True);
        Assert.That(ImGui.IsAnyItemActive(), Is.True);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 200.0f), ImGuiCond.Always);
        ImGui.Begin("Button");
        id = ImGui.GetID("button");
        ImGui.ItemSize(bb.GetSize());
        ImGui.ItemAdd(bb, id);
        pressed = ImGui.ButtonBehavior(bb, id, out hovered, out held);

        Assert.That(pressed, Is.True);
        Assert.That(held, Is.False);
        Assert.That(ImGui.IsItemActive(), Is.False);
        Assert.That(ImGui.IsItemDeactivated(), Is.True);
        Assert.That(ImGui.IsAnyItemActive(), Is.False);
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.MarkItemEdited"/> sets the edited item status flag.
    /// </summary>
    [Test]
    public void Item_MarkItemEdited_SetsEditedStatus()
    {
        WindowFrame("Edited", new ImVec2(300.0f, 200.0f), () =>
        {
            ImVec2 cursor = ImGui.GetCursorScreenPos();
            ImRect bb = new(cursor, cursor + new ImVec2(100.0f, 30.0f));
            uint id = ImGui.GetID("edit");
            ImGui.ItemSize(bb.GetSize());
            ImGui.ItemAdd(bb, id);

            Assert.That(ImGui.IsItemEdited(), Is.False);

            ImGui.MarkItemEdited(id);

            Assert.That(ImGui.IsItemEdited(), Is.True);
            Assert.That((ImGui.GetItemStatusFlags() & ImGuiItemStatusFlags.EditedInternal) != 0, Is.True);
            Assert.That(_ctx.AnyIdHasBeenEditedThisFrame, Is.True);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.PushItemFlag"/> applies an item flag to subsequently
    /// submitted items and <see cref="ImGui.PopItemFlag"/> restores the previous flags.
    /// </summary>
    [Test]
    public void Item_PushItemFlag_RoundTripsThroughLastItemFlags()
    {
        WindowFrame("Flags", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.PushItemFlag(ImGuiItemFlags.Disabled, true);
            Assert.That((_ctx.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0, Is.True);

            ImGui.Dummy(new ImVec2(10.0f, 10.0f));
            Assert.That((ImGui.GetItemFlags() & ImGuiItemFlags.Disabled) != 0, Is.True);

            ImGui.PopItemFlag();
            Assert.That((_ctx.CurrentItemFlags & ImGuiItemFlags.Disabled) == 0, Is.True);

            ImGui.Dummy(new ImVec2(10.0f, 10.0f));
            Assert.That((ImGui.GetItemFlags() & ImGuiItemFlags.Disabled) == 0, Is.True);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextItemOpen"/> is observable through
    /// <see cref="ImGuiContext.NextItemData"/> and that <see cref="ImGui.ItemAdd"/> clears the
    /// queued state.
    /// </summary>
    [Test]
    public void Item_SetNextItemOpen_IsObservableAndConsumed()
    {
        WindowFrame("NextOpen", new ImVec2(300.0f, 200.0f), () =>
        {
            ImGui.SetNextItemOpen(true);
            Assert.That((_ctx.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasOpen) != 0, Is.True);
            Assert.That(_ctx.NextItemData.OpenVal, Is.True);
            Assert.That(_ctx.NextItemData.OpenCond, Is.EqualTo((byte)ImGuiCond.Always));

            ImGui.Dummy(new ImVec2(10.0f, 10.0f));

            Assert.That(_ctx.NextItemData.HasFlags, Is.EqualTo(ImGuiNextItemDataFlags.None));
        });
    }

    #endregion

    #region Scrolling

    /// <summary>
    /// Verifies that content taller than the window produces a positive vertical scroll range.
    /// </summary>
    [Test]
    public void Scroll_ContentExceedingHeight_ProducesScrollMaxY()
    {
        WindowFrame("ScrollMax", new ImVec2(300.0f, 300.0f), () => FillScrollableContent(40));
        WindowFrame("ScrollMax", new ImVec2(300.0f, 300.0f), () =>
        {
            FillScrollableContent(40);
            Assert.That(ImGui.GetScrollMaxY(), Is.GreaterThan(0.0f));
            Assert.That(ImGui.GetScrollMaxX(), Is.EqualTo(0.0f));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetScrollY"/> queues a target that the next Begin resolves
    /// and that <see cref="ImGui.GetScrollY"/> then reports it.
    /// </summary>
    [Test]
    public void Scroll_SetScrollY_RoundTripsOnNextFrame()
    {
        WindowFrame("ScrollRT", new ImVec2(300.0f, 300.0f), () =>
        {
            FillScrollableContent(40);
            ImGui.SetScrollY(37.0f);
            Assert.That(_ctx.CurrentWindow!.ScrollTarget.Y, Is.EqualTo(37.0f));
        });

        WindowFrame("ScrollRT", new ImVec2(300.0f, 300.0f), () =>
        {
            FillScrollableContent(40);
            Assert.That(ImGui.GetScrollY(), Is.EqualTo(37.0f));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetScrollHereY"/> targets the current cursor line and that the
    /// resolved scroll position lands within the legal range.
    /// </summary>
    [Test]
    public void Scroll_SetScrollHereY_TargetsCurrentCursor()
    {
        WindowFrame("ScrollHere", new ImVec2(300.0f, 300.0f), () => FillScrollableContent(40));
        WindowFrame("ScrollHere", new ImVec2(300.0f, 300.0f), () =>
        {
            FillScrollableContent(40);
            ImGui.SetScrollHereY(0.0f);
        });
        // The target queued above is resolved at the start of this frame.
        WindowFrame("ScrollHere", new ImVec2(300.0f, 300.0f), () =>
        {
            FillScrollableContent(40);
            Assert.That(ImGui.GetScrollY(), Is.GreaterThan(0.0f));
            Assert.That(ImGui.GetScrollY(), Is.LessThanOrEqualTo(ImGui.GetScrollMaxY()));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.ScrollbarEx"/> converts a mouse press outside the grab into a
    /// canonical page-sized scroll step.
    /// </summary>
    [Test]
    public void Scroll_ScrollbarEx_DragOutsideGrabPagesTheView()
    {
        // Establish the window rectangle so the synthetic scrollbar can be hovered.
        WindowFrame("Bar", new ImVec2(300.0f, 300.0f), () => { });

        _ctx.IO.AddMousePosEvent(307.0f, 270.0f);
        _ctx.IO.AddMouseButtonEvent(0, true);

        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 300.0f), ImGuiCond.Always);
        ImGui.Begin("Bar");

        ImRect bbFrame = new(new ImVec2(300.0f, 90.0f), new ImVec2(314.0f, 290.0f));
        float scroll = 0.0f;
        bool held = ImGui.ScrollbarEx(bbFrame, ImGui.GetID("bar"), ImGuiAxis.Y, ref scroll, 100.0f, 500.0f, 0);

        Assert.That(held, Is.True);
        // ConfigScrollbarScrollByPage defaults to true, so a click below the grab pages by one
        // visible height, clamped to the total scroll range of contents minus visible height.
        Assert.That(scroll, Is.EqualTo(100.0f));

        ImGui.End();
        ImGui.Render();
    }

    #endregion
}
