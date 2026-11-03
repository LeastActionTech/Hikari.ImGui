// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical window pipeline: Begin/End lifetime and persistence, window state
/// queries, identifier hashing parity, next-window positioning and sizing, child windows, display
/// layering, and auto-fit minimum sizing.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/> against canonical Dear ImGui.
/// </remarks>
[TestFixture]
public class WindowTests : ImGuiTestFixture
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


    /// <summary>Starts and finishes one full frame so queued input is applied and retired.</summary>
    private static void Frame()
    {
        NewTestFrame();
        ImGui.Render();
    }

    /// <summary>
    /// Runs one frame that begins and ends a single window and invokes <paramref name="body"/> while
    /// the window is current.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="body">Assertions executed between Begin and End.</param>
    private static void WindowFrame(string name, Action body)
    {
        NewTestFrame();
        ImGui.Begin(name);
        body();
        ImGui.End();
        ImGui.Render();
    }

    #region Window lifetime and state

    /// <summary>
    /// Verifies that <see cref="ImGui.Begin"/> returns true for a fresh window, that
    /// <see cref="ImGui.End"/> pops the window stack, and that the window is addressable by name.
    /// </summary>
    [Test]
    public void Window_BeginEnd_ReturnsTrueAndPopsStack()
    {
        NewTestFrame();

        bool open = ImGui.Begin("Basic");

        Assert.That(open, Is.True);
        Assert.That(ImGui.FindWindowByName("Basic"), Is.Not.Null);
        Assert.That(_ctx.CurrentWindowStack.Count, Is.EqualTo(2)); // The implicit fallback plus the user window.

        ImGui.End();

        Assert.That(_ctx.CurrentWindowStack.Count, Is.EqualTo(1));
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that a window object persists across frames and is returned as the same instance by
    /// <see cref="ImGui.FindWindowByName"/>.
    /// </summary>
    [Test]
    public void Window_PersistsAcrossFrames_SameInstance()
    {
        WindowFrame("Persist", () => { });
        ImGuiWindow first = ImGui.FindWindowByName("Persist")!;

        WindowFrame("Persist", () => { });
        ImGuiWindow second = ImGui.FindWindowByName("Persist")!;

        Assert.That(second, Is.SameAs(first));
        Assert.That(first.ID, Is.Not.EqualTo(0u));
        Assert.That(first.Active, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.IsWindowAppearing"/> is true only on the first frame a window
    /// is submitted.
    /// </summary>
    [Test]
    public void Window_IsWindowAppearing_OnlyOnFirstFrame()
    {
        WindowFrame("Appearing", () => Assert.That(ImGui.IsWindowAppearing(), Is.True));
        WindowFrame("Appearing", () => Assert.That(ImGui.IsWindowAppearing(), Is.False));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextWindowCollapsed"/> with
    /// <see cref="ImGuiCond.Always"/> drives <see cref="ImGui.IsWindowCollapsed"/> on the next
    /// Begin.
    /// </summary>
    [Test]
    public void Window_IsWindowCollapsed_TracksNextCollapsed()
    {
        WindowFrame("Collapse", () => Assert.That(ImGui.IsWindowCollapsed(), Is.False));

        NewTestFrame();
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.Always);
        ImGui.Begin("Collapse");
        Assert.That(ImGui.IsWindowCollapsed(), Is.True);
        ImGui.End();
        ImGui.Render();

        NewTestFrame();
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        ImGui.Begin("Collapse");
        Assert.That(ImGui.IsWindowCollapsed(), Is.False);
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextWindowFocus"/> makes
    /// <see cref="ImGui.IsWindowFocused()"/> report the window on the next Begin.
    /// </summary>
    [Test]
    public void Window_IsWindowFocused_AfterSetNextWindowFocus()
    {
        WindowFrame("Focus", () => { });

        NewTestFrame();
        ImGui.SetNextWindowFocus();
        ImGui.Begin("Focus");
        Assert.That(ImGui.IsWindowFocused(), Is.True);
        Assert.That(ImGui.IsWindowFocused(ImGuiFocusedFlags.AnyWindow), Is.True);
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.IsWindowHovered"/> reports the window when the simulated mouse
    /// position lies inside its rectangle. Hover is resolved at the start of a frame from the window
    /// rectangle produced by the previous frame, so the geometry is established one frame earlier.
    /// </summary>
    [Test]
    public void Window_IsWindowHovered_WhenMouseInside()
    {
        _ctx.IO.AddMousePosEvent(150.0f, 150.0f);

        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 200.0f), ImGuiCond.Always);
        ImGui.Begin("Hover");
        Assert.That(ImGui.IsWindowHovered(), Is.False); // Hover was computed before this window existed.
        ImGui.End();
        ImGui.Render();

        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 200.0f), ImGuiCond.Always);
        ImGui.Begin("Hover");
        Assert.That(ImGui.GetWindowPos(), Is.EqualTo(new ImVec2(100.0f, 100.0f)));
        Assert.That(ImGui.GetWindowSize(), Is.EqualTo(new ImVec2(200.0f, 200.0f)));
        Assert.That(ImGui.IsWindowHovered(), Is.True);
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that an empty auto-fit window never shrinks below
    /// <see cref="ImGuiStyle.WindowMinSize"/>.
    /// </summary>
    [Test]
    public void Window_AutoFitSize_RespectsWindowMinSize()
    {
        WindowFrame("Empty", () => { });
        WindowFrame("Empty", () =>
        {
            ImVec2 size = ImGui.GetWindowSize();
            Assert.That(size.X, Is.GreaterThanOrEqualTo(_ctx.Style.WindowMinSize.X));
            Assert.That(size.Y, Is.GreaterThanOrEqualTo(_ctx.Style.WindowMinSize.Y));
        });
    }

    #endregion

    #region Hashing and identifiers

    /// <summary>
    /// Verifies the canonical CRC32 hash values, including the <c>###</c> reset rule that restarts
    /// the accumulator from the seed.
    /// </summary>
    [Test]
    public void Hash_ImHashStr_MatchesCanonicalValues()
    {
        Assert.That(ImGui.ImHashStr("Window"), Is.EqualTo(0xFA2EAA04u));
        Assert.That(ImGui.ImHashStr("Hello"), Is.EqualTo(0x81D90E1Bu));
        Assert.That(ImGui.ImHashStr("Hello###World"), Is.EqualTo(0x2A023419u));
        Assert.That(ImGui.ImHashStr("###World"), Is.EqualTo(0x2A023419u));
        Assert.That(ImGui.ImHashStr("a###b"), Is.EqualTo(ImGui.ImHashStr("b")));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.PushID(string)"/> chains the identifier seed so the same label
    /// resolves to a different identifier until <see cref="ImGui.PopID"/> restores the seed.
    /// </summary>
    [Test]
    public void Hash_PushID_ChainsSeed()
    {
        NewTestFrame();
        ImGui.Begin("Ids");

        uint noPush = ImGui.GetID("Widget");

        ImGui.PushID("scope");
        uint pushed = ImGui.GetID("Widget");
        ImGui.PopID();

        uint afterPop = ImGui.GetID("Widget");

        Assert.That(pushed, Is.Not.EqualTo(noPush));
        Assert.That(afterPop, Is.EqualTo(noPush));

        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.GetID(string)"/> is stable for the same string, in the same
    /// identifier-stack position, across frames.
    /// </summary>
    [Test]
    public void Hash_GetID_StableAcrossFrames()
    {
        uint first = 0;
        uint second = 0;

        NewTestFrame();
        ImGui.Begin("Stable");
        first = ImGui.GetID("Item");
        ImGui.End();
        ImGui.Render();

        NewTestFrame();
        ImGui.Begin("Stable");
        second = ImGui.GetID("Item");
        ImGui.End();
        ImGui.Render();

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Is.Not.EqualTo(0u));
    }

    #endregion

    #region Placement, sizing, and constraints

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextWindowPos(ImVec2, ImGuiCond, ImVec2)"/> and
    /// <see cref="ImGui.SetNextWindowSize(ImVec2, ImGuiCond)"/> with
    /// <see cref="ImGuiCond.Always"/> are honored by the next Begin.
    /// </summary>
    [Test]
    public void Window_SetNextPosAndSize_AlwaysHonored()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(120.0f, 80.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(320.0f, 240.0f), ImGuiCond.Always);
        ImGui.Begin("Placed");

        Assert.That(ImGui.GetWindowPos(), Is.EqualTo(new ImVec2(120.0f, 80.0f)));
        Assert.That(ImGui.GetWindowSize(), Is.EqualTo(new ImVec2(320.0f, 240.0f)));
        Assert.That(ImGui.GetWindowWidth(), Is.EqualTo(320.0f));
        Assert.That(ImGui.GetWindowHeight(), Is.EqualTo(240.0f));

        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetNextWindowSizeConstraints"/> clamps the resolved size through
    /// a custom <see cref="ImGuiSizeCallback"/>.
    /// </summary>
    [Test]
    public void Window_SetNextWindowSizeConstraints_ClampsViaCallback()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(10.0f, 10.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(400.0f, 400.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(
            new ImVec2(0.0f, 0.0f),
            new ImVec2(float.MaxValue, float.MaxValue),
            data => data.DesiredSize = new ImVec2(123.0f, 77.0f));
        ImGui.Begin("Constrained");

        Assert.That(ImGui.GetWindowSize(), Is.EqualTo(new ImVec2(123.0f, 77.0f)));

        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetWindowCollapsed(string, bool, ImGuiCond)"/> collapses an
    /// existing window addressed by name.
    /// </summary>
    [Test]
    public void Window_SetWindowCollapsedByName_CollapsesExistingWindow()
    {
        WindowFrame("ByName", () => { });

        NewTestFrame();
        ImGui.SetWindowCollapsed("ByName", true, ImGuiCond.Always);
        ImGui.Begin("ByName");
        Assert.That(ImGui.IsWindowCollapsed(), Is.True);
        ImGui.End();
        ImGui.Render();
    }

    #endregion

    #region Child windows

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginChild(string, ImVec2, ImGuiChildFlags, ImGuiWindowFlags)"/>
    /// nests a child inside its parent, that the child position lies inside the parent rectangle, and
    /// that the child is addressable by its composed name.
    /// </summary>
    [Test]
    public void Child_BeginChild_NestsInsideParent()
    {
        uint childId = 0;
        ImVec2 parentPos = default;
        ImVec2 childPos = default;

        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 300.0f), ImGuiCond.Always);
        ImGui.Begin("Parent");

        parentPos = ImGui.GetWindowPos();
        childId = ImGui.GetID("Child");
        bool childOpen = ImGui.BeginChild("Child", new ImVec2(120.0f, 90.0f), ImGuiChildFlags.Borders);
        Assert.That(childOpen, Is.True);

        childPos = ImGui.GetWindowPos();
        Assert.That(childPos.X, Is.GreaterThanOrEqualTo(parentPos.X));
        Assert.That(childPos.Y, Is.GreaterThanOrEqualTo(parentPos.Y));

        ImGui.EndChild();
        ImGui.End();
        ImGui.Render();

        ImGuiWindow? child = ImGui.FindWindowByName($"Parent/Child_{childId:X8}");
        Assert.That(child, Is.Not.Null);
        Assert.That((child!.Flags & ImGuiWindowFlags.ChildWindow) != 0, Is.True);
    }

    /// <summary>
    /// Verifies the canonical clipped-child behavior: a child whose rectangle is fully outside the
    /// parent clip rectangle reports as hidden and <see cref="ImGui.BeginChild"/> returns false once
    /// its auto-fit frames have elapsed.
    /// </summary>
    [Test]
    public void Child_BeginChild_ReturnsFalseWhenClipped()
    {
        // First frame establishes the parent and the out-of-clip child geometry.
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
        ImGui.Begin("ClipParent");
        ImGui.SetCursorPos(new ImVec2(1000.0f, 1000.0f));
        ImGui.BeginChild("Clipped", new ImVec2(50.0f, 50.0f));
        ImGui.EndChild();
        ImGui.End();
        ImGui.Render();

        bool secondFrameOpen = true;
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(100.0f, 100.0f), ImGuiCond.Always);
        ImGui.Begin("ClipParent");
        ImGui.SetCursorPos(new ImVec2(1000.0f, 1000.0f));
        secondFrameOpen = ImGui.BeginChild("Clipped", new ImVec2(50.0f, 50.0f));
        ImGui.EndChild();
        ImGui.End();
        ImGui.Render();

        Assert.That(secondFrameOpen, Is.False);
    }

    #endregion

    #region Display layering and draw lists

    /// <summary>
    /// Verifies that <see cref="ImGui.BringWindowToDisplayFront(ImGuiWindow)"/> moves a background
    /// window ahead of a window that was created later.
    /// </summary>
    [Test]
    public void Layers_BringWindowToDisplayFront_MovesToFront()
    {
        WindowFrame("LayerA", () => { });
        WindowFrame("LayerB", () => { });

        ImGuiWindow a = ImGui.FindWindowByName("LayerA")!;
        ImGuiWindow b = ImGui.FindWindowByName("LayerB")!;
        int indexA = ImGui.FindWindowDisplayIndex(a);
        int indexB = ImGui.FindWindowDisplayIndex(b);
        Assert.That(indexA, Is.LessThan(indexB));

        ImGui.BringWindowToDisplayFront(a);

        Assert.That(ImGui.FindWindowDisplayIndex(a), Is.GreaterThan(ImGui.FindWindowDisplayIndex(b)));
        Assert.That(ImGui.FindWindowDisplayIndex(a), Is.EqualTo(_ctx.Windows.Count - 1));
    }

    /// <summary>
    /// Verifies that the background and foreground draw lists are distinct from each other and from
    /// the current window's draw list.
    /// </summary>
    [Test]
    public void Layers_BackgroundAndForegroundDrawLists_AreDistinct()
    {
        NewTestFrame();
        ImGui.Begin("Draw");

        ImDrawList windowDrawList = ImGui.GetWindowDrawList();
        ImDrawList background = ImGui.GetBackgroundDrawList();
        ImDrawList foreground = ImGui.GetForegroundDrawList();

        Assert.That(background, Is.Not.SameAs(windowDrawList));
        Assert.That(foreground, Is.Not.SameAs(windowDrawList));
        Assert.That(background, Is.Not.SameAs(foreground));

        ImGui.End();
        ImGui.Render();
    }

    #endregion

    #region Window title bar buttons

    /// <summary>Window origin used by the title-bar button tests.</summary>
    private static readonly ImVec2 TitleBarOrigin = new(100.0f, 100.0f);

    /// <summary>Window size used by the title-bar button tests.</summary>
    private static readonly ImVec2 TitleBarSize = new(400.0f, 300.0f);

    /// <summary>
    /// Sends one frame that begins and ends a fixed-geometry window, invoking <paramref name="body"/>
    /// while it is current.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="body">Body executed between Begin and End.</param>
    private static void TitleBarFrame(string name, Action body)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(TitleBarOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(TitleBarSize, ImGuiCond.Always);
        ImGui.Begin(name);
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Hovers, presses and releases the left mouse button over <paramref name="point"/> across three
    /// frames so that a default <c>ButtonBehavior</c> item fires on release.
    /// </summary>
    /// <param name="point">Screen-space point to click.</param>
    /// <param name="frame">Frame submission run on every frame.</param>
    private void ClickAt(ImVec2 point, Action frame)
    {
        _ctx.IO.AddMousePosEvent(point.X, point.Y);
        frame();
        _ctx.IO.AddMouseButtonEvent(0, true);
        frame();
        _ctx.IO.AddMouseButtonEvent(0, false);
        frame();
    }

    /// <summary>
    /// Verifies that the window title bar submits the canonical collapse button
    /// (<c>#COLLAPSE</c>) and that pressing it collapses the window through
    /// <c>WantCollapseToggle</c>.
    /// (, )
    /// </summary>
    [Test]
    public void WindowTitleBar_CollapseButton_CollapsesWindow()
    {
        void Frame() => TitleBarFrame("CollapseBar", () => { });

        Frame(); // Establishes the window; the font size is only valid once a frame has run.
        ImGuiStyle style = ImGui.GetStyle();
        float fontSize = ImGui.GetFontSize();
        // The title-bar rect is inset by the window border before the buttons are placed (canonical
        ImVec2 collapseCenter = new(
            TitleBarOrigin.X + style.WindowBorderSize + style.FramePadding.X + fontSize * 0.5f,
            TitleBarOrigin.Y + style.FramePadding.Y + fontSize * 0.5f);

        ClickAt(collapseCenter, Frame);
        Frame(); // The deferred WantCollapseToggle is consumed at the start of this Begin.

        ImGuiWindow? window = ImGui.FindWindowByName("CollapseBar");
        Assert.That(window, Is.Not.Null);
        Assert.That(window!.Collapsed, Is.True);
    }

    /// <summary>
    /// Verifies that the window title bar submits the canonical close button (<c>#CLOSE</c>) and
    /// that pressing it clears the caller's <c>p_open</c> reference.
    /// </summary>
    [Test]
    public void WindowTitleBar_CloseButton_ClearsPOpen()
    {
        bool open = true;
        void Frame()
        {
            NewTestFrame();
            ImGui.SetNextWindowPos(TitleBarOrigin, ImGuiCond.Always);
            ImGui.SetNextWindowSize(TitleBarSize, ImGuiCond.Always);
            ImGui.Begin("CloseBar", ref open);
            ImGui.End();
            ImGui.Render();
        }

        Frame(); // Establishes the window; the font size is only valid once a frame has run.
        ImGuiStyle style = ImGui.GetStyle();
        float fontSize = ImGui.GetFontSize();
        ImVec2 closeCenter = new(
            TitleBarOrigin.X + TitleBarSize.X - style.WindowBorderSize - style.FramePadding.X - fontSize * 0.5f,
            TitleBarOrigin.Y + style.FramePadding.Y + fontSize * 0.5f);

        ClickAt(closeCenter, Frame);

        Assert.That(open, Is.False);
    }

    /// <summary>
    /// Verifies that collapsing a window still draws the canonical collapsed title-bar strip rather
    /// than leaving only the title-bar buttons.
    /// </summary>
    [Test]
    public void WindowTitleBar_Collapsed_DrawsTitleBarBackground()
    {
        bool found = false;
        NewTestFrame();
        ImGui.SetNextWindowPos(TitleBarOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(TitleBarSize, ImGuiCond.Always);
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.Always);
        ImGui.Begin("CollapsedBg");

        uint collapsedCol = ImGui.GetColorU32(ImGuiCol.TitleBgCollapsed);
        uint activeCol = ImGui.GetColorU32(ImGuiCol.TitleBgActive);
        foreach (ImDrawVert vertex in ImGui.GetWindowDrawList().VtxBuffer)
        {
            if (vertex.Col == collapsedCol || vertex.Col == activeCol)
            {
                found = true;
                break;
            }
        }

        Assert.That(ImGui.IsWindowCollapsed(), Is.True);
        Assert.That(found, Is.True, "The collapsed title-bar background was not drawn.");
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that the window title text is rendered into the title bar.
    /// </summary>
    [Test]
    public void WindowTitleBar_RendersTitleText()
    {
        ImGuiStyle style = ImGui.GetStyle();
        ImGuiDir backupMenuButton = style.WindowMenuButtonPosition;
        style.WindowMenuButtonPosition = ImGuiDir.None; // Remove the buttons so only the title text can draw text-colored vertices.

        bool foundText = false;
        NewTestFrame();
        ImGui.SetNextWindowPos(TitleBarOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(TitleBarSize, ImGuiCond.Always);
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.Always);
        ImGui.Begin("TitleTextWindow");

        uint textCol = ImGui.GetColorU32(ImGuiCol.Text);
        foreach (ImDrawVert vertex in ImGui.GetWindowDrawList().VtxBuffer)
        {
            if (vertex.Col == textCol)
            {
                foundText = true;
                break;
            }
        }

        Assert.That(foundText, Is.True, "The window title text was not rendered.");
        ImGui.End();
        ImGui.Render();
        style.WindowMenuButtonPosition = backupMenuButton;
    }

    #endregion
}
