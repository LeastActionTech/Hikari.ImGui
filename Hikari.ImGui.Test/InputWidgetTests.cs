// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical scalar sliders, scalar drags, and text-entry widgets owned by
/// <c>ImGuiWidgets.cs</c>: slider and drag geometry and value math, and single-line, hinted,
/// multiline, and numeric text input.
/// </summary>
/// <remarks>
/// <para>
/// Each test drives a real <see cref="ImGuiContext"/>. /// The slider and drag *mathematics* is exercised directly through the port's internal
/// <c>SliderBehaviorT</c> and <c>DragBehaviorT</c> entry points, because those routines hold the
/// value through a <c>ref</c> parameter and can be driven without a host window. The public
/// one-line overloads are exercised through their real item submission, which is where the port
/// currently drops the computed value (see the ignored tests at the end of this fixture).
/// </para>
/// </remarks>
[TestFixture]
public class InputWidgetTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
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


    /// <summary>Runs one full frame that begins and ends a fixed-geometry window with no content.</summary>
    private static void WarmupFrame()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("Warmup");
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="body">The widget submissions and assertions to run while the window is current.</param>
    private static void WindowFrame(Action body)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("Window");
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>Submits one widget for one frame and returns the screen rectangle it occupied.</summary>
    /// <param name="submit">Submits exactly one widget.</param>
    /// <returns>The item rectangle observed on that frame.</returns>
    private static ImRect CaptureItem(Action submit)
    {
        ImRect rect = default;
        WindowFrame(() =>
        {
            submit();
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        return rect;
    }

    /// <summary>
    /// Presses the left mouse button inside <paramref name="rect"/>, releasing it on the following
    /// frame, while continuing to submit the same widget every frame.
    /// </summary>
    /// <param name="rect">The item rectangle to click.</param>
    /// <param name="submit">Submits exactly one widget.</param>
    private void ClickItem(ImRect rect, Action submit)
    {
        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        WindowFrame(submit);
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(submit);
    }

    /// <summary>
    /// Presses the left mouse button inside <paramref name="rect"/> and leaves it held, so the caller
    /// can move the pointer across a later frame while the widget remains active.
    /// </summary>
    /// <param name="rect">The item rectangle to press.</param>
    /// <param name="submit">Submits exactly one widget.</param>
    private void PressItemAndHold(ImRect rect, Action submit)
    {
        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        WindowFrame(submit);
    }

    /// <summary>Releases the left mouse button and submits one final widget frame.</summary>
    /// <param name="submit">Submits exactly one widget.</param>
    private void ReleaseMouseAndFrame(Action submit)
    {
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(submit);
    }

    /// <summary>Feeds one character per frame into an active text widget.</summary>
    /// <param name="text">The characters to insert.</param>
    /// <param name="submit">Submits the same text widget on every frame.</param>
    private void TypeText(string text, Action submit)
    {
        foreach (char c in text)
        {
            _ctx.IO.AddInputCharacter(c);
            WindowFrame(submit);
        }
    }

    /// <summary>
    /// Presses a key for one frame and releases it on the next, keeping the widget submitted, and
    /// returns whatever value the widget produced on the key-down frame.
    /// </summary>
    /// <param name="key">The key to press.</param>
    /// <param name="submit">Submits the widget and returns its boolean result.</param>
    /// <param name="release">The value produced on the key-up frame.</param>
    /// <returns>The value produced on the key-down frame.</returns>
    private bool PressKey(ImGuiKey key, Func<bool> submit, out bool release)
    {
        _ctx.IO.AddKeyEvent(key, true);
        bool down = false;
        WindowFrame(() => down = submit());
        _ctx.IO.AddKeyEvent(key, false);
        bool up = false;
        WindowFrame(() => up = submit());
        release = up;
        return down;
    }

    /// <summary>
    /// Makes <paramref name="id"/> the active mouse-owned item with a valid, threshold-crossing mouse,
    /// which is the state the drag core requires before it consumes mouse movement.
    /// </summary>
    /// <param name="id">The identifier to activate.</param>
    private void ActivateMouseItem(uint id)
    {
        _ctx.ActiveId = id;
        _ctx.ActiveIdSource = ImGuiInputSource.Mouse;
        _ctx.ActiveIdIsJustActivated = false;
        _ctx.IO.MouseDown[0] = true;
        _ctx.IO.MousePos = new ImVec2(200.0f, 200.0f);
        _ctx.IO.MouseDragMaxDistanceSqr[0] = 1000.0f;
    }

    /// <summary>Renders one horizontal slider rectangle used by the direct slider-math tests.</summary>
    private static readonly ImRect SliderRect = new(new ImVec2(100.0f, 190.0f), new ImVec2(300.0f, 210.0f));

    /// <summary>Renders one vertical slider rectangle used by the direct slider-math tests.</summary>
    private static readonly ImRect VerticalSliderRect = new(new ImVec2(100.0f, 100.0f), new ImVec2(130.0f, 300.0f));

    #region Slider geometry

    /// <summary>
    /// Verifies that <see cref="ImGui.VSliderFloat"/> consumes exactly the requested rectangle when
    /// the label renders empty. Canonical vertical-slider sizing.
    /// </summary>
    [Test]
    public void VSliderFloat_ItemRectMatchesRequestedSize()
    {
        WarmupFrame();
        float v = 5.0f;
        ImRect rect = CaptureItem(() => ImGui.VSliderFloat("##vs", new ImVec2(40.0f, 160.0f), ref v, 0.0f, 10.0f));

        Assert.That(rect.Max.X - rect.Min.X, Is.EqualTo(40.0f).Within(0.01f));
        Assert.That(rect.Max.Y - rect.Min.Y, Is.EqualTo(160.0f).Within(0.01f));
    }

    /// <summary>
    /// Verifies that a horizontal <see cref="ImGui.SliderFloat"/> frame is one
    /// <see cref="ImGui.CalcItemWidth"/> wide and one font height plus the frame padding tall, as
    /// canonical <c>SliderScalar</c> computes its frame.
    /// </summary>
    [Test]
    public void SliderFloat_ItemRectMatchesCalcItemWidth()
    {
        WarmupFrame();
        float v = 5.0f;
        float expectedWidth = 0.0f;
        float expectedHeight = 0.0f;
        ImRect rect = CaptureItem(() =>
        {
            expectedWidth = ImGui.CalcItemWidth();
            expectedHeight = ImGui.GetFrameHeight();
            ImGui.SliderFloat("##s", ref v, 0.0f, 10.0f);
        });

        Assert.That(rect.Max.X - rect.Min.X, Is.EqualTo(expectedWidth).Within(0.01f));
        Assert.That(rect.Max.Y - rect.Min.Y, Is.EqualTo(expectedHeight).Within(0.01f));
    }

    #endregion

    #region Slider behavior math

    /// <summary>
    /// Verifies that a mouse click past the right end of the slider track stores the upper bound and
    /// a click past the left end stores the lower bound, and that both report a change. Canonical
    /// <c>SliderBehaviorT</c> saturates the clicked parametric position
    /// </summary>
    [Test]
    public void SliderBehaviorT_MousePositionClampsToBounds()
    {
        WarmupFrame();
        ActivateMouseItem(77u);
        float v = 5.0f;

        _ctx.ActiveIdIsJustActivated = true;
        _ctx.IO.MousePos = new ImVec2(400.0f, 200.0f);
        bool rightChanged = ImGui.SliderBehaviorT(
            SliderRect, 77u, ImGuiDataType.Float, ref v, 0.0f, 10.0f, "%.3f", 0, out ImRect rightGrab);

        Assert.That(rightChanged, Is.True);
        Assert.That(v, Is.EqualTo(10.0f).Within(0.001f));
        Assert.That(rightGrab.Max.X, Is.GreaterThan(SliderRect.Min.X));

        _ctx.ActiveIdIsJustActivated = false;
        _ctx.IO.MousePos = new ImVec2(0.0f, 200.0f);
        bool leftChanged = ImGui.SliderBehaviorT(
            SliderRect, 77u, ImGuiDataType.Float, ref v, 0.0f, 10.0f, "%.3f", 0, out _);

        Assert.That(leftChanged, Is.True);
        Assert.That(v, Is.EqualTo(0.0f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that the logarithmic slider places the geometric mean of its bounds at the middle of
    /// the track. Canonical converts through <c>ScaleRatioFromValueT</c>/<c>ScaleValueFromRatioT</c>
    /// with the logarithmic branch.; the
    /// geometric mean of <c>1</c> and <c>100</c> is <c>10</c>.
    /// </summary>
    [Test]
    public void SliderBehaviorT_LogarithmicMidpointIsGeometricMean()
    {
        WarmupFrame();
        ActivateMouseItem(78u);
        float v = 1.0f;
        _ctx.ActiveIdIsJustActivated = false;
        _ctx.IO.MousePos = new ImVec2(200.0f, 200.0f);

        bool changed = ImGui.SliderBehaviorT(
            SliderRect, 78u, ImGuiDataType.Float, ref v, 1.0f, 100.0f, "%.3f", ImGuiSliderFlags.Logarithmic, out _);

        Assert.That(changed, Is.True);
        Assert.That(v, Is.EqualTo(10.0f).Within(0.01f));
    }

    /// <summary>
    /// Verifies that the vertical slider maps the bottom of its track to the lower bound and the top
    /// to the upper bound. Canonical inverts the grabbed ratio for the vertical axis
    /// </summary>
    [Test]
    public void SliderBehaviorT_VerticalAxisMapsTopAndBottom()
    {
        WarmupFrame();
        ActivateMouseItem(79u);
        const ImGuiSliderFlags Vertical =
            (ImGuiSliderFlags)ImGuiSliderFlagsPrivate.ImGuiSliderFlags_Vertical;
        float v = 5.0f;

        _ctx.ActiveIdIsJustActivated = true;
        _ctx.IO.MousePos = new ImVec2(115.0f, 400.0f);
        ImGui.SliderBehaviorT(
            VerticalSliderRect, 79u, ImGuiDataType.Float, ref v, 0.0f, 10.0f, "%.3f", Vertical, out _);
        Assert.That(v, Is.EqualTo(0.0f).Within(0.001f));

        _ctx.ActiveIdIsJustActivated = false;
        _ctx.IO.MousePos = new ImVec2(115.0f, 0.0f);
        ImGui.SliderBehaviorT(
            VerticalSliderRect, 79u, ImGuiDataType.Float, ref v, 0.0f, 10.0f, "%.3f", Vertical, out _);
        Assert.That(v, Is.EqualTo(10.0f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that the reported grab rectangle centres on the position implied by the stored value
    /// and is <c>style.GrabMinSize</c> wide. Canonical grab-rectangle layout
    /// </summary>
    [Test]
    public void SliderBehaviorT_GrabRectCentersOnValue()
    {
        WarmupFrame();
        ActivateMouseItem(80u);
        float v = 5.0f;
        _ctx.ActiveIdIsJustActivated = false;
        _ctx.IO.MousePos = new ImVec2(0.0f, 200.0f);
        ImGui.SliderBehaviorT(SliderRect, 80u, ImGuiDataType.Float, ref v, 0.0f, 10.0f, "%.3f", 0, out _);

        // Re-run with the value pinned at the upper bound so the grab must sit at the track maximum.
        v = 10.0f;
        _ctx.ActiveIdIsJustActivated = true;
        _ctx.IO.MousePos = new ImVec2(400.0f, 200.0f);
        ImGui.SliderBehaviorT(SliderRect, 80u, ImGuiDataType.Float, ref v, 0.0f, 10.0f, "%.3f", 0, out ImRect grab);

        float grabWidth = grab.Max.X - grab.Min.X;
        float trackMax = SliderRect.Min.X + 2.0f + (grabWidth * 0.5f)
            + ((SliderRect.Max.X - SliderRect.Min.X) - 4.0f - grabWidth);
        Assert.That(grabWidth, Is.EqualTo(12.0f).Within(0.001f));
        Assert.That((grab.Min.X + grab.Max.X) * 0.5f, Is.EqualTo(trackMax).Within(0.01f));
        Assert.That(grab.Min.Y, Is.EqualTo(SliderRect.Min.Y + 2.0f).Within(0.001f));
    }

    #endregion

    #region Drag behavior math

    /// <summary>
    /// Verifies that each pixel of horizontal mouse travel moves the value by
    /// <c>vSpeed</c>. Canonical accumulates <c>MouseDelta * v_speed</c> into
    /// <c>g.DragCurrentAccum</c>.
    /// </summary>
    [Test]
    public void DragBehaviorT_MouseDeltaScalesWithSpeed()
    {
        WarmupFrame();
        ActivateMouseItem(81u);
        _ctx.IO.MouseDelta = new ImVec2(20.0f, 0.0f);
        float v = 5.0f;

        bool changed = ImGui.DragBehaviorT(
            ImGuiDataType.Float, ref v, 1.0f, -float.MaxValue, float.MaxValue, "%.3f", 0);

        Assert.That(changed, Is.True);
        Assert.That(v, Is.EqualTo(25.0f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that a bounded drag clamps the accumulated result to the upper bound. Canonical
    /// clamp branch after rounding.
    /// </summary>
    [Test]
    public void DragBehaviorT_BoundedRangeClamps()
    {
        WarmupFrame();
        ActivateMouseItem(82u);
        _ctx.IO.MouseDelta = new ImVec2(25.0f, 0.0f);
        float v = 0.0f;

        bool changed = ImGui.DragBehaviorT(ImGuiDataType.Float, ref v, 1.0f, 0.0f, 10.0f, "%.3f", 0);

        Assert.That(changed, Is.True);
        Assert.That(v, Is.EqualTo(10.0f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that vertical drag inverts the sign of mouse travel so that dragging up raises the
    /// value. Canonical negates the accumulated delta for the vertical axis
    /// </summary>
    [Test]
    public void DragBehaviorT_VerticalAxisInvertsDelta()
    {
        WarmupFrame();
        ActivateMouseItem(83u);
        _ctx.IO.MouseDelta = new ImVec2(0.0f, 20.0f);
        float v = 5.0f;
        const ImGuiSliderFlags Vertical =
            (ImGuiSliderFlags)ImGuiSliderFlagsPrivate.ImGuiSliderFlags_Vertical;

        bool changed = ImGui.DragBehaviorT(
            ImGuiDataType.Float, ref v, 1.0f, -float.MaxValue, float.MaxValue, "%.3f", Vertical);

        Assert.That(changed, Is.True);
        Assert.That(v, Is.EqualTo(-15.0f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that a zero <c>vSpeed</c> on a bounded drag uses the canonical default ratio of the
    /// whole range per hundred pixels (<c>DragSpeedDefaultRatio = 1/100</c>). Canonical default-speed
    /// derivation.
    /// </summary>
    [Test]
    public void DragBehaviorT_DefaultSpeedScalesWithRange()
    {
        WarmupFrame();
        ActivateMouseItem(84u);
        _ctx.IO.MouseDelta = new ImVec2(20.0f, 0.0f);
        float v = 0.0f;

        bool changed = ImGui.DragBehaviorT(ImGuiDataType.Float, ref v, 0.0f, 0.0f, 100.0f, "%.3f", 0);

        Assert.That(changed, Is.True);
        Assert.That(v, Is.EqualTo(20.0f).Within(0.001f));
    }

    #endregion

    #region Text entry

    /// <summary>
    /// Verifies that typed characters append to the buffer and that Backspace removes the last one.
    /// Canonical STB text-edit insertion and deletion through <c>InputTextEx</c>
    /// </summary>
    [Test]
    public void InputText_TypedCharactersAndBackspace()
    {
        WarmupFrame();
        string buf = "";
        ImRect rect = CaptureItem(() => ImGui.InputText("##t", ref buf));
        ClickItem(rect, () => ImGui.InputText("##t", ref buf));

        TypeText("AB", () => ImGui.InputText("##t", ref buf));
        Assert.That(buf, Is.EqualTo("AB"));

        bool released;
        PressKey(ImGuiKey.Backspace, () => { ImGui.InputText("##t", ref buf); return false; }, out released);
        Assert.That(buf, Is.EqualTo("A"));
    }

    /// <summary>
    /// Verifies that an edited text field whose active id moves to another widget writes its retained text
    /// back to the caller's buffer on the deactivation frame, through the canonical deactivate hook
    /// (, ).
    /// </summary>
    [Test]
    public void InputText_EditedFieldWritesBackWhenActiveIdMovesToButton()
    {
        WarmupFrame();
        string buf = "";
        ImRect rect = CaptureItem(() => ImGui.InputText("##t", ref buf));
        ClickItem(rect, () => ImGui.InputText("##t", ref buf));
        TypeText("Z", () => ImGui.InputText("##t", ref buf));
        Assert.That(buf, Is.EqualTo("Z"));

        // Activating a button takes the active id away from the text field.
        WindowFrame(() => ImGui.Button("B"));
        Assert.That(buf, Is.EqualTo("Z"));
    }

    /// <summary>
    /// Verifies that the cursor moves with the Left arrow key and that a later character is inserted
    /// at the cursor rather than appended. Canonical cursor movement and insertion
    /// </summary>
    [Test]
    public void InputText_CursorMovementInsertsAtCursor()
    {
        WarmupFrame();
        string buf = "AB";
        ImRect rect = CaptureItem(() => ImGui.InputText("##t", ref buf));
        ClickItem(rect, () => ImGui.InputText("##t", ref buf));

        bool released;
        PressKey(ImGuiKey.End, () => { ImGui.InputText("##t", ref buf); return false; }, out released);
        PressKey(ImGuiKey.LeftArrow, () => { ImGui.InputText("##t", ref buf); return false; }, out released);
        TypeText("C", () => ImGui.InputText("##t", ref buf));

        Assert.That(buf, Is.EqualTo("ACB"));
    }

    /// <summary>
    /// Verifies that Enter deactivates the input and that
    /// <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/> makes the widget report true on that
    /// frame. Canonical Enter handling.
    /// </summary>
    [Test]
    public void InputText_EnterReturnsTrue()
    {
        WarmupFrame();
        string buf = "";
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.EnterReturnsTrue;
        ImRect rect = CaptureItem(() => ImGui.InputText("##t", ref buf, flags));
        ClickItem(rect, () => ImGui.InputText("##t", ref buf, flags));
        TypeText("A", () => ImGui.InputText("##t", ref buf, flags));

        bool up;
        bool down = PressKey(ImGuiKey.Enter, () => ImGui.InputText("##t", ref buf, flags), out up);

        Assert.That(buf, Is.EqualTo("A"));
        Assert.That(down || up, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiInputTextFlags.ReadOnly"/> rejects typed characters and leaves
    /// the buffer untouched. Canonical read-only guard.
    /// </summary>
    [Test]
    public void InputText_ReadOnlyRejectsTyping()
    {
        WarmupFrame();
        string buf = "";
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.ReadOnly;
        ImRect rect = CaptureItem(() => ImGui.InputText("##t", ref buf, flags));
        ClickItem(rect, () => ImGui.InputText("##t", ref buf, flags));

        TypeText("XYZ", () => ImGui.InputText("##t", ref buf, flags));

        Assert.That(buf, Is.EqualTo(string.Empty));
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiInputTextFlags.Password"/> keeps the real characters in the
    /// buffer while rendering a different glyph. Canonical password masking
    ///.; the buffer must hold the typed text so
    /// callers can read the secret, while the drawn glyph atlas coordinates must differ from the
    /// plain-text rendering of the same character.
    /// </summary>
    [Test]
    public void InputText_PasswordMasksDisplay()
    {
        WarmupFrame();
        string plain = "A";
        string secret = "A";

        // Warm both windows so their fixed geometry is applied before the comparison frame.
        for (int i = 0; i < 2; i++)
        {
            NewTestFrame();
            ImGui.SetNextWindowPos(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new ImVec2(300.0f, 200.0f), ImGuiCond.Always);
            ImGui.Begin("Plain");
            ImGui.InputText("##p", ref plain);
            ImGui.End();
            ImGui.SetNextWindowPos(new ImVec2(500.0f, 100.0f), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new ImVec2(300.0f, 200.0f), ImGuiCond.Always);
            ImGui.Begin("Masked");
            ImGui.InputText("##m", ref secret, ImGuiInputTextFlags.Password);
            ImGui.End();
            ImGui.Render();
        }

        // Two windows in one frame: the same character rendered plainly and masked. Comparing the
        // glyph atlas coordinates isolates the mask substitution from the identical frame quads.
        List<string> plainUvs = new();
        List<string> maskedUvs = new();
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 200.0f), ImGuiCond.Always);
        ImGui.Begin("Plain");
        ImGui.InputText("##p", ref plain);
        CollectGlyphUvs(ImGui.GetCurrentContext()!.CurrentWindow!.DrawList, plainUvs);
        ImGui.End();
        ImGui.SetNextWindowPos(new ImVec2(500.0f, 100.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 200.0f), ImGuiCond.Always);
        ImGui.Begin("Masked");
        ImGui.InputText("##m", ref secret, ImGuiInputTextFlags.Password);
        CollectGlyphUvs(ImGui.GetCurrentContext()!.CurrentWindow!.DrawList, maskedUvs);
        ImGui.End();
        ImGui.Render();

        Assert.That(secret, Is.EqualTo("A"));
        Assert.That(maskedUvs, Is.Not.EqualTo(plainUvs));
    }

    /// <summary>
    /// Collects the distinct texture coordinates drawn by a window, which for text identifies the
    /// glyphs actually rendered.
    /// </summary>
    /// <param name="drawList">The window's draw list.</param>
    /// <param name="destination">Receives one entry per distinct coordinate.</param>
    private static void CollectGlyphUvs(ImDrawList drawList, List<string> destination)
    {
        for (int i = 0; i < drawList.VtxBuffer.Count; i++)
        {
            ImVec2 uv = drawList.VtxBuffer[i].UV;
            string entry = $"{uv.X:F3},{uv.Y:F3}";
            if (!destination.Contains(entry))
                destination.Add(entry);
        }
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.InputTextWithHint"/> displays a hint while empty and accepts
    /// typed characters into its buffer. Canonical hinted input.
    /// </summary>
    [Test]
    public void InputTextWithHint_AcceptsTyping()
    {
        WarmupFrame();
        string buf = "";
        ImRect rect = CaptureItem(() => ImGui.InputTextWithHint("##t", "enter text", ref buf));
        ClickItem(rect, () => ImGui.InputTextWithHint("##t", "enter text", ref buf));

        TypeText("hi", () => ImGui.InputTextWithHint("##t", "enter text", ref buf));

        Assert.That(buf, Is.EqualTo("hi"));
    }

    /// <summary>
    /// Verifies that Enter inside a multiline input inserts a newline instead of deactivating the
    /// widget. Canonical multiline Enter handling.
    /// </summary>
    [Test]
    public void InputTextMultiline_InsertsNewLine()
    {
        WarmupFrame();
        string buf = "";
        Action submit = () => ImGui.InputTextMultiline("##ml", ref buf, new ImVec2(200.0f, 80.0f));
        ImRect rect = CaptureItem(submit);
        ClickItem(rect, submit);
        TypeText("ab", submit);

        bool released;
        PressKey(ImGuiKey.Enter, () => { submit(); return false; }, out released);
        TypeText("cd", submit);

        Assert.That(buf, Is.EqualTo("ab\ncd"));
    }

    #endregion

    #region Numeric entry

    /// <summary>
    /// Verifies that typed decimal and integer text is parsed back into the referenced value once
    /// the widget deactivates. Canonical <c>InputScalar</c> parse on deactivation
    ///., exposed by <c>InputFloat</c>
    /// and <c>InputInt</c>.
    /// </summary>
    [Test]
    public void InputFloat_And_InputInt_ParseTypedText()
    {
        WarmupFrame();
        float f = 0.0f;
        ImRect floatRect = CaptureItem(() => ImGui.InputFloat("##f", ref f));
        ClickItem(floatRect, () => ImGui.InputFloat("##f", ref f));
        TypeText("3.5", () => ImGui.InputFloat("##f", ref f));
        bool released;
        PressKey(ImGuiKey.Enter, () => { ImGui.InputFloat("##f", ref f); return false; }, out released);
        Assert.That(f, Is.EqualTo(3.5f).Within(0.001f));

        int i = 0;
        ImRect intRect = CaptureItem(() => ImGui.InputInt("##i", ref i));
        ClickItem(intRect, () => ImGui.InputInt("##i", ref i));
        TypeText("42", () => ImGui.InputInt("##i", ref i));
        PressKey(ImGuiKey.Enter, () => { ImGui.InputInt("##i", ref i); return false; }, out released);
        Assert.That(i, Is.EqualTo(42));
    }

    #endregion

    #region Public one-line dispatch

    /// <summary>
    /// Verifies that dragging the public one-line float slider writes the dragged value back into the
    /// caller's variable and reports the change frame. The button is held across the drag frame, which
    /// is what keeps the slider active; releasing first would end the interaction.
    /// Canonical <c>SliderScalar</c>. with the
    /// shared <c>SliderBehavior</c> write-through.
    /// </summary>
    [Test]
    public void SliderWidgets_PublicApiWritesBackEditedValue()
    {
        WarmupFrame();
        float v = 5.0f;
        ImRect rect = CaptureItem(() => ImGui.SliderFloat("##s", ref v, 0.0f, 10.0f));
        PressItemAndHold(rect, () => ImGui.SliderFloat("##s", ref v, 0.0f, 10.0f));

        // Hold the button and move the pointer past the right end of the track: the grab clamps to the
        // maximum value, so the caller's variable must become exactly the slider maximum.
        _ctx.IO.AddMousePosEvent(rect.Max.X + 200.0f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        WindowFrame(() => ImGui.SliderFloat("##s", ref v, 0.0f, 10.0f));
        ReleaseMouseAndFrame(() => ImGui.SliderFloat("##s", ref v, 0.0f, 10.0f));

        Assert.That(v, Is.EqualTo(10.0f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that dragging the public one-line float drag writes the dragged value back into the
    /// caller's variable. The button is held across the drag frame, which is what keeps the drag item
    /// active; releasing first would end the interaction.
    /// Canonical <c>DragBehavior</c> write-through.
    /// </summary>
    [Test]
    public void DragWidgets_PublicApiWritesBackEditedValue()
    {
        WarmupFrame();
        float v = 5.0f;
        ImRect rect = CaptureItem(() => ImGui.DragFloat("##d", ref v, 1.0f));
        PressItemAndHold(rect, () => ImGui.DragFloat("##d", ref v, 1.0f));

        // Hold the button and move 20 pixels to the right: at a speed of one unit per pixel the value
        // must advance by the same amount and be stored back into the caller's variable.
        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f + 20.0f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        WindowFrame(() => ImGui.DragFloat("##d", ref v, 1.0f));
        ReleaseMouseAndFrame(() => ImGui.DragFloat("##d", ref v, 1.0f));

        Assert.That(v, Is.EqualTo(25.0f).Within(0.001f));
    }

    #endregion

    #region Canonical typed scalar overloads

    /// <summary>
    /// Verifies that the signed 8-bit, 16-bit, and 64-bit <c>DragScalar</c> overloads box the caller's
    /// value, drive the shared drag core, and store the edited value back into the typed variable.
    /// Canonical <c>DragScalar</c>. with the shared
    /// <c>DragBehavior</c> write-through.
    /// </summary>
    [Test]
    public void DragScalar_SignedIntegerWidths_WriteBackThroughTheSharedCore()
    {
        WarmupFrame();
        sbyte s8 = 0;
        short s16 = 0;
        long s64 = 0;
        ImRect rectS8 = default;
        ImRect rectS16 = default;
        ImRect rectS64 = default;

        // All three rows are submitted every frame, so each drag presses a distinct row. Consecutive
        // presses at the same position would otherwise be treated as a double click, which converts a
        // drag into a temporary text input and never advances the value.
        void Submit()
        {
            ImGui.DragScalar("##s8", ImGuiDataType.S8, ref s8, 1.0f);
            rectS8 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.DragScalar("##s16", ImGuiDataType.S16, ref s16, 1.0f);
            rectS16 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.DragScalar("##s64", ImGuiDataType.S64, ref s64, 1.0f);
            rectS64 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }

        WindowFrame(Submit);

        PressItemAndHold(rectS8, Submit);
        _ctx.IO.AddMousePosEvent((rectS8.Min.X + rectS8.Max.X) * 0.5f + 20.0f, (rectS8.Min.Y + rectS8.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(s8, Is.EqualTo((sbyte)20), "signed 8-bit drag");

        PressItemAndHold(rectS16, Submit);
        _ctx.IO.AddMousePosEvent((rectS16.Min.X + rectS16.Max.X) * 0.5f + 20.0f, (rectS16.Min.Y + rectS16.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(s16, Is.EqualTo((short)20), "signed 16-bit drag");

        PressItemAndHold(rectS64, Submit);
        _ctx.IO.AddMousePosEvent((rectS64.Min.X + rectS64.Max.X) * 0.5f + 20.0f, (rectS64.Min.Y + rectS64.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(s64, Is.EqualTo(20L), "signed 64-bit drag");
    }

    /// <summary>
    /// Verifies that the unsigned 8-bit, 16-bit, and 64-bit <c>DragScalar</c> overloads box the
    /// caller's value, drive the shared drag core, and store the edited value back into the typed
    /// variable.
    /// Canonical <c>DragScalar</c>.
    /// </summary>
    [Test]
    public void DragScalar_UnsignedIntegerWidths_WriteBackThroughTheSharedCore()
    {
        WarmupFrame();
        byte u8 = 0;
        ushort u16 = 0;
        ulong u64 = 0;
        ImRect rectU8 = default;
        ImRect rectU16 = default;
        ImRect rectU64 = default;

        // All three rows are submitted every frame, so each drag presses a distinct row and is not
        // mistaken for a double click (see the signed-width test above).
        void Submit()
        {
            ImGui.DragScalar("##u8", ImGuiDataType.U8, ref u8, 1.0f);
            rectU8 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.DragScalar("##u16", ImGuiDataType.U16, ref u16, 1.0f);
            rectU16 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.DragScalar("##u64", ImGuiDataType.U64, ref u64, 1.0f);
            rectU64 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }

        WindowFrame(Submit);

        PressItemAndHold(rectU8, Submit);
        _ctx.IO.AddMousePosEvent((rectU8.Min.X + rectU8.Max.X) * 0.5f + 20.0f, (rectU8.Min.Y + rectU8.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(u8, Is.EqualTo((byte)20), "unsigned 8-bit drag");

        PressItemAndHold(rectU16, Submit);
        _ctx.IO.AddMousePosEvent((rectU16.Min.X + rectU16.Max.X) * 0.5f + 20.0f, (rectU16.Min.Y + rectU16.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(u16, Is.EqualTo((ushort)20), "unsigned 16-bit drag");

        PressItemAndHold(rectU64, Submit);
        _ctx.IO.AddMousePosEvent((rectU64.Min.X + rectU64.Max.X) * 0.5f + 20.0f, (rectU64.Min.Y + rectU64.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(u64, Is.EqualTo(20UL), "unsigned 64-bit drag");
    }

    /// <summary>
    /// Verifies that the newly added integer <c>SliderScalar</c> overloads clamp the dragged grab to
    /// the declared bounds and write the clamped value back into the typed variable, including the
    /// 64-bit unsigned width whose bounds exceed the range of a machine integer.
    /// Canonical <c>SliderScalar</c>.
    /// </summary>
    [Test]
    public void SliderScalar_IntegerWidths_ClampAtTheDeclaredMaximum()
    {
        WarmupFrame();
        sbyte s8 = 0;
        ulong u64 = 0;
        ImRect rectS8 = default;
        ImRect rectU64 = default;

        // Both rows are submitted every frame so each drag presses a distinct row.
        void Submit()
        {
            ImGui.SliderScalar("##s8s", ImGuiDataType.S8, ref s8, (sbyte)0, (sbyte)10);
            rectS8 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.SliderScalar("##u64s", ImGuiDataType.U64, ref u64, 0UL, 10UL);
            rectU64 = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }

        WindowFrame(Submit);

        PressItemAndHold(rectS8, Submit);
        _ctx.IO.AddMousePosEvent(rectS8.Max.X + 200.0f, (rectS8.Min.Y + rectS8.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(s8, Is.EqualTo((sbyte)10), "signed 8-bit slider clamps to its maximum");

        PressItemAndHold(rectU64, Submit);
        _ctx.IO.AddMousePosEvent(rectU64.Max.X + 200.0f, (rectU64.Min.Y + rectU64.Max.Y) * 0.5f);
        WindowFrame(Submit);
        ReleaseMouseAndFrame(Submit);
        Assert.That(u64, Is.EqualTo(10UL), "unsigned 64-bit slider clamps to its maximum");
    }

    /// <summary>
    /// Verifies that the newly added integer <c>InputScalar</c> overloads parse the typed text with the
    /// canonical 64-bit decimal path and write the parsed value back into the typed variable.
    /// Canonical <c>InputScalar</c>.
    /// </summary>
    [Test]
    public void InputScalar_IntegerWidths_ParseTypedText()
    {
        WarmupFrame();

        sbyte s8 = 0;
        ImRect s8Rect = CaptureItem(() => ImGui.InputScalar("##is8", ImGuiDataType.S8, ref s8));
        ClickItem(s8Rect, () => ImGui.InputScalar("##is8", ImGuiDataType.S8, ref s8));
        TypeText("42", () => ImGui.InputScalar("##is8", ImGuiDataType.S8, ref s8));
        bool released;
        PressKey(ImGuiKey.Enter, () => { ImGui.InputScalar("##is8", ImGuiDataType.S8, ref s8); return false; }, out released);
        Assert.That(s8, Is.EqualTo((sbyte)42));

        ushort u16 = 0;
        ImRect u16Rect = CaptureItem(() => ImGui.InputScalar("##iu16", ImGuiDataType.U16, ref u16));
        ClickItem(u16Rect, () => ImGui.InputScalar("##iu16", ImGuiDataType.U16, ref u16));
        TypeText("42", () => ImGui.InputScalar("##iu16", ImGuiDataType.U16, ref u16));
        PressKey(ImGuiKey.Enter, () => { ImGui.InputScalar("##iu16", ImGuiDataType.U16, ref u16); return false; }, out released);
        Assert.That(u16, Is.EqualTo((ushort)42));

        long s64 = 0;
        ImRect s64Rect = CaptureItem(() => ImGui.InputScalar("##is64", ImGuiDataType.S64, ref s64));
        ClickItem(s64Rect, () => ImGui.InputScalar("##is64", ImGuiDataType.S64, ref s64));
        TypeText("123456789", () => ImGui.InputScalar("##is64", ImGuiDataType.S64, ref s64));
        PressKey(ImGuiKey.Enter, () => { ImGui.InputScalar("##is64", ImGuiDataType.S64, ref s64); return false; }, out released);
        Assert.That(s64, Is.EqualTo(123456789L));

        ulong u64 = 0;
        ImRect u64Rect = CaptureItem(() => ImGui.InputScalar("##iu64", ImGuiDataType.U64, ref u64));
        ClickItem(u64Rect, () => ImGui.InputScalar("##iu64", ImGuiDataType.U64, ref u64));
        TypeText("123456789", () => ImGui.InputScalar("##iu64", ImGuiDataType.U64, ref u64));
        PressKey(ImGuiKey.Enter, () => { ImGui.InputScalar("##iu64", ImGuiDataType.U64, ref u64); return false; }, out released);
        Assert.That(u64, Is.EqualTo(123456789UL));
    }

    #endregion

    #region Canonical printf formatting and password masking

    /// <summary>
    /// Verifies that the canonical printf-style formatter renders the scientific notation used by the
    /// demo's "input scientific" field and by the high-range scalar sliders.
    /// Canonical <c>ImFormatStringV</c> delegates to <c>vsnprintf</c>
    ///., so <c>%e</c> must produce the exponent form.
    /// </summary>
    [Test]
    public void ImFormatString_SupportsScientificNotation()
    {
        Assert.That(ImGui.ImFormatString("%e", 1.0e10f), Is.EqualTo("1.000000e+10"));
        Assert.That(ImGui.ImFormatString("%E", 1.0e10f), Is.EqualTo("1.000000E+10"));
        Assert.That(ImGui.ImFormatString("%.2e", 1234.5f), Is.EqualTo("1.23e+03"));
        Assert.That(ImGui.ImFormatString("%12.3e", 0.000123f), Is.EqualTo("   1.230e-04"));
    }

    /// <summary>
    /// Verifies that a password field renders every character through the asterisk glyph rather than
    /// emitting blank or invisible quads. Canonical swaps the baked lookup tables for one whose
    /// fallback is the asterisk glyph (,
    /// used); the rendered quads must therefore sample the
    /// asterisk's texture rectangle.
    /// </summary>
    [Test]
    public void PasswordInput_MasksEveryGlyphWithTheAsteriskGlyph()
    {
        WarmupFrame();

        // Draw one text row first: this forces the atlas to build (which locks it), so the asterisk can
        // be baked on the following frame when the atlas is unlocked.
        WindowFrame(() => ImGui.Text("0"));
        ImFontBaked baked = ImGui.GetFont()!.GetFontBaked(ImGui.GetFontSize());
        ImFontGlyph asterisk = baked.FindGlyph('*');
        Assert.That(asterisk.Visible, Is.True, "the atlas must contain a visible asterisk glyph");

        string password = "password";
        ImVec2 whiteTexel = ImGui.GetFontTexUvWhitePixel();
        int glyphVertexCount = 0;
        bool everyGlyphUvInsideAsterisk = true;

        WindowFrame(() =>
        {
            ImGui.InputText("##pw", ref password, ImGuiInputTextFlags.Password);
            ImRect itemRect = new(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImDrawList drawList = ImGui.GetWindowDrawList();
            glyphVertexCount = 0;
            everyGlyphUvInsideAsterisk = true;

            // Glyph quads are the vertices inside the frame rectangle that do not sample the atlas white
            // texel used by the frame fill and border.
            foreach (ImDrawVert vertex in drawList.VtxBuffer)
            {
                if (vertex.Pos.X < itemRect.Min.X || vertex.Pos.X > itemRect.Max.X ||
                    vertex.Pos.Y < itemRect.Min.Y || vertex.Pos.Y > itemRect.Max.Y)
                {
                    continue;
                }
                if (Math.Abs(vertex.UV.X - whiteTexel.X) < 1e-6f && Math.Abs(vertex.UV.Y - whiteTexel.Y) < 1e-6f)
                    continue;

                glyphVertexCount++;
                bool inside = vertex.UV.X >= asterisk.U0 - 1e-6f && vertex.UV.X <= asterisk.U1 + 1e-6f &&
                              vertex.UV.Y >= asterisk.V0 - 1e-6f && vertex.UV.Y <= asterisk.V1 + 1e-6f;
                if (!inside)
                    everyGlyphUvInsideAsterisk = false;
            }
        });

        // The masked text must emit one visible quad per character, all sampling the asterisk glyph.
        Assert.That(glyphVertexCount, Is.EqualTo(password.Length * 4), "every password character must render as a visible masked glyph");
        Assert.That(everyGlyphUvInsideAsterisk, Is.True, "every masked glyph must sample the asterisk glyph");
    }

    #endregion

}
