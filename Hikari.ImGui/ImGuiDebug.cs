#nullable enable
/*================================================================================================
 * DEBUG TOOLS: LOGGING, DEBUG LOG, AND DIAGNOSTIC HELPERS
 *================================================================================================
 * ROLE IN ENGINE:
 * Provides the canonical Dear ImGui debug instrumentation: the text-capture facility that mirrors
 * every rendered string into a TTY, a file, the clipboard, or a buffer; the debug log that records
 * internal event traces so a maintainer can see why a frame behaved as it did; and the small
 * diagnostic draw and locate helpers used by the metrics window and the item picker. It also provides
 * the Metrics/Debugger window itself, which reports the frame's windows, draw lists, viewports, popups,
 * tab bars, tables, docking state, settings, allocations, and input state, and draws the requested
 * debug overlays.
 *
 * These facilities observe the engine and never change its layout or interaction results, with one
 * documented exception: while capture is active the canonical clipper stops clipping items
 * (ItemUnclipByLog) and window clipping is suppressed, because otherwise the captured text would
 * omit anything scrolled out of view.
 *
 * SUBSYSTEM CONNECTIONS & DATA FLOW:
 *
 *    ┌──────────────────────────────────────────────────────────────────┐
 *    │ Widgets and core render helpers                                  │
 *    │ • RenderText / RenderTextClipped / Checkbox / TreeNode / tables  │
 *    └──────────────────────────────────────────────────────────────────┘
 *          │ LogRenderedText(text, pos) when g.LogEnabled                │
 *          ▼                                                            │
 *    ┌──────────────────────────────────────────────────────────────────┐
 *    │ Capture state on ImGuiContext                                    │
 *    │ • LogEnabled / LogFlags / LogFile / LogBuffer / LogLinePosY      │
 *    │ • LogDepthRef / LogDepthToExpand / LogLineFirstItem              │
 *    └──────────────────────────────────────────────────────────────────┘
 *          │ LogTextV(formatted)                                        │
 *          ├──────────────► TTY (Console.Out) or file (TextWriter)      │
 *          └──────────────► g.LogBuffer, flushed by LogFinish           │
 *
 *    ┌──────────────────────────────────────────────────────────────────┐
 *    │ Debug log                                                        │
 *    │ • DebugLog / DebugLogV append into DebugLogBuf + DebugLogIndex   │
 *    │ • per-event gating through DebugLogFlags                         │
 *    └──────────────────────────────────────────────────────────────────┘
 *          │ read by ShowDebugLogWindow, drained by its Clear button
 *          ▼
 *
 * STYLIZED USAGE EXAMPLE:
 *
 *   ImGui.LogToBuffer();                 // start capturing
 *   ImGui.Begin("Captured");
 *   ImGui.Text("this line is captured");
 *   ImGui.End();
 *   ImGui.LogFinish();                   // stop; the text is now in the context log buffer
 *
 *   ImGui.DebugLog("frame {0}: {1} items", ImGui.GetFrameCount(), itemCount);
 *   bool open = true;
 *   ImGui.ShowDebugLogWindow(ref open);
 *
 * THREADING & LIFECYCLE NOTES:
 * - All capture and debug-log state lives on the current ImGuiContext and is only valid while
 *   logging is active. Begin()/LogFinish() must be balanced by the caller; finishing is idempotent.
 * - LogToFile owns the file handle it opens and closes it in LogFinish; LogToTTY borrows Console.Out
 *   and never closes it. A capture left running when the context is destroyed leaks nothing managed
 *   but leaves the file handle to the finalizer, so callers should always finish.
 * - No internal synchronization is provided. Every method runs on the frame-submitting thread.
 *================================================================================================*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Hikari.ImGui;

public static partial class ImGui
{
    #region SECTION: LOGGING/CAPTURING (imgui.cpp:16120-16366)

    /// <summary>
    /// Appends already-formatted text to the current capture destination.
    /// (<c>ThirdParty/imgui/imgui.cpp:16128-16140</c>)
    /// </summary>
    /// <param name="g">The context whose capture state receives the text.</param>
    /// <param name="formatted">The text to append.</param>
    /// <remarks>
    /// The canonical helper mirrors the text into <c>g.LogBuffer</c> and writes the buffer to
    /// <c>g.LogFile</c> when a file or stream is attached. The port writes the managed string directly
    /// to the attached <see cref="System.IO.TextWriter"/> and still mirrors it into the buffer, which
    /// keeps <c>LogBuffer</c> equal to the most recent chunk exactly as canonical does. The buffer is
    /// therefore scratch space for file and TTY output, and it accumulates the whole capture for the
    /// destinations that attach no stream, which are <see cref="ImGuiLogFlags.OutputBuffer"/> and
    /// <see cref="ImGuiLogFlags.OutputClipboard"/>.
    /// </remarks>
    private static void LogTextV(ImGuiContext g, string formatted)
    {
        if (g.LogFile != null)
        {
            g.LogBuffer.Clear();
            g.LogBuffer.Append(formatted);
            g.LogFile.Write(formatted);
        }
        else
        {
            g.LogBuffer.Append(formatted);
        }
    }

    /// <summary>
    /// Writes text straight to the active capture destination without rendering it.
    /// (<c>ThirdParty/imgui/imgui.cpp:16142-16152</c>, declared <c>ThirdParty/imgui/imgui.h:1006</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// A no-op when capture is inactive, which lets callers log unconditionally.
    /// </remarks>
    public static void LogText(string format, params object?[] args)
    {
        ImGuiContext g = GetContext();
        if (!g.LogEnabled)
            return;
        LogTextV(g, FormatLogText(format, args));
    }

    /// <summary>
    /// Writes text straight to the active capture destination, taking the argument array directly.
    /// (<c>ThirdParty/imgui/imgui.cpp:16154-16161</c>, declared <c>ThirdParty/imgui/imgui.h:1007</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    public static void LogTextV(string format, object?[] args) => LogText(format, args);

    /// <summary>
    /// Captures a rendered text run, splitting it into lines and indenting it to the current tree depth.
    /// (<c>ThirdParty/imgui/imgui.cpp:16166-16222</c>, declared <c>ThirdParty/imgui/imgui_internal.h:3735</c>)
    /// </summary>
    /// <param name="refPos">
    /// The screen position the run was emitted at, or <see langword="null"/> when the caller has no
    /// position to offer. The position decides whether a carriage return is emitted first.
    /// </param>
    /// <param name="text">The text run as rendered.</param>
    /// <param name="textEnd">
    /// <see langword="null"/> to stop the captured run at the first <c>##</c> marker, as
    /// <see cref="FindRenderedTextEnd"/> does; any non-null value declares that the caller has already
    /// resolved the run's display range, so the whole of <paramref name="text"/> is captured verbatim.
    /// </param>
    /// <remarks>
    /// <para>
    /// The canonical signature takes raw pointers for both ends of the run and tolerates a null
    /// <c>text_end</c>; the managed port takes the run as a string and an optional stop marker, which is
    /// the same convention the other text helpers in this port use.
    /// </para>
    /// <para>
    /// A new line is emitted when the incoming position is more than one frame padding below the
    /// position of the previous captured item, which is how canonical detects that the caller moved to a
    /// new visual row. Indentation is four spaces per tree level for the first item on a line and a
    /// single space for continuations.
    /// </para>
    /// <para>
    /// <c>LogNextPrefix</c> and <c>LogNextSuffix</c> are consumed by the first call and re-entered
    /// recursively, matching canonical: a widget can announce decoration such as button brackets once,
    /// and both the decoration and the label are captured in order.
    /// </para>
    /// </remarks>
    internal static void LogRenderedText(ImVec2? refPos, string text, string? textEnd = null)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;

        string? prefix = g.LogNextPrefix;
        string? suffix = g.LogNextSuffix;
        g.LogNextPrefix = null;
        g.LogNextSuffix = null;

        string rendered = textEnd == null ? FindRenderedTextEnd(text) : text;

        // Row-change detection: canonical compares against the previous captured row and allows for one
        // frame padding or item spacing before declaring a new line.
        bool logNewLine = refPos.HasValue
            && refPos.Value.Y > g.LogLinePosY + MathF.Max(g.Style.FramePadding.Y, g.Style.ItemSpacing.Y) + 1.0f;
        if (refPos.HasValue)
            g.LogLinePosY = refPos.Value.Y;
        if (logNewLine)
        {
            LogText("\n");
            g.LogLineFirstItem = true;
        }

        if (prefix != null)
            // The decoration is captured verbatim: canonical passes an explicit end pointer for it, so a
            // decoration that itself begins with "##" is not mistaken for an identifier marker.
            LogRenderedText(refPos, prefix, prefix);

        // A capture can outlive the tree that started it, so the reference depth is clamped downward as
        // the walk leaves nested nodes, matching canonical.
        if (g.LogDepthRef > window.DC.TreeDepth)
            g.LogDepthRef = window.DC.TreeDepth;
        int treeDepth = window.DC.TreeDepth - g.LogDepthRef;

        int remaining = 0;
        for (;;)
        {
            int lineStart = remaining;
            int lineBreak = rendered.IndexOf('\n', lineStart);
            bool isLastLine = lineBreak < 0;
            int lineEnd = isLastLine ? rendered.Length : lineBreak;

            // An empty final line is not emitted, so a trailing newline does not add a blank entry.
            if (lineStart != lineEnd || !isLastLine)
            {
                int indentation = g.LogLineFirstItem ? treeDepth * 4 : 1;
                LogText("{0}{1}", new string(' ', indentation), rendered[lineStart..lineEnd]);
                g.LogLineFirstItem = false;
                if (!isLastLine)
                {
                    LogText("\n");
                    g.LogLineFirstItem = true;
                }
            }

            if (isLastLine)
                break;
            remaining = lineEnd + 1;
        }

        if (suffix != null)
            LogRenderedText(refPos, suffix, suffix);
    }

    /// <summary>
    /// Arms the capture facility for one of the four output destinations.
    /// (<c>ThirdParty/imgui/imgui.cpp:16225-16241</c>, declared <c>ThirdParty/imgui/imgui_internal.h:3733</c>)
    /// </summary>
    /// <param name="flags">Exactly one of the <see cref="ImGuiLogFlags"/> output bits.</param>
    /// <param name="autoOpenDepth">
    /// The tree depth at which nodes should open themselves while capturing, or a negative value to use
    /// <see cref="ImGuiContext.LogDepthToExpandDefault"/>.
    /// </param>
    /// <remarks>
    /// The context must not already be capturing and must have no pending capture output, matching the
    /// canonical assertions. The destination stream is attached by the caller after this returns.
    /// </remarks>
    internal static void LogBegin(ImGuiLogFlags flags, int autoOpenDepth)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;

        ImGuiLogFlags outputMask = flags & ImGuiLogFlags.OutputMask_;
        ImAssert.Check(!g.LogEnabled, "LogBegin called while a capture is already active.");
        ImAssert.Check(g.LogFile == null && g.LogBuffer.Empty, "LogBegin called with pending capture output.");
        ImAssert.Check(
            outputMask != ImGuiLogFlags.None && ((int)outputMask & ((int)outputMask - 1)) == 0,
            "LogBegin requires exactly one output flag.");

        g.LogEnabled = true;
        g.ItemUnclipByLog = true;
        g.LogFlags = flags;
        g.LogWindow = window;
        g.LogNextPrefix = null;
        g.LogNextSuffix = null;
        g.LogDepthRef = window.DC.TreeDepth;
        g.LogDepthToExpand = autoOpenDepth >= 0 ? autoOpenDepth : g.LogDepthToExpandDefault;
        g.LogLinePosY = float.MaxValue;
        g.LogLineFirstItem = true;
    }

    /// <summary>
    /// Starts capturing rendered text to standard output.
    /// (<c>ThirdParty/imgui/imgui.cpp:16251-16261</c>, declared <c>ThirdParty/imgui/imgui.h:1001</c>)
    /// </summary>
    /// <param name="autoOpenDepth">The tree depth at which nodes open themselves during capture.</param>
    /// <remarks>
    /// Borrows <see cref="Console.Out"/>; <see cref="LogFinish"/> flushes but never closes it.
    /// </remarks>
    public static void LogToTTY(int autoOpenDepth = -1)
    {
        ImGuiContext g = GetContext();
        if (g.LogEnabled)
            return;
        LogBegin(ImGuiLogFlags.OutputTTY, autoOpenDepth);
        g.LogFile = Console.Out;
    }

    /// <summary>
    /// Starts capturing rendered text to a file opened for appending.
    /// (<c>ThirdParty/imgui/imgui.cpp:16264-16286</c>, declared <c>ThirdParty/imgui/imgui.h:1002</c>)
    /// </summary>
    /// <param name="autoOpenDepth">The tree depth at which nodes open themselves during capture.</param>
    /// <param name="filename">
    /// The destination file, or <see langword="null"/> to use <see cref="ImGuiIO.LogFilename"/>.
    /// </param>
    /// <remarks>
    /// The file is opened in append mode so repeated runs accumulate, matching the canonical binary
    /// append. An unusable path asserts and starts no capture, leaving the caller's next
    /// <see cref="LogFinish"/> a no-op.
    /// </remarks>
    public static void LogToFile(int autoOpenDepth = -1, string? filename = null)
    {
        ImGuiContext g = GetContext();
        if (g.LogEnabled)
            return;

        filename ??= g.IO.LogFilename;
        if (string.IsNullOrEmpty(filename))
            return;

        System.IO.StreamWriter writer;
        try
        {
            writer = new System.IO.StreamWriter(filename, append: true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ImAssert.Check(false, $"LogToFile could not open '{filename}' for appending.");
            return;
        }

        LogBegin(ImGuiLogFlags.OutputFile, autoOpenDepth);
        g.LogFile = writer;
    }

    /// <summary>
    /// Starts capturing rendered text to the clipboard.
    /// (<c>ThirdParty/imgui/imgui.cpp:16289-16295</c>, declared <c>ThirdParty/imgui/imgui.h:1003</c>)
    /// </summary>
    /// <param name="autoOpenDepth">The tree depth at which nodes open themselves during capture.</param>
    /// <remarks>
    /// The clipboard is only written by <see cref="LogFinish"/>, which hands over the whole capture.
    /// </remarks>
    public static void LogToClipboard(int autoOpenDepth = -1)
    {
        ImGuiContext g = GetContext();
        if (g.LogEnabled)
            return;
        LogBegin(ImGuiLogFlags.OutputClipboard, autoOpenDepth);
    }

    /// <summary>
    /// Starts capturing rendered text into <see cref="ImGuiContext.LogBuffer"/>.
    /// (<c>ThirdParty/imgui/imgui.cpp:16297-16303</c>, declared <c>ThirdParty/imgui/imgui_internal.h:3734</c>)
    /// </summary>
    /// <param name="autoOpenDepth">The tree depth at which nodes open themselves during capture.</param>
    internal static void LogToBuffer(int autoOpenDepth = -1)
    {
        ImGuiContext g = GetContext();
        if (g.LogEnabled)
            return;
        LogBegin(ImGuiLogFlags.OutputBuffer, autoOpenDepth);
    }

    /// <summary>
    /// Ends capture and delivers the result to the destination selected by <see cref="LogBegin"/>.
    /// (<c>ThirdParty/imgui/imgui.cpp:16305-16337</c>, declared <c>ThirdParty/imgui/imgui.h:1004</c>)
    /// </summary>
    /// <remarks>
    /// A trailing newline is always appended so the last captured line is terminated. Standard output is
    /// flushed but not closed, a file is closed, the buffer destination needs no delivery, and the
    /// clipboard receives the accumulated text. The capture state is reset either way, which makes a
    /// second call harmless.
    /// </remarks>
    public static void LogFinish()
    {
        ImGuiContext g = GetContext();
        if (!g.LogEnabled)
            return;

        LogText("\n");
        switch (g.LogFlags & ImGuiLogFlags.OutputMask_)
        {
            case ImGuiLogFlags.OutputTTY:
                Console.Out.Flush();
                break;
            case ImGuiLogFlags.OutputFile:
                g.LogFile?.Dispose();
                break;
            case ImGuiLogFlags.OutputBuffer:
                break;
            case ImGuiLogFlags.OutputClipboard:
                if (!g.LogBuffer.Empty)
                    SetClipboardText(g.LogBuffer.CStr());
                break;
            default:
                ImAssert.Check(false, "Invalid capture output flag combination.");
                break;
        }

        g.LogEnabled = false;
        g.ItemUnclipByLog = false;
        g.LogFlags = ImGuiLogFlags.None;
        g.LogFile = null;
        g.LogBuffer.Clear();
    }

    /// <summary>
    /// Submits the canonical row of capture buttons plus the default auto-open depth slider.
    /// (<c>ThirdParty/imgui/imgui.cpp:16341-16366</c>, declared <c>ThirdParty/imgui/imgui.h:1005</c>)
    /// </summary>
    /// <remarks>
    /// Capture starts only after the buttons are submitted, so the buttons themselves are never part of
    /// the captured text. The depth slider edits <see cref="ImGuiContext.LogDepthToExpandDefault"/>, which
    /// subsequent <see cref="LogBegin"/> calls use when the caller passes a negative depth.
    /// </remarks>
    public static void LogButtons()
    {
        ImGuiContext g = GetContext();

        PushID("LogButtons");
        bool logToTty = Button("Log To TTY");
        SameLine();
        bool logToFile = Button("Log To File");
        SameLine();
        bool logToClipboard = Button("Log To Clipboard");
        SameLine();

        PushItemFlag(ImGuiItemFlags.NoTabStop, true);
        SetNextItemWidth(CalcTextSize("999").X);
        int expandDepth = g.LogDepthToExpandDefault;
        if (SliderInt("Default Depth", ref expandDepth, 0, 9))
            g.LogDepthToExpandDefault = expandDepth;
        PopItemFlag();
        PopID();

        if (logToTty)
            LogToTTY();
        if (logToFile)
            LogToFile();
        if (logToClipboard)
            LogToClipboard();
    }

    /// <summary>
    /// Formats a capture message with the invariant culture.
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <returns>The formatted text, or <paramref name="format"/> unchanged when no arguments are given.</returns>
    /// <remarks>
    /// Capture files are compared between machines during debugging, so the host locale must not influence
    /// the output; the invariant culture is used for the same reason the settings writer uses it.
    /// </remarks>
    private static string FormatLogText(string format, object?[] args)
        => args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);

    #endregion

    #region SECTION: DEBUG LOG WINDOW (imgui.cpp:24031-24201)

    /// <summary>
    /// Appends a formatted line to the debug log buffer and mirrors it to the enabled outputs.
    /// (<c>ThirdParty/imgui/imgui.cpp:24034-24041</c>, declared <c>ThirdParty/imgui/imgui.h:1186</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// Canonical gates this behind the <c>IMGUI_DEBUG_LOG_*</c> macros rather than inside the function;
    /// this port offers the same gating through <see cref="DebugLogEvent"/> and <see cref="DebugLogError"/>.
    /// </remarks>
    public static void DebugLog(string format, params object?[] args) => DebugLogV(format, args);

    /// <summary>
    /// Appends a formatted line to the debug log buffer, taking the argument array directly.
    /// (<c>ThirdParty/imgui/imgui.cpp:24042-24072</c>, declared <c>ThirdParty/imgui/imgui.h:1187</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// <para>
    /// Every line is prefixed with the frame number, and with the context name when one is set, then
    /// recorded in <see cref="ImGuiContext.DebugLogIndex"/> so the debug-log window can page through it
    /// with a clipper. The newly appended slice, and only that slice, is mirrored to the outputs enabled
    /// in <see cref="ImGuiContext.DebugLogFlags"/>.
    /// </para>
    /// <para>
    /// Canonical also tests <c>OutputToTestEngine</c>; this port has no test engine, so that flag is
    /// accepted and ignored, which is documented on the flag itself.
    /// </para>
    /// </remarks>
    public static void DebugLogV(string format, object?[] args)
    {
        ImGuiContext g = GetContext();

        int oldSize = g.DebugLogBuf.Size;
        if (g.ContextName.Length != 0)
            g.DebugLogBuf.AppendF("[{0}] [{1:D5}] ", g.ContextName, g.FrameCount);
        else
            g.DebugLogBuf.AppendF("[{1:D5}] ", g.ContextName, g.FrameCount);
        g.DebugLogBuf.AppendF(format, args);
        g.DebugLogIndex.Append(g.DebugLogBuf.CStr(), oldSize, g.DebugLogBuf.Size);

        string full = g.DebugLogBuf.CStr();
        string appended = oldSize < full.Length ? full[oldSize..] : string.Empty;
        if ((g.DebugLogFlags & ImGuiDebugLogFlags.OutputToTTY) != 0)
            Console.Out.Write(appended);
        if ((g.DebugLogFlags & ImGuiDebugLogFlags.OutputToDebugger) != 0)
        {
            // Canonical prefixes the debugger output with this literal before the message itself.
            Debug.Write("[imgui] ");
            Debug.Write(appended);
        }
    }

    /// <summary>
    /// Appends a debug-log line only when its event category is enabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:256-267</c>)
    /// </summary>
    /// <param name="eventFlag">The single event flag the message belongs to.</param>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// Realizes the canonical <c>IMGUI_DEBUG_LOG_XXX</c> macros, which test the flag at the call site so
    /// hot paths pay only a bit test when the category is disabled.
    /// </remarks>
    internal static void DebugLogEvent(ImGuiDebugLogFlags eventFlag, string format, params object?[] args)
    {
        ImGuiContext g = GetContext();
        if ((g.DebugLogFlags & eventFlag) != 0)
            DebugLog(format, args);
    }

    /// <summary>
    /// Appends an error line to the debug log, or counts it as skipped when error logging is disabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:255</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// The skipped count is surfaced by the debug-log window and highlights the Errors checkbox, which is
    /// how canonical warns a maintainer that error output was suppressed.
    /// </remarks>
    internal static void DebugLogError(string format, params object?[] args)
    {
        ImGuiContext g = GetContext();
        if ((g.DebugLogFlags & ImGuiDebugLogFlags.EventError) != 0)
            DebugLog(format, args);
        else
            g.DebugLogSkippedErrors++;
    }

    /// <summary>
    /// Lays out the following item on the same line when it still fits, otherwise leaves it on a new line.
    /// (<c>ThirdParty/imgui/imgui.cpp:24073-24081</c>)
    /// </summary>
    /// <param name="size">The size of the item that is about to be submitted.</param>
    /// <remarks>
    /// Used by the debug-log flag row so the checkboxes wrap instead of overflowing a narrow window. The
    /// canonical comment records this as a manual stand-in for the layout system it intends to build.
    /// </remarks>
    private static void SameLineOrWrap(ImVec2 size)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        ImVec2 pos = new(window.DC.CursorPosPrevLine.X + g.Style.ItemSpacing.X, window.DC.CursorPosPrevLine.Y);
        if (window.WorkRect.Contains(new ImRect(pos, pos + size)))
            SameLine();
    }

    /// <summary>
    /// Submits one debug-log event checkbox, with the two-frame auto-disable and skipped-error highlight.
    /// (<c>ThirdParty/imgui/imgui.cpp:24082-24106</c>)
    /// </summary>
    /// <param name="name">The checkbox label, which is also the canonical short event name.</param>
    /// <param name="flags">The single event flag this checkbox toggles.</param>
    /// <remarks>
    /// Clicking while holding Shift enables the category for two frames only, which is the canonical way to
    /// sample a spammy category without leaving it on. When error output was skipped the label is tinted
    /// toward red and the tooltip reports how many messages were suppressed.
    /// </remarks>
    private static void ShowDebugLogFlag(string name, ImGuiDebugLogFlags flags)
    {
        ImGuiContext g = GetContext();
        ImVec2 size = new(
            GetFrameHeight() + g.Style.ItemInnerSpacing.X + CalcTextSize(name).X,
            GetFrameHeight());
        SameLineOrWrap(size);

        bool highlightErrors = flags == ImGuiDebugLogFlags.EventError && g.DebugLogSkippedErrors > 0;
        if (highlightErrors)
            PushStyleColor(ImGuiCol.Text, LerpColor(GetStyleColorVec4(ImGuiCol.Text), new ImVec4(1.0f, 0.0f, 0.0f, 1.0f), 0.30f));

        int flagsValue = (int)g.DebugLogFlags;
        bool changed = CheckboxFlags(name, ref flagsValue, (int)flags);
        g.DebugLogFlags = (ImGuiDebugLogFlags)flagsValue;
        if (changed && g.IO.KeyShift && (g.DebugLogFlags & flags) != 0)
        {
            g.DebugLogAutoDisableFrames = 2;
            g.DebugLogAutoDisableFlags |= flags;
        }

        if (highlightErrors)
        {
            PopStyleColor();
            SetItemTooltip("{0} past errors skipped.", g.DebugLogSkippedErrors);
        }
        else
        {
            SetItemTooltip("Hold Shift when clicking to enable for 2 frames only (useful for spammy log entries)");
        }
    }

    /// <summary>
    /// Submits a debug-log output checkbox that only needs to toggle a single flag bit.
    /// (<c>ThirdParty/imgui/imgui.cpp:24150-24155</c>)
    /// </summary>
    /// <param name="name">The checkbox label, which is also the canonical flag name.</param>
    /// <param name="flags">The single output flag this checkbox toggles.</param>
    private static void ShowDebugLogOutputFlag(string name, ImGuiDebugLogFlags flags)
    {
        ImGuiContext g = GetContext();
        int flagsValue = (int)g.DebugLogFlags;
        CheckboxFlags(name, ref flagsValue, (int)flags);
        g.DebugLogFlags = (ImGuiDebugLogFlags)flagsValue;
    }

    /// <summary>
    /// Submits the canonical Debug Log window.
    /// (<c>ThirdParty/imgui/imgui.cpp:24107-24178</c>, declared <c>ThirdParty/imgui/imgui.h:416</c>)
    /// </summary>
    /// <param name="pOpen">Tracks whether the window is open; cleared when the user closes it.</param>
    /// <remarks>
    /// <para>
    /// The window shows one checkbox per event category plus an "All" toggle that excludes the spammy
    /// input-routing category, the Clear/Copy buttons, the output configuration popup, and a clipped child
    /// that lists the buffered lines. Hovering a line that contains a <c>0xXXXXXXXX</c> identifier offers
    /// to locate that item, matching canonical.
    /// </para>
    /// <para>
    /// The clipper category is temporarily disabled while the window lists its own buffer, because the
    /// clipper emits debug-log lines of its own and would otherwise feed itself.
    /// </para>
    /// </remarks>
    public static void ShowDebugLogWindow(ref bool pOpen)
    {
        ImGuiContext g = GetContext();
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSize) == 0)
            SetNextWindowSize(new ImVec2(0.0f, GetFontSize() * 12.0f), ImGuiCond.FirstUseEver);
        if (!Begin("Dear ImGui Debug Log", ref pOpen) || GetCurrentWindow().BeginCount > 1)
        {
            End();
            return;
        }

        ImGuiDebugLogFlags allEnableFlags = ImGuiDebugLogFlags.EventMask_ & ~ImGuiDebugLogFlags.EventInputRouting;
        int allFlagsValue = (int)g.DebugLogFlags;
        if (CheckboxFlags("All", ref allFlagsValue, (int)allEnableFlags))
            g.DebugLogFlags = (ImGuiDebugLogFlags)allFlagsValue;
        SetItemTooltip("(except InputRouting which is spammy)");

        ShowDebugLogFlag("Errors", ImGuiDebugLogFlags.EventError);
        ShowDebugLogFlag("ActiveId", ImGuiDebugLogFlags.EventActiveId);
        ShowDebugLogFlag("Clipper", ImGuiDebugLogFlags.EventClipper);
        ShowDebugLogFlag("Docking", ImGuiDebugLogFlags.EventDocking);
        ShowDebugLogFlag("Focus", ImGuiDebugLogFlags.EventFocus);
        ShowDebugLogFlag("IO", ImGuiDebugLogFlags.EventIO);
        ShowDebugLogFlag("Font", ImGuiDebugLogFlags.EventFont);
        ShowDebugLogFlag("Nav", ImGuiDebugLogFlags.EventNav);
        ShowDebugLogFlag("Popup", ImGuiDebugLogFlags.EventPopup);
        ShowDebugLogFlag("Selection", ImGuiDebugLogFlags.EventSelection);
        ShowDebugLogFlag("Table", ImGuiDebugLogFlags.EventTable);
        ShowDebugLogFlag("Viewport", ImGuiDebugLogFlags.EventViewport);
        ShowDebugLogFlag("InputRouting", ImGuiDebugLogFlags.EventInputRouting);

        if (SmallButton("Clear"))
        {
            g.DebugLogBuf.Clear();
            g.DebugLogIndex.Clear();
            g.DebugLogSkippedErrors = 0;
        }
        SameLine();
        if (SmallButton("Copy"))
            SetClipboardText(g.DebugLogBuf.CStr());
        SameLine();
        if (SmallButton("Configure Outputs.."))
            OpenPopup("Outputs");
        if (BeginPopup("Outputs"))
        {
            ShowDebugLogOutputFlag("OutputToTTY", ImGuiDebugLogFlags.OutputToTTY);
            ShowDebugLogOutputFlag("OutputToDebugger", ImGuiDebugLogFlags.OutputToDebugger);

            // The port has no test engine, so the canonical test-engine output is permanently unavailable
            // and the checkbox is shown disabled rather than silently accepting a flag that does nothing.
            BeginDisabled();
            ShowDebugLogOutputFlag("OutputToTestEngine", ImGuiDebugLogFlags.OutputToTestEngine);
            EndDisabled();
            EndPopup();
        }

        BeginChild(
            "##log",
            new ImVec2(0.0f, 0.0f),
            ImGuiChildFlags.Borders,
            ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.AlwaysHorizontalScrollbar);

        // Suppress the clipper category while listing, so the listing does not log itself.
        ImGuiDebugLogFlags backupLogFlags = g.DebugLogFlags;
        g.DebugLogFlags &= ~ImGuiDebugLogFlags.EventClipper;

        ImGuiListClipper clipper = new();
        clipper.Begin(g.DebugLogIndex.Size());
        string logText = g.DebugLogBuf.CStr();
        while (clipper.Step())
        {
            for (int lineNo = clipper.DisplayStart; lineNo < clipper.DisplayEnd; lineNo++)
            {
                int begin = g.DebugLogIndex.GetLineBegin(lineNo);
                int end = g.DebugLogIndex.GetLineEnd(lineNo);
                if (begin < 0 || begin > logText.Length)
                    continue;
                end = Math.Clamp(end, begin, logText.Length);
                DebugTextUnformattedWithLocateItem(logText[begin..end]);
            }
        }
        g.DebugLogFlags = backupLogFlags;

        if (GetScrollY() >= GetScrollMaxY())
            SetScrollHereY(1.0f);
        EndChild();

        End();
    }

    /// <summary>
    /// Draws one debug-log line and offers to locate any identifier it contains.
    /// (<c>ThirdParty/imgui/imgui.cpp:24181-24200</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4141</c>)
    /// </summary>
    /// <param name="line">The log line to draw.</param>
    /// <remarks>
    /// Canonical scans the hovered line for <c>0x</c> followed by up to eight hexadecimal digits and, for
    /// each, narrows the last-item rectangle to that run of characters before calling
    /// <see cref="DebugLocateItemOnHover"/>. A ninth hexadecimal digit disqualifies the match, which is how
    /// canonical avoids treating a longer number as a 32-bit identifier.
    /// </remarks>
    internal static void DebugTextUnformattedWithLocateItem(string line)
    {
        TextUnformatted(line);
        if (!IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            return;

        ImGuiContext g = GetContext();
        ImRect textRect = g.LastItemData.Rect;
        for (int p = 0; p <= line.Length - 10; p++)
        {
            if (line[p] != '0' || (line[p + 1] != 'x' && line[p + 1] != 'X'))
                continue;

            // Canonical parses the token with sscanf and rejects it outright when a hexadecimal digit is
            // still present at offset ten, which discards identifiers longer than eight digits.
            if (p + 10 < line.Length && Uri.IsHexDigit(line[p + 10]))
                continue;

            // Consume at most eight hexadecimal digits for the identifier itself.
            int hexStart = p + 2;
            int hexEnd = hexStart;
            while (hexEnd < line.Length && hexEnd - hexStart < 8 && Uri.IsHexDigit(line[hexEnd]))
                hexEnd++;
            if (hexEnd == hexStart)
                continue;

            uint id = uint.Parse(
                line.AsSpan(hexStart, hexEnd - hexStart),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            ImVec2 p0 = CalcTextSize(line[..p]);
            ImVec2 p1 = CalcTextSize(line.Substring(p, 10));
            g.LastItemData.Rect = new ImRect(
                textRect.Min + new ImVec2(p0.X, 0.0f),
                textRect.Min + new ImVec2(p0.X + p1.X, p1.Y));
            if (IsMouseHoveringRect(g.LastItemData.Rect.Min, g.LastItemData.Rect.Max, true))
                DebugLocateItemOnHover(id);
            p += 10;
        }
    }

    /// <summary>
    /// Linearly interpolates two colours.
    /// </summary>
    /// <param name="a">The colour at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The colour at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation factor.</param>
    /// <returns>The interpolated colour.</returns>
    /// <remarks>
    /// The port has no scalar-colour <c>ImLerp</c> in the shared math helpers, so the debug tools keep this
    /// local instead of widening the public math surface for a debug-only tint.
    /// </remarks>
    private static ImVec4 LerpColor(ImVec4 a, ImVec4 b, float t)
        => new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t);

    #endregion

    #region SECTION: OTHER DEBUG TOOLS (imgui.cpp:24203-24544)

    /// <summary>
    /// The colour used by every locate-item and item-picker highlight.
    /// (<c>ThirdParty/imgui/imgui.cpp:24238</c>)
    /// </summary>
    /// <remarks>
    /// Canonical <c>IM_COL32(0, 255, 0, 255)</c>; this port stores colours as packed <c>ABGR</c> in a
    /// <see cref="uint"/>, so the literal encodes alpha 255, blue 0, green 255, red 0.
    /// </remarks>
    private const uint DebugLocateItemColor = 0xFF00FF00;

    /// <summary>
    /// Draws a small cross at the current cursor position in the current window's draw list.
    /// (<c>ThirdParty/imgui/imgui.cpp:24207-24214</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4138</c>)
    /// </summary>
    /// <param name="col">The highlight colour.</param>
    internal static void DebugDrawCursorPos(uint col = 0xFF0000FF)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        ImVec2 pos = window.DC.CursorPos;
        window.DrawList.AddLineV(pos.X, pos.Y - 3.0f, pos.Y + 4.0f, col);
        window.DrawList.AddLineH(pos.X - 3.0f, pos.X + 4.0f, pos.Y, col);
    }

    /// <summary>
    /// Draws a ten-pixel bracket around the cursor column across the current line's height.
    /// (<c>ThirdParty/imgui/imgui.cpp:24217-24227</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4139</c>)
    /// </summary>
    /// <param name="col">The highlight colour.</param>
    /// <remarks>
    /// The line extent is taken from the previous line while the current item shares a line, because in
    /// that case the current line has not been measured yet.
    /// </remarks>
    internal static void DebugDrawLineExtents(uint col = 0xFF0000FF)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        float currX = window.DC.CursorPos.X;
        float lineY1 = window.DC.IsSameLine ? window.DC.CursorPosPrevLine.Y : window.DC.CursorPos.Y;
        float lineY2 = lineY1 + (window.DC.IsSameLine ? window.DC.PrevLineSize.Y : window.DC.CurrLineSize.Y);
        window.DrawList.AddLineH(currX - 5.0f, currX + 5.0f, lineY1, col);
        window.DrawList.AddLineV(currX - 0.5f, lineY1, lineY2, col);
        window.DrawList.AddLineH(currX - 5.0f, currX + 5.0f, lineY2, col);
    }

    /// <summary>
    /// Draws the last item's rectangle into the foreground draw list so it stays visible.
    /// (<c>ThirdParty/imgui/imgui.cpp:24230-24235</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4140</c>)
    /// </summary>
    /// <param name="col">The highlight colour.</param>
    internal static void DebugDrawItemRect(uint col = 0xFF0000FF)
    {
        ImGuiContext g = GetContext();
        GetForegroundDrawList(g.CurrentWindow!).AddRect(g.LastItemData.Rect.Min, g.LastItemData.Rect.Max, col);
    }

    /// <summary>
    /// Requests that the item with the given identifier be highlighted for the next two frames.
    /// (<c>ThirdParty/imgui/imgui.cpp:24240-24246</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4142</c>)
    /// </summary>
    /// <param name="targetId">The identifier of the item to locate.</param>
    /// <remarks>
    /// Only one item can be located at a time, and the request is consumed by
    /// <see cref="DebugLocateItemResolveWithLastItem"/> when that item is next submitted.
    /// </remarks>
    internal static void DebugLocateItem(uint targetId)
    {
        ImGuiContext g = GetContext();
        g.DebugLocateId = targetId;
        g.DebugLocateFrames = 2;
        g.DebugBreakInLocateId = false;
    }

    /// <summary>
    /// Highlights a rectangle around the currently hovered item and offers to break into its call site.
    /// (<c>ThirdParty/imgui/imgui.cpp:24249-24264</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4143</c>)
    /// </summary>
    /// <param name="targetId">The identifier to locate when the just-submitted item is hovered.</param>
    /// <remarks>
    /// Only the item that was submitted immediately before this call can be located, so the function is
    /// only meaningful in reaction to a hover. The debugger-break offer is suppressed unless the host has
    /// declared that a debugger is attached, because the canonical break would otherwise crash the process.
    /// </remarks>
    internal static void DebugLocateItemOnHover(uint targetId)
    {
        if (targetId == 0
            || !IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem | ImGuiHoveredFlags.AllowWhenBlockedByPopup))
        {
            return;
        }

        ImGuiContext g = GetContext();
        DebugLocateItem(targetId);
        GetForegroundDrawList(g.CurrentWindow!).AddRect(
            g.LastItemData.Rect.Min - new ImVec2(3.0f, 3.0f),
            g.LastItemData.Rect.Max + new ImVec2(3.0f, 3.0f),
            DebugLocateItemColor);

        if (g.IO.ConfigDebugIsDebuggerPresent && g.MouseStationaryTimer > 1.0f)
        {
            DebugBreakButtonTooltip(false, "in ItemAdd()");
            if (IsKeyChordPressed(g.DebugBreakKeyChord))
                g.DebugBreakInLocateId = true;
        }
    }

    /// <summary>
    /// Draws the locate highlight for the item that was last submitted, and consumes the pending request.
    /// (<c>ThirdParty/imgui/imgui.cpp:24266-24283</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4144</c>)
    /// </summary>
    /// <remarks>
    /// The rectangle is expanded by three pixels and a line is drawn from the mouse to the nearest point on
    /// it, so a maintainer can see which item the mouse is being attributed to. The pending identifier is
    /// cleared before drawing so a single request is consumed by a single item.
    /// </remarks>
    internal static void DebugLocateItemResolveWithLastItem()
    {
        ImGuiContext g = GetContext();

        // The debug break is requested by the hover path and consumed here, where the stack still reaches
        // the item's own submission code.
        if (g.DebugBreakInLocateId)
            Debugger.Break();

        ImRect r = g.LastItemData.Rect;
        g.DebugLocateId = 0;

        ImDrawList drawList = GetForegroundDrawList(g.CurrentWindow!);
        r.Expand(3.0f);
        ImVec2 p1 = g.IO.MousePos;
        // Clamp the mouse position onto the rectangle so the connector line ends on its border.
        ImVec2 p2 = new(
            p1.X < r.Min.X ? r.Min.X : (p1.X > r.Max.X ? r.Max.X : p1.X),
            p1.Y < r.Min.Y ? r.Min.Y : (p1.Y > r.Max.Y ? r.Max.Y : p1.Y));
        drawList.AddRect(r.Min, r.Max, DebugLocateItemColor);
        drawList.AddLine(p1, p2, DebugLocateItemColor);
    }

    /// <summary>
    /// Starts the interactive item picker.
    /// (<c>ThirdParty/imgui/imgui.cpp:24285-24289</c>, declared <c>ThirdParty/imgui/imgui.h:1183</c>)
    /// </summary>
    /// <remarks>
    /// The picker is advanced once per frame by <see cref="UpdateDebugToolItemPicker"/>, which the frame
    /// lifecycle calls for as long as the picker is active.
    /// </remarks>
    public static void DebugStartItemPicker() => GetContext().DebugItemPickerActive = true;

    /// <summary>
    /// Advances the interactive item picker and reports the hovered identifier.
    /// (<c>ThirdParty/imgui/imgui.cpp:24292-24323</c>)
    /// </summary>
    /// <remarks>
    /// <para>
    /// While active, the picker takes over the hand cursor and shows its own tooltip, reports the identifier
    /// hovered on the previous frame, aborts on Escape, and records the identifier the user clicks as
    /// <see cref="ImGuiContext.DebugItemPickerBreakId"/>. Holding Ctrl and Shift lets the user remap which
    /// mouse button picks, which is useful when the button being debugged is the one under test.
    /// </para>
    /// <para>
    /// The break identifier is cleared every frame so a stale pick is never re-reported.
    /// </para>
    /// </remarks>
    internal static void UpdateDebugToolItemPicker()
    {
        ImGuiContext g = GetContext();
        g.DebugItemPickerBreakId = 0;
        if (!g.DebugItemPickerActive)
            return;

        uint hoveredId = g.HoveredIdPreviousFrame;
        SetMouseCursor(ImGuiMouseCursor.Hand);
        if (IsKeyPressed(ImGuiKey.Escape))
            g.DebugItemPickerActive = false;

        bool changeMapping = g.IO.KeyMods == (ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift);
        if (!changeMapping && IsMouseClicked((ImGuiMouseButton)g.DebugItemPickerMouseButton) && hoveredId != 0)
        {
            g.DebugItemPickerBreakId = hoveredId;
            g.DebugItemPickerActive = false;
        }

        for (int mouseButton = 0; mouseButton < 3; mouseButton++)
        {
            if (changeMapping && IsMouseClicked((ImGuiMouseButton)mouseButton))
                g.DebugItemPickerMouseButton = (byte)mouseButton;
        }

        SetNextWindowBgAlpha(0.70f);
        if (!BeginTooltip())
            return;
        Text("HoveredId: 0x{0:X8}", hoveredId);
        Text("Press ESC to abort picking.");
        string[] mouseButtonNames = { "Left", "Right", "Middle" };
        if (changeMapping)
            Text("Remap w/ Ctrl+Shift: click anywhere to select new mouse button.");
        else
            TextColored(
                GetStyleColorVec4(hoveredId != 0 ? ImGuiCol.Text : ImGuiCol.TextDisabled),
                "Click {0} Button to break in debugger! (remap w/ Ctrl+Shift)",
                mouseButtonNames[g.DebugItemPickerMouseButton]);
        EndTooltip();
    }

    /// <summary>
    /// Clears the one-shot debug-break requests after they have been armed for a frame.
    /// (<c>ThirdParty/imgui/imgui.cpp:23296-23303</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4145</c>)
    /// </summary>
    /// <remarks>
    /// The three request fields live in the hot data of their own subsystems rather than beside the other
    /// debug state, which is why canonical clears them here instead of at their declaration sites.
    /// </remarks>
    internal static void DebugBreakClearData()
    {
        ImGuiContext g = GetContext();
        g.DebugBreakInWindow = 0;
        g.DebugBreakInTable = 0;
        g.DebugBreakInShortcutRouting = ImGuiKey.None;
    }

    /// <summary>
    /// Shows the tooltip that explains how to trigger a debugger break.
    /// (<c>ThirdParty/imgui/imgui.cpp:23305-23315</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4147</c>)
    /// </summary>
    /// <param name="keyboardOnly">
    /// Whether only the keyboard route is offered, which is used when a click would disturb the state being
    /// debugged.
    /// </param>
    /// <param name="descriptionOfLocation">A description of where the break will occur.</param>
    internal static void DebugBreakButtonTooltip(bool keyboardOnly, string descriptionOfLocation)
    {
        if (!BeginItemTooltip())
            return;
        Text("To call IM_DEBUG_BREAK() {0}:", descriptionOfLocation);
        Separator();
        TextUnformatted(keyboardOnly
            ? "- Press 'Pause/Break' on keyboard."
            : "- Press 'Pause/Break' on keyboard.\n- or Click (may alter focus/active id).\n- or navigate using keyboard and press space.");
        Separator();
        TextUnformatted("Choose one way that doesn't interfere with what you are trying to debug!\nYou need a debugger attached or this will crash!");
        EndTooltip();
    }

    /// <summary>
    /// Submits a button that arms a debugger break without taking focus or input ownership.
    /// (<c>ThirdParty/imgui/imgui.cpp:23319-23352</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4146</c>)
    /// </summary>
    /// <param name="label">The button label.</param>
    /// <param name="descriptionOfLocation">A description of where the break will occur.</param>
    /// <returns>Whether the button was activated this frame.</returns>
    /// <remarks>
    /// The button deliberately bypasses the ordinary button helpers so that arming a break changes as little
    /// of the surrounding state as possible; that is also why it draws its own frame rather than delegating
    /// to <see cref="Button(string, ImVec2)"/>.
    /// </remarks>
    internal static bool DebugBreakButton(string label, string descriptionOfLocation)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetContext();
        uint id = window.GetID(label);
        ImVec2 labelSize = CalcTextSize(label, hideTextAfterDoubleHash: true);
        ImVec2 pos = window.DC.CursorPos + new ImVec2(0.0f, window.DC.CurrLineTextBaseOffset);
        ImVec2 size = new(labelSize.X + g.Style.FramePadding.X * 2.0f, labelSize.Y);

        ImRect bb = new(pos, pos + size);
        ItemSize(size, 0.0f);
        if (!ItemAdd(bb, id))
            return false;

        bool hovered = ItemHoverable(bb, id, g.CurrentItemFlags);
        bool pressed = hovered
            && (IsKeyChordPressed(g.DebugBreakKeyChord) || IsMouseClicked(ImGuiMouseButton.Left) || g.NavActivateId == id);
        DebugBreakButtonTooltip(false, descriptionOfLocation);

        ImVec4 col4f = GetStyleColorVec4(hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
        ColorConvertRGBtoHSV(col4f.X, col4f.Y, col4f.Z, out float h, out float s, out float v);
        ColorConvertHSVtoRGB(h + 0.20f, s, v, out float r, out float g2, out float b);
        col4f = new ImVec4(r, g2, b, col4f.W);

        RenderNavCursor(bb, id);
        RenderFrame(bb.Min, bb.Max, GetColorU32(col4f), true, g.Style.FrameRounding);
        RenderTextClipped(bb.Min, bb.Max, label, null, labelSize, g.Style.ButtonTextAlign, bb);
        return pressed;
    }

    #endregion

    #region SECTION: METRICS WINDOW SUPPORT (imgui.cpp:5317-5340, 11651-11662)

    /// <summary>
    /// Records one allocation or release against the per-frame allocation audit.
    /// (<c>ThirdParty/imgui/imgui.cpp:5317-5340</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4137</c>)
    /// </summary>
    /// <param name="info">The record that receives the accounting.</param>
    /// <param name="frameCount">The frame the operation belongs to.</param>
    /// <param name="ptr">The canonical allocation address, which this port does not use.</param>
    /// <param name="size">
    /// The allocated size in bytes, or -1 to report a release, matching the canonical
    /// <c>(size_t)-1</c> sentinel.
    /// </param>
    /// <remarks>
    /// <para>
    /// The engine's guidance is that idle and repeating frames should not allocate, so the audit keeps the
    /// last six frames that had any activity rather than an unbounded history. When the stored entry belongs
    /// to an older frame, the ring advances and a fresh entry is started, which means a quiet frame is never
    /// represented and the report always shows six frames that did something.
    /// </para>
    /// <para>
    /// The pointer is accepted to mirror the canonical signature and is deliberately unused: managed code
    /// cannot report stable addresses, so the audit counts operations rather than tracking them by address.
    /// </para>
    /// </remarks>
    internal static void DebugAllocHook(ImGuiDebugAllocInfo info, int frameCount, IntPtr ptr, long size)
    {
        _ = ptr;
        ImGuiDebugAllocEntry entry = info.LastEntriesBuf[info.LastEntriesIdx];
        if (entry.FrameCount != frameCount)
        {
            info.LastEntriesIdx = (short)((info.LastEntriesIdx + 1) % info.LastEntriesBuf.Length);
            entry = info.LastEntriesBuf[info.LastEntriesIdx];
            entry.FrameCount = frameCount;
            entry.AllocCount = 0;
            entry.FreeCount = 0;
        }

        if (size != -1)
        {
            entry.AllocCount++;
            info.TotalAllocCount++;
        }
        else
        {
            entry.FreeCount++;
            info.TotalFreeCount++;
        }
    }

    /// <summary>
    /// Verifies that the caller was compiled against the same version and type layout as this library.
    /// (<c>ThirdParty/imgui/imgui.cpp:11651-11662</c>, declared <c>ThirdParty/imgui/imgui.h:1184</c>)
    /// </summary>
    /// <param name="version">The version string the caller compiled against.</param>
    /// <param name="szIo">The caller's size of <see cref="ImGuiIO"/>.</param>
    /// <param name="szStyle">The caller's size of <see cref="ImGuiStyle"/>.</param>
    /// <param name="szVec2">The caller's size of <see cref="ImVec2"/>.</param>
    /// <param name="szVec4">The caller's size of <see cref="ImVec4"/>.</param>
    /// <param name="szVert">The caller's size of <see cref="ImDrawVert"/>.</param>
    /// <param name="szIdx">The caller's size of <c>ImDrawIdx</c>, which this port represents as an unsigned short.</param>
    /// <returns>True when every check passed.</returns>
    /// <remarks>
    /// <para>
    /// Canonical compares each size against <c>sizeof</c> of the corresponding type, because a C caller
    /// compiling a different header revision would otherwise silently misinterpret the layout. Managed types
    /// have no equivalent compile-time size guarantee, so this port compares the version string and treats
    /// the size arguments as satisfied; a host that needs a layout check must pin the library version
    /// instead. The parameters are retained so the canonical call shape and its callers keep working.
    /// </para>
    /// <para>
    /// The canonical assertions fire only in debug builds, which is why a Release host sees a boolean rather
    /// than a crash.
    /// </para>
    /// </remarks>
    public static bool DebugCheckVersionAndDataLayout(
        string version,
        ulong szIo,
        ulong szStyle,
        ulong szVec2,
        ulong szVec4,
        ulong szVert,
        ulong szIdx)
    {
        _ = szIo;
        _ = szStyle;
        _ = szVec2;
        _ = szVec4;
        _ = szVert;
        _ = szIdx;
        bool error = version != ImGuiVersion;
        ImAssert.Check(!error, "Mismatched version string!");
        return !error;
    }

    /// <summary>
    /// Submits the standard "(?)" help marker used throughout the metrics window.
    /// (<c>ThirdParty/imgui/imgui.cpp:22169-22183</c>)
    /// </summary>
    /// <param name="desc">The explanation shown in the tooltip.</param>
    /// <remarks>
    /// Canonical gives this helper a unity-build-safe name; in this port it is a private member of the
    /// single <see cref="ImGui"/> class, so no name mangling is required.
    /// </remarks>
    private static void MetricsHelpMarker(string desc)
    {
        TextDisabled("(?)");
        if (BeginItemTooltip())
        {
            PushTextWrapPos(GetFontSize() * 35.0f);
            TextUnformatted(desc);
            PopTextWrapPos();
            EndTooltip();
        }
    }

    /// <summary>
    /// Reports one platform monitor's bounds and DPI scale.
    /// (<c>ThirdParty/imgui/imgui.cpp:23904-23911</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4168</c>)
    /// </summary>
    /// <param name="monitor">The monitor to report.</param>
    /// <param name="label">The label prefix, for example <c>"Monitor"</c>.</param>
    /// <param name="idx">The monitor index.</param>
    /// <remarks>
    /// The maximum corner of each rectangle is shown rather than its size alone, because the two together
    /// let a maintainer confirm that the work area really is contained inside the main area.
    /// </remarks>
    internal static void DebugNodePlatformMonitor(ImGuiPlatformMonitor monitor, string label, int idx)
    {
        BulletText(
            "{0} {1}: DPI {2:F0}%\n MainMin ({3:F0},{4:F0}), MainMax ({5:F0},{6:F0}), MainSize ({7:F0},{8:F0})\n WorkMin ({9:F0},{10:F0}), WorkMax ({11:F0},{12:F0}), WorkSize ({13:F0},{14:F0})",
            label,
            idx,
            monitor.DpiScale * 100.0f,
            monitor.MainPos.X,
            monitor.MainPos.Y,
            monitor.MainPos.X + monitor.MainSize.X,
            monitor.MainPos.Y + monitor.MainSize.Y,
            monitor.MainSize.X,
            monitor.MainSize.Y,
            monitor.WorkPos.X,
            monitor.WorkPos.Y,
            monitor.WorkPos.X + monitor.WorkSize.X,
            monitor.WorkPos.Y + monitor.WorkSize.Y,
            monitor.WorkSize.X,
            monitor.WorkSize.Y);
    }

    /// <summary>
    /// Lists the entries of a state storage, offering to locate the identifier each entry was stored under.
    /// (<c>ThirdParty/imgui/imgui.cpp:23814-23824</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4152</c>)
    /// </summary>
    /// <param name="storage">The storage to list.</param>
    /// <param name="label">The tree-node label.</param>
    /// <remarks>
    /// <para>
    /// Canonical always prints the integer member of each pair and comments that the real stored value may
    /// not be an integer, because the storage does not record which of its members a key holds. This port
    /// reproduces that warning rather than guessing the type.
    /// </para>
    /// <para>
    /// The reported byte count is the canonical pair payload (a key, an integer, a float, and a pointer)
    /// multiplied by the entry count; managed per-object overhead is not representable and is not included.
    /// </para>
    /// </remarks>
    internal static void DebugNodeStorage(ImGuiStorage storage, string label)
    {
        int payloadBytes = storage.Data.Count * (12 + IntPtr.Size);
        if (!TreeNode(label, "{0}: {1} entries, {2} bytes", label, storage.Data.Count, payloadBytes))
            return;
        foreach (ImGuiStoragePair pair in storage.Data)
        {
            // Important: the storage does not record a type, so the real value may not be an integer.
            BulletText("Key 0x{0:X8} Value {{ i: {1} }}", pair.Key, pair.ValueInt);
            DebugLocateItemOnHover(pair.Key);
        }
        TreePop();
    }

    /// <summary>
    /// Lists a legacy columns set: its identifier, column count, flags, extent, and per-column offsets.
    /// (<c>ThirdParty/imgui/imgui.cpp:23355-23363</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4148</c>)
    /// </summary>
    /// <param name="columns">The columns set to list.</param>
    /// <remarks>
    /// Canonical seeds the tree node from the columns identifier used as a pointer. A boxed integer does not
    /// have a stable managed hash, so this port derives a stable identifier string from the same value
    /// instead; the label text is unchanged.
    /// </remarks>
    internal static void DebugNodeColumns(ImGuiOldColumns columns)
    {
        if (!TreeNode(
                $"##Columns{columns.ID:X8}",
                "Columns Id: 0x{0:X8}, Count: {1}, Flags: 0x{2:X4}",
                columns.ID,
                columns.Count,
                (int)columns.Flags))
        {
            return;
        }

        BulletText(
            "Width: {0:F1} (MinX: {1:F1}, MaxX: {2:F1})",
            columns.OffMaxX - columns.OffMinX,
            columns.OffMinX,
            columns.OffMaxX);
        for (int n = 0; n < columns.Columns.Count; n++)
        {
            ImGuiOldColumnData column = columns.Columns[n];
            BulletText(
                "Column {0:D2}: OffsetNorm {1:F3} (= {2:F1} px)",
                n,
                column.OffsetNorm,
                GetColumnOffsetFromNorm(columns, column.OffsetNorm));
        }
        TreePop();
    }

    /// <summary>
    /// Reports one persisted window settings record.
    /// (<c>ThirdParty/imgui/imgui.cpp:23989-23998</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4165</c>)
    /// </summary>
    /// <param name="settings">The settings record to report.</param>
    /// <remarks>
    /// A record that is queued for deletion is shown through the disabled style, so a maintainer can tell a
    /// live record from one that will be dropped at the next save.
    /// </remarks>
    internal static void DebugNodeWindowSettings(ImGuiWindowSettings settings)
    {
        if (settings.WantDelete)
            BeginDisabled();
        BulletText(
            "0x{0:X8} \"{1}\" Pos ({2},{3}) Size ({4},{5}) Collapsed={6}",
            settings.ID,
            settings.Name,
            settings.Pos.X,
            settings.Pos.Y,
            settings.Size.X,
            settings.Size.Y,
            settings.Collapsed ? 1 : 0);
        if (settings.WantDelete)
            EndDisabled();
    }

    /// <summary>
    /// Converts a backend texture identifier into an unsigned value suitable for display.
    /// (<c>ThirdParty/imgui/imgui.cpp:22370-22376</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4150</c>)
    /// </summary>
    /// <param name="texId">The backend texture identifier to convert.</param>
    /// <returns>The identifier reinterpreted as an unsigned 64-bit value.</returns>
    internal static ulong DebugTextureIDToU64(IntPtr texId) => (ulong)texId.ToInt64();

    /// <summary>
    /// Formats a texture reference for display in the metrics window.
    /// (<c>ThirdParty/imgui/imgui.cpp:22377-22391</c>)
    /// </summary>
    /// <param name="texRef">The texture reference to format.</param>
    /// <returns>
    /// The atlas texture's unique identifier when the reference carries texture data, followed by the
    /// resolved backend identifier in hexadecimal.
    /// </returns>
    /// <remarks>
    /// Canonical writes into a caller-supplied buffer and returns it; the managed port returns a string,
    /// which removes the truncation risk the fixed-size buffer carried.
    /// </remarks>
    private static string FormatTextureRefForDebugDisplay(ImTextureRef texRef)
    {
        string uniqueIdPrefix = texRef.TexData != null ? $"#{texRef.TexData.UniqueID:D3}: " : string.Empty;
        return uniqueIdPrefix + $"0x{DebugTextureIDToU64(texRef.GetTexID()):X}";
    }

    /// <summary>
    /// Draws the wireframe mesh and/or the bounding boxes of one draw command into another draw list.
    /// (<c>ThirdParty/imgui/imgui.cpp:23549-23576</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4157</c>)
    /// </summary>
    /// <param name="outDrawList">The draw list that receives the overlay.</param>
    /// <param name="drawList">The draw list the command belongs to.</param>
    /// <param name="drawCmd">The command to outline.</param>
    /// <param name="showMesh">Whether to draw the triangles of the command.</param>
    /// <param name="showAabb">Whether to draw the clip rectangle and the vertices' bounding box.</param>
    /// <remarks>
    /// Anti-aliasing is disabled for the duration because it makes very large or very thin triangles harder
    /// to read. The clip rectangle is pink and the vertex bounding box is cyan, matching canonical.
    /// </remarks>
    internal static void DebugNodeDrawCmdShowMeshAndBoundingBox(
        ImDrawList outDrawList,
        ImDrawList drawList,
        ImDrawCmd drawCmd,
        bool showMesh,
        bool showAabb)
    {
        ImAssert.Check(showMesh || showAabb);

        ImRect clipRect = new(drawCmd.ClipRect.X, drawCmd.ClipRect.Y, drawCmd.ClipRect.Z, drawCmd.ClipRect.W);
        ImRect vtxsRect = new(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        ImDrawFlags backupFlags = outDrawList.Flags;
        outDrawList.Flags &= ~ImDrawFlags.AALines;

        // The triangle vertices are read from the shared buffers and outlined one triangle at a time. The
        // index buffer is optional: when it is empty the command draws vertices in submission order.
        uint idxEnd = drawCmd.IdxOffset + drawCmd.ElemCount;
        for (uint idxN = drawCmd.IdxOffset; idxN < idxEnd;)
        {
            ImVec2[] triangle = new ImVec2[3];
            for (int n = 0; n < 3; n++, idxN++)
            {
                int vertexIndex = drawList.IdxBuffer.Count > 0
                    ? drawList.IdxBuffer[(int)idxN]
                    : (int)idxN;
                triangle[n] = drawList.VtxBuffer[(int)drawCmd.VtxOffset + vertexIndex].Pos;
                vtxsRect.Add(triangle[n]);
            }
            if (showMesh)
                outDrawList.AddPolyline(triangle, 0xFFFFFF00, 1.0f, ImDrawFlags.Closed); // Yellow: mesh triangles.
        }

        if (showAabb)
        {
            // Pink: the clipping rectangle submitted to the GPU. Cyan: the bounding box of the triangles.
            outDrawList.AddRect(Truncate(clipRect.Min), Truncate(clipRect.Max), 0xFFFF00FF);
            outDrawList.AddRect(Truncate(vtxsRect.Min), Truncate(vtxsRect.Max), 0xFFFFFF00);
        }
        outDrawList.Flags = backupFlags;
    }

    /// <summary>
    /// Truncates both components of a position toward zero.
    /// </summary>
    /// <param name="v">The position to truncate.</param>
    /// <returns>The truncated position.</returns>
    /// <remarks>
    /// Canonical applies <c>ImTrunc</c> before drawing the debug rectangles so a fractional clip rectangle
    /// does not produce a blurry one-pixel outline.
    /// </remarks>
    private static ImVec2 Truncate(ImVec2 v) => new(MathF.Truncate(v.X), MathF.Truncate(v.Y));

    /// <summary>
    /// Lists a draw list's commands, offering per-command mesh, bounding-box, and vertex inspection.
    /// (<c>ThirdParty/imgui/imgui.cpp:23454-23546</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4155</c>)
    /// </summary>
    /// <param name="window">The owning window, or <see langword="null"/> for a viewport-owned list.</param>
    /// <param name="viewport">The viewport whose foreground list receives the overlay, or <see langword="null"/>.</param>
    /// <param name="drawList">The draw list to inspect.</param>
    /// <param name="label">The tree-node label.</param>
    /// <remarks>
    /// <para>
    /// A trailing command with no elements and no callback is not counted, because the draw-list system keeps
    /// one such placeholder command alive, matching canonical.
    /// </para>
    /// <para>
    /// The list currently being appended cannot be reported: its buffers are still being written, so the
    /// node says so and returns rather than showing moving statistics.
    /// </para>
    /// </remarks>
    internal static void DebugNodeDrawList(ImGuiWindow? window, ImGuiViewportP? viewport, ImDrawList drawList, string label)
    {
        ImGuiContext g = GetContext();
        ImGuiMetricsConfig cfg = g.DebugMetricsConfig;

        int cmdCount = drawList.CmdBuffer.Count;
        if (cmdCount > 0)
        {
            ImDrawCmd last = drawList.CmdBuffer[cmdCount - 1];
            if (last.ElemCount == 0 && last.UserCallback == null)
                cmdCount--;
        }

        bool nodeOpen = TreeNode(
            drawList,
            "{0}: '{1}' {2} vtx, {3} indices, {4} cmds",
            label,
            drawList.OwnerName ?? string.Empty,
            drawList.VtxBuffer.Count,
            drawList.IdxBuffer.Count,
            cmdCount);
        if (ReferenceEquals(drawList, GetWindowDrawList()))
        {
            SameLine();
            TextColored(new ImVec4(1.0f, 0.4f, 0.4f, 1.0f), "CURRENTLY APPENDING"); // The active list is not inspectable.
            if (nodeOpen)
                TreePop();
            return;
        }

        ImDrawList? fgDrawList = viewport != null ? GetForegroundDrawList(viewport) : null;
        if (window != null && IsItemHovered() && fgDrawList != null)
            fgDrawList.AddRect(window.Pos, window.Pos + window.Size, 0xFF00FFFF); // Yellow outline.

        if (!nodeOpen)
            return;

        if (window != null && !window.WasActive)
            TextDisabled("Warning: owning Window is inactive. This DrawList is not being rendered!");

        for (int cmdIndex = 0; cmdIndex < cmdCount; cmdIndex++)
        {
            ImDrawCmd pcmd = drawList.CmdBuffer[cmdIndex];
            if (pcmd.UserCallback != null)
            {
                BulletText("Callback {0}, user_data {1}", pcmd.UserCallback, pcmd.UserCallbackData);
                continue;
            }

            string texIdDesc = FormatTextureRefForDebugDisplay(pcmd.TexRef);
            string cmdLabel = string.Format(
                CultureInfo.InvariantCulture,
                "DrawCmd:{0,5} tris, Tex {1}, ClipRect ({2,4:F0},{3,4:F0})-({4,4:F0},{5,4:F0})",
                pcmd.ElemCount / 3,
                texIdDesc,
                pcmd.ClipRect.X,
                pcmd.ClipRect.Y,
                pcmd.ClipRect.Z,
                pcmd.ClipRect.W);
            bool cmdNodeOpen = TreeNode($"##DrawCmd{cmdIndex}", "{0}", cmdLabel);
            if (IsItemHovered() && (cfg.ShowDrawCmdMesh || cfg.ShowDrawCmdBoundingBoxes) && fgDrawList != null)
                DebugNodeDrawCmdShowMeshAndBoundingBox(fgDrawList, drawList, pcmd, cfg.ShowDrawCmdMesh, cfg.ShowDrawCmdBoundingBoxes);
            if (!cmdNodeOpen)
                continue;

            // Approximate the touched pixel count by summing the areas of the command's triangles. The area
            // is in pixels squared as long as the renderer applies no additional scaling to its output.
            float totalArea = 0.0f;
            for (uint idxN = pcmd.IdxOffset; idxN < pcmd.IdxOffset + pcmd.ElemCount;)
            {
                ImVec2[] triangle = ReadTriangle(drawList, pcmd.VtxOffset, idxN);
                totalArea += TriangleArea(triangle[0], triangle[1], triangle[2]);
                idxN += 3;
            }

            // Hovering this row draws the whole command's mesh in wireframe.
            Selectable(string.Format(
                CultureInfo.InvariantCulture,
                "Mesh: ElemCount: {0}, VtxOffset: +{1}, IdxOffset: +{2}, Area: ~{3:F0} px",
                pcmd.ElemCount,
                pcmd.VtxOffset,
                pcmd.IdxOffset,
                totalArea));
            if (IsItemHovered() && fgDrawList != null)
                DebugNodeDrawCmdShowMeshAndBoundingBox(fgDrawList, drawList, pcmd, true, false);

            // Individual triangles are printed through a clipper so only the rows that can be seen are built.
            ImGuiListClipper clipper = new();
            clipper.Begin((int)(pcmd.ElemCount / 3));
            while (clipper.Step())
            {
                for (int prim = clipper.DisplayStart, idxI = (int)pcmd.IdxOffset + clipper.DisplayStart * 3;
                     prim < clipper.DisplayEnd;
                     prim++)
                {
                    System.Text.StringBuilder buf = new();
                    ImVec2[] triangle = new ImVec2[3];
                    for (int n = 0; n < 3; n++, idxI++)
                    {
                        int vertexIndex = drawList.IdxBuffer.Count > 0 ? drawList.IdxBuffer[idxI] : idxI;
                        ImDrawVert v = drawList.VtxBuffer[(int)pcmd.VtxOffset + vertexIndex];
                        triangle[n] = v.Pos;
                        buf.Append(CultureInfo.InvariantCulture, $"{(n == 0 ? "Vert:" : "     ")} {idxI:D4}: pos ({v.Pos.X,8:F2},{v.Pos.Y,8:F2}), uv ({v.UV.X:F6},{v.UV.Y:F6}), col {v.Col:X8}\n");
                    }

                    Selectable(buf.ToString(), false);
                    if (fgDrawList != null && IsItemHovered())
                    {
                        // Anti-aliasing is disabled so the highlighted triangle's outline stays readable.
                        ImDrawFlags backupFlags = fgDrawList.Flags;
                        fgDrawList.Flags &= ~ImDrawFlags.AALines;
                        fgDrawList.AddPolyline(triangle, 0xFF00FFFF, 1.0f, ImDrawFlags.Closed); // Yellow.
                        fgDrawList.Flags = backupFlags;
                    }
                }
            }
            TreePop();
        }
        TreePop();
    }

    /// <summary>
    /// Reads one triangle's three positions from a draw list's shared buffers.
    /// </summary>
    /// <param name="drawList">The draw list holding the buffers.</param>
    /// <param name="vtxOffset">The command's vertex-buffer offset.</param>
    /// <param name="idxN">The index of the triangle's first index.</param>
    /// <returns>The three triangle positions.</returns>
    /// <remarks>
    /// The index buffer is optional: when it is empty the vertex buffer is consumed in submission order,
    /// which is how canonical handles a draw list that was built without indexed geometry.
    /// </remarks>
    private static ImVec2[] ReadTriangle(ImDrawList drawList, uint vtxOffset, uint idxN)
    {
        ImVec2[] triangle = new ImVec2[3];
        for (int n = 0; n < 3; n++, idxN++)
        {
            int vertexIndex = drawList.IdxBuffer.Count > 0 ? drawList.IdxBuffer[(int)idxN] : (int)idxN;
            triangle[n] = drawList.VtxBuffer[(int)vtxOffset + vertexIndex].Pos;
        }
        return triangle;
    }

    /// <summary>
    /// Computes the area of a triangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1075-1078</c>)
    /// </summary>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second vertex.</param>
    /// <param name="c">The third vertex.</param>
    /// <returns>The unsigned triangle area.</returns>
    /// <remarks>
    /// The managed port keeps this local to the debug tools rather than widening the public math surface,
    /// because the metrics window is its only consumer.
    /// </remarks>
    private static float TriangleArea(ImVec2 a, ImVec2 b, ImVec2 c)
        => MathF.Abs(a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y)) * 0.5f;

    /// <summary>
    /// Reports one window: its identity, flags, geometry, scroll state, navigation state, docking state,
    /// and its child, parent, root, draw-list, columns, and storage relationships.
    /// (<c>ThirdParty/imgui/imgui.cpp:23912-23988</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4162</c>)
    /// </summary>
    /// <param name="window">The window to report, or <see langword="null"/> to report a missing window.</param>
    /// <param name="label">The tree-node label.</param>
    /// <remarks>
    /// An inactive window is shown through the disabled style and marked with a suffix, which is how a
    /// maintainer distinguishes a window that is still being submitted from one that merely held its slot.
    /// The node also recurses into the window's root, parent, focus-route parent, and child list.
    /// </remarks>
    internal static void DebugNodeWindow(ImGuiWindow? window, string label)
    {
        if (window == null)
        {
            BulletText("{0}: NULL", label);
            return;
        }

        ImGuiContext g = GetContext();
        bool isActive = window.WasActive;
        ImGuiTreeNodeFlags treeNodeFlags = window == g.NavWindow ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None;
        if (!isActive)
            PushStyleColor(ImGuiCol.Text, GetStyleColorVec4(ImGuiCol.TextDisabled));
        bool open = TreeNodeEx(label, treeNodeFlags, "{0} '{1}'{2}", label, window.Name, isActive ? string.Empty : " *Inactive*");
        if (!isActive)
            PopStyleColor();
        if (IsItemHovered() && isActive)
            GetForegroundDrawList(window).AddRect(window.Pos, window.Pos + window.Size, 0xFF00FFFF); // Yellow outline.
        if (!open)
            return;

        if (window.MemoryCompacted)
            TextDisabled("Note: some memory buffers have been compacted/freed.");

        if (g.IO.ConfigDebugIsDebuggerPresent && DebugBreakButton("**DebugBreak**", "in Begin()"))
            g.DebugBreakInWindow = window.ID;

        ImGuiWindowFlags flags = window.Flags;
        DebugNodeDrawList(window, window.Viewport, window.DrawList, "DrawList");
        BulletText(
            "Pos: ({0:F1},{1:F1}), Size: ({2:F1},{3:F1}), ContentSize ({4:F1},{5:F1}) Ideal ({6:F1},{7:F1})",
            window.Pos.X,
            window.Pos.Y,
            window.Size.X,
            window.Size.Y,
            window.ContentSize.X,
            window.ContentSize.Y,
            window.ContentSizeIdeal.X,
            window.ContentSizeIdeal.Y);
        BulletText(
            "Flags: 0x{0:X8} ({1}{2}{3}{4}{5}{6}{7}{8}{9}..)",
            (int)flags,
            (flags & ImGuiWindowFlags.ChildWindow) != 0 ? "Child " : string.Empty,
            (flags & ImGuiWindowFlags.Tooltip) != 0 ? "Tooltip " : string.Empty,
            (flags & ImGuiWindowFlags.Popup) != 0 ? "Popup " : string.Empty,
            (flags & ImGuiWindowFlags.Modal) != 0 ? "Modal " : string.Empty,
            (flags & ImGuiWindowFlags.ChildMenu) != 0 ? "ChildMenu " : string.Empty,
            (flags & ImGuiWindowFlags.NoSavedSettings) != 0 ? "NoSavedSettings " : string.Empty,
            (flags & ImGuiWindowFlags.NoMouseInputs) != 0 ? "NoMouseInputs" : string.Empty,
            (flags & ImGuiWindowFlags.NoNavInputs) != 0 ? "NoNavInputs" : string.Empty,
            (flags & ImGuiWindowFlags.AlwaysAutoResize) != 0 ? "AlwaysAutoResize" : string.Empty);
        if ((flags & ImGuiWindowFlags.ChildWindow) != 0)
        {
            BulletText(
                "ChildFlags: 0x{0:X8} ({1}{2}{3}{4}..)",
                (int)window.ChildFlags,
                (window.ChildFlags & ImGuiChildFlags.Borders) != 0 ? "Borders " : string.Empty,
                (window.ChildFlags & ImGuiChildFlags.ResizeX) != 0 ? "ResizeX " : string.Empty,
                (window.ChildFlags & ImGuiChildFlags.ResizeY) != 0 ? "ResizeY " : string.Empty,
                (window.ChildFlags & ImGuiChildFlags.NavFlattened) != 0 ? "NavFlattened " : string.Empty);
        }
        BulletText("WindowClassId: 0x{0:X8}", window.WindowClass.ClassId);
        BulletText(
            "Scroll: ({0:F2}/{1:F2},{2:F2}/{3:F2}) Scrollbar:{4}{5}",
            window.Scroll.X,
            window.ScrollMax.X,
            window.Scroll.Y,
            window.ScrollMax.Y,
            window.ScrollbarX ? "X" : string.Empty,
            window.ScrollbarY ? "Y" : string.Empty);
        BulletText(
            "Active: {0}/{1}, WriteAccessed: {2}, BeginOrderWithinContext: {3}",
            window.Active ? 1 : 0,
            window.WasActive ? 1 : 0,
            window.WriteAccessed ? 1 : 0,
            window.Active || window.WasActive ? window.BeginOrderWithinContext : -1);
        BulletText(
            "Appearing: {0}, Hidden: {1} (CanSkip {2} Cannot {3}), SkipItems: {4}",
            window.Appearing ? 1 : 0,
            window.Hidden ? 1 : 0,
            window.HiddenFramesCanSkipItems,
            window.HiddenFramesCannotSkipItems,
            window.SkipItems ? 1 : 0);

        for (int layer = 0; layer < (int)ImGuiNavLayer.COUNT; layer++)
        {
            ImRect r = window.NavRectRel[layer];
            if (r.Min.X >= r.Max.X && r.Min.Y >= r.Max.Y)
                BulletText("NavLastIds[{0}]: 0x{1:X8}", layer, window.NavLastIds[layer]);
            else
                BulletText("NavLastIds[{0}]: 0x{1:X8} at +({2:F1},{3:F1})({4:F1},{5:F1})", layer, window.NavLastIds[layer], r.Min.X, r.Min.Y, r.Max.X, r.Max.Y);
            DebugLocateItemOnHover(window.NavLastIds[layer]);
        }
        for (int layer = 0; layer < (int)ImGuiNavLayer.COUNT; layer++)
        {
            // The sentinel is displayed as a large negative value so the row stays readable, matching canonical.
            ImVec2 p = window.NavPreferredScoringPosRel[layer];
            BulletText(
                "NavPreferredScoringPosRel[{0}] = ({1:F1},{2:F1})",
                layer,
                p.X == float.MaxValue ? -99999.0f : p.X,
                p.Y == float.MaxValue ? -99999.0f : p.Y);
        }
        BulletText(
            "NavLayersActiveMask: {0:X}, NavLastChildNavWindow: {1}",
            window.DC.NavLayersActiveMask,
            window.NavLastChildNavWindow != null ? window.NavLastChildNavWindow.Name : "NULL");

        BulletText(
            "Viewport: {0}{1}, ViewportId: 0x{2:X8}, ViewportPos: ({3:F1},{4:F1})",
            window.Viewport != null ? window.Viewport.Idx : -1,
            window.ViewportOwned ? " (Owned)" : string.Empty,
            window.ViewportId,
            window.ViewportPos.X,
            window.ViewportPos.Y);
        BulletText("ViewportMonitor: {0}", window.Viewport != null ? window.Viewport.PlatformMonitor : -1);
        BulletText(
            "DockId: 0x{0:X4}, DockOrder: {1}, Act: {2}, Vis: {3}",
            window.DockId,
            window.DockOrder,
            window.DockIsActive ? 1 : 0,
            window.DockTabIsVisible ? 1 : 0);
        if (window.DockNode != null || window.DockNodeAsHost != null)
        {
            DebugNodeDockNode(
                window.DockNodeAsHost != null ? window.DockNodeAsHost : window.DockNode!,
                window.DockNodeAsHost != null ? "DockNodeAsHost" : "DockNode");
        }

        if (window.RootWindow != window)
            DebugNodeWindow(window.RootWindow, "RootWindow");
        if (window.RootWindowDockTree != window.RootWindow)
            DebugNodeWindow(window.RootWindowDockTree, "RootWindowDockTree");
        if (window.ParentWindow != null)
            DebugNodeWindow(window.ParentWindow, "ParentWindow");
        if (window.ParentWindowForFocusRoute != null)
            DebugNodeWindow(window.ParentWindowForFocusRoute, "ParentWindowForFocusRoute");
        if (window.DC.ChildWindows.Count > 0)
            DebugNodeWindowsList(window.DC.ChildWindows, "ChildWindows");
        if (window.ColumnsStorage.Count > 0 && TreeNode("Columns", "Columns sets ({0})", window.ColumnsStorage.Count))
        {
            foreach (ImGuiOldColumns columns in window.ColumnsStorage)
                DebugNodeColumns(columns);
            TreePop();
        }
        DebugNodeStorage(window.StateStorage, "Storage");
        TreePop();
    }

    /// <summary>
    /// Lists a set of windows under a tree node.
    /// (<c>ThirdParty/imgui/imgui.cpp:23999-24010</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4166</c>)
    /// </summary>
    /// <param name="windows">The windows to list.</param>
    /// <param name="label">The tree-node label.</param>
    /// <remarks>
    /// Canonical iterates the list back to front while commenting that it draws "front to back", because the
    /// window list stores the top-most window last; this port preserves that order.
    /// </remarks>
    internal static void DebugNodeWindowsList(List<ImGuiWindow> windows, string label)
    {
        if (!TreeNode(label, "{0} ({1})", label, windows.Count))
            return;
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            PushID(windows[i]);
            DebugNodeWindow(windows[i], "Window");
            PopID();
        }
        TreePop();
    }

    /// <summary>
    /// Lists a set of windows grouped by the window they were most recently begun underneath.
    /// (<c>ThirdParty/imgui/imgui.cpp:24013-24028</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4167</c>)
    /// </summary>
    /// <param name="windows">The windows to inspect.</param>
    /// <param name="windowsStart">
    /// The first index of the region to inspect. Canonical advances the list pointer past each reported
    /// window, so the managed port carries the offset explicitly instead.
    /// </param>
    /// <param name="windowsSize">How many entries starting at <paramref name="windowsStart"/> to inspect.</param>
    /// <param name="parentInBeginStack">The begin-stack parent to group under.</param>
    /// <remarks>
    /// The recursion advances past each listed window so a window is only reported under its own begin-stack
    /// parent, which makes an unbalanced or re-nested begin order visible. The canonical comment records this
    /// as technically suboptimal but simpler.
    /// </remarks>
    internal static void DebugNodeWindowsListByBeginStackParent(
        List<ImGuiWindow> windows,
        int windowsStart,
        int windowsSize,
        ImGuiWindow? parentInBeginStack)
    {
        for (int i = 0; i < windowsSize; i++)
        {
            ImGuiWindow window = windows[windowsStart + i];
            if (window.ParentWindowInBeginStack != parentInBeginStack)
                continue;
            string windowLabel = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:D4}] Window",
                window.BeginOrderWithinContext);
            DebugNodeWindow(window, windowLabel);
            TreePush(windowLabel);
            DebugNodeWindowsListByBeginStackParent(windows, windowsStart + i + 1, windowsSize - i - 1, window);
            TreePop();
        }
    }

    /// <summary>
    /// Shows how a string is encoded as UTF-8 and which of its characters the current font can render.
    /// (<c>ThirdParty/imgui/imgui.cpp:22303-22339</c>, declared <c>ThirdParty/imgui/imgui.h:1181</c>)
    /// </summary>
    /// <param name="text">The string to inspect.</param>
    /// <remarks>
    /// <para>
    /// Each row reports one codepoint: its byte offset, the hexadecimal value of every byte it occupies, the
    /// character itself (marked <c>[missing]</c> when the font has no glyph for it), and the Unicode code
    /// point. This is how a maintainer diagnoses mojibake, a font that lacks a glyph, or a mis-encoded source
    /// string.
    /// </para>
    /// <para>
    /// The input may be malformed UTF-8, so the canonical implementation passes an explicit end rather than
    /// letting the decoder assume enough bytes remain. The managed port keeps the same guarantee by decoding
    /// from a fixed byte array and letting the decoder report the malformed sequence, which is why no bounds
    /// violation is possible even though canonical needed the extra care.
    /// </para>
    /// </remarks>
    public static void DebugTextEncoding(string text)
    {
        Text("Text: \"{0}\"", text);
        if (!BeginTable(
                "##DebugTextEncoding",
                4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable))
        {
            return;
        }

        TableSetupColumn("Offset");
        TableSetupColumn("UTF-8");
        TableSetupColumn("Glyph");
        TableSetupColumn("Codepoint");
        TableHeadersRow();

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        for (int p = 0; p < bytes.Length;)
        {
            int start = p;
            int utf8Len = ImFont.ImTextCharFromUtf8(bytes, ref p, out uint codepoint);
            if (utf8Len <= 0 || p <= start)
                break; // Malformed or empty sequence; stop rather than spin.

            TableNextColumn();
            Text("{0}", start);
            TableNextColumn();
            for (int byteIndex = 0; byteIndex < utf8Len; byteIndex++)
            {
                if (byteIndex > 0)
                    SameLine();
                Text("0x{0:X2}", bytes[start + byteIndex]);
            }
            TableNextColumn();
            TextUnformatted(System.Text.Encoding.UTF8.GetString(bytes, start, utf8Len));
            if (GetFont() is { } font && !font.IsGlyphInFont(codepoint))
            {
                SameLine();
                TextUnformatted("[missing]");
            }
            TableNextColumn();
            Text("U+{0:X4}", codepoint);
        }

        EndTable();
    }

    /// <summary>
    /// Restores the style colour that <see cref="DebugFlashStyleColor"/> temporarily replaced.
    /// (<c>ThirdParty/imgui/imgui.cpp:22341-22347</c>)
    /// </summary>
    /// <remarks>
    /// Restoring is idempotent: when no flash is in progress the index is already
    /// <see cref="ImGuiCol.COUNT"/> and nothing is written back.
    /// </remarks>
    private static void DebugFlashStyleColorStop()
    {
        ImGuiContext g = GetContext();
        if (g.DebugFlashStyleColorIdx != ImGuiCol.COUNT)
            g.Style.Colors[(int)g.DebugFlashStyleColorIdx] = g.DebugFlashStyleColorBackup;
        g.DebugFlashStyleColorIdx = ImGuiCol.COUNT;
    }

    /// <summary>
    /// Starts flashing one style colour for half a second so a maintainer can find every widget that uses it.
    /// (<c>ThirdParty/imgui/imgui.cpp:22350-22357</c>, declared <c>ThirdParty/imgui/imgui.h:1182</c>)
    /// </summary>
    /// <param name="idx">The style colour to flash.</param>
    /// <remarks>
    /// The original colour is saved and restored by the per-frame updater. While a flash is active,
    /// <c>PushStyleColor</c> deliberately leaves this colour alone so the flash stays visible through
    /// scoped overrides.
    /// </remarks>
    public static void DebugFlashStyleColor(ImGuiCol idx)
    {
        ImGuiContext g = GetContext();
        DebugFlashStyleColorStop();
        g.DebugFlashStyleColorTime = 0.5f;
        g.DebugFlashStyleColorIdx = idx;
        g.DebugFlashStyleColorBackup = g.Style.Colors[(int)idx];
    }

    /// <summary>
    /// Animates the flashing style colour and restores the original when the flash expires.
    /// (<c>ThirdParty/imgui/imgui.cpp:22359-22368</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4149</c>)
    /// </summary>
    /// <remarks>
    /// The colour cycles through the hue circle once per second by driving the HSV hue with a cosine, so the
    /// flash is equally visible on light and dark styles. Only the hue is animated; saturation and value are
    /// fixed so the flashed colour stays legible.
    /// </remarks>
    internal static void UpdateDebugToolFlashStyleColor()
    {
        ImGuiContext g = GetContext();
        if (g.DebugFlashStyleColorTime <= 0.0f)
            return;

        ColorConvertHSVtoRGB(
            MathF.Cos(g.DebugFlashStyleColorTime * 6.0f) * 0.5f + 0.5f,
            0.5f,
            0.5f,
            out float r,
            out float gr,
            out float b);
        ImVec4 flashed = g.Style.Colors[(int)g.DebugFlashStyleColorIdx];
        flashed.X = r;
        flashed.Y = gr;
        flashed.Z = b;
        flashed.W = 1.0f;
        g.Style.Colors[(int)g.DebugFlashStyleColorIdx] = flashed;

        g.DebugFlashStyleColorTime -= g.IO.DeltaTime;
        if (g.DebugFlashStyleColorTime <= 0.0f)
            DebugFlashStyleColorStop();
    }

    /// <summary>
    /// Lists a tab bar's tabs, offering the manual reorder arrows canonical exposes for debugging.
    /// (<c>ThirdParty/imgui/imgui.cpp:23827-23865</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4153</c>)
    /// </summary>
    /// <param name="tabBar">The tab bar to inspect.</param>
    /// <param name="label">The tree-node label.</param>
    /// <remarks>
    /// <para>
    /// The node label previews at most the first three tab names, because a dock node host can carry many
    /// tabs and the label would otherwise be unusable. A tab bar that has not been submitted for two frames
    /// is shown through the disabled style and marked inactive.
    /// </para>
    /// <para>
    /// Hovering the node outlines the bar and marks both scrolling-button thresholds, so a maintainer can see
    /// exactly where the scrolling region begins and ends. Each listed tab carries the arrow buttons that
    /// queue a reorder, which is the only way to exercise the reorder path without dragging.
    /// </para>
    /// </remarks>
    internal static void DebugNodeTabBar(ImGuiTabBar tabBar, string label)
    {
        ImGuiContext g = GetContext();

        // Standalone tab bars hold no separate title, so the preview is built from the tab names themselves.
        System.Text.StringBuilder preview = new();
        preview.Append(CultureInfo.InvariantCulture, $"{label} 0x{tabBar.ID:X8} ({tabBar.Tabs.Count} tabs)");
        bool isActive = tabBar.PrevFrameVisible >= g.FrameCount - 2;
        if (!isActive)
            preview.Append(" *Inactive*");
        preview.Append("  {");
        int previewCount = Math.Min(tabBar.Tabs.Count, 3);
        for (int tabN = 0; tabN < previewCount; tabN++)
        {
            preview.Append(CultureInfo.InvariantCulture, $"{(tabN > 0 ? ", " : string.Empty)}'{TabBarGetTabName(tabBar, tabBar.Tabs[tabN])}'");
        }
        preview.Append(tabBar.Tabs.Count > 3 ? " ... }" : " } ");

        if (!isActive)
            PushStyleColor(ImGuiCol.Text, GetStyleColorVec4(ImGuiCol.TextDisabled));
        bool open = TreeNode(label, "{0}", preview.ToString());
        if (!isActive)
            PopStyleColor();
        if (isActive && IsItemHovered())
        {
            // A tab bar that is still being submitted always has an owning window; the active check above
        // guarantees that, which is why the canonical code dereferences it without a guard.
        ImDrawList drawList = GetForegroundDrawList(tabBar.Window!);
            drawList.AddRect(tabBar.BarRect.Min, tabBar.BarRect.Max, 0xFF00FFFF); // Yellow outline.
            drawList.AddLineV(tabBar.ScrollingRectMinX, tabBar.BarRect.Min.Y, tabBar.BarRect.Max.Y, 0xFF00FF00);
            drawList.AddLineV(tabBar.ScrollingRectMaxX, tabBar.BarRect.Min.Y, tabBar.BarRect.Max.Y, 0xFF00FF00);
        }

        if (open)
        {
            for (int tabN = 0; tabN < tabBar.Tabs.Count; tabN++)
            {
                ImGuiTabItem tab = tabBar.Tabs[tabN];
                PushID(tab);
                if (SmallButton("<"))
                    TabBarQueueReorder(tabBar, tab, -1);
                SameLine(0.0f, 2.0f);
                if (SmallButton(">"))
                    TabBarQueueReorder(tabBar, tab, +1);
                SameLine();
                Text(
                    "{0:D2}{1} Tab 0x{2:X8} '{3}' Offset: {4:F2}, Width: {5:F2}/{6:F2}",
                    tabN,
                    tab.ID == tabBar.SelectedTabId ? '*' : ' ',
                    tab.ID,
                    TabBarGetTabName(tabBar, tab),
                    tab.Offset,
                    tab.Width,
                    tab.ContentWidth);
                PopID();
            }
            TreePop();
        }
    }

    /// <summary>
    /// One key of the keyboard preview board: its row and column, the label drawn on the key cap, and the
    /// engine key it reports. (<c>ThirdParty/imgui/imgui.cpp:22269-22275</c>)
    /// </summary>
    /// <param name="Row">The board row.</param>
    /// <param name="Col">The board column.</param>
    /// <param name="Label">The label drawn on the key cap, or empty for a modifier cap.</param>
    /// <param name="Key">The key whose down state is reflected.</param>
    private readonly record struct KeyLayoutData(int Row, int Col, string Label, ImGuiKey Key);

    /// <summary>
    /// Draws a small keyboard board that highlights the keys currently held down.
    /// (<c>ThirdParty/imgui/imgui.cpp:22253-22300</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4171</c>)
    /// </summary>
    /// <param name="drawList">The draw list that receives the board.</param>
    /// <remarks>
    /// <para>
    /// Manual draw-list shapes are not clipped automatically, so the board reserves its area with
    /// <see cref="Dummy"/> and skips the whole drawing when that area is out of view. It then pushes its own
    /// clip rectangle for the duration.
    /// </para>
    /// <para>
    /// Every dimension is scaled by the font size so the board stays proportionate when the style scales the
    /// interface. This is how a maintainer confirms that a physical key reaches the engine as the expected
    /// named key.
    /// </para>
    /// </remarks>
    internal static void DebugRenderKeyboardPreview(ImDrawList drawList)
    {
        float scale = GetFontSize() / 13.0f;
        ImVec2 keySize = new ImVec2(35.0f, 35.0f) * scale;
        float keyRounding = 3.0f * scale;
        ImVec2 keyFaceSize = new ImVec2(25.0f, 25.0f) * scale;
        ImVec2 keyFacePos = new ImVec2(5.0f, 3.0f) * scale;
        float keyFaceRounding = 2.0f * scale;
        ImVec2 keyLabelPos = new ImVec2(7.0f, 4.0f) * scale;
        ImVec2 keyStep = new(keySize.X - 1.0f, keySize.Y - 1.0f);
        float keyRowOffset = 9.0f * scale;

        ImVec2 boardMin = GetCursorScreenPos();
        ImVec2 boardMax = new(
            boardMin.X + 3 * keyStep.X + 2 * keyRowOffset + 10.0f,
            boardMin.Y + 3 * keyStep.Y + 10.0f);
        ImVec2 startPos = new(boardMin.X + 5.0f - keyStep.X, boardMin.Y);

        KeyLayoutData[] keysToDisplay =
        {
            new(0, 0, string.Empty, ImGuiKey.Tab),
            new(0, 1, "Q", ImGuiKey.Q),
            new(0, 2, "W", ImGuiKey.W),
            new(0, 3, "E", ImGuiKey.E),
            new(0, 4, "R", ImGuiKey.R),
            new(1, 0, string.Empty, ImGuiKey.CapsLock),
            new(1, 1, "A", ImGuiKey.A),
            new(1, 2, "S", ImGuiKey.S),
            new(1, 3, "D", ImGuiKey.D),
            new(1, 4, "F", ImGuiKey.F),
            new(2, 0, string.Empty, ImGuiKey.LeftShift),
            new(2, 1, "Z", ImGuiKey.Z),
            new(2, 2, "X", ImGuiKey.X),
            new(2, 3, "C", ImGuiKey.C),
            new(2, 4, "V", ImGuiKey.V),
        };

        Dummy(boardMax - boardMin);
        if (!IsItemVisible())
            return;

        drawList.PushClipRect(boardMin, boardMax, true);
        foreach (KeyLayoutData keyData in keysToDisplay)
        {
            ImVec2 keyMin = new(
                startPos.X + keyData.Col * keyStep.X + keyData.Row * keyRowOffset,
                startPos.Y + keyData.Row * keyStep.Y);
            ImVec2 keyMax = keyMin + keySize;
            drawList.AddRectFilled(keyMin, keyMax, 0xFFCCCCCC, keyRounding);
            drawList.AddRect(keyMin, keyMax, 0xFF181818, keyRounding);

            ImVec2 faceMin = new(keyMin.X + keyFacePos.X, keyMin.Y + keyFacePos.Y);
            ImVec2 faceMax = new(faceMin.X + keyFaceSize.X, faceMin.Y + keyFaceSize.Y);
            drawList.AddRect(faceMin, faceMax, 0xFFC1C1C1, keyFaceRounding, 2.0f);
            drawList.AddRectFilled(faceMin, faceMax, 0xFFFCFCFC, keyFaceRounding);

            ImVec2 labelMin = new(keyMin.X + keyLabelPos.X, keyMin.Y + keyLabelPos.Y);
            drawList.AddText(labelMin, 0xFF404040, keyData.Label);
            if (IsKeyDown(keyData.Key))
                drawList.AddRectFilled(keyMin, keyMax, 0x800000FF, keyRounding); // Red overlay while held.
        }
        drawList.PopClipRect();
    }

    #endregion

    #region SECTION: METRICS WINDOW (imgui.cpp:22184-23295)

    /// <summary>
    /// The smallest positive normal single-precision value, negated, which the metrics window passes
    /// where canonical passes <c>-FLT_MIN</c> to request a widget that fills the available width.
    /// </summary>
    /// <remarks>
    /// C# <see cref="float.Epsilon"/> is a subnormal and would not produce the same near-zero negative
    /// argument, so the literal value of the canonical <c>FLT_MIN</c> is used, matching the convention
    /// already documented in the widgets module.
    /// </remarks>
    private const float NegativeFloatMin = -1.17549435e-38f;

    /// <summary>
    /// The display names of the input sources, indexed by <see cref="ImGuiInputSource"/>.
    /// (<c>ThirdParty/imgui/imgui.cpp:11246</c>)
    /// </summary>
    private static readonly string[] InputSourceNames = { "None", "Mouse", "Keyboard", "Gamepad" };

    /// <summary>
    /// The display names of the mouse sources, indexed by <see cref="ImGuiMouseSource"/>.
    /// (<c>ThirdParty/imgui/imgui.cpp:11253</c>)
    /// </summary>
    private static readonly string[] MouseSourceNames = { "Mouse", "TouchScreen", "Pen" };

    /// <summary>
    /// The window-rectangle preset names listed by the metrics window's window-rectangle combo.
    /// (<c>ThirdParty/imgui/imgui.cpp:22614</c>)
    /// </summary>
    private static readonly string[] WrtRectsNames =
    {
        "OuterRect", "OuterRectClipped", "InnerRect", "InnerClipRect", "WorkRect", "Content", "ContentIdeal", "ContentRegionRect",
    };

    /// <summary>
    /// The table-rectangle preset names listed by the metrics window's table-rectangle combo.
    /// (<c>ThirdParty/imgui/imgui.cpp:22616</c>)
    /// </summary>
    private static readonly string[] TrtRectsNames =
    {
        "OuterRect", "InnerRect", "WorkRect", "HostClipRect", "InnerClipRect", "BackgroundClipRect", "ColumnsRect",
        "ColumnsWorkRect", "ColumnsClipRect", "ColumnsContentHeadersUsed", "ColumnsContentHeadersIdeal",
        "ColumnsContentFrozen", "ColumnsContentUnfrozen",
    };

    /// <summary>
    /// Scratch viewport list reused by the metrics window's inferred-z-order node so the per-frame sort
    /// does not allocate once the list has grown.
    /// <c>ThirdParty/imgui/imgui.cpp:22844</c> uses a function-static vector for the same reason.
    /// </summary>
    private static readonly List<ImGuiViewportP> ViewportsByInferredZOrder = new();

    /// <summary>
    /// Scratch text for the metrics window's UTF-8 encoding viewer. Canonical keeps a function-static
    /// <c>char[64]</c> so the typed text survives between frames; the managed port keeps the equivalent
    /// managed string instead. (<c>ThirdParty/imgui/imgui.cpp:22759</c>)
    /// </summary>
    private static string DebugTextEncodingBuffer = string.Empty;

    /// <summary>
    /// Whether the metrics window's docking node lists only root dock nodes. Canonical keeps this as a
    /// function-static flag so the choice survives between frames.
    /// (<c>ThirdParty/imgui/imgui.cpp:22939</c>)
    /// </summary>
    private static bool DebugDockRootNodesOnly = true;

    /// <summary>
    /// Returns the display name of an input source.
    /// (<c>ThirdParty/imgui/imgui.cpp:11244-11250</c>)
    /// </summary>
    /// <param name="source">The input source to name.</param>
    /// <returns>The source name, or <c>"Unknown"</c> when the value is outside the enumeration.</returns>
    private static string GetInputSourceName(ImGuiInputSource source)
    {
        ImAssert.Check(InputSourceNames.Length == (int)ImGuiInputSource.COUNT);
        int index = (int)source;
        return index < 0 || index >= InputSourceNames.Length ? "Unknown" : InputSourceNames[index];
    }

    /// <summary>
    /// Returns the display name of a mouse source.
    /// (<c>ThirdParty/imgui/imgui.cpp:11251-11261</c>)
    /// </summary>
    /// <param name="source">The mouse source to name.</param>
    /// <returns>The source name, or <c>"Unknown"</c> when the value is outside the enumeration.</returns>
    private static string GetMouseSourceName(ImGuiMouseSource source)
    {
        ImAssert.Check(MouseSourceNames.Length == (int)ImGuiMouseSource.COUNT);
        int index = (int)source;
        return index < 0 || index >= MouseSourceNames.Length ? "Unknown" : MouseSourceNames[index];
    }

    /// <summary>
    /// Compares two windows by the order in which they were begun within the context.
    /// (<c>ThirdParty/imgui/imgui.cpp:22783</c>)
    /// </summary>
    /// <param name="lhs">The left window.</param>
    /// <param name="rhs">The right window.</param>
    /// <returns>A negative value when <paramref name="lhs"/> was begun first.</returns>
    private static int WindowComparerByBeginOrder(ImGuiWindow lhs, ImGuiWindow rhs)
        => lhs.BeginOrderWithinContext - rhs.BeginOrderWithinContext;

    /// <summary>
    /// Compares two viewports by descending last-focused stamp, so the most recently focused viewport
    /// sorts first. (<c>ThirdParty/imgui/imgui.cpp:22245-22250</c>)
    /// </summary>
    /// <param name="lhs">The left viewport.</param>
    /// <param name="rhs">The right viewport.</param>
    /// <returns>A positive value when <paramref name="lhs"/> was focused more recently.</returns>
    private static int ViewportComparerByLastFocusedStampCount(ImGuiViewportP lhs, ImGuiViewportP rhs)
        => rhs.LastFocusedStampCount - lhs.LastFocusedStampCount;

    /// <summary>
    /// The cached delegate for <see cref="WindowComparerByBeginOrder"/>, so the per-frame begin-stack
    /// sort does not allocate a delegate.
    /// </summary>
    private static readonly Comparison<ImGuiWindow> WindowComparison = WindowComparerByBeginOrder;

    /// <summary>
    /// The cached delegate for <see cref="ViewportComparerByLastFocusedStampCount"/>, so the per-frame
    /// inferred-z-order sort does not allocate a delegate.
    /// </summary>
    private static readonly Comparison<ImGuiViewportP> ViewportComparison = ViewportComparerByLastFocusedStampCount;

    /// <summary>
    /// Draws the scaled thumbnail of every window hosted by one viewport, plus the viewport outline.
    /// (<c>ThirdParty/imgui/imgui.cpp:22184-22215</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4169</c>)
    /// </summary>
    /// <param name="drawList">The draw list that receives the viewport outline.</param>
    /// <param name="viewport">The viewport being thumbnailed.</param>
    /// <param name="bb">The screen rectangle the thumbnail occupies.</param>
    /// <remarks>
    /// Window rectangles are scaled from viewport space into the thumbnail rectangle and then clipped to
    /// it. A minimized viewport is drawn at thirty percent opacity, and the viewport whose identifier
    /// matches <see cref="ImGuiMetricsConfig.HighlightViewportID"/> gains a yellow outline. The window
    /// rectangles are drawn through the current window's own draw list, mirroring the canonical use of
    /// <c>g.CurrentWindow-&gt;DrawList</c> rather than the passed list.
    /// </remarks>
    internal static void DebugRenderViewportThumbnail(ImDrawList drawList, ImGuiViewportP viewport, ImRect bb)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;

        ImVec2 scale = bb.GetSize() / viewport.Size;
        ImVec2 off = bb.Min - viewport.Pos * scale;
        float alphaMul = (viewport.Flags & ImGuiViewportFlags.IsMinimized) != 0 ? 0.30f : 1.00f;
        window.DrawList.AddRectFilled(bb.Min, bb.Max, GetColorU32(ImGuiCol.Border, alphaMul * 0.40f));
        foreach (ImGuiWindow thumbWindow in g.Windows)
        {
            if (!thumbWindow.WasActive || (thumbWindow.Flags & ImGuiWindowFlags.ChildWindow) != 0)
                continue;
            if (thumbWindow.Viewport != viewport)
                continue;

            ImRect thumbRect = thumbWindow.Rect();
            ImRect titleRect = thumbWindow.TitleBarRect();
            thumbRect = new ImRect(Truncate(off + thumbRect.Min * scale), Truncate(off + thumbRect.Max * scale));
            // The title bar height is tripled so it stays visible at thumbnail scale.
            titleRect = new ImRect(
                Truncate(off + titleRect.Min * scale),
                Truncate(off + new ImVec2(titleRect.Max.X, titleRect.Min.Y + titleRect.GetHeight() * 3.0f) * scale));
            thumbRect.ClipWithFull(bb);
            titleRect.ClipWithFull(bb);
            bool windowIsFocused = g.NavWindow != null
                && thumbWindow.RootWindowForTitleBarHighlight == g.NavWindow.RootWindowForTitleBarHighlight;
            window.DrawList.AddRectFilled(thumbRect.Min, thumbRect.Max, GetColorU32(ImGuiCol.WindowBg, alphaMul));
            window.DrawList.AddRectFilled(titleRect.Min, titleRect.Max, GetColorU32(windowIsFocused ? ImGuiCol.TitleBgActive : ImGuiCol.TitleBg, alphaMul));
            window.DrawList.AddRect(thumbRect.Min, thumbRect.Max, GetColorU32(ImGuiCol.Border, alphaMul));
            window.DrawList.AddText(
                GetFont()!,
                g.FontSize * 1.0f,
                titleRect.Min,
                GetColorU32(ImGuiCol.Text, alphaMul),
                FindRenderedTextEnd(thumbWindow.Name));
        }
        drawList.AddRect(bb.Min, bb.Max, GetColorU32(ImGuiCol.Border, alphaMul));
        if (viewport.ID == g.DebugMetricsConfig.HighlightViewportID)
            window.DrawList.AddRect(bb.Min, bb.Max, 0xFF00FFFF); // Yellow: highlighted viewport.
    }

    /// <summary>
    /// Draws the monitor boundaries and every viewport thumbnail that make up the metrics window's
    /// Windows Minimap. (<c>ThirdParty/imgui/imgui.cpp:22217-22243</c>)
    /// </summary>
    /// <remarks>
    /// Every monitor is laid out in the same scaled space as the viewport thumbnails, and the highlighted
    /// monitor is outlined in yellow. The node reserves its area with <see cref="Dummy"/> after drawing,
    /// because the shapes are emitted directly to the draw list.
    /// </remarks>
    private static void RenderViewportsThumbnails()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;

        const float scale = 1.0f / 8.0f;
        ImRect bbFull = new(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);
        for (int monitorN = 0; monitorN < g.PlatformIO.Monitors.Count; monitorN++)
        {
            ImGuiPlatformMonitor monitor = g.PlatformIO.Monitors[monitorN];
            bbFull.Add(new ImRect(monitor.MainPos, monitor.MainPos + monitor.MainSize));
        }
        ImVec2 p = window.DC.CursorPos;
        ImVec2 off = p - bbFull.Min * scale;
        for (int monitorN = 0; monitorN < g.PlatformIO.Monitors.Count; monitorN++)
        {
            ImGuiPlatformMonitor monitor = g.PlatformIO.Monitors[monitorN];
            ImRect monitorDrawBb = new(off + monitor.MainPos * scale, off + (monitor.MainPos + monitor.MainSize) * scale);
            window.DrawList.AddRect(
                monitorDrawBb.Min,
                monitorDrawBb.Max,
                g.DebugMetricsConfig.HighlightMonitorIdx == monitorN ? 0xFF00FFFF : GetColorU32(ImGuiCol.Border),
                4.0f);
            window.DrawList.AddRectFilled(monitorDrawBb.Min, monitorDrawBb.Max, GetColorU32(ImGuiCol.Border, 0.10f), 4.0f);
        }

        foreach (ImGuiViewportP viewport in g.Viewports)
        {
            ImRect viewportDrawBb = new(off + viewport.Pos * scale, off + (viewport.Pos + viewport.Size) * scale);
            DebugRenderViewportThumbnail(window.DrawList, viewport, viewportDrawBb);
        }
        Dummy(bbFull.GetSize() * scale);
    }

    /// <summary>
    /// Reports one viewport: its identity, geometry, monitor binding, flags, and draw lists.
    /// (<c>ThirdParty/imgui/imgui.cpp:23867-23902</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4169</c>)
    /// </summary>
    /// <param name="viewport">The viewport to report.</param>
    /// <remarks>
    /// The node opens by itself once because the metrics window is usually inspected right after a
    /// viewport misbehaves. Hovering the node marks the viewport for the thumbnail highlight, and a
    /// non-primary viewport offers a reset-position button that also moves its owning window. Canonical
    /// leaves the standard platform-window flag out of the flag list; this port does the same.
    /// </remarks>
    internal static void DebugNodeViewport(ImGuiViewportP viewport)
    {
        ImGuiContext g = GetContext();
        SetNextItemOpen(true, ImGuiCond.Once);
        // Canonical seeds this node from the viewport identifier cast to a pointer; a stable identifier
        // string is used instead because a boxed integer has no stable managed hash.
        bool open = TreeNode(
            $"##Viewport{viewport.ID:X8}",
            "Viewport #%d, ID: 0x%08X, Parent: 0x%08X, Window: \"%s\"",
            viewport.Idx,
            viewport.ID,
            viewport.ParentViewportId,
            viewport.Window != null ? viewport.Window.Name : "N/A");
        if (IsItemHovered())
            g.DebugMetricsConfig.HighlightViewportID = viewport.ID;
        if (open)
        {
            ImGuiViewportFlags flags = viewport.Flags;
            BulletText(
                "Main Pos: (%.0f,%.0f), Size: (%.0f,%.0f)\nFrameBufferScale: (%.2f,%.2f)\nWorkArea Inset Left: %.0f Top: %.0f, Right: %.0f, Bottom: %.0f\nMonitor: %d, DpiScale: %.0f%%",
                viewport.Pos.X,
                viewport.Pos.Y,
                viewport.Size.X,
                viewport.Size.Y,
                viewport.FramebufferScale.X,
                viewport.FramebufferScale.Y,
                viewport.WorkInsetMin.X,
                viewport.WorkInsetMin.Y,
                viewport.WorkInsetMax.X,
                viewport.WorkInsetMax.Y,
                (int)viewport.PlatformMonitor,
                viewport.DpiScale * 100.0f);
            if (viewport.Idx > 0)
            {
                SameLine();
                if (SmallButton("Reset Pos"))
                {
                    viewport.Pos = new ImVec2(200, 200);
                    viewport.UpdateWorkRect();
                    if (viewport.Window != null)
                        viewport.Window.Pos = viewport.Pos;
                }
            }
            BulletText(
                "Flags: 0x%04X =%s%s%s%s%s%s%s%s%s%s%s%s%s",
                (int)flags,
                // IsPlatformWindow is omitted because it is the standard flag.
                (flags & ImGuiViewportFlags.IsPlatformMonitor) != 0 ? " IsPlatformMonitor" : string.Empty,
                (flags & ImGuiViewportFlags.IsMinimized) != 0 ? " IsMinimized" : string.Empty,
                (flags & ImGuiViewportFlags.IsFocused) != 0 ? " IsFocused" : string.Empty,
                (flags & ImGuiViewportFlags.OwnedByApp) != 0 ? " OwnedByApp" : string.Empty,
                (flags & ImGuiViewportFlags.NoDecoration) != 0 ? " NoDecoration" : string.Empty,
                (flags & ImGuiViewportFlags.NoTaskBarIcon) != 0 ? " NoTaskBarIcon" : string.Empty,
                (flags & ImGuiViewportFlags.NoFocusOnAppearing) != 0 ? " NoFocusOnAppearing" : string.Empty,
                (flags & ImGuiViewportFlags.NoFocusOnClick) != 0 ? " NoFocusOnClick" : string.Empty,
                (flags & ImGuiViewportFlags.NoInputs) != 0 ? " NoInputs" : string.Empty,
                (flags & ImGuiViewportFlags.NoRendererClear) != 0 ? " NoRendererClear" : string.Empty,
                (flags & ImGuiViewportFlags.NoAutoMerge) != 0 ? " NoAutoMerge" : string.Empty,
                (flags & ImGuiViewportFlags.TopMost) != 0 ? " TopMost" : string.Empty,
                (flags & ImGuiViewportFlags.CanHostOtherWindows) != 0 ? " CanHostOtherWindows" : string.Empty);
            foreach (ImDrawList drawList in viewport.DrawDataP.CmdLists)
                DebugNodeDrawList(null, viewport, drawList, "DrawList");
            TreePop();
        }
    }

    /// <summary>
    /// Submits the canonical Dear ImGui Metrics/Debugger window.
    /// (<c>ThirdParty/imgui/imgui.cpp:22578-23294</c>, declared <c>ThirdParty/imgui/imgui.h:1185</c>)
    /// </summary>
    /// <param name="pOpen">Tracks whether the window is open; cleared when the user closes it.</param>
    /// <remarks>
    /// <para>
    /// The window gathers the frame statistics, the tools for enabling debug overlays, the window, draw
    /// list, viewport, popup, tab bar, table, input, docking, settings, allocation, and input-state
    /// reports, and then draws the windows, tables, and docking overlays that were requested through the
    /// tools. It is the primary written record of how a frame was assembled.
    /// </para>
    /// <para>
    /// Canonical passes many context flags to widgets by address. In this port those flags are public
    /// properties, which cannot be passed by <see langword="ref"/>, so each such call reads the property
    /// into a local, passes the local by reference, and writes it back immediately. Writing back
    /// immediately, rather than at the end of the function, is required because sections such as
    /// <c>DrawLists</c> consult the same configuration while the window is still being built.
    /// </para>
    /// </remarks>
    public static void ShowMetricsWindow(ref bool pOpen)
    {
        ImGuiContext g = GetContext();
        ImGuiIO io = g.IO;
        ImGuiPlatformIO platformIO = g.PlatformIO;
        ImGuiMetricsConfig cfg = g.DebugMetricsConfig;
        if (cfg.ShowDebugLog)
        {
            bool showDebugLog = cfg.ShowDebugLog;
            ShowDebugLogWindow(ref showDebugLog);
            cfg.ShowDebugLog = showDebugLog;
        }
        if (cfg.ShowIDStackTool)
        {
            bool showIDStackTool = cfg.ShowIDStackTool;
            ShowIDStackToolWindow(ref showIDStackTool);
            cfg.ShowIDStackTool = showIDStackTool;
        }

        if (!Begin("Dear ImGui Metrics/Debugger", ref pOpen) || GetCurrentWindow().BeginCount > 1)
        {
            End();
            return;
        }

        // [DEBUG] Clear debug breaks hooks after exactly one cycle.
        DebugBreakClearData();

        // Basic info
        Text("Dear ImGui %s (%d)", ImGuiVersion, ImGuiVersionNum);
        if (g.ContextName.Length != 0)
        {
            SameLine();
            Text("(Context Name: \"%s\")", g.ContextName);
        }
        Text("Application average %.3f ms/frame (%.1f FPS)", 1000.0f / io.Framerate, io.Framerate);
        Text("%d vertices, %d indices (%d triangles)", io.MetricsRenderVertices, io.MetricsRenderIndices, io.MetricsRenderIndices / 3);
        Text("%d visible windows, %d current allocations", io.MetricsRenderWindows, g.DebugAllocInfo.TotalAllocCount - g.DebugAllocInfo.TotalFreeCount);

        Separator();

        // Debugging enums
        const int WRT_OuterRect = 0;
        const int WRT_OuterRectClipped = 1;
        const int WRT_InnerRect = 2;
        const int WRT_InnerClipRect = 3;
        const int WRT_WorkRect = 4;
        const int WRT_Content = 5;
        const int WRT_ContentIdeal = 6;
        const int WRT_ContentRegionRect = 7;
        const int WRT_Count = 8;
        const int TRT_OuterRect = 0;
        const int TRT_InnerRect = 1;
        const int TRT_WorkRect = 2;
        const int TRT_HostClipRect = 3;
        const int TRT_InnerClipRect = 4;
        const int TRT_BackgroundClipRect = 5;
        const int TRT_ColumnsRect = 6;
        const int TRT_ColumnsWorkRect = 7;
        const int TRT_ColumnsClipRect = 8;
        const int TRT_ColumnsContentHeadersUsed = 9;
        const int TRT_ColumnsContentHeadersIdeal = 10;
        const int TRT_ColumnsContentFrozen = 11;
        const int TRT_ColumnsContentUnfrozen = 12;
        const int TRT_Count = 13;
        int showWindowsRectsTypeInitial = cfg.ShowWindowsRectsType;
        if (showWindowsRectsTypeInitial < 0)
            showWindowsRectsTypeInitial = WRT_WorkRect;
        cfg.ShowWindowsRectsType = showWindowsRectsTypeInitial;
        int showTablesRectsTypeInitial = cfg.ShowTablesRectsType;
        if (showTablesRectsTypeInitial < 0)
            showTablesRectsTypeInitial = TRT_WorkRect;
        cfg.ShowTablesRectsType = showTablesRectsTypeInitial;

        // Resolves one table rectangle preset for the canonical Funcs::GetTableRect helper.
        static ImRect GetTableRect(ImGuiTable table, int rectType, int n)
        {
            ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent); // Always using last submitted instance.
            if (rectType == TRT_OuterRect)
                return table.OuterRect;
            if (rectType == TRT_InnerRect)
                return table.InnerRect;
            if (rectType == TRT_WorkRect)
                return table.WorkRect;
            if (rectType == TRT_HostClipRect)
                return table.HostClipRect;
            if (rectType == TRT_InnerClipRect)
                return table.InnerClipRect;
            if (rectType == TRT_BackgroundClipRect)
                return table.BgClipRect;
            if (rectType == TRT_ColumnsRect)
            {
                ImGuiTableColumn c = table.Columns[n];
                return new ImRect(c.MinX, table.InnerClipRect.Min.Y, c.MaxX, table.InnerClipRect.Min.Y + tableInstance.LastOuterHeight);
            }
            if (rectType == TRT_ColumnsWorkRect)
            {
                ImGuiTableColumn c = table.Columns[n];
                return new ImRect(c.WorkMinX, table.WorkRect.Min.Y, c.WorkMaxX, table.WorkRect.Max.Y);
            }
            if (rectType == TRT_ColumnsClipRect)
                return table.Columns[n].ClipRect;
            if (rectType == TRT_ColumnsContentHeadersUsed)
            {
                ImGuiTableColumn c = table.Columns[n];
                return new ImRect(c.WorkMinX, table.InnerClipRect.Min.Y, c.ContentMaxXHeadersUsed, table.InnerClipRect.Min.Y + tableInstance.LastTopHeadersRowHeight);
            }
            if (rectType == TRT_ColumnsContentHeadersIdeal)
            {
                ImGuiTableColumn c = table.Columns[n];
                return new ImRect(c.WorkMinX, table.InnerClipRect.Min.Y, c.ContentMaxXHeadersIdeal, table.InnerClipRect.Min.Y + tableInstance.LastTopHeadersRowHeight);
            }
            if (rectType == TRT_ColumnsContentFrozen)
            {
                ImGuiTableColumn c = table.Columns[n];
                return new ImRect(c.WorkMinX, table.InnerClipRect.Min.Y, c.ContentMaxXFrozen, table.InnerClipRect.Min.Y + tableInstance.LastFrozenHeight);
            }
            if (rectType == TRT_ColumnsContentUnfrozen)
            {
                ImGuiTableColumn c = table.Columns[n];
                return new ImRect(c.WorkMinX, table.InnerClipRect.Min.Y + tableInstance.LastFrozenHeight, c.ContentMaxXUnfrozen, table.InnerClipRect.Max.Y);
            }
            ImAssert.Check(false);
            return new ImRect();
        }

        // Resolves one window rectangle preset for the canonical Funcs::GetWindowRect helper.
        static ImRect GetWindowRect(ImGuiWindow window, int rectType)
        {
            if (rectType == WRT_OuterRect)
                return window.Rect();
            if (rectType == WRT_OuterRectClipped)
                return window.OuterRectClipped;
            if (rectType == WRT_InnerRect)
                return window.InnerRect;
            if (rectType == WRT_InnerClipRect)
                return window.InnerClipRect;
            if (rectType == WRT_WorkRect)
                return window.WorkRect;
            if (rectType == WRT_Content)
            {
                ImVec2 min = window.InnerRect.Min - window.Scroll + window.WindowPadding;
                return new ImRect(min, min + window.ContentSize);
            }
            if (rectType == WRT_ContentIdeal)
            {
                ImVec2 min = window.InnerRect.Min - window.Scroll + window.WindowPadding;
                return new ImRect(min, min + window.ContentSizeIdeal);
            }
            if (rectType == WRT_ContentRegionRect)
                return window.ContentRegionRect;
            ImAssert.Check(false);
            return new ImRect();
        }

        // The canonical IMGUI_DEBUG_HIGHLIGHT_ALL_ID_CONFLICTS build-time banner has no managed
        // equivalent, because the port has no compiled build-flag surface.

        // Tools
        if (TreeNode("Tools"))
        {
            // Debug Break features
            // The Item Picker tool is useful to visually select an item and break into the call-stack of
            // where it was submitted.
            SeparatorTextEx(0, "Debug breaks", FindRenderedTextEnd("Debug breaks"), CalcTextSize("(?)").X + g.Style.SeparatorTextPadding.X);
            SameLine();
            MetricsHelpMarker("Will call the IM_DEBUG_BREAK() macro to break in debugger.\nWarning: If you don't have a debugger attached, this will probably crash.");
            bool debugItemPickerActive = g.DebugItemPickerActive;
            bool itemPickerChanged = Checkbox("Show Item Picker", ref debugItemPickerActive);
            g.DebugItemPickerActive = debugItemPickerActive;
            if (itemPickerChanged && debugItemPickerActive)
                DebugStartItemPicker();
            bool configDebugIsDebuggerPresent = g.IO.ConfigDebugIsDebuggerPresent;
            Checkbox("Show \"Debug Break\" buttons in other sections (io.ConfigDebugIsDebuggerPresent)", ref configDebugIsDebuggerPresent);
            g.IO.ConfigDebugIsDebuggerPresent = configDebugIsDebuggerPresent;

            SeparatorText("Visualize");

            bool showDebugLog = cfg.ShowDebugLog;
            Checkbox("Show Debug Log", ref showDebugLog);
            cfg.ShowDebugLog = showDebugLog;
            SameLine();
            MetricsHelpMarker("You can also call ImGui::ShowDebugLogWindow() from your code.");

            bool showIDStackTool = cfg.ShowIDStackTool;
            Checkbox("Show ID Stack Tool", ref showIDStackTool);
            cfg.ShowIDStackTool = showIDStackTool;
            SameLine();
            MetricsHelpMarker("You can also call ImGui::ShowIDStackToolWindow() from your code.");

            bool showWindowsBeginOrder = cfg.ShowWindowsBeginOrder;
            Checkbox("Show windows begin order", ref showWindowsBeginOrder);
            cfg.ShowWindowsBeginOrder = showWindowsBeginOrder;
            bool showWindowsRects = cfg.ShowWindowsRects;
            Checkbox("Show windows rectangles", ref showWindowsRects);
            cfg.ShowWindowsRects = showWindowsRects;
            SameLine();
            SetNextItemWidth(GetFontSize() * 12);
            int showWindowsRectsType = cfg.ShowWindowsRectsType;
            showWindowsRects |= Combo("##show_windows_rect_type", ref showWindowsRectsType, WrtRectsNames, WrtRectsNames.Length);
            cfg.ShowWindowsRects = showWindowsRects;
            cfg.ShowWindowsRectsType = showWindowsRectsType;
            if (cfg.ShowWindowsRects && g.NavWindow != null)
            {
                BulletText("'%s':", g.NavWindow.Name);
                Indent();
                for (int rectN = 0; rectN < WRT_Count; rectN++)
                {
                    ImRect r = GetWindowRect(g.NavWindow, rectN);
                    Text(
                        "(%6.1f,%6.1f) (%6.1f,%6.1f) Size (%6.1f,%6.1f) %s",
                        r.Min.X,
                        r.Min.Y,
                        r.Max.X,
                        r.Max.Y,
                        r.GetWidth(),
                        r.GetHeight(),
                        WrtRectsNames[rectN]);
                }
                Unindent();
            }

            bool showTablesRects = cfg.ShowTablesRects;
            Checkbox("Show tables rectangles", ref showTablesRects);
            cfg.ShowTablesRects = showTablesRects;
            SameLine();
            SetNextItemWidth(GetFontSize() * 12);
            int showTablesRectsType = cfg.ShowTablesRectsType;
            showTablesRects |= Combo("##show_table_rects_type", ref showTablesRectsType, TrtRectsNames, TrtRectsNames.Length);
            cfg.ShowTablesRects = showTablesRects;
            cfg.ShowTablesRectsType = showTablesRectsType;
            if (cfg.ShowTablesRects && g.NavWindow != null)
            {
                for (int tableN = 0; tableN < g.Tables.Count; tableN++)
                {
                    ImGuiTable? table = tableN < g.Tables.Count ? g.Tables[tableN] : null;
                    if (table == null || table.LastFrameActive < g.FrameCount - 1 || (table.OuterWindow != g.NavWindow && table.InnerWindow != g.NavWindow))
                        continue;

                    BulletText("Table 0x%08X (%d columns, in '%s')", table.ID, table.ColumnsCount, table.OuterWindow!.Name);
                    if (IsItemHovered())
                        GetForegroundDrawList(table.OuterWindow!).AddRect(table.OuterRect.Min - new ImVec2(1, 1), table.OuterRect.Max + new ImVec2(1, 1), 0xFF00FFFF, 0.0f, 2.0f);
                    Indent();
                    for (int rectN = 0; rectN < TRT_Count; rectN++)
                    {
                        if (rectN >= TRT_ColumnsRect)
                        {
                            if (rectN != TRT_ColumnsRect && rectN != TRT_ColumnsClipRect)
                                continue;
                            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                            {
                                ImRect r = GetTableRect(table, rectN, columnN);
                                string buf = ImFormatString(
                                    "(%6.1f,%6.1f) (%6.1f,%6.1f) Size (%6.1f,%6.1f) Col %d %s",
                                    r.Min.X,
                                    r.Min.Y,
                                    r.Max.X,
                                    r.Max.Y,
                                    r.GetWidth(),
                                    r.GetHeight(),
                                    columnN,
                                    TrtRectsNames[rectN]);
                                Selectable(buf);
                                if (IsItemHovered())
                                    GetForegroundDrawList(table.OuterWindow!).AddRect(r.Min - new ImVec2(1, 1), r.Max + new ImVec2(1, 1), 0xFF00FFFF, 0.0f, 2.0f);
                            }
                        }
                        else
                        {
                            ImRect r = GetTableRect(table, rectN, -1);
                            string buf = ImFormatString(
                                "(%6.1f,%6.1f) (%6.1f,%6.1f) Size (%6.1f,%6.1f) %s",
                                r.Min.X,
                                r.Min.Y,
                                r.Max.X,
                                r.Max.Y,
                                r.GetWidth(),
                                r.GetHeight(),
                                TrtRectsNames[rectN]);
                            Selectable(buf);
                            if (IsItemHovered())
                                GetForegroundDrawList(table.OuterWindow!).AddRect(r.Min - new ImVec2(1, 1), r.Max + new ImVec2(1, 1), 0xFF00FFFF, 0.0f, 2.0f);
                        }
                    }
                    Unindent();
                }
            }
            // Stored in the context because the group code uses it in hot data.
            bool debugShowGroupRects = g.DebugShowGroupRects;
            Checkbox("Show groups rectangles", ref debugShowGroupRects);
            g.DebugShowGroupRects = debugShowGroupRects;

            SeparatorText("Validate");

            bool configDebugBeginReturnValueLoop = io.ConfigDebugBeginReturnValueLoop;
            Checkbox("Debug Begin/BeginChild return value", ref configDebugBeginReturnValueLoop);
            io.ConfigDebugBeginReturnValueLoop = configDebugBeginReturnValueLoop;
            SameLine();
            MetricsHelpMarker("Some calls to Begin()/BeginChild() will return false.\n\nWill cycle through window depths then repeat. Windows should be flickering while running.");

            bool showTextEncodingViewer = cfg.ShowTextEncodingViewer;
            Checkbox("UTF-8 Encoding viewer", ref showTextEncodingViewer);
            cfg.ShowTextEncodingViewer = showTextEncodingViewer;
            SameLine();
            MetricsHelpMarker("You can also call ImGui::DebugTextEncoding() from your code with a given string to test that your UTF-8 encoding settings are correct.");
            if (cfg.ShowTextEncodingViewer)
            {
                SetNextItemWidth(NegativeFloatMin);
                InputText("##DebugTextEncodingBuf", ref DebugTextEncodingBuffer);
                if (DebugTextEncodingBuffer.Length != 0)
                    DebugTextEncoding(DebugTextEncodingBuffer);
            }

            TreePop();
        }
        // Windows
        if (TreeNode("Windows", "Windows (%d)", g.Windows.Count))
        {
            DebugNodeWindowsList(g.Windows, "By display order");
            DebugNodeWindowsList(g.WindowsFocusOrder, "By focus order (root windows)");
            if (TreeNode("By submission order (begin stack)"))
            {
                // Windows are displayed in submitted order and hierarchy, but the begin stack does not
                // constitute a Parent<>Child relationship.
                List<ImGuiWindow> tempBuffer = g.WindowsTempSortBuffer;
                tempBuffer.Clear();
                foreach (ImGuiWindow window in g.Windows)
                {
                    if (window.LastFrameActive + 1 >= g.FrameCount)
                        tempBuffer.Add(window);
                }
                tempBuffer.Sort(WindowComparison);
                DebugNodeWindowsListByBeginStackParent(tempBuffer, 0, tempBuffer.Count, null);
                TreePop();
            }

            TreePop();
        }

        // DrawLists
        int drawlistCount = 0;
        foreach (ImGuiViewportP viewport in g.Viewports)
            drawlistCount += viewport.DrawDataP.CmdLists.Count;
        if (TreeNode("DrawLists", "DrawLists (%d)", drawlistCount))
        {
            bool showDrawCmdMesh = cfg.ShowDrawCmdMesh;
            Checkbox("Show ImDrawCmd mesh when hovering", ref showDrawCmdMesh);
            cfg.ShowDrawCmdMesh = showDrawCmdMesh;
            bool showDrawCmdBoundingBoxes = cfg.ShowDrawCmdBoundingBoxes;
            Checkbox("Show ImDrawCmd bounding boxes when hovering", ref showDrawCmdBoundingBoxes);
            cfg.ShowDrawCmdBoundingBoxes = showDrawCmdBoundingBoxes;
            foreach (ImGuiViewportP viewport in g.Viewports)
            {
                bool viewportHasDrawlist = false;
                foreach (ImDrawList drawList in viewport.DrawDataP.CmdLists)
                {
                    if (!viewportHasDrawlist)
                        Text("Active DrawLists in Viewport #%d, ID: 0x%08X", viewport.Idx, viewport.ID);
                    viewportHasDrawlist = true;
                    DebugNodeDrawList(null, viewport, drawList, "DrawList");
                }
            }
            TreePop();
        }

        // Viewports
        if (TreeNode("Viewports", "Viewports (%d)", g.Viewports.Count))
        {
            cfg.HighlightMonitorIdx = -1;
            bool open = TreeNode("Monitors", "Monitors (%d)", g.PlatformIO.Monitors.Count);
            SameLine();
            MetricsHelpMarker("Dear ImGui uses monitor data:\n- to query DPI settings on a per monitor basis\n- to position popup/tooltips so they don't straddle monitors.");
            if (open)
            {
                for (int monitorN = 0; monitorN < g.PlatformIO.Monitors.Count; monitorN++)
                {
                    DebugNodePlatformMonitor(g.PlatformIO.Monitors[monitorN], "Monitor", monitorN);
                    if (IsItemHovered())
                        cfg.HighlightMonitorIdx = monitorN;
                }
                DebugNodePlatformMonitor(g.FallbackMonitor, "Fallback", 0);
                TreePop();
            }

            SetNextItemOpen(true, ImGuiCond.Once);
            if (TreeNode("Windows Minimap"))
            {
                RenderViewportsThumbnails();
                TreePop();
            }
            cfg.HighlightViewportID = 0;

            BulletText(
                "MouseViewport: 0x%08X (UserHovered 0x%08X, LastHovered 0x%08X)",
                g.MouseViewport != null ? g.MouseViewport.ID : 0,
                g.IO.MouseHoveredViewport,
                g.MouseLastHoveredViewport != null ? g.MouseLastHoveredViewport.ID : 0);
            if (TreeNode("Inferred Z order (front-to-back)"))
            {
                ViewportsByInferredZOrder.Clear();
                ViewportsByInferredZOrder.AddRange(g.Viewports);
                if (ViewportsByInferredZOrder.Count > 1)
                    ViewportsByInferredZOrder.Sort(ViewportComparison);
                foreach (ImGuiViewportP viewport in ViewportsByInferredZOrder)
                {
                    BulletText(
                        "Viewport #%d, ID: 0x%08X, LastFocused = %08d, PlatformFocused = %s, Window: \"%s\"",
                        viewport.Idx,
                        viewport.ID,
                        viewport.LastFocusedStampCount,
                        g.PlatformIO.Platform_GetWindowFocus != null && viewport.PlatformWindowCreated
                            ? (g.PlatformIO.Platform_GetWindowFocus(viewport) ? "1" : "0")
                            : "N/A",
                        viewport.Window != null ? viewport.Window.Name : "N/A");
                    if (IsItemHovered())
                        cfg.HighlightViewportID = viewport.ID;
                }
                TreePop();
            }

            foreach (ImGuiViewportP viewport in g.Viewports)
                DebugNodeViewport(viewport);
            TreePop();
        }
        // Details for Fonts
        // Canonical enumerates g.FontAtlases and opens one node per atlas. The port owns a single atlas
        // through io.Fonts, so that one atlas is reported here.
        ImFontAtlas fonts = io.Fonts;
        if (TreeNode("Fonts", "Fonts (%d), Textures (%d)", fonts.Fonts.Count, fonts.TexData != null ? 1 : 0))
        {
            ShowFontAtlas(fonts);
            TreePop();
        }

        // Details for Popups
        if (TreeNode("Popups", "Popups (%d)", g.OpenPopupStack.Count))
        {
            // Interacting with tree nodes while popups are open is difficult, so everything is displayed
            // inline.
            foreach (ImGuiPopupData popupData in g.OpenPopupStack)
            {
                ImGuiWindow? window = popupData.Window;
                BulletText(
                    "PopupID: %08x, Window: '%s' (%s%s), RestoreNavWindow '%s', ParentWindow '%s'",
                    popupData.PopupId,
                    window != null ? window.Name : "NULL",
                    window != null && (window.Flags & ImGuiWindowFlags.ChildWindow) != 0 ? "Child;" : string.Empty,
                    window != null && (window.Flags & ImGuiWindowFlags.ChildMenu) != 0 ? "Menu;" : string.Empty,
                    popupData.RestoreNavWindow != null ? popupData.RestoreNavWindow.Name : "NULL",
                    window != null && window.ParentWindow != null ? window.ParentWindow.Name : "NULL");
            }
            TreePop();
        }

        // Details for TabBars
        if (TreeNode("TabBars", "Tab Bars (%d)", g.TabBars.Count))
        {
            for (int n = 0; n < g.TabBars.Capacity; n++)
            {
                ImGuiTabBar? tabBar = g.TabBars.GetByIndex(n);
                if (tabBar == null)
                    continue;
                // Canonical seeds this identity from the tab bar pointer; a stable identifier string is
                // used instead because a boxed value has no stable managed hash.
                PushID($"##TabBar{tabBar.ID:X8}");
                DebugNodeTabBar(tabBar, "TabBar");
                PopID();
            }
            TreePop();
        }

        // Details for Tables
        if (TreeNode("Tables", "Tables (%d)", g.Tables.Count))
        {
            for (int n = 0; n < g.Tables.Count; n++)
                DebugNodeTable(g.Tables[n]);
            TreePop();
        }

        // Details for InputText
        if (TreeNode("InputText"))
        {
            DebugNodeInputTextState(g.InputTextState);
            TreePop();
        }

        // Details for TypingSelect
        if (TreeNode("TypingSelect", "TypingSelect (%d)", g.TypingSelectState.SearchBuffer.Length != 0 ? 1 : 0))
        {
            DebugNodeTypingSelectState(g.TypingSelectState);
            TreePop();
        }

        // Details for MultiSelect
        if (TreeNode("MultiSelect", "MultiSelect (%d)", g.MultiSelectStorage.Count))
        {
            ImGuiBoxSelectState bs = g.BoxSelectState;
            BulletText("BoxSelect ID=0x%08X, Starting = %d, Active %d", bs.ID, bs.IsStarting, bs.IsActive);
            for (int n = 0; n < g.MultiSelectStorage.Capacity; n++)
            {
                ImGuiMultiSelectState? state = g.MultiSelectStorage.GetByIndex(n);
                if (state != null)
                    DebugNodeMultiSelectState(state);
            }
            TreePop();
        }

        // Details for Docking
        if (TreeNode("Docking"))
        {
            ImGuiDockContext dc = g.DockContext;
            Checkbox("List root nodes", ref DebugDockRootNodesOnly);
            bool showDockingNodes = cfg.ShowDockingNodes;
            Checkbox("Ctrl shows window dock info", ref showDockingNodes);
            cfg.ShowDockingNodes = showDockingNodes;
            if (SmallButton("Clear nodes"))
                DockContextClearNodes(g, 0, true);
            SameLine();
            if (SmallButton("Rebuild all"))
                dc.WantFullRebuild = true;
            // Canonical walks the dock-node pool's slot array; this port keeps the nodes in a dictionary,
            // so the values are enumerated instead.
            foreach (ImGuiDockNode node in dc.Nodes.Values)
            {
                if (!DebugDockRootNodesOnly || node.IsRootNode())
                    DebugNodeDockNode(node, "Node");
            }
            TreePop();
        }

        // Settings
        if (TreeNode("Settings"))
        {
            if (SmallButton("Clear"))
                ClearIniSettings();
            SameLine();
            if (SmallButton("Save to memory"))
                SaveIniSettingsToMemory();
            SameLine();
            if (SmallButton("Save to disk"))
                SaveIniSettingsToDisk(g.IO.IniFilename!);
            SameLine();
            if (g.IO.IniFilename != null)
                Text("\"%s\"", g.IO.IniFilename);
            else
                TextUnformatted("<NULL>");
            Text("SettingsDirtyTimer %.2f", g.SettingsDirtyTimer);

            int highlightOlderThanDate = 0;
            Text("SessionDate: %d", platformIO.Platform_SessionDate);
            BeginDisabled(platformIO.Platform_SessionDate == 0);
            bool settingsHighlightOldEntries = cfg.SettingsHighlightOldEntries;
            Checkbox("Highlight Entries Older Than", ref settingsHighlightOldEntries);
            cfg.SettingsHighlightOldEntries = settingsHighlightOldEntries;
            SetNextItemWidth(GetFontSize() * 8);
            SameLine();
            int settingsDiscardMonths = cfg.SettingsDiscardMonths;
            SliderInt("Months", ref settingsDiscardMonths, 1, 24);
            cfg.SettingsDiscardMonths = settingsDiscardMonths;
            if (cfg.SettingsHighlightOldEntries && cfg.SettingsDiscardMonths > 0)
            {
                ImGuiPackedDate cutoffDate = new((uint)platformIO.Platform_SessionDate);
                cutoffDate.SubtractMonths(cfg.SettingsDiscardMonths);
                highlightOlderThanDate = cutoffDate.Unpack();
                SameLine();
                if (Button("Discard"))
                    CleanupIniSettings(new ImGuiSettingsCleanupArgs { DiscardOlderThanMonths = cfg.SettingsDiscardMonths });
            }
            EndDisabled();
            bool configDebugIniSettings = io.ConfigDebugIniSettings;
            Checkbox("io.ConfigDebugIniSettings", ref configDebugIniSettings);
            io.ConfigDebugIniSettings = configDebugIniSettings;

            if (TreeNode("SettingsHandlers", "Settings handlers: (%d)", g.SettingsHandlers.Count))
            {
                foreach (ImGuiSettingsHandler handler in g.SettingsHandlers)
                    BulletText("\"%s\"", handler.TypeName);
                TreePop();
            }
            // Canonical reports the packed chunk-stream byte size; this port stores settings records in a
            // list until the settings chunk realizes the stream, so the record count is reported instead.
            if (TreeNode("SettingsWindows", "Settings packed data: Windows: %d entries", g.SettingsWindows.Count))
            {
                foreach (ImGuiWindowSettings settings in g.SettingsWindows)
                {
                    // Canonical tints entries older than the cutoff so stale layout data is visible at a glance.
                    bool highlight = highlightOlderThanDate != 0 && settings.LastUsedDate.Unpack() < highlightOlderThanDate;
                    if (highlight)
                        PushStyleColor(ImGuiCol.Text, new ImVec4(1.0f, 0.4f, 0.4f, 1.0f));
                    DebugNodeWindowSettings(settings);
                    if (highlight)
                        PopStyleColor();
                }
                TreePop();
            }

            if (TreeNode("SettingsTables", "Settings packed data: Tables: %d entries", g.SettingsTables.Count))
            {
                foreach (ImGuiTableSettings settings in g.SettingsTables)
                {
                    bool highlight = highlightOlderThanDate != 0 && settings.LastUsedDate.Unpack() < highlightOlderThanDate;
                    if (highlight)
                        PushStyleColor(ImGuiCol.Text, new ImVec4(1.0f, 0.4f, 0.4f, 1.0f));
                    DebugNodeTableSettings(settings, null);
                    if (highlight)
                        PopStyleColor();
                }
                TreePop();
            }

            if (TreeNode("SettingsDocking", "Settings packed data: Docking"))
            {
                ImGuiDockContext dc = g.DockContext;
                Text("In SettingsWindows:");
                foreach (ImGuiWindowSettings settings in g.SettingsWindows)
                {
                    if (settings.DockId != 0)
                        BulletText("Window '%s' -> DockId %08X DockOrder=%d", settings.Name, settings.DockId, settings.DockOrder);
                }
                Text("In SettingsNodes:");
                for (int n = 0; n < dc.NodesSettings.Count; n++)
                {
                    ImGuiDockNodeSettings settings = dc.NodesSettings[n];
                    string? selectedTabName = null;
                    if (settings.SelectedTabId != 0)
                    {
                        ImGuiWindow? window = FindWindowByID(settings.SelectedTabId);
                        if (window != null)
                            selectedTabName = window.Name;
                        else
                        {
                            ImGuiWindowSettings? windowSettings = FindWindowSettingsByID(settings.SelectedTabId);
                            if (windowSettings != null)
                                selectedTabName = windowSettings.Name;
                        }
                    }
                    BulletText(
                        "Node %08X, Parent %08X, SelectedTab %08X ('%s')",
                        settings.ID,
                        settings.ParentNodeId,
                        settings.SelectedTabId,
                        selectedTabName ?? (settings.SelectedTabId != 0 ? "N/A" : string.Empty));
                }
                TreePop();
            }

            if (TreeNode("SettingsIniData", "Settings unpacked data (.ini): %d bytes", g.SettingsIniData.Size))
            {
                // Canonical passes the live packed buffer; this port passes a managed copy into the
                // read-only multiline editor.
                string iniData = g.SettingsIniData.CStr();
                InputTextMultiline(
                    "##Ini",
                    ref iniData,
                    new ImVec2(NegativeFloatMin, g.FontSize * 20.0f),
                    ImGuiInputTextFlags.ReadOnly);
                TreePop();
            }
            TreePop();
        }
        // Settings
        if (TreeNode("Memory allocations"))
        {
            ImGuiDebugAllocInfo info = g.DebugAllocInfo;
            Text("%d current allocations", info.TotalAllocCount - info.TotalFreeCount);
            Text("Releasing selected unused buffers after: %.2f secs", g.IO.ConfigMemoryCompactTimer);
            if (SmallButton("GC now"))
                g.GcCompactAll = true;
            Text("Recent frames with allocations:");
            int bufSize = info.LastEntriesBuf.Length;
            for (int n = bufSize - 1; n >= 0; n--)
            {
                ImGuiDebugAllocEntry entry = info.LastEntriesBuf[(info.LastEntriesIdx - n + bufSize) % bufSize];
                BulletText("Frame %06d: %+3d ( %2d alloc, %2d free )", entry.FrameCount, entry.AllocCount - entry.FreeCount, entry.AllocCount, entry.FreeCount);
                if (n == 0)
                {
                    SameLine();
                    Text("<- %d frames ago", g.FrameCount - entry.FrameCount);
                }
            }
            TreePop();
        }

        if (TreeNode("Inputs"))
        {
            Text("KEYBOARD/GAMEPAD/MOUSE KEYS");
            {
                // User code should never have to go through such hoops; iterating between
                // ImGuiKey_NamedKey_BEGIN and ImGuiKey_NamedKey_END is the supported route.
                Indent();
                Text("Keys down:");
                for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
                {
                    if (!IsKeyDown(key))
                        continue;
                    SameLine();
                    if (IsNamedKey(key))
                        Text("\"%s\"", GetKeyName(key));
                    else
                        Text("\"%s\" %d", GetKeyName(key), (int)key);
                    SameLine();
                    Text("(%.02f)", GetKeyData(key).DownDuration);
                }
                Text("Keys pressed:");
                for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
                {
                    if (!IsKeyPressed(key))
                        continue;
                    SameLine();
                    if (IsNamedKey(key))
                        Text("\"%s\"", GetKeyName(key));
                    else
                        Text("\"%s\" %d", GetKeyName(key), (int)key);
                }
                Text("Keys released:");
                for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
                {
                    if (!IsKeyReleased(key))
                        continue;
                    SameLine();
                    if (IsNamedKey(key))
                        Text("\"%s\"", GetKeyName(key));
                    else
                        Text("\"%s\" %d", GetKeyName(key), (int)key);
                }
                Text("Keys mods: %s%s%s%s", io.KeyCtrl ? "Ctrl " : string.Empty, io.KeyShift ? "Shift " : string.Empty, io.KeyAlt ? "Alt " : string.Empty, io.KeySuper ? "Super " : string.Empty);
                Text("Chars queue:");
                for (int i = 0; i < io.InputQueueCharacters.Count; i++)
                {
                    uint c = io.InputQueueCharacters[i];
                    SameLine();
                    Text("\'%c\' (0x%04X)", c > ' ' && c <= 255 ? (char)c : '?', c);
                }
                DebugRenderKeyboardPreview(GetWindowDrawList());
                Unindent();
            }

            Text("MOUSE STATE");
            {
                Indent();
                // The canonical formatter has no %g conversion; fixed point is used for the two %g call
                // sites in this section.
                if (IsMousePosValid())
                    Text("Mouse pos: (%.4f, %.4f)", io.MousePos.X, io.MousePos.Y);
                else
                    Text("Mouse pos: <INVALID>");
                Text("Mouse delta: (%.4f, %.4f)", io.MouseDelta.X, io.MouseDelta.Y);
                int count = io.MouseDown.Length;
                Text("Mouse down:");
                for (int i = 0; i < count; i++)
                {
                    if (!IsMouseDown((ImGuiMouseButton)i))
                        continue;
                    SameLine();
                    Text("b%d (%.02f secs)", i, io.MouseDownDuration[i]);
                }
                Text("Mouse clicked:");
                for (int i = 0; i < count; i++)
                {
                    if (!IsMouseClicked((ImGuiMouseButton)i))
                        continue;
                    SameLine();
                    Text("b%d (%d)", i, io.MouseClickedCount[i]);
                }
                Text("Mouse released:");
                for (int i = 0; i < count; i++)
                {
                    if (!IsMouseReleased((ImGuiMouseButton)i))
                        continue;
                    SameLine();
                    Text("b%d", i);
                }
                Text("Mouse wheel: %.1f", io.MouseWheel);
                Text("MouseStationaryTimer: %.2f", g.MouseStationaryTimer);
                Text("Mouse source: %s", GetMouseSourceName(io.MouseSource));
                Text("Pen Pressure: %.1f", io.PenPressure); // Currently unused by the engine.
                Unindent();
            }

            Text("MOUSE WHEELING");
            {
                Indent();
                Text("WheelingWindow: '%s'", g.WheelingWindow != null ? g.WheelingWindow.Name : "NULL");
                Text("WheelingWindowReleaseTimer: %.2f", g.WheelingWindowReleaseTimer);
                Text(
                    "WheelingAxisAvg[] = { %.3f, %.3f }, Main Axis: %s",
                    g.WheelingAxisAvg.X,
                    g.WheelingAxisAvg.Y,
                    g.WheelingAxisAvg.X > g.WheelingAxisAvg.Y ? "X" : (g.WheelingAxisAvg.X < g.WheelingAxisAvg.Y ? "Y" : "<none>"));
                Unindent();
            }

            Text("KEY OWNERS");
            {
                Indent();
                if (BeginChild(
                        "##owners",
                        new ImVec2(NegativeFloatMin, GetTextLineHeightWithSpacing() * 8.0f),
                        ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY,
                        ImGuiWindowFlags.NoSavedSettings))
                {
                    for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
                    {
                        ImGuiKeyOwnerData ownerData = GetKeyOwnerData(key);
                        if (ownerData.OwnerCurr == ImGuiKeyOwner_NoOwner)
                            continue;
                        Text(
                            "%s: 0x%08X%s",
                            GetKeyName(key),
                            ownerData.OwnerCurr,
                            ownerData.LockUntilRelease ? " LockUntilRelease" : ownerData.LockThisFrame ? " LockThisFrame" : string.Empty);
                        DebugLocateItemOnHover(ownerData.OwnerCurr);
                    }
                }
                EndChild();
                Unindent();
            }
            Text("SHORTCUT ROUTING");
            SameLine();
            MetricsHelpMarker("Declared shortcut routes automatically set key owner when mods matches.");
            {
                Indent();
                if (BeginChild(
                        "##routes",
                        new ImVec2(NegativeFloatMin, GetTextLineHeightWithSpacing() * 8.0f),
                        ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY,
                        ImGuiWindowFlags.NoSavedSettings))
                {
                    for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
                    {
                        ImGuiKeyRoutingTable rt = g.KeysRoutingTable;
                        short idx = rt.Index[(int)key - (int)ImGuiKey.NamedKey_BEGIN];
                        while (idx != -1)
                        {
                            ImGuiKeyRoutingData routingData = rt.Entries[idx];
                            ImGuiKey keyChord = key | (ImGuiKey)routingData.Mods;
                            Text("%s: 0x%08X (scored %d)", GetKeyChordName(keyChord), routingData.RoutingCurr, routingData.RoutingCurrScore);
                            DebugLocateItemOnHover(routingData.RoutingCurr);
                            if (g.IO.ConfigDebugIsDebuggerPresent)
                            {
                                SameLine();
                                if (DebugBreakButton("**DebugBreak**", "in SetShortcutRouting() for this KeyChord"))
                                    g.DebugBreakInShortcutRouting = keyChord;
                            }
                            idx = routingData.NextEntryIndex;
                        }
                    }
                }
                EndChild();
                Text("(ActiveIdUsing: AllKeyboardKeys: %d, NavDirMask: 0x%X)", g.ActiveIdUsingAllKeyboardKeys, g.ActiveIdUsingNavDirMask);
                Unindent();
            }
            TreePop();
        }

        if (TreeNode("Internal state"))
        {
            Text("WINDOWING");
            Indent();
            Text("HoveredWindow: '%s'", g.HoveredWindow != null ? g.HoveredWindow.Name : "NULL");
            Text("HoveredWindow->Root: '%s'", g.HoveredWindow != null ? (g.HoveredWindow.RootWindowDockTree?.Name ?? "NULL") : "NULL");
            Text("HoveredWindowUnderMovingWindow: '%s'", g.HoveredWindowUnderMovingWindow != null ? g.HoveredWindowUnderMovingWindow.Name : "NULL");
            Text("HoveredDockNode: 0x%08X", g.DebugHoveredDockNode != null ? g.DebugHoveredDockNode.ID : 0);
            Text("MovingWindow: '%s'", g.MovingWindow != null ? g.MovingWindow.Name : "NULL");
            Text(
                "MouseViewport: 0x%08X (UserHovered 0x%08X, LastHovered 0x%08X)",
                g.MouseViewport != null ? g.MouseViewport.ID : 0,
                g.IO.MouseHoveredViewport,
                g.MouseLastHoveredViewport != null ? g.MouseLastHoveredViewport.ID : 0);
            Unindent();

            Text("ITEMS");
            Indent();
            Text(
                "ActiveId: 0x%08X/0x%08X (%.2f sec), AllowOverlap: %d, Source: %s",
                g.ActiveId,
                g.ActiveIdPreviousFrame,
                g.ActiveIdTimer,
                g.ActiveIdAllowOverlap,
                GetInputSourceName(g.ActiveIdSource));
            DebugLocateItemOnHover(g.ActiveId);
            Text("ActiveIdWindow: '%s'", g.ActiveIdWindow != null ? g.ActiveIdWindow.Name : "NULL");
            Text("ActiveIdUsing: AllKeyboardKeys: %d, NavDirMask: %X", g.ActiveIdUsingAllKeyboardKeys, g.ActiveIdUsingNavDirMask);
            // g.HoveredId is not displayed because it is updated mid-frame.
            Text("HoveredId: 0x%08X (%.2f sec), AllowOverlap: %d", g.HoveredIdPreviousFrame, g.HoveredIdTimer, g.HoveredIdAllowOverlap);
            Text("HoverItemDelayId: 0x%08X, Timer: %.2f, ClearTimer: %.2f", g.HoverItemDelayId, g.HoverItemDelayTimer, g.HoverItemDelayClearTimer);
            Text(
                "DragDrop: %d, SourceId = 0x%08X, Payload \"%s\" (%d bytes)",
                g.DragDropActive,
                g.DragDropPayload.SourceId,
                g.DragDropPayload.DataType,
                g.DragDropPayload.DataSize);
            DebugLocateItemOnHover(g.DragDropPayload.SourceId);
            Unindent();

            Text("NAV,FOCUS");
            Indent();
            Text("NavWindow: '%s'", g.NavWindow != null ? g.NavWindow.Name : "NULL");
            Text("NavId: 0x%08X, NavLayer: %d", g.NavId, (int)g.NavLayer);
            DebugLocateItemOnHover(g.NavId);
            Text("NavInputSource: %s", GetInputSourceName(g.NavInputSource));
            Text("NavLastValidSelectionUserData = %d (0x%X)", g.NavLastValidSelectionUserData, g.NavLastValidSelectionUserData);
            Text("NavActive: %d, NavVisible: %d", g.IO.NavActive, g.IO.NavVisible);
            Text("NavActivateId/DownId/PressedId: %08X/%08X/%08X", g.NavActivateId, g.NavActivateDownId, g.NavActivatePressedId);
            Text("NavActivateFlags: %04X", (int)g.NavActivateFlags);
            Text("NavCursorVisible: %d, NavHighlightItemUnderNav: %d", g.NavCursorVisible, g.NavHighlightItemUnderNav);
            Text("NavFocusScopeId = 0x%08X", g.NavFocusScopeId);
            Text("NavFocusRoute[] = ");
            for (int pathN = g.NavFocusRoute.Count - 1; pathN >= 0; pathN--)
            {
                ImGuiFocusScopeData focusScope = g.NavFocusRoute[pathN];
                SameLine(0.0f, 0.0f);
                Text("0x%08X/", focusScope.ID);
                SetItemTooltip("In window \"%s\"", FindWindowByID(focusScope.WindowID)?.Name ?? "NULL");
            }
            Text("NavWindowingTarget: '%s'", g.NavWindowingTarget != null ? g.NavWindowingTarget.Name : "NULL");
            Unindent();

            TreePop();
        }
        // Overlay: Display windows Rectangles and Begin Order
        if (cfg.ShowWindowsRects || cfg.ShowWindowsBeginOrder)
        {
            foreach (ImGuiWindow window in g.Windows)
            {
                if (!window.WasActive)
                    continue;
                ImDrawList drawList = GetForegroundDrawList(window);
                if (cfg.ShowWindowsRects)
                {
                    ImRect r = GetWindowRect(window, cfg.ShowWindowsRectsType);
                    drawList.AddRect(r.Min, r.Max, 0xFF8000FF); // Pink: IM_COL32(255, 0, 128, 255).
                }
                if (cfg.ShowWindowsBeginOrder && (window.Flags & ImGuiWindowFlags.ChildWindow) == 0)
                {
                    string buf = ImFormatString("%d", window.BeginOrderWithinContext);
                    float fontSize = GetFontSize();
                    drawList.AddRectFilled(window.Pos, window.Pos + new ImVec2(fontSize, fontSize), 0xFF6464C8); // IM_COL32(200, 100, 100, 255).
                    drawList.AddText(window.Pos, 0xFFFFFFFF, buf);
                }
            }
        }

        // Overlay: Display Tables Rectangles
        if (cfg.ShowTablesRects)
        {
            for (int tableN = 0; tableN < g.Tables.Count; tableN++)
            {
                ImGuiTable? table = tableN < g.Tables.Count ? g.Tables[tableN] : null;
                if (table == null || table.LastFrameActive < g.FrameCount - 1)
                    continue;
                ImDrawList drawList = GetForegroundDrawList(table.OuterWindow!);
                if (cfg.ShowTablesRectsType >= TRT_ColumnsRect)
                {
                    for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                    {
                        ImRect r = GetTableRect(table, cfg.ShowTablesRectsType, columnN);
                        uint col = table.HoveredColumnBody == columnN ? 0xFF80FFFF : 0xFF8000FF; // Yellow when hovered, otherwise pink.
                        float thickness = table.HoveredColumnBody == columnN ? 3.0f : 1.0f;
                        drawList.AddRect(r.Min, r.Max, col, 0.0f, thickness);
                    }
                }
                else
                {
                    ImRect r = GetTableRect(table, cfg.ShowTablesRectsType, -1);
                    drawList.AddRect(r.Min, r.Max, 0xFF8000FF); // Pink.
                }
            }
        }

        // Overlay: Display Docking info
        if (cfg.ShowDockingNodes && g.IO.KeyCtrl && g.DebugHoveredDockNode != null)
        {
            ImGuiDockNode node = g.DebugHoveredDockNode;
            ImDrawList overlayDrawList = node.HostWindow != null
                ? GetForegroundDrawList(node.HostWindow)
                : GetForegroundDrawList(GetMainViewport());
            string buf =
                ImFormatString("DockId: %X%s\n", node.ID, node.IsCentralNode() ? " *CentralNode*" : string.Empty)
                + ImFormatString("WindowClass: %08X\n", node.WindowClass.ClassId)
                + ImFormatString("Size: (%.0f, %.0f)\n", node.Size.X, node.Size.Y)
                + ImFormatString("SizeRef: (%.0f, %.0f)\n", node.SizeRef.X, node.SizeRef.Y);
            int depth = DockNodeGetDepth(node);
            overlayDrawList.AddRect(
                node.Pos + new ImVec2(3, 3) * depth,
                node.Pos + node.Size - new ImVec2(3, 3) * depth,
                0xFF6464C8); // IM_COL32(200, 100, 100, 255).
            ImVec2 pos = node.Pos + new ImVec2(3, 3) * depth;
            overlayDrawList.AddRectFilled(pos - new ImVec2(1, 1), pos + CalcTextSize(buf) + new ImVec2(1, 1), 0xFF6464C8);
            overlayDrawList.AddText(pos, 0xFFFFFFFF, buf);
        }

        End();
    }

    #endregion

    #region SECTION: ITEM-PATH QUERY (imgui.cpp:24327-24433)

    /// <summary>
    /// Advances the item-path query by one step and returns the identifier the caller should register as
    /// the hook, or zero when the hook should be cleared.
    /// (<c>ThirdParty/imgui/imgui.cpp:24327-24361</c>)
    /// </summary>
    /// <param name="query">The query state to advance.</param>
    /// <param name="id">The identifier being queried, or zero to idle the hook.</param>
    /// <returns>The identifier to install as the hook, or zero when no hook is needed.</returns>
    /// <remarks>
    /// <para>
    /// Only one identifier can be queried per frame, which is what keeps the <c>GetID</c> family cheap: the
    /// hook test is a single comparison, and the expensive walk happens one stack level per frame.
    /// </para>
    /// <para>
    /// A level advances when it reports success or after it has been asked for more than two frames, so a
    /// level that never answers cannot stall the walk forever. Changing the queried identifier restarts the
    /// walk from scratch.
    /// </para>
    /// </remarks>
    private static uint DebugItemPathQuery_UpdateAndGetHookId(ImGuiDebugItemPathQuery query, uint id)
    {
        if (query.MainID != id)
        {
            query.MainID = id;
            query.Step = -1;
            query.Complete = false;
            query.Results.Clear();
            query.ResultsDescBuf.Clear();
        }
        query.Active = false;
        if (id == 0)
            return 0;

        if (query.Step >= 0 && query.Step < query.Results.Count)
        {
            ImGuiStackLevelInfo current = query.Results[query.Step];
            if (current.QuerySuccess || current.QueryFrameCount > 2)
                query.Step++;
        }

        query.Complete = query.Step == query.Results.Count;
        if (query.Step == -1)
        {
            query.Active = true;
            return id;
        }
        if (query.Step >= 0 && query.Step < query.Results.Count)
        {
            ImGuiStackLevelInfo current = query.Results[query.Step];
            current.QueryFrameCount++;
            query.Active = true;
            return current.ID;
        }
        return 0;
    }

    /// <summary>
    /// Publishes the identifier the item-path query wants hooked for this frame.
    /// (<c>ThirdParty/imgui/imgui.cpp:24364-24371</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4179</c>)
    /// </summary>
    /// <remarks>
    /// The query only considers an identifier while the identifier-stack tool has been active on the previous
    /// frame, which is how the tool avoids paying for the walk when it is closed. The hovered identifier from
    /// the previous frame is preferred over the active one so that merely hovering an item is enough to
    /// inspect it.
    /// </remarks>
    internal static void UpdateDebugToolItemPathQuery()
    {
        ImGuiContext g = GetContext();
        uint id = 0;
        if (g.DebugIDStackTool.LastActiveFrame + 1 == g.FrameCount)
            id = g.HoveredIdPreviousFrame != 0 ? g.HoveredIdPreviousFrame : g.ActiveId;
        g.DebugHookIdInfoId = DebugItemPathQuery_UpdateAndGetHookId(g.DebugItemPathQuery, id);
    }

    /// <summary>
    /// Records how one identifier was produced, so the identifier-stack tool can explain its provenance.
    /// (<c>ThirdParty/imgui/imgui.cpp:24374-24432</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4151</c>)
    /// </summary>
    /// <param name="id">The identifier that was just computed.</param>
    /// <param name="dataType">
    /// How the identifier was produced. Canonical passes a mix of public <see cref="ImGuiDataType"/> values
    /// (the integer case) and private <see cref="ImGuiDataTypePrivate"/> values (pointer and override) through
    /// one parameter; the managed port therefore takes the raw discriminator, and each caller passes the
    /// matching constant.
    /// </param>
    /// <param name="dataId">The payload used for the integer and pointer cases.</param>
    /// <param name="dataIdEnd">The end of the payload, which only the string case used in canonical.</param>
    /// <param name="dataString">
    /// The label for the string case. Canonical passes a character range; the managed port passes the label
    /// as a string, which removes the range arithmetic and cannot over-read.
    /// </param>
    /// <remarks>
    /// The first step records the whole identifier stack plus the final identifier, because that is the path a
    /// maintainer needs. Later steps describe one level each, and a level is only described once: the offset
    /// into the description buffer is recorded so a repeated frame reuses the existing text. The override case
    /// is deliberately attributed to the first of its two hook calls, matching canonical, because
    /// <c>PushOverrideID</c> is commonly used precisely to avoid hashing the label twice.
    /// </remarks>
    internal static void DebugHookIdInfo(
        uint id,
        int dataType,
        IntPtr dataId,
        IntPtr dataIdEnd,
        string? dataString = null)
    {
        _ = dataIdEnd;
        ImGuiContext g = GetContext();
        ImGuiDebugItemPathQuery query = g.DebugItemPathQuery;
        if (!query.Active)
        {
            ImAssert.Check(id == 0, "The identifier hook must only fire while a query is active.");
            return;
        }

        ImGuiWindow window = g.CurrentWindow!;

        // The identifier is assumed to have been computed with the current identifier stack, which holds for
        // the engines' own widgets and is what makes the reconstructed path meaningful.
        if (query.Step == -1)
        {
            ImAssert.Check(query.Results.Count == 0);
            query.Step++;
            for (int n = 0; n < window.IDStack.Count + 1; n++)
            {
                query.Results.Add(new ImGuiStackLevelInfo
                {
                    ID = n < window.IDStack.Count ? window.IDStack[n] : id,
                });
            }
            return;
        }

        ImAssert.Check(query.Step >= 0);
        if (query.Step != window.IDStack.Count)
            return;

        ImGuiStackLevelInfo info = query.Results[query.Step];
        ImAssert.Check(info.ID == id && info.QueryFrameCount > 0);

        if (info.DescOffset == -1)
        {
            string result;
            if (dataType == (int)ImGuiDataType.S32)
                result = ((int)dataId).ToString(CultureInfo.InvariantCulture);
            else if (dataType == (int)ImGuiDataType.String)
                result = dataString ?? string.Empty;
            else if (dataType == (int)ImGuiDataTypePrivate.ImGuiDataType_Pointer)
                result = $"(void*)0x{dataId.ToInt64():X}";
            else if (dataType == (int)ImGuiDataTypePrivate.ImGuiDataType_ID)
                result = $"0x{id:X8} [override]";
            else
            {
                ImAssert.Check(false, "Unsupported identifier payload type.");
                result = string.Empty;
            }

            info.DescOffset = query.ResultsDescBuf.Size;
            // The canonical buffer stores a zero-terminated description; the managed reader stops at the next
            // recorded offset, so the terminator is kept for byte-for-byte compatibility of the buffer.
            query.ResultsDescBuf.Append(result);
            query.ResultsDescBuf.Append("\0");
        }
        info.QuerySuccess = true;
        if (info.DataType == -1)
            info.DataType = (sbyte)dataType;
    }

    /// <summary>
    /// Returns the part of a label that contributes to its hash, dropping every <c>###</c> prefix.
    /// (<c>ThirdParty/imgui/imgui.cpp:2614-2621</c>, declared <c>imgui_internal.h:387</c>)
    /// </summary>
    /// <param name="label">The label to trim.</param>
    /// <returns>The label from just past the last <c>###</c> marker, or the whole label when there is none.</returns>
    /// <remarks>
    /// The canonical scan advances its cursor before examining the next two characters, so the marker it looks
    /// for is three consecutive hashes and the result starts one character past the last of them. The managed
    /// port reproduces that scan exactly.
    /// </remarks>
    private static string HashSkipUncontributingPrefix(string label)
    {
        int result = 0;
        for (int i = 0; i + 2 < label.Length; i++)
        {
            if (label[i] == '#' && label[i + 1] == '#' && label[i + 2] == '#')
                result = i + 3;
        }
        return label[result..];
    }

    /// <summary>
    /// Reads one cached level description out of the query's description buffer.
    /// </summary>
    /// <param name="query">The query holding the buffer.</param>
    /// <param name="descOffset">The recorded offset of the description, or -1 when there is none.</param>
    /// <returns>The description, or the empty string when no description was recorded.</returns>
    /// <remarks>
    /// Canonical stores a zero-terminated description and indexes the buffer directly. This port's buffer is
    /// byte-oriented, so the description runs from the recorded offset to the next terminator; the terminator
    /// is retained when writing so a caller reading the buffer the canonical way still stops in the same place.
    /// </remarks>
    private static string ReadLevelDescription(ImGuiDebugItemPathQuery query, int descOffset)
    {
        if (descOffset < 0)
            return string.Empty;
        string buf = query.ResultsDescBuf.CStr();
        if (descOffset > buf.Length)
            return string.Empty;
        int end = buf.IndexOf('\0', descOffset);
        if (end < 0)
            end = buf.Length;
        return buf[descOffset..end];
    }

    /// <summary>
    /// Describes one level of an item path, either for display or for the copyable path string.
    /// (<c>ThirdParty/imgui/imgui.cpp:24434-24449</c>)
    /// </summary>
    /// <param name="query">The query holding the level.</param>
    /// <param name="n">The level index.</param>
    /// <param name="formatForUi">Whether the description is decorated for display.</param>
    /// <returns>The level description, which may be empty while the query is still running.</returns>
    /// <remarks>
    /// <para>
    /// The root level of a window is not produced by a <c>GetID</c> call and therefore is never hooked, so it
    /// is resolved from the window identifier instead. A hooked level takes priority over any fallback because
    /// patterns such as <c>PushID(str)</c> followed by an empty label legitimately share an identifier.
    /// </para>
    /// <para>
    /// While the query is still running an undescribed level reports nothing rather than a placeholder, which
    /// stops the display from flickering through <c>???</c> markers and then resolving them.
    /// </para>
    /// <para>
    /// Canonical adds a test-engine label lookup here; this port has no test-engine layer, so the final
    /// fallback is reached directly.
    /// </para>
    /// </remarks>
    private static string DebugItemPathQuery_FormatLevelInfo(ImGuiDebugItemPathQuery query, int n, bool formatForUi)
    {
        ImGuiStackLevelInfo info = query.Results[n];
        ImGuiWindow? window = info.DescOffset == -1 && n == 0 ? FindWindowByID(info.ID) : null;
        if (window != null)
        {
            // Source: the window name, because the root identifier is never produced by GetID().
            string windowName = HashSkipUncontributingPrefix(window.Name);
            return formatForUi ? $"\"{windowName}\" [window]" : windowName;
        }
        if (info.QuerySuccess)
        {
            // Source: the GetID() hook.
            string desc = HashSkipUncontributingPrefix(ReadLevelDescription(query, info.DescOffset));
            return formatForUi && info.DataType == (sbyte)ImGuiDataType.String ? $"\"{desc}\"" : desc;
        }
        if (query.Step < query.Results.Count)
            return string.Empty; // Still querying: do not show a placeholder yet.
        return "???";
    }

    /// <summary>
    /// Renders a completed item path as a single copyable string, escaping separators and optionally
    /// hex-encoding non-ASCII characters.
    /// (<c>ThirdParty/imgui/imgui.cpp:24451-24474</c>)
    /// </summary>
    /// <param name="query">The query holding the results.</param>
    /// <param name="hexEncodeNonAsciiChars">Whether non-ASCII characters are written as <c>\xNN</c> escapes.</param>
    /// <returns>The path string, also stored in the query's result-path buffer.</returns>
    /// <remarks>
    /// Levels are joined with a doubled leading separator and single separators after it, so the first level
    /// reads as an absolute root. A separator appearing inside a level name is escaped, because it would
    /// otherwise be indistinguishable from a level boundary when the path is pasted elsewhere.
    /// </remarks>
    private static string DebugItemPathQuery_GetResultAsPath(ImGuiDebugItemPathQuery query, bool hexEncodeNonAsciiChars)
    {
        query.ResultPathBuf.Clear();
        for (int stackN = 0; stackN < query.Results.Count; stackN++)
        {
            string levelDesc = DebugItemPathQuery_FormatLevelInfo(query, stackN, false);
            query.ResultPathBuf.Append(stackN == 0 ? "//" : "/");
            // The port's text buffers are byte-per-character, so each character of the description is one
            // codepoint and is examined directly rather than decoded from a UTF-8 run.
            foreach (char c in levelDesc)
            {
                if (c == '/')
                    query.ResultPathBuf.Append("\\");
                if (c < 256 || !hexEncodeNonAsciiChars)
                    query.ResultPathBuf.Append(c.ToString());
                else
                    query.ResultPathBuf.AppendF("\\x{0:x2}", (byte)c);
            }
        }
        return query.ResultPathBuf.CStr();
    }

    #endregion

    #region SECTION: ID STACK TOOL WINDOW (imgui.cpp:24476-24544)

    /// <summary>
    /// Submits the identifier-stack tool window, which explains how the identifier of the hovered item was
    /// assembled from the pushes on the current identifier stack.
    /// (<c>ThirdParty/imgui/imgui.cpp:24476-24544</c>, declared <c>ThirdParty/imgui/imgui.h:417</c>)
    /// </summary>
    /// <param name="pOpen">Whether the window is open; cleared when the user closes it.</param>
    /// <remarks>
    /// <para>
    /// The window is the driving end of the item-path query: it records the frame it was last active on so
    /// <see cref="UpdateDebugToolItemPathQuery"/> knows to keep querying, and it displays whatever the query
    /// has resolved so far. The display therefore fills in progressively over the frames after the mouse
    /// first hovers an item, one stack level per frame.
    /// </para>
    /// <para>
    /// The canonical ordering is preserved exactly: the result path is computed before the checkboxes are
    /// submitted, so toggling "Hex-encode non-ASCII" changes the path on the following frame. The copy
    /// confirmation flashes for 0.75 seconds in quarter-second pulses, which is why the color test wraps
    /// the elapsed time with a remainder.
    /// </para>
    /// </remarks>
    public static void ShowIDStackToolWindow(ref bool pOpen)
    {
        ImGuiContext g = GetContext();
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSize) == 0)
            SetNextWindowSize(new ImVec2(0.0f, GetFontSize() * 8.0f), ImGuiCond.FirstUseEver);
        if (!Begin("Dear ImGui ID Stack Tool", ref pOpen) || GetCurrentWindow().BeginCount > 1)
        {
            End();
            return;
        }

        ImGuiDebugItemPathQuery query = g.DebugItemPathQuery;
        ImGuiIDStackTool tool = g.DebugIDStackTool;
        tool.LastActiveFrame = g.FrameCount;
        string resultPath = DebugItemPathQuery_GetResultAsPath(query, tool.OptHexEncodeNonAsciiChars);
        Text("0x%08X", query.MainID);
        SameLine();
        MetricsHelpMarker(
            "Hover an item with the mouse to display elements of the ID Stack leading to the item's final ID.\n" +
            "Each level of the stack correspond to a PushID() call.\n" +
            "All levels of the stack are hashed together to make the final ID of a widget (ID displayed at the bottom level of the stack).\n" +
            "Read FAQ entry about the ID stack for details.");

        // Ctrl+C to copy path
        float timeSinceCopy = (float)g.Time - tool.CopyToClipboardLastTime;
        PushStyleVarY(ImGuiStyleVar.FramePadding, 0.0f);
        bool hexEncodeNonAsciiChars = tool.OptHexEncodeNonAsciiChars;
        Checkbox("Hex-encode non-ASCII", ref hexEncodeNonAsciiChars);
        tool.OptHexEncodeNonAsciiChars = hexEncodeNonAsciiChars;
        SameLine();
        bool copyToClipboardOnCtrlC = tool.OptCopyToClipboardOnCtrlC;
        Checkbox("Ctrl+C: copy path", ref copyToClipboardOnCtrlC);
        tool.OptCopyToClipboardOnCtrlC = copyToClipboardOnCtrlC;
        PopStyleVar();
        SameLine();
        TextColored(
            timeSinceCopy >= 0.0f && timeSinceCopy < 0.75f && timeSinceCopy % 0.25f < 0.25f * 0.5f
                ? new ImVec4(1.0f, 1.0f, 0.3f, 1.0f)
                : new ImVec4(),
            "*COPIED*");
        if (tool.OptCopyToClipboardOnCtrlC && Shortcut(
                ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.C,
                ImGuiInputFlags.RouteGlobal | ImGuiInputFlags.RouteOverFocused))
        {
            tool.CopyToClipboardLastTime = (float)g.Time;
            SetClipboardText(resultPath);
        }

        Text("- Path \"%s\"", query.Complete ? resultPath : string.Empty);
        Separator();

        // Display decorated stack
        if (query.Results.Count > 0 && BeginTable("##table", 3, ImGuiTableFlags.Borders))
        {
            float idWidth = CalcTextSize("0xDDDDDDDD").X;
            TableSetupColumn("Seed", ImGuiTableColumnFlags.WidthFixed, idWidth);
            TableSetupColumn("PushID", ImGuiTableColumnFlags.WidthStretch);
            TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, idWidth);
            TableHeadersRow();
            for (int n = 0; n < query.Results.Count; n++)
            {
                ImGuiStackLevelInfo info = query.Results[n];
                TableNextColumn();
                Text("0x%08X", n > 0 ? query.Results[n - 1].ID : 0);
                TableNextColumn();
                TextUnformatted(DebugItemPathQuery_FormatLevelInfo(query, n, true));
                TableNextColumn();
                Text("0x%08X", info.ID);
                if (n == query.Results.Count - 1)
                    TableSetBgColor(ImGuiTableBgTarget.CellBg, GetColorU32(ImGuiCol.Header));
            }
            EndTable();
        }
        End();
    }

    #endregion

    #region SECTION: FONT ATLAS SHOWCASE (imgui.cpp:22392-22580, 23560-23810, 24569-24597)

    // Divergences from canonical, all forced by the port's single-atlas font system:
    // - The port has no ImFontAtlasBuilder. The "Rects Index" tree node, the packed and discarded
    //   rectangle statistics, and the highlight rectangle they feed to DebugNodeTexture are omitted;
    //   the atlas rectangle counters are private and no public API is added to read them.
    // - The port has no ImFontLoader abstraction and no Loader tree node. The stb_truetype and FreeType
    //   radio buttons, their tooltips, and the shared FreeType loader flags are omitted, and the
    //   per-input "Loader" line is omitted.
    // - The port has no TexList. The single atlas texture (atlas.TexData) is shown as texture zero.
    // - The port has no CompactCache, ImFontAtlasTextureGrow, or ImFontAtlasBuildClear equivalent, so
    //   the Compact, Grow, and Clear All buttons are omitted.
    // - The port has no ImFontAtlasBuildGetOversampleFactors, so the per-baked-input line reports the
    //   source's own oversampling values only and omits the computed factors.
    // - The port has no FontLoaderName and no ImFontAtlasFontDestroyOutput/InitOutput pair, so the
    //   loader-name display and the FreeType flag editor are omitted.

    /// <summary>
    /// The largest Unicode codepoint the port represents, matching canonical
    /// <c>IM_UNICODE_CODEPOINT_MAX</c> (<c>ThirdParty/imgui/imgui.h:3623</c>).
    /// </summary>
    /// <remarks>
    /// The constant is repeated here because the equivalent member on <see cref="ImFont"/> is private.
    /// Both values describe the same inclusive range, so the glyph pages shown by the font debug nodes
    /// cover exactly the codepoints canonical enumerates.
    /// </remarks>
    private const uint UnicodeCodepointMax = 0x10FFFF;

    /// <summary>
    /// Returns the display name of a texture upload status.
    /// (<c>ThirdParty/imgui/imgui_draw.cpp:2476-2487</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4376</c>)
    /// </summary>
    /// <param name="status">The texture status to name.</param>
    /// <returns>The canonical name, or <c>"N/A"</c> when the value is outside the enumeration.</returns>
    /// <remarks>
    /// Canonical owns this helper next to the texture-data utilities; the port declares it in the font
    /// atlas showcase because that showcase is its only caller. The canonical spelling is preserved so
    /// the debug display reads the same in both implementations.
    /// </remarks>
    public static string ImTextureDataGetStatusName(ImTextureStatus status)
    {
        switch (status)
        {
            case ImTextureStatus.OK:
                return "OK";
            case ImTextureStatus.Destroyed:
                return "Destroyed";
            case ImTextureStatus.WantCreate:
                return "WantCreate";
            case ImTextureStatus.WantUpdates:
                return "WantUpdates";
            case ImTextureStatus.WantDestroy:
                return "WantDestroy";
        }
        return "N/A";
    }

    /// <summary>
    /// Returns the display name of a texture pixel format.
    /// (<c>ThirdParty/imgui/imgui_draw.cpp:2489-2497</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4377</c>)
    /// </summary>
    /// <param name="format">The texture format to name.</param>
    /// <returns>The canonical name, or <c>"N/A"</c> when the value is outside the enumeration.</returns>
    /// <remarks>
    /// Canonical owns this helper next to the texture-data utilities; the port declares it in the font
    /// atlas showcase because that showcase is its only caller. The canonical spelling is preserved so
    /// the debug display reads the same in both implementations.
    /// </remarks>
    public static string ImTextureDataGetFormatName(ImTextureFormat format)
    {
        switch (format)
        {
            case ImTextureFormat.Alpha8:
                return "Alpha8";
            case ImTextureFormat.RGBA32:
                return "RGBA32";
        }
        return "N/A";
    }

    /// <summary>
    /// Encodes one codepoint as a UTF-8 string for the font debug nodes.
    /// (<c>ThirdParty/imgui/imgui.cpp:23643-23647</c>)
    /// </summary>
    /// <param name="codepoint">The codepoint to encode.</param>
    /// <returns>The UTF-8 text for the codepoint, or <c>"?"</c> when it lies outside the Unicode range.</returns>
    /// <remarks>
    /// Canonical keeps a five-byte scratch buffer and prints it directly through <c>%s</c>. The managed
    /// port has no zero-terminated byte buffer to pass to the text formatter, so the bytes produced by
    /// <see cref="ImTextCharToUtf8"/> are decoded into a managed string here instead.
    /// </remarks>
    private static string CodepointToUtf8String(uint codepoint)
    {
        Span<byte> utf8 = stackalloc byte[5];
        int length = ImTextCharToUtf8(utf8, codepoint);
        return System.Text.Encoding.UTF8.GetString(utf8[..length]);
    }

    /// <summary>
    /// Computes the mask of font inputs that contain a codepoint, or zero when only one input contains
    /// it. (<c>ThirdParty/imgui/imgui.cpp:23578-23591</c>)
    /// </summary>
    /// <param name="atlas">The atlas that owns the font.</param>
    /// <param name="font">The font whose inputs are tested.</param>
    /// <param name="codepoint">The codepoint to test.</param>
    /// <returns>A bit per input that contains the codepoint when more than one does; otherwise zero.</returns>
    /// <remarks>
    /// Canonical asks each input's loader through <c>FontSrcContainsGlyph</c>. The port has no loader
    /// abstraction, so containment is answered directly by each input's parsed TrueType face, the same
    /// way <see cref="ImFont.IsGlyphInFont"/> answers it. Glyph exclusion ranges are consequently not
    /// consulted, matching that method.
    /// </remarks>
    private static int CalcFontGlyphSrcOverlapMask(ImFontAtlas atlas, ImFont font, uint codepoint)
    {
        int mask = 0;
        int count = 0;
        for (int srcN = 0; srcN < font.Sources.Count; srcN++)
        {
            ImFontConfig src = font.Sources[srcN];
            atlas.EnsureStbFont(font, src);
            if (src.StbFont == null || src.StbFont.FindGlyphIndex((int)codepoint) == 0)
                continue;
            mask |= 1 << srcN;
            count++;
        }
        return count > 1 ? mask : 0;
    }

    /// <summary>
    /// Submits the font atlas showcase: the font-selection controls, the font list with one
    /// <see cref="DebugNodeFont"/> per loaded font, and the atlas texture report.
    /// (<c>ThirdParty/imgui/imgui.cpp:22392-22543</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4137</c>)
    /// </summary>
    /// <param name="atlas">The atlas to report, or <see langword="null"/> to show only the controls that
    /// do not need font data.</param>
    /// <remarks>
    /// <para>
    /// The font-selection combo, the base-size and scale sliders, the help text, and the font list follow
    /// canonical order. The "Show font preview" checkbox drives the preview line inside
    /// <see cref="DebugNodeFont"/>.
    /// </para>
    /// <para>
    /// The Loader tree node, the Rects Index tree node, the builder rectangle statistics, and the
    /// Compact/Grow/Clear All buttons are omitted because the port has no font loader and no atlas
    /// builder; see the divergence notes at the top of this region. Because the Rects Index is omitted,
    /// the texture node receives no highlight rectangle. The single atlas texture is reported through
    /// <see cref="DebugNodeTexture"/> with identifier zero.
    /// </para>
    /// </remarks>
    public static void ShowFontAtlas(ImFontAtlas? atlas)
    {
        ImGuiContext g = GetContext();
        ImGuiIO io = g.IO;
        ImGuiStyle style = g.Style;

        BeginDisabled();
        uint backendFlags = (uint)io.BackendFlags;
        CheckboxFlags("io.BackendFlags: RendererHasTextures", ref backendFlags, (uint)ImGuiBackendFlags.RendererHasTextures);
        io.BackendFlags = (ImGuiBackendFlags)backendFlags;
        EndDisabled();
        ShowFontSelector("Font");

        float fontSizeBase = style.FontSizeBase;
        if (DragFloat("FontSizeBase", ref fontSizeBase, 0.20f, 5.0f, 100.0f, "%.0f"))
        {
            // Canonical writes the field and then queues the value for the next frame so the change is
            // visible immediately without disturbing the current frame's layout.
            style.FontSizeBase = fontSizeBase;
            style._NextFrameFontSizeBase = fontSizeBase;
        }
        SameLine(0.0f, 0.0f);
        Text(" (out %.2f)", GetFontSize());
        SameLine();
        MetricsHelpMarker("- This is scaling font only. General scaling will come later.");

        float fontScaleMain = style.FontScaleMain;
        if (DragFloat("FontScaleMain", ref fontScaleMain, 0.02f, 0.5f, 4.0f))
            style.FontScaleMain = fontScaleMain;

        float fontScaleDpi = style.FontScaleDpi;
        if (DragFloat("FontScaleDpi", ref fontScaleDpi, 0.02f, 0.5f, 4.0f))
            style.FontScaleDpi = fontScaleDpi;

        if ((io.BackendFlags & ImGuiBackendFlags.RendererHasTextures) == 0)
        {
            BulletText("Warning: Font scaling will NOT be smooth, because\nImGuiBackendFlags_RendererHasTextures is not set!");
            BulletText("For instructions, see:");
            SameLine();
            TextLinkOpenURL("docs/BACKENDS.md", "https://github.com/ocornut/imgui/blob/master/docs/BACKENDS.md");
        }
        BulletText("Load a nice font for better results!");
        BulletText("Please submit feedback:");
        SameLine();
        TextLinkOpenURL("#8465", "https://github.com/ocornut/imgui/issues/8465");
        BulletText("Read FAQ for more details:");
        SameLine();
        TextLinkOpenURL("dearimgui.com/faq", "https://www.dearimgui.com/faq/");

        SeparatorText("Font List");

        ImGuiMetricsConfig cfg = g.DebugMetricsConfig;
        bool showFontPreview = cfg.ShowFontPreview;
        Checkbox("Show font preview", ref showFontPreview);
        cfg.ShowFontPreview = showFontPreview;

        if (atlas != null)
        {
            for (int fontN = 0; fontN < atlas.Fonts.Count; fontN++)
            {
                ImFont font = atlas.Fonts[fontN];
                PushID(font);
                DebugNodeFont(font);
                PopID();
            }

            SeparatorText("Font Atlas");
            // The Compact, Grow, and Clear All buttons have no port equivalent and are omitted.

            if (atlas.TexData is ImTextureData texture)
                Text("Tex: %dx%d", texture.Width, texture.Height);
            // Canonical additionally reports the builder's packed and discarded rectangle statistics,
            // which the port does not expose; see the divergence notes at the top of this region.

            // Canonical shows every entry of the texture list. The port owns exactly one texture, so the
            // node is pre-opened once and receives identifier zero and no highlight rectangle.
            if (atlas.TexData is ImTextureData atlasTexture)
            {
                SetNextItemOpen(true, ImGuiCond.Once);
                DebugNodeTexture(atlasTexture, 0, null);
            }
        }
    }

    /// <summary>
    /// Submits one texture as a collapsible node showing its pixels, used rectangle, and lifecycle
    /// state. (<c>ThirdParty/imgui/imgui.cpp:22545-22576</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4144</c>)
    /// </summary>
    /// <param name="tex">The texture data to report.</param>
    /// <param name="intId">Integer identifier that scopes the node, standing in for the canonical
    /// pointer argument.</param>
    /// <param name="highlightRect">Rectangle to outline and cut out of a dimming overlay, or
    /// <see langword="null"/> when no rectangle is highlighted.</param>
    /// <remarks>
    /// <para>
    /// Canonical passes the texture by pointer; the managed port carries the reference directly, so the
    /// node never observes a null texture. A texture queued for destruction is represented by an empty
    /// placeholder of its own size rather than an image, because its pixels are gone.
    /// </para>
    /// <para>
    /// The "Show used rect" outline is magenta and the highlight cut-out is a semi-transparent black
    /// overlay with a yellow outline, matching canonical. The backend user data is rendered with
    /// <c>%s</c> rather than canonical's <c>%p</c> because the port's text formatter has no pointer
    /// conversion.
    /// </para>
    /// </remarks>
    public static void DebugNodeTexture(ImTextureData tex, int intId, ImFontAtlasRect? highlightRect)
    {
        ImGuiContext g = GetContext();
        PushID(intId);
        if (TreeNode("", "Texture #%03d (%dx%d pixels)", tex.UniqueID, tex.Width, tex.Height))
        {
            ImGuiMetricsConfig cfg = g.DebugMetricsConfig;
            bool showTextureUsedRect = cfg.ShowTextureUsedRect;
            Checkbox("Show used rect", ref showTextureUsedRect);
            cfg.ShowTextureUsedRect = showTextureUsedRect;

            PushStyleVar(ImGuiStyleVar.ImageBorderSize, MathF.Max(1.0f, g.Style.ImageBorderSize));
            ImVec2 p = GetCursorScreenPos();
            if (tex.WantDestroyNextFrame)
                Dummy(new ImVec2(tex.Width, tex.Height));
            else
                ImageWithBg(tex.GetTexRef(), new ImVec2(tex.Width, tex.Height), new ImVec2(0.0f, 0.0f), new ImVec2(1.0f, 1.0f), new ImVec4(0.0f, 0.0f, 0.0f, 1.0f));

            if (cfg.ShowTextureUsedRect)
            {
                GetWindowDrawList().AddRect(
                    new ImVec2(p.X + tex.UsedRect.X, p.Y + tex.UsedRect.Y),
                    new ImVec2(p.X + tex.UsedRect.X + tex.UsedRect.W, p.Y + tex.UsedRect.Y + tex.UsedRect.H),
                    0xFFFF00FFu); // Magenta: outline of the used rectangle.
            }
            if (highlightRect is ImFontAtlasRect highlight)
            {
                ImRect outer = new(p.X, p.Y, p.X + tex.Width, p.Y + tex.Height);
                ImRect inner = new(
                    p.X + highlight.X,
                    p.Y + highlight.Y,
                    p.X + highlight.X + highlight.W,
                    p.Y + highlight.Y + highlight.H);
                RenderRectFilledWithHole(GetWindowDrawList(), outer, inner, 0x64000000u, 0.0f);
                GetWindowDrawList().AddRect(inner.Min - new ImVec2(1.0f, 1.0f), inner.Max + new ImVec2(1.0f, 1.0f), 0xFF00FFFFu); // Yellow: highlighted rectangle.
            }
            PopStyleVar();

            Text(
                "Status = %s (%d), Format = %s (%d), UseColors = %d",
                ImTextureDataGetStatusName(tex.Status),
                (int)tex.Status,
                ImTextureDataGetFormatName(tex.Format),
                (int)tex.Format,
                tex.UseColors ? 1 : 0);
            Text(
                "TexRef = %s, BackendUserData = %s",
                FormatTextureRefForDebugDisplay(tex.GetTexRef()),
                tex.BackendUserData != null ? tex.BackendUserData.ToString() ?? "NULL" : "NULL");
            TreePop();
        }
        PopID();
    }

    /// <summary>
    /// Submits one loaded font as a collapsible node: its preview text, its atlas-mutating actions, its
    /// metrics, its inputs, the input-overlap detection tool, and one glyph page per baked size.
    /// (<c>ThirdParty/imgui/imgui.cpp:23594-23744</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4145</c>)
    /// </summary>
    /// <param name="font">The font to report.</param>
    /// <remarks>
    /// <para>
    /// A collapsed node still shows the preview line, which canonical renders with the node's own font
    /// at its natural size. The Remove, Clear bakes, and Clear unused actions require an owning atlas;
    /// a detached font shows the node but leaves those actions disabled.
    /// </para>
    /// <para>
    /// The per-input loader line and the FreeType loader-flag editor are omitted because the port has no
    /// font loader. The per-baked-input oversampling line reports the source's own oversampling values
    /// because the port has no <c>ImFontAtlasBuildGetOversampleFactors</c> equivalent. See the
    /// divergence notes at the top of this region.
    /// </para>
    /// </remarks>
    public static void DebugNodeFont(ImFont font)
    {
        ImGuiContext g = GetContext();
        ImGuiMetricsConfig cfg = g.DebugMetricsConfig;
        ImFontAtlas? atlas = font.OwnerAtlas;

        bool opened = TreeNode(font, "Font: \"%s\": %d sources(s)", font.GetDebugName(), font.Sources.Count);

        // Display preview text.
        if (!opened)
            Indent();
        Indent();
        if (cfg.ShowFontPreview)
        {
            PushFont(font, 0.0f);
            Text("The quick brown fox jumps over the lazy dog");
            PopFont();
        }
        if (!opened)
        {
            Unindent();
            Unindent();
            return;
        }

        if (SmallButton("Set as default"))
            GetIO().FontDefault = font;
        SameLine();
        BeginDisabled(atlas == null || atlas.Fonts.Count <= 1 || atlas.Locked);
        if (SmallButton("Remove"))
            atlas?.RemoveFont(font);
        EndDisabled();
        SameLine();
        if (SmallButton("Clear bakes"))
            atlas?.FontDiscardBakes(font, 0);
        SameLine();
        if (SmallButton("Clear unused"))
            atlas?.FontDiscardBakes(font, 2);

        // Display details.
        SetNextItemWidth(GetFontSize() * 8.0f);
        float fontScale = font.Scale;
        if (DragFloat("Font scale", ref fontScale, 0.005f, 0.3f, 2.0f, "%.1f"))
            font.Scale = fontScale;

        Text("Fallback character: '%s' (U+%04X)", CodepointToUtf8String(font.FallbackChar), font.FallbackChar);
        Text("Ellipsis character: '%s' (U+%04X)", CodepointToUtf8String(font.EllipsisChar), font.EllipsisChar);

        for (int srcN = 0; srcN < font.Sources.Count; srcN++)
        {
            ImFontConfig src = font.Sources[srcN];
            if (TreeNode(src, "Input %d: '%s' [%d], Oversample: %d,%d, PixelSnapH: %d, Offset: (%.1f,%.1f)",
                    srcN, src.Name, src.FontNo, src.OversampleH, src.OversampleV, src.PixelSnapH ? 1 : 0, src.GlyphOffset.X, src.GlyphOffset.Y))
            {
                // Canonical reports the input's loader name and, for FreeType, its loader flags; the
                // port has no loader abstraction, so this node has no further content.
                TreePop();
            }
        }

        if (atlas != null && font.Sources.Count > 1 && TreeNode("Input Glyphs Overlap Detection Tool"))
        {
            TextWrapped(
                "- First Input that contains the glyph is used.\n" +
                "- Use ImFontConfig::GlyphExcludeRanges[] to specify ranges to ignore glyph in given Input.\n" +
                "- Prefer using a small number of ranges as the list is scanned every time a new glyph is loaded,\n" +
                "  - e.g. GlyphExcludeRanges[] = { ICON_MIN_FA, ICON_MAX_FA, 0 };\n" +
                "- This tool doesn't cache results and is slow, don't keep it open!");
            if (BeginTable("table", 2))
            {
                for (uint c = 0; c < 0x10000; c++)
                {
                    int overlapMask = CalcFontGlyphSrcOverlapMask(atlas, font, c);
                    if (overlapMask == 0)
                        continue;

                    uint cEnd = c + 1;
                    while (cEnd < 0x10000 && CalcFontGlyphSrcOverlapMask(atlas, font, cEnd) == overlapMask)
                        cEnd++;
                    if (TableNextColumn() && TreeNode(
                            $"##overlap{c:X}",
                            "U+%04X-U+%04X: %d codepoints in %d inputs",
                            c,
                            cEnd - 1,
                            cEnd - c,
                            BitOperations.PopCount((uint)overlapMask)))
                    {
                        for (uint n = c; n < cEnd; n++)
                            BulletText("Codepoint U+%04X (%s)", n, CodepointToUtf8String(n));
                        TreePop();
                    }
                    TableNextColumn();
                    for (int srcN = 0; srcN < font.Sources.Count; srcN++)
                    {
                        if ((overlapMask & (1 << srcN)) != 0)
                        {
                            Text("%d ", srcN);
                            SameLine();
                        }
                    }
                    c = cEnd - 1;
                }
                EndTable();
            }
            TreePop();
        }

        // Display all glyphs of the font in separate pages of 256 characters.
        int frameCount = atlas != null ? atlas.FrameCount : -1;
        for (int bakedN = 0; bakedN < font.BakedList.Count; bakedN++)
        {
            ImFontBaked baked = font.BakedList[bakedN];
            if (!ReferenceEquals(baked.OwnerFont, font))
                continue;

            PushID((int)baked.BakedId);
            if (TreeNode(
                    "Glyphs",
                    "Baked at { %.2fpx, d.%.2f }: %d glyphs%s",
                    baked.Size,
                    baked.RasterizerDensity,
                    baked.Glyphs.Count,
                    baked.LastUsedFrame < frameCount - 1 ? " *Unused*" : string.Empty))
            {
                if (SmallButton("Load all"))
                {
                    for (uint baseCodepoint = 0; baseCodepoint <= UnicodeCodepointMax; baseCodepoint++)
                        baked.FindGlyph(baseCodepoint);
                }

                int surfaceSqrt = (int)MathF.Sqrt(baked.MetricsTotalSurface);
                Text("Ascent: %f, Descent: %f, Ascent-Descent: %f", baked.Ascent, baked.Descent, baked.Ascent - baked.Descent);
                Text("Texture Area: about %d px ~%dx%d px", baked.MetricsTotalSurface, surfaceSqrt, surfaceSqrt);
                for (int srcN = 0; srcN < font.Sources.Count; srcN++)
                {
                    ImFontConfig src = font.Sources[srcN];
                    // Canonical reports the source values and the computed oversample factors side by
                    // side; the port omits the computed factors, which have no equivalent.
                    BulletText(
                        "Input %d: '%s', Oversample: (%d,%d), PixelSnapH: %d, Offset: (%.1f,%.1f)",
                        srcN,
                        src.Name,
                        src.OversampleH,
                        src.OversampleV,
                        src.PixelSnapH ? 1 : 0,
                        src.GlyphOffset.X,
                        src.GlyphOffset.Y);
                }

                DebugNodeFontGlyphsForSrcMask(font, baked, ~0);
                TreePop();
            }
            PopID();
        }
        TreePop();
        Unindent();
    }

    /// <summary>
    /// Draws up to 256 glyphs of one baked font size as a 16-by-16 grid, one collapsible page per
    /// 256-codepoint block, filtered by the source mask. Hovering a cell shows its glyph details.
    /// (<c>ThirdParty/imgui/imgui.cpp:23746-23795</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4146</c>)
    /// </summary>
    /// <param name="font">The font that provides the drawing.</param>
    /// <param name="baked">The baked glyph set to draw from.</param>
    /// <param name="srcMask">Bit mask of font inputs to include; pass all bits set to include every
    /// input.</param>
    /// <remarks>
    /// Large runs of codepoints that the font never baked are skipped in 8192-codepoint blocks, which is
    /// the same coarse filter canonical applies. The cell outline is drawn for a baked glyph and skipped
    /// for an unbaked one, so the grid also shows which codepoints the font supplies.
    /// </remarks>
    public static void DebugNodeFontGlyphsForSrcMask(ImFont font, ImFontBaked baked, int srcMask)
    {
        ImDrawList drawList = GetWindowDrawList();
        uint glyphCol = GetColorU32(ImGuiCol.Text);
        float cellSize = baked.Size * 1.0f;
        float cellSpacing = GetStyle().ItemSpacing.Y;
        for (uint baseCodepoint = 0; baseCodepoint <= UnicodeCodepointMax; baseCodepoint += 256)
        {
            // Skip ahead when a large block of glyphs is absent; canonical tests in chunks of 8192.
            if ((baseCodepoint & 8191) == 0 && font.IsGlyphRangeUnused(baseCodepoint, baseCodepoint + 8191))
            {
                baseCodepoint += 8192 - 256;
                continue;
            }

            int count = 0;
            for (uint n = 0; n < 256; n++)
            {
                if (baked.IsGlyphLoaded(baseCodepoint + n)
                    && (srcMask & (1 << (int)baked.FindGlyph(baseCodepoint + n).SourceIdx)) != 0)
                {
                    count++;
                }
            }
            if (count <= 0)
                continue;
            if (!TreeNode(
                    $"##glyphPage{baseCodepoint:X}",
                    "U+%04X..U+%04X (%d %s)",
                    baseCodepoint,
                    baseCodepoint + 255,
                    count,
                    count > 1 ? "glyphs" : "glyph"))
            {
                continue;
            }

            // Draw a 16x16 grid of glyphs.
            ImVec2 basePos = GetCursorScreenPos();
            for (uint n = 0; n < 256; n++)
            {
                ImVec2 cellP1 = new(
                    basePos.X + (n % 16) * (cellSize + cellSpacing),
                    basePos.Y + (n / 16) * (cellSize + cellSpacing));
                ImVec2 cellP2 = new(cellP1.X + cellSize, cellP1.Y + cellSize);
                ImFontGlyph? glyph = baked.IsGlyphLoaded(baseCodepoint + n) ? baked.FindGlyph(baseCodepoint + n) : null;
                drawList.AddRect(cellP1, cellP2, glyph != null ? 0x64FFFFFFu : 0x32FFFFFFu);
                if (glyph == null || (srcMask & (1 << (int)glyph.Value.SourceIdx)) == 0)
                    continue;

                // RenderChar is used directly because no zero-terminated UTF-8 string is available here.
                font.RenderChar(drawList, cellSize, cellP1, glyphCol, baseCodepoint + n);
                if (IsMouseHoveringRect(cellP1, cellP2) && BeginTooltip())
                {
                    DebugNodeFontGlyph(font, glyph.Value);
                    EndTooltip();
                }
            }
            Dummy(new ImVec2((cellSize + cellSpacing) * 16.0f, (cellSize + cellSpacing) * 16.0f));
            TreePop();
        }
    }

    /// <summary>
    /// Submits the tooltip contents for one glyph: its codepoint, visibility, advance, quad, texture
    /// coordinates, packed rectangle, and originating input.
    /// (<c>ThirdParty/imgui/imgui.cpp:23797-23811</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4147</c>)
    /// </summary>
    /// <param name="font">The font that owns the glyph, used to resolve its packed rectangle.</param>
    /// <param name="glyph">The glyph to report; the port's glyph structure is passed by value.</param>
    /// <remarks>
    /// Canonical passes a glyph pointer. The port passes the structure by value because
    /// <see cref="ImFontGlyph"/> is a value type. The packed-rectangle line is emitted when the glyph has
    /// a live rectangle, which is resolved through the atlas's public
    /// <see cref="ImFontAtlas.GetCustomRect"/> rather than the canonical internal packer lookup.
    /// </remarks>
    public static void DebugNodeFontGlyph(ImFont font, ImFontGlyph glyph)
    {
        Text("Codepoint: U+%04X", glyph.Codepoint);
        Separator();
        Text("Visible: %d", glyph.Visible ? 1 : 0);
        Text("AdvanceX: %.1f", glyph.AdvanceX);
        Text("Pos: (%.2f,%.2f)->(%.2f,%.2f)", glyph.X0, glyph.Y0, glyph.X1, glyph.Y1);
        Text("UV: (%.3f,%.3f)->(%.3f,%.3f)", glyph.U0, glyph.V0, glyph.U1, glyph.V1);
        if (glyph.PackId >= 0
            && font.OwnerAtlas is ImFontAtlas atlas
            && atlas.GetCustomRect(glyph.PackId, out ImFontAtlasRect rect))
        {
            Text("PackId: 0x%X (%dx%d rect at %d,%d)", glyph.PackId, rect.W, rect.H, rect.X, rect.Y);
        }
        Text("SourceIdx: %d", (int)glyph.SourceIdx);
    }

    /// <summary>
    /// Submits the font-selection combo and its explanatory marker, letting the user choose the default
    /// font from the loaded atlas.
    /// (<c>ThirdParty/imgui/imgui.cpp:24569-24597</c>, declared <c>ThirdParty/imgui/imgui_internal.h:4343</c>)
    /// </summary>
    /// <param name="label">The combo label and identifier seed.</param>
    /// <remarks>
    /// Canonical reads the current font directly; the port handles a missing current font because
    /// <see cref="GetFont"/> is nullable. The help text branches on whether the backend reports texture
    /// support, exactly as canonical does.
    /// </remarks>
    public static void ShowFontSelector(string label)
    {
        ImGuiIO io = GetIO();
        ImFont? fontCurrent = GetFont();
        if (BeginCombo(label, fontCurrent != null ? fontCurrent.GetDebugName() : "<null>"))
        {
            for (int fontN = 0; fontN < io.Fonts.Fonts.Count; fontN++)
            {
                ImFont font = io.Fonts.Fonts[fontN];
                PushID(font);
                if (Selectable(font.GetDebugName(), font == fontCurrent, ImGuiSelectableFlags.SelectOnNav))
                    io.FontDefault = font;
                if (font == fontCurrent)
                    SetItemDefaultFocus();
                PopID();
            }
            EndCombo();
        }
        SameLine();
        if ((io.BackendFlags & ImGuiBackendFlags.RendererHasTextures) != 0)
        {
            MetricsHelpMarker(
                "- Load additional fonts with io.Fonts->AddFontXXX() functions.\n" +
                "- Read FAQ and docs/FONTS.md for more details.");
        }
        else
        {
            MetricsHelpMarker(
                "- Load additional fonts with io.Fonts->AddFontXXX() functions.\n" +
                "- The font atlas is built when calling io.Fonts->GetTexDataAsXXXX() or io.Fonts->Build().\n" +
                "- Read FAQ and docs/FONTS.md for more details.\n" +
                "- Legacy backend: if you need to add/remove fonts at runtime (e.g. for DPI change), do it before calling NewFrame().");
        }
    }

    #endregion
}