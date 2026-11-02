#nullable enable
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical style metrics, dark palette, size scaling, and input/output key model
/// implemented by <see cref="ImGuiStyle"/> and <see cref="ImGuiIO"/>.
/// </summary>
/// <remarks>
/// The input tests drive a real <see cref="ImGuiContext"/> because the <see cref="ImGuiIO"/> event
/// producers queue canonical <see cref="ImGuiInputEvent"/> records during Chunk 3. The live input
/// state is only written when <see cref="ImGui.NewFrame"/> runs <c>UpdateInputEvents</c>, so each
/// test creates a context, adds a default font, and drives at least one frame.
/// </remarks>
[TestFixture]
public class StyleAndIOTests : ImGuiTestFixture
{

    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        // Canonical trickling defers later events of the same kind to the next frame. The IO tests
        // assert one deterministic frame per queued batch, so trickling is disabled.
        io.ConfigInputTrickleEventQueue = false;
    }


    /// <summary>
    /// Starts and finishes one full frame so that every event queued before the call is consumed and
    /// the transient input values are retired.
    /// </summary>
    private static void Frame()
    {
        NewTestFrame();
        ImGui.Render();
    }

    #region Style Metrics

    /// <summary>Verifies that the default style metrics match the canonical ImGuiStyle constructor values.</summary>
    [Test]
    public void Style_DefaultMetrics_MatchCanonicalValues()
    {
        ImGuiStyle style = new();

        Assert.That(style.TabRounding, Is.EqualTo(5.0f));
        Assert.That(style.WindowBorderHoverPadding, Is.EqualTo(4.0f));
        Assert.That(style.ScrollbarPadding, Is.EqualTo(2.0f));
        Assert.That(style.TabMinWidthShrink, Is.EqualTo(80.0f));
        Assert.That(style.TabCloseButtonMinWidthSelected, Is.EqualTo(-1.0f));
        Assert.That(style.TabCloseButtonMinWidthUnselected, Is.EqualTo(0.0f));
        Assert.That(style.DragDropTargetBorderSize, Is.EqualTo(2.0f));
        Assert.That(style.DragDropTargetPadding, Is.EqualTo(3.0f));
        Assert.That(style.ColorMarkerSize, Is.EqualTo(3.0f));
        Assert.That(style.DockingSeparatorSize, Is.EqualTo(2.0f));
        Assert.That(style.MouseCursorScale, Is.EqualTo(1.0f));
        Assert.That(style.SeparatorTextBorderSize, Is.EqualTo(3.0f));
        Assert.That(style.WindowMenuButtonPosition, Is.EqualTo(ImGuiDir.Left));
        Assert.That(style.ColorButtonPosition, Is.EqualTo(ImGuiDir.Right));
        Assert.That(style.TableAngledHeadersAngle, Is.EqualTo(35.0f * (MathF.PI / 180.0f)).Within(1e-6f));
    }

    /// <summary>Verifies that scaling truncates each metric toward zero with canonical ImTrunc semantics.</summary>
    [Test]
    public void Style_ScaleAllSizes_TruncatesMetricsTowardZero()
    {
        ImGuiStyle style = new();

        style.ScaleAllSizes(1.5f);

        // Canonical ImTrunc truncates each metric: 4 * 1.5 = 6, 3 * 1.5 = 4.5 -> 4.
        Assert.That(style.FramePadding, Is.EqualTo(new ImVec2(6.0f, 4.0f)));
        Assert.That(style.WindowPadding, Is.EqualTo(new ImVec2(12.0f, 12.0f)));
        // 21 * 1.5 = 31.5 -> 31 and 5 * 1.5 = 7.5 -> 7.
        Assert.That(style.IndentSpacing, Is.EqualTo(31.0f));
        Assert.That(style.TabRounding, Is.EqualTo(7.0f));
        // 19 * 1.5 = 28.5 -> 28.
        Assert.That(style.DisplayWindowPadding, Is.EqualTo(new ImVec2(28.0f, 28.0f)));
        // The sentinel value -1 is preserved because the canonical expression only scales positive values.
        Assert.That(style.TabCloseButtonMinWidthSelected, Is.EqualTo(-1.0f));
    }

    #endregion

    #region Dark Theme

    /// <summary>Verifies that the dark theme assigns every canonical color slot and copies the aliased slots.</summary>
    [Test]
    public void Style_DarkTheme_AssignsEveryCanonicalSlot()
    {
        ImGuiStyle style = new();

        Assert.That(style.Colors.Length, Is.EqualTo((int)ImGuiCol.COUNT));
        Assert.That((int)ImGuiCol.COUNT, Is.EqualTo(63));

        Assert.That(style.Colors[(int)ImGuiCol.Text], Is.EqualTo(new ImVec4(1.00f, 1.00f, 1.00f, 1.00f)));
        Assert.That(style.Colors[(int)ImGuiCol.Separator], Is.EqualTo(style.Colors[(int)ImGuiCol.Border]));
        Assert.That(style.Colors[(int)ImGuiCol.InputTextCursor], Is.EqualTo(style.Colors[(int)ImGuiCol.Text]));
        Assert.That(style.Colors[(int)ImGuiCol.TextLink], Is.EqualTo(style.Colors[(int)ImGuiCol.HeaderActive]));
        Assert.That(style.Colors[(int)ImGuiCol.TreeLines], Is.EqualTo(style.Colors[(int)ImGuiCol.Border]));
    }

    /// <summary>Verifies that derived dark theme colors follow the canonical interpolation expressions.</summary>
    [Test]
    public void Style_DarkTheme_DerivedColorsUseCanonicalInterpolation()
    {
        ImGuiStyle style = new();

        ImVec4 frameBg = style.Colors[(int)ImGuiCol.FrameBg];
        ImVec4 frameBgHovered = style.Colors[(int)ImGuiCol.FrameBgHovered];
        ImVec4 expectedCheckbox = Lerp(frameBg, frameBgHovered, 0.65f);

        Assert.That(style.Colors[(int)ImGuiCol.CheckboxSelectedBg], Is.EqualTo(expectedCheckbox));

        ImVec4 header = style.Colors[(int)ImGuiCol.Header];
        ImVec4 titleBgActive = style.Colors[(int)ImGuiCol.TitleBgActive];
        Assert.That(style.Colors[(int)ImGuiCol.Tab], Is.EqualTo(Lerp(header, titleBgActive, 0.80f)));

        ImVec4 headerActive = style.Colors[(int)ImGuiCol.HeaderActive];
        Assert.That(style.Colors[(int)ImGuiCol.TabSelected], Is.EqualTo(Lerp(headerActive, titleBgActive, 0.60f)));
    }

    #endregion

    #region Input Output

    /// <summary>Verifies that the key data array length matches the canonical named-key count.</summary>
    [Test]
    public void IO_KeysData_UsesCanonicalNamedKeyCount()
    {
        ImGuiIO io = new();

        Assert.That(io.KeysData.Length, Is.EqualTo((int)ImGuiKey.NamedKey_COUNT));
        Assert.That((int)ImGuiKey.NamedKey_COUNT, Is.EqualTo(155));
        Assert.That(io.KeysData[0].DownDuration, Is.EqualTo(-1.0f));
    }

    /// <summary>
    /// Verifies that a queued key event writes the canonical key data entry when the next frame
    /// consumes the queue. (<c>ThirdParty/imgui/imgui.cpp:11344-11361</c>)
    /// </summary>
    [Test]
    public void IO_AddKeyEvent_WritesCanonicalKeyDataEntry()
    {
        ImGuiIO io = _ctx.IO;
        int index = (int)ImGuiKey.Space - (int)ImGuiKey.NamedKey_BEGIN;

        io.AddKeyEvent(ImGuiKey.Space, true);
        Assert.That(io.KeysData[index].Down, Is.False); // Still queued, not applied.
        Assert.That(_ctx.InputEventsQueue.Count, Is.EqualTo(1));

        Frame();

        Assert.That(io.KeysData[index].Down, Is.True);
        Assert.That(io.KeysData[index].AnalogValue, Is.EqualTo(1.0f));
        Assert.That(io.KeysData[index].DownDuration, Is.EqualTo(0.0f));

        io.AddKeyEvent(ImGuiKey.Space, false);
        Frame();

        Assert.That(io.KeysData[index].Down, Is.False);
        Assert.That(io.KeysData[index].AnalogValue, Is.EqualTo(0.0f));
        Assert.That(io.KeysData[index].DownDuration, Is.EqualTo(-1.0f));
    }

    /// <summary>
    /// Verifies that a repeated identical key event is filtered against the newest queued event and
    /// therefore does not create a second queue entry.
    /// (<c>ThirdParty/imgui/imgui.cpp:1926-1961</c>)
    /// </summary>
    [Test]
    public void IO_AddKeyEvent_DuplicateEventIsFiltered()
    {
        ImGuiIO io = _ctx.IO;

        io.AddKeyEvent(ImGuiKey.A, true);
        io.AddKeyEvent(ImGuiKey.A, true); // Identical to the newest queued event, so filtered.

        Assert.That(_ctx.InputEventsQueue.Count, Is.EqualTo(1));

        // A release edge is not a duplicate and is queued.
        io.AddKeyEvent(ImGuiKey.A, false);
        Assert.That(_ctx.InputEventsQueue.Count, Is.EqualTo(2));

        Frame();
        Assert.That(_ctx.InputEventsQueue, Is.Empty);
    }

    /// <summary>
    /// Verifies that clearing the event queue discards every queued event so a subsequent frame does
    /// not apply them. (<c>ThirdParty/imgui/imgui.cpp:1857-1863</c>)
    /// </summary>
    [Test]
    public void IO_ClearEventsQueue_DropsPendingEvents()
    {
        ImGuiIO io = _ctx.IO;
        int index = (int)ImGuiKey.A - (int)ImGuiKey.NamedKey_BEGIN;

        io.AddKeyEvent(ImGuiKey.A, true);
        io.AddInputCharacter('x');
        Assert.That(_ctx.InputEventsQueue.Count, Is.EqualTo(2));

        io.ClearEventsQueue();

        Assert.That(_ctx.InputEventsQueue, Is.Empty);

        NewTestFrame();

        Assert.That(io.KeysData[index].Down, Is.False);
        Assert.That(io.InputQueueCharacters, Is.Empty);
    }

    /// <summary>
    /// Verifies that legacy native key codes are rejected by the producer even while a context is
    /// installed, so no event is queued and no key-state lookup is attempted. Canonical documents the
    /// range below the first named key as legacy native codes that this API does not accept
    /// (<c>ThirdParty/imgui/imgui.cpp:1931-1933</c>).
    /// </summary>
    [Test]
    public void IO_AddKeyAnalogEvent_WithContext_RejectsLegacyNativeKeyCodesWithoutQueueing()
    {
        ImGuiIO io = _ctx.IO;

        // Canonical rejects the legacy native key code with an assertion. The fixture records the guard
        // instead of letting it abort, so the test declares the misuse it exercises.
        using (ExpectAssert("The backend must submit a named key or a modifier pseudo-key."))
            io.AddKeyAnalogEvent((ImGuiKey)100, true, 1.0f);

        Assert.That(_ctx.InputEventsQueue, Is.Empty, "a legacy native key code must not be queued");

        // The frame must remain well formed: no legacy key may reach the key-state lookup.
        NewTestFrame();
        Assert.That(io.KeysData[0].Down, Is.False);
        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies that legacy native key codes below the named-key range are rejected by the producer
    /// before a context is installed, matching the canonical precondition that only named keys and
    /// modifiers may be submitted. (<c>ThirdParty/imgui/imgui.cpp:1926-1932</c>)
    /// </summary>
    [Test]
    public void IO_AddKeyAnalogEvent_IgnoresLegacyNativeKeyCodes()
    {
        ImGuiIO io = new();

        // The input record has no context attached, so the producer reports the misuse and returns
        // without queueing. The fixture records the guard so the test can declare it.
        using (ExpectAssert("g != null"))
            io.AddKeyAnalogEvent((ImGuiKey)100, true, 1.0f);

        Assert.That(io.KeysData[0].Down, Is.False);
    }

    /// <summary>
    /// Verifies that a queued UTF-16 surrogate pair is combined into a single Unicode code point and
    /// appended to the live character queue by the next frame.
    /// (<c>ThirdParty/imgui/imgui.cpp:1809-1841</c>)
    /// </summary>
    [Test]
    public void IO_AddInputCharacterUTF16_CombinesSurrogatePair()
    {
        ImGuiIO io = _ctx.IO;

        io.AddInputCharacterUTF16(0xD83D);
        Assert.That(io.InputQueueCharacters, Is.Empty);
        Assert.That(_ctx.InputEventsQueue, Is.Empty); // The high surrogate is retained, not queued.

        io.AddInputCharacterUTF16(0xDE00);
        Assert.That(_ctx.InputEventsQueue.Count, Is.EqualTo(1));

        NewTestFrame();

        Assert.That(io.InputQueueCharacters, Is.EqualTo(new[] { 0x1F600u }));
    }

    /// <summary>
    /// Verifies that a string is queued as Unicode code points and consumed by the next frame.
    /// (<c>ThirdParty/imgui/imgui.cpp:1843-1854</c>)
    /// </summary>
    [Test]
    public void IO_AddInputCharactersUTF8_DecodesCodePoints()
    {
        ImGuiIO io = _ctx.IO;

        io.AddInputCharactersUTF8("A\u00E9");
        Assert.That(io.InputQueueCharacters, Is.Empty);

        NewTestFrame();

        Assert.That(io.InputQueueCharacters, Is.EqualTo(new[] { 0x41u, 0xE9u }));
    }

    /// <summary>
    /// Verifies that clearing keyboard input immediately releases keys and modifiers and discards
    /// pending character input on the live state.
    /// (<c>ThirdParty/imgui/imgui.cpp:1865-1879</c>)
    /// </summary>
    [Test]
    public void IO_ClearInputKeys_ReleasesKeysAndModifiers()
    {
        ImGuiIO io = _ctx.IO;
        int spaceIndex = (int)ImGuiKey.Space - (int)ImGuiKey.NamedKey_BEGIN;

        // Modifier state is derived from the modifier key slots during UpdateKeyboardInputs, so the
        // Control modifier is submitted as the canonical ImGuiMod_Ctrl key.
        io.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        io.AddKeyEvent(ImGuiKey.Space, true);
        io.AddInputCharacter('x');

        NewTestFrame();

        Assert.That(io.KeysData[spaceIndex].Down, Is.True);
        Assert.That(io.KeyCtrl, Is.True);
        Assert.That(io.InputQueueCharacters, Is.EqualTo(new[] { (uint)'x' }));

        io.ClearInputKeys();

        Assert.That(io.KeysData[spaceIndex].Down, Is.False);
        Assert.That(io.KeysData[spaceIndex].DownDuration, Is.EqualTo(-1.0f));
        Assert.That(io.KeyCtrl, Is.False);
        Assert.That(io.KeyMods, Is.EqualTo(ImGuiKey.None));
        Assert.That(io.InputQueueCharacters, Is.Empty);
    }

    /// <summary>
    /// Verifies that clearing mouse input immediately releases buttons and resets position and wheel
    /// deltas on the live state. (<c>ThirdParty/imgui/imgui.cpp:1882-1900</c>)
    /// </summary>
    [Test]
    public void IO_ClearInputMouse_ResetsMouseState()
    {
        ImGuiIO io = _ctx.IO;

        io.AddMousePosEvent(10.0f, 20.0f);
        io.AddMouseButtonEvent(0, true);
        io.AddMouseWheelEvent(1.0f, 2.0f);

        NewTestFrame();

        Assert.That(io.MousePos, Is.EqualTo(new ImVec2(10.0f, 20.0f)));
        Assert.That(io.MouseDown[0], Is.True);
        Assert.That(io.MouseWheel, Is.EqualTo(2.0f));
        Assert.That(io.MouseWheelH, Is.EqualTo(1.0f));

        io.ClearInputMouse();

        Assert.That(io.MousePos.X, Is.EqualTo(-float.MaxValue));
        Assert.That(io.MousePos.Y, Is.EqualTo(-float.MaxValue));
        Assert.That(io.MouseDown[0], Is.False);
        Assert.That(io.MouseDownDuration[0], Is.EqualTo(-1.0f));
        Assert.That(io.MouseWheel, Is.EqualTo(0.0f));
        Assert.That(io.MouseWheelH, Is.EqualTo(0.0f));
    }

    #endregion

    /// <summary>
    /// Interpolates two colors with the canonical component-wise expression.
    /// </summary>
    /// <param name="a">The color returned when the factor is zero.</param>
    /// <param name="b">The color returned when the factor is one.</param>
    /// <param name="t">The interpolation factor.</param>
    /// <returns>The interpolated color.</returns>
    private static ImVec4 Lerp(ImVec4 a, ImVec4 b, float t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t,
        a.W + (b.W - a.W) * t);
}
