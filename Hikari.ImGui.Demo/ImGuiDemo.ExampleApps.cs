// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Globalization;
using System.Text;
using Hikari.ImGui;

namespace Hikari.ImGui.Demo;

/// <summary>
/// ImGui Demo example applications that the port hosts as standalone windows, together with the
/// demo-only helper types those applications own.
/// Ports canonical Dear ImGui implementation.
/// </summary>
public static partial class ImGuiDemo
{
    #region [SECTION] Example App: Image Viewer Shared Helpers

    /// <summary>
    /// Holds the pan, zoom, and overlay state of the interactive image viewer used by both the
    /// "Widgets/Images" demo section and the standalone image viewer example application. Ports
    /// canonical <c>ExampleImageViewerData</c>.
    /// </summary>
    private sealed class ExampleImageViewerData
    {
        /// <summary>The fill color drawn behind the image.</summary>
        public uint ImageBackgroundColor = 0xFF646464u; // IM_COL32(100, 100, 100, 255)

        /// <summary>The pixel-grid line color.</summary>
        public uint GridColor = 0x64FFFFFFu; // IM_COL32(255, 255, 255, 100)

        /// <summary>Whether the pixel-grid overlay is drawn.</summary>
        public bool GridEnabled = true;

        /// <summary>
        /// Whether the next canvas submission re-centers the view. The canvas clears the flag after
        /// applying it, matching the canonical one-shot reset.
        /// </summary>
        public bool ViewReset = true;

        /// <summary>The view offset in image space.</summary>
        public ImVec2 ViewOffset;

        /// <summary>The current zoom, in screen pixels per image pixel.</summary>
        public float Zoom = 10.0f;

        /// <summary>The smallest zoom the interactive controls allow.</summary>
        public float ZoomMin = 1.0f;

        /// <summary>The largest zoom the interactive controls allow.</summary>
        public float ZoomMax = 10000.0f;
    }

    /// <summary>
    /// Submits the viewer's grid toggle and zoom drag, bound to the <c>G</c> shortcut. Ports canonical
    /// <c>ExampleImageViewer_DrawOptions</c>.
    /// </summary>
    /// <param name="data">The viewer state edited in place.</param>
    private static void ExampleImageViewerDrawOptions(ExampleImageViewerData data)
    {
        ImGui.SetNextItemShortcut(ImGuiKey.G, ImGuiInputFlags.Tooltip);
        ImGui.Checkbox("Grid", ref data.GridEnabled);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10.0f);
        float zoomPercent = data.Zoom * 100.0f;
        if (ImGui.DragFloat("##Zoom", ref zoomPercent, 5.0f, data.ZoomMin * 100.0f, data.ZoomMax * 100.0f, "%.0f%%", ImGuiSliderFlags.AlwaysClamp))
            data.Zoom = zoomPercent / 100.0f;
    }

    /// <summary>
    /// Draws the pan/zoom image canvas: an invisible button that owns the drag and wheel input, the
    /// image quad, and the optional per-pixel grid overlay. Ports canonical
    /// <c>ExampleImageViewer_DrawCanvas</c>.
    /// </summary>
    /// <param name="data">The viewer state edited in place.</param>
    /// <param name="canvasSize">The canvas extent in screen units.</param>
    /// <param name="imageTexRef">The texture drawn inside the canvas.</param>
    /// <param name="imageWidth">The texture width in texels.</param>
    /// <param name="imageHeight">The texture height in texels.</param>
    private static void ExampleImageViewerDrawCanvas(ExampleImageViewerData data, ImVec2 canvasSize, ImTextureRef imageTexRef, int imageWidth, int imageHeight)
    {
        ImGuiIO io = ImGui.GetIO();
        ImGuiPlatformIO platformIo = ImGui.GetPlatformIO();
        ImDrawList drawList = ImGui.GetWindowDrawList();

        // The invisible button establishes the item rectangle that supplies both the clipped draw
        // region and the hover/active state driving the wheel zoom and pan drag.
        ImGui.InvisibleButton("##Canvas", canvasSize);
        ImVec2 canvasMin = ImGui.GetItemRectMin();
        ImVec2 canvasMax = ImGui.GetItemRectMax();

        if (data.ViewReset)
        {
            // Center the image inside the canvas and add half a pixel of padding so the first texel
            // is not hidden behind the canvas border.
            data.ViewOffset = new ImVec2((canvasSize.X * 0.5f / data.Zoom) - 0.5f, (canvasSize.Y * 0.5f / data.Zoom) - 0.5f);
        }
        data.ViewReset = false;

        // Wheel zoom is routed to the canvas item so the surrounding window keeps its own scroll.
        if (ImGui.SetItemKeyOwner(ImGuiKey.MouseWheelY))
        {
            if (io.MouseWheel != 0.0f)
                data.Zoom = Math.Clamp(data.Zoom * (1.0f + io.MouseWheel * 0.10f), data.ZoomMin, data.ZoomMax);
        }

        float zoom = data.Zoom;
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            // The offset is tracked in image space, so the screen delta is divided by the zoom.
            data.ViewOffset = new ImVec2(data.ViewOffset.X - io.MouseDelta.X / zoom, data.ViewOffset.Y - io.MouseDelta.Y / zoom);
        }

        // Convert the image-space offset back to a screen rectangle; the truncation to whole pixels
        // keeps the grid lines aligned with the texel edges.
        ImVec2 imageMin = new(
            (float)(int)((canvasMin.X - (data.ViewOffset.X * zoom)) + (canvasSize.X * 0.5f)),
            (float)(int)((canvasMin.Y - (data.ViewOffset.Y * zoom)) + (canvasSize.Y * 0.5f)));
        ImVec2 imageMax = new(
            (float)(int)(imageMin.X + imageWidth * zoom),
            (float)(int)(imageMin.Y + imageHeight * zoom));

        drawList.AddRect(new ImVec2(canvasMin.X - 1.0f, canvasMin.Y - 1.0f), new ImVec2(canvasMax.X + 1.0f, canvasMax.Y + 1.0f), 0xFFFFFFFFu);
        drawList.PushClipRect(canvasMin, canvasMax, true);
        drawList.AddRectFilled(imageMin, imageMax, data.ImageBackgroundColor);

        // Nearest-neighbor sampling keeps texels crisp while inspecting individual pixels; the linear
        // callback restores the backend default for the draw commands that follow.
        if (platformIo.DrawCallback_SetSamplerNearest != null)
            drawList.AddCallback(platformIo.DrawCallback_SetSamplerNearest);
        drawList.AddImage(imageTexRef, imageMin, imageMax);
        if (platformIo.DrawCallback_SetSamplerLinear != null)
            drawList.AddCallback(platformIo.DrawCallback_SetSamplerLinear);

        if (data.GridEnabled && zoom > 6.0f)
        {
            float step = zoom;
            for (int px = (int)((canvasMin.X - imageMin.X) / step); px <= (int)((canvasMax.X - imageMin.X) / step); px++)
                drawList.AddLineV(imageMin.X + px * step, canvasMin.Y, canvasMax.Y, data.GridColor, 1.0f);
            for (int py = (int)((canvasMin.Y - imageMin.Y) / step); py <= (int)((canvasMax.Y - imageMin.Y) / step); py++)
                drawList.AddLineH(canvasMin.X, canvasMax.X, imageMin.Y + py * step, data.GridColor, 1.0f);
        }
        drawList.PopClipRect();
    }

    #endregion

    #region [SECTION] Example App: Image Viewer

    /// <summary>
    /// Standalone window hosting the interactive image viewer over the font atlas texture. Ports
    /// canonical <c>ShowExampleAppImageViewer</c>.
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    public static void ShowExampleAppImageViewer(ref bool pOpen)
    {
        ImFontAtlas atlas = ImGui.GetIO().Fonts;
        ImTextureData texData = atlas.TexData!;
        ImTextureRef texRef = texData.GetTexRef();
        int texWidth = texData.Width;
        int texHeight = texData.Height;

        if (ImGui.Begin("Example: Image Viewer", ref pOpen))
        {
            ExampleImageViewerDrawOptions(s_imageViewer);
            ImVec2 canvasSize = ImGui.GetContentRegionAvail();
            ImVec2 canvasMinSize = ImGui.IsWindowAppearing() ? new ImVec2(3.0f * texWidth, 4.0f * texHeight) : new ImVec2(1.0f, 1.0f);
            canvasSize = new ImVec2(MathF.Max(canvasSize.X, canvasMinSize.X), MathF.Max(canvasSize.Y, canvasMinSize.Y));
            ExampleImageViewerDrawCanvas(s_imageViewer, canvasSize, texRef, texWidth, texHeight);
        }
        ImGui.End();
    }

    /// <summary>The persistent viewer state shared by the image viewer window and the Widgets/Images section.</summary>
    private static readonly ExampleImageViewerData s_imageViewer = new();

    #endregion

    #region [SECTION] Example App: Main Menu Bar

    /// <summary>
    /// Demonstrates the helper that creates a menu-bar-sized window at the top of the main viewport
    /// and populates it with the File and Edit menus.
    /// Ports canonical <c>ShowExampleAppMainMenuBar</c>
    /// </summary>
    /// <remarks>
    /// The File menu delegates to <see cref="ShowExampleMenuFile"/>, so the same showcase menu is
    /// shared with the menu-bar, child-window, and modal demonstrations. The Edit menu items are
    /// display-only in canonical as well; the port keeps the same behavior.
    /// </remarks>
    public static void ShowExampleAppMainMenuBar()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                ShowExampleMenuFile();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z")) { }
                if (ImGui.MenuItem("Redo", "Ctrl+Y", false, false)) { }
                ImGui.Separator();
                if (ImGui.MenuItem("Cut", "Ctrl+X")) { }
                if (ImGui.MenuItem("Copy", "Ctrl+C")) { }
                if (ImGui.MenuItem("Paste", "Ctrl+V")) { }
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }
    }

    #endregion

    #region [SECTION] Example App: Console

    /// <summary>
    /// A command console that keeps a filtering log, tab completion for its command table, and
    /// browsing history. Ports canonical <c>ExampleAppConsole</c>
    /// </summary>
    /// <remarks>
    /// Canonical stores raw <c>char*</c> entries and frees them with <c>ImGui::MemFree</c>; this port
    /// stores managed strings, so <see cref="ClearLog"/> only drops the list contents. Log entries are
    /// formatted with <see cref="FormattableString"/> and the invariant culture instead of a canonical
    /// <c>vsnprintf</c> buffer, and the canonical 1024-byte truncation therefore does not apply.
    /// </remarks>
    private sealed class ExampleAppConsole
    {
        /// <summary>The log entries in submission order.</summary>
        private readonly List<string> _items = new();

        /// <summary>The command table displayed by the HELP command and matched by tab completion.</summary>
        private readonly List<string> _commands = new();

        /// <summary>The most recently submitted commands, oldest first; the HISTORY command reads it.</summary>
        private readonly List<string> _history = new();

        /// <summary>
        /// The browsing position inside <see cref="_history"/>: -1 means "new line", otherwise an
        /// index into the history list. <see cref="TextEditCallback"/> advances and rewinds it.
        /// </summary>
        private int _historyPos = -1;

        /// <summary>The inclusion and exclusion filter applied while drawing log entries.</summary>
        private readonly ImGuiTextFilter _filter = new();

        /// <summary>Whether the log region follows the bottom edge while the user stays at the bottom.</summary>
        private bool _autoScroll = true;

        /// <summary>
        /// A one-shot request to scroll to the bottom regardless of <see cref="_autoScroll"/>, used
        /// after a command is submitted so its output is visible.
        /// </summary>
        private bool _scrollToBottom;

        /// <summary>The current contents of the command input line.</summary>
        private string _inputBuf = string.Empty;

        /// <summary>
        /// Creates the console, seeds the command table, and writes the welcome entry. Ports the
        /// canonical constructor.
        /// </summary>
        /// <remarks>
        /// The command table deliberately includes CLASSIFY so that typing C followed by Tab
        /// demonstrates a multi-match completion.
        /// </remarks>
        public ExampleAppConsole()
        {
            ClearLog();
            _commands.Add("HELP");
            _commands.Add("HISTORY");
            _commands.Add("CLEAR");
            _commands.Add("CLASSIFY");
            AddLog($"Welcome to Dear ImGui!");
        }

        /// <summary>
        /// Empties the log. Ports canonical <c>ClearLog</c>
        /// </summary>
        public void ClearLog()
        {
            _items.Clear();
        }

        /// <summary>
        /// Appends one already-formatted log entry. Ports canonical <c>AddLog</c>
        /// </summary>
        /// <param name="message">The interpolated entry text, formatted with the invariant culture.</param>
        public void AddLog(FormattableString message)
        {
            _items.Add(message.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Draws the console window, its log region, and its command input line. Ports canonical
        /// <c>Draw</c>.
        /// </summary>
        /// <param name="title">The window title.</param>
        /// <param name="pOpen">State indicating whether the window is open; cleared by "Close Console".</param>
        public void Draw(string title, ref bool pOpen)
        {
            ImGui.SetNextWindowSize(new ImVec2(520, 600), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin(title, ref pOpen))
            {
                ImGui.End();
                return;
            }

            // After Begin() the last item is the title bar, so this context menu is only reachable
            // from the title bar.
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Close Console"))
                    pOpen = false;
                ImGui.EndPopup();
            }

            ImGui.TextWrapped(
                "This example implements a console with basic coloring, completion (TAB key) and history (Up/Down keys). A more elaborate "
                + "implementation may want to store entries along with extra data such as timestamp, emitter, etc.");
            ImGui.TextWrapped("Enter 'HELP' for help.");

            if (ImGui.SmallButton("Add Debug Text"))
            {
                AddLog($"{_items.Count} some text");
                AddLog($"some more text");
                AddLog($"display very important message here!");
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Add Debug Error"))
                AddLog($"[error] something went wrong");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
                ClearLog();
            ImGui.SameLine();
            bool copyToClipboard = ImGui.SmallButton("Copy");

            ImGui.Separator();

            if (ImGui.BeginPopup("Options"))
            {
                ImGui.Checkbox("Auto-scroll", ref _autoScroll);
                ImGui.EndPopup();
            }

            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.O, ImGuiInputFlags.Tooltip);
            if (ImGui.Button("Options"))
                ImGui.OpenPopup("Options");
            ImGui.SameLine();
            _filter.Draw("Filter (\"incl,-excl\") (\"error\")", 180.0f);
            ImGui.Separator();

            // Reserve enough left-over height for one separator and one input line.
            ImGuiStyle style = ImGui.GetStyle();
            float footerHeightToReserve = style.SeparatorSize + style.ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
            if (ImGui.BeginChild("ScrollingRegion", new ImVec2(0, -footerHeightToReserve), ImGuiChildFlags.NavFlattened, ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (ImGui.BeginPopupContextWindow())
                {
                    if (ImGui.Selectable("Clear"))
                        ClearLog();
                    ImGui.EndPopup();
                }

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(4, 1));
                if (copyToClipboard)
                    ImGui.LogToClipboard();
                foreach (string item in _items)
                {
                    if (!_filter.PassFilter(item))
                        continue;

                    // Canonical marks errors and command echoes with an explicit text color and leaves
                    // every other entry in the style's default text color.
                    ImVec4 color = default;
                    bool hasColor = false;
                    if (item.Contains("[error]", StringComparison.Ordinal))
                    {
                        color = new ImVec4(1.0f, 0.4f, 0.4f, 1.0f);
                        hasColor = true;
                    }
                    else if (item.StartsWith("# ", StringComparison.Ordinal))
                    {
                        color = new ImVec4(1.0f, 0.8f, 0.6f, 1.0f);
                        hasColor = true;
                    }
                    if (hasColor)
                        ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TextUnformatted(item);
                    if (hasColor)
                        ImGui.PopStyleColor();
                }
                if (copyToClipboard)
                    ImGui.LogFinish();

                // Follow the bottom edge only when the user was already there; scrolling away keeps
                // the view in place until a command forces it back.
                if (_scrollToBottom || (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY()))
                    ImGui.SetScrollHereY(1.0f);
                _scrollToBottom = false;

                ImGui.PopStyleVar();
            }
            ImGui.EndChild();
            ImGui.Separator();

            bool reclaimFocus = false;
            ImGuiInputTextFlags inputTextFlags = ImGuiInputTextFlags.EnterReturnsTrue
                | ImGuiInputTextFlags.EscapeClearsAll
                | ImGuiInputTextFlags.CallbackCompletion
                | ImGuiInputTextFlags.CallbackHistory;
            if (ImGui.InputText("Input", ref _inputBuf, inputTextFlags, data => TextEditCallback(data)))
            {
                _inputBuf = _inputBuf.TrimEnd(' ');
                if (_inputBuf.Length > 0)
                    ExecCommand(_inputBuf);
                _inputBuf = string.Empty;
                reclaimFocus = true;
            }

            ImGui.SetItemDefaultFocus();
            if (reclaimFocus)
                ImGui.SetKeyboardFocusHere(-1);

            ImGui.End();
        }

        /// <summary>
        /// Runs one command line after echoing it, records it in the history, and scrolls to the
        /// bottom. Ports canonical <c>ExecCommand</c>
        /// </summary>
        /// <param name="commandLine">The trimmed command line, which is never empty.</param>
        private void ExecCommand(string commandLine)
        {
            AddLog($"# {commandLine}\n");

            // Remove any earlier identical entry so the newest submission moves to the back.
            _historyPos = -1;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (StrEqualNoCase(_history[i], commandLine))
                {
                    _history.RemoveAt(i);
                    break;
                }
            }
            _history.Add(commandLine);

            if (StrEqualNoCase(commandLine, "CLEAR"))
            {
                ClearLog();
            }
            else if (StrEqualNoCase(commandLine, "HELP"))
            {
                AddLog($"Commands:");
                foreach (string command in _commands)
                    AddLog($"- {command}");
            }
            else if (StrEqualNoCase(commandLine, "HISTORY"))
            {
                int first = _history.Count - 10;
                for (int i = first > 0 ? first : 0; i < _history.Count; i++)
                    AddLog($"{i,3}: {_history[i]}\n");
            }
            else
            {
                AddLog($"Unknown command: '{commandLine}'\n");
            }

            _scrollToBottom = true;
        }

        /// <summary>
        /// Handles the completion and history edit events delivered by the input field. Ports
        /// canonical <c>TextEditCallback</c>.
        /// </summary>
        /// <param name="data">The callback event data whose buffer the method rewrites.</param>
        /// <returns>Always zero, matching the canonical callback contract.</returns>
        /// <remarks>
        /// Canonical forwards through <c>TextEditCallbackStub</c>, which recovers the instance from
        /// <c>data-&gt;UserData</c>; the port passes a lambda that captures this instance instead, so
        /// no user-data pointer is involved. Canonical declares no CallbackEdit branch, and this port
        /// likewise handles only completion and history.
        /// </remarks>
        private int TextEditCallback(ImGuiInputTextCallbackData data)
        {
            switch (data.EventFlag)
            {
                case ImGuiInputTextFlags.CallbackCompletion:
                    {
                        // Locate the beginning of the word under the cursor. Offsets are UTF-8 byte
                        // offsets, matching the canonical char* arithmetic.
                        byte[] buffer = data.Buf!;
                        int wordEnd = data.CursorPos;
                        int wordStart = wordEnd;
                        while (wordStart > 0)
                        {
                            char c = (char)buffer[wordStart - 1];
                            if (c == ' ' || c == '\t' || c == ',' || c == ';')
                                break;
                            wordStart--;
                        }

                        string word = Encoding.UTF8.GetString(buffer, wordStart, wordEnd - wordStart);
                        List<string> candidates = new();
                        foreach (string command in _commands)
                        {
                            if (Strnicmp(command, word, word.Length) == 0)
                                candidates.Add(command);
                        }

                        if (candidates.Count == 0)
                        {
                            AddLog($"No match for \"{word}\"!\n");
                        }
                        else if (candidates.Count == 1)
                        {
                            // A single match is inserted whole so the canonical casing is restored.
                            data.DeleteChars(wordStart, wordEnd - wordStart);
                            data.InsertChars(data.CursorPos, Encoding.UTF8.GetBytes(candidates[0]));
                            data.InsertChars(data.CursorPos, " "u8);
                        }
                        else
                        {
                            // Extend the typed prefix as far as every candidate shares it.
                            int matchLen = wordEnd - wordStart;
                            for (; ; )
                            {
                                char c = '\0';
                                bool allCandidatesMatch = true;
                                for (int i = 0; i < candidates.Count && allCandidatesMatch; i++)
                                {
                                    char candidateChar = matchLen < candidates[i].Length ? char.ToUpperInvariant(candidates[i][matchLen]) : '\0';
                                    if (i == 0)
                                        c = candidateChar;
                                    else if (c == '\0' || c != candidateChar)
                                        allCandidatesMatch = false;
                                }
                                if (!allCandidatesMatch)
                                    break;
                                matchLen++;
                            }

                            if (matchLen > 0)
                            {
                                int insertLength = Math.Min(matchLen, candidates[0].Length);
                                data.DeleteChars(wordStart, wordEnd - wordStart);
                                data.InsertChars(data.CursorPos, Encoding.UTF8.GetBytes(candidates[0][..insertLength]));
                            }

                            AddLog($"Possible matches:\n");
                            foreach (string candidate in candidates)
                                AddLog($"- {candidate}\n");
                        }

                        break;
                    }
                case ImGuiInputTextFlags.CallbackHistory:
                    {
                        int previousHistoryPos = _historyPos;
                        if (data.EventKey == ImGuiKey.UpArrow)
                        {
                            if (_historyPos == -1)
                                _historyPos = _history.Count - 1;
                            else if (_historyPos > 0)
                                _historyPos--;
                        }
                        else if (data.EventKey == ImGuiKey.DownArrow)
                        {
                            if (_historyPos != -1)
                            {
                                if (++_historyPos >= _history.Count)
                                    _historyPos = -1;
                            }
                        }

                        if (previousHistoryPos != _historyPos)
                        {
                            string historyStr = _historyPos >= 0 ? _history[_historyPos] : string.Empty;
                            data.DeleteChars(0, data.BufTextLen);
                            data.InsertChars(0, Encoding.UTF8.GetBytes(historyStr));
                        }
                        break;
                    }
            }
            return 0;
        }

        /// <summary>
        /// Compares two strings case-insensitively, returning zero when they are equal. Mirrors
        /// canonical <c>ExampleAppConsole::Stricmp</c>
        /// </summary>
        /// <param name="s1">The first string.</param>
        /// <param name="s2">The second string.</param>
        /// <returns>Zero when the strings match ignoring case; a nonzero difference otherwise.</returns>
        private static bool StrEqualNoCase(string s1, string s2)
        {
            return Strnicmp(s1, s2, int.MaxValue) == 0;
        }

        /// <summary>
        /// Compares at most <paramref name="n"/> characters case-insensitively. Mirrors canonical
        /// <c>ExampleAppConsole::Strnicmp</c>.
        /// </summary>
        /// <param name="s1">The first string.</param>
        /// <param name="s2">The second string.</param>
        /// <param name="n">The maximum number of characters to compare.</param>
        /// <returns>Zero when the prefixes match ignoring case; a nonzero difference otherwise.</returns>
        /// <remarks>A string terminator compares as the value zero, matching the canonical C loop.</remarks>
        private static int Strnicmp(string s1, string s2, int n)
        {
            int i = 0;
            int d = 0;
            while (n > 0)
            {
                char c1 = i < s1.Length ? s1[i] : '\0';
                char c2 = i < s2.Length ? s2[i] : '\0';
                d = char.ToUpperInvariant(c2) - char.ToUpperInvariant(c1);
                if (d != 0 || c1 == '\0')
                    break;
                i++;
                n--;
            }
            return d;
        }
    }

    /// <summary>
    /// Standalone console window hosting the interactive command log.
    /// Ports canonical <c>ShowExampleAppConsole</c>
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    public static void ShowExampleAppConsole(ref bool pOpen)
    {
        s_console.Draw("Example: Console", ref pOpen);
    }

    /// <summary>The persistent console state shared across frames.</summary>
    private static readonly ExampleAppConsole s_console = new();

    #endregion

    #region [SECTION] Example App: Log

    /// <summary>
    /// A scrolling text log with optional filtering and auto-scroll.
    /// Ports canonical <c>ExampleAppLog</c>.
    /// </summary>
    /// <remarks>
    /// Canonical keeps the text in an <c>ImGuiTextBuffer</c> and an index of line offsets; the port
    /// uses the existing <see cref="ImGuiTextBuffer"/> and a managed offset list. Entries are
    /// formatted with <see cref="FormattableString"/> and the invariant culture rather than a
    /// canonical <c>vsnprintf</c> buffer.
    /// </remarks>
    private sealed class ExampleAppLog
    {
        /// <summary>The complete log text, appended to as entries arrive.</summary>
        private readonly ImGuiTextBuffer _buf = new();

        /// <summary>
        /// The byte offset of the first character of each line, including the leading zero for an
        /// empty log. <see cref="AddLog"/> appends one offset per newline it observes.
        /// </summary>
        private readonly List<int> _lineOffsets = new();

        /// <summary>The inclusion and exclusion filter applied to individual lines.</summary>
        private readonly ImGuiTextFilter _filter = new();

        /// <summary>Whether the view follows the bottom edge while the user stays at the bottom.</summary>
        private bool _autoScroll = true;

        /// <summary>
        /// Creates the log with auto-scroll enabled and one empty line registered. Ports the canonical
        /// constructor.
        /// </summary>
        public ExampleAppLog()
        {
            _autoScroll = true;
            Clear();
        }

        /// <summary>
        /// Discards the text and re-seeds the line-offset list with the leading zero. Ports canonical
        /// <c>Clear</c>.
        /// </summary>
        public void Clear()
        {
            _buf.Clear();
            _lineOffsets.Clear();
            _lineOffsets.Add(0);
        }

        /// <summary>
        /// Appends one already-formatted entry and records an offset after every newline it contains.
        /// Ports canonical <c>AddLog</c>.
        /// </summary>
        /// <param name="message">The interpolated entry text, formatted with the invariant culture.</param>
        public void AddLog(FormattableString message)
        {
            int oldSize = _buf.Buf.Count;
            _buf.Append(message.ToString(CultureInfo.InvariantCulture));
            for (int newSize = _buf.Buf.Count; oldSize < newSize; oldSize++)
            {
                if (_buf.Buf[oldSize] == (byte)'\n')
                    _lineOffsets.Add(oldSize + 1);
            }
        }

        /// <summary>
        /// Draws the log window with its option buttons, filter box, and scrolling text region. Ports
        /// canonical <c>Draw</c>.
        /// </summary>
        /// <param name="title">The window title.</param>
        /// <param name="pOpen">State indicating whether the window is open.</param>
        public void Draw(string title, ref bool pOpen)
        {
            if (!ImGui.Begin(title, ref pOpen))
            {
                ImGui.End();
                return;
            }

            if (ImGui.BeginPopup("Options"))
            {
                ImGui.Checkbox("Auto-scroll", ref _autoScroll);
                ImGui.EndPopup();
            }

            if (ImGui.Button("Options"))
                ImGui.OpenPopup("Options");
            ImGui.SameLine();
            bool clear = ImGui.Button("Clear");
            ImGui.SameLine();
            bool copy = ImGui.Button("Copy");
            ImGui.SameLine();
            _filter.Draw("Filter", -100.0f);

            ImGui.Separator();

            if (ImGui.BeginChild("scrolling", new ImVec2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (clear)
                    Clear();
                if (copy)
                    ImGui.LogToClipboard();

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(0, 0));
                string buf = _buf.CStr();
                if (_filter.IsActive())
                {
                    // The filter hides lines, so the clipper cannot be used: it requires random
                    // access to the visible items.
                    for (int lineNo = 0; lineNo < _lineOffsets.Count; lineNo++)
                    {
                        (int lineStart, int lineEnd) = LogLineRange(buf, lineNo);
                        if (lineStart > lineEnd)
                            continue;
                        if (_filter.PassFilter(buf[lineStart..lineEnd]))
                            ImGui.TextUnformatted(buf[lineStart..lineEnd]);
                    }
                }
                else
                {
                    // With no filter, the offset list gives cheap random access, so the clipper only
                    // submits the lines that can be visible.
                    // The clipper must end even when drawing throws, because the port has no destructor to call End().
                    ImGuiListClipper clipper = new();
                    try
                    {
                        clipper.Begin(_lineOffsets.Count);
                        while (clipper.Step())
                        {
                            for (int lineNo = clipper.DisplayStart; lineNo < clipper.DisplayEnd; lineNo++)
                            {
                                (int lineStart, int lineEnd) = LogLineRange(buf, lineNo);
                                if (lineStart > lineEnd)
                                    continue;
                                ImGui.TextUnformatted(buf[lineStart..lineEnd]);
                            }
                        }
                    }
                    finally
                    {
                        clipper.End();
                    }
                }
                ImGui.PopStyleVar();

                // Follow the bottom edge only when the user was already there; scrolling away keeps
                // the view in place.
                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                    ImGui.SetScrollHereY(1.0f);
            }
            ImGui.EndChild();
            ImGui.End();
        }

        /// <summary>
        /// Resolves the half-open character range a line occupies inside the log text. Mirrors the
        /// canonical <c>line_start</c>/<c>line_end</c> computation
        /// </summary>
        /// <param name="buf">The whole log text.</param>
        /// <param name="lineNo">The zero-based line index.</param>
        /// <returns>The clamped start and end offsets of the line, excluding its newline.</returns>
        private (int Start, int End) LogLineRange(string buf, int lineNo)
        {
            int lineStart = _lineOffsets[lineNo];
            int lineEnd = (lineNo + 1 < _lineOffsets.Count) ? (_lineOffsets[lineNo + 1] - 1) : buf.Length;
            lineStart = Math.Clamp(lineStart, 0, buf.Length);
            lineEnd = Math.Clamp(lineEnd, lineStart, buf.Length);
            return (lineStart, lineEnd);
        }
    }

    /// <summary>
    /// Standalone log window that also hosts the debug button adding five sample entries before the
    /// regular log contents. Ports canonical <c>ShowExampleAppLog</c>
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    public static void ShowExampleAppLog(ref bool pOpen)
    {
        // For the demo, the debug button is submitted before the log entries. Both Begin() calls
        // append to the same window, which is why the window is ended here and reopened by Draw().
        ImGui.SetNextWindowSize(new ImVec2(500, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin("Example: Log", ref pOpen);
        if (ImGui.SmallButton("[Debug] Add 5 entries"))
        {
            string[] categories = { "info", "warn", "error" };
            string[] words = { "Bumfuzzled", "Cattywampus", "Snickersnee", "Abibliophobia", "Absquatulate", "Nincompoop", "Pauciloquent" };
            for (int n = 0; n < 5; n++)
            {
                string category = categories[s_logCounter % categories.Length];
                string word = words[s_logCounter % words.Length];
                s_log.AddLog($"[{ImGui.GetFrameCount():D5}] [{category}] Hello, current time is {ImGui.GetTime():F1}, here's a word: '{word}'\n");
                s_logCounter++;
            }
        }
        ImGui.End();

        s_log.Draw("Example: Log", ref pOpen);
    }

    /// <summary>The persistent log state shared across frames.</summary>
    private static readonly ExampleAppLog s_log = new();

    /// <summary>
    /// The rotating index the debug button uses to vary the category and word of each sample entry;
    /// canonical keeps the equivalent counter as a function-local static.
    /// </summary>
    private static int s_logCounter;

    #endregion

    #region [SECTION] Example App: Constrained Resize

    /// <summary>
    /// Demonstrates the built-in size constraints and the three programmatic constraint callbacks.
    /// Ports canonical <c>ShowExampleAppConstrainedResize</c>
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    /// <remarks>
    /// Canonical passes <c>&amp;aspect_ratio</c> and <c>&amp;fixed_step</c> as the callback user data
    /// and the callbacks dereference them. The port keeps those values in the static fields
    /// <see cref="s_constrainedAspectRatio"/> and <see cref="s_constrainedFixedStep"/>, which the
    /// callbacks read directly, and passes <see cref="IntPtr.Zero"/> for the user data. This is the
    /// simplest faithful option because the values are already file-scope demo state and no other
    /// window shares the callbacks.
    /// </remarks>
    public static void ShowExampleAppConstrainedResize(ref bool pOpen)
    {
        string[] testDesc =
        {
            "Between 100x100 and 500x500",
            "At least 100x100",
            "Resize vertical + lock current width",
            "Resize horizontal + lock current height",
            "Width Between 400 and 500",
            "Height at least 400",
            "Custom: Aspect Ratio 16:9",
            "Custom: Always Square",
            "Custom: Fixed Steps (100)",
        };

        s_constrainedAspectRatio = 16.0f / 9.0f;
        s_constrainedFixedStep = 100.0f;
        if (s_constrainedType == 0)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(100, 100), new ImVec2(500, 500));
        if (s_constrainedType == 1)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(100, 100), new ImVec2(float.MaxValue, float.MaxValue));
        if (s_constrainedType == 2)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(-1, 0), new ImVec2(-1, float.MaxValue));
        if (s_constrainedType == 3)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(0, -1), new ImVec2(float.MaxValue, -1));
        if (s_constrainedType == 4)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(400, -1), new ImVec2(500, -1));
        if (s_constrainedType == 5)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(-1, 400), new ImVec2(-1, float.MaxValue));
        if (s_constrainedType == 6)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(0, 0), new ImVec2(float.MaxValue, float.MaxValue), CustomConstraints.AspectRatio);
        if (s_constrainedType == 7)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(0, 0), new ImVec2(float.MaxValue, float.MaxValue), CustomConstraints.Square);
        if (s_constrainedType == 8)
            ImGui.SetNextWindowSizeConstraints(new ImVec2(0, 0), new ImVec2(float.MaxValue, float.MaxValue), CustomConstraints.Step);

        if (!s_constrainedWindowPadding)
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new ImVec2(0.0f, 0.0f));
        ImGuiWindowFlags windowFlags = s_constrainedAutoResize ? ImGuiWindowFlags.AlwaysAutoResize : 0;
        bool windowOpen = ImGui.Begin("Example: Constrained Resize", ref pOpen, windowFlags);
        if (!s_constrainedWindowPadding)
            ImGui.PopStyleVar();

        if (windowOpen)
        {
            if (ImGui.GetIO().KeyShift)
            {
                // A dummy viewport stands in for a real application's texture so the constrained
                // extent can be observed while resizing.
                ImVec2 availSize = ImGui.GetContentRegionAvail();
                ImVec2 pos = ImGui.GetCursorScreenPos();
                ImGui.ColorButton("viewport", new ImVec4(0.5f, 0.2f, 0.5f, 1.0f), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, availSize);
                ImGui.SetCursorScreenPos(new ImVec2(pos.X + 10, pos.Y + 10));
                ImGui.Text("%.2f x %.2f", availSize.X, availSize.Y);
            }
            else
            {
                ImGui.Text("(Hold Shift to display a dummy viewport)");
                if (ImGui.IsWindowDocked())
                    ImGui.Text("Warning: Sizing Constraints won't work if the window is docked!");
                if (ImGui.Button("Set 200x200"))
                    ImGui.SetWindowSize(new ImVec2(200, 200));
                ImGui.SameLine();
                if (ImGui.Button("Set 500x500"))
                    ImGui.SetWindowSize(new ImVec2(500, 500));
                ImGui.SameLine();
                if (ImGui.Button("Set 800x200"))
                    ImGui.SetWindowSize(new ImVec2(800, 200));
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 20);
                ImGui.Combo("Constraint", ref s_constrainedType, testDesc);
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 20);
                ImGui.DragInt("Lines", ref s_constrainedDisplayLines, 0.2f, 1, 100);
                ImGui.Checkbox("Auto-resize", ref s_constrainedAutoResize);
                ImGui.Checkbox("Window padding", ref s_constrainedWindowPadding);
                for (int i = 0; i < s_constrainedDisplayLines; i++)
                    ImGui.Text("%*sHello, sailor! Making this line long enough for the example.", i * 4, "");
            }
        }
        ImGui.End();
    }

    /// <summary>
    /// The three programmatic constraint callbacks. Ports canonical <c>CustomConstraints</c>
    /// </summary>
    /// <remarks>
    /// The callbacks read the file-scope demo parameters directly instead of dereferencing
    /// <see cref="ImGuiSizeCallbackData.UserData"/>, so the caller passes
    /// <see cref="IntPtr.Zero"/>; see <see cref="ShowExampleAppConstrainedResize"/>.
    /// </remarks>
    private static class CustomConstraints
    {
        /// <summary>
        /// Forces the requested height to satisfy the 16:9 aspect ratio while leaving the requested
        /// width unchanged.
        /// </summary>
        /// <param name="data">The current constraint request, updated in place.</param>
        public static void AspectRatio(ImGuiSizeCallbackData data)
        {
            float aspectRatio = s_constrainedAspectRatio;
            data.DesiredSize = new ImVec2(data.DesiredSize.X, (float)(int)(data.DesiredSize.X / aspectRatio));
        }

        /// <summary>Expands both requested axes to the larger of the two, producing a square.</summary>
        /// <param name="data">The current constraint request, updated in place.</param>
        public static void Square(ImGuiSizeCallbackData data)
        {
            float size = MathF.Max(data.DesiredSize.X, data.DesiredSize.Y);
            data.DesiredSize = new ImVec2(size, size);
        }

        /// <summary>Rounds both requested axes to the nearest multiple of the fixed step.</summary>
        /// <param name="data">The current constraint request, updated in place.</param>
        public static void Step(ImGuiSizeCallbackData data)
        {
            float step = s_constrainedFixedStep;
            data.DesiredSize = new ImVec2(
                (int)(data.DesiredSize.X / step + 0.5f) * step,
                (int)(data.DesiredSize.Y / step + 0.5f) * step);
        }
    }

    /// <summary>Whether the auto-resize constraint is enabled.</summary>
    private static bool s_constrainedAutoResize;

    /// <summary>Whether the constrained window keeps its normal padding.</summary>
    private static bool s_constrainedWindowPadding = true;

    /// <summary>The index of the selected constraint in the list of test descriptions.</summary>
    private static int s_constrainedType = 6;

    /// <summary>The number of long sample lines drawn inside the constrained window.</summary>
    private static int s_constrainedDisplayLines = 10;

    /// <summary>The aspect ratio read by <see cref="CustomConstraints.AspectRatio"/>.</summary>
    private static float s_constrainedAspectRatio = 16.0f / 9.0f;

    /// <summary>The step read by <see cref="CustomConstraints.Step"/>.</summary>
    private static float s_constrainedFixedStep = 100.0f;

    #endregion

    #region [SECTION] Example App: Fullscreen Window

    /// <summary>
    /// Demonstrates a window covering the entire viewport, or its work area when menu bars and task
    /// bars must be respected. Ports canonical <c>ShowExampleAppFullscreen</c>
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open; cleared by the close button.</param>
    public static void ShowExampleAppFullscreen(ref bool pOpen)
    {
        ImGuiViewport viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(s_fullscreenUseWorkArea ? viewport.WorkPos : viewport.Pos);
        ImGui.SetNextWindowSize(s_fullscreenUseWorkArea ? viewport.WorkSize : viewport.Size);

        if (ImGui.Begin("Example: Fullscreen window", ref pOpen, s_fullscreenFlags))
        {
            ImGui.Checkbox("Use work area instead of main area", ref s_fullscreenUseWorkArea);
            ImGui.SameLine();
            HelpMarker(
                "Main Area = entire viewport,\nWork Area = entire viewport minus sections used by the main menu bars, task bars etc.\n\n"
                + "Enable the main-menu bar in Examples menu to see the difference.");

            CheckboxFlags("ImGuiWindowFlags_NoBackground", ref s_fullscreenFlags, ImGuiWindowFlags.NoBackground);
            CheckboxFlags("ImGuiWindowFlags_NoDecoration", ref s_fullscreenFlags, ImGuiWindowFlags.NoDecoration);
            ImGui.Indent();
            CheckboxFlags("ImGuiWindowFlags_NoTitleBar", ref s_fullscreenFlags, ImGuiWindowFlags.NoTitleBar);
            CheckboxFlags("ImGuiWindowFlags_NoCollapse", ref s_fullscreenFlags, ImGuiWindowFlags.NoCollapse);
            CheckboxFlags("ImGuiWindowFlags_NoScrollbar", ref s_fullscreenFlags, ImGuiWindowFlags.NoScrollbar);
            ImGui.Unindent();

            if (ImGui.Button("Close this window"))
                pOpen = false;
        }
        ImGui.End();
    }

    /// <summary>Whether the fullscreen window covers the viewport work area rather than the whole viewport.</summary>
    private static bool s_fullscreenUseWorkArea = true;

    /// <summary>The mutable window flags the fullscreen demonstration toggles with checkboxes.</summary>
    private static ImGuiWindowFlags s_fullscreenFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    #endregion

    #region [SECTION] Example App: Manipulating Window Titles

    /// <summary>
    /// Demonstrates the use of the <c>##</c> and <c>###</c> identifiers to keep stable window
    /// identifiers while the displayed titles vary or repeat. Ports canonical
    /// <c>ShowExampleAppWindowTitles</c>.
    /// </summary>
    /// <param name="pOpen">
    /// Accepted for consistency with the other example applications. Canonical takes a <c>bool*</c>
    /// and never reads or writes it, and this port likewise leaves the state untouched.
    /// </param>
    public static void ShowExampleAppWindowTitles(ref bool pOpen)
    {
        ImGuiViewport viewport = ImGui.GetMainViewport();
        ImVec2 basePos = viewport.Pos;

        // "##" hides the text that follows from the displayed title while keeping it in the identifier.
        ImGui.SetNextWindowPos(new ImVec2(basePos.X + 100, basePos.Y + 100), ImGuiCond.FirstUseEver);
        ImGui.Begin("Same title as another window##1");
        ImGui.Text("This is window 1.\nMy title is the same as window 2, but my identifier is unique.");
        ImGui.End();

        ImGui.SetNextWindowPos(new ImVec2(basePos.X + 100, basePos.Y + 200), ImGuiCond.FirstUseEver);
        ImGui.Begin("Same title as another window##2");
        ImGui.Text("This is window 2.\nMy title is the same as window 1, but my identifier is unique.");
        ImGui.End();

        // "###" keeps the identifier fixed at "AnimatedTitle" while the displayed title changes.
        char spinner = "|/-\\"[(int)(ImGui.GetTime() / 0.25) & 3];
        string title = $"Animated title {spinner} {ImGui.GetFrameCount()}###AnimatedTitle";
        ImGui.SetNextWindowPos(new ImVec2(basePos.X + 100, basePos.Y + 300), ImGuiCond.FirstUseEver);
        ImGui.Begin(title);
        ImGui.Text("This window has a changing title.");
        ImGui.End();
    }

    #endregion

    #region [SECTION] Example Tree Data Model

    /// <summary>
    /// A node of the small tree shown by the Property Editor example application. Ports canonical
    /// <c>ExampleTreeNode</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The canonical struct is a plain aggregate whose members the demo reads and writes directly;
    /// this port keeps public fields for the same reason. Interior nodes exist only for navigation,
    /// while leaves carry the editable sample data flagged by <see cref="HasData"/>.
    /// </para>
    /// <para>
    /// Canonical <c>ExampleTree_DestroyNode</c>.
    /// recursively frees the nodes allocated with <c>IM_NEW</c>. No managed equivalent exists because
    /// the garbage collector reclaims the whole graph once the demo drops its root reference.
    /// </para>
    /// </remarks>
    private sealed class ExampleTreeNode
    {
        /// <summary>The node display name; canonical stores it in a fixed 28-byte buffer.</summary>
        public string Name = string.Empty;

        /// <summary>The stable identifier used as the tree item identifier and property-table identifier.</summary>
        public int Uid;

        /// <summary>The parent node, or <see langword="null"/> for the root.</summary>
        public ExampleTreeNode? Parent;

        /// <summary>The child nodes in insertion order.</summary>
        public List<ExampleTreeNode> Children = new();

        /// <summary>The node's own index within <see cref="Parent"/>'s child list, or zero for the root.</summary>
        public int IndexInParent;

        /// <summary>Whether this node is a leaf that exposes the sample member data for editing.</summary>
        public bool HasData;

        /// <summary>The sample Boolean member edited by the property editor.</summary>
        public bool DataMyBool = true;

        /// <summary>The sample 32-bit integer member edited by the property editor.</summary>
        public int DataMyInt = 128;

        /// <summary>The sample two-component float member edited by the property editor.</summary>
        public ImVec2 DataMyVec2 = new(0.0f, 3.141592f);
    }

    /// <summary>
    /// Describes one editable member of <see cref="ExampleTreeNode"/> for the data-driven property
    /// table. Ports canonical <c>ExampleMemberInfo</c>
    /// </summary>
    /// <remarks>
    /// Canonical records the member type, the component count, and a byte offset computed with
    /// <c>offsetof</c> so the editor can form a raw pointer into the node. Managed code cannot take a
    /// byte offset into a reference object, so this port keeps the member name and type metadata and
    /// resolves the target field by switching on <see cref="Name"/> when the editor submits the
    /// widget. This preserves the canonical data-driven table without introducing unsafe code.
    /// </remarks>
    private sealed class ExampleMemberInfo
    {
        /// <summary>The canonical member label displayed in the property table.</summary>
        public string Name = string.Empty;

        /// <summary>The canonical scalar type used to validate the editor chosen for the member.</summary>
        public ImGuiDataType DataType;

        /// <summary>The number of components; one for scalar members.</summary>
        public int DataCount;
    }

    /// <summary>
    /// The canonical metadata table describing the editable members of <see cref="ExampleTreeNode"/>.
    /// Ports canonical <c>ExampleTreeNodeMemberInfos</c>
    /// </summary>
    private static readonly ExampleMemberInfo[] s_exampleTreeNodeMemberInfos =
    {
        new() { Name = "MyName", DataType = ImGuiDataType.String, DataCount = 1 },
        new() { Name = "MyBool", DataType = ImGuiDataType.Bool, DataCount = 1 },
        new() { Name = "MyInt", DataType = ImGuiDataType.S32, DataCount = 1 },
        new() { Name = "MyVec2", DataType = ImGuiDataType.Float, DataCount = 2 },
    };

    /// <summary>The category names that seed the level-one node names of the demo tree.</summary>
    private static readonly string[] s_exampleTreeCategoryNames =
    {
        "Apple", "Banana", "Cherry", "Kiwi", "Mango", "Orange", "Pear", "Pineapple", "Strawberry", "Watermelon",
    };

    /// <summary>
    /// Creates one node, records its position among its siblings, and links it into its parent's child
    /// list. Ports canonical <c>ExampleTree_CreateNode</c>
    /// </summary>
    /// <param name="name">The node display name.</param>
    /// <param name="uid">The stable node identifier.</param>
    /// <param name="parent">The parent node, or <see langword="null"/> to create a detached root.</param>
    /// <returns>The newly created node.</returns>
    private static ExampleTreeNode ExampleTreeCreateNode(string name, int uid, ExampleTreeNode? parent)
    {
        ExampleTreeNode node = new()
        {
            Name = name,
            Uid = uid,
            Parent = parent,
            IndexInParent = parent?.Children.Count ?? 0,
        };
        parent?.Children.Add(node);
        return node;
    }

    /// <summary>
    /// Builds the demo tree used by the Property Editor example: a hidden root, twenty category nodes,
    /// one child per character of each category node's name, and one sub-child beneath each first
    /// child. Ports canonical <c>ExampleTree_CreateDemoTree</c>
    /// </summary>
    /// <returns>The root node whose children are the visible top-level nodes.</returns>
    /// <remarks>
    /// Canonical formats node names into a fixed 28-byte buffer; managed strings have no such cap, so
    /// the canonical truncation cannot occur with the names this method produces. Level-two and
    /// level-three nodes are marked as leaves with <see cref="ExampleTreeNode.HasData"/>.
    /// </remarks>
    private static ExampleTreeNode ExampleTreeCreateDemoTree()
    {
        const int rootItemsCount = 20;
        int uid = 0;
        ExampleTreeNode rootNode = ExampleTreeCreateNode("<ROOT>", ++uid, null);
        for (int indexL0 = 0; indexL0 < rootItemsCount; indexL0++)
        {
            int categoryCount = s_exampleTreeCategoryNames.Length;
            string categoryName = s_exampleTreeCategoryNames[indexL0 / (rootItemsCount / categoryCount)];
            string nodeName = string.Format(CultureInfo.InvariantCulture, "{0} {1}", categoryName, indexL0 % (rootItemsCount / categoryCount));
            ExampleTreeNode nodeL1 = ExampleTreeCreateNode(nodeName, ++uid, rootNode);
            int numberOfChildren = nodeL1.Name.Length;
            for (int indexL1 = 0; indexL1 < numberOfChildren; indexL1++)
            {
                ExampleTreeNode nodeL2 = ExampleTreeCreateNode(string.Format(CultureInfo.InvariantCulture, "Child {0}", indexL1), ++uid, nodeL1);
                nodeL2.HasData = true;
                if (indexL1 == 0)
                {
                    ExampleTreeNode nodeL3 = ExampleTreeCreateNode("Sub-child 0", ++uid, nodeL2);
                    nodeL3.HasData = true;
                }
            }
        }
        return rootNode;
    }

    /// <summary>
    /// Tree helpers shared by the Widgets/Selection State "Multi-Select (trees)" demonstration. Ports
    /// canonical <c>ExampleTreeFuncs</c>., which the
    /// canonical demo defines locally inside that subsection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Canonical stores a raw <c>ExampleTreeNode*</c> as the multi-select selection user data and casts
    /// it back when it applies a range request. Managed code cannot round-trip a boxed reference
    /// through a 64-bit value without unsafe code, so this port stores the stable
    /// <see cref="ExampleTreeNode.Uid"/> instead and resolves it with <see cref="FindNodeByUid"/>.
    /// The lookup is only performed once per range request, which matches the canonical comment that a
    /// parent search is acceptable here.
    /// </para>
    /// <para>
    /// Canonical closes the storage of the collapsed subtree with <c>ImGui::TreeNodeSetOpen()</c>
    ///., which the demo reaches through the forward
    /// declaration. The port exposes
    /// <see cref="ImGui.TreeNodeSetOpen"/> publicly for the same reason, so
    /// <see cref="TreeCloseAndUnselectChildNodes"/> clears the stored open state exactly as canonical.
    /// </para>
    /// </remarks>
    private static class ExampleTreeFuncs
    {
        /// <summary>
        /// Draws one node and recurses into its children, updating the multi-select state when the node
        /// collapses. Ports canonical <c>ExampleTreeFuncs::DrawNode</c>
        /// </summary>
        /// <param name="node">The node to draw.</param>
        /// <param name="selection">The selection store that supplies and receives selection state.</param>
        public static void DrawNode(ExampleTreeNode node, ImGuiSelectionBasicStorage selection)
        {
            ImGuiTreeNodeFlags treeNodeFlags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
            treeNodeFlags |= ImGuiTreeNodeFlags.NavLeftJumpsToParent;
            if (node.Children.Count == 0)
                treeNodeFlags |= ImGuiTreeNodeFlags.Bullet | ImGuiTreeNodeFlags.Leaf;
            if (selection.Contains((uint)node.Uid))
                treeNodeFlags |= ImGuiTreeNodeFlags.Selected;

            // The selection user data and the open/close storage both use the node UID, so the
            // multi-select code and the tree storage can address any node without a UI-stack position.
            ImGui.SetNextItemSelectionUserData(node.Uid);
            ImGui.SetNextItemStorageID((uint)node.Uid);
            if (ImGui.TreeNodeEx(node.Name, treeNodeFlags))
            {
                foreach (ExampleTreeNode child in node.Children)
                    DrawNode(child, selection);
                ImGui.TreePop();
            }
            else if (ImGui.IsItemToggledOpen())
            {
                TreeCloseAndUnselectChildNodes(node, selection);
            }
        }

        /// <summary>
        /// Closes and unselects the descendants of a collapsed node and selects the node itself when any
        /// descendant had been selected. Ports canonical
        /// <c>ExampleTreeFuncs::TreeCloseAndUnselectChildNodes</c>
        /// </summary>
        /// <param name="node">The node that was just collapsed.</param>
        /// <param name="selection">The selection store to update.</param>
        /// <param name="depth">The recursion depth; zero marks the node the user collapsed.</param>
        /// <returns>The number of selected nodes found in the subtree.</returns>
        /// <remarks>
        /// The recursion descends into the children first so that the collapsed node's own open state is
        /// cleared last, matching canonical. The node is then selected
        /// when any descendant had been selected, so a collapsed subtree reports as one selected item.
        /// </remarks>
        public static int TreeCloseAndUnselectChildNodes(ExampleTreeNode node, ImGuiSelectionBasicStorage selection, int depth = 0)
        {
            int unselectedCount = selection.Contains((uint)node.Uid) ? 1 : 0;
            if (depth == 0 || ImGui.TreeNodeGetOpen((uint)node.Uid))
            {
                foreach (ExampleTreeNode child in node.Children)
                    unselectedCount += TreeCloseAndUnselectChildNodes(child, selection, depth + 1);
                ImGui.TreeNodeSetOpen((uint)node.Uid, false);
            }

            selection.SetItemSelected((uint)node.Uid, depth == 0 && unselectedCount > 0);
            return unselectedCount;
        }

        /// <summary>
        /// Applies the multi-select requests produced for the tree to the selection store. Ports
        /// canonical <c>ExampleTreeFuncs::ApplySelectionRequests</c>
        /// </summary>
        /// <param name="msIo">The multi-select I/O whose request list is applied.</param>
        /// <param name="tree">The root node used to resolve request endpoints.</param>
        /// <param name="selection">The selection store to update.</param>
        public static void ApplySelectionRequests(ImGuiMultiSelectIO msIo, ExampleTreeNode tree, ImGuiSelectionBasicStorage selection)
        {
            foreach (ImGuiSelectionRequest request in msIo.Requests)
            {
                if (request.Type == ImGuiSelectionRequestType.SetAll)
                {
                    if (request.Selected)
                        TreeSetAllInOpenNodes(tree, selection, request.Selected);
                    else
                        selection.Clear();
                }
                else if (request.Type == ImGuiSelectionRequestType.SetRange)
                {
                    ExampleTreeNode? firstNode = FindNodeByUid(tree, request.RangeFirstItem);
                    ExampleTreeNode? lastNode = FindNodeByUid(tree, request.RangeLastItem);
                    for (ExampleTreeNode? node = firstNode; node != null; node = TreeGetNextNodeInVisibleOrder(node, lastNode))
                        selection.SetItemSelected((uint)node.Uid, request.Selected);
                }
            }
        }

        /// <summary>
        /// Selects or clears every visible node in the opened part of the tree and honors the
        /// <c>SetAll</c> request. Ports canonical <c>ExampleTreeFuncs::TreeSetAllInOpenNodes</c>
        /// </summary>
        /// <param name="node">The subtree root to process.</param>
        /// <param name="selection">The selection store to update.</param>
        /// <param name="selected">The selection state to apply.</param>
        /// <remarks>The root node itself is neither visible nor selectable, matching canonical.</remarks>
        public static void TreeSetAllInOpenNodes(ExampleTreeNode node, ImGuiSelectionBasicStorage selection, bool selected)
        {
            if (node.Parent != null)
                selection.SetItemSelected((uint)node.Uid, selected);
            if (node.Parent == null || ImGui.TreeNodeGetOpen((uint)node.Uid))
            {
                foreach (ExampleTreeNode child in node.Children)
                    TreeSetAllInOpenNodes(child, selection, selected);
            }
        }

        /// <summary>
        /// Returns the next node in user-visible order, crossing only opened nodes, stopping after
        /// <paramref name="lastNode"/>. Ports canonical
        /// <c>ExampleTreeFuncs::TreeGetNextNodeInVisibleOrder</c>
        /// </summary>
        /// <param name="currNode">The node whose successor is requested.</param>
        /// <param name="lastNode">The inclusive end of the range, or <see langword="null"/> for none.</param>
        /// <returns>The successor node, or <see langword="null"/> when the range ends.</returns>
        /// <remarks>
        /// <see cref="ExampleTreeNode.IndexInParent"/> makes the sibling step a direct index rather
        /// than a search, which is the canonical rationale for maintaining it.
        /// </remarks>
        public static ExampleTreeNode? TreeGetNextNodeInVisibleOrder(ExampleTreeNode currNode, ExampleTreeNode? lastNode)
        {
            if (ReferenceEquals(currNode, lastNode))
                return null;

            if (currNode.Children.Count > 0 && ImGui.TreeNodeGetOpen((uint)currNode.Uid))
                return currNode.Children[0];

            ExampleTreeNode? current = currNode;
            while (current.Parent != null)
            {
                if (current.IndexInParent + 1 < current.Parent.Children.Count)
                    return current.Parent.Children[current.IndexInParent + 1];
                current = current.Parent;
            }
            return null;
        }

        /// <summary>
        /// Finds the node carrying the given selection user data (its <see cref="ExampleTreeNode.Uid"/>).
        /// </summary>
        /// <param name="node">The subtree to search.</param>
        /// <param name="uid">The node UID stored as multi-select user data.</param>
        /// <returns>The matching node, or <see langword="null"/> when no node carries the UID.</returns>
        /// <remarks>
        /// This search replaces the canonical pointer cast from <c>ImGuiSelectionUserData</c> back to
        /// <c>ExampleTreeNode*</c>. and is invoked
        /// once per range request rather than per traversed node.
        /// </remarks>
        private static ExampleTreeNode? FindNodeByUid(ExampleTreeNode node, long uid)
        {
            if (node.Uid == uid)
                return node;
            foreach (ExampleTreeNode child in node.Children)
            {
                ExampleTreeNode? match = FindNodeByUid(child, uid);
                if (match != null)
                    return match;
            }
            return null;
        }
    }

    #endregion

    #region [SECTION] Example App: Property Editor

    /// <summary>
    /// Owns the persistent selection, filter, and clipper-toggle state of the Property Editor example
    /// window and draws its tree and property panes. Ports canonical <c>ExampleAppPropertyEditor</c>
    /// </summary>
    private sealed class ExampleAppPropertyEditor
    {
        /// <summary>The substring filter applied to the visible top-level nodes.</summary>
        public readonly ImGuiTextFilter Filter = new();

        /// <summary>The node currently reflected in the property pane, or <see langword="null"/>.</summary>
        public ExampleTreeNode? SelectedNode;

        /// <summary>Whether the flat tree traversal uses the list clipper instead of recursion.</summary>
        public bool UseClipper;

        /// <summary>
        /// Draws the child window holding the tree and its filter controls, followed by the property
        /// pane for the selected node. Ports canonical <c>ExampleAppPropertyEditor::Draw</c>
        /// </summary>
        /// <param name="rootNode">The demo tree root.</param>
        public void Draw(ExampleTreeNode rootNode)
        {
            // Left side: the resizable tree pane. The table supplies the alternating row background.
            if (ImGui.BeginChild("##tree", new ImVec2(300, 0), ImGuiChildFlags.ResizeX | ImGuiChildFlags.Borders | ImGuiChildFlags.NavFlattened))
            {
                ImGui.PushItemFlag(ImGuiItemFlags.NoNavDefaultFocus, true);
                ImGui.Checkbox("Use Clipper", ref UseClipper);
                ImGui.SameLine();
                ImGui.TextUnformatted(string.Format(CultureInfo.InvariantCulture, "({0} root nodes)", rootNode.Children.Count));
                ImGui.SetNextItemWidth(-float.Epsilon);
                ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.F, ImGuiInputFlags.Tooltip);
                if (ImGui.InputTextWithHint("##Filter", "incl,-excl", Filter.InputBuf, Filter.InputBuf.Length, ImGuiInputTextFlags.EscapeClearsAll))
                    Filter.Build();
                ImGui.PopItemFlag();

                if (ImGui.BeginTable("##list", 1, ImGuiTableFlags.RowBg))
                {
                    if (UseClipper)
                        DrawClippedTree(rootNode);
                    else
                        DrawTree(rootNode);
                    ImGui.EndTable();
                }
            }
            ImGui.EndChild();

            // Right side: the properties of the selected node.
            ImGui.SameLine();

            ImGui.BeginGroup(); // Lock the X position.
            if (SelectedNode is ExampleTreeNode node)
            {
                ImGui.TextUnformatted(node.Name);
                ImGui.TextDisabled(string.Format(CultureInfo.InvariantCulture, "UID: 0x{0:X8}", node.Uid));
                ImGui.Separator();
                if (ImGui.BeginTable("##properties", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
                {
                    // The object identifier is pushed after entering the table so the table layout is
                    // shared by every node while the editable widgets stay distinct per node.
                    ImGui.PushID(node.Uid);
                    ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthStretch, 2.0f);
                    if (node.HasData)
                    {
                        foreach (ExampleMemberInfo memberInfo in s_exampleTreeNodeMemberInfos)
                        {
                            ImGui.TableNextRow();
                            ImGui.PushID(memberInfo.Name);
                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextUnformatted(memberInfo.Name);
                            ImGui.TableNextColumn();
                            DrawMemberEditor(node, memberInfo);
                            ImGui.PopID();
                        }
                    }
                    ImGui.PopID();
                    ImGui.EndTable();
                }
            }
            ImGui.EndGroup();
        }

        /// <summary>
        /// Submits the editor widget for one member of the selected node. Ports the canonical member
        /// switch inside <c>ExampleAppPropertyEditor::Draw</c>
        /// </summary>
        /// <param name="node">The node whose member is edited in place.</param>
        /// <param name="memberInfo">The member metadata that selects the field and the widget.</param>
        /// <remarks>
        /// Canonical forms a raw pointer from the member offset and calls the generic scalar widget.
        /// This port resolves the field from <see cref="ExampleMemberInfo.Name"/>, which is the managed
        /// equivalent of that offset lookup, and asserts the accompanying type metadata so a mismatch
        /// between the table and the switch is caught during development. The canonical 28-byte cap on
        /// the name member is not reproduced because managed strings are unbounded.
        /// </remarks>
        private static void DrawMemberEditor(ExampleTreeNode node, ExampleMemberInfo memberInfo)
        {
            switch (memberInfo.Name)
            {
                case "MyName":
                    System.Diagnostics.Debug.Assert(memberInfo.DataType == ImGuiDataType.String && memberInfo.DataCount == 1);
                    ImGui.InputText("##Editor", ref node.Name);
                    break;
                case "MyBool":
                    System.Diagnostics.Debug.Assert(memberInfo.DataType == ImGuiDataType.Bool && memberInfo.DataCount == 1);
                    ImGui.Checkbox("##Editor", ref node.DataMyBool);
                    break;
                case "MyInt":
                    System.Diagnostics.Debug.Assert(memberInfo.DataType == ImGuiDataType.S32 && memberInfo.DataCount == 1);
                    ImGui.SetNextItemWidth(-float.Epsilon);
                    ImGui.DragScalar("##Editor", ImGuiDataType.S32, ref node.DataMyInt, 1.0f, int.MinValue, int.MaxValue);
                    break;
                case "MyVec2":
                {
                    System.Diagnostics.Debug.Assert(memberInfo.DataType == ImGuiDataType.Float && memberInfo.DataCount == 2);
                    float[] components = { node.DataMyVec2.X, node.DataMyVec2.Y };
                    ImGui.SetNextItemWidth(-float.Epsilon);
                    if (ImGui.SliderScalarN("##Editor", ImGuiDataType.Float, components, 2, 0.0f, 1.0f))
                        node.DataMyVec2 = new ImVec2(components[0], components[1]);
                    break;
                }
            }
        }

        /// <summary>
        /// Reports whether a top-level node passes the substring filter. Ports canonical
        /// <c>ExampleAppPropertyEditor::IsNodePassingFilter</c>
        /// </summary>
        /// <param name="node">The node to test.</param>
        /// <returns>
        /// <see langword="true"/> when the node is deeper than the top level or its name matches the
        /// filter. As in canonical, only the top-level nodes are filtered.
        /// </returns>
        private bool IsNodePassingFilter(ExampleTreeNode node)
        {
            return node.Parent!.Parent != null || Filter.PassFilter(node.Name);
        }

        /// <summary>
        /// Draws the tree recursively and closes each open node. Ports canonical
        /// <c>ExampleAppPropertyEditor::DrawTree</c>
        /// </summary>
        /// <param name="node">The node whose children are drawn.</param>
        private void DrawTree(ExampleTreeNode node)
        {
            foreach (ExampleTreeNode child in node.Children)
            {
                if (IsNodePassingFilter(child) && DrawTreeNode(child))
                {
                    DrawTree(child);
                    ImGui.TreePop();
                }
            }
        }

        /// <summary>
        /// Draws the tree with the indeterminate list clipper, which fast-forwards through non-visible
        /// chunks instead of submitting every node. Ports canonical
        /// <c>ExampleAppPropertyEditor::DrawClippedTree</c>
        /// </summary>
        /// <param name="rootNode">The demo tree root.</param>
        /// <remarks>
        /// The clipper begins with an indeterminate count so it can expose visible ranges before the
        /// item count is known; after the traversal the final count is reported with
        /// <see cref="ImGuiListClipper.SeekCursorForItem"/> so the scrollbar remains accurate.
        /// </remarks>
        private void DrawClippedTree(ExampleTreeNode rootNode)
        {
            ExampleTreeNode? node = rootNode.Children[0];
            ImGuiListClipper clipper = new();
            try
            {
                clipper.Begin(int.MaxValue);
                while (clipper.Step())
                {
                    while (clipper.UserIndex < clipper.DisplayEnd && node != null)
                        node = DrawClippedTreeNodeAndAdvanceToNext(clipper, node);
                }

                // Continue past the visible range purely to count the remaining nodes.
                while (node != null)
                    node = DrawClippedTreeNodeAndAdvanceToNext(clipper, node);
                clipper.SeekCursorForItem(clipper.UserIndex);
            }
            finally
            {
                clipper.End();
            }
        }

        /// <summary>
        /// Submits or skips one node and returns the next node in visible order. Ports canonical
        /// <c>ExampleAppPropertyEditor::DrawClippedTreeNodeAndAdvanceToNext</c>
        /// </summary>
        /// <param name="clipper">
        /// The clipper whose <see cref="ImGuiListClipper.UserIndex"/> tracks the traversal position.
        /// </param>
        /// <param name="node">The node to process.</param>
        /// <returns>The next node in visible order, or <see langword="null"/> when the traversal ends.</returns>
        private ExampleTreeNode? DrawClippedTreeNodeAndAdvanceToNext(ImGuiListClipper clipper, ExampleTreeNode node)
        {
            if (IsNodePassingFilter(node))
            {
                bool isOpen;
                if (clipper.UserIndex >= clipper.DisplayStart && clipper.UserIndex < clipper.DisplayEnd)
                {
                    isOpen = DrawTreeNode(node);
                }
                else
                {
                    // Outside the visible range only the stored open state is needed: the node is not
                    // drawn, but its children must still be visited when it is open.
                    isOpen = node.Children.Count > 0 && ImGui.TreeNodeGetOpen((uint)node.Uid);
                    if (isOpen)
                        ImGui.TreePush(node.Name);
                }
                clipper.UserIndex++;

                if (isOpen)
                    return node.Children[0];
            }

            // Next node: the following sibling, otherwise walk up to an ancestor that has one.
            ExampleTreeNode? current = node;
            while (current != null)
            {
                if (current.IndexInParent + 1 < current.Parent!.Children.Count)
                    return current.Parent.Children[current.IndexInParent + 1];
                current = current.Parent;
                if (current?.Parent == null)
                    break;
                ImGui.TreePop();
            }
            return null;
        }

        /// <summary>
        /// Draws one tree row and updates the selection when the row gains focus. Ports canonical
        /// <c>ExampleAppPropertyEditor::DrawTreeNode</c>
        /// </summary>
        /// <param name="node">The node to draw.</param>
        /// <returns><see langword="true"/> when the node is open and its children should be drawn.</returns>
        private bool DrawTreeNode(ExampleTreeNode node)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGuiTreeNodeFlags treeFlags = ImGuiTreeNodeFlags.None;
            treeFlags |= ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
            treeFlags |= ImGuiTreeNodeFlags.NavLeftJumpsToParent;
            treeFlags |= ImGuiTreeNodeFlags.SpanFullWidth;
            treeFlags |= ImGuiTreeNodeFlags.DrawLinesToNodes;
            if (ReferenceEquals(node, SelectedNode))
                treeFlags |= ImGuiTreeNodeFlags.Selected;
            if (node.Children.Count == 0)
                treeFlags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet | ImGuiTreeNodeFlags.NoTreePushOnOpen;
            if (!node.DataMyBool)
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.SetNextItemStorageID((uint)node.Uid);
            bool isOpen = ImGui.TreeNodeEx(node.Uid, treeFlags, "%s", node.Name);
            if (node.Children.Count == 0)
                isOpen = false;
            if (!node.DataMyBool)
                ImGui.PopStyleColor();
            if (ImGui.IsItemFocused())
                SelectedNode = node;
            return isOpen;
        }
    }

    /// <summary>The persistent state of the Property Editor example window.</summary>
    private static readonly ExampleAppPropertyEditor s_propertyEditor = new();

    /// <summary>The demo tree, created once on first use and retained for the lifetime of the demo.</summary>
    private static ExampleTreeNode? s_propertyEditorDemoTree;

    /// <summary>
    /// Demonstrates a two-pane property editor: a filterable tree of demo nodes on the left and the
    /// editable members of the selected leaf on the right. Ports canonical
    /// <c>ShowExampleAppPropertyEditor</c>.
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    public static void ShowExampleAppPropertyEditor(ref bool pOpen)
    {
        ImGui.SetNextWindowSize(new ImVec2(430, 450), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Example: Property editor", ref pOpen))
        {
            ImGui.End();
            return;
        }

        s_propertyEditorDemoTree ??= ExampleTreeCreateDemoTree();
        s_propertyEditor.Draw(s_propertyEditorDemoTree);

        ImGui.End();
    }

    #endregion

    #region [SECTION] Example App: Documents

    /// <summary>
    /// Selects how the Documents example submits its open documents.
    /// </summary>
    /// <remarks>
    /// Ports the canonical function-local <c>Target</c> enumeration
    ///. The port keeps the selected mode in a
    /// static field beside <see cref="ShowExampleAppDocuments"/>, so the enumeration is a nested type.
    /// </remarks>
    private enum DocumentsTarget
    {
        /// <summary>Submits no document bodies, which isolates the surrounding example window.</summary>
        /// <remarks>Ports canonical <c>Target_None</c>.</remarks>
        None,

        /// <summary>Submits open documents as tabs in a local tab bar.</summary>
        /// <remarks>Ports canonical <c>Target_Tab</c>.</remarks>
        Tab,

        /// <summary>Submits open documents as regular windows docked into an embedded dock space.</summary>
        /// <remarks>
        /// Ports canonical <c>Target_DockSpaceAndWindow</c>
        /// </remarks>
        DockSpaceAndWindow,
    }

    /// <summary>
    /// One document in the Documents example: a stable identifier, a mutable title, open and dirty
    /// state, and an arbitrary associated color.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>MyDocument</c>.
    /// Canonical stores the model by value in a vector and addresses it by pointer; this port declares
    /// a reference type so the document list, the close queue, and the rename target all refer to the
    /// same instance, preserving the pointer identity the canonical code relies on.
    /// </remarks>
    private sealed class ExampleDocument
    {
        /// <summary>
        /// Initializes a document with the supplied identity, title, open state, and color.
        /// </summary>
        /// <param name="uid">The never-changing document identifier.</param>
        /// <param name="name">The initial document title.</param>
        /// <param name="open">Whether the document starts open.</param>
        /// <param name="color">The arbitrary color associated with the document.</param>
        /// <remarks>
        /// Ports canonical <c>MyDocument::MyDocument</c>
        /// </remarks>
        public ExampleDocument(int uid, string name, bool open, ImVec4 color)
        {
            Uid = uid;
            Name = name;
            Open = open;
            OpenPrev = open;
            Dirty = false;
            Color = color;
        }

        /// <summary>The document title, which the rename popup edits in place.</summary>
        /// <remarks>Ports canonical <c>MyDocument::Name</c>.</remarks>
        public string Name;

        /// <summary>The unique identifier appended to the tab name so tabs stay stable across renames.</summary>
        /// <remarks>Ports canonical <c>MyDocument::UID</c>.</remarks>
        public int Uid;

        /// <summary>Whether the document is currently open.</summary>
        /// <remarks>Ports canonical <c>MyDocument::Open</c>.</remarks>
        public bool Open;

        /// <summary>The previous frame's open state, used to detect programmatic closure.</summary>
        /// <remarks>Ports canonical <c>MyDocument::OpenPrev</c>.</remarks>
        public bool OpenPrev;

        /// <summary>Whether the document has unsaved modifications.</summary>
        /// <remarks>Ports canonical <c>MyDocument::Dirty</c>.</remarks>
        public bool Dirty;

        /// <summary>The arbitrary color shown by the placeholder contents.</summary>
        /// <remarks>Ports canonical <c>MyDocument::Color</c>.</remarks>
        public ImVec4 Color;

        /// <summary>Marks the document open.</summary>
        /// <remarks>Ports canonical <c>MyDocument::DoOpen</c>.</remarks>
        public void DoOpen() => Open = true;

        /// <summary>Closes the document and discards its unsaved state.</summary>
        /// <remarks>
        /// Ports canonical <c>MyDocument::DoForceClose</c>
        /// </remarks>
        public void DoForceClose()
        {
            Open = false;
            Dirty = false;
        }

        /// <summary>Clears the unsaved flag.</summary>
        /// <remarks>Ports canonical <c>MyDocument::DoSave</c>.</remarks>
        public void DoSave() => Dirty = false;
    }

    /// <summary>
    /// Owns the document list, the deferred close queue, and the pending rename for the Documents
    /// example.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ExampleAppDocuments</c>
    /// </remarks>
    private sealed class ExampleAppDocuments
    {
        /// <summary>Initializes the fixed demonstration document set.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::ExampleAppDocuments</c>
        /// </remarks>
        public ExampleAppDocuments()
        {
            Documents.Add(new ExampleDocument(0, "Lettuce", true, new ImVec4(0.4f, 0.8f, 0.4f, 1.0f)));
            Documents.Add(new ExampleDocument(1, "Eggplant", true, new ImVec4(0.8f, 0.5f, 1.0f, 1.0f)));
            Documents.Add(new ExampleDocument(2, "Carrot", true, new ImVec4(1.0f, 0.8f, 0.5f, 1.0f)));
            Documents.Add(new ExampleDocument(3, "Tomato", false, new ImVec4(1.0f, 0.3f, 0.4f, 1.0f)));
            Documents.Add(new ExampleDocument(4, "A Rather Long Title", false, new ImVec4(0.4f, 0.8f, 0.8f, 1.0f)));
            Documents.Add(new ExampleDocument(5, "Some Document", false, new ImVec4(0.8f, 0.8f, 1.0f, 1.0f)));
        }

        /// <summary>All known documents, whether open or closed.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::Documents</c>
        /// </remarks>
        public readonly List<ExampleDocument> Documents = new();

        /// <summary>Documents whose closure is deferred until the save confirmation runs.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::CloseQueue</c>
        /// </remarks>
        public readonly List<ExampleDocument> CloseQueue = new();

        /// <summary>The document being renamed, or <see langword="null"/> when no rename is pending.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::RenamingDoc</c>
        /// </remarks>
        public ExampleDocument? RenamingDoc;

        /// <summary>Whether the pending rename should open its popup on this frame.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::RenamingStarted</c>
        /// </remarks>
        public bool RenamingStarted;

        /// <summary>Builds the tab label from the mutable title plus the stable document identifier.</summary>
        /// <param name="doc">The document whose tab label is built.</param>
        /// <returns>The label in <c>title###docNN</c> form.</returns>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::GetTabName</c>
        ///. Canonical writes into a caller buffer;
        /// this port returns the formatted string using the invariant culture.
        /// </remarks>
        public string GetTabName(ExampleDocument doc)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}###doc{1}", doc.Name, doc.Uid);
        }

        /// <summary>Submits the placeholder body and the per-document shortcut buttons.</summary>
        /// <param name="doc">The document whose contents are submitted.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::DisplayDocContents</c>
        /// </remarks>
        public void DisplayDocContents(ExampleDocument doc)
        {
            ImGui.PushID(doc);
            ImGui.Text("Document \"%s\"", doc.Name);
            ImGui.PushStyleColor(ImGuiCol.Text, doc.Color);
            ImGui.TextWrapped("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.");
            ImGui.PopStyleColor();

            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.R, ImGuiInputFlags.Tooltip);
            if (ImGui.Button("Rename.."))
            {
                RenamingDoc = doc;
                RenamingStarted = true;
            }
            ImGui.SameLine();

            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.M, ImGuiInputFlags.Tooltip);
            if (ImGui.Button("Modify"))
                doc.Dirty = true;

            ImGui.SameLine();
            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.S, ImGuiInputFlags.Tooltip);
            if (ImGui.Button("Save"))
                doc.DoSave();

            ImGui.SameLine();
            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.W, ImGuiInputFlags.Tooltip);
            if (ImGui.Button("Close"))
                CloseQueue.Add(doc);

            // The port's ColorEdit3() consumes a float buffer rather than a pointer into the color, so
            // the document color is copied in and written back when the widget reports an edit.
            float[] color = [doc.Color.X, doc.Color.Y, doc.Color.Z];
            if (ImGui.ColorEdit3("color", color))
                doc.Color = new ImVec4(color[0], color[1], color[2], doc.Color.W);
            ImGui.PopID();
        }

        /// <summary>Submits the context menu for one document tab.</summary>
        /// <param name="doc">The document whose context menu is submitted.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::DisplayDocContextMenu</c>
        /// </remarks>
        public void DisplayDocContextMenu(ExampleDocument doc)
        {
            if (!ImGui.BeginPopupContextItem())
                return;

            if (ImGui.MenuItem(string.Format(CultureInfo.InvariantCulture, "Save {0}", doc.Name), "Ctrl+S", false, doc.Open))
                doc.DoSave();
            if (ImGui.MenuItem("Rename...", "Ctrl+R", false, doc.Open))
                RenamingDoc = doc;
            if (ImGui.MenuItem("Close", "Ctrl+W", false, doc.Open))
                CloseQueue.Add(doc);
            ImGui.EndPopup();
        }

        /// <summary>
        /// Notifies the tab bar when a document was closed outside the regular tab interface, which
        /// avoids the one-frame hole canonical describes.
        /// </summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAppDocuments::NotifyOfDocumentsClosedElsewhere</c>
        /// </remarks>
        public void NotifyOfDocumentsClosedElsewhere()
        {
            for (int n = 0; n < Documents.Count; n++)
            {
                ExampleDocument doc = Documents[n];
                if (!doc.Open && doc.OpenPrev)
                    ImGui.SetTabItemClosed(doc.Name);
                doc.OpenPrev = doc.Open;
            }
        }
    }

    /// <summary>The persistent state of the Documents example window.</summary>
    /// <remarks>
    /// Ports the canonical function-local <c>static ExampleAppDocuments app</c>
    /// </remarks>
    private static readonly ExampleAppDocuments s_documents = new();

    /// <summary>The selected document submission mode.</summary>
    /// <remarks>
    /// Ports the canonical function-local <c>static Target opt_target</c>, initialized to
    /// <c>Target_Tab</c>.
    /// </remarks>
    private static DocumentsTarget s_documentsTarget = DocumentsTarget.Tab;

    /// <summary>Whether the documents' tab bar permits reordering.</summary>
    /// <remarks>
    /// Ports the canonical function-local <c>static bool opt_reorderable</c>
    /// </remarks>
    private static bool s_documentsReorderable = true;

    /// <summary>The tab bar fitting policy applied when documents are submitted as tabs.</summary>
    /// <remarks>
    /// Ports the canonical function-local <c>static ImGuiTabBarFlags opt_fitting_flags</c>
    /// </remarks>
    private static readonly ImGuiTabBarFlags s_documentsFittingFlags = ImGuiTabBarFlags.FittingPolicyDefault_;

    /// <summary>
    /// Demonstrates three ways of presenting a set of documents: none, tabs in a local tab bar, and
    /// regular windows docked into an embedded dock space.
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    /// <remarks>
    /// Ports canonical <c>ShowExampleAppDocuments</c>
    ///. The canonical <c>IMGUI_DEMO_MARKER</c> has
    /// no counterpart in the port.
    /// </remarks>
    public static void ShowExampleAppDocuments(ref bool pOpen)
    {
        // The documents are submitted even when the parent window is not visible in the dock-space
        // mode, otherwise a document docked over the parent would stop resubmitting itself.
        bool windowContentsVisible = ImGui.Begin("Example: Documents", ref pOpen, ImGuiWindowFlags.MenuBar);
        if (!windowContentsVisible && s_documentsTarget != DocumentsTarget.DockSpaceAndWindow)
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                int openCount = 0;
                for (int n = 0; n < s_documents.Documents.Count; n++)
                    openCount += s_documents.Documents[n].Open ? 1 : 0;

                if (ImGui.BeginMenu("Open", openCount < s_documents.Documents.Count))
                {
                    for (int n = 0; n < s_documents.Documents.Count; n++)
                        if (!s_documents.Documents[n].Open && ImGui.MenuItem(s_documents.Documents[n].Name))
                            s_documents.Documents[n].DoOpen();
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem("Close All Documents", null, false, openCount > 0))
                    for (int n = 0; n < s_documents.Documents.Count; n++)
                        s_documents.CloseQueue.Add(s_documents.Documents[n]);
                if (ImGui.MenuItem("Exit"))
                    pOpen = false;
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }

        // Debug list: one checkbox per document that opens and force-closes it.
        for (int docN = 0; docN < s_documents.Documents.Count; docN++)
        {
            ExampleDocument doc = s_documents.Documents[docN];
            if (docN > 0)
                ImGui.SameLine();
            ImGui.PushID(doc);
            if (ImGui.Checkbox(doc.Name, ref doc.Open))
                if (!doc.Open)
                    doc.DoForceClose();
            ImGui.PopID();
        }

        ImGui.PushItemWidth(ImGui.GetFontSize() * 12.0f);
        int target = (int)s_documentsTarget;
        if (ImGui.Combo("Output", ref target, "None\0TabBar+Tabs\0DockSpace+Window\0"))
            s_documentsTarget = (DocumentsTarget)target;
        ImGui.PopItemWidth();

        bool redockAll = false;
        if (s_documentsTarget == DocumentsTarget.Tab)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Reorderable Tabs", ref s_documentsReorderable);
        }
        if (s_documentsTarget == DocumentsTarget.DockSpaceAndWindow)
        {
            ImGui.SameLine();
            redockAll = ImGui.Button("Redock all");
        }

        ImGui.Separator();

        if (s_documentsTarget == DocumentsTarget.Tab)
        {
            ImGuiTabBarFlags tabBarFlags = s_documentsFittingFlags | (s_documentsReorderable ? ImGuiTabBarFlags.Reorderable : ImGuiTabBarFlags.None);
            tabBarFlags |= ImGuiTabBarFlags.DrawSelectedOverline;
            if (ImGui.BeginTabBar("##tabs", tabBarFlags))
            {
                if (s_documentsReorderable)
                    s_documents.NotifyOfDocumentsClosedElsewhere();

                for (int docN = 0; docN < s_documents.Documents.Count; docN++)
                {
                    ExampleDocument doc = s_documents.Documents[docN];
                    if (!doc.Open)
                        continue;

                    ImGuiTabItemFlags tabFlags = doc.Dirty ? ImGuiTabItemFlags.UnsavedDocument : ImGuiTabItemFlags.None;
                    bool visible = ImGui.BeginTabItem(s_documents.GetTabName(doc), ref doc.Open, tabFlags);

                    // A close attempt on an unsaved document is cancelled and queued for confirmation.
                    if (!doc.Open && doc.Dirty)
                    {
                        doc.Open = true;
                        s_documents.CloseQueue.Add(doc);
                    }

                    s_documents.DisplayDocContextMenu(doc);
                    if (visible)
                    {
                        s_documents.DisplayDocContents(doc);
                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
        }
        else if (s_documentsTarget == DocumentsTarget.DockSpaceAndWindow)
        {
            if ((ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0)
            {
                s_documents.NotifyOfDocumentsClosedElsewhere();

                // A dock space node accepts any window, so the documents dock into the example window.
                uint dockspaceId = ImGui.GetID("MyDockSpace");
                ImGui.DockSpace(dockspaceId);

                for (int docN = 0; docN < s_documents.Documents.Count; docN++)
                {
                    ExampleDocument doc = s_documents.Documents[docN];
                    if (!doc.Open)
                        continue;

                    ImGui.SetNextWindowDockID(dockspaceId, redockAll ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
                    ImGuiWindowFlags windowFlags = doc.Dirty ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None;
                    bool visible = ImGui.Begin(doc.Name, ref doc.Open, windowFlags);

                    if (!doc.Open && doc.Dirty)
                    {
                        doc.Open = true;
                        s_documents.CloseQueue.Add(doc);
                    }

                    s_documents.DisplayDocContextMenu(doc);
                    if (visible)
                        s_documents.DisplayDocContents(doc);

                    ImGui.End();
                }
            }
            else
            {
                ShowDockingDisabledMessage();
            }
        }

        // The remaining contents are only drawn while the parent window itself is visible.
        if (!windowContentsVisible)
        {
            ImGui.End();
            return;
        }

        ExampleDocument? renamingDoc = s_documents.RenamingDoc;
        if (renamingDoc != null)
        {
            if (s_documents.RenamingStarted)
                ImGui.OpenPopup("Rename");
            if (ImGui.BeginPopup("Rename"))
            {
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 30.0f);
                if (ImGui.InputText("###Name", ref renamingDoc.Name, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    ImGui.CloseCurrentPopup();
                    s_documents.RenamingDoc = null;
                }
                if (s_documents.RenamingStarted)
                    ImGui.SetKeyboardFocusHere(-1);
                ImGui.EndPopup();
            }
            else
            {
                s_documents.RenamingDoc = null;
            }
            s_documents.RenamingStarted = false;
        }

        if (s_documents.CloseQueue.Count > 0)
        {
            int closeQueueUnsavedDocuments = 0;
            for (int n = 0; n < s_documents.CloseQueue.Count; n++)
                if (s_documents.CloseQueue[n].Dirty)
                    closeQueueUnsavedDocuments++;

            if (closeQueueUnsavedDocuments == 0)
            {
                // Nothing to save, so every queued document closes immediately.
                for (int n = 0; n < s_documents.CloseQueue.Count; n++)
                    s_documents.CloseQueue[n].DoForceClose();
                s_documents.CloseQueue.Clear();
            }
            else
            {
                if (!ImGui.IsPopupOpen("Save?"))
                    ImGui.OpenPopup("Save?");
                if (ImGui.BeginPopupModal("Save?", ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.Text("Save change to the following items?");
                    float itemHeight = ImGui.GetTextLineHeightWithSpacing();
                    if (ImGui.BeginChild(ImGui.GetID("frame"), new ImVec2(-float.Epsilon, 6.25f * itemHeight), ImGuiChildFlags.FrameStyle))
                    {
                        for (int n = 0; n < s_documents.CloseQueue.Count; n++)
                            if (s_documents.CloseQueue[n].Dirty)
                                ImGui.Text("%s", s_documents.CloseQueue[n].Name);
                    }
                    ImGui.EndChild();

                    ImVec2 buttonSize = new(ImGui.GetFontSize() * 7.0f, 0.0f);
                    if (ImGui.Button("Yes", buttonSize))
                    {
                        for (int n = 0; n < s_documents.CloseQueue.Count; n++)
                        {
                            if (s_documents.CloseQueue[n].Dirty)
                                s_documents.CloseQueue[n].DoSave();
                            s_documents.CloseQueue[n].DoForceClose();
                        }
                        s_documents.CloseQueue.Clear();
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("No", buttonSize))
                    {
                        for (int n = 0; n < s_documents.CloseQueue.Count; n++)
                            s_documents.CloseQueue[n].DoForceClose();
                        s_documents.CloseQueue.Clear();
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel", buttonSize))
                    {
                        s_documents.CloseQueue.Clear();
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
        }

        ImGui.End();
    }

    #endregion

    #region [SECTION] Example App: Assets Browser

    /// <summary>
    /// One asset in the Assets Browser example: a stable identifier and a type index that selects the
    /// overlay color.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ExampleAsset</c>.
    /// </remarks>
    private sealed class ExampleAsset
    {
        /// <summary>
        /// Initializes an asset with the supplied identifier and type.
        /// </summary>
        /// <param name="id">The persistent asset identifier, which replaces the canonical pointer.</param>
        /// <param name="type">The asset type index.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAsset::ExampleAsset</c>
        /// </remarks>
        public ExampleAsset(uint id, int type)
        {
            Id = id;
            Type = type;
        }

        /// <summary>The persistent asset identifier.</summary>
        /// <remarks>Ports canonical <c>ExampleAsset::ID</c>.</remarks>
        public uint Id;

        /// <summary>The asset type index.</summary>
        /// <remarks>Ports canonical <c>ExampleAsset::Type</c>.</remarks>
        public int Type;

        /// <summary>The sort specifications currently driving the comparison, or null outside a sort.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAsset::s_current_sort_specs</c>
        /// </remarks>
        private static ImGuiTableSortSpecs? s_currentSortSpecs;

        /// <summary>The cached comparison delegate, so sorting does not allocate a new delegate per call.</summary>
        /// <remarks>
        /// Ports the function pointer canonical passes to <c>qsort()</c>
        /// </remarks>
        private static readonly Comparison<ExampleAsset> s_compareWithSortSpecs = CompareWithSortSpecs;

        /// <summary>Sorts the asset list with the table's current sort specifications.</summary>
        /// <param name="sortSpecs">The specifications returned by <c>TableGetSortSpecs()</c>.</param>
        /// <param name="items">The mutable asset list, sorted in place.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAsset::SortWithSortSpecs</c>
        ///. Canonical calls <c>qsort()</c>; this
        /// port sorts the list with the cached comparison delegate.
        /// </remarks>
        public static void SortWithSortSpecs(ImGuiTableSortSpecs sortSpecs, List<ExampleAsset> items)
        {
            s_currentSortSpecs = sortSpecs;
            if (items.Count > 1)
                items.Sort(s_compareWithSortSpecs);
            s_currentSortSpecs = null;
        }

        /// <summary>Orders two assets by the active specifications, falling back to identifier order.</summary>
        /// <param name="a">The left asset.</param>
        /// <param name="b">The right asset.</param>
        /// <returns>A negative value when <paramref name="a"/> precedes <paramref name="b"/>, a positive
        /// value when it follows, and zero when they compare equal.</returns>
        /// <remarks>
        /// Ports canonical <c>ExampleAsset::CompareWithSortSpecs</c>
        /// </remarks>
        private static int CompareWithSortSpecs(ExampleAsset a, ExampleAsset b)
        {
            for (int n = 0; n < s_currentSortSpecs!.SpecsCount; n++)
            {
                ImGuiTableColumnSortSpecs sortSpec = s_currentSortSpecs.Specs![n];
                int delta = 0;
                if (sortSpec.ColumnIndex == 0)
                    delta = (int)a.Id - (int)b.Id;
                else if (sortSpec.ColumnIndex == 1)
                    delta = a.Type - b.Type;
                if (delta > 0)
                    return sortSpec.SortDirection == ImGuiSortDirection.Ascending ? +1 : -1;
                if (delta < 0)
                    return sortSpec.SortDirection == ImGuiSortDirection.Ascending ? -1 : +1;
            }
            return (int)a.Id - (int)b.Id;
        }
    }

    /// <summary>
    /// Owns the asset list, the shared selection, and the icon-grid layout of the Assets Browser
    /// example.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ExampleAssetsBrowser</c>
    /// </remarks>
    private sealed class ExampleAssetsBrowser
    {
        /// <summary>Initializes the browser with the canonical ten thousand demonstration assets.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::ExampleAssetsBrowser</c>
        /// </remarks>
        public ExampleAssetsBrowser()
        {
            AddItems(10000);
        }

        /// <summary>Whether the per-asset type overlay square is drawn.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::ShowTypeOverlay</c>
        /// </remarks>
        public bool ShowTypeOverlay = true;

        /// <summary>Whether the single-header sorting table is submitted.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::AllowSorting</c>
        /// </remarks>
        public bool AllowSorting = true;

        /// <summary>Whether two-dimensional box selection is enabled.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::AllowBoxSelect</c>
        /// </remarks>
        public bool AllowBoxSelect = true;

        /// <summary>Whether a box selection may start inside an existing selection.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::AllowBoxSelectInsideSelection</c>
        /// </remarks>
        public bool AllowBoxSelectInsideSelection;

        /// <summary>Whether an unselected asset may be dragged without first selecting it.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::AllowDragUnselected</c>
        /// </remarks>
        public bool AllowDragUnselected;

        /// <summary>The icon edge length in screen units; zero defers to the label width.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::IconSize</c>
        /// </remarks>
        public float IconSize;

        /// <summary>The spacing between icon cells.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::IconSpacing</c>
        /// </remarks>
        public int IconSpacing = 10;

        /// <summary>The extra spacing subtracted from the selectable so gaps remain box-selectable.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::IconHitSpacing</c>
        /// </remarks>
        public int IconHitSpacing = 4;

        /// <summary>Whether leftover row width is distributed as extra spacing.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::StretchSpacing</c>
        /// </remarks>
        public bool StretchSpacing = true;

        /// <summary>Whether twice as many items are submitted per line to exercise horizontal scroll.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::UseScrollX</c>
        /// </remarks>
        public bool UseScrollX;

        /// <summary>The assets currently held by the browser.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::Items</c>
        /// </remarks>
        public readonly List<ExampleAsset> Items = new();

        /// <summary>The shared selection store, extended with deletion support.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::Selection</c>
        ///. The store type already exists for the
        /// widgets multi-select demonstrations.
        /// </remarks>
        public readonly ExampleSelectionWithDeletion Selection = new();

        /// <summary>The identifier assigned to the next created asset.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::NextItemId</c>
        /// </remarks>
        public uint NextItemId;

        /// <summary>A deletion request deferred to the item loop.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::RequestDelete</c>
        /// </remarks>
        public bool RequestDelete;

        /// <summary>A sort request deferred until the sorting table is submitted.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::RequestSort</c>
        /// </remarks>
        public bool RequestSort;

        /// <summary>The accumulated wheel delta that drives icon zooming.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::ZoomWheelAccum</c>
        /// </remarks>
        public float ZoomWheelAccum;

        /// <summary>The size of one icon cell, produced by <see cref="UpdateLayoutSizes"/>.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutItemSize</c>
        /// </remarks>
        public ImVec2 LayoutItemSize;

        /// <summary>The advance from one cell to the next, equal to the cell size plus the spacing.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutItemStep</c>
        /// </remarks>
        public ImVec2 LayoutItemStep;

        /// <summary>The spacing between icon cells.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutItemSpacing</c>
        /// </remarks>
        public float LayoutItemSpacing;

        /// <summary>The item spacing the selectables use, which leaves the hit gaps uncovered.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutSelectableSpacing</c>
        /// </remarks>
        public float LayoutSelectableSpacing;

        /// <summary>The padding added before the first and after the last icon cell.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutOuterPadding</c>
        /// </remarks>
        public float LayoutOuterPadding;

        /// <summary>The number of icon cells per line.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutColumnCount</c>
        /// </remarks>
        public int LayoutColumnCount;

        /// <summary>The number of lines the asset list occupies.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::LayoutLineCount</c>
        /// </remarks>
        public int LayoutLineCount;

        /// <summary>Appends new assets whose types repeat in the canonical twenty-item cycle.</summary>
        /// <param name="count">The number of assets to append.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::AddItems</c>
        ///. Canonical reserves vector capacity
        /// first; the managed list grows on demand.
        /// </remarks>
        public void AddItems(int count)
        {
            if (Items.Count == 0)
                NextItemId = 0;
            for (int n = 0; n < count; n++, NextItemId++)
                Items.Add(new ExampleAsset(NextItemId, (NextItemId % 20) < 15 ? 0 : (NextItemId % 20) < 18 ? 1 : 2));
            RequestSort = true;
        }

        /// <summary>Removes every asset and clears the selection.</summary>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::ClearItems</c>
        /// </remarks>
        public void ClearItems()
        {
            Items.Clear();
            Selection.Clear();
        }

        /// <summary>Recomputes the icon-grid geometry for the supplied content width.</summary>
        /// <param name="availWidth">The width available inside the assets child window.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::UpdateLayoutSizes</c>
        /// </remarks>
        public void UpdateLayoutSizes(float availWidth)
        {
            // Without stretching, the row is allowed to extend into its right-most spacing.
            LayoutItemSpacing = IconSpacing;
            if (!StretchSpacing)
                availWidth += MathF.Floor(LayoutItemSpacing * 0.5f);

            LayoutItemSize = new ImVec2(MathF.Floor(IconSize), MathF.Floor(IconSize));
            LayoutColumnCount = Math.Max((int)(availWidth / (LayoutItemSize.X + LayoutItemSpacing)), 1);

            // With stretching, the remaining width becomes extra spacing; round before dividing so the
            // spacing may be fractional.
            if (StretchSpacing && LayoutColumnCount > 1)
                LayoutItemSpacing = MathF.Floor(availWidth - (LayoutItemSize.X * LayoutColumnCount)) / LayoutColumnCount;

            if (UseScrollX)
                LayoutColumnCount *= 2;
            LayoutLineCount = (Items.Count + LayoutColumnCount - 1) / LayoutColumnCount;

            LayoutItemStep = new ImVec2(LayoutItemSize.X + LayoutItemSpacing, LayoutItemSize.Y + LayoutItemSpacing);
            LayoutSelectableSpacing = Math.Max(MathF.Floor(LayoutItemSpacing) - IconHitSpacing, 0.0f);
            LayoutOuterPadding = MathF.Floor(LayoutItemSpacing * 0.5f);
        }

        /// <summary>Submits the browser window: menu bar, sorting table, icon grid, and context menu.</summary>
        /// <param name="title">The window title.</param>
        /// <param name="pOpen">State indicating whether the window is open.</param>
        /// <remarks>
        /// Ports canonical <c>ExampleAssetsBrowser::Draw</c>
        ///. The canonical <c>IMGUI_DEMO_MARKER</c>
        /// has no counterpart in the port.
        /// </remarks>
        public void Draw(string title, ref bool pOpen)
        {
            if (IconSize <= 0.0f)
                IconSize = ImGui.CalcTextSize("99999").X;

            ImGui.SetNextWindowSize(new ImVec2(IconSize * 25.0f, IconSize * 15.0f), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin(title, ref pOpen, ImGuiWindowFlags.MenuBar))
            {
                ImGui.End();
                return;
            }

            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("Add 10000 items"))
                        AddItems(10000);
                    if (ImGui.MenuItem("Clear items"))
                        ClearItems();
                    ImGui.Separator();
                    if (ImGui.MenuItem("Close"))
                        pOpen = false;
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Edit"))
                {
                    if (ImGui.MenuItem("Delete", "Del", false, Selection.Size > 0))
                        RequestDelete = true;
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Options"))
                {
                    ImGui.PushItemWidth(ImGui.GetFontSize() * 10.0f);

                    ImGui.SeparatorText("Contents");
                    ImGui.Checkbox("Show Type Overlay", ref ShowTypeOverlay);
                    ImGui.Checkbox("Allow Sorting", ref AllowSorting);

                    ImGui.SeparatorText("Selection Behavior");
                    ImGui.Checkbox("Allow box-selection", ref AllowBoxSelect);
                    if (ImGui.Checkbox("Allow box-selection from selected items", ref AllowBoxSelectInsideSelection) && AllowBoxSelectInsideSelection)
                        AllowDragUnselected = false;
                    if (ImGui.Checkbox("Allow dragging unselected item", ref AllowDragUnselected) && AllowDragUnselected)
                        AllowBoxSelectInsideSelection = false;

                    ImGui.SeparatorText("Layout");
                    ImGui.SliderFloat("Icon Size", ref IconSize, 16.0f, 128.0f, "%.0f");
                    ImGui.SameLine();
                    HelpMarker("Use Ctrl+Wheel to zoom");
                    ImGui.SliderInt("Icon Spacing", ref IconSpacing, 0, 32);
                    ImGui.SliderInt("Icon Hit Spacing", ref IconHitSpacing, 0, 32);
                    ImGui.Checkbox("Stretch Spacing", ref StretchSpacing);
                    ImGui.Checkbox("Use ScrollX", ref UseScrollX);
                    ImGui.PopItemWidth();
                    ImGui.EndMenu();
                }
                ImGui.EndMenuBar();
            }

            // A table with a single header row demonstrates reusing the table sorting interface.
            if (AllowSorting)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(0, 0));
                ImGuiTableFlags tableFlagsForSortSpecs = ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders;
                if (ImGui.BeginTable("for_sort_specs_only", 2, tableFlagsForSortSpecs, new ImVec2(0.0f, ImGui.GetFrameHeight())))
                {
                    ImGui.TableSetupColumn("Index");
                    ImGui.TableSetupColumn("Type");
                    ImGui.TableHeadersRow();
                    ImGuiTableSortSpecs? sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs != null && (sortSpecs.SpecsDirty || RequestSort))
                    {
                        ExampleAsset.SortWithSortSpecs(sortSpecs, Items);
                        sortSpecs.SpecsDirty = false;
                        RequestSort = false;
                    }
                    ImGui.EndTable();
                }
                ImGui.PopStyleVar();
            }

            ImGuiIO io = ImGui.GetIO();
            ImGui.SetNextWindowContentSize(new ImVec2(0.0f, LayoutOuterPadding + (LayoutLineCount * (LayoutItemSize.Y + LayoutItemSpacing))));
            if (ImGui.BeginChild("Assets", new ImVec2(0.0f, -ImGui.GetTextLineHeightWithSpacing()), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImDrawList drawList = ImGui.GetWindowDrawList();

                float availWidth = ImGui.GetContentRegionAvail().X;
                UpdateLayoutSizes(availWidth);

                // The grid starts at the padded cursor position of the child window.
                ImVec2 startPos = ImGui.GetCursorScreenPos();
                startPos = new ImVec2(startPos.X + LayoutOuterPadding, startPos.Y + LayoutOuterPadding);
                ImGui.SetCursorScreenPos(startPos);

                ImGuiMultiSelectFlags msFlags = ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.ClearOnClickVoid;

                // Two-dimensional box selection lets the clipped items use the rectangle's X bounds.
                if (AllowBoxSelect)
                    msFlags |= ImGuiMultiSelectFlags.BoxSelect2d;

                if (AllowDragUnselected)
                    msFlags |= ImGuiMultiSelectFlags.SelectOnClickRelease;
                else if (AllowBoxSelectInsideSelection)
                    msFlags |= ImGuiMultiSelectFlags.SelectOnClickAlways;

                msFlags |= ImGuiMultiSelectFlags.NavWrapX;

                ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(msFlags, Selection.Size, Items.Count);

                // The adapter stores the persistent asset identifier in the selection store.
                Selection.UserData = this;
                Selection.AdapterIndexToStorageId = static (self, idx) => ((ExampleAssetsBrowser)self.UserData!).Items[idx].Id;
                Selection.ApplyRequests(msIo);

                bool wantDelete = (ImGui.Shortcut(ImGuiKey.Delete, ImGuiInputFlags.Repeat) && Selection.Size > 0) || RequestDelete;
                int itemCurrIdxToFocus = wantDelete ? Selection.ApplyDeletionPreLoop(msIo, Items.Count) : -1;
                RequestDelete = false;

                // The selectable item spacing visually fills the space between icons, while the hit
                // gaps stay uncovered so box selection can start there.
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(LayoutSelectableSpacing, LayoutSelectableSpacing));

                uint[] iconTypeOverlayColors = [0u, 0xFF4646C8u, 0xFF46AA46u]; // IM_COL32(200,70,70,255), IM_COL32(70,170,70,255)
                uint iconBgColor = ImGui.GetColorU32(0xDC232323u); // IM_COL32(35,35,35,220)
                ImVec2 iconTypeOverlaySize = new(4.0f, 4.0f);
                bool displayLabel = LayoutItemSize.X >= ImGui.CalcTextSize("999").X;

                int columnCount = LayoutColumnCount;
                ImGuiListClipper clipper = new();
                clipper.Begin(LayoutLineCount, LayoutItemStep.Y);
                if (itemCurrIdxToFocus != -1)
                    clipper.IncludeItemByIndex(itemCurrIdxToFocus / columnCount);
                if (msIo.RangeSrcItem != -1)
                    clipper.IncludeItemByIndex((int)msIo.RangeSrcItem / columnCount);
                while (clipper.Step())
                {
                    for (int lineIdx = clipper.DisplayStart; lineIdx < clipper.DisplayEnd; lineIdx++)
                    {
                        int itemMinIdxForCurrentLine = lineIdx * columnCount;
                        int itemMaxIdxForCurrentLine = Math.Min((lineIdx + 1) * columnCount, Items.Count);
                        for (int itemIdx = itemMinIdxForCurrentLine; itemIdx < itemMaxIdxForCurrentLine; ++itemIdx)
                        {
                            ExampleAsset itemData = Items[itemIdx];
                            ImGui.PushID((int)itemData.Id);

                            ImVec2 pos = new(startPos.X + ((itemIdx % columnCount) * LayoutItemStep.X), startPos.Y + (lineIdx * LayoutItemStep.Y));
                            ImGui.SetCursorScreenPos(pos);

                            ImGui.SetNextItemSelectionUserData(itemIdx);
                            bool itemIsSelected = Selection.Contains(itemData.Id);
                            bool itemIsVisible = ImGui.IsRectVisible(LayoutItemSize);
                            ImGui.Selectable("", itemIsSelected, ImGuiSelectableFlags.None, LayoutItemSize);

                            // The toggled state is applied immediately so the label and icon colors
                            // reflect the click before EndMultiSelect() returns its requests.
                            if (ImGui.IsItemToggledSelection())
                                itemIsSelected = !itemIsSelected;

                            if (itemCurrIdxToFocus == itemIdx)
                                ImGui.SetKeyboardFocusHere(-1);

                            if (ImGui.BeginDragDropSource())
                            {
                                // The payload carries the full selection, or the single unselected
                                // item that SelectOnClickRelease makes draggable.
                                if (ImGui.GetDragDropPayload() == null)
                                {
                                    List<uint> payloadItems = new();
                                    if (!itemIsSelected)
                                    {
                                        payloadItems.Add(itemData.Id);
                                    }
                                    else
                                    {
                                        int opaqueIt = -1;
                                        while (Selection.GetNextSelectedItem(ref opaqueIt, out uint id))
                                            payloadItems.Add(id);
                                    }
                                    ImGui.SetDragDropPayload("ASSETS_BROWSER_ITEMS", payloadItems.ToArray());
                                }

                                // The canonical payload size is read back from the payload data; the
                                // managed payload exposes the identifier array instead.
                                ImGuiPayload? payload = ImGui.GetDragDropPayload();
                                int payloadCount = payload?.ObjectData is uint[] payloadIds ? payloadIds.Length : 0;
                                ImGui.Text("%d assets", payloadCount);

                                ImGui.EndDragDropSource();
                            }

                            // Rendering is coarse-clipped as well, because two-dimensional box
                            // selection can make the clipping rectangle larger than the icon cell.
                            if (itemIsVisible)
                            {
                                ImVec2 boxMin = new(pos.X - 1, pos.Y - 1);
                                ImVec2 boxMax = new(boxMin.X + LayoutItemSize.X + 2, boxMin.Y + LayoutItemSize.Y + 2);
                                drawList.AddRectFilled(boxMin, boxMax, iconBgColor);
                                if (ShowTypeOverlay && itemData.Type != 0)
                                {
                                    uint typeCol = iconTypeOverlayColors[itemData.Type % iconTypeOverlayColors.Length];
                                    drawList.AddRectFilled(
                                        new ImVec2(boxMax.X - 2 - iconTypeOverlaySize.X, boxMin.Y + 2),
                                        new ImVec2(boxMax.X - 2, boxMin.Y + 2 + iconTypeOverlaySize.Y),
                                        typeCol);
                                }
                                if (displayLabel)
                                {
                                    uint labelCol = ImGui.GetColorU32(itemIsSelected ? ImGuiCol.Text : ImGuiCol.TextDisabled);
                                    string label = string.Format(CultureInfo.InvariantCulture, "{0}", itemData.Id);
                                    drawList.AddText(new ImVec2(boxMin.X, boxMax.Y - ImGui.GetFontSize()), labelCol, label);
                                }
                            }

                            ImGui.PopID();
                        }
                    }
                }
                clipper.End();
                if (Items.Count == 0)
                    ImGui.Dummy(new ImVec2(0, 0));
                ImGui.PopStyleVar();

                if (ImGui.BeginPopupContextWindow())
                {
                    ImGui.Text("Selection: %d items", Selection.Size);
                    ImGui.Separator();
                    if (ImGui.MenuItem("Delete", "Del", false, Selection.Size > 0))
                        RequestDelete = true;
                    ImGui.EndPopup();
                }

                msIo = ImGui.EndMultiSelect();
                Selection.ApplyRequests(msIo);
                if (wantDelete)
                    Selection.ApplyDeletionPostLoop(msIo, Items, itemCurrIdxToFocus);

                // Ctrl+Wheel zooms the icons while keeping the item under the mouse in place.
                if (ImGui.IsWindowAppearing())
                    ZoomWheelAccum = 0.0f;
                if (ImGui.IsWindowHovered() && io.MouseWheel != 0.0f && ImGui.IsKeyDown(ImGuiKey.ImGuiMod_Ctrl) && !ImGui.IsAnyItemActive())
                {
                    ZoomWheelAccum += io.MouseWheel;
                    if (MathF.Abs(ZoomWheelAccum) >= 1.0f)
                    {
                        // The hovered index is recovered from the mouse position in grid space.
                        float hoveredItemNx = (io.MousePos.X - startPos.X + (LayoutItemSpacing * 0.5f)) / LayoutItemStep.X;
                        float hoveredItemNy = (io.MousePos.Y - startPos.Y + (LayoutItemSpacing * 0.5f)) / LayoutItemStep.Y;
                        int hoveredItemIdx = ((int)hoveredItemNy * LayoutColumnCount) + (int)hoveredItemNx;

                        IconSize *= MathF.Pow(1.1f, (int)ZoomWheelAccum);
                        IconSize = Math.Clamp(IconSize, 16.0f, 128.0f);
                        ZoomWheelAccum -= (int)ZoomWheelAccum;
                        UpdateLayoutSizes(availWidth);

                        // The scroll offset is adjusted so the hovered item stays under the mouse in
                        // the next frame's layout.
                        // The remainder operator matches C's fmodf, including the sign of the dividend.
                        float hoveredItemRelPosY = ((float)(hoveredItemIdx / LayoutColumnCount) + (hoveredItemNy % 1.0f)) * LayoutItemStep.Y;
                        hoveredItemRelPosY += ImGui.GetStyle().WindowPadding.Y;
                        float mouseLocalY = io.MousePos.Y - ImGui.GetWindowPos().Y;
                        ImGui.SetScrollY(hoveredItemRelPosY - mouseLocalY);
                    }
                }
            }
            ImGui.EndChild();

            ImGui.Text("Selected: %d/%d items", Selection.Size, Items.Count);
            ImGui.End();
        }
    }

    /// <summary>The persistent state of the Assets Browser example window.</summary>
    /// <remarks>
    /// Ports the canonical function-local <c>static ExampleAssetsBrowser assets_browser</c>
    /// </remarks>
    private static readonly ExampleAssetsBrowser s_assetsBrowser = new();

    /// <summary>
    /// Demonstrates a zoomable, box-selectable icon grid backed by a multi-select store, a list
    /// clipper, and a single-header sorting table.
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    /// <remarks>
    /// Ports canonical <c>ShowExampleAppAssetsBrowser</c>
    ///. The canonical <c>IMGUI_DEMO_MARKER</c> has
    /// no counterpart in the port.
    /// </remarks>
    public static void ShowExampleAppAssetsBrowser(ref bool pOpen)
    {
        s_assetsBrowser.Draw("Example: Assets Browser", ref pOpen);
    }

    #endregion
}