#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the disabled-scope, group-bounds, shortcut-routing, clip-rectangle, and clipboard
/// surfaces of <see cref="ImGui"/> with concrete state assertions.
/// </summary>
/// <remarks>
/// <para>
/// Canonical reference: Dear ImGui <c>v1.92.9b-docking</c>. Disabled scopes are defined by
/// <c>BeginDisabled()</c>/<c>EndDisabled()</c> (<c>ThirdParty/imgui/imgui.cpp:8983-9008</c>),
/// groups by <c>BeginGroup()</c>/<c>EndGroup()</c> (<c>imgui.cpp:12569-12663</c>), shortcut routing by
/// <c>SetNextItemShortcut()</c>/<c>Shortcut()</c> (<c>imgui.cpp:11554-11626</c>) with the route table
/// resolved through <c>UpdateKeyRoutingTable()</c> (<c>imgui.cpp:10399-10448</c>), clip rectangles by
/// <c>PushClipRect()</c>/<c>PopClipRect()</c> (<c>imgui.cpp:6196-6208</c>), and the clipboard by
/// <c>GetClipboardText()</c>/<c>SetClipboardText()</c> (<c>imgui.cpp:5343-5354</c>).
/// </para>
/// <para>
/// Every test drives a real <see cref="ImGuiContext"/>. A shortcut route becomes current only on the
/// frame after it is submitted, so the routing tests submit the route once before the frame that
/// presses the chord.
/// </para>
/// </remarks>
[TestFixture]
public class DisabledAndShortcutTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);

    /// <summary>Window size shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowSize = new(500.0f, 400.0f);


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


    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current, ending the frame so queued input is retired.
    /// </summary>
    /// <param name="body">The item submissions and assertions to run while the window is current.</param>
    private void WindowFrame(Action body)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(WindowSize, ImGuiCond.Always);
        ImGui.Begin("disabled-and-shortcut");
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Submits one throwaway frame so the window exists before the measured frames. The shortcut and
    /// disabled paths do not depend on this, but it keeps the measured item identifiers stable.
    /// </summary>
    private void WarmupFrame()
    {
        WindowFrame(() => { });
    }

    #endregion

    #region Disabled state and group bounds

    /// <summary>
    /// Verifies that a button submitted inside <c>BeginDisabled()</c>/<c>EndDisabled()</c> carries the
    /// disabled item flag, does not report a press for a full press/release click, and that the style
    /// alpha is scaled by <see cref="ImGuiStyle.DisabledAlpha"/> and restored on
    /// <c>EndDisabled()</c>. An enabled button clicked the same way reports a press, which confirms the
    /// disabled result is caused by the scope.
    /// </summary>
    /// <remarks>
    /// Canonical interaction semantics: <c>ItemHoverable()</c> rejects disabled items
    /// (<c>ThirdParty/imgui/imgui.cpp:5150-5151</c>), while <c>BeginDisabled()</c> applies the alpha
    /// (<c>imgui.cpp:8983-8996</c>).
    /// </remarks>
    [Test]
    public void BeginDisabled_ButtonDoesNotPressAndCarriesDisabledFlag()
    {
        WarmupFrame();

        ImGuiStyle style = ImGui.GetStyle();
        float enabledAlpha = style.Alpha;
        float disabledAlphaScale = style.DisabledAlpha;

        ImGuiItemFlags disabledFlags = ImGuiItemFlags.None;
        float alphaWhileDisabled = 0.0f;
        float alphaAfterEnd = 0.0f;
        ImRect disabledRect = default;
        WindowFrame(() =>
        {
            ImGui.BeginDisabled();
            ImGui.Button("##disabled", new ImVec2(120.0f, 40.0f));
            disabledRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            disabledFlags = ImGui.GetItemFlags();
            alphaWhileDisabled = ImGui.GetStyle().Alpha;
            ImGui.EndDisabled();
            alphaAfterEnd = ImGui.GetStyle().Alpha;
        });

        Assert.That((disabledFlags & ImGuiItemFlags.Disabled) != 0, Is.True,
            "The item flag ImGuiItemFlags_Disabled must be set inside a disabled scope.");
        Assert.That(alphaWhileDisabled, Is.EqualTo(enabledAlpha * disabledAlphaScale).Within(1.0e-5f),
            "The style alpha must be scaled by Style.DisabledAlpha inside a disabled scope.");
        Assert.That(alphaAfterEnd, Is.EqualTo(enabledAlpha).Within(1.0e-5f),
            "EndDisabled() must restore the style alpha saved by BeginDisabled().");

        // Click the disabled button. The release frame is the one that would normally report a press,
        // so it is asserted explicitly rather than relying on the press frame.
        _ctx.IO.AddMousePosEvent(
            (disabledRect.Min.X + disabledRect.Max.X) * 0.5f,
            (disabledRect.Min.Y + disabledRect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        bool pressed = false;
        WindowFrame(() =>
        {
            ImGui.BeginDisabled();
            pressed = ImGui.Button("##disabled", new ImVec2(120.0f, 40.0f));
            ImGui.EndDisabled();
        });
        Assert.That(pressed, Is.False, "A disabled button must not report a press on the press frame.");

        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(() =>
        {
            ImGui.BeginDisabled();
            pressed = ImGui.Button("##disabled", new ImVec2(120.0f, 40.0f));
            ImGui.EndDisabled();
        });
        Assert.That(pressed, Is.False, "A disabled button must not report a press on the release frame.");

        // Control: an enabled button clicked the same way reports the release-frame press.
        ImRect enabledRect = default;
        WindowFrame(() =>
        {
            ImGui.Button("##enabled", new ImVec2(120.0f, 40.0f));
            enabledRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        _ctx.IO.AddMousePosEvent(
            (enabledRect.Min.X + enabledRect.Max.X) * 0.5f,
            (enabledRect.Min.Y + enabledRect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        WindowFrame(() => { ImGui.Button("##enabled", new ImVec2(120.0f, 40.0f)); });
        _ctx.IO.AddMouseButtonEvent(0, false);
        bool enabledPressed = false;
        WindowFrame(() => { enabledPressed = ImGui.Button("##enabled", new ImVec2(120.0f, 40.0f)); });
        Assert.That(enabledPressed, Is.True,
            "The enabled control button must report a press, confirming the disabled result is scope-driven.");
    }

    /// <summary>
    /// Verifies that <c>EndGroup()</c> submits a parent item whose rectangle spans the children: its
    /// minimum matches the first child's minimum and its maximum covers the last child's maximum.
    /// </summary>
    /// <remarks>
    /// Canonical <c>EndGroup()</c> folds the group's cursor extent with the last item rectangle and
    /// submits it through <c>ItemSize()</c>/<c>ItemAdd()</c> (<c>ThirdParty/imgui/imgui.cpp:12630-12647</c>).
    /// </remarks>
    [Test]
    public void EndGroup_ItemRectSpansSubmittedChildren()
    {
        WarmupFrame();

        ImVec2 firstMin = default;
        ImVec2 lastMax = default;
        ImVec2 groupMin = default;
        ImVec2 groupMax = default;
        WindowFrame(() =>
        {
            ImGui.BeginGroup();
            ImGui.Button("##first", new ImVec2(90.0f, 24.0f));
            firstMin = ImGui.GetItemRectMin();
            ImGui.Button("##second", new ImVec2(60.0f, 24.0f));
            lastMax = ImGui.GetItemRectMax();
            ImGui.EndGroup();

            groupMin = ImGui.GetItemRectMin();
            groupMax = ImGui.GetItemRectMax();
        });

        Assert.That(groupMin.X, Is.EqualTo(firstMin.X).Within(0.01f),
            "The group rectangle must start at the first child's left edge.");
        Assert.That(groupMin.Y, Is.EqualTo(firstMin.Y).Within(0.01f),
            "The group rectangle must start at the first child's top edge.");
        Assert.That(groupMax.X, Is.GreaterThanOrEqualTo(firstMin.X + 90.0f - 0.01f),
            "The group rectangle must span the widest child.");
        Assert.That(groupMax.Y, Is.GreaterThanOrEqualTo(lastMax.Y - 0.01f),
            "The group rectangle must reach the last child's bottom edge.");
    }

    #endregion

    #region Shortcut routing

    /// <summary>
    /// Verifies that <c>SetNextItemShortcut()</c> activates the following button when the queued chord
    /// is pressed, and does not activate it when the chord is released. The route is submitted on the
    /// preceding frame because canonical resolves a route into the current slot at the next frame
    /// boundary.
    /// </summary>
    /// <remarks>
    /// Canonical <c>ItemHandleShortcut()</c> queues navigation activation for the item's owner when the
    /// chord is pressed (<c>ThirdParty/imgui/imgui.cpp:11562-11585</c>). The focused route is the
    /// default, so this also pins the window focus-scope push performed by <c>Begin()</c>
    /// (<c>imgui.cpp:8048</c>).
    /// </remarks>
    [Test]
    public void SetNextItemShortcut_ActivatesFollowingButtonWhenChordPressed()
    {
        WarmupFrame();

        ImGuiKey chord = ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.S;

        bool pressed = false;
        WindowFrame(() =>
        {
            ImGui.SetNextItemShortcut(chord);
            pressed = ImGui.Button("Save");
        });
        Assert.That(pressed, Is.False, "A shortcut route is not current on the frame that submits it.");

        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        _ctx.IO.AddKeyEvent(ImGuiKey.S, true);
        WindowFrame(() =>
        {
            ImGui.SetNextItemShortcut(chord);
            pressed = ImGui.Button("Save");
        });
        Assert.That(pressed, Is.True,
            "The following button must activate while its shortcut chord is pressed.");

        _ctx.IO.AddKeyEvent(ImGuiKey.S, false);
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, false);
        WindowFrame(() =>
        {
            ImGui.SetNextItemShortcut(chord);
            pressed = ImGui.Button("Save");
        });
        Assert.That(pressed, Is.False,
            "The following button must not activate once the shortcut chord is released.");
    }

    /// <summary>
    /// Verifies that <c>Shortcut()</c> returns true for a matching chord on the frame after its route
    /// is submitted and false once the chord no longer matches.
    /// </summary>
    /// <remarks>
    /// Canonical <c>Shortcut()</c> claims the route and then tests
    /// <c>IsKeyChordPressed()</c> (<c>ThirdParty/imgui/imgui.cpp:11592-11626</c>). A global route is
    /// used so the result does not depend on which window is focused.
    /// </remarks>
    [Test]
    public void Shortcut_ReturnsTrueForMatchingChordAndFalseOtherwise()
    {
        WarmupFrame();

        ImGuiKey chord = ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.A;
        bool shortcutPressed = false;

        WindowFrame(() => { shortcutPressed = ImGui.Shortcut(chord, ImGuiInputFlags.RouteGlobal); });
        Assert.That(shortcutPressed, Is.False, "A global route is not current on the frame that submits it.");

        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        _ctx.IO.AddKeyEvent(ImGuiKey.A, true);
        WindowFrame(() => { shortcutPressed = ImGui.Shortcut(chord, ImGuiInputFlags.RouteGlobal); });
        Assert.That(shortcutPressed, Is.True, "Shortcut() must report a pressed matching chord.");

        // Hold Ctrl but swap the key: the chord no longer matches, so Shortcut() must report false.
        _ctx.IO.AddKeyEvent(ImGuiKey.A, false);
        _ctx.IO.AddKeyEvent(ImGuiKey.B, true);
        WindowFrame(() => { shortcutPressed = ImGui.Shortcut(chord, ImGuiInputFlags.RouteGlobal); });
        Assert.That(shortcutPressed, Is.False, "Shortcut() must report false for a non-matching chord.");

        _ctx.IO.AddKeyEvent(ImGuiKey.B, false);
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, false);
    }

    #endregion

    #region Clip rectangles

    /// <summary>
    /// Verifies that pushing then popping a clip rectangle restores the exact window clip rectangle,
    /// and that an intersecting push is the intersection of the new rectangle and the current one.
    /// </summary>
    /// <remarks>
    /// Canonical <c>PushClipRect()</c> and <c>PopClipRect()</c> maintain the draw-list clip stack and
    /// refresh <c>window->ClipRect</c> from its top (<c>ThirdParty/imgui/imgui.cpp:6196-6208</c>).
    /// </remarks>
    [Test]
    public void ClipRect_PushAndPopRestoreWindowClipRect()
    {
        WarmupFrame();

        ImRect baseClip = default;
        ImRect pushedClip = default;
        ImRect restoredClip = default;
        ImRect secondPushedClip = default;
        ImRect secondRestoredClip = default;
        WindowFrame(() =>
        {
            baseClip = ImGui.GetCurrentWindow().ClipRect;

            ImGui.PushClipRect(
                new ImVec2(baseClip.Min.X + 10.0f, baseClip.Min.Y + 20.0f),
                new ImVec2(baseClip.Max.X - 30.0f, baseClip.Max.Y - 40.0f),
                true);
            pushedClip = ImGui.GetCurrentWindow().ClipRect;
            ImGui.PopClipRect();
            restoredClip = ImGui.GetCurrentWindow().ClipRect;

            // Push/pop a second time to confirm the stack does not drift across the frame.
            ImGui.PushClipRect(
                new ImVec2(baseClip.Min.X + 10.0f, baseClip.Min.Y + 20.0f),
                new ImVec2(baseClip.Max.X - 30.0f, baseClip.Max.Y - 40.0f),
                true);
            secondPushedClip = ImGui.GetCurrentWindow().ClipRect;
            ImGui.PopClipRect();
            secondRestoredClip = ImGui.GetCurrentWindow().ClipRect;
        });

        Assert.That(pushedClip.Min.X, Is.EqualTo(baseClip.Min.X + 10.0f).Within(0.01f));
        Assert.That(pushedClip.Min.Y, Is.EqualTo(baseClip.Min.Y + 20.0f).Within(0.01f));
        Assert.That(pushedClip.Max.X, Is.EqualTo(baseClip.Max.X - 30.0f).Within(0.01f));
        Assert.That(pushedClip.Max.Y, Is.EqualTo(baseClip.Max.Y - 40.0f).Within(0.01f));

        Assert.That(restoredClip.Min.X, Is.EqualTo(baseClip.Min.X).Within(0.01f));
        Assert.That(restoredClip.Min.Y, Is.EqualTo(baseClip.Min.Y).Within(0.01f));
        Assert.That(restoredClip.Max.X, Is.EqualTo(baseClip.Max.X).Within(0.01f));
        Assert.That(restoredClip.Max.Y, Is.EqualTo(baseClip.Max.Y).Within(0.01f));

        Assert.That(secondRestoredClip.Min.X, Is.EqualTo(baseClip.Min.X).Within(0.01f));
        Assert.That(secondRestoredClip.Min.Y, Is.EqualTo(baseClip.Min.Y).Within(0.01f));
        Assert.That(secondRestoredClip.Max.X, Is.EqualTo(baseClip.Max.X).Within(0.01f));
        Assert.That(secondRestoredClip.Max.Y, Is.EqualTo(baseClip.Max.Y).Within(0.01f));
        Assert.That(secondPushedClip.Min.X, Is.EqualTo(pushedClip.Min.X).Within(0.01f));
        Assert.That(secondPushedClip.Max.Y, Is.EqualTo(pushedClip.Max.Y).Within(0.01f));
    }

    #endregion

    #region Clipboard

    /// <summary>
    /// Verifies that <c>SetClipboardText()</c> and <c>GetClipboardText()</c> round-trip through the
    /// installed host callbacks, forwarding the user data unchanged, and that a missing getter yields
    /// an empty string.
    /// </summary>
    /// <remarks>
    /// Canonical forwards to <c>PlatformIO</c>'s clipboard callbacks
    /// (<c>ThirdParty/imgui/imgui.cpp:5343-5354</c>); this port reads the equivalent
    /// <see cref="ImGuiIO"/> callbacks until the platform interface lands in Chunk 11.
    /// </remarks>
    [Test]
    public void Clipboard_SetAndGetRoundTripThroughHostCallbacks()
    {
        string stored = string.Empty;
        object? seenUserData = null;
        _ctx.IO.ClipboardUserData = this;
        _ctx.IO.SetClipboardTextFn = (userData, text) =>
        {
            seenUserData = userData;
            stored = text;
        };
        _ctx.IO.GetClipboardTextFn = userData =>
        {
            seenUserData = userData;
            return stored;
        };

        ImGui.SetClipboardText("hikari clipboard");
        Assert.That(stored, Is.EqualTo("hikari clipboard"),
            "SetClipboardText() must forward the text to the host setter.");
        Assert.That(seenUserData, Is.SameAs(this),
            "The clipboard callbacks must receive ClipboardUserData unchanged.");
        Assert.That(ImGui.GetClipboardText(), Is.EqualTo("hikari clipboard"),
            "GetClipboardText() must round-trip the text stored by SetClipboardText().");

        ImGui.SetClipboardText(string.Empty);
        Assert.That(ImGui.GetClipboardText(), Is.Empty,
            "An empty stored clipboard value must round-trip as an empty string.");

        _ctx.IO.GetClipboardTextFn = null;
        Assert.That(ImGui.GetClipboardText(), Is.Empty,
            "GetClipboardText() must return an empty string when no host getter is installed.");
    }

    #endregion
}
