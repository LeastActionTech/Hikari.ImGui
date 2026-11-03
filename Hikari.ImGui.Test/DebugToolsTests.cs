// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.IO;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the text-capture facility and the debug log with concrete state assertions: buffer
/// accumulation and clearing, line splitting and tree indentation, file output, per-category debug-log
/// filtering, the skipped-error counter, the error-log destinations, the error-tooltip window, and the
/// window-state recovery unwind.
/// </summary>
/// <remarks>
/// <para>
/// Every test runs on the shared <see cref="ImGuiTestFixture"/> lifecycle. Canonical guard failures are
/// recorded by the fixture instead of aborting the run; a test that intentionally trips one declares it
/// with <c>ExpectAssert</c>, and any undeclared guard failure fails the test during teardown.
/// </para>
/// </remarks>
[TestFixture]
public class DebugToolsTests : ImGuiTestFixture
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

        // Keep the diagnostic output quiet: only the error category is buffered, and the TTY/debugger
        // mirrors are off so the test run is not polluted by the debug log.
        _ctx.DebugLogFlags = ImGuiDebugLogFlags.EventError;
    }


    #endregion

    #region Text capture

    /// <summary>
    /// Verifies that buffer capture accumulates the text handed to <see cref="ImGui.LogText"/> and that
    /// <see cref="ImGui.LogFinish"/> resets the capture state and empties the buffer.
    /// </summary>
    [Test]
    public void LogToBuffer_AccumulatesTextAndFinishClears()
    {
        NewTestFrame();
        ImGui.LogToBuffer();

        ImGui.LogText("first ");
        ImGui.LogText("{0}-{1}", "second", 3);

        Assert.That(_ctx.LogEnabled, Is.True);
        Assert.That(_ctx.LogBuffer.CStr(), Is.EqualTo("first second-3"));

        ImGui.LogFinish();

        Assert.That(_ctx.LogEnabled, Is.False);
        Assert.That(_ctx.ItemUnclipByLog, Is.False);
        Assert.That(_ctx.LogFlags, Is.EqualTo(ImGuiLogFlags.None));
        Assert.That(_ctx.LogBuffer.Empty, Is.True);
    }

    /// <summary>
    /// Verifies that a capture call is a no-op while no capture is active, so callers may log
    /// unconditionally.
    /// </summary>
    [Test]
    public void LogText_WithoutActiveCapture_IsIgnored()
    {
        NewTestFrame();
        Assert.That(_ctx.LogEnabled, Is.False);

        ImGui.LogText("not captured");

        Assert.That(_ctx.LogBuffer.Empty, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.LogRenderedText"/> splits a run at its line breaks and indents the
    /// start of each line by four spaces per tree level, as measured from the depth recorded when the
    /// capture began.
    /// </summary>
    [Test]
    public void LogRenderedText_SplitsLinesAndIndentsByTreeDepth()
    {
        NewTestFrame();
        ImGui.LogToBuffer();

        // Depth two means the first item of every captured line is indented by eight spaces. Tree depth
        // is borrowed internal state, so it is restored before the frame ends; otherwise the end-of-frame
        // recovery reports a missing TreePop().
        ImGuiWindow window = ImGui.GetCurrentWindow();
        int savedTreeDepth = window.DC.TreeDepth;
        window.DC.TreeDepth = 2;
        try
        {
            ImGui.LogRenderedText(null, "alpha\nbeta");
            Assert.That(_ctx.LogBuffer.CStr(), Is.EqualTo("        alpha\n        beta"));
        }
        finally
        {
            window.DC.TreeDepth = savedTreeDepth;
        }

        ImGui.LogFinish();
    }

    /// <summary>
    /// Verifies that a rendered run stops at the <c>##</c> identifier marker, matching
    /// <see cref="ImGui.FindRenderedTextEnd"/>.
    /// </summary>
    [Test]
    public void LogRenderedText_StopsAtIdentifierMarker()
    {
        NewTestFrame();
        ImGui.LogToBuffer();

        ImGui.LogRenderedText(null, "visible##hidden");

        Assert.That(_ctx.LogBuffer.CStr(), Is.EqualTo("visible"));

        ImGui.LogFinish();
    }

    /// <summary>
    /// Verifies that a decoration latched by <see cref="ImGui.LogSetNextTextDecoration"/> is captured
    /// verbatim even when it begins with <c>##</c>, which would otherwise be mistaken for an identifier
    /// marker and dropped from the capture.
    /// </summary>
    /// <remarks>
    /// The single spaces around the label are canonical, not an artifact: capturing the prefix clears
    /// <c>LogLineFirstItem</c>, so the label and the suffix each take the one-space continuation indent
    /// instead of the four-spaces-per-tree-level first-item indent.
    /// </remarks>
    [Test]
    public void LogRenderedText_CapturesDecorationVerbatimWhenItStartsWithHashHash()
    {
        NewTestFrame();
        ImGui.LogToBuffer();

        // Framed tree nodes latch "###" on both sides, which is the case that exposed the defect.
        ImGui.LogSetNextTextDecoration("###", "###");
        ImGui.LogRenderedText(null, "alpha");

        Assert.That(_ctx.LogBuffer.CStr(), Is.EqualTo("### alpha ###"));

        ImGui.LogFinish();
    }

    /// <summary>
    /// Verifies that logging to a file appends the captured text and that finishing closes the file so the
    /// content is durable.
    /// </summary>
    [Test]
    public void LogToFile_AppendsTextAndFinishClosesTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hikari-imgui-log-{Guid.NewGuid():N}.txt");
        try
        {
            NewTestFrame();
            ImGui.LogToFile(filename: path);
            ImGui.LogText("captured line");

            Assert.That(_ctx.LogFile, Is.Not.Null);

            ImGui.LogFinish();

            Assert.That(_ctx.LogFile, Is.Null);
            Assert.That(File.ReadAllText(path), Does.Contain("captured line"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that starting a second capture while one is already active is ignored, so the first
    /// destination is not silently replaced.
    /// </summary>
    [Test]
    public void LogToBuffer_WhileAlreadyCapturing_IsIgnored()
    {
        NewTestFrame();
        ImGui.LogToBuffer();
        ImGuiLogFlags firstFlags = _ctx.LogFlags;

        ImGui.LogToClipboard();

        Assert.That(_ctx.LogFlags, Is.EqualTo(firstFlags));

        ImGui.LogFinish();
    }

    #endregion

    #region Debug log

    /// <summary>
    /// Verifies that <see cref="ImGui.DebugLog"/> appends a frame-prefixed line to the debug buffer and
    /// records one index entry, so the debug-log window can page through it.
    /// </summary>
    [Test]
    public void DebugLog_AppendsLineAndIndexEntry()
    {
        NewTestFrame();
        int linesBefore = _ctx.DebugLogIndex.Size();
        int sizeBefore = _ctx.DebugLogBuf.Size;

        ImGui.DebugLog("probe {0}", 7);

        Assert.That(_ctx.DebugLogBuf.Size, Is.GreaterThan(sizeBefore));
        Assert.That(_ctx.DebugLogBuf.CStr(), Does.Contain("probe 7"));
        Assert.That(_ctx.DebugLogIndex.Size(), Is.EqualTo(linesBefore + 1));
    }

    /// <summary>
    /// Verifies that the per-category debug-log helper writes only when its event flag is enabled, which
    /// is how the canonical <c>IMGUI_DEBUG_LOG_XXX</c> macros keep hot paths cheap.
    /// </summary>
    [Test]
    public void DebugLogEvent_WritesOnlyWhenItsCategoryIsEnabled()
    {
        _ctx.DebugLogFlags = ImGuiDebugLogFlags.EventError;
        int sizeBefore = _ctx.DebugLogBuf.Size;

        ImGui.DebugLogEvent(ImGuiDebugLogFlags.EventNav, "filtered");
        Assert.That(_ctx.DebugLogBuf.Size, Is.EqualTo(sizeBefore));

        ImGui.DebugLogEvent(ImGuiDebugLogFlags.EventError, "kept");
        Assert.That(_ctx.DebugLogBuf.Size, Is.GreaterThan(sizeBefore));
        Assert.That(_ctx.DebugLogBuf.CStr(), Does.Contain("kept"));
    }

    /// <summary>
    /// Verifies that a suppressed error is counted rather than dropped silently, which is what lets the
    /// debug-log window warn that error output was skipped.
    /// </summary>
    [Test]
    public void DebugLogError_IncrementsSkippedCountWhenCategoryDisabled()
    {
        _ctx.DebugLogFlags = ImGuiDebugLogFlags.None;
        _ctx.DebugLogSkippedErrors = 0;
        int sizeBefore = _ctx.DebugLogBuf.Size;

        ImGui.DebugLogError("suppressed error");

        Assert.That(_ctx.DebugLogSkippedErrors, Is.EqualTo(1));
        Assert.That(_ctx.DebugLogBuf.Size, Is.EqualTo(sizeBefore));
    }

    /// <summary>
    /// Verifies that clearing the debug log empties both the text buffer and its line index.
    /// </summary>
    [Test]
    public void DebugLogBuffer_AndIndex_CanBeClearedTogether()
    {
        NewTestFrame();
        ImGui.DebugLog("disposable");

        _ctx.DebugLogBuf.Clear();
        _ctx.DebugLogIndex.Clear();
        _ctx.DebugLogSkippedErrors = 0;

        Assert.That(_ctx.DebugLogBuf.Empty, Is.True);
        Assert.That(_ctx.DebugLogIndex.Size(), Is.EqualTo(0));
        Assert.That(_ctx.DebugLogSkippedErrors, Is.EqualTo(0));
    }

    #endregion

    #region Error log and tooltip

    /// <summary>
    /// Verifies that a recoverable error is written to the debug log, that the first-error banner is
    /// emitted once, and that the first-error flag is cleared afterwards.
    /// </summary>
    [Test]
    public void ErrorLog_WritesDebugLogLineAndClearsErrorFirst()
    {
        _ctx.IO.ConfigErrorRecoveryEnableDebugLog = true;
        _ctx.IO.ConfigErrorRecoveryEnableTooltip = false;
        _ctx.IO.ConfigErrorRecoveryEnableAssert = false;
        _ctx.ErrorFirst = true;

        NewTestFrame();
        int sizeBefore = _ctx.DebugLogBuf.Size;

        ImGui.ErrorLog("something went wrong");

        Assert.That(_ctx.ErrorFirst, Is.False);
        Assert.That(_ctx.DebugLogBuf.Size, Is.GreaterThan(sizeBefore));
        Assert.That(_ctx.DebugLogBuf.CStr(), Does.Contain("[imgui-error]"));
        Assert.That(_ctx.DebugLogBuf.CStr(), Does.Contain("something went wrong"));
    }

    /// <summary>
    /// Verifies that the error tooltip opens a real pseudo-tooltip window and that the window is named as
    /// canonical names it, so the tooltip can be found and amended at end of frame.
    /// </summary>
    [Test]
    public void BeginErrorTooltip_OpensThePseudoTooltipWindow()
    {
        NewTestFrame();

        Assert.That(ImGui.BeginErrorTooltip(), Is.True);
        Assert.That(ImGui.FindWindowByName("##Tooltip_Error"), Is.Not.Null);
        ImGui.EndErrorTooltip();
    }

    /// <summary>
    /// Verifies that the error counter advances only while the tooltip destination is enabled, so the
    /// end-of-frame amendment reports exactly the errors that were surfaced.
    /// </summary>
    [Test]
    public void ErrorLog_AdvancesCounterOnlyWhenTooltipEnabled()
    {
        _ctx.IO.ConfigErrorRecoveryEnableTooltip = false;
        _ctx.IO.ConfigErrorRecoveryEnableDebugLog = false;
        _ctx.IO.ConfigErrorRecoveryEnableAssert = false;

        // Disabling every recovery destination is itself a canonical misuse that NewFrame reports. The
        // test declares it because it is measuring exactly that configuration.
        using (ExpectAssert("Error recovery requires at least one output."))
            NewTestFrame();
        ImGui.ErrorLog("first");

        Assert.That(_ctx.ErrorCountCurrentFrame, Is.EqualTo(0));

        _ctx.IO.ConfigErrorRecoveryEnableTooltip = true;
        ImGui.ErrorLog("second");

        Assert.That(_ctx.ErrorCountCurrentFrame, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that the error callback is notified regardless of which built-in destinations are enabled,
    /// so a host that routes errors itself always observes them.
    /// </summary>
    [Test]
    public void ErrorLog_NotifiesCallbackRegardlessOfDestinations()
    {
        _ctx.IO.ConfigErrorRecoveryEnableTooltip = false;
        _ctx.IO.ConfigErrorRecoveryEnableDebugLog = false;
        string? observed = null;
        _ctx.ErrorCallback = (_, _, message) => observed = message;

        NewTestFrame();
        ImGui.ErrorLog("routed to host");

        Assert.That(observed, Is.EqualTo("routed to host"));
    }

    #endregion

    #region Window-state recovery

    /// <summary>
    /// Verifies that the window-state recovery unwinds an unbalanced group and identifier stack back to
    /// the recorded sizes without leaving the context in a broken state.
    /// </summary>
    [Test]
    public void ErrorRecoveryTryToRecoverWindowState_UnwindsGroupAndIdStacks()
    {
        _ctx.IO.ConfigErrorRecoveryEnableTooltip = false;
        _ctx.IO.ConfigErrorRecoveryEnableDebugLog = false;
        _ctx.IO.ConfigErrorRecoveryEnableAssert = false;

        using (ExpectAssert("Error recovery requires at least one output."))
            NewTestFrame();
        ImGui.Begin("RecoveryHost");

        ImGuiErrorRecoveryState state = new();
        ImGui.ErrorRecoveryStoreState(state);

        // Deliberately leave one group and one identifier scope open.
        ImGui.BeginGroup();
        ImGui.PushID("unbalanced");

        Assert.That(_ctx.GroupStack.Count, Is.EqualTo(state.SizeOfGroupStack + 1));
        Assert.That(ImGui.GetCurrentWindow().IDStack.Count, Is.EqualTo(state.SizeOfIDStack + 1));

        ImGui.ErrorRecoveryTryToRecoverWindowState(state);

        Assert.That(_ctx.GroupStack.Count, Is.EqualTo(state.SizeOfGroupStack));
        Assert.That(ImGui.GetCurrentWindow().IDStack.Count, Is.EqualTo(state.SizeOfIDStack));

        ImGui.End();
        ImGui.EndFrame();
    }

    #endregion
}