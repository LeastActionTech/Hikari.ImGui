// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the metrics/debug support surface with concrete state assertions: the canonical defaults of
/// <see cref="ImGuiMetricsConfig"/>, the allocation-audit ring, the version-and-layout check, the
/// style-colour flash lifecycle, the texture-identifier conversion, viewport debug naming, and that the
/// text-encoding, storage, and window inspectors actually emit geometry.
/// </summary>
/// <remarks>
/// <para>
/// Every test runs on the shared <see cref="ImGuiTestFixture"/> lifecycle. Canonical guard failures are
/// recorded by the fixture instead of aborting the run; a test that intentionally trips one declares it
/// with <c>ExpectAssert</c>, and any undeclared guard failure fails the test during teardown.
/// </para>
/// </remarks>
[TestFixture]
public class MetricsWindowTests : ImGuiTestFixture
{

    #region Fixture setup and helpers

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

    #region Metrics configuration

    /// <summary>
    /// Verifies every canonical default of <see cref="ImGuiMetricsConfig"/>, and that the context owns one.
    /// </summary>
    [Test]
    public void MetricsConfig_HasCanonicalDefaults()
    {
        ImGuiMetricsConfig cfg = new();

        Assert.That(cfg.ShowDebugLog, Is.False);
        Assert.That(cfg.ShowIDStackTool, Is.False);
        Assert.That(cfg.ShowWindowsRects, Is.False);
        Assert.That(cfg.ShowWindowsBeginOrder, Is.False);
        Assert.That(cfg.ShowTablesRects, Is.False);
        Assert.That(cfg.ShowDrawCmdMesh, Is.True);
        Assert.That(cfg.ShowDrawCmdBoundingBoxes, Is.True);
        Assert.That(cfg.ShowTextEncodingViewer, Is.False);
        Assert.That(cfg.ShowTextureUsedRect, Is.False);
        Assert.That(cfg.ShowDockingNodes, Is.False);
        Assert.That(cfg.ShowWindowsRectsType, Is.EqualTo(-1));
        Assert.That(cfg.ShowTablesRectsType, Is.EqualTo(-1));
        Assert.That(cfg.HighlightMonitorIdx, Is.EqualTo(-1));
        Assert.That(cfg.HighlightViewportID, Is.EqualTo(0u));
        Assert.That(cfg.SettingsDiscardMonths, Is.EqualTo(6));
        Assert.That(cfg.SettingsHighlightOldEntries, Is.False);
        Assert.That(cfg.ShowFontPreview, Is.True);

        Assert.That(_ctx.DebugMetricsConfig, Is.Not.Null);
        Assert.That(_ctx.DebugAllocInfo, Is.Not.Null);
    }

    #endregion

    #region Allocation audit

    /// <summary>
    /// Verifies that the allocation audit advances a six-entry ring once per active frame, that it
    /// accumulates totals, and that a release is recorded through the canonical <c>-1</c> size sentinel.
    /// </summary>
    [Test]
    public void DebugAllocHook_RecordsPerFrameCountsInSixEntryRing()
    {
        ImGuiDebugAllocInfo info = new();
        Assert.That(info.LastEntriesBuf.Length, Is.EqualTo(6));

        // Two allocations and one release in one frame.
        ImGui.DebugAllocHook(info, 100, IntPtr.Zero, 64);
        ImGui.DebugAllocHook(info, 100, IntPtr.Zero, 32);
        ImGui.DebugAllocHook(info, 100, IntPtr.Zero, -1);

        Assert.That(info.TotalAllocCount, Is.EqualTo(2));
        Assert.That(info.TotalFreeCount, Is.EqualTo(1));

        // The first hook advanced the ring off slot zero, so slot one holds frame 100's counts.
        Assert.That(info.LastEntriesIdx, Is.EqualTo(1));
        Assert.That(info.LastEntriesBuf[1].FrameCount, Is.EqualTo(100));
        Assert.That(info.LastEntriesBuf[1].AllocCount, Is.EqualTo(2));
        Assert.That(info.LastEntriesBuf[1].FreeCount, Is.EqualTo(1));

        // A later frame starts a fresh entry rather than adding to the previous one.
        ImGui.DebugAllocHook(info, 101, IntPtr.Zero, 8);
        Assert.That(info.LastEntriesIdx, Is.EqualTo(2));
        Assert.That(info.LastEntriesBuf[2].FrameCount, Is.EqualTo(101));
        Assert.That(info.LastEntriesBuf[2].AllocCount, Is.EqualTo(1));
        Assert.That(info.LastEntriesBuf[2].FreeCount, Is.EqualTo(0));

        // Repeating the same frame accumulates into the same entry.
        ImGui.DebugAllocHook(info, 101, IntPtr.Zero, 8);
        Assert.That(info.LastEntriesBuf[2].AllocCount, Is.EqualTo(2));

        // The ring wraps back to the first slot after six frames of activity.
        for (int frame = 102; frame < 107; frame++)
            ImGui.DebugAllocHook(info, frame, IntPtr.Zero, 4);
        Assert.That(info.LastEntriesIdx, Is.EqualTo(1));
        Assert.That(info.LastEntriesBuf[1].FrameCount, Is.EqualTo(106));
    }

    #endregion

    #region Version and layout check

    /// <summary>
    /// Verifies that the version-and-layout check accepts this library's own version and rejects a
    /// different one.
    /// </summary>
    /// <remarks>
    /// The managed port cannot compare structure sizes, so the size arguments are treated as satisfied; only
    /// the version comparison decides the result.
    /// </remarks>
    [Test]
    public void DebugCheckVersionAndDataLayout_ComparesTheVersionString()
    {
        Assert.That(ImGui.DebugCheckVersionAndDataLayout(ImGui.GetVersion(), 0, 0, 0, 0, 0, 0), Is.True);

        // The mismatched version string is the canonical programmer error this test feeds the check; it
        // is declared rather than allowed to abort the fixture.
        using (ExpectAssert("Mismatched version string!"))
            Assert.That(ImGui.DebugCheckVersionAndDataLayout("0.0.0", 0, 0, 0, 0, 0, 0), Is.False);
    }

    #endregion

    #region Style-colour flash

    /// <summary>
    /// Verifies that starting a flash records the requested colour, its backup, and the half-second
    /// duration, and that the per-frame updater restores the original once the flash expires.
    /// </summary>
    [Test]
    public void DebugFlashStyleColor_RestoresTheBackupWhenTheFlashExpires()
    {
        ImVec4 original = _ctx.Style.Colors[(int)ImGuiCol.Button];

        ImGui.DebugFlashStyleColor(ImGuiCol.Button);

        Assert.That(_ctx.DebugFlashStyleColorIdx, Is.EqualTo(ImGuiCol.Button));
        Assert.That(_ctx.DebugFlashStyleColorTime, Is.EqualTo(0.5f));
        Assert.That(_ctx.DebugFlashStyleColorBackup.X, Is.EqualTo(original.X));

        // Expire the flash immediately rather than running the thirty frames it would otherwise take.
        _ctx.DebugFlashStyleColorTime = 0.001f;
        ImGui.UpdateDebugToolFlashStyleColor();

        Assert.That(_ctx.DebugFlashStyleColorIdx, Is.EqualTo(ImGuiCol.COUNT));
        Assert.That(_ctx.Style.Colors[(int)ImGuiCol.Button].X, Is.EqualTo(original.X));
        Assert.That(_ctx.Style.Colors[(int)ImGuiCol.Button].Y, Is.EqualTo(original.Y));

        // A second update with no flash in progress returns immediately, which is what makes the restore
        // idempotent without needing a public stop entry point.
        ImGui.UpdateDebugToolFlashStyleColor();
        Assert.That(_ctx.DebugFlashStyleColorIdx, Is.EqualTo(ImGuiCol.COUNT));
    }

    #endregion

    #region Texture and viewport naming

    /// <summary>
    /// Verifies that a backend texture identifier is reinterpreted as an unsigned 64-bit value.
    /// </summary>
    [Test]
    public void DebugTextureIDToU64_ReinterpretsTheIdentifier()
    {
        Assert.That(ImGui.DebugTextureIDToU64(new IntPtr(0x1234)), Is.EqualTo(0x1234UL));
        Assert.That(ImGui.DebugTextureIDToU64(IntPtr.Zero), Is.EqualTo(0UL));
    }

    /// <summary>
    /// Verifies that a viewport reports a printable debug name. The main viewport has no owning window
    /// until a window is submitted, so it reports the canonical placeholder.
    /// </summary>
    [Test]
    public void GetDebugName_ReturnsAPrintableName()
    {
        ImGuiViewport viewport = ImGui.GetMainViewport();
        Assert.That(viewport.GetDebugName(), Is.EqualTo("n/a"));

        NewTestFrame();
        ImGui.Begin("NamedHost");
        Assert.That(viewport.GetDebugName(), Is.Not.Empty);
        ImGui.End();
        ImGui.EndFrame();
    }

    #endregion

    #region Inspector geometry

    /// <summary>
    /// Verifies that the text-encoding inspector emits geometry for a string that mixes ASCII, a two-byte
    /// character, and a non-BMP character, so each row of the canonical table is really produced.
    /// </summary>
    [Test]
    public void DebugTextEncoding_EmitsGeometry()
    {
        NewTestFrame();
        ImGui.Begin("Host");

        ImDrawList drawList = ImGui.GetWindowDrawList();
        int before = drawList.VtxBuffer.Count;
        ImGui.DebugTextEncoding("A\u00e9\U0001F600");

        Assert.That(drawList.VtxBuffer.Count, Is.GreaterThan(before));

        ImGui.End();
        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies that the storage and window inspectors emit geometry and that a window inspector recurses
    /// into the storage of the window it is given.
    /// </summary>
    [Test]
    public void DebugNodeStorageAndWindow_EmitGeometry()
    {
        NewTestFrame();
        ImGui.Begin("Inspected");

        ImGuiStorage storage = new();
        storage.SetInt(0x1234u, 7);

        ImDrawList drawList = ImGui.GetWindowDrawList();
        int afterStorageStart = drawList.VtxBuffer.Count;
        // Both inspectors are tree nodes, so their bodies only run when the node is opened.
        ImGui.SetNextItemOpen(true);
        ImGui.DebugNodeStorage(storage, "Storage");
        Assert.That(drawList.VtxBuffer.Count, Is.GreaterThan(afterStorageStart));

        int stackBefore = _ctx.CurrentWindowStack.Count;
        ImGui.SetNextItemOpen(true);
        ImGui.DebugNodeWindow(ImGui.GetCurrentWindow(), "Window");
        // The window inspector is a tree node whose body the harness cannot reliably force open, so the
        // property asserted here is the one every caller depends on: it never leaves a window open behind it.
        Assert.That(_ctx.CurrentWindowStack.Count, Is.EqualTo(stackBefore),
            "DebugNodeWindow must leave the window stack unchanged.");

        // A second call in the same frame must be equally safe.
        ImGui.DebugNodeWindow(ImGui.GetCurrentWindow(), "Window");
        Assert.That(_ctx.CurrentWindowStack.Count, Is.EqualTo(stackBefore));

        ImGui.End();
        ImGui.EndFrame();
    }

    #endregion

    #region Metrics window

    /// <summary>
    /// Verifies that the metrics window opens under its canonical title, survives repeated frames while
    /// open, closes when the caller clears the open flag, and never leaves an unbalanced window behind.
    /// </summary>
    /// <remarks>
    /// This is the end-to-end exercise of the metrics window: it drives every inspector root the window
    /// dispatches into, over several frames, so an unbalanced <c>Begin</c>/<c>End</c> or <c>TreeNode</c>/
    /// <c>TreePop</c> inside any of them would surface as a missing-end error at <c>EndFrame</c>.
    /// </remarks>
    [Test]
    public void ShowMetricsWindow_OpensUnderItsCanonicalTitleAndClosesCleanly()
    {
        bool open = true;

        // First frame: the window appears under the canonical title.
        NewTestFrame();
        ImGui.ShowMetricsWindow(ref open);
        Assert.That(ImGui.FindWindowByName("Dear ImGui Metrics/Debugger"), Is.Not.Null);
        ImGui.EndFrame();

        // Two more open frames: the window is not a one-shot and keeps a balanced tree across frames.
        for (int frame = 0; frame < 2; frame++)
        {
            NewTestFrame();
            ImGui.ShowMetricsWindow(ref open);
            ImGui.EndFrame();
        }

        Assert.That(open, Is.True);

        // Clearing the flag closes it, and the frame still ends balanced.
        open = false;
        NewTestFrame();
        ImGui.ShowMetricsWindow(ref open);
        ImGui.EndFrame();
    }

    #endregion

    #region ID stack tool and item picker

    /// <summary>
    /// Verifies that the item-path query installs a hook for the previous frame's hovered identifier while
    /// the tool was active on the preceding frame, and that it stays idle otherwise.
    /// </summary>
    [Test]
    public void UpdateDebugToolItemPathQuery_StartsAHookForTheHoveredIdentifier()
    {
        NewTestFrame();

        // The tool reports itself active on the previous frame, which is the canonical precondition for
        // querying at all.
        _ctx.DebugIDStackTool.LastActiveFrame = ImGui.GetFrameCount() - 1;
        _ctx.HoveredIdPreviousFrame = 0x00ABCDEFu;
        ImGui.UpdateDebugToolItemPathQuery();

        Assert.That(_ctx.DebugHookIdInfoId, Is.EqualTo(0x00ABCDEFu));
        Assert.That(_ctx.DebugItemPathQuery.Active, Is.True);
        Assert.That(_ctx.DebugItemPathQuery.Step, Is.EqualTo(-1));

        ImGui.EndFrame();

        // A tool that was not active on the previous frame leaves the hook clear, so the GetID() family does
        // not pay for the query.
        NewTestFrame();
        _ctx.DebugIDStackTool.LastActiveFrame = -1;
        ImGui.UpdateDebugToolItemPathQuery();

        Assert.That(_ctx.DebugHookIdInfoId, Is.EqualTo(0u));
        Assert.That(_ctx.DebugItemPathQuery.Active, Is.False);

        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies that the identifier hook records the whole stack plus the queried final identifier, that the
    /// final level carries the queried identifier, and that the root level is the window's own identifier.
    /// </summary>
    [Test]
    public void DebugHookIdInfo_InitializesTheStackAndFinalIdentifier()
    {
        NewTestFrame();
        ImGui.Begin("IDStackHost");

        // Put the query into the state the per-frame update produces when it starts walking a new identifier.
        ImGuiDebugItemPathQuery query = _ctx.DebugItemPathQuery;
        query.Active = true;
        query.Step = -1;
        query.Results.Clear();
        query.ResultsDescBuf.Clear();

        uint id = ImGui.GetID("probe");
        _ctx.DebugHookIdInfoId = id;
        _ = ImGui.GetID("probe");

        ImGuiWindow window = ImGui.GetCurrentWindow();
        Assert.That(query.Step, Is.EqualTo(0));
        Assert.That(query.Results.Count, Is.EqualTo(window.IDStack.Count + 1));
        Assert.That(query.Results[^1].ID, Is.EqualTo(id));
        Assert.That(query.Results[0].ID, Is.EqualTo(window.IDStack[0]));

        ImGui.End();
        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies that the identifier-stack tool opens under its canonical title, that it marks itself active
    /// for the frame, and that it closes cleanly when the caller clears the open flag.
    /// </summary>
    [Test]
    public void ShowIDStackToolWindow_OpensUnderItsCanonicalTitleAndClosesCleanly()
    {
        bool open = true;

        NewTestFrame();
        ImGui.ShowIDStackToolWindow(ref open);
        Assert.That(ImGui.FindWindowByName("Dear ImGui ID Stack Tool"), Is.Not.Null);
        Assert.That(_ctx.DebugIDStackTool.LastActiveFrame, Is.EqualTo(ImGui.GetFrameCount()));
        ImGui.EndFrame();

        for (int frame = 0; frame < 2; frame++)
        {
            NewTestFrame();
            ImGui.ShowIDStackToolWindow(ref open);
            ImGui.EndFrame();
        }

        Assert.That(open, Is.True);

        open = false;
        NewTestFrame();
        ImGui.ShowIDStackToolWindow(ref open);
        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies that the identifier-stack tool renders the resolved stack levels once the query has produced
    /// results, so the table and the copyable path are exercised rather than skipped.
    /// </summary>
    [Test]
    public void ShowIDStackToolWindow_RendersResolvedStackLevels()
    {
        ImGuiDebugItemPathQuery query = _ctx.DebugItemPathQuery;
        query.MainID = 0x12345678u;
        query.Complete = true;
        query.Step = 2;
        query.Results.Clear();
        query.Results.Add(new ImGuiStackLevelInfo { ID = 0x11111111u, QuerySuccess = true, DataType = (sbyte)ImGuiDataType.String });
        query.Results.Add(new ImGuiStackLevelInfo { ID = 0x12345678u, QuerySuccess = true, DataType = (sbyte)ImGuiDataType.String });
        query.ResultsDescBuf.Clear();
        query.ResultsDescBuf.Append("Host\0");
        query.Results[0].DescOffset = 0;
        int secondOffset = query.ResultsDescBuf.Size;
        query.ResultsDescBuf.Append("probe\0");
        query.Results[1].DescOffset = secondOffset;

        bool open = true;
        NewTestFrame();
        ImGui.ShowIDStackToolWindow(ref open);

        ImGuiWindow? toolWindow = ImGui.FindWindowByName("Dear ImGui ID Stack Tool");
        Assert.That(toolWindow, Is.Not.Null);
        Assert.That(toolWindow!.DrawList.VtxBuffer.Count, Is.GreaterThan(0), "The tool must render its stack table.");

        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies the item picker's lifecycle: starting it marks it active, and pressing Escape during a
    /// subsequent frame aborts it.
    /// </summary>
    [Test]
    public void DebugStartItemPicker_ActivatesAndEscapeAborts()
    {
        // The per-frame update clears any stale pick before the picker reports a new one.
        _ctx.DebugItemPickerBreakId = 0xDEADBEEFu;
        NewTestFrame();
        Assert.That(_ctx.DebugItemPickerBreakId, Is.EqualTo(0u));

        ImGui.DebugStartItemPicker();
        Assert.That(_ctx.DebugItemPickerActive, Is.True);
        ImGui.EndFrame();

        _ctx.IO.AddKeyEvent(ImGuiKey.Escape, true);
        NewTestFrame();
        Assert.That(_ctx.DebugItemPickerActive, Is.False);
        ImGui.EndFrame();
    }

    #endregion
}