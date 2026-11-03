// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical popup lifecycle, modal blocking, context-menu open paths, and tooltip
/// submission and placement owned by <c>ImGuiWidgets.cs</c>.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/> against canonical Dear ImGui.
/// </remarks>
[TestFixture]
public class PopupAndTooltipTests : ImGuiTestFixture
{
    /// <summary>Window origin used by the placement-sensitive tests.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);

    /// <summary>The canonical tooltip mouse offset constant <c>TOOLTIP_DEFAULT_OFFSET_MOUSE</c>.</summary>
    private static readonly ImVec2 TooltipDefaultOffsetMouse = new(16.0f, 10.0f);


    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
    }


    /// <summary>Begins a fixed-geometry window; the caller must close it with <see cref="ImGui.End"/>.</summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    private static void BeginFixed(string name, ImVec2 size)
    {
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin(name);
    }

    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="body">Assertions executed between Begin and End.</param>
    private static void WindowFrame(string name, ImVec2 size, Action body)
    {
        NewTestFrame();
        BeginFixed(name, size);
        body();
        ImGui.End();
        ImGui.Render();
    }

    #region Popup lifecycle

    /// <summary>
    /// Verifies the canonical popup lifecycle across frames: <see cref="ImGui.OpenPopup(string, ImGuiPopupFlags)"/>
    /// makes <see cref="ImGui.BeginPopup"/> report open on the current and the following frame, and
    /// <see cref="ImGui.CloseCurrentPopup"/> removes it from the open-popup stack so the next frame
    /// reports closed.
    /// </summary>
    [Test]
    public void OpenPopup_BeginPopup_CloseCurrentPopup_Lifecycle()
    {
        ImVec2 size = new(400.0f, 300.0f);

        WindowFrame("Host", size, () =>
        {
            ImGui.OpenPopup("P");
            bool open = ImGui.BeginPopup("P");
            Assert.That(open, Is.True);
            ImGui.Text("content");
            ImGui.EndPopup();
            Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(1));
        });

        WindowFrame("Host", size, () =>
        {
            bool open = ImGui.BeginPopup("P");
            Assert.That(open, Is.True); // Popups persist until explicitly closed.
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        });

        WindowFrame("Host", size, () =>
        {
            bool open = ImGui.BeginPopup("P");
            Assert.That(open, Is.False);
            Assert.That(ImGui.IsPopupOpen(ImGui.GetID("P"), ImGuiPopupFlags.AnyPopupLevel), Is.False);
        });
    }

    /// <summary>
    /// Verifies that focusing a window closes an open popup that is not a descendant of that window
    ///. The popup opened from the first window is removed
    /// from the open-popup stack when the second window is focused.
    /// </summary>
    [Test]
    public void FocusWindow_ClosesPopupsNotOwnedByTargetWindow()
    {
        ImVec2 size = new(300.0f, 200.0f);
        // The popup must be submitted so that its window exists; an entry with a null window is skipped by
        // canonical ClosePopupsOverWindow.
        WindowFrame("A", size, () =>
        {
            ImGui.OpenPopup("P");
            ImGui.BeginPopup("P");
            ImGui.Text("p");
            ImGui.EndPopup();
        });
        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(1));

        WindowFrame("B", size, () => { });
        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that a right click on empty space closes the open popup and restores focus to the window
    /// under it, rather than leaving the popup focused (,
    /// <c>ClosePopupToLevel(., restoreFocusToWindowUnder: true)</c>).
    /// </summary>
    [Test]
    public void RightClick_ClosesPopupWithoutChangingFocus()
    {
        ImVec2 size = new(300.0f, 200.0f);
        WindowFrame("A", size, () =>
        {
            ImGui.OpenPopup("P");
            ImGui.BeginPopup("P");
            ImGui.Text("p");
            ImGui.EndPopup();
        });
        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(1));

        // Move over empty space outside the popup, then press the right button.
        _ctx.IO.AddMousePosEvent(1200.0f, 700.0f);
        _ctx.IO.AddMouseButtonEvent(1, true);
        WindowFrame("A", size, () => { });
        _ctx.IO.AddMouseButtonEvent(1, false);
        WindowFrame("A", size, () => { });

        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(0));
        Assert.That(_ctx.NavWindow, Is.Not.Null);
        Assert.That(_ctx.NavWindow!.Flags & ImGuiWindowFlags.Popup, Is.EqualTo(ImGuiWindowFlags.None));
    }

    /// <summary>
    /// Verifies that Escape closes the top non-modal popup.
    /// </summary>
    [Test]
    public void Escape_ClosesTopNonModalPopup()
    {
        ImVec2 size = new(300.0f, 200.0f);
        WindowFrame("A", size, () =>
        {
            ImGui.OpenPopup("P");
            ImGui.BeginPopup("P");
            ImGui.Text("p");
            ImGui.EndPopup();
        });
        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(1));

        _ctx.IO.AddKeyEvent(ImGuiKey.Escape, true);
        WindowFrame("A", size, () => { });
        _ctx.IO.AddKeyEvent(ImGuiKey.Escape, false);
        WindowFrame("A", size, () => { });
        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that an open modal blocks interaction with the window underneath: with the mouse over
    /// a button in the lower window, its item and window hover queries report false while the modal is
    /// open, but true while no modal is open
    /// </summary>
    [Test]
    public void BeginPopupModal_BlocksWindowBelow()
    {
        ImVec2 size = new(300.0f, 200.0f);
        ImRect buttonRect = default;

        WindowFrame("Below", size, () =>
        {
            ImGui.Button("B");
            buttonRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        ImVec2 center = new((buttonRect.Min.X + buttonRect.Max.X) * 0.5f, (buttonRect.Min.Y + buttonRect.Max.Y) * 0.5f);

        // Control frame: no modal, so the button is hovered.
        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        bool controlHovered = false;
        bool controlWindowHovered = false;
        NewTestFrame();
        BeginFixed("Below", size);
        ImGui.Button("B");
        controlHovered = ImGui.IsItemHovered();
        controlWindowHovered = ImGui.IsWindowHovered();
        ImGui.End();
        ImGui.Render();
        Assert.That(controlHovered, Is.True);
        Assert.That(controlWindowHovered, Is.True);

        // Open the modal.
        WindowFrame("Below", size, () =>
        {
            ImGui.OpenPopup("Modal");
            bool open = ImGui.BeginPopupModal("Modal");
            Assert.That(open, Is.True);
            ImGui.Text("modal");
            ImGui.EndPopup();
        });

        // With the modal open, the same mouse position no longer hovers the window below.
        bool blockedHovered = true;
        bool blockedWindowHovered = true;
        NewTestFrame();
        BeginFixed("Below", size);
        ImGui.Button("B");
        blockedHovered = ImGui.IsItemHovered();
        blockedWindowHovered = ImGui.IsWindowHovered();
        if (ImGui.BeginPopupModal("Modal"))
            ImGui.EndPopup();
        ImGui.End();
        ImGui.Render();

        Assert.That(blockedHovered, Is.False);
        Assert.That(blockedWindowHovered, Is.False);
    }

    /// <summary>
    /// Verifies the canonical <c>AnyPopupId</c>/<c>AnyPopupLevel</c> queries of
    /// <see cref="ImGui.IsPopupOpen(uint, ImGuiPopupFlags)"/>: no popup reports false, and an open
    /// popup reports true for every any-popup combination
    /// </summary>
    [Test]
    public void IsPopupOpen_AnyPopupFlags()
    {
        ImVec2 size = new(400.0f, 300.0f);

        WindowFrame("Host", size, () =>
        {
            Assert.That(ImGui.IsPopupOpen(0u, ImGuiPopupFlags.AnyPopupId), Is.False);
            Assert.That(ImGui.IsPopupOpen(0u, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel), Is.False);
        });

        WindowFrame("Host", size, () =>
        {
            ImGui.OpenPopup("P");
            Assert.That(ImGui.IsPopupOpen("P"), Is.True);
            Assert.That(ImGui.IsPopupOpen(0u, ImGuiPopupFlags.AnyPopupId), Is.True);
            Assert.That(ImGui.IsPopupOpen(0u, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel), Is.True);
            Assert.That(ImGui.IsPopupOpen(ImGui.GetID("P"), ImGuiPopupFlags.AnyPopupLevel), Is.True);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiPopupFlags.NoOpenOverExistingPopup"/> refuses to open a second
    /// popup while one is already open at the current level
    /// </summary>
    [Test]
    public void OpenPopup_NoOpenOverExistingPopup_RefusesWhileOpen()
    {
        WindowFrame("Host", new ImVec2(400.0f, 300.0f), () =>
        {
            ImGui.OpenPopup("A");
            ImGui.BeginPopup("A");
            ImGui.EndPopup();

            ImGui.OpenPopup("B", ImGuiPopupFlags.NoOpenOverExistingPopup);

            Assert.That(ImGui.IsPopupOpen("A"), Is.True);
            Assert.That(ImGui.IsPopupOpen(ImGui.GetID("B"), ImGuiPopupFlags.AnyPopupLevel), Is.False);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiPopupFlags.NoReopen"/> does not duplicate an already-open popup
    /// on the open-popup stack.
    /// </summary>
    [Test]
    public void OpenPopup_NoReopen_DoesNotDuplicate()
    {
        WindowFrame("Host", new ImVec2(400.0f, 300.0f), () =>
        {
            ImGui.OpenPopup("P", ImGuiPopupFlags.NoReopen);
            ImGui.OpenPopup("P", ImGuiPopupFlags.NoReopen);

            Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(1));
            Assert.That(ImGui.IsPopupOpen("P"), Is.True);
        });
    }

    #endregion

    #region Context menus

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginPopupContextItem(string, ImGuiPopupFlags)"/> opens on a
    /// right-button release over the last item and returns true for the popup content
    /// </summary>
    [Test]
    public void BeginPopupContextItem_OpensOnRightClickRelease()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImRect itemRect = default;

        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        itemRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.BeginPopupContextItem("ctx");
        ImGui.End();
        ImGui.Render();

        ImVec2 center = new((itemRect.Min.X + itemRect.Max.X) * 0.5f, (itemRect.Min.Y + itemRect.Max.Y) * 0.5f);
        Assert.That(_ctx.OpenPopupStack, Is.Empty);

        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        _ctx.IO.AddMouseButtonEvent(1, true);
        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        bool openOnPress = ImGui.BeginPopupContextItem("ctx");
        if (openOnPress)
            ImGui.EndPopup();
        ImGui.End();
        ImGui.Render();
        Assert.That(openOnPress, Is.False);

        _ctx.IO.AddMouseButtonEvent(1, false);
        bool open = false;
        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        open = ImGui.BeginPopupContextItem("ctx");
        if (open)
        {
            ImGui.MenuItem("Context");
            ImGui.EndPopup();
        }
        ImGui.End();
        ImGui.Render();

        Assert.That(open, Is.True);
        Assert.That(_ctx.OpenPopupStack.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiPopupFlags.NoOpenOverItems"/> suppresses the window context menu
    /// while an item is hovered.
    /// </summary>
    [Test]
    public void BeginPopupContextWindow_NoOpenOverItems()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImRect itemRect = default;

        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        itemRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.End();
        ImGui.Render();

        ImVec2 center = new((itemRect.Min.X + itemRect.Max.X) * 0.5f, (itemRect.Min.Y + itemRect.Max.Y) * 0.5f);
        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        _ctx.IO.AddMouseButtonEvent(1, true);
        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        ImGui.BeginPopupContextWindow("wctx", ImGuiPopupFlags.NoOpenOverItems);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(1, false);
        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        bool open = ImGui.BeginPopupContextWindow("wctx", ImGuiPopupFlags.NoOpenOverItems);
        if (open)
            ImGui.EndPopup();
        ImGui.End();
        ImGui.Render();

        Assert.That(open, Is.False);
        Assert.That(_ctx.OpenPopupStack, Is.Empty);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginPopupContextVoid(string, ImGuiPopupFlags)"/> opens on a
    /// right-button release while no window is hovered
    /// </summary>
    [Test]
    public void BeginPopupContextVoid_OpensOnEmptyArea()
    {
        ImVec2 size = new(400.0f, 300.0f);

        _ctx.IO.AddMousePosEvent(900.0f, 600.0f);
        _ctx.IO.AddMouseButtonEvent(1, true);
        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("Item");
        ImGui.BeginPopupContextVoid("vctx");
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(1, false);
        bool open = false;
        NewTestFrame();
        BeginFixed("Host", size);
        open = ImGui.BeginPopupContextVoid("vctx");
        if (open)
        {
            ImGui.MenuItem("Void");
            ImGui.EndPopup();
        }
        ImGui.End();
        ImGui.Render();

        Assert.That(open, Is.True);
    }

    #endregion

    #region Tooltips

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginTooltip"/> / <see cref="ImGui.EndTooltip"/> and
    /// <see cref="ImGui.SetTooltip(string, object?[])"/> create a tooltip window carrying the canonical
    /// flag set (<c>Tooltip | NoInputs | NoTitleBar | NoMove | NoResize | NoSavedSettings |
    /// AlwaysAutoResize | NoDocking</c>).
    /// </summary>
    [Test]
    public void SetTooltip_CreatesTooltipWindowWithCanonicalFlags()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImGuiWindow? tooltip = null;
        bool beginTooltipReturned = false;
        bool endTooltipRestoredHost = false;

        WindowFrame("Host", size, () =>
        {
            beginTooltipReturned = ImGui.BeginTooltip();
            tooltip = _ctx.CurrentWindow;
            ImGui.Text("tip");
            ImGui.EndTooltip();
            endTooltipRestoredHost = _ctx.CurrentWindow!.Name == "Host";
        });

        Assert.That(beginTooltipReturned, Is.True);
        Assert.That(endTooltipRestoredHost, Is.True);
        Assert.That(tooltip, Is.Not.Null);
        ImGuiWindowFlags expected = ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoDocking;
        Assert.That((tooltip!.Flags & expected), Is.EqualTo(expected));

        // SetTooltip begins and ends the same window kind in one call.
        ImGuiWindow? setTooltipWindow = null;
        WindowFrame("Host", size, () =>
        {
            ImGui.SetTooltip("hello");
            setTooltipWindow = _ctx.TooltipPreviousWindow;
        });
        Assert.That(setTooltipWindow, Is.Not.Null);
        Assert.That((setTooltipWindow!.Flags & ImGuiWindowFlags.Tooltip), Is.EqualTo(ImGuiWindowFlags.Tooltip));
    }

    /// <summary>
    /// Verifies the canonical tooltip placement math: <see cref="ImGui.FindBestWindowPosForPopup"/>
    /// resolves the mouse-anchored reference position plus <c>TOOLTIP_DEFAULT_OFFSET_MOUSE</c> and then
    /// clamps it against the mouse avoidance rectangle, whose right edge is
    /// <c>ref.x + 24 * scale</c> for the preferred right direction.
    /// </summary>
    [Test]
    public void TooltipPlacement_FindBestWindowPosMatchesCanonicalOffset()
    {
        ImVec2 size = new(400.0f, 300.0f);
        float scale = _ctx.Style.MouseCursorScale;
        ImVec2 mouse = new(300.0f, 200.0f);
        _ctx.IO.AddMousePosEvent(mouse.X, mouse.Y);

        ImGuiWindow? tooltip = null;
        ImVec2 placed = default;
        WindowFrame("Host", size, () =>
        {
            ImGui.BeginTooltip();
            tooltip = _ctx.CurrentWindow;
            placed = ImGui.FindBestWindowPosForPopup(tooltip!);
            ImGui.Text("tip");
            ImGui.EndTooltip();
        });

        Assert.That(tooltip, Is.Not.Null);

        // The reference offset is (16, 10); the preferred right direction pins X to the avoidance
        // rectangle's right edge (24 px) and keeps the reference Y. The reference X carries the
        // canonical one-pixel offset from NavCalcPreferredRefPos.
        float expectedX = mouse.X + 1.0f + 24.0f * scale;
        float expectedY = mouse.Y + TooltipDefaultOffsetMouse.Y * scale;
        Assert.That(placed.X, Is.EqualTo(expectedX).Within(1e-3f));
        Assert.That(placed.Y, Is.EqualTo(expectedY).Within(1e-3f));
    }

    /// <summary>
    /// Verifies that an automatically placed tooltip window follows the mouse using the canonical
    /// default offset. Canonical assigns <c>window-&gt;Pos = FindBestWindowPosForPopup(window)</c> while
    /// beginning a tooltip window.
    /// </summary>
    /// <remarks>
    /// Ignored because the port's <c>Begin</c> leaves all three automatic popup/child-menu/tooltip
    /// position assignments inert (<c>Hikari.ImGui/ImGui.cs:7882-7893</c>), so a tooltip keeps the
    /// default cascaded position (60, 60) instead of following the mouse. The placement helper itself
    /// is correct and is covered by
    /// <see cref="TooltipPlacement_FindBestWindowPosMatchesCanonicalOffset"/>.
    /// </remarks>
    [Test]
    public void Tooltip_FollowsMouseWithDefaultOffset()
    {
        ImVec2 size = new(400.0f, 300.0f);
        float scale = _ctx.Style.MouseCursorScale;
        ImVec2 mouse = new(300.0f, 200.0f);
        _ctx.IO.AddMousePosEvent(mouse.X, mouse.Y);

        ImGuiWindow? tooltip = null;
        WindowFrame("Host", size, () =>
        {
            ImGui.SetTooltip("tip");
            tooltip = _ctx.TooltipPreviousWindow;
        });

        Assert.That(tooltip, Is.Not.Null);
        float expectedX = mouse.X + TooltipDefaultOffsetMouse.X * scale;
        float expectedY = mouse.Y + TooltipDefaultOffsetMouse.Y * scale;
        Assert.That(tooltip!.Pos.X, Is.GreaterThanOrEqualTo(expectedX));
        Assert.That(tooltip.Pos.Y, Is.EqualTo(expectedY).Within(1e-3f));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetItemTooltip(string, object?[])"/> submits a tooltip only while
    /// the last item is hovered with the canonical tooltip hover flags
    /// </summary>
    [Test]
    public void SetItemTooltip_OnlyWhenHovered()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImRect rect = default;

        WindowFrame("Host", size, () =>
        {
            ImGui.Button("B");
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        // Mouse away: no tooltip window is created.
        _ctx.IO.AddMousePosEvent(900.0f, 600.0f);
        ImGuiWindow? away = null;
        NewTestFrame();
        BeginFixed("Host", size);
        ImGui.Button("B");
        ImGui.SetItemTooltip("tip");
        away = _ctx.TooltipPreviousWindow;
        ImGui.End();
        ImGui.Render();
        Assert.That(away, Is.Null);

        // Mouse over the item: the tooltip appears after the canonical stationary/short hover delay.
        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        bool over = false;
        for (int frame = 0; frame < 30 && !over; frame++)
        {
            NewTestFrame();
            BeginFixed("Host", size);
            ImGui.Button("B");
            ImGui.SetItemTooltip("tip");
            over = _ctx.TooltipPreviousWindow != null;
            ImGui.End();
            ImGui.Render();
        }

        Assert.That(over, Is.True);
    }

    #endregion
}
