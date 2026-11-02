#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical text helper types: <see cref="ImGuiTextFilter"/> parsing and matching,
/// <see cref="ImGuiTextBuffer"/> accumulation, and <see cref="ImGuiOnceUponAFrame"/> once-per-frame
/// latching. Expected values are derived from Dear ImGui <c>v1.92.9b-docking</c>
/// <c>ThirdParty/imgui/imgui.cpp:3151-3305</c> and <c>ThirdParty/imgui/imgui.h:2891-2944</c>.
/// </summary>
/// <remarks>
/// The filter and buffer tests are pure data operations; only the once-per-frame and state-storage
/// tests drive a real <see cref="ImGuiContext"/>.
/// </remarks>
[TestFixture]
public class TextHelperTests : ImGuiTestFixture
{

    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
    }


    #region ImGuiTextFilter

    /// <summary>
    /// Verifies a single positive term includes matching text and excludes everything else. Canonical
    /// greps with <c>ImStristr</c> and returns true on the first inclusion match
    /// (<c>ThirdParty/imgui/imgui.cpp:3210-3245</c>).
    /// </summary>
    [Test]
    public void TextFilter_PositiveTerm_IncludesOnlyMatches()
    {
        var filter = new ImGuiTextFilter("mesh");

        Assert.That(filter.CountGrep, Is.EqualTo(1));
        Assert.That(filter.Filters.Count, Is.EqualTo(1));
        Assert.That(filter.Filters[0].Text, Is.EqualTo("mesh"));
        Assert.That(filter.IsActive(), Is.True);
        Assert.That(filter.PassFilter("mesh_01"), Is.True);
        Assert.That(filter.PassFilter("cube"), Is.False);
    }

    /// <summary>
    /// Verifies a single negative term excludes matching text and lets everything else pass (the
    /// implicit <c>*</c> inclusion when <c>CountGrep</c> is zero).
    /// </summary>
    [Test]
    public void TextFilter_NegativeTerm_ExcludesOnlyMatches()
    {
        var filter = new ImGuiTextFilter("-hidden");

        Assert.That(filter.CountGrep, Is.EqualTo(0));
        Assert.That(filter.Filters.Count, Is.EqualTo(1));
        Assert.That(filter.PassFilter("mesh_01"), Is.True);
        Assert.That(filter.PassFilter("hidden_node"), Is.False);
    }

    /// <summary>
    /// Verifies comma-separated inclusion and exclusion terms: a candidate must match an inclusion term
    /// and must not match an exclusion term, with the canonical short-circuit that each term is applied
    /// as soon as it is reached. Canonical splits the input on commas and trims blanks
    /// (<c>ThirdParty/imgui/imgui.cpp:3172-3245</c>).
    /// </summary>
    [Test]
    public void TextFilter_MixedTerms_RequiresInclusionAndForbidsExclusion()
    {
        var filter = new ImGuiTextFilter("mesh, cube, -hidden");

        Assert.That(TextTokens(filter), Is.EqualTo(new[] { "mesh", "cube", "-hidden" }));
        Assert.That(filter.CountGrep, Is.EqualTo(2));
        Assert.That(filter.PassFilter("cube_01"), Is.True);

        // In Dear ImGui 1.93, exclusion terms are evaluated first and always apply regardless of include position.
        Assert.That(filter.PassFilter("mesh_hidden"), Is.False);
        Assert.That(filter.PassFilter("mesh_visible"), Is.True);

        // "sphere_hidden" matches no inclusion term, so the later exclusion term rejects it.
        Assert.That(filter.PassFilter("sphere_hidden"), Is.False);
        Assert.That(filter.PassFilter("sphere"), Is.False);
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTextFilter.Build"/> re-parses <see cref="ImGuiTextFilter.InputBuf"/>
    /// in place and recounts inclusion terms, matching the canonical InputText-then-Build flow
    /// (<c>ThirdParty/imgui/imgui.cpp:3162-3170</c>).
    /// </summary>
    [Test]
    public void TextFilter_Build_ReParsesInputBuffer()
    {
        var filter = new ImGuiTextFilter();
        WriteAscii(filter.InputBuf, "water, -lava");
        filter.Build();

        Assert.That(filter.CountGrep, Is.EqualTo(1));
        Assert.That(TextTokens(filter), Is.EqualTo(new[] { "water", "-lava" }));
        Assert.That(filter.PassFilter("water_deep"), Is.True);
        Assert.That(filter.PassFilter("lava_flow"), Is.False);

        // Rebuilding from new text replaces the previous parse rather than appending to it.
        WriteAscii(filter.InputBuf, "stone");
        filter.Build();
        Assert.That(TextTokens(filter), Is.EqualTo(new[] { "stone" }));
        Assert.That(filter.PassFilter("water_deep"), Is.False);
    }

    /// <summary>
    /// Verifies that matching ignores ASCII case, mirroring canonical <c>ImStristr</c>, and that
    /// trimming removes leading and trailing blanks from a term.
    /// </summary>
    [Test]
    public void TextFilter_IgnoresCaseAndTrimsBlanks()
    {
        var filter = new ImGuiTextFilter("  Mesh  ");

        Assert.That(filter.Filters[0].Text, Is.EqualTo("Mesh"));
        Assert.That(filter.PassFilter("MESH_01"), Is.True);
        Assert.That(filter.PassFilter("mEsH_01"), Is.True);
    }

    /// <summary>
    /// Verifies that clearing the filter removes every parsed term and lets all candidates pass.
    /// (<c>ThirdParty/imgui/imgui.h:2911</c>)
    /// </summary>
    [Test]
    public void TextFilter_Clear_RemovesAllTerms()
    {
        var filter = new ImGuiTextFilter("mesh,-hidden");
        Assert.That(filter.IsActive(), Is.True);

        filter.Clear();

        Assert.That(filter.IsActive(), Is.False);
        Assert.That(filter.Filters.Count, Is.EqualTo(0));
        Assert.That(filter.CountGrep, Is.EqualTo(0));
        Assert.That(filter.PassFilter("anything"), Is.True);
    }

    /// <summary>
    /// Verifies that a non-ASCII default filter is stored as UTF-8 and matches as a filter term, rather than
    /// being truncated to its low byte (<c>ThirdParty/imgui/imgui.cpp:3151-3161</c>).
    /// </summary>
    [Test]
    public void TextFilter_NonAsciiDefault_RemainsActiveTerm()
    {
        var filter = new ImGuiTextFilter("\u0100");
        Assert.That(filter.Filters.Count, Is.EqualTo(1));
        Assert.That(filter.CountGrep, Is.EqualTo(1));
        Assert.That(filter.PassFilter("\u0100 mesh"), Is.True);
        Assert.That(filter.PassFilter("mesh"), Is.False);
    }

    /// <summary>
    /// Verifies that a supplementary-plane default is kept whole, with no split surrogate pair or partial
    /// UTF-8 sequence left in the buffer.
    /// </summary>
    [Test]
    public void TextFilter_SupplementaryDefault_KeepsCodePointWhole()
    {
        string emoji = char.ConvertFromUtf32(0x1F600);
        var filter = new ImGuiTextFilter(emoji);
        Assert.That(filter.Filters.Count, Is.EqualTo(1));
        Assert.That(filter.Filters[0].Text, Is.EqualTo(emoji));
    }

    /// <summary>
    /// Verifies that ASCII case folding does not fold non-ASCII letters, matching canonical <c>ImToUpper</c>
    /// (<c>ThirdParty/imgui/imgui_internal.h:420</c>).
    /// </summary>
    [Test]
    public void TextFilter_AsciiOnlyCaseFolding()
    {
        var filter = new ImGuiTextFilter("MESH");
        Assert.That(filter.PassFilter("mesh_01"), Is.True);
        Assert.That(filter.PassFilter("MeSh"), Is.True);

        // U+00C9 (É) and U+00E9 (é) must not match each other under ASCII-only folding.
        var accented = new ImGuiTextFilter("\u00C9");
        Assert.That(accented.PassFilter("\u00E9"), Is.False);
        Assert.That(accented.PassFilter("\u00C9"), Is.True);
    }

    #endregion

    #region ImGuiTextBuffer

    /// <summary>
    /// Verifies that appends accumulate in order and that the reported size and content match the
    /// canonical zero-terminated buffer semantics
    /// (<c>ThirdParty/imgui/imgui.h:2935-2944</c>, <c>ThirdParty/imgui/imgui.cpp:3259-3275</c>).
    /// </summary>
    [Test]
    public void TextBuffer_AppendAccumulatesContentAndLength()
    {
        var buffer = new ImGuiTextBuffer();
        Assert.That(buffer.Size, Is.EqualTo(0));
        Assert.That(buffer.Empty, Is.True);
        Assert.That(buffer.CStr(), Is.EqualTo(string.Empty));

        buffer.Append("Hello");
        Assert.That(buffer.Size, Is.EqualTo(5));
        Assert.That(buffer.Empty, Is.False);
        Assert.That(buffer.CStr(), Is.EqualTo("Hello"));

        buffer.Append(" World");
        Assert.That(buffer.Size, Is.EqualTo(11));
        Assert.That(buffer.CStr(), Is.EqualTo("Hello World"));

        buffer.Clear();
        Assert.That(buffer.Size, Is.EqualTo(0));
        Assert.That(buffer.Empty, Is.True);
        Assert.That(buffer.CStr(), Is.EqualTo(string.Empty));
    }

    /// <summary>
    /// Verifies the canonical shared empty terminator used by <c>c_str()</c> when the buffer was never
    /// written (<c>ThirdParty/imgui/imgui.h:2929</c>).
    /// </summary>
    [Test]
    public void TextBuffer_EmptyStringIsSingleTerminator()
    {
        Assert.That(ImGuiTextBuffer.EmptyString.Length, Is.EqualTo(1));
        Assert.That(ImGuiTextBuffer.EmptyString[0], Is.EqualTo(0));
    }

    #endregion

    #region ImGuiTextIndex

    /// <summary>
    /// Verifies that the line index records one start offset per line and one final end offset, dropping
    /// a trailing line break. Canonical <c>ImGuiTextIndex::append</c> is at
    /// <c>ThirdParty/imgui/imgui.cpp:3313-3325</c>.
    /// </summary>
    [Test]
    public void TextIndex_Append_RecordsLineStartsAndEndOffset()
    {
        var index = new ImGuiTextIndex();

        // "ab\ncd\n": starts at 0, the break at 2 starts line 2 at 3, and the trailing break at 5 does
        // not start a line.
        index.Append("ab\ncd\n", 0, 6);
        Assert.That(index.Offsets, Is.EqualTo(new[] { 0, 3 }));
        Assert.That(index.EndOffset, Is.EqualTo(6));
        Assert.That(index.Size(), Is.EqualTo(2));
        Assert.That(index.GetLineBegin(0), Is.EqualTo(0));
        Assert.That(index.GetLineBegin(1), Is.EqualTo(3));
        Assert.That(index.GetLineEnd(0), Is.EqualTo(2));
        Assert.That(index.GetLineEnd(1), Is.EqualTo(6));

        // Appending "ef\ngh" continues at old size 6; the previous end was a break, so line 3 starts at
        // 6, and the break at 8 starts line 4 at 9.
        index.Append("ab\ncd\nef\ngh", 6, 11);
        Assert.That(index.Offsets, Is.EqualTo(new[] { 0, 3, 6, 9 }));
        Assert.That(index.EndOffset, Is.EqualTo(11));
        Assert.That(index.GetLineBegin(2), Is.EqualTo(6));
        Assert.That(index.GetLineBegin(3), Is.EqualTo(9));
        Assert.That(index.GetLineEnd(3), Is.EqualTo(11));

        // Re-appending an unchanged size is a no-op.
        index.Append("ab\ncd\nef\ngh", 11, 11);
        Assert.That(index.Offsets.Count, Is.EqualTo(4));

        index.Clear();
        Assert.That(index.Offsets.Count, Is.EqualTo(0));
        Assert.That(index.EndOffset, Is.EqualTo(0));
    }

    #endregion

    #region ImGuiOnceUponAFrame

    /// <summary>
    /// Verifies the latch returns true exactly once per frame and remembers the frame it fired on
    /// (<c>ThirdParty/imgui/imgui.h:2891-2898</c>).
    /// </summary>
    [Test]
    public void OnceUponAFrame_TrueOncePerFrame()
    {
        var once = new ImGuiOnceUponAFrame();
        Assert.That(once.RefFrame, Is.EqualTo(-1));

        NewTestFrame();
        bool first = once.Check();
        int firstFrame = ImGui.GetFrameCount();
        bool second = once.Check();
        ImGui.Render();

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(once.RefFrame, Is.EqualTo(firstFrame));

        // A new frame re-arms the latch.
        NewTestFrame();
        bool third = once;
        ImGui.Render();

        Assert.That(third, Is.True);
        Assert.That(once.RefFrame, Is.EqualTo(ImGui.GetFrameCount()));
    }

    #endregion

    #region State storage

    /// <summary>
    /// Verifies that <see cref="ImGui.SetStateStorage"/> swaps the current window's state storage and
    /// that passing <see langword="null"/> restores the window's own store
    /// (<c>ThirdParty/imgui/imgui.cpp:9622-9632</c>).
    /// </summary>
    [Test]
    public void StateStorage_SwapsAndRestores()
    {
        ImGuiStorage defaultStorage = null!;
        ImGuiStorage customStorage = new();
        ImGuiStorage? activeAfterSwap = null;
        ImGuiStorage? activeAfterRestore = null;

        NewTestFrame();
        ImGui.Begin("Storage");
        defaultStorage = ImGui.GetStateStorage();
        ImGui.SetStateStorage(customStorage);
        activeAfterSwap = ImGui.GetStateStorage();
        ImGui.SetStateStorage(null);
        activeAfterRestore = ImGui.GetStateStorage();
        ImGui.End();
        ImGui.Render();

        Assert.That(activeAfterSwap, Is.SameAs(customStorage));
        Assert.That(activeAfterRestore, Is.SameAs(defaultStorage));
        Assert.That(customStorage, Is.Not.SameAs(defaultStorage));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Copies an ASCII string into a fixed byte buffer and terminates it, for tests that drive
    /// <see cref="ImGuiTextFilter.InputBuf"/> directly.
    /// </summary>
    /// <param name="buffer">Destination zero-terminated buffer.</param>
    /// <param name="text">ASCII text to store.</param>
    private static void WriteAscii(byte[] buffer, string text)
    {
        int length = Math.Min(text.Length, buffer.Length - 1);
        for (int n = 0; n < length; n++)
            buffer[n] = (byte)text[n];
        buffer[length] = 0;
    }

    /// <summary>
    /// Returns the parsed filter terms in declaration order.
    /// </summary>
    /// <param name="filter">Filter whose terms are read.</param>
    /// <returns>The parsed term strings.</returns>
    private static string[] TextTokens(ImGuiTextFilter filter)
    {
        string[] tokens = new string[filter.Filters.Count];
        for (int n = 0; n < tokens.Length; n++)
            tokens[n] = filter.Filters[n].Text;
        return tokens;
    }

    #endregion
}
