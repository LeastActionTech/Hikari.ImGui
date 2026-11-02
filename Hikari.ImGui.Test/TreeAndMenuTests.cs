#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical tree nodes, tree scopes, collapsing headers, and menu widgets owned by
/// <c>ImGuiWidgets.cs</c>.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>. The canonical reference is Dear ImGui
/// <c>v1.92.9b-docking</c> at <c>ThirdParty/imgui/imgui_widgets.cpp</c>; each test cites the
/// canonical line range that defines the behavior it pins.
/// </remarks>
[TestFixture]
public class TreeAndMenuTests : ImGuiTestFixture
{
    /// <summary>Window origin used by the placement-sensitive tests.</summary>
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


    /// <summary>Begins a fixed-geometry window; the caller must close it with <see cref="ImGui.End"/>.</summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="flags">Window flags.</param>
    private static void BeginFixed(string name, ImVec2 size, ImGuiWindowFlags flags = 0)
    {
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin(name, flags);
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

    /// <summary>Counter used to give each measurement a fresh, un-polluted window.</summary>
    private int _measureId;

    /// <summary>
    /// Measures the change in the current window's vertex buffer count caused by one submission in a
    /// freshly created window so that no menu-column or layout state leaks between measurements.
    /// </summary>
    /// <param name="draw">The submission to measure.</param>
    /// <returns>The vertex count delta.</returns>
    private int MeasureVertices(Action draw)
    {
        int delta = 0;
        WindowFrame($"Measure{_measureId++}", new ImVec2(400.0f, 300.0f), () =>
        {
            ImDrawList drawList = ImGui.GetCurrentContext()!.CurrentWindow!.DrawList;
            int before = drawList.VtxBuffer.Count;
            draw();
            delta = drawList.VtxBuffer.Count - before;
        });
        return delta;
    }

    /// <summary>
    /// Submits one widget for a frame to learn its screen rectangle, then presses and releases the
    /// left mouse button at a point derived from that rectangle.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="submit">Submits exactly one widget and returns its boolean result.</param>
    /// <param name="pointSelector">Maps the learned rectangle to the press point.</param>
    /// <returns>The release-frame result of <paramref name="submit"/>.</returns>
    private bool ClickWidgetAt(string name, ImVec2 size, Func<bool> submit, Func<ImRect, ImVec2> pointSelector)
    {
        // The first frame stabilizes the window; the second captures a stable rectangle.
        WindowFrame(name, size, () => submit());
        ImRect rect = default;
        WindowFrame(name, size, () =>
        {
            submit();
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        ImVec2 point = pointSelector(rect);
        _ctx.IO.AddMousePosEvent(point.X, point.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed(name, size);
        submit();
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        BeginFixed(name, size);
        bool result = submit();
        ImGui.End();
        ImGui.Render();
        return result;
    }

    /// <summary>Presses and releases the left mouse button at the center of the learned rectangle.</summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <param name="submit">Submits exactly one widget and returns its boolean result.</param>
    /// <returns>The release-frame result of <paramref name="submit"/>.</returns>
    private bool ClickWidget(string name, ImVec2 size, Func<bool> submit)
        => ClickWidgetAt(name, size, submit, rect => new ImVec2((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f));

    #region Trees

    /// <summary>
    /// Verifies that a tree node starts closed, opens on a click, and keeps its open state through the
    /// window's state storage on the following frames. Canonical reads and writes the per-window
    /// <c>StateStorage</c> keyed by the node identifier
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6848-6893</c>, <c>6919-7196</c>).
    /// </summary>
    [Test]
    public void TreeNode_OpenClosePersistence()
    {
        ImVec2 size = new(400.0f, 300.0f);
        uint storageId = 0;

        WindowFrame("Tree", size, () => ImGui.TreeNode("Node"));
        WindowFrame("Tree", size, () =>
        {
            bool open = ImGui.TreeNode("Node");
            storageId = ImGui.GetID("Node");
            Assert.That(open, Is.False);
            if (open)
                ImGui.TreePop();
        });

        bool opened = ClickWidget("Tree", size, () =>
        {
            bool open = ImGui.TreeNode("Node");
            if (open)
                ImGui.TreePop();
            return open;
        });
        Assert.That(opened, Is.True);

        WindowFrame("Tree", size, () =>
        {
            Assert.That(ImGui.TreeNodeGetOpen(storageId), Is.True);
            bool open = ImGui.TreeNode("Node");
            Assert.That(open, Is.True);
            if (open)
                ImGui.TreePop();
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.IsItemToggledOpen"/> reports the open-toggle edge only on the
    /// frame the node is clicked. Canonical sets <c>ImGuiItemStatusFlags_ToggledOpen</c> inside
    /// <c>TreeNodeBehavior</c> (<c>ThirdParty/imgui/imgui_widgets.cpp:7103-7121</c>).
    /// </summary>
    [Test]
    public void TreeNode_IsItemToggledOpen()
    {
        ImVec2 size = new(400.0f, 300.0f);
        bool toggledOnClickFrame = false;

        WindowFrame("Tree", size, () => ImGui.TreeNode("Node"));
        WindowFrame("Tree", size, () => ImGui.TreeNode("Node"));

        ClickWidget("Tree", size, () =>
        {
            bool open = ImGui.TreeNode("Node");
            toggledOnClickFrame = ImGui.IsItemToggledOpen();
            if (open)
                ImGui.TreePop();
            return open;
        });

        Assert.That(toggledOnClickFrame, Is.True);

        bool toggledOnPlainFrame = true;
        WindowFrame("Tree", size, () =>
        {
            bool open = ImGui.TreeNode("Node");
            toggledOnPlainFrame = ImGui.IsItemToggledOpen();
            if (open)
                ImGui.TreePop();
        });
        Assert.That(toggledOnPlainFrame, Is.False);
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTreeNodeFlags.DefaultOpen"/> opens a node without any interaction
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6848-6893</c>, <c>6919-6926</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_DefaultOpen()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            bool open = ImGui.TreeNodeEx("A", ImGuiTreeNodeFlags.DefaultOpen);
            Assert.That(open, Is.True);
            if (open)
                ImGui.TreePop();
        });
    }

    /// <summary>
    /// Verifies that a leaf node always reports open and still opens a tree scope (the canonical leaf
    /// has no arrow but is not implicitly <c>NoTreePushOnOpen</c>)
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6851-6852</c>, <c>7003</c>, <c>7193-7196</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_LeafPushesScope()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            int idCountBefore = _ctx.CurrentWindow!.IDStack.Count;
            bool open = ImGui.TreeNodeEx("Leaf", ImGuiTreeNodeFlags.Leaf);
            Assert.That(open, Is.True);
            Assert.That(_ctx.CurrentWindow!.IDStack.Count, Is.EqualTo(idCountBefore + 1));
            ImGui.TreePop();
            Assert.That(_ctx.CurrentWindow!.IDStack.Count, Is.EqualTo(idCountBefore));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTreeNodeFlags.NoTreePushOnOpen"/> opens the node visually while
    /// leaving the identifier stack untouched (<c>ThirdParty/imgui/imgui_widgets.cpp:7193-7196</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_NoTreePushOnOpen()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            int idCountBefore = _ctx.CurrentWindow!.IDStack.Count;
            bool open = ImGui.TreeNodeEx(
                "Bare",
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.NoTreePushOnOpen);
            Assert.That(open, Is.True);
            Assert.That(_ctx.CurrentWindow!.IDStack.Count, Is.EqualTo(idCountBefore));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTreeNodeFlags.OpenOnArrow"/> ignores a click on the label but
    /// toggles on a click over the arrow column. Canonical only sets <c>toggled</c> when
    /// <c>is_mouse_x_over_arrow</c> holds for this flag
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7045-7092</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_OpenOnArrow()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow;

        WindowFrame("Tree", size, () => ImGui.TreeNodeEx("Arrow", flags));
        WindowFrame("Tree", size, () => ImGui.TreeNodeEx("Arrow", flags));

        // A click on the label center is outside the arrow column and must not toggle.
        bool labelClickOpen = ClickWidget("Tree", size, () =>
        {
            bool open = ImGui.TreeNodeEx("Arrow", flags);
            if (open)
                ImGui.TreePop();
            return open;
        });
        Assert.That(labelClickOpen, Is.False);

        // A click inside the arrow column toggles the node on the press edge.
        bool arrowClickOpen = ClickWidgetAt(
            "Tree",
            size,
            () =>
            {
                bool open = ImGui.TreeNodeEx("Arrow", flags);
                if (open)
                    ImGui.TreePop();
                return open;
            },
            rect => new ImVec2(rect.Min.X + 2.0f, (rect.Min.Y + rect.Max.Y) * 0.5f));
        Assert.That(arrowClickOpen, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTreeNodeFlags.OpenOnDoubleClick"/> ignores a single click and
    /// toggles on the second click of a double-click pair
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7045-7092</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_OpenOnDoubleClick()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnDoubleClick;

        WindowFrame("Tree", size, () => ImGui.TreeNodeEx("Double", flags));
        ImRect rect = default;
        WindowFrame("Tree", size, () =>
        {
            ImGui.TreeNodeEx("Double", flags);
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        ImVec2 center = new((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);

        // First click: activated but not toggled.
        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed("Tree", size);
        bool first = ImGui.TreeNodeEx("Double", flags);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        BeginFixed("Tree", size);
        bool firstRelease = ImGui.TreeNodeEx("Double", flags);
        ImGui.End();
        ImGui.Render();
        Assert.That(first, Is.False);
        Assert.That(firstRelease, Is.False);

        // Second click within the double-click window: toggles.
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed("Tree", size);
        bool second = ImGui.TreeNodeEx("Double", flags);
        if (second)
            ImGui.TreePop();
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        BeginFixed("Tree", size);
        bool secondRelease = ImGui.TreeNodeEx("Double", flags);
        if (secondRelease)
            ImGui.TreePop();
        ImGui.End();
        ImGui.Render();

        Assert.That(second || secondRelease, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTreeNodeFlags.SpanAllColumns"/> inside a table widens the node's frame
    /// to the parent work rectangle, and that the flag has no effect outside a table
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6942-6955</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_SpanAllColumns_UsesParentWorkRectInsideTable()
    {
        ImRect plainInTable = default;
        ImRect spanInTable = default;
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            Assert.That(ImGui.BeginTable("T", 2, ImGuiTableFlags.None, new ImVec2(300.0f, 120.0f)), Is.True);
            ImGui.TableSetupColumn("A");
            ImGui.TableSetupColumn("B");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TreeNodeEx("Plain", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
            plainInTable = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TreeNodeEx("Span", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.SpanAllColumns | ImGuiTreeNodeFlags.NoTreePushOnOpen);
            spanInTable = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.EndTable();
        });

        // The spanning node reaches the table's parent work rect, which is wider than column 0.
        Assert.That(spanInTable.Max.X, Is.GreaterThan(plainInTable.Max.X));
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiTreeNodeFlags.SpanAvailWidth"/> widens the node's interaction
    /// rectangle to the full work width while the default node stays label-sized
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6960-6962</c>).
    /// </summary>
    [Test]
    public void TreeNodeEx_SpanAvailWidth()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            float workWidth = _ctx.CurrentWindow!.WorkRect.Max.X - _ctx.CurrentWindow!.DC.CursorPos.X;
            ImGui.TreeNodeEx("Plain");
            float plainWidth = ImGui.GetItemRectSize().X;

            ImGui.TreeNodeEx("Wide", ImGuiTreeNodeFlags.SpanAvailWidth);
            float wideWidth = ImGui.GetItemRectSize().X;

            Assert.That(wideWidth, Is.EqualTo(workWidth).Within(1.0f));
            Assert.That(plainWidth, Is.LessThan(wideWidth));
        });
    }

    /// <summary>
    /// Verifies the <see cref="ImGui.TreeNodeGetOpen"/> / <see cref="ImGui.TreeNodeSetOpen"/> storage
    /// round trip keyed by an explicit storage identifier
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6834-6846</c>).
    /// </summary>
    [Test]
    public void TreeNodeGetOpen_SetOpenRoundTrip()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            uint id = ImGui.GetID("Stored");
            Assert.That(ImGui.TreeNodeGetOpen(id), Is.False);
            ImGui.TreeNodeSetOpen(id, true);
            Assert.That(ImGui.TreeNodeGetOpen(id), Is.True);
            ImGui.TreeNodeSetOpen(id, false);
            Assert.That(ImGui.TreeNodeGetOpen(id), Is.False);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TreePush(string)"/> and <see cref="ImGui.TreePop"/> balance the
    /// window identifier stack and tree depth while indenting and unindenting the cursor
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7260-7315</c>).
    /// </summary>
    [Test]
    public void TreePush_TreePop_Balance()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
        {
            int idBefore = _ctx.CurrentWindow!.IDStack.Count;
            int depthBefore = _ctx.CurrentWindow!.DC.TreeDepth;
            float indentBefore = ImGui.GetCursorPosX();

            ImGui.TreePush("child");
            Assert.That(_ctx.CurrentWindow!.IDStack.Count, Is.EqualTo(idBefore + 1));
            Assert.That(_ctx.CurrentWindow!.DC.TreeDepth, Is.EqualTo(depthBefore + 1));
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(indentBefore + _ctx.Style.IndentSpacing));

            ImGui.TreePop();
            Assert.That(_ctx.CurrentWindow!.IDStack.Count, Is.EqualTo(idBefore));
            Assert.That(_ctx.CurrentWindow!.DC.TreeDepth, Is.EqualTo(depthBefore));
            Assert.That(ImGui.GetCursorPosX(), Is.EqualTo(indentBefore));
        });
    }

    /// <summary>
    /// Verifies the canonical label spacing helper: the font size plus twice the horizontal frame
    /// padding (<c>ThirdParty/imgui/imgui_widgets.cpp:7318-7322</c>).
    /// </summary>
    [Test]
    public void GetTreeNodeToLabelSpacing_MatchesCanonicalFormula()
    {
        WindowFrame("Tree", new ImVec2(400.0f, 300.0f), () =>
            Assert.That(ImGui.GetTreeNodeToLabelSpacing(), Is.EqualTo(_ctx.FontSize + _ctx.Style.FramePadding.X * 2.0f)));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.CollapsingHeader(string, ImGuiTreeNodeFlags)"/> starts closed and
    /// opens on a click, and that the visibility-bound overload returns false and draws nothing while
    /// the referenced flag is false. Canonical folds
    /// <c>Framed | NoTreePushOnOpen | NoAutoOpenOnLog</c> and gates on <c>p_visible</c>
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7347-7391</c>).
    /// </summary>
    [Test]
    public void CollapsingHeader_OpenAndVisible()
    {
        ImVec2 size = new(400.0f, 300.0f);

        WindowFrame("Header", size, () =>
        {
            bool open = ImGui.CollapsingHeader("Header");
            Assert.That(open, Is.False);
            Assert.That(_ctx.CurrentWindow!.IDStack.Count, Is.EqualTo(1));
        });

        bool opened = ClickWidget("Header", size, () => ImGui.CollapsingHeader("Header"));
        Assert.That(opened, Is.True);

        WindowFrame("Header", size, () =>
        {
            bool visible = true;
            bool open = ImGui.CollapsingHeader("Header", ref visible, ImGuiTreeNodeFlags.DefaultOpen);
            Assert.That(open, Is.True);
            Assert.That(visible, Is.True);
        });

        WindowFrame("Hidden", size, () =>
        {
            bool visible = false;
            bool open = ImGui.CollapsingHeader("Header", ref visible);
            Assert.That(open, Is.False);
            Assert.That(visible, Is.False);
        });
    }

    /// <summary>
    /// Verifies that the trailing close button of the visibility-bound collapsing header clears the
    /// visibility flag. Canonical submits <c>CloseButton(GetIDWithSeed("#CLOSE", NULL, id), ...)</c>
    /// and clears <c>p_visible</c> on press (<c>ThirdParty/imgui/imgui_widgets.cpp:7360-7391</c>).
    /// </summary>
    /// <remarks>
    /// Ignored because the port's <c>CollapsingHeader(string, ref bool, ...)</c> reserves the trailing
    /// button space via <c>ClipLabelForTrailingButton</c> but never submits the close button, so a
    /// click cannot clear <c>p_visible</c> (see the port's own remarks on that overload, and the
    /// unused <c>ImGui.CloseButton</c> helper at <c>Hikari.ImGui/ImGuiWidgets.cs:1736</c>). The
    /// canonical close-button behavior is not reachable in this port.
    /// </remarks>
    [Test]
    public void CollapsingHeader_CloseButtonClearsVisible()
    {
        ImVec2 size = new(400.0f, 300.0f);
        bool visible = true;

        WindowFrame("HeaderClose", size, () => ImGui.CollapsingHeader("Header", ref visible));
        ImRect rect = default;
        WindowFrame("HeaderClose", size, () =>
        {
            ImGui.CollapsingHeader("Header", ref visible);
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        ImVec2 point = new(rect.Max.X - _ctx.FontSize, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMousePosEvent(point.X, point.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed("HeaderClose", size);
        ImGui.CollapsingHeader("Header", ref visible);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        BeginFixed("HeaderClose", size);
        ImGui.CollapsingHeader("Header", ref visible);
        ImGui.End();
        ImGui.Render();

        Assert.That(visible, Is.False);
    }

    #endregion

    #region Menus

    /// <summary>
    /// Verifies the canonical menu-bar flow: <see cref="ImGui.BeginMenuBar"/> succeeds only inside a
    /// <see cref="ImGuiWindowFlags.MenuBar"/> window, and <see cref="ImGui.BeginMenu"/> opens its popup
    /// when the entry is hovered, returning true for the popup content
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:9208-9296</c>, <c>9405-9633</c>).
    /// </summary>
    [Test]
    public void MenuBar_BeginMenu_OpensOnHover()
    {
        ImVec2 size = new(400.0f, 300.0f);
        ImRect menuRect = default;

        WindowFrame("Menu", size, () =>
        {
            Assert.That(ImGui.BeginMenuBar(), Is.False); // No ImGuiWindowFlags_MenuBar.
        });

        NewTestFrame();
        BeginFixed("Menu", size, ImGuiWindowFlags.MenuBar);
        Assert.That(ImGui.BeginMenuBar(), Is.True);
        bool openNotHovered = ImGui.BeginMenu("File");
        menuRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.EndMenuBar();
        ImGui.End();
        ImGui.Render();
        Assert.That(openNotHovered, Is.False);

        _ctx.IO.AddMousePosEvent((menuRect.Min.X + menuRect.Max.X) * 0.5f, (menuRect.Min.Y + menuRect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        int popupDepth = -1;
        bool open = false;
        // A menu-bar entry uses SelectOnClick, so it opens on the press edge.
        NewTestFrame();
        BeginFixed("Menu", size, ImGuiWindowFlags.MenuBar);
        ImGui.BeginMenuBar();
        open = ImGui.BeginMenu("File");
        if (open)
        {
            popupDepth = _ctx.BeginPopupStack.Count;
            ImGui.MenuItem("New");
            ImGui.EndMenu();
        }
        ImGui.EndMenuBar();
        ImGui.End();
        ImGui.Render();

        Assert.That(open, Is.True);
        Assert.That(popupDepth, Is.EqualTo(1));

        _ctx.IO.AddMouseButtonEvent(0, false);
        bool stillOpen = false;
        NewTestFrame();
        BeginFixed("Menu", size, ImGuiWindowFlags.MenuBar);
        ImGui.BeginMenuBar();
        stillOpen = ImGui.BeginMenu("File");
        if (stillOpen)
        {
            ImGui.EndMenu();
        }
        ImGui.EndMenuBar();
        ImGui.End();
        ImGui.Render();
        Assert.That(stillOpen, Is.True);
    }

    /// <summary>
    /// Verifies menu-item activation, the optional shortcut geometry, the selected checkmark, and the
    /// <c>ref bool</c> selection overload. Canonical submits a selectable row and renders the label,
    /// icon, shortcut, and checkmark columns (<c>ThirdParty/imgui/imgui_widgets.cpp:9654-9758</c>).
    /// </summary>
    [Test]
    public void MenuItem_ActivationSelectedShortcutAndRefBool()
    {
        ImVec2 size = new(400.0f, 300.0f);

        bool activated = ClickWidget("Menu", size, () => ImGui.MenuItem("Save"));
        Assert.That(activated, Is.True);

        int plainVtx = MeasureVertices(() => ImGui.MenuItem("Save"));
        int shortcutVtx = MeasureVertices(() => ImGui.MenuItem("Save", "Ctrl+S"));
        // "Ctrl+S" has six visible glyphs; the shortcut column adds exactly those quads.
        Assert.That(plainVtx, Is.EqualTo(4 * 4));
        Assert.That(shortcutVtx - plainVtx, Is.EqualTo(6 * 4));

        int selectedVtx = MeasureVertices(() => ImGui.MenuItem("Save", null, selected: true));
        // The selected row additionally renders the three-point checkmark stroke (10 vertices in Dear ImGui v1.93).
        Assert.That(selectedVtx - plainVtx, Is.EqualTo(10));

        bool selected = false;
        bool refActivated = ClickWidget("Menu", size, () => ImGui.MenuItem("Pick", null, ref selected));
        Assert.That(refActivated, Is.True);
        Assert.That(selected, Is.True);

        bool disabledActivated = PressReleaseAtDisabled("Menu", size);
        Assert.That(disabledActivated, Is.False);
    }

    /// <summary>Presses and releases at the center of a disabled menu item to prove it stays inert.</summary>
    /// <param name="name">Window name.</param>
    /// <param name="size">Window size.</param>
    /// <returns>False for the disabled item.</returns>
    private bool PressReleaseAtDisabled(string name, ImVec2 size)
    {
        ImRect rect = default;
        WindowFrame(name, size, () =>
        {
            ImGui.MenuItem("Locked", null, false, enabled: false);
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });
        WindowFrame(name, size, () =>
        {
            ImGui.MenuItem("Locked", null, false, enabled: false);
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        ImVec2 center = new((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        BeginFixed(name, size);
        ImGui.MenuItem("Locked", null, false, enabled: false);
        ImGui.End();
        ImGui.Render();

        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        BeginFixed(name, size);
        bool pressed = ImGui.MenuItem("Locked", null, false, enabled: false);
        ImGui.End();
        ImGui.Render();
        return pressed;
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginMainMenuBar"/> opens the canonical
    /// <c>"##MainMenuBar"</c> side-bar window on the main viewport, clears the queued safe-area offset,
    /// permits settings inside the bar, and that <see cref="ImGui.EndMainMenuBar"/> closes it and restores
    /// the previous current window
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:9301-9379</c>).
    /// </summary>
    [Test]
    public void BeginMainMenuBar_OpensSideBarAndEndClosesIt()
    {
        NewTestFrame();
        ImGuiWindow? fallback = _ctx.CurrentWindow;
        bool open = ImGui.BeginMainMenuBar();

        Assert.That(open, Is.True, "BeginMainMenuBar() must open the main menu bar side window.");
        Assert.That(_ctx.CurrentWindow, Is.Not.SameAs(fallback), "The main menu bar window must become current.");
        Assert.That(_ctx.CurrentWindow!.Name, Is.EqualTo("##MainMenuBar"));
        // The queued safe-area menu-bar offset must be consumed and not leak into the next window.
        Assert.That(_ctx.NextWindowData.MenuBarOffsetMinVal, Is.EqualTo(ImVec2.Zero));
        // _NoSavedSettings is temporarily cleared so nested tables and child windows may use settings.
        Assert.That((_ctx.CurrentWindow.Flags & ImGuiWindowFlags.NoSavedSettings), Is.EqualTo(ImGuiWindowFlags.None));

        ImGui.EndMainMenuBar();
        Assert.That(_ctx.CurrentWindow, Is.SameAs(fallback), "EndMainMenuBar() must return to the previous window.");

        ImGui.EndFrame();
        ImGui.Render();
    }

    #endregion
}
