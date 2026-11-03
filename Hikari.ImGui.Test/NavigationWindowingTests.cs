// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the Ctrl+Tab navigation windowing state machine ported from canonical <c>NavUpdateWindowing</c>
///. and its overlay.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/> through full frames. Keyboard input is injected with
/// <see cref="ImGuiIO.AddKeyEvent(ImGuiKey, bool)"/> so that the shortcut routing runs as in an application.
/// </remarks>
[TestFixture]
public class NavigationWindowingTests : ImGuiTestFixture
{

    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
    }


    /// <summary>Submits a frame containing two named windows, each with a single button.</summary>
    private static void TwoWindowFrame()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(20.0f, 20.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 150.0f), ImGuiCond.Always);
        ImGui.Begin("Alpha");
        ImGui.Button("A");
        ImGui.End();
        ImGui.SetNextWindowPos(new ImVec2(300.0f, 20.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 150.0f), ImGuiCond.Always);
        ImGui.Begin("Beta");
        ImGui.Button("B");
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that holding Ctrl and pressing Tab starts windowing and previews the next window without
    /// yet changing the focused window.
    /// </summary>
    [Test]
    public void CtrlTab_StartsWindowingAndPreviewsTarget()
    {
        TwoWindowFrame();
        TwoWindowFrame();
        ImGuiWindow? focusedBefore = _ctx.NavWindow;
        Assert.That(focusedBefore, Is.Not.Null);

        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        _ctx.IO.AddKeyEvent(ImGuiKey.Tab, true);
        TwoWindowFrame();

        Assert.That(_ctx.NavWindowingTarget, Is.Not.Null, "Ctrl+Tab must start windowing.");
        Assert.That(_ctx.NavWindowingInputSource, Is.EqualTo(ImGuiInputSource.Keyboard));
        Assert.That(_ctx.NavWindowingTarget!.Name, Is.Not.EqualTo(focusedBefore!.Name),
            "The preview target must differ from the focused window when another navigable window exists.");
    }

    /// <summary>
    /// Verifies that releasing Ctrl applies the previewed window as the focused window and clears the target
    /// (, ).
    /// </summary>
    [Test]
    public void ReleasingCtrl_AppliesFocusToPreviewedWindow()
    {
        TwoWindowFrame();
        TwoWindowFrame();

        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        _ctx.IO.AddKeyEvent(ImGuiKey.Tab, true);
        TwoWindowFrame();
        string previewed = _ctx.NavWindowingTarget!.Name;

        _ctx.IO.AddKeyEvent(ImGuiKey.Tab, false);
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, false);
        TwoWindowFrame();
        TwoWindowFrame();

        Assert.That(_ctx.NavWindowingTarget, Is.Null, "Releasing Ctrl must end windowing.");
        Assert.That(_ctx.NavWindow, Is.Not.Null);
        Assert.That(_ctx.NavWindow!.RootWindow!.Name, Is.EqualTo(previewed));
    }

    /// <summary>
    /// Verifies that windowing does not start while a modal popup is open, because canonical
    /// <c>NavUpdateWindowing</c> clears the target when <c>GetTopMostPopupModal()</c> is non-null
    /// </summary>
    [Test]
    public void CtrlTab_DoesNotStartWhileModalIsOpen()
    {
        // Open the modal and keep submitting it on every frame, as the existing modal test does, so that it
        // stays on the open-popup stack for the windowing frames.
        ModalFrame(open: true);
        ModalFrame(open: true);
        Assert.That(GetTopMostPopupModalCount(), Is.EqualTo(1), "Precondition: the modal must be open.");

        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        _ctx.IO.AddKeyEvent(ImGuiKey.Tab, true);
        ModalFrame(open: true);
        ModalFrame(open: true);

        Assert.That(_ctx.NavWindowingTarget, Is.Null, "A modal must suppress Ctrl+Tab windowing.");
    }

    /// <summary>Runs one frame with a base window and, when <paramref name="open"/> is set, a modal popup.</summary>
    /// <param name="open">Whether the modal popup is opened and submitted this frame.</param>
    private static void ModalFrame(bool open)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(20.0f, 20.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 150.0f), ImGuiCond.Always);
        ImGui.Begin("Base");
        ImGui.End();
        if (open)
        {
            ImGui.OpenPopup("Modal");
            if (ImGui.BeginPopupModal("Modal"))
                ImGui.EndPopup();
        }
        ImGui.Render();
    }

    /// <summary>Counts open popup-stack entries flagged as modal, as the canonical modal lookup does.</summary>
    /// <returns>The number of open modal popups.</returns>
    private int GetTopMostPopupModalCount()
    {
        int count = 0;
        foreach (ImGuiPopupData popup in _ctx.OpenPopupStack)
            if (popup.Window != null && (popup.Window.Flags & ImGuiWindowFlags.Modal) != 0)
                count++;
        return count;
    }

    /// <summary>
    /// Verifies that the window-list overlay is hidden until the list delay has elapsed
    /// (, delay <c>NAV_WINDOWING_LIST_APPEAR_DELAY = 0.15</c>).
    /// </summary>
    [Test]
    public void WindowingOverlay_AppearsOnlyAfterListDelay()
    {
        TwoWindowFrame();
        TwoWindowFrame();

        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Ctrl, true);
        _ctx.IO.AddKeyEvent(ImGuiKey.Tab, true);
        TwoWindowFrame();
        Assert.That(_ctx.NavWindowingListWindow, Is.Null, "The list must not appear on the first frame.");

        // Hold the keys for more than the 0.15 s delay at 60 fps (about 10 frames).
        for (int n = 0; n < 12; n++)
            TwoWindowFrame();
        Assert.That(_ctx.NavWindowingListWindow, Is.Not.Null, "The list appears after the delay.");
    }
}
