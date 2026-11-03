// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical <see cref="ImGuiListClipper"/> against fixed window geometry so that every
/// displayed range is a concrete pair of item indices rather than a broad boolean. The expected
/// ranges are derived from canonical Dear ImGui rules.
/// </summary>
/// <remarks>
/// Each test submits a decorated-nowhere window with zero padding and zero item spacing, so the
/// cursor starts at the window origin and an item of height ten advances the cursor by exactly ten.
/// All coordinates are then integers and the visible range is exact.
/// </remarks>
[TestFixture]
public class ListClipperTests : ImGuiTestFixture
{
    #region Fixture lifecycle

    /// <summary>Window size used by the clipper tests; the visible height is 200 units.</summary>
    private static readonly ImVec2 WindowSize = new(200.0f, 200.0f);

    /// <summary>Origin of the clipper test window.</summary>
    private static readonly ImVec2 WindowOrigin = new(0.0f, 0.0f);


    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
    }


    #endregion

    #region Frame helper

    /// <summary>
    /// Runs one frame with a fixed, undecorated window at the origin and invokes <paramref name="body"/>
    /// while that window is current.
    /// </summary>
    /// <param name="body">Assertions and clipper steps executed while the window is current.</param>
    private static void Frame(Action body)
    {
        NewTestFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new ImVec2(0.0f, 0.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(0.0f, 0.0f));
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(WindowSize, ImGuiCond.Always);
        ImGui.Begin(
            "Clipper",
            ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoSavedSettings);
        body();
        ImGui.End();
        ImGui.PopStyleVar(3);
        ImGui.Render();
    }

    #endregion

    #region Clipper tests

    /// <summary>
    /// Verifies that an auto-measured clipper first exposes one item for measurement, then exposes the
    /// visible range, and finally reports the full count once the loop ends. Canonical step semantics
    /// are.
    /// </summary>
    [Test]
    public void Step_AutoMeasuredHeight_ExposesMeasureThenVisibleRange()
    {
        var clipper = new ImGuiListClipper();
        int[] firstStart = new int[1];
        int[] firstEnd = new int[1];
        int[] secondStart = new int[1];
        int[] secondEnd = new int[1];
        bool[] secondStep = new bool[1];
        bool[] thirdStep = new bool[1];

        Frame(() =>
        {
            clipper.Begin(100, -1.0f);

            // Step 1 measures the first item; the caller submits an item of height 10 (item spacing is
            // pinned to zero).
            bool step1 = clipper.Step();
            firstStart[0] = clipper.DisplayStart;
            firstEnd[0] = clipper.DisplayEnd;
            Assert.That(step1, Is.True);
            ImGui.Dummy(new ImVec2(0.0f, 10.0f));

            // Step 2 has inferred ItemsHeight = (10 - 0) / (1 - 0) = 10 and can now expose the visible
            // range. The cursor sits at Y = 10, so the clip rectangle [0, 200] maps to items 1.20.
            secondStep[0] = clipper.Step();
            secondStart[0] = clipper.DisplayStart;
            secondEnd[0] = clipper.DisplayEnd;

            // Step 3 ends the loop and seeks the cursor to the end of the list.
            thirdStep[0] = clipper.Step();
        });

        Assert.That(firstStart[0], Is.EqualTo(0));
        Assert.That(firstEnd[0], Is.EqualTo(1));
        Assert.That(secondStep[0], Is.True);
        Assert.That(secondStart[0], Is.EqualTo(1));
        Assert.That(secondEnd[0], Is.EqualTo(20));
        Assert.That(thirdStep[0], Is.False);
        Assert.That(clipper.DisplayStart, Is.EqualTo(100));
        Assert.That(clipper.DisplayEnd, Is.EqualTo(100));
    }

    /// <summary>
    /// Verifies that a clipper given an item height up front skips the measurement step and exposes the
    /// visible range immediately. Canonical computes positions with
    /// <c>(pos - cursorY - lossyness) / itemsHeight</c>
    /// </summary>
    [Test]
    public void Step_KnownHeight_ExposesVisibleRangeImmediately()
    {
        var clipper = new ImGuiListClipper();
        int[] start = new int[1];
        int[] end = new int[1];
        bool[] firstStep = new bool[1];

        Frame(() =>
        {
            clipper.Begin(100, 10.0f);
            firstStep[0] = clipper.Step();
            start[0] = clipper.DisplayStart;
            end[0] = clipper.DisplayEnd;
        });

        Assert.That(firstStep[0], Is.True);
        Assert.That(start[0], Is.EqualTo(0));
        Assert.That(end[0], Is.EqualTo(20));
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiListClipper.IncludeItemsByIndex"/> keeps an off-screen range in the
    /// display set and that the visible range is sorted before it. Canonical sorts and fuses ranges in
    /// <c>ImGuiListClipper_SortAndFuseRanges</c>.
    /// </summary>
    [Test]
    public void IncludeItemsByIndex_AddsUnclippedRange_AndSortsIt()
    {
        var clipper = new ImGuiListClipper();
        int[] firstStart = new int[1];
        int[] firstEnd = new int[1];
        int[] secondStart = new int[1];
        int[] secondEnd = new int[1];

        Frame(() =>
        {
            clipper.Begin(100, 10.0f);
            clipper.IncludeItemsByIndex(50, 60);

            Assert.That(clipper.Step(), Is.True);
            firstStart[0] = clipper.DisplayStart;
            firstEnd[0] = clipper.DisplayEnd;

            Assert.That(clipper.Step(), Is.True);
            secondStart[0] = clipper.DisplayStart;
            secondEnd[0] = clipper.DisplayEnd;
        });

        Assert.That(firstStart[0], Is.EqualTo(0));
        Assert.That(firstEnd[0], Is.EqualTo(20));
        Assert.That(secondStart[0], Is.EqualTo(50));
        Assert.That(secondEnd[0], Is.EqualTo(60));
    }

    /// <summary>
    /// Verifies the double-precision seek arithmetic: the cursor lands at
    /// <c>StartPosY + StartSeekOffsetY + itemIndex * ItemsHeight</c>
    /// </summary>
    [Test]
    public void SeekCursorForItem_MovesCursorToExactPosition()
    {
        var clipper = new ImGuiListClipper();
        float[] cursorAfterBegin = new float[1];
        float[] cursorAfterSeek = new float[1];

        Frame(() =>
        {
            clipper.Begin(100, 10.0f);
            cursorAfterBegin[0] = ImGui.GetCursorPosY();
            clipper.SeekCursorForItem(7);
            cursorAfterSeek[0] = ImGui.GetCursorPosY();
            clipper.End();
        });

        Assert.That(cursorAfterBegin[0], Is.EqualTo(0.0f));
        Assert.That(cursorAfterSeek[0], Is.EqualTo(70.0f));
    }

    /// <summary>
    /// Verifies the indeterminate-count contract: <c>Begin(int.MaxValue)</c> never advances the cursor
    /// on its final step, and the caller seeks manually afterwards
    /// </summary>
    [Test]
    public void SeekCursorForItem_IndeterminateCount_DoesNotAdvanceOnFinalStep()
    {
        var clipper = new ImGuiListClipper();
        float[] cursorAtEnd = new float[1];

        Frame(() =>
        {
            clipper.Begin(int.MaxValue, 10.0f);
            Assert.That(clipper.Step(), Is.True);
            Assert.That(clipper.Step(), Is.False);
            cursorAtEnd[0] = ImGui.GetCursorPosY();

            // Only an explicit seek moves the cursor once the loop has ended.
            clipper.SeekCursorForItem(42);
            Assert.That(ImGui.GetCursorPosY(), Is.EqualTo(420.0f));
        });

        // The final step of an indeterminate clipper leaves the cursor at item 0's position, and the
        // clipper is reset so reusing it starts clean.
        Assert.That(cursorAtEnd[0], Is.EqualTo(0.0f));
        Assert.That(clipper.ItemsCount, Is.EqualTo(-1));
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiListClipper.End"/> restores the context's stacked clipper count and
    /// resets the visible range. Canonical releases the pooled temporary data in
    /// <c>ImGuiListClipper::End</c>.
    /// </summary>
    [Test]
    public void End_ReleasesTemporaryDataAndResetsRange()
    {
        var clipper = new ImGuiListClipper();
        int[] stackedDuring = new int[1];
        int[] stackedAfter = new int[1];

        Frame(() =>
        {
            clipper.Begin(50, 10.0f);
            stackedDuring[0] = _ctx.ClipperTempDataStacked;
            Assert.That(clipper.Step(), Is.True);
            clipper.End();
            stackedAfter[0] = _ctx.ClipperTempDataStacked;
        });

        Assert.That(stackedDuring[0], Is.EqualTo(1));
        Assert.That(stackedAfter[0], Is.EqualTo(0));
        Assert.That(clipper.DisplayStart, Is.EqualTo(50));
        Assert.That(clipper.DisplayEnd, Is.EqualTo(50));
        Assert.That(clipper.ItemsCount, Is.EqualTo(-1));
    }

    /// <summary>
    /// Verifies that calling <see cref="ImGuiListClipper.End"/> after the clipper has already finished stepping is
    /// a no-op. The demo closes its clippers in a <c>finally</c> block, so this second call must be safe
    ///.
    /// </summary>
    [Test]
    public void End_AfterCompletedStepping_IsIdempotent()
    {
        int visited = 0;
        Frame(() =>
        {
            ImGuiListClipper clipper = new();
            try
            {
                clipper.Begin(5, 10.0f);
                while (clipper.Step())
                    visited += clipper.DisplayEnd - clipper.DisplayStart;
            }
            finally
            {
                clipper.End();
            }
            clipper.End();
        });
        Assert.That(visited, Is.GreaterThan(0));
    }

    #endregion
}
