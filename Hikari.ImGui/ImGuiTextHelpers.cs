#nullable enable
/*================================================================================================
 * TEXT HELPERS
 *================================================================================================
 * ROLE IN ENGINE:
 * Provides the small text-oriented helper types that widgets, demos, and tools use directly: a
 * once-per-frame latch for constructing UI from deeply nested code, and a text filter that parses a
 * comma-separated list of include and exclude terms and matches candidate strings against it.
 *
 * SUBSYSTEM CONNECTIONS & DATA FLOW:
 *
 *    ┌──────────────────────────────────────────────────────┐
 *    │ ImGuiOnceUponAFrame                                  │
 *    │ • RefFrame: frame number of the last successful use  │
 *    └──────────────────────────────────────────────────────┘
 *    │ Check() reads ImGui.GetFrameCount()
 *    ▼
 *    ┌──────────────────────────────────────────────────────┐
 *    │ ImGuiTextFilter                                      │
 *    │ • InputBuf: editable term text                       │
 *    │ • Filters: parsed ImGuiTextRange list                │
 *    │ • CountGrep: number of inclusion terms               │
 *    └──────────────────────────────────────────────────────┘
 *    │ Build() splits InputBuf on commas
 *    ▼
 *    ┌──────────────────────────────────────────────────────┐
 *    │ ImGuiTextRange                                       │
 *    │ • Text: one term, trimmed of leading/trailing blanks │
 *    └──────────────────────────────────────────────────────┘
 *    │ PassFilter(text) evaluates the parsed terms
 *    ▼
 *    ┌──────────────────────────────────────────────────────┐
 *    │ caller's candidate strings                           │
 *    │ • PassFilter() returns true/false for each candidate │
 *    └──────────────────────────────────────────────────────┘
 *
 * STYLIZED USAGE EXAMPLE:
 *
 *   var once = new ImGuiOnceUponAFrame();
 *   if (once.Check())
 *       ImGui.Text("Only the first caller this frame sees this line.");
 *
 *   var filter = new ImGuiTextFilter("mesh,-hidden");
 *   if (filter.PassFilter(candidateName))
 *       ImGui.Text(candidateName);
 *
 * THREADING & LIFECYCLE NOTES:
 * - Both types are plain single-threaded mutable state owned by the caller; no internal
 *   synchronization is provided.
 * - ImGuiTextFilter rebuilds its parsed terms only when its text changes, so repeated PassFilter()
 *   calls within a frame reuse the same parsed list.
 *================================================================================================*/

using System;
using System.Collections.Generic;

namespace Hikari.ImGui;

#region ImGuiOnceUponAFrame

/// <summary>
/// A latch that reports success only on its first use within a frame, so a UI fragment can be
/// constructed exactly once per frame even when it is nested in code that runs several times
/// (<c>ThirdParty/imgui/imgui.h:2891-2898</c>).
/// </summary>
/// <remarks>
/// The latch records the frame number of the last successful use. It is caller-owned and carries no
/// context reference; the frame number is read from the current context when <see cref="Check"/> runs.
/// </remarks>
public sealed class ImGuiOnceUponAFrame
{
    /// <summary>
    /// Gets or sets the frame number on which the latch last fired, or -1 before its first use.
    /// (<c>ThirdParty/imgui/imgui.h:2895</c>)
    /// </summary>
    public int RefFrame { get; set; } = -1;

    /// <summary>
    /// Reports whether this is the latch's first use in the current frame and records the frame when
    /// it is. Mirrors the canonical <c>operator bool</c>
    /// (<c>ThirdParty/imgui/imgui.h:2896</c>).
    /// </summary>
    /// <returns><see langword="true"/> on the first use in a frame, otherwise <see langword="false"/>.</returns>
    public bool Check()
    {
        int currentFrame = ImGui.GetFrameCount();
        if (RefFrame == currentFrame)
            return false;
        RefFrame = currentFrame;
        return true;
    }

    /// <summary>
    /// Enables the canonical <c>if (once_upon_a_frame)</c> usage by converting the latch to the result of
    /// <see cref="Check"/>. Mirrors the canonical <c>operator bool</c>
    /// (<c>ThirdParty/imgui/imgui.h:2896</c>).
    /// </summary>
    /// <param name="value">The latch to evaluate.</param>
    /// <returns>The result of <see cref="Check"/> on the latch.</returns>
    public static implicit operator bool(ImGuiOnceUponAFrame value)
    {
        return value.Check();
    }
}

#endregion

#region ImGuiTextFilter

/// <summary>
/// Pre-parsed term for <see cref="ImGuiTextFilter"/>.
/// (<c>ThirdParty/imgui/imgui_internal.h:880-887</c>)
/// </summary>
public struct ImGuiTextFilterItem
{
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the text item string. Canonical C++ pointer alias (<c>ThirdParty/imgui/imgui_internal.h:882</c>).
    /// </summary>
    public string? Begin
    {
        get => Text;
        set => Text = value ?? string.Empty;
    }

    public int Len => Text?.Length ?? 0;
    public short CountInclude { get; set; }
    public ushort MatchIncludesMask { get; set; }

    public ImGuiTextFilterItem(string text)
    {
        Text = text;
        CountInclude = 0;
        MatchIncludesMask = 0;
    }
}

/// <summary>
/// Parses and applies a text filter in the canonical format <c>aaaaa[,bbbb][,ccccc]</c>. Terms are
/// comma-separated; a term beginning with <c>-</c> excludes matching text and every other term
/// includes it (<c>ThirdParty/imgui/imgui.h:2899-2925</c>,
/// <c>ThirdParty/imgui/imgui.cpp:3151-3255</c>).
/// </summary>
/// <remarks>
/// <para>
/// When only exclusion terms are present, a candidate that matches no exclusion passes. When at least
/// one inclusion term is present, a candidate must match one of them and must not match any exclusion.
/// </para>
/// <para>
/// Matching is ASCII case-insensitive, mirroring the canonical <c>ImStristr</c> comparison.
/// </para>
/// <para>
/// This type is plain single-threaded mutable state and provides no internal synchronization.
/// </para>
/// </remarks>
public sealed class ImGuiTextFilter
{
    #region Constants

    /// <summary>
    /// Capacity of <see cref="InputBuf"/>, including the zero terminator. Mirrors the canonical
    /// <c>char InputBuf[256]</c> (<c>ThirdParty/imgui/imgui.h:2919</c>).
    /// </summary>
    private const int InputBufCapacity = 256;

    #endregion

    #region Properties and state fields

    /// <summary>
    /// Gets the editable, zero-terminated filter text. (<c>ThirdParty/imgui/imgui.h:2919</c>)
    /// </summary>
    public byte[] InputBuf { get; } = new byte[InputBufCapacity];

    /// <summary>
    /// Number of leading exclusion items. (<c>ThirdParty/imgui/imgui.h:2947</c>)
    /// </summary>
    public int _CountExclude;

    /// <summary>
    /// Pre-parsed, trimmed, reordered filter items. (<c>ThirdParty/imgui/imgui.h:2948</c>)
    /// </summary>
    public readonly List<ImGuiTextFilterItem> _Items = new();

    /// <summary>
    /// Gets the parsed terms. The list is rebuilt by <see cref="Build"/>.
    /// (<c>ThirdParty/imgui/imgui.h:2920</c>)
    /// </summary>
    public List<ImGuiTextRange> Filters { get; } = new();

    /// <summary>
    /// Gets or sets the number of inclusion terms, used to apply the implicit <c>*</c> match when no
    /// inclusion term is present. (<c>ThirdParty/imgui/imgui.h:2921</c>)
    /// </summary>
    public int CountGrep { get; set; }

    #endregion

    #region Construction

    /// <summary>
    /// Initializes a filter, optionally from a default filter string. Mirrors the canonical constructor
    /// (<c>ThirdParty/imgui/imgui.cpp:3151-3161</c>).
    /// </summary>
    /// <param name="defaultFilter">
    /// Initial filter text. The canonical default is the empty string; a null value is treated as empty.
    /// </param>
    public ImGuiTextFilter(string defaultFilter = "")
    {
        InputBuf[0] = 0;
        CountGrep = 0;
        if (!string.IsNullOrEmpty(defaultFilter))
        {
            WriteInputUtf8(defaultFilter);
            Build();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Draws an input field bound to <see cref="InputBuf"/> and rebuilds the parsed terms when the text
    /// changes. Mirrors the canonical <c>Draw</c> helper
    /// (<c>ThirdParty/imgui/imgui.h:2939</c>).
    /// </summary>
    /// <param name="label">Field label.</param>
    /// <param name="width">Field width, or zero to keep the current layout width.</param>
    /// <returns><see langword="true"/> when the text changed during this call.</returns>
    public bool Draw(string label = "Filter", float width = 0.0f)
    {
        if (width != 0.0f)
            ImGui.SetNextItemWidth(width);
        bool valueChanged = ImGui.InputText(label, InputBuf, InputBuf.Length);
        if (valueChanged)
            Build();
        return valueChanged;
    }

    /// <summary>
    /// Draws an input field with a placeholder hint bound to <see cref="InputBuf"/> and rebuilds the
    /// parsed terms when the text changes. Mirrors canonical <c>DrawWithHint</c>
    /// (<c>ThirdParty/imgui/imgui.h:2940</c>).
    /// </summary>
    /// <param name="label">Field label.</param>
    /// <param name="hint">Hint text displayed when input is empty.</param>
    /// <param name="width">Field width, or zero to keep current layout width.</param>
    /// <returns><see langword="true"/> when the text changed during this call.</returns>
    public bool DrawWithHint(string label = "Filter", string hint = "incl -excl", float width = 0.0f)
    {
        if (width != 0.0f)
            ImGui.SetNextItemWidth(width);
        bool valueChanged = ImGui.InputTextWithHint(label, hint, InputBuf, InputBuf.Length);
        if (valueChanged)
            Build();
        return valueChanged;
    }

    /// <summary>
    /// Reports whether a candidate string passes the filter. Mirrors the canonical <c>PassFilter</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:3295-3325</c>).
    /// </summary>
    /// <param name="text">Candidate text to test; a null value is treated as empty.</param>
    /// <returns><see langword="true"/> when the candidate satisfies every parsed term.</returns>
    public bool PassFilter(string? text)
    {
        if (_Items.Count == 0)
            return true;

        string haystack = text ?? string.Empty;

        // Filters are sorted so that '-' ones are always leading.
        for (int i = 0; i < _CountExclude; i++)
        {
            if (ContainsIgnoreCase(haystack, _Items[i].Text))
                return false;
        }

        // Process includes
        if (_CountExclude == _Items.Count)
            return true; // When only exclusions are specified, implicitly pass if none matched

        int seqIdx = _CountExclude;
        while (seqIdx < _Items.Count)
        {
            int seqInclEnd = seqIdx + _Items[seqIdx].CountInclude;
            bool allMatched = true;
            for (int i = seqIdx; i < seqInclEnd; i++)
            {
                if (!ContainsIgnoreCase(haystack, _Items[i].Text))
                {
                    allMatched = false;
                    break;
                }
            }
            if (allMatched)
                return true;
            seqIdx = seqInclEnd;
        }
        return false;
    }

    /// <summary>
    /// Re-parses <see cref="InputBuf"/> into <see cref="_Items"/> and recounts the inclusion terms.
    /// Mirrors the canonical <c>Build</c> (<c>ThirdParty/imgui/imgui.cpp:3235-3292</c>).
    /// </summary>
    public void Build()
    {
        _Items.Clear();
        _CountExclude = 0;
        Filters.Clear();

        string input = ReadInput();
        if (string.IsNullOrEmpty(input))
        {
            CountGrep = 0;
            return;
        }

        int bufLen = input.Length;
        int wordE = 0;
        int seqStartIdx = -1;

        for (int wordB = 0; wordB < bufLen; wordB = wordE + 1)
        {
            // Trim blanks
            while (wordB < bufLen && (input[wordB] == ' ' || input[wordB] == '\t'))
                wordB++;
            if (wordB >= bufLen)
                break;

            bool isExcl = input[wordB] == '-';
            if (isExcl)
                wordB++;
            if (wordB >= bufLen)
                break;

            bool isQuote = input[wordB] == '\"';
            if (isQuote)
            {
                wordB++;
                wordE = input.IndexOf('\"', wordB);
                if (wordE == -1)
                    wordE = bufLen;
            }
            else
            {
                wordE = wordB;
                while (wordE < bufLen && input[wordE] != ' ' && input[wordE] != ',')
                    wordE++;
            }

            if (wordE > wordB)
            {
                string term = input.Substring(wordB, wordE - wordB);
                var item = new ImGuiTextFilterItem(term);
                if (isExcl)
                {
                    _Items.Insert(_CountExclude, item);
                    _CountExclude++;
                    if (seqStartIdx != -1)
                        seqStartIdx++;
                    Filters.Add(new ImGuiTextRange("-" + term));
                }
                else
                {
                    if (seqStartIdx == -1)
                        seqStartIdx = _Items.Count;
                    _Items.Add(item);
                    var firstItem = _Items[seqStartIdx];
                    firstItem.CountInclude++;
                    _Items[seqStartIdx] = firstItem;
                    Filters.Add(new ImGuiTextRange(term));
                }
            }

            if (wordE < bufLen && input[wordE] == ',')
                seqStartIdx = -1;
        }

        CountGrep = _Items.Count - _CountExclude;
    }

    /// <summary>
    /// Clears the filter text and the parsed terms. (<c>ThirdParty/imgui/imgui.h:2935</c>)
    /// </summary>
    public void Clear()
    {
        InputBuf[0] = 0;
        Build();
    }

    /// <summary>
    /// Reports whether the filter currently has any parsed terms.
    /// (<c>ThirdParty/imgui/imgui.h:2936</c>)
    /// </summary>
    /// <returns><see langword="true"/> when at least one term is present.</returns>
    public bool IsActive()
    {
        return _Items.Count != 0;
    }

    #endregion

    #region Parsing helpers

    /// <summary>
    /// Encodes <paramref name="text"/> as UTF-8 into <see cref="InputBuf"/>, stopping before any code point
    /// that would not fit so that no multi-byte sequence is split. Mirrors the canonical
    /// <c>ImStrncpy</c> behavior applied to a UTF-8 default filter (<c>ThirdParty/imgui/imgui.cpp:3151-3161</c>).
    /// </summary>
    /// <param name="text">Text to store; a surrogate pair is kept whole or dropped whole.</param>
    private void WriteInputUtf8(string text)
    {
        byte[] encoded = System.Text.Encoding.UTF8.GetBytes(text);
        int length = Math.Min(encoded.Length, InputBufCapacity - 1);
        // Back off to the start of a code point: continuation bytes are 10xxxxxx.
        if (length < encoded.Length)
        {
            while (length > 0 && (encoded[length] & 0xC0) == 0x80)
                length--;
        }
        Array.Copy(encoded, InputBuf, length);
        InputBuf[length] = 0;
    }

    /// <summary>
    /// Reads the zero-terminated text held in <see cref="InputBuf"/> as a managed string.
    /// </summary>
    /// <returns>The text before the first zero byte.</returns>
    private string ReadInput()
    {
        int length = 0;
        while (length < InputBuf.Length && InputBuf[length] != 0)
            length++;
        // Decode as UTF-8 so that non-ASCII terms survive Build(). Byte-by-byte widening would turn every
        // multi-byte sequence into separate Latin-1 characters.
        return System.Text.Encoding.UTF8.GetString(InputBuf, 0, length);
    }

    /// <summary>
    /// Reports whether <paramref name="haystack"/> contains <paramref name="needle"/> ignoring ASCII
    /// case, mirroring the canonical <c>ImStristr</c> comparison
    /// (<c>ThirdParty/imgui/imgui.cpp:3210-3245</c>).
    /// </summary>
    /// <param name="haystack">Text to search.</param>
    /// <param name="needle">Non-empty term to find.</param>
    /// <returns><see langword="true"/> when the term occurs.</returns>
    private static bool ContainsIgnoreCase(string haystack, string needle)
    {
        // Only ASCII letters fold case, matching canonical ImToUpper (imgui_internal.h:420-421).
        // OrdinalIgnoreCase would also fold non-ASCII letters, which canonical does not.
        if (needle.Length == 0)
            return true;
        for (int start = 0; start + needle.Length <= haystack.Length; start++)
        {
            int n = 0;
            while (n < needle.Length && AsciiUpper(haystack[start + n]) == AsciiUpper(needle[n]))
                n++;
            if (n == needle.Length)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Uppercases one ASCII letter and leaves every other character unchanged. Mirrors canonical
    /// <c>ImToUpper</c> (<c>ThirdParty/imgui/imgui_internal.h:420</c>).
    /// </summary>
    /// <param name="c">Character to fold.</param>
    /// <returns>The uppercase ASCII letter, or <paramref name="c"/> unchanged.</returns>
    private static char AsciiUpper(char c)
    {
        return c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
    }

    #endregion

    #region ImGuiTextRange

    /// <summary>
    /// A contiguous slice of filter text, used while splitting <see cref="InputBuf"/> on commas.
    /// Mirrors the canonical nested <c>ImGuiTextFilter::ImGuiTextRange</c>
    /// (<c>ThirdParty/imgui/imgui.h:2905-2915</c>).
    /// </summary>
    public sealed class ImGuiTextRange
    {
        /// <summary>
        /// Gets or sets the slice text. (<c>ThirdParty/imgui/imgui.h:2909</c>)
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Initializes a range from its text.
        /// (<c>ThirdParty/imgui/imgui.h:2907</c>)
        /// </summary>
        /// <param name="text">The slice text; never null.</param>
        public ImGuiTextRange(string text)
        {
            Text = text;
        }

        /// <summary>
        /// Gets a value indicating whether the slice holds no text.
        /// (<c>ThirdParty/imgui/imgui.h:2913</c>)
        /// </summary>
        public bool Empty => Text.Length == 0;

        /// <summary>
        /// Splits this slice on <paramref name="separator"/>, replacing <paramref name="outRanges"/>
        /// with the resulting slices. Empty slices at a trailing separator are dropped, mirroring the
        /// canonical <c>split</c> (<c>ThirdParty/imgui/imgui.cpp:3172-3188</c>).
        /// </summary>
        /// <param name="separator">Separator character.</param>
        /// <param name="outRanges">List replaced with the resulting slices.</param>
        public void Split(char separator, List<ImGuiTextRange> outRanges)
        {
            outRanges.Clear();
            int wordBegin = 0;
            for (int cursor = 0; cursor < Text.Length; cursor++)
            {
                if (Text[cursor] == separator)
                {
                    outRanges.Add(new ImGuiTextRange(Text.Substring(wordBegin, cursor - wordBegin)));
                    wordBegin = cursor + 1;
                }
            }
            if (wordBegin != Text.Length)
                outRanges.Add(new ImGuiTextRange(Text.Substring(wordBegin)));
        }
    }

    #endregion
}

#endregion
