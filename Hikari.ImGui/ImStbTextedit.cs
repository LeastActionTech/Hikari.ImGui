#nullable enable
// ROLE IN ENGINE
//   Pure C# port of the text-editing state machine that Dear ImGui bundles as imstb_textedit.h.
//   It maps mouse and keyboard input onto insertions, deletions, cursor movement, selection changes,
//   and undo/redo records for one active InputText() field. The port carries no display or rendering
//   logic: the host draws from the cursor and selection offsets it maintains here.
//
// SUBSYSTEM CONNECTIONS & DATA FLOW
//
//   ┌───────────────────────────────────────────────┐
//   │ ImGuiInputTextState / ImStbTexteditState      │
//   │ • UTF-8 byte buffer, cursor, selection, undo  │
//   └───────────────────────────────────────────────┘
//                          │ key / click / drag / text
//                          ▼
//   ┌───────────────────────────────────────────────┐
//   │ ImStbTextedit                                 │
//   │ • key, drag and undo state machine            │
//   │ • layout and word-boundary hooks              │
//   └───────────────────────────────────────────────┘
//                          │ mutated buffer and offsets
//                          ▼
//   ┌───────────────────────────────────────────────┐
//   │ InputText() host (cursor drawing, scrolling)  │
//   └───────────────────────────────────────────────┘
//
// STYLIZED USAGE EXAMPLE
//
//   var state = new ImGuiInputTextState { Ctx = ctx, BufCapacity = 64 };
//   state.TextA.AddRange("hello"u8.ToArray());
//   state.TextA.Add(0);
//   state.TextLen = 5;
//   ImStbTextedit.InitializeState(state.Stb, isSingleLine: true);
//   ImStbTextedit.Click(state, 12.0f, 0.0f);
//   ImStbTextedit.Key(state, ImStbTextedit.KeyRight);
//   ImStbTextedit.Text(state, "!"u8);
//
// THREADING & LIFECYCLE NOTES
//   The state machine is not thread-safe. The owning context drives it from the UI thread, and exactly
//   one ImGuiInputTextState is edited at a time. No method allocates on the heap except when the host
//   buffer is resized for a resizable field, so the key and drag paths stay allocation-free.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Hikari.ImGui;

/// <summary>
/// Pure C# port of the stb_textedit text-editing state machine as configured by Dear ImGui.
/// </summary>
/// <remarks>
/// <para>
/// <b>Buffer and state design.</b> The canonical <c>STB_TEXTEDIT_STRING</c> is
/// <c>ImGuiInputTextState</c> and the canonical editor state is <c>STB_TexteditState</c>. This port
/// keeps the same split: the text lives in the existing <see cref="ImGuiInputTextState.TextA"/> byte
/// list with length <see cref="ImGuiInputTextState.TextLen"/>, and the editor state is the existing
/// <see cref="ImStbTexteditState"/> owned by <see cref="ImGuiInputTextState.Stb"/>. All positions are
/// UTF-8 <i>byte</i> offsets, exactly as canonical's <c>char*</c> offsets are, because canonical
/// defines <c>STB_TEXTEDIT_CHARTYPE</c> as <c>char</c> and reads a UTF-8 buffer. No text is ever
/// converted to a managed string, so no per-call allocation occurs on the key or drag paths.
/// </para>
/// <para>
/// <b>Host configuration deviations.</b> The task briefing assumed
/// <c>#define STB_TEXTEDIT_KEYTYPE ImGuiKey</c>; the definitive checkout does not define it, so the
/// key type is the canonical <c>int</c> (<c>imstb_textedit.h:792-794</c>,
/// <c>imgui_internal.h:1304</c> <c>OnKeyPressed(int)</c>). The key constants are the exact
/// <c>0x200000</c>-based values from <c>imgui_widgets.cpp:4271-4287</c>. Dear ImGui deliberately
/// leaves <c>STB_TEXTEDIT_KEYTOTEXT</c> undefined and does not use <c>STB_TEXTEDIT_IS_SPACE</c>
/// (<c>imgui_widgets.cpp:4042-4044</c>); character input arrives through <see cref="Text"/>, which
/// corresponds to canonical <c>stb_textedit_text</c>. The optional hooks are provided for interface
/// completeness and documented as inert in this configuration. Word movement likewise follows the
/// Dear ImGui overrides <c>STB_TEXTEDIT_MOVEWORDLEFT_IMPL</c>/<c>STB_TEXTEDIT_MOVEWORDRIGHT_IMPL</c>
/// (<c>imgui_widgets.cpp:4130-4155</c>) rather than the canonical optional default handlers, so it is
/// separator-aware and platform-dependent.
/// </para>
/// <para>
/// <b>Layout hooks.</b> Canonical's <c>InputTextCalcTextSize</c> measures the UTF-8 buffer directly
/// (<c>imgui_widgets.cpp:4032-4062</c>). This port's <see cref="ImFont.CalcTextSizeA"/> is UTF-16
/// based, so <see cref="LayoutRow"/> and <see cref="GetWidth"/> measure the UTF-8 bytes through
/// <see cref="ImFontBaked.GetCharAdvance"/> using the same advance scaling and word-wrap classifier
/// as canonical <c>ImFontCalcTextSizeEx</c> (<c>imgui_draw.cpp:5673-5762</c>) and
/// <c>ImFontCalcWordWrapPositionEx</c> (<c>imgui_draw.cpp:5553-5666</c>). The wrap width used is the
/// host-provided <see cref="ImGuiInputTextState.WrapWidth"/>, which the canonical host computes with
/// <c>ImGui.CalcWrapWidthForPos</c> before entering the state machine.
/// </para>
/// <para>
/// The canonical header is <c>ThirdParty/imgui/imstb_textedit.h</c> at commit <c>b48d1af</c>, and the
/// host integration is <c>ThirdParty/imgui/imgui_widgets.cpp:3998-4600</c>. Every member cites the
/// canonical line range it ports.
/// </para>
/// </remarks>
internal static class ImStbTextedit
{
    #region Key constants and undo limits

    /// <summary>Key input moving the cursor one character left (<c>imgui_widgets.cpp:4271</c>).</summary>
    public const int KeyLeft = 0x200000;

    /// <summary>Key input moving the cursor one character right (<c>imgui_widgets.cpp:4272</c>).</summary>
    public const int KeyRight = 0x200001;

    /// <summary>Key input moving the cursor one row up (<c>imgui_widgets.cpp:4273</c>).</summary>
    public const int KeyUp = 0x200002;

    /// <summary>Key input moving the cursor one row down (<c>imgui_widgets.cpp:4274</c>).</summary>
    public const int KeyDown = 0x200003;

    /// <summary>Key input moving the cursor to the start of its visual line (<c>imgui_widgets.cpp:4275</c>).</summary>
    public const int KeyLineStart = 0x200004;

    /// <summary>Key input moving the cursor to the end of its visual line (<c>imgui_widgets.cpp:4276</c>).</summary>
    public const int KeyLineEnd = 0x200005;

    /// <summary>Key input moving the cursor to the start of the whole text (<c>imgui_widgets.cpp:4277</c>).</summary>
    public const int KeyTextStart = 0x200006;

    /// <summary>Key input moving the cursor to the end of the whole text (<c>imgui_widgets.cpp:4278</c>).</summary>
    public const int KeyTextEnd = 0x200007;

    /// <summary>Key input deleting the selection or the character under the cursor (<c>imgui_widgets.cpp:4279</c>).</summary>
    public const int KeyDelete = 0x200008;

    /// <summary>Key input deleting the selection or the character left of the cursor (<c>imgui_widgets.cpp:4280</c>).</summary>
    public const int KeyBackspace = 0x200009;

    /// <summary>Key input performing one undo step (<c>imgui_widgets.cpp:4281</c>).</summary>
    public const int KeyUndo = 0x20000A;

    /// <summary>Key input performing one redo step (<c>imgui_widgets.cpp:4282</c>).</summary>
    public const int KeyRedo = 0x20000B;

    /// <summary>Key input moving the cursor one word left (<c>imgui_widgets.cpp:4283</c>).</summary>
    public const int KeyWordLeft = 0x20000C;

    /// <summary>Key input moving the cursor one word right (<c>imgui_widgets.cpp:4284</c>).</summary>
    public const int KeyWordRight = 0x20000D;

    /// <summary>Key input moving the cursor one page up (<c>imgui_widgets.cpp:4285</c>).</summary>
    public const int KeyPageUp = 0x20000E;

    /// <summary>Key input moving the cursor one page down (<c>imgui_widgets.cpp:4286</c>).</summary>
    public const int KeyPageDown = 0x20000F;

    /// <summary>Bit or-ed into a movement key to extend the selection (<c>imgui_widgets.cpp:4287</c>).</summary>
    public const int KeyShift = 0x400000;

    /// <summary>Number of undo records, matching <c>IMSTB_TEXTEDIT_UNDOSTATECOUNT</c> (<c>imstb_textedit.h:312</c>).</summary>
    private const int UndoStateCount = 99;

    /// <summary>Number of characters stored by the undo system, matching <c>IMSTB_TEXTEDIT_UNDOCHARCOUNT</c> (<c>imstb_textedit.h:315</c>).</summary>
    private const int UndoCharCount = 999;

    /// <summary>Width returned for a newline so up/down movement stops at the row break (<c>imgui_internal.h:1265</c>).</summary>
    private const float GetWidthNewline = -1.0f;

    /// <summary>Sentinel meaning "no valid undo record or character storage" (<c>imstb_textedit.h:1273</c>).</summary>
    private const int NoUndoRecord = -1;

    #endregion

    #region Layout result types

    /// <summary>
    /// One laid-out row of characters; mirrors canonical <c>StbTexteditRow</c>
    /// (<c>imstb_textedit.h:389-395</c>).
    /// </summary>
    private struct TexteditRow
    {
        /// <summary>Starting x location of the row.</summary>
        public float X0;

        /// <summary>Ending x location of the row.</summary>
        public float X1;

        /// <summary>Baseline offset relative to the previous row's baseline.</summary>
        public float BaselineYDelta;

        /// <summary>Row height above the baseline.</summary>
        public float YMin;

        /// <summary>Row height below the baseline.</summary>
        public float YMax;

        /// <summary>Number of bytes consumed by this row.</summary>
        public int NumChars;
    }

    /// <summary>
    /// Character location plus previous-row bookkeeping; mirrors canonical <c>StbFindState</c>
    /// (<c>imstb_textedit.h:553-559</c>).
    /// </summary>
    private struct FindState
    {
        /// <summary>X location of the target character within its row.</summary>
        public float X;

        /// <summary>Y location of the target row's top.</summary>
        public float Y;

        /// <summary>Height of the target row.</summary>
        public float Height;

        /// <summary>First byte of the row containing the target character.</summary>
        public int FirstChar;

        /// <summary>Number of bytes in the row containing the target character.</summary>
        public int Length;

        /// <summary>First byte of the previous row, used by up/page-up movement.</summary>
        public int PrevFirst;
    }

    #endregion

    #region Host hooks: text buffer access

    /// <summary>
    /// Returns the UTF-8 byte length of the edited text; the <c>STB_TEXTEDIT_STRINGLEN</c> hook
    /// (<c>imgui_widgets.cpp:4047</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <returns>The number of UTF-8 bytes in <see cref="ImGuiInputTextState.TextA"/>.</returns>
    private static int StringLen(ImGuiInputTextState obj)
    {
        return obj.TextLen;
    }

    /// <summary>
    /// Returns the byte at a position, or zero at and beyond the end; the <c>STB_TEXTEDIT_GETCHAR</c>
    /// hook (<c>imgui_widgets.cpp:4048</c>). Canonical asserts <c>idx &lt;= TextLen</c> and reads the
    /// zero terminator at <c>TextLen</c>; this port returns zero rather than reading past the buffer.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Zero-based byte offset.</param>
    /// <returns>The byte value, or zero when the offset is out of range.</returns>
    private static byte GetChar(ImGuiInputTextState obj, int idx)
    {
        return idx >= 0 && idx < obj.TextA.Count ? obj.TextA[idx] : (byte)0;
    }

    /// <summary>
    /// Deletes <paramref name="count"/> bytes at <paramref name="pos"/> and marks the field edited;
    /// the <c>STB_TEXTEDIT_DELETECHARS</c> hook (<c>imgui_widgets.cpp:4226-4235</c>). The canonical
    /// <c>memmove</c> also copies the zero terminator; this port copies the remaining bytes and then
    /// re-writes the terminator.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="pos">First byte offset to remove.</param>
    /// <param name="count">Number of bytes to remove; the call is a no-op when zero or negative.</param>
    private static void DeleteChars(ImGuiInputTextState obj, int pos, int count)
    {
        if (count <= 0)
        {
            return;
        }

        int textLen = obj.TextLen;
        List<byte> text = obj.TextA;
        for (int i = pos; i < textLen - count; i++)
        {
            text[i] = text[i + count];
        }

        obj.EditedBefore = true;
        obj.EditedThisFrame = true;
        obj.TextLen = textLen - count;

        EnsureBufferLength(text, obj.TextLen + 1);
        text[obj.TextLen] = 0;
    }

    /// <summary>
    /// Inserts the leading bytes of <paramref name="newText"/> at <paramref name="pos"/> and returns
    /// how many bytes were inserted; the <c>STB_TEXTEDIT_INSERTCHARS</c> hook
    /// (<c>imgui_widgets.cpp:4237-4268</c>). A non-resizable field truncates the insertion to the
    /// closest UTF-8 codepoint boundary within the user buffer capacity.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="pos">Byte offset at which to insert; must not exceed <see cref="ImGuiInputTextState.TextLen"/>.</param>
    /// <param name="newText">UTF-8 bytes to insert; only a leading prefix may be inserted.</param>
    /// <returns>The number of bytes actually inserted, which is zero when nothing fits.</returns>
    private static int InsertChars(ImGuiInputTextState obj, int pos, ReadOnlySpan<byte> newText)
    {
        bool isResizable = (obj.Flags & ImGuiInputTextFlags.CallbackResize) != 0;
        int textLen = obj.TextLen;
        int newTextLen = newText.Length;

        // We support partial insertion (with a mod in stb_textedit.h).
        int avail = obj.BufCapacity - 1 - textLen;
        if (!isResizable && newTextLen > avail)
        {
            newTextLen = FindValidUtf8CodepointEnd(newText, avail);
        }

        if (newTextLen == 0)
        {
            return 0;
        }

        List<byte> text = obj.TextA;
        if (isResizable && textLen + newTextLen + 1 > text.Capacity)
        {
            text.Capacity = textLen + Math.Clamp(newTextLen, 32, Math.Max(256, newTextLen)) + 1;
        }

        EnsureBufferLength(text, textLen + newTextLen + 1);

        // memmove(text + pos + new_text_len, text + pos, text_len - pos), then memcpy.
        for (int i = textLen; i > pos; i--)
        {
            text[i + newTextLen - 1] = text[i - 1];
        }

        for (int i = 0; i < newTextLen; i++)
        {
            text[pos + i] = newText[i];
        }

        obj.EditedBefore = true;
        obj.EditedThisFrame = true;
        obj.TextLen = textLen + newTextLen;
        text[obj.TextLen] = 0;

        return newTextLen;
    }

    /// <summary>
    /// Grows a managed list with zeros until it holds at least <paramref name="length"/> elements, so
    /// the raw-buffer byte writes that mirror canonical <c>memmove</c> stay in range.
    /// </summary>
    /// <param name="buffer">Buffer to grow.</param>
    /// <param name="length">Required element count.</param>
    private static void EnsureBufferLength(List<byte> buffer, int length)
    {
        while (buffer.Count < length)
        {
            buffer.Add(0);
        }
    }

    /// <summary>
    /// Returns the state backing the edited text; canonical accesses <c>str-&gt;Stb</c> directly in
    /// several hooks (<c>imstb_textedit.h:588</c>, <c>imgui_widgets.cpp:4332-4336</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <returns>The embedded editor state.</returns>
    private static ImStbTexteditState Stb(ImGuiInputTextState obj)
    {
        return obj.Stb;
    }

    #endregion

    #region Host hooks: UTF-8 character stepping

    /// <summary>
    /// Decodes the codepoint starting at a byte offset. Canonical uses
    /// <c>ImTextCharFromUtf8</c> over the UTF-8 buffer (<c>imgui_widgets.cpp:4049</c>,
    /// <c>4083-4153</c>); this port reuses <see cref="ImFont.ImTextCharFromUtf8"/>.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Byte offset of the codepoint; at or beyond the length the terminator decodes as zero.</param>
    /// <returns>The decoded codepoint, or U+FFFD for malformed input.</returns>
    private static uint DecodeAt(ImGuiInputTextState obj, int idx)
    {
        if (idx >= obj.TextLen)
        {
            return 0;
        }

        int i = idx;
        ImFont.ImTextCharFromUtf8(CollectionsMarshal.AsSpan(obj.TextA), ref i, out uint c);
        return c;
    }

    /// <summary>
    /// Returns the number of UTF-8 bytes consumed by the codepoint starting at a byte offset.
    /// Canonical <c>IMSTB_TEXTEDIT_GETNEXTCHARINDEX_IMPL</c> uses the same decode
    /// (<c>imgui_widgets.cpp:4067-4073</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Byte offset of the codepoint.</param>
    /// <returns>The number of bytes the codepoint occupies, at least one.</returns>
    private static int DecodeConsumed(ImGuiInputTextState obj, int idx)
    {
        int i = idx;
        int consumed = ImFont.ImTextCharFromUtf8(CollectionsMarshal.AsSpan(obj.TextA), ref i, out _);
        return consumed > 0 ? consumed : 1;
    }

    /// <summary>
    /// Returns the byte offset after the codepoint at <paramref name="idx"/>; canonical
    /// <c>IMSTB_TEXTEDIT_GETNEXTCHARINDEX_IMPL</c> (<c>imgui_widgets.cpp:4067-4073</c>). Positions at
    /// or beyond the end return <c>TextLen + 1</c>, which callers clamp.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Byte offset of a codepoint start.</param>
    /// <returns>The next codepoint start, or <c>TextLen + 1</c> past the end.</returns>
    private static int NextCharIndex(ImGuiInputTextState obj, int idx)
    {
        int len = obj.TextLen;
        if (idx >= len)
        {
            return len + 1;
        }

        return idx + DecodeConsumed(obj, idx);
    }

    /// <summary>
    /// Returns the start of the codepoint before <paramref name="idx"/>; canonical
    /// <c>IMSTB_TEXTEDIT_GETPREVCHARINDEX_IMPL</c> and <c>ImTextFindPreviousUtf8Codepoint</c>
    /// (<c>imgui_widgets.cpp:4075-4081</c>, <c>imgui.cpp:2882-2891</c>). Returns -1 at or before the
    /// start of the text.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Byte offset to step back from.</param>
    /// <returns>The previous codepoint start, or -1 when none exists.</returns>
    private static int PrevCharIndex(ImGuiInputTextState obj, int idx)
    {
        if (idx <= 0)
        {
            return -1;
        }

        int p = idx;
        while (p > 0)
        {
            p--;
            if ((GetChar(obj, p) & 0xC0) != 0x80)
            {
                return p;
            }
        }

        return 0;
    }

    /// <summary>
    /// Truncates a byte range to the closest valid UTF-8 codepoint boundary; ports
    /// <c>ImTextFindValidUtf8CodepointEnd</c> (<c>imgui.cpp:2893-2903</c>, used by
    /// <c>imgui_widgets.cpp:4246</c>). A limit inside a codepoint backs up to that codepoint's start.
    /// </summary>
    /// <param name="text">Candidate insertion bytes.</param>
    /// <param name="at">Requested byte limit.</param>
    /// <returns>The valid byte limit, which never exceeds <paramref name="text"/>.</returns>
    private static int FindValidUtf8CodepointEnd(ReadOnlySpan<byte> text, int at)
    {
        if (at <= 0)
        {
            return 0;
        }

        if (at >= text.Length)
        {
            return text.Length;
        }

        int prev = at;
        while (prev > 0)
        {
            prev--;
            if ((text[prev] & 0xC0) != 0x80)
            {
                break;
            }
        }

        int i = prev;
        ImFont.ImTextCharFromUtf8(text, ref i, out uint c);
        int consumed = i - prev;
        if (c != 0xFFFD && consumed <= at - prev)
        {
            return at;
        }

        return prev;
    }

    #endregion

    #region Host hooks: layout and measurement

    /// <summary>
    /// Returns the baked font the host measures with; canonical reads <c>g.FontBaked</c>
    /// (<c>imgui_widgets.cpp:4049</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <returns>The baked font, or null when no frame has resolved one.</returns>
    private static ImFontBaked? GetBaked(ImGuiInputTextState obj)
    {
        // Canonical GETWIDTH reads g.FontBaked, which NewFrame only resolves while a window is
        // current. InputText always runs inside a window, so canonical never observes null; the
        // explicit fallback keeps the hook usable by a headless host and does not change in-window
        // behavior.
        ImFontBaked? baked = obj.Ctx?.FontBaked ?? ImGui.GetFontBaked();
        if (baked == null)
        {
            ImFont? font = obj.Ctx?.Font ?? ImGui.GetFont();
            baked = font?.GetFontBaked(ImGui.GetFontSize());
        }

        return baked;
    }

    /// <summary>
    /// Returns the scale from baked-font units to pixels; canonical multiplies by
    /// <c>g.FontBakedScale</c> (<c>imgui_widgets.cpp:4049</c>, <c>imgui.cpp:1288</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="baked">Baked font the advance is read from.</param>
    /// <returns>The pixel scale factor.</returns>
    private static float GetScale(ImGuiInputTextState obj, ImFontBaked baked)
    {
        if (obj.Ctx != null && obj.Ctx.FontBakedScale > 0.0f)
        {
            return obj.Ctx.FontBakedScale;
        }

        return baked.Size != 0.0f ? ImGui.GetFontSize() / baked.Size : 0.0f;
    }

    /// <summary>
    /// Lays out one row and reports its width, height, and byte length; the
    /// <c>STB_TEXTEDIT_LAYOUTROW</c> hook (<c>imgui_widgets.cpp:4051-4062</c>).
    /// </summary>
    /// <param name="row">Receives the laid-out row.</param>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="lineStartIdx">Byte offset where the row starts.</param>
    private static void LayoutRow(ref TexteditRow row, ImGuiInputTextState obj, int lineStartIdx)
    {
        ImVec2 size = CalcTextSizeEx(
            obj,
            lineStartIdx,
            ImDrawTextFlags.StopOnNewLine | ImDrawTextFlags.WrapKeepBlanks,
            out int remaining);

        row.X0 = 0.0f;
        row.X1 = size.X;
        row.BaselineYDelta = size.Y;
        row.YMin = 0.0f;
        row.YMax = size.Y;
        row.NumChars = remaining - lineStartIdx;
    }

    /// <summary>
    /// Returns the pixel advance from one character to the next on a row; the
    /// <c>STB_TEXTEDIT_GETWIDTH</c> hook (<c>imgui_widgets.cpp:4049</c>). A newline reports
    /// <see cref="GetWidthNewline"/> so row-walking loops stop at the break.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="lineStartIdx">Byte offset where the row starts.</param>
    /// <param name="charIdx">Byte offset of the character within the row.</param>
    /// <returns>The advance in pixels, or <see cref="GetWidthNewline"/> for a newline.</returns>
    private static float GetWidth(ImGuiInputTextState obj, int lineStartIdx, int charIdx)
    {
        uint c = DecodeAt(obj, lineStartIdx + charIdx);
        if (c == '\n')
        {
            return GetWidthNewline;
        }

        ImFontBaked? baked = GetBaked(obj);
        return baked == null ? 0.0f : baked.GetCharAdvance(c) * GetScale(obj, baked);
    }

    /// <summary>
    /// Measures a UTF-8 byte run with a hard width limit, reporting how many bytes were consumed;
    /// ports <c>ImFontCalcTextSizeEx</c> as invoked by <c>InputTextCalcTextSize</c>
    /// (<c>imgui_draw.cpp:5673-5762</c>, <c>imgui_widgets.cpp:4032-4038</c>). The port's own
    /// <see cref="ImFont.CalcTextSizeA"/> is UTF-16 based, so this hook walks the UTF-8 bytes directly.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="start">First byte offset to measure.</param>
    /// <param name="flags">Wrap behavior flags; <see cref="ImDrawTextFlags.StopOnNewLine"/> ends the run at the first newline.</param>
    /// <param name="remaining">Receives the byte offset just past the measured run.</param>
    /// <returns>The measured width and height in pixels.</returns>
    private static ImVec2 CalcTextSizeEx(ImGuiInputTextState obj, int start, ImDrawTextFlags flags, out int remaining)
    {
        int textEnd = obj.TextLen;
        ImFontBaked? baked = GetBaked(obj);
        float lineHeight = ImGui.GetFontSize();
        float scale = baked == null ? 0.0f : GetScale(obj, baked);

        float textSizeX = 0.0f;
        float textSizeY = 0.0f;
        float lineWidth = 0.0f;

        bool wrapEnabled = obj.WrapWidth > 0.0f;
        int wrapEol = -1;

        int s = start;
        int prevS = start;
        while (s < textEnd)
        {
            if (wrapEnabled)
            {
                if (wrapEol < 0)
                {
                    wrapEol = CalcWordWrapPosition(obj, s, textEnd, obj.WrapWidth - lineWidth, flags);
                }

                if (s >= wrapEol)
                {
                    if (textSizeX < lineWidth)
                    {
                        textSizeX = lineWidth;
                    }

                    textSizeY += lineHeight;
                    lineWidth = 0.0f;
                    s = WrapNextLineStart(obj, s, textEnd, flags);
                    if ((flags & ImDrawTextFlags.StopOnNewLine) != 0)
                    {
                        break;
                    }

                    wrapEol = -1;
                    continue;
                }
            }

            prevS = s;
            uint c = DecodeAt(obj, s);
            s += DecodeConsumed(obj, s);

            if (c == '\n')
            {
                textSizeX = MathF.Max(textSizeX, lineWidth);
                textSizeY += lineHeight;
                lineWidth = 0.0f;
                if ((flags & ImDrawTextFlags.StopOnNewLine) != 0)
                {
                    break;
                }

                continue;
            }

            if (c == '\r')
            {
                continue;
            }

            float charWidth = baked == null ? 0.0f : baked.GetCharAdvance(c) * scale;
            if (lineWidth + charWidth >= float.MaxValue)
            {
                s = prevS;
                break;
            }

            lineWidth += charWidth;
        }

        if (textSizeX < lineWidth)
        {
            textSizeX = lineWidth;
        }

        if (lineWidth > 0.0f || textSizeY == 0.0f)
        {
            textSizeY += lineHeight;
        }

        remaining = s;
        return new ImVec2(textSizeX, textSizeY);
    }

    /// <summary>
    /// Advances past trailing blanks and a newline at a wrap boundary; ports
    /// <c>ImTextCalcWordWrapNextLineStart</c> (<c>imgui_draw.cpp:5491-5499</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="at">Current byte offset.</param>
    /// <param name="textEnd">End of the text being wrapped.</param>
    /// <param name="flags">Wrap behavior flags; <see cref="ImDrawTextFlags.WrapKeepBlanks"/> preserves blanks.</param>
    /// <returns>The next line start offset.</returns>
    private static int WrapNextLineStart(ImGuiInputTextState obj, int at, int textEnd, ImDrawTextFlags flags)
    {
        if ((flags & ImDrawTextFlags.WrapKeepBlanks) == 0)
        {
            while (at < textEnd && (GetChar(obj, at) == (byte)' ' || GetChar(obj, at) == (byte)'\t'))
            {
                at++;
            }
        }

        if (at < textEnd && GetChar(obj, at) == (byte)'\n')
        {
            at++;
        }

        return at;
    }

    /// <summary>
    /// Returns the byte offset where wrapping should break; ports
    /// <c>ImFontCalcWordWrapPositionEx</c> (<c>imgui_draw.cpp:5553-5666</c>), including its separator
    /// classification (<c>imgui_draw.cpp:5533-5548</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="start">First byte offset of the candidate line.</param>
    /// <param name="textEnd">End of the text being wrapped.</param>
    /// <param name="wrapWidth">Remaining horizontal space in pixels.</param>
    /// <param name="flags">Wrap behavior flags.</param>
    /// <returns>The absolute byte offset of the wrap position.</returns>
    private static int CalcWordWrapPosition(ImGuiInputTextState obj, int start, int textEnd, float wrapWidth, ImDrawTextFlags flags)
    {
        ImFontBaked? baked = GetBaked(obj);
        if (baked == null)
        {
            return start;
        }

        float scale = GetScale(obj, baked);
        float lineWidth = 0.0f;
        float blankWidth = 0.0f;
        float unscaledWrap = scale != 0.0f ? wrapWidth / scale : 0.0f;

        int prevType = WcharClassOther;
        int spanEnd = start;
        float spanWidth = 0.0f;
        bool keepBlanks = (flags & ImDrawTextFlags.WrapKeepBlanks) != 0;

        int s = start;
        while (s < textEnd)
        {
            int charStart = s;
            uint c = DecodeAt(obj, s);
            s += DecodeConsumed(obj, s);

            if (c < 32)
            {
                if (c == '\n')
                {
                    return charStart;
                }

                if (c == '\r')
                {
                    continue;
                }
            }

            float charWidth = baked.GetCharAdvance(c);
            int currType = ClassifyChar(c);

            if (currType == WcharClassBlank)
            {
                if (prevType != WcharClassBlank && !keepBlanks)
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth;
                    spanWidth = 0.0f;
                }

                blankWidth += charWidth;
            }
            else
            {
                if (prevType == WcharClassPunct && currType != WcharClassPunct && !(c >= '0' && c <= '9'))
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth + blankWidth;
                    spanWidth = blankWidth = 0.0f;
                }
                else if (prevType == WcharClassBlank && keepBlanks)
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth + blankWidth;
                    spanWidth = blankWidth = 0.0f;
                }

                spanWidth += charWidth;
            }

            if (spanWidth + blankWidth + lineWidth > unscaledWrap)
            {
                if (spanWidth + blankWidth > unscaledWrap)
                {
                    break;
                }

                return spanEnd;
            }

            prevType = currType;
        }

        // A too-small wrap width would otherwise produce zero characters per line; force one.
        if (s == start && start < textEnd)
        {
            return start + DecodeConsumed(obj, start);
        }

        return s;
    }

    /// <summary>Character class for a line-breaking blank, matching <c>ImWcharClass_Blank</c>.</summary>
    private const int WcharClassBlank = 1;

    /// <summary>Character class for a line-breaking separator, matching <c>ImWcharClass_Punct</c>.</summary>
    private const int WcharClassPunct = 2;

    /// <summary>Character class for any other character, matching <c>ImWcharClass_Other</c>.</summary>
    private const int WcharClassOther = 0;

    /// <summary>
    /// Classifies a codepoint for word wrapping; reproduces the canonical classifier tables
    /// (<c>imgui_draw.cpp:5529-5548</c>).
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns>One of <see cref="WcharClassBlank"/>, <see cref="WcharClassPunct"/>, or <see cref="WcharClassOther"/>.</returns>
    private static int ClassifyChar(uint c)
    {
        if (c == ' ' || c == '\t' || c == 0x3000)
        {
            return WcharClassBlank;
        }

        if (c == '.' || c == ',' || c == ';' || c == '!' || c == '?' || c == '"' || c == 0x3001 || c == 0x3002)
        {
            return WcharClassPunct;
        }

        return WcharClassOther;
    }

    #endregion

    #region Host hooks: word boundaries

    /// <summary>
    /// Reports whether a codepoint is whitespace; the optional <c>STB_TEXTEDIT_IS_SPACE</c> hook
    /// (<c>imstb_textedit.h:174</c>). Dear ImGui does not use this hook because it overrides word
    /// movement with separator-aware handlers (<c>imgui_widgets.cpp:4042-4044</c>); it is retained so
    /// the port exposes the complete canonical interface.
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns><see langword="true"/> for space, tab, and the CJK ideographic space.</returns>
    private static bool IsSpace(uint c)
    {
        return c == ' ' || c == '\t' || c == 0x3000;
    }

    /// <summary>
    /// Maps a key input to an insertable character; the optional <c>STB_TEXTEDIT_KEYTOTEXT</c> hook
    /// (<c>imstb_textedit.h:144-146</c>). Dear ImGui deliberately leaves it undefined for UTF-8 and
    /// routes characters through <see cref="Text"/> instead (<c>imstb_textedit.h:185-190</c>,
    /// <c>imgui_widgets.cpp:4339-4348</c>); this port therefore maps no keys and returns -1.
    /// </summary>
    /// <param name="key">Key input value.</param>
    /// <returns>Always -1 in this configuration.</returns>
    private static int KeyToText(int key)
    {
        return -1;
    }

    /// <summary>Separator codepoints used by the word-boundary handlers (<c>imgui_widgets.cpp:4083-4095</c>).</summary>
    private static readonly uint[] SeparatorList =
    {
        ',', 0x3001, '.', 0x3002, ';', 0xFF1B, '(', 0xFF08, ')', 0xFF09, '{', 0xFF5B, '}', 0xFF5D,
        '[', 0x300C, ']', 0x300D, '|', 0xFF5C, '!', 0xFF01, '\\', 0xFFE5, '/', 0x30FB, 0xFF0F,
        '\n', '\r',
    };

    /// <summary>
    /// Reports whether a codepoint is one of the hard-coded word separators; ports
    /// <c>ImCharIsSeparatorW</c> (<c>imgui_widgets.cpp:4083-4095</c>).
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns><see langword="true"/> when the codepoint is a separator.</returns>
    private static bool IsSeparator(uint c)
    {
        for (int i = 0; i < SeparatorList.Length; i++)
        {
            if (SeparatorList[i] == c)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether a codepoint is a blank; ports <c>ImCharIsBlankW</c>
    /// (<c>imgui_internal.h:422</c>).
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns><see langword="true"/> for space, tab, and the CJK ideographic space.</returns>
    private static bool IsBlank(uint c)
    {
        return c == ' ' || c == '\t' || c == 0x3000;
    }

    /// <summary>
    /// Reports whether <paramref name="idx"/> is a word boundary when scanning right; ports
    /// <c>is_word_boundary_from_right</c> (<c>imgui_widgets.cpp:4097-4113</c>). Password fields never
    /// report a boundary so the shortcut cannot leak the underlying separators.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Byte offset to test.</param>
    /// <returns>Non-zero when the offset begins a new word.</returns>
    private static int IsWordBoundaryFromRight(ImGuiInputTextState obj, int idx)
    {
        if ((obj.Flags & ImGuiInputTextFlags.Password) != 0 || idx <= 0)
        {
            return 0;
        }

        uint currC = DecodeAt(obj, idx);
        int prevIdx = PrevCharIndex(obj, idx);
        uint prevC = DecodeAt(obj, prevIdx < 0 ? 0 : prevIdx);

        bool prevWhite = IsBlank(prevC);
        bool prevSepar = IsSeparator(prevC);
        bool currWhite = IsBlank(currC);
        bool currSepar = IsSeparator(currC);
        return ((prevWhite || prevSepar) && !(currSepar || currWhite)) || (currSepar && !prevSepar) ? 1 : 0;
    }

    /// <summary>
    /// Reports whether <paramref name="idx"/> is a word boundary when scanning left; ports
    /// <c>is_word_boundary_from_left</c> (<c>imgui_widgets.cpp:4114-4129</c>), including its
    /// intentionally swapped current/previous decode.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Byte offset to test.</param>
    /// <returns>Non-zero when the offset begins a new word.</returns>
    private static int IsWordBoundaryFromLeft(ImGuiInputTextState obj, int idx)
    {
        if ((obj.Flags & ImGuiInputTextFlags.Password) != 0 || idx <= 0)
        {
            return 0;
        }

        // Canonical reads the codepoint at 'idx' into prev_c and the one before it into curr_c.
        uint prevC = DecodeAt(obj, idx);
        int currIdx = PrevCharIndex(obj, idx);
        uint currC = DecodeAt(obj, currIdx < 0 ? 0 : currIdx);

        bool prevWhite = IsBlank(prevC);
        bool prevSepar = IsSeparator(prevC);
        bool currWhite = IsBlank(currC);
        bool currSepar = IsSeparator(currC);
        return ((prevWhite) && !(currSepar || currWhite)) || (currSepar && !prevSepar) ? 1 : 0;
    }

    /// <summary>
    /// Moves the cursor to the previous word start; ports <c>STB_TEXTEDIT_MOVEWORDLEFT_IMPL</c>
    /// (<c>imgui_widgets.cpp:4130-4136</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Current byte offset.</param>
    /// <returns>The previous word-boundary offset, never below zero.</returns>
    private static int MoveWordLeft(ImGuiInputTextState obj, int idx)
    {
        idx = PrevCharIndex(obj, idx);
        while (idx >= 0 && IsWordBoundaryFromRight(obj, idx) == 0)
        {
            idx = PrevCharIndex(obj, idx);
        }

        return idx < 0 ? 0 : idx;
    }

    /// <summary>
    /// Moves the cursor to the next word start for Windows-style word movement; ports
    /// <c>STB_TEXTEDIT_MOVEWORDRIGHT_WIN</c> (<c>imgui_widgets.cpp:4145-4152</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Current byte offset.</param>
    /// <returns>The next word-boundary offset, never beyond the text length.</returns>
    private static int MoveWordRightWindows(ImGuiInputTextState obj, int idx)
    {
        idx = NextCharIndex(obj, idx);
        int len = obj.TextLen;
        while (idx < len && IsWordBoundaryFromRight(obj, idx) == 0)
        {
            idx = NextCharIndex(obj, idx);
        }

        return idx > len ? len : idx;
    }

    /// <summary>
    /// Moves the cursor to the next word start for macOS-style word movement; ports
    /// <c>STB_TEXTEDIT_MOVEWORDRIGHT_MAC</c> (<c>imgui_widgets.cpp:4137-4144</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Current byte offset.</param>
    /// <returns>The next word-boundary offset, never beyond the text length.</returns>
    private static int MoveWordRightMac(ImGuiInputTextState obj, int idx)
    {
        int len = obj.TextLen;
        idx = NextCharIndex(obj, idx);
        while (idx < len && IsWordBoundaryFromLeft(obj, idx) == 0)
        {
            idx = NextCharIndex(obj, idx);
        }

        return idx > len ? len : idx;
    }

    /// <summary>
    /// Moves the cursor to the next word start, selecting the platform behavior; ports
    /// <c>STB_TEXTEDIT_MOVEWORDRIGHT_IMPL</c> (<c>imgui_widgets.cpp:4153</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="idx">Current byte offset.</param>
    /// <returns>The next word-boundary offset.</returns>
    private static int MoveWordRight(ImGuiInputTextState obj, int idx)
    {
        bool macOS = obj.Ctx != null && obj.Ctx.IO.ConfigMacOSXBehaviors;
        return macOS ? MoveWordRightMac(obj, idx) : MoveWordRightWindows(obj, idx);
    }

    #endregion

    #region Line movement hooks

    /// <summary>
    /// Moves the cursor to the start of its visual line, honoring word wrap; ports
    /// <c>STB_TEXTEDIT_MOVELINESTART_IMPL</c> (<c>imgui_widgets.cpp:4158-4192</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; single-line fields always return zero.</param>
    /// <param name="cursor">Current byte offset.</param>
    /// <returns>The line-start byte offset.</returns>
    private static int MoveLineStart(ImGuiInputTextState obj, ImStbTexteditState state, int cursor)
    {
        if (state.SingleLine != 0)
        {
            return 0;
        }

        if (obj.WrapWidth > 0.0f)
        {
            int pBol = MoveLineStartRaw(obj, cursor);
            int p = pBol;
            int textEnd = obj.TextLen;
            while (p >= pBol)
            {
                int pEol = CalcWordWrapPosition(obj, p, textEnd, obj.WrapWidth, ImDrawTextFlags.WrapKeepBlanks);
                if (p == cursor)
                {
                    return pBol;
                }

                if (pEol == cursor && GetChar(obj, cursor) != (byte)'\n' && obj.LastMoveDirectionLR == (sbyte)ImGuiDir.Left)
                {
                    return pBol;
                }

                if (pEol >= cursor)
                {
                    return p;
                }

                p = pEol < textEnd && GetChar(obj, pEol) == (byte)'\n' ? pEol + 1 : pEol;
            }
        }

        // Regular handler, same as stb_textedit_move_line_start().
        while (cursor > 0)
        {
            int prevCursor = PrevCharIndex(obj, cursor);
            if (GetChar(obj, prevCursor) == (byte)'\n')
            {
                break;
            }

            cursor = prevCursor;
        }

        return cursor;
    }

    /// <summary>
    /// Moves the cursor to the end of its visual line, honoring word wrap; ports
    /// <c>STB_TEXTEDIT_MOVELINEEND_IMPL</c> (<c>imgui_widgets.cpp:4194-4221</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; single-line fields always return the text end.</param>
    /// <param name="cursor">Current byte offset.</param>
    /// <returns>The line-end byte offset.</returns>
    private static int MoveLineEnd(ImGuiInputTextState obj, ImStbTexteditState state, int cursor)
    {
        int n = StringLen(obj);
        if (state.SingleLine != 0)
        {
            return n;
        }

        if (obj.WrapWidth > 0.0f)
        {
            int pCursor = cursor;
            int p = MoveLineStartRaw(obj, cursor);
            int textEnd = obj.TextLen;
            while (p < textEnd)
            {
                int pEol = CalcWordWrapPosition(obj, p, textEnd, obj.WrapWidth, ImDrawTextFlags.WrapKeepBlanks);
                cursor = pEol;
                if (pEol == pCursor && obj.LastMoveDirectionLR != (sbyte)ImGuiDir.Left)
                {
                    break;
                }

                if (pEol > pCursor)
                {
                    return cursor;
                }

                p = pEol < textEnd && GetChar(obj, pEol) == (byte)'\n' ? pEol + 1 : pEol;
            }
        }

        // Regular handler, same as stb_textedit_move_line_end().
        while (cursor < n && GetChar(obj, cursor) != (byte)'\n')
        {
            cursor = NextCharIndex(obj, cursor);
        }

        return cursor;
    }

    /// <summary>
    /// Returns the byte offset of the start of the hard line containing <paramref name="cursor"/>;
    /// ports <c>ImStrbol</c> (<c>imgui.cpp:2337-2343</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="cursor">Byte offset within the line.</param>
    /// <returns>The byte offset just after the previous newline, or zero.</returns>
    private static int MoveLineStartRaw(ImGuiInputTextState obj, int cursor)
    {
        int p = cursor;
        while (p > 0 && GetChar(obj, p - 1) != (byte)'\n')
        {
            p--;
        }

        return p;
    }

    #endregion

    #region Mouse input

    /// <summary>
    /// Finds the nearest character to a display position and reports which side of it the point
    /// falls on; ports <c>stb_text_locate_coord</c> (<c>imstb_textedit.h:433-496</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="x">Widget-relative x coordinate.</param>
    /// <param name="y">Widget-relative y coordinate.</param>
    /// <param name="sideOnLine">Receives non-zero when the point sits after the returned character.</param>
    /// <returns>The byte offset of the located character.</returns>
    private static int LocateCoord(ImGuiInputTextState obj, float x, float y, out int sideOnLine)
    {
        TexteditRow r = default;
        int n = StringLen(obj);
        float baseY = 0.0f;
        float prevX;
        int i = 0;
        int k;

        r.X0 = r.X1 = 0.0f;
        r.YMin = r.YMax = 0.0f;
        r.NumChars = 0;
        sideOnLine = 0;

        // Search rows to find one that straddles 'y'.
        while (i < n)
        {
            LayoutRow(ref r, obj, i);
            if (r.NumChars <= 0)
            {
                return n;
            }

            if (i == 0 && y < baseY + r.YMin)
            {
                return 0;
            }

            if (y < baseY + r.YMax)
            {
                break;
            }

            i += r.NumChars;
            baseY += r.BaselineYDelta;
        }

        // Below all text, return 'after' the last character.
        if (i >= n)
        {
            sideOnLine = 1;
            return n;
        }

        // Check whether the point is before the beginning of the line.
        if (x < r.X0)
        {
            return i;
        }

        // Check whether the point is before the end of the line.
        if (x < r.X1)
        {
            prevX = r.X0;
            for (k = 0; k < r.NumChars; k = NextCharIndex(obj, i + k) - i)
            {
                float w = GetWidth(obj, i, k);
                if (x < prevX + w)
                {
                    sideOnLine = k == 0 ? 0 : 1;
                    return x < prevX + w / 2.0f ? k + i : NextCharIndex(obj, i + k);
                }

                prevX += w;
            }
        }

        // If the last character is a newline, return it; otherwise return after the last character.
        sideOnLine = 1;
        return GetChar(obj, i + r.NumChars - 1) == (byte)'\n' ? i + r.NumChars - 1 : i + r.NumChars;
    }

    /// <summary>
    /// Moves the cursor to a clicked position and resets the selection; the <c>stb_textedit_click</c>
    /// entry point (<c>imstb_textedit.h:499-516</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="x">Widget-relative x coordinate.</param>
    /// <param name="y">Widget-relative y coordinate.</param>
    public static void Click(ImGuiInputTextState obj, float x, float y)
    {
        ImStbTexteditState state = Stb(obj);

        // In single-line mode, always use the first row's y so dragging off the widget keeps working.
        if (state.SingleLine != 0)
        {
            TexteditRow r = default;
            LayoutRow(ref r, obj, 0);
            y = r.YMin;
        }

        state.Cursor = LocateCoord(obj, x, y, out int sideOnLine);
        state.SelectStart = state.Cursor;
        state.SelectEnd = state.Cursor;
        state.HasPreferredX = 0;
        obj.LastMoveDirectionLR = (sbyte)(sideOnLine != 0 ? ImGuiDir.Right : ImGuiDir.Left);
    }

    /// <summary>
    /// Moves the cursor and the selection endpoint to a dragged position; the <c>stb_textedit_drag</c>
    /// entry point (<c>imstb_textedit.h:519-539</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="x">Widget-relative x coordinate.</param>
    /// <param name="y">Widget-relative y coordinate.</param>
    public static void Drag(ImGuiInputTextState obj, float x, float y)
    {
        ImStbTexteditState state = Stb(obj);

        // In single-line mode, always use the first row's y so dragging off the widget keeps working.
        if (state.SingleLine != 0)
        {
            TexteditRow r = default;
            LayoutRow(ref r, obj, 0);
            y = r.YMin;
        }

        if (state.SelectStart == state.SelectEnd)
        {
            state.SelectStart = state.Cursor;
        }

        int p = LocateCoord(obj, x, y, out int sideOnLine);
        state.Cursor = state.SelectEnd = p;
        obj.LastMoveDirectionLR = (sbyte)(sideOnLine != 0 ? ImGuiDir.Right : ImGuiDir.Left);
    }

    #endregion

    #region Selection and deletion helpers

    /// <summary>
    /// Reports whether the editor currently holds a selection; the canonical
    /// <c>STB_TEXT_HAS_SELECTION</c> macro (<c>imstb_textedit.h:613</c>).
    /// </summary>
    /// <param name="state">Editor state; must not be null.</param>
    /// <returns><see langword="true"/> when the selection endpoints differ.</returns>
    private static bool HasSelection(ImStbTexteditState state)
    {
        return state.SelectStart != state.SelectEnd;
    }

    /// <summary>
    /// Canonicalizes a selection so that the start is not greater than the end; ports
    /// <c>stb_textedit_sortselection</c> (<c>imstb_textedit.h:654-661</c>).
    /// </summary>
    /// <param name="state">Editor state; must not be null.</param>
    private static void SortSelection(ImStbTexteditState state)
    {
        if (state.SelectEnd < state.SelectStart)
        {
            int temp = state.SelectEnd;
            state.SelectEnd = state.SelectStart;
            state.SelectStart = temp;
        }
    }

    /// <summary>
    /// Clamps the cursor and selection to the current text length; ports
    /// <c>stb_textedit_clamp</c> (<c>imstb_textedit.h:616-627</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    public static void Clamp(ImGuiInputTextState obj, ImStbTexteditState state)
    {
        int n = StringLen(obj);
        if (HasSelection(state))
        {
            if (state.SelectStart > n)
            {
                state.SelectStart = n;
            }

            if (state.SelectEnd > n)
            {
                state.SelectEnd = n;
            }

            if (state.SelectStart == state.SelectEnd)
            {
                state.Cursor = state.SelectStart;
            }
        }

        if (state.Cursor > n)
        {
            state.Cursor = n;
        }
    }

    /// <summary>
    /// Updates the selection and cursor to match each other; ports
    /// <c>stb_textedit_prep_selection_at_cursor</c> (<c>imstb_textedit.h:755-761</c>).
    /// </summary>
    /// <param name="state">Editor state; must not be null.</param>
    public static void PrepSelectionAtCursor(ImStbTexteditState state)
    {
        if (!HasSelection(state))
        {
            state.SelectStart = state.SelectEnd = state.Cursor;
        }
        else
        {
            state.Cursor = state.SelectEnd;
        }
    }

    /// <summary>
    /// Moves the cursor to the first character of the selection; ports
    /// <c>stb_textedit_move_to_first</c> (<c>imstb_textedit.h:664-672</c>).
    /// </summary>
    /// <param name="state">Editor state; must not be null.</param>
    public static void MoveToFirst(ImStbTexteditState state)
    {
        if (HasSelection(state))
        {
            SortSelection(state);
            state.Cursor = state.SelectStart;
            state.SelectEnd = state.SelectStart;
            state.HasPreferredX = 0;
        }
    }

    /// <summary>
    /// Moves the cursor to the last character of the selection; ports
    /// <c>stb_textedit_move_to_last</c> (<c>imstb_textedit.h:675-684</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    public static void MoveToLast(ImGuiInputTextState obj, ImStbTexteditState state)
    {
        if (HasSelection(state))
        {
            SortSelection(state);
            Clamp(obj, state);
            state.Cursor = state.SelectEnd;
            state.SelectStart = state.SelectEnd;
            state.HasPreferredX = 0;
        }
    }

    /// <summary>
    /// Deletes a byte range while recording the deletion for undo; ports
    /// <c>stb_textedit_delete</c> (<c>imstb_textedit.h:630-635</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    /// <param name="where">First byte offset to delete.</param>
    /// <param name="length">Number of bytes to delete.</param>
    private static void Delete(ImGuiInputTextState obj, ImStbTexteditState state, int where, int length)
    {
        MakeUndoDelete(obj, state, where, length);
        DeleteChars(obj, where, length);
        state.HasPreferredX = 0;
    }

    /// <summary>
    /// Deletes the current selection and collapses the cursor to the deletion point; ports
    /// <c>stb_textedit_delete_selection</c> (<c>imstb_textedit.h:638-651</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    public static void DeleteSelection(ImGuiInputTextState obj, ImStbTexteditState state)
    {
        Clamp(obj, state);
        if (HasSelection(state))
        {
            if (state.SelectStart < state.SelectEnd)
            {
                Delete(obj, state, state.SelectStart, state.SelectEnd - state.SelectStart);
                state.SelectEnd = state.Cursor = state.SelectStart;
            }
            else
            {
                Delete(obj, state, state.SelectEnd, state.SelectStart - state.SelectEnd);
                state.SelectStart = state.Cursor = state.SelectEnd;
            }

            state.HasPreferredX = 0;
        }
    }

    #endregion

    #region Keyboard and text input

    /// <summary>
    /// Processes one character input and inserts it, replacing the selection or, in insert mode, the
    /// character under the cursor; ports <c>stb_textedit_text</c> (<c>imstb_textedit.h:798-821</c>).
    /// Dear ImGui delivers characters here through <c>OnCharPressed</c>
    /// (<c>imgui_widgets.cpp:4339-4348</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="text">One UTF-8 encoded character; an empty span is ignored.</param>
    public static void Text(ImGuiInputTextState obj, ReadOnlySpan<byte> text)
    {
        ImStbTexteditState state = Stb(obj);
        if (text.Length == 0)
        {
            return;
        }

        // A newline cannot be added in single-line mode.
        if (text[0] == (byte)'\n' && state.SingleLine != 0)
        {
            return;
        }

        if (state.InsertMode != 0 && !HasSelection(state) && state.Cursor < StringLen(obj))
        {
            MakeUndoReplace(obj, state, state.Cursor, 1, 1);
            DeleteChars(obj, state.Cursor, 1);
            int replaced = InsertChars(obj, state.Cursor, text);
            if (replaced != 0)
            {
                state.Cursor += replaced;
                state.HasPreferredX = 0;
            }
        }
        else
        {
            DeleteSelection(obj, state); // Implicitly clamps.
            int inserted = InsertChars(obj, state.Cursor, text);
            if (inserted != 0)
            {
                MakeUndoInsert(state, state.Cursor, inserted);
                state.Cursor += inserted;
                state.HasPreferredX = 0;
            }
        }
    }

    /// <summary>
    /// Processes one keyboard input; the <c>stb_textedit_key</c> entry point
    /// (<c>imstb_textedit.h:824-1192</c>). Movement, selection, deletion, and undo/redo are all
    /// driven from this switch.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="key">Key input value, optionally or-ed with <see cref="KeyShift"/>.</param>
    public static void Key(ImGuiInputTextState obj, int key)
    {
        ImStbTexteditState state = Stb(obj);

    retry:
        switch (key)
        {
            default:
            {
                // Canonical calls STB_TEXTEDIT_KEYTOTEXT and stb_textedit_text when the hook is
                // defined; Dear ImGui leaves it undefined for UTF-8, so unrecognized keys are ignored.
                int c = KeyToText(key);
                if (c > 0)
                {
                    Span<byte> ch = stackalloc byte[1];
                    ch[0] = (byte)c;
                    Text(obj, ch);
                }

                break;
            }

            case KeyUndo:
                Undo(obj, state);
                state.HasPreferredX = 0;
                break;

            case KeyRedo:
                Redo(obj, state);
                state.HasPreferredX = 0;
                break;

            case KeyLeft:
                if (HasSelection(state))
                {
                    MoveToFirst(state);
                }
                else if (state.Cursor > 0)
                {
                    state.Cursor = PrevCharIndex(obj, state.Cursor);
                }

                state.HasPreferredX = 0;
                break;

            case KeyRight:
                if (HasSelection(state))
                {
                    MoveToLast(obj, state);
                }
                else
                {
                    state.Cursor = NextCharIndex(obj, state.Cursor);
                }

                Clamp(obj, state);
                state.HasPreferredX = 0;
                break;

            case KeyLeft | KeyShift:
                Clamp(obj, state);
                PrepSelectionAtCursor(state);
                if (state.SelectEnd > 0)
                {
                    state.SelectEnd = PrevCharIndex(obj, state.SelectEnd);
                }

                state.Cursor = state.SelectEnd;
                state.HasPreferredX = 0;
                break;

            case KeyWordLeft:
                if (HasSelection(state))
                {
                    MoveToFirst(state);
                }
                else
                {
                    state.Cursor = MoveWordLeft(obj, state.Cursor);
                    Clamp(obj, state);
                }

                break;

            case KeyWordLeft | KeyShift:
                if (!HasSelection(state))
                {
                    PrepSelectionAtCursor(state);
                }

                state.Cursor = MoveWordLeft(obj, state.Cursor);
                state.SelectEnd = state.Cursor;
                Clamp(obj, state);
                break;

            case KeyWordRight:
                if (HasSelection(state))
                {
                    MoveToLast(obj, state);
                }
                else
                {
                    state.Cursor = MoveWordRight(obj, state.Cursor);
                    Clamp(obj, state);
                }

                break;

            case KeyWordRight | KeyShift:
                if (!HasSelection(state))
                {
                    PrepSelectionAtCursor(state);
                }

                state.Cursor = MoveWordRight(obj, state.Cursor);
                state.SelectEnd = state.Cursor;
                Clamp(obj, state);
                break;

            case KeyRight | KeyShift:
                PrepSelectionAtCursor(state);
                state.SelectEnd = NextCharIndex(obj, state.SelectEnd);
                Clamp(obj, state);
                state.Cursor = state.SelectEnd;
                state.HasPreferredX = 0;
                break;

            case KeyDown:
            case KeyDown | KeyShift:
            case KeyPageDown:
            case KeyPageDown | KeyShift:
            {
                FindState find = default;
                TexteditRow row = default;
                int i;
                int sel = (key & KeyShift) != 0 ? 1 : 0;
                bool isPage = (key & ~KeyShift) == KeyPageDown;
                int rowCount = isPage ? state.RowCountPerPage : 1;

                if (!isPage && state.SingleLine != 0)
                {
                    // On Windows, up and down in a single-line field behave like left and right.
                    key = KeyRight | (key & KeyShift);
                    goto retry;
                }

                if (sel != 0)
                {
                    PrepSelectionAtCursor(state);
                }
                else if (HasSelection(state))
                {
                    MoveToLast(obj, state);
                }

                Clamp(obj, state);
                FindCharPos(obj, state, ref find, state.Cursor, state.SingleLine != 0);

                for (int j = 0; j < rowCount; j++)
                {
                    float goalX = state.HasPreferredX != 0 ? state.PreferredX : find.X;
                    int start = find.FirstChar + find.Length;

                    if (find.Length == 0)
                    {
                        break;
                    }

                    // Find the character position one row down.
                    state.Cursor = start;
                    LayoutRow(ref row, obj, state.Cursor);
                    float x = row.X0;
                    for (i = 0; i < row.NumChars;)
                    {
                        float dx = GetWidth(obj, start, i);
                        int next = NextCharIndex(obj, state.Cursor);
                        if (dx == GetWidthNewline)
                        {
                            break;
                        }

                        x += dx;
                        if (x > goalX)
                        {
                            break;
                        }

                        i += next - state.Cursor;
                        state.Cursor = next;
                    }

                    Clamp(obj, state);
                    if (state.Cursor == find.FirstChar + find.Length)
                    {
                        obj.LastMoveDirectionLR = (sbyte)ImGuiDir.Left;
                    }

                    state.HasPreferredX = 1;
                    state.PreferredX = goalX;
                    if (sel != 0)
                    {
                        state.SelectEnd = state.Cursor;
                    }

                    // Go to the next line.
                    find.FirstChar = find.FirstChar + find.Length;
                    find.Length = row.NumChars;
                }

                break;
            }

            case KeyUp:
            case KeyUp | KeyShift:
            case KeyPageUp:
            case KeyPageUp | KeyShift:
            {
                FindState find = default;
                TexteditRow row = default;
                int i;
                int sel = (key & KeyShift) != 0 ? 1 : 0;
                bool isPage = (key & ~KeyShift) == KeyPageUp;
                int rowCount = isPage ? state.RowCountPerPage : 1;

                if (!isPage && state.SingleLine != 0)
                {
                    // On Windows, up and down in a single-line field behave like left and right.
                    key = KeyLeft | (key & KeyShift);
                    goto retry;
                }

                if (sel != 0)
                {
                    PrepSelectionAtCursor(state);
                }
                else if (HasSelection(state))
                {
                    MoveToFirst(state);
                }

                Clamp(obj, state);
                FindCharPos(obj, state, ref find, state.Cursor, state.SingleLine != 0);

                for (int j = 0; j < rowCount; j++)
                {
                    float goalX = state.HasPreferredX != 0 ? state.PreferredX : find.X;

                    // Can only go up when a previous row exists.
                    if (find.PrevFirst == find.FirstChar)
                    {
                        break;
                    }

                    // Find the character position one row up.
                    state.Cursor = find.PrevFirst;
                    LayoutRow(ref row, obj, state.Cursor);
                    float x = row.X0;
                    for (i = 0; i < row.NumChars;)
                    {
                        float dx = GetWidth(obj, find.PrevFirst, i);
                        int next = NextCharIndex(obj, state.Cursor);
                        if (dx == GetWidthNewline)
                        {
                            break;
                        }

                        x += dx;
                        if (x > goalX)
                        {
                            break;
                        }

                        i += next - state.Cursor;
                        state.Cursor = next;
                    }

                    Clamp(obj, state);
                    if (state.Cursor == find.FirstChar)
                    {
                        obj.LastMoveDirectionLR = (sbyte)ImGuiDir.Right;
                    }
                    else if (state.Cursor == find.PrevFirst)
                    {
                        obj.LastMoveDirectionLR = (sbyte)ImGuiDir.Left;
                    }

                    state.HasPreferredX = 1;
                    state.PreferredX = goalX;
                    if (sel != 0)
                    {
                        state.SelectEnd = state.Cursor;
                    }

                    // Go to the previous line; the line before that has to be rescanned.
                    int prevScan = find.PrevFirst > 0 ? find.PrevFirst - 1 : 0;
                    while (prevScan > 0)
                    {
                        int prev = PrevCharIndex(obj, prevScan);
                        if (GetChar(obj, prev) == (byte)'\n')
                        {
                            break;
                        }

                        prevScan = prev;
                    }

                    find.FirstChar = find.PrevFirst;
                    find.PrevFirst = MoveLineStart(obj, state, prevScan);
                }

                break;
            }

            case KeyDelete:
            case KeyDelete | KeyShift:
                if (HasSelection(state))
                {
                    DeleteSelection(obj, state);
                }
                else
                {
                    int n = StringLen(obj);
                    if (state.Cursor < n)
                    {
                        Delete(obj, state, state.Cursor, NextCharIndex(obj, state.Cursor) - state.Cursor);
                    }
                }

                state.HasPreferredX = 0;
                break;

            case KeyBackspace:
            case KeyBackspace | KeyShift:
                if (HasSelection(state))
                {
                    DeleteSelection(obj, state);
                }
                else
                {
                    Clamp(obj, state);
                    if (state.Cursor > 0)
                    {
                        int prev = PrevCharIndex(obj, state.Cursor);
                        Delete(obj, state, prev, state.Cursor - prev);
                        state.Cursor = prev;
                    }
                }

                state.HasPreferredX = 0;
                break;

            case KeyTextStart:
                state.Cursor = state.SelectStart = state.SelectEnd = 0;
                state.HasPreferredX = 0;
                break;

            case KeyTextEnd:
                state.Cursor = StringLen(obj);
                state.SelectStart = state.SelectEnd = 0;
                state.HasPreferredX = 0;
                break;

            case KeyTextStart | KeyShift:
                PrepSelectionAtCursor(state);
                state.Cursor = state.SelectEnd = 0;
                state.HasPreferredX = 0;
                break;

            case KeyTextEnd | KeyShift:
                PrepSelectionAtCursor(state);
                state.Cursor = state.SelectEnd = StringLen(obj);
                state.HasPreferredX = 0;
                break;

            case KeyLineStart:
                Clamp(obj, state);
                MoveToFirst(state);
                state.Cursor = MoveLineStart(obj, state, state.Cursor);
                state.HasPreferredX = 0;
                break;

            case KeyLineEnd:
                Clamp(obj, state);
                MoveToLast(obj, state);
                state.Cursor = MoveLineEnd(obj, state, state.Cursor);
                state.HasPreferredX = 0;
                break;

            case KeyLineStart | KeyShift:
                Clamp(obj, state);
                PrepSelectionAtCursor(state);
                state.Cursor = MoveLineStart(obj, state, state.Cursor);
                state.SelectEnd = state.Cursor;
                state.HasPreferredX = 0;
                break;

            case KeyLineEnd | KeyShift:
                Clamp(obj, state);
                PrepSelectionAtCursor(state);
                state.Cursor = MoveLineEnd(obj, state, state.Cursor);
                state.SelectEnd = state.Cursor;
                state.HasPreferredX = 0;
                break;
        }
    }

    /// <summary>
    /// Finds the x/y location of a character and remembers the previous row for up movement; ports
    /// <c>stb_textedit_find_charpos</c> (<c>imstb_textedit.h:563-611</c>), including the Dear ImGui
    /// wrap-point and last-line special cases (<c>imstb_textedit.h:588-599</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    /// <param name="find">Receives the character location and row bookkeeping.</param>
    /// <param name="n">Byte offset of the target character.</param>
    /// <param name="singleLine">Whether the editor runs in single-line mode.</param>
    private static void FindCharPos(ImGuiInputTextState obj, ImStbTexteditState state, ref FindState find, int n, bool singleLine)
    {
        TexteditRow r = default;
        int prevStart = 0;
        int z = StringLen(obj);
        int i = 0;
        int first;

        if (n == z && singleLine)
        {
            // Special case for the end of a single-line field.
            LayoutRow(ref r, obj, 0);
            find.Y = 0.0f;
            find.FirstChar = 0;
            find.Length = z;
            find.Height = r.YMax - r.YMin;
            find.X = r.X1;
            return;
        }

        // Search rows to find the one that straddles character n.
        find.Y = 0.0f;

        for (;;)
        {
            LayoutRow(ref r, obj, i);
            if (n < i + r.NumChars)
            {
                break;
            }

            // [DEAR IMGUI] Wrapping-point handling.
            if (obj.LastMoveDirectionLR == (sbyte)ImGuiDir.Right && state.Cursor > 0 && state.Cursor == i + r.NumChars
                && GetChar(obj, i + r.NumChars - 1) != (byte)'\n')
            {
                break;
            }

            // [DEAR IMGUI] Special handling for the last line.
            if (i + r.NumChars == z && z > 0 && GetChar(obj, z - 1) != (byte)'\n')
            {
                break;
            }

            prevStart = i;
            i += r.NumChars;
            find.Y += r.BaselineYDelta;
            if (i == z)
            {
                r.NumChars = 0;
                break;
            }
        }

        find.FirstChar = first = i;
        find.Length = r.NumChars;
        find.Height = r.YMax - r.YMin;
        find.PrevFirst = prevStart;

        // Now scan to find the x position.
        find.X = r.X0;
        for (i = 0; first + i < n; i = NextCharIndex(obj, first + i) - first)
        {
            find.X += GetWidth(obj, first, i);
        }
    }

    #endregion

    #region Undo and redo

    /// <summary>
    /// Drops the entire redo list, which happens whenever a new undo record is created; ports
    /// <c>stb_textedit_flush_redo</c> (<c>imstb_textedit.h:1200-1204</c>).
    /// </summary>
    /// <param name="state">Undo storage; must not be null.</param>
    private static void FlushRedo(StbUndoState state)
    {
        state.RedoPoint = (short)UndoStateCount;
        state.RedoCharPoint = UndoCharCount;
    }

    /// <summary>
    /// Discards the oldest undo record and shifts the remaining records down; ports
    /// <c>stb_textedit_discard_undo</c> (<c>imstb_textedit.h:1207-1223</c>). The canonical
    /// <c>memmove</c> calls become array shifts because the records are references.
    /// </summary>
    /// <param name="state">Undo storage; must not be null.</param>
    private static void DiscardUndo(StbUndoState state)
    {
        if (state.UndoPoint > 0)
        {
            StbUndoRecord[] records = state.UndoRec;
            if (records[0].CharStorage >= 0)
            {
                int n = records[0].InsertLength;
                state.UndoCharPoint -= n;
                Array.Copy(state.UndoChar, n, state.UndoChar, 0, state.UndoCharPoint);
                for (int i = 0; i < state.UndoPoint; i++)
                {
                    if (records[i].CharStorage >= 0)
                    {
                        records[i].CharStorage -= n;
                    }
                }
            }

            state.UndoPoint--;
            Array.Copy(records, 1, records, 0, state.UndoPoint);
        }
    }

    /// <summary>
    /// Discards the oldest redo record and shifts the remaining records up; ports
    /// <c>stb_textedit_discard_redo</c> (<c>imstb_textedit.h:1229-1257</c>) including the Dear ImGui
    /// record-move fix. The canonical <c>memmove</c> calls become array shifts.
    /// </summary>
    /// <param name="state">Undo storage; must not be null.</param>
    private static void DiscardRedo(StbUndoState state)
    {
        int k = UndoStateCount - 1;
        if (state.RedoPoint <= k)
        {
            StbUndoRecord[] records = state.UndoRec;
            if (records[k].CharStorage >= 0)
            {
                int n = records[k].InsertLength;
                state.RedoCharPoint += n;
                Array.Copy(state.UndoChar, state.RedoCharPoint - n, state.UndoChar, state.RedoCharPoint, UndoCharCount - state.RedoCharPoint);
                for (int i = state.RedoPoint; i < k; i++)
                {
                    if (records[i].CharStorage >= 0)
                    {
                        records[i].CharStorage += n;
                    }
                }
            }

            Array.Copy(records, state.RedoPoint, records, state.RedoPoint + 1, UndoStateCount - state.RedoPoint - 1);
            state.RedoPoint++;
        }
    }

    /// <summary>
    /// Reserves an undo record with space for <paramref name="numChars"/> characters; ports
    /// <c>stb_text_create_undo_record</c> (<c>imstb_textedit.h:1259-1281</c>). Returns -1 when the
    /// characters cannot fit and undo must be abandoned.
    /// </summary>
    /// <param name="state">Undo storage; must not be null.</param>
    /// <param name="numChars">Number of characters the record must store.</param>
    /// <returns>The record index, or <see cref="NoUndoRecord"/> when none is available.</returns>
    private static int CreateUndoRecord(StbUndoState state, int numChars)
    {
        // Any time we create a new undo record, we discard redo.
        FlushRedo(state);

        if (state.UndoPoint == UndoStateCount)
        {
            DiscardUndo(state);
        }

        // If the characters to store cannot fit at all, undo is impossible.
        if (numChars > UndoCharCount)
        {
            state.UndoPoint = 0;
            state.UndoCharPoint = 0;
            return NoUndoRecord;
        }

        while (state.UndoCharPoint + numChars > UndoCharCount)
        {
            DiscardUndo(state);
        }

        int index = state.UndoPoint;
        state.UndoPoint = (short)(index + 1);
        return index;
    }

    /// <summary>
    /// Creates one undo record and reserves character storage for it; ports
    /// <c>stb_text_createundo</c> (<c>imstb_textedit.h:1283-1301</c>).
    /// </summary>
    /// <param name="state">Undo storage; must not be null.</param>
    /// <param name="pos">Byte position of the edit.</param>
    /// <param name="insertLen">Characters that an undo must insert.</param>
    /// <param name="deleteLen">Characters that an undo must delete.</param>
    /// <returns>The character-storage base index, or -1 when no character storage is needed or available.</returns>
    private static int CreateUndo(StbUndoState state, int pos, int insertLen, int deleteLen)
    {
        int record = CreateUndoRecord(state, insertLen);
        if (record == NoUndoRecord)
        {
            return NoUndoRecord;
        }

        StbUndoRecord r = state.UndoRec[record];
        r.Where = pos;
        r.InsertLength = insertLen;
        r.DeleteLength = deleteLen;

        if (insertLen == 0)
        {
            r.CharStorage = -1;
            return NoUndoRecord;
        }

        r.CharStorage = state.UndoCharPoint;
        state.UndoCharPoint += insertLen;
        return r.CharStorage;
    }

    /// <summary>
    /// Applies one undo step and derives the matching redo record; ports <c>stb_text_undo</c>
    /// (<c>imstb_textedit.h:1303-1369</c>). Record fields are copied into locals first because the
    /// canonical value copy of <c>StbUndoRecord</c> must not alias the redo record being written.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="editorState">Editor state; must not be null.</param>
    private static void Undo(ImGuiInputTextState obj, ImStbTexteditState editorState)
    {
        StbUndoState s = editorState.Undostate;
        if (s.UndoPoint == 0)
        {
            return;
        }

        // We apply the undo record and create a redo record.
        StbUndoRecord u = s.UndoRec[s.UndoPoint - 1];
        int uWhere = u.Where;
        int uInsertLength = u.InsertLength;
        int uDeleteLength = u.DeleteLength;
        int uCharStorage = u.CharStorage;

        StbUndoRecord r = s.UndoRec[s.RedoPoint - 1];
        r.CharStorage = -1;
        r.InsertLength = uDeleteLength;
        r.DeleteLength = uInsertLength;
        r.Where = uWhere;

        if (uDeleteLength != 0)
        {
            // The redo record must re-insert the characters that are about to be deleted.
            if (s.UndoCharPoint + uDeleteLength >= UndoCharCount)
            {
                r.InsertLength = 0;
            }
            else
            {
                while (s.UndoCharPoint + uDeleteLength > s.RedoCharPoint)
                {
                    if (s.RedoPoint == UndoStateCount)
                    {
                        return;
                    }

                    DiscardRedo(s);
                }

                r = s.UndoRec[s.RedoPoint - 1];
                r.CharStorage = s.RedoCharPoint - uDeleteLength;
                s.RedoCharPoint -= uDeleteLength;

                for (int i = 0; i < uDeleteLength; i++)
                {
                    s.UndoChar[r.CharStorage + i] = GetChar(obj, uWhere + i);
                }
            }

            DeleteChars(obj, uWhere, uDeleteLength);
        }

        // Check the type of the recorded action.
        if (uInsertLength != 0)
        {
            uInsertLength = InsertChars(obj, uWhere, AsSpan(s.UndoChar, uCharStorage, uInsertLength));
            s.UndoCharPoint -= uInsertLength;
        }

        editorState.Cursor = uWhere + uInsertLength;
        s.UndoPoint--;
        s.RedoPoint--;
    }

    /// <summary>
    /// Applies one redo step and derives the matching undo record; ports <c>stb_text_redo</c>
    /// (<c>imstb_textedit.h:1371-1420</c>). The canonical redo record value copy becomes explicit
    /// locals so the undo record being written cannot alias it.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="editorState">Editor state; must not be null.</param>
    private static void Redo(ImGuiInputTextState obj, ImStbTexteditState editorState)
    {
        StbUndoState s = editorState.Undostate;
        if (s.RedoPoint == UndoStateCount)
        {
            return;
        }

        // We apply the redo record and create an undo record.
        StbUndoRecord u = s.UndoRec[s.UndoPoint];
        StbUndoRecord r = s.UndoRec[s.RedoPoint];
        int rWhere = r.Where;
        int rInsertLength = r.InsertLength;
        int rDeleteLength = r.DeleteLength;
        int rCharStorage = r.CharStorage;

        // There is always room for the undo record because the redo record derived from one.
        u.DeleteLength = rInsertLength;
        u.InsertLength = rDeleteLength;
        u.Where = rWhere;
        u.CharStorage = -1;

        if (rDeleteLength != 0)
        {
            // The redo record requires a deletion, so the undo record must store the characters.
            if (s.UndoCharPoint + u.InsertLength > s.RedoCharPoint)
            {
                u.InsertLength = 0;
                u.DeleteLength = 0;
            }
            else
            {
                u.CharStorage = s.UndoCharPoint;
                s.UndoCharPoint += u.InsertLength;

                for (int i = 0; i < u.InsertLength; i++)
                {
                    s.UndoChar[u.CharStorage + i] = GetChar(obj, u.Where + i);
                }
            }

            DeleteChars(obj, rWhere, rDeleteLength);
        }

        if (rInsertLength != 0)
        {
            rInsertLength = InsertChars(obj, rWhere, AsSpan(s.UndoChar, rCharStorage, rInsertLength));
            s.RedoCharPoint += rInsertLength;
        }

        editorState.Cursor = rWhere + rInsertLength;
        s.UndoPoint++;
        s.RedoPoint++;
    }

    /// <summary>
    /// Returns a slice of the undo character buffer. Canonical passes a raw pointer plus a length
    /// (<c>imstb_textedit.h:1361</c>, <c>1412</c>); this helper keeps the same byte range.
    /// </summary>
    /// <param name="buffer">Undo character buffer.</param>
    /// <param name="start">First index of the range.</param>
    /// <param name="length">Number of bytes in the range.</param>
    /// <returns>The requested span, clamped to the buffer.</returns>
    private static ReadOnlySpan<byte> AsSpan(byte[] buffer, int start, int length)
    {
        if (start < 0 || length <= 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return new ReadOnlySpan<byte>(buffer, start, length);
    }

    /// <summary>
    /// Records an insertion so that undoing it deletes the inserted characters; ports
    /// <c>stb_text_makeundo_insert</c> (<c>imstb_textedit.h:1422-1425</c>).
    /// </summary>
    /// <param name="state">Editor state; must not be null.</param>
    /// <param name="where">Byte position of the insertion.</param>
    /// <param name="length">Number of inserted bytes.</param>
    private static void MakeUndoInsert(ImStbTexteditState state, int where, int length)
    {
        CreateUndo(state.Undostate, where, 0, length);
    }

    /// <summary>
    /// Records a deletion and stores the deleted characters; ports <c>stb_text_makeundo_delete</c>
    /// (<c>imstb_textedit.h:1427-1435</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    /// <param name="where">Byte position of the deletion.</param>
    /// <param name="length">Number of deleted bytes.</param>
    private static void MakeUndoDelete(ImGuiInputTextState obj, ImStbTexteditState state, int where, int length)
    {
        int p = CreateUndo(state.Undostate, where, length, 0);
        if (p != NoUndoRecord)
        {
            for (int i = 0; i < length; i++)
            {
                state.Undostate.UndoChar[p + i] = GetChar(obj, where + i);
            }
        }
    }

    /// <summary>
    /// Records a replacement and stores the replaced characters; ports
    /// <c>stb_text_makeundo_replace</c> (<c>imstb_textedit.h:1437-1445</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="state">Editor state; must not be null.</param>
    /// <param name="where">Byte position of the replacement.</param>
    /// <param name="oldLength">Number of bytes removed by the replacement.</param>
    /// <param name="newLength">Number of bytes added by the replacement.</param>
    private static void MakeUndoReplace(ImGuiInputTextState obj, ImStbTexteditState state, int where, int oldLength, int newLength)
    {
        int p = CreateUndo(state.Undostate, where, oldLength, newLength);
        if (p != NoUndoRecord)
        {
            for (int i = 0; i < oldLength; i++)
            {
                state.Undostate.UndoChar[p + i] = GetChar(obj, where + i);
            }
        }
    }

    #endregion

    #region Public entry points

    /// <summary>
    /// Resets the editor state to a known-good default; ports <c>stb_textedit_clear_state</c> and
    /// <c>stb_textedit_initialize_state</c> (<c>imstb_textedit.h:1448-1469</c>).
    /// </summary>
    /// <param name="state">Editor state to reset; must not be null.</param>
    /// <param name="isSingleLine">Whether the field suppresses newlines and page movement.</param>
    public static void InitializeState(ImStbTexteditState state, bool isSingleLine)
    {
        state.Undostate.UndoPoint = 0;
        state.Undostate.UndoCharPoint = 0;
        state.Undostate.RedoPoint = (short)UndoStateCount;
        state.Undostate.RedoCharPoint = UndoCharCount;
        state.SelectEnd = state.SelectStart = 0;
        state.Cursor = 0;
        state.HasPreferredX = 0;
        state.PreferredX = 0.0f;
        state.CursorAtEndOfLine = 0;
        state.Initialized = 1;
        state.SingleLine = (byte)(isSingleLine ? 1 : 0);
        state.InsertMode = 0;
        state.RowCountPerPage = 0;
    }

    /// <summary>
    /// Replaces the entire text and records a single undo entry; ports the ImGui helper
    /// <c>stb_textedit_replace</c> (<c>imgui_widgets.cpp:4295-4310</c>). The canonical assert that the
    /// insertion cannot fail is represented by the no-op branch below.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="text">Replacement UTF-8 bytes.</param>
    public static void Replace(ImGuiInputTextState obj, ReadOnlySpan<byte> text)
    {
        ImStbTexteditState state = Stb(obj);
        MakeUndoReplace(obj, state, 0, obj.TextLen, text.Length);
        DeleteChars(obj, 0, obj.TextLen);
        state.Cursor = state.SelectStart = state.SelectEnd = 0;
        if (text.Length <= 0)
        {
            return;
        }

        int inserted = InsertChars(obj, 0, text);
        if (inserted > 0)
        {
            state.Cursor = state.SelectStart = state.SelectEnd = text.Length;
            state.HasPreferredX = 0;
        }
    }

    /// <summary>
    /// Deletes the current selection; the <c>stb_textedit_cut</c> entry point
    /// (<c>imstb_textedit.h:764-772</c>). The caller is responsible for copying the selection to the
    /// system clipboard first.
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <returns><see langword="true"/> when a selection was deleted.</returns>
    public static bool Cut(ImGuiInputTextState obj)
    {
        ImStbTexteditState state = Stb(obj);
        if (HasSelection(state))
        {
            DeleteSelection(obj, state); // Implicitly clamps.
            state.HasPreferredX = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces the selection, or inserts at the cursor, with the supplied text; the
    /// <c>stb_textedit_paste</c> entry point (<c>imstb_textedit.h:775-790</c>, <c>1476-1479</c>).
    /// </summary>
    /// <param name="obj">Edited text field; must not be null.</param>
    /// <param name="text">UTF-8 bytes to paste.</param>
    /// <returns><see langword="true"/> when anything was inserted.</returns>
    public static bool Paste(ImGuiInputTextState obj, ReadOnlySpan<byte> text)
    {
        ImStbTexteditState state = Stb(obj);

        // If there is a selection, the paste deletes it first.
        Clamp(obj, state);
        DeleteSelection(obj, state);
        int inserted = InsertChars(obj, state.Cursor, text);
        if (inserted != 0)
        {
            MakeUndoInsert(state, state.Cursor, inserted);
            state.Cursor += inserted;
            state.HasPreferredX = 0;
            return true;
        }

        // A paste failure leaves the deleted selection in place; undo restores it.
        return false;
    }

    #endregion
}
