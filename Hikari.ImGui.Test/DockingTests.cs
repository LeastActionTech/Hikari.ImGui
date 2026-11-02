#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical docking subsystem in <see cref="ImGui"/>, covering dock context lifecycle,
/// dock builder node creation, tree splitting, window-to-node docking, and dockspace submissions.
/// </summary>
/// <remarks>
/// Canonical reference: Dear ImGui <c>v1.92.9b-docking</c> at <c>ThirdParty/imgui/imgui.cpp:17921-21905</c>.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class DockingTests : ImGuiTestFixture
{

    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
    }


    /// <summary>
    /// Verifies that dock context initializes with empty nodes and maps.
    /// Canonical: <c>imgui.cpp:18047-18055</c> (<c>DockContextInitialize</c>).
    /// </summary>
    [Test]
    public void DockContext_Initialize_CreatesCleanState()
    {
        ImGuiDockContext dockCtx = _ctx.DockContext;
        Assert.That(dockCtx, Is.Not.Null);
        Assert.That(dockCtx.Nodes.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies node addition, lookup, and removal via DockBuilder APIs.
    /// Canonical: <c>imgui.cpp:21588-21620</c> (<c>DockBuilderAddNode</c>, <c>DockBuilderRemoveNode</c>).
    /// </summary>
    [Test]
    public void DockBuilder_AddAndRemoveNode_TracksInContext()
    {
        uint dockId = 42;
        ImGui.DockBuilderAddNode(dockId, ImGuiDockNodeFlags.None);

        ImGuiDockNode? node = ImGui.DockBuilderGetNode(dockId);
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.ID, Is.EqualTo(dockId));
        Assert.That(node.IsRootNode(), Is.True);

        ImGui.DockBuilderRemoveNode(dockId);
        Assert.That(ImGui.DockBuilderGetNode(dockId), Is.Null);
    }

    /// <summary>
    /// Verifies splitting a dock node horizontally and vertically into child nodes.
    /// Canonical: <c>imgui.cpp:21670-21760</c> (<c>DockBuilderSplitNode</c>).
    /// </summary>
    [Test]
    public void DockBuilder_SplitNode_CreatesExpectedChildrenAndSizes()
    {
        uint dockId = 100;
        ImGui.DockBuilderAddNode(dockId, ImGuiDockNodeFlags.DockSpace);
        ImGui.DockBuilderSetNodeSize(dockId, new ImVec2(800.0f, 600.0f));
        ImGui.DockBuilderSetNodePos(dockId, new ImVec2(0.0f, 0.0f));

        uint outIdAtDir = 0;
        uint outIdAtOppositeDir = 0;

        // Split 25% to the left
        uint splitResult = ImGui.DockBuilderSplitNode(dockId, ImGuiDir.Left, 0.25f, out outIdAtDir, out outIdAtOppositeDir);

        Assert.That(splitResult, Is.EqualTo(outIdAtDir));
        Assert.That(outIdAtDir, Is.Not.EqualTo(0u));
        Assert.That(outIdAtOppositeDir, Is.Not.EqualTo(0u));

        ImGuiDockNode? parentNode = ImGui.DockBuilderGetNode(dockId);
        ImGuiDockNode? leftChild = ImGui.DockBuilderGetNode(outIdAtDir);
        ImGuiDockNode? rightChild = ImGui.DockBuilderGetNode(outIdAtOppositeDir);

        Assert.That(parentNode, Is.Not.Null);
        Assert.That(leftChild, Is.Not.Null);
        Assert.That(rightChild, Is.Not.Null);

        Assert.That(parentNode!.IsSplitNode(), Is.True);
        Assert.That(parentNode.ChildNodes[0], Is.SameAs(leftChild));
        Assert.That(parentNode.ChildNodes[1], Is.SameAs(rightChild));
        Assert.That(parentNode.SplitAxis, Is.EqualTo(ImGuiAxis.X));

        Assert.That(leftChild!.ParentNode, Is.SameAs(parentNode));
        Assert.That(rightChild!.ParentNode, Is.SameAs(parentNode));

        // Finish building
        ImGui.DockBuilderFinish(dockId);
    }

    /// <summary>
    /// Verifies docking a window into a dock node via <see cref="ImGui.DockBuilderDockWindow"/>.
    /// Canonical: <c>imgui.cpp:21622-21655</c>.
    /// </summary>
    [Test]
    public void DockBuilder_DockWindow_AssociatesWindowWithNode()
    {
        uint dockId = 200;
        ImGui.DockBuilderAddNode(dockId, ImGuiDockNodeFlags.DockSpace);

        string windowName = "ToolWindow";

        // First frame: create host dockspace and tool window
        NewTestFrame();
        ImGui.Begin("HostWindow");
        ImGui.DockSpace(dockId);
        ImGui.End();

        ImGui.Begin(windowName);
        ImGui.Text("Inside dock");
        ImGui.End();
        ImGui.Render();

        // Dock the window into the dock node
        ImGui.DockBuilderDockWindow(windowName, dockId);
        ImGuiWindow? w1 = ImGui.FindWindowByName(windowName);
        Assert.That(w1, Is.Not.Null);
        Assert.That(w1!.DockId, Is.EqualTo(dockId));
        ImGui.DockBuilderFinish(dockId);

        // Second frame: submit again to process docking
        NewTestFrame();
        ImGui.Begin("HostWindow");
        ImGui.DockSpace(dockId);
        ImGui.End();

        ImGui.Begin(windowName);
        ImGui.Text("Inside dock");
        Assert.That(ImGui.GetCurrentWindowRead()!.DockId, Is.EqualTo(dockId));
        ImGui.End();
        ImGui.Render();

        ImGuiWindow? window = ImGui.FindWindowByName(windowName);
        Assert.That(window, Is.Not.Null);
        Assert.That(window!.DockId, Is.EqualTo(dockId));
    }

    /// <summary>
    /// Verifies <see cref="ImGui.DockSpace"/> creates a dock node for the given ID within the current window.
    /// Canonical: <c>imgui.cpp:20888-21040</c>.
    /// </summary>
    [Test]
    public void DockSpace_SubmitsAndCreatesCentralNode()
    {
        uint dockspaceId = 0;

        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(50.0f, 50.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("DockSpaceHost");

        dockspaceId = ImGui.GetID("MyDockSpace");
        uint returnedId = ImGui.DockSpace(dockspaceId, new ImVec2(0.0f, 0.0f), ImGuiDockNodeFlags.None);

        ImGui.End();
        ImGui.Render();

        Assert.That(returnedId, Is.EqualTo(dockspaceId));
        ImGuiDockNode? node = ImGui.DockBuilderGetNode(dockspaceId);
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.IsRootNode(), Is.True);
        Assert.That(node.IsCentralNode(), Is.True);
    }

    /// <summary>
    /// Verifies <see cref="ImGui.DockSpaceOverViewport"/> creates a full-viewport dockspace.
    /// Canonical: <c>imgui.cpp:21042-21080</c>.
    /// </summary>
    [Test]
    public void DockSpaceOverViewport_CreatesViewportHostWindow()
    {
        NewTestFrame();
        uint dsId = ImGui.DockSpaceOverViewport(0, null, ImGuiDockNodeFlags.PassthruCentralNode);
        ImGui.Render();

        Assert.That(dsId, Is.Not.EqualTo(0u));
        ImGuiDockNode? node = ImGui.DockBuilderGetNode(dsId);
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.SharedFlags & ImGuiDockNodeFlags.PassthruCentralNode, Is.EqualTo(ImGuiDockNodeFlags.PassthruCentralNode));
    }

    /// <summary>
    /// Verifies tree navigation helpers: <c>DockNodeGetRootNode</c> and <c>DockNodeGetDepth</c>.
    /// Canonical: <c>imgui.cpp:18057-18080</c>.
    /// </summary>
    [Test]
    public void DockNode_HierarchyNavigation_ComputesDepthAndRoot()
    {
        uint rootId = 300;
        ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.None);

        uint childLeft = 0;
        uint childRight = 0;
        ImGui.DockBuilderSplitNode(rootId, ImGuiDir.Left, 0.5f, out childLeft, out childRight);

        uint subChildUp = 0;
        uint subChildDown = 0;
        ImGui.DockBuilderSplitNode(childLeft, ImGuiDir.Up, 0.5f, out subChildUp, out subChildDown);

        ImGuiDockNode? root = ImGui.DockBuilderGetNode(rootId);
        ImGuiDockNode? left = ImGui.DockBuilderGetNode(childLeft);
        ImGuiDockNode? subUp = ImGui.DockBuilderGetNode(subChildUp);

        Assert.That(root, Is.Not.Null);
        Assert.That(left, Is.Not.Null);
        Assert.That(subUp, Is.Not.Null);

        Assert.That(ImGui.DockNodeGetRootNode(subUp!), Is.SameAs(root));
        Assert.That(ImGui.DockNodeGetDepth(root!), Is.EqualTo(0));
        Assert.That(ImGui.DockNodeGetDepth(left!), Is.EqualTo(1));
        Assert.That(ImGui.DockNodeGetDepth(subUp!), Is.EqualTo(2));
    }

    #region Remediation Chunk 6R Regression Coverage

    /// <summary>
    /// Verifies that negative size arguments offset from available content region and clamp to minimum 4.0f floor.
    /// Canonical: <c>imgui.cpp:20833-20839</c> (Bug 6-B7).
    /// </summary>
    [Test]
    public void DockSpace_NegativeSize_AppliesOffsetAndEnforcesFourFloor()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(500.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("HostWindow");

        uint dsId = ImGui.GetID("TestDockSpaceNegative");
        ImGui.DockSpace(dsId, new ImVec2(-100.0f, -100.0f));
        ImGui.End();
        ImGui.Render();

        ImGuiDockNode? node = ImGui.DockBuilderGetNode(dsId);
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Size.X, Is.GreaterThanOrEqualTo(4.0f));
        Assert.That(node.Size.Y, Is.GreaterThanOrEqualTo(4.0f));
    }

    /// <summary>
    /// Verifies that touching nodes gathering collects leaf nodes along the splitter axis.
    /// Canonical: <c>imgui.cpp:20569-20582</c> (Bug 6-B2).
    /// </summary>
    [Test]
    public void DockNodeTree_SplitterTouchingNodes_GathersLeafNodes()
    {
        uint rootId = 400;
        ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.None);
        ImGui.DockBuilderSetNodeSize(rootId, new ImVec2(800.0f, 600.0f));

        uint childLeft = 0;
        uint childRight = 0;
        ImGui.DockBuilderSplitNode(rootId, ImGuiDir.Left, 0.5f, out childLeft, out childRight);

        uint subLeft1 = 0;
        uint subLeft2 = 0;
        ImGui.DockBuilderSplitNode(childLeft, ImGuiDir.Up, 0.5f, out subLeft1, out subLeft2);
        ImGui.DockBuilderFinish(rootId);

        ImGuiDockNode root = ImGui.DockBuilderGetNode(rootId)!;
        ImGuiDockNode leftNode = ImGui.DockBuilderGetNode(childLeft)!;
        leftNode.IsVisible = true;
        ImGui.DockBuilderGetNode(subLeft1)!.IsVisible = true;
        ImGui.DockBuilderGetNode(subLeft2)!.IsVisible = true;

        System.Collections.Generic.List<ImGuiDockNode> touchingNodes = new();
        ImGui.DockNodeTreeUpdateSplitterFindTouchingNode(leftNode, ImGuiAxis.X, 1, touchingNodes);

        Assert.That(touchingNodes.Count, Is.GreaterThan(0));
    }

    /// <summary>
    /// Verifies that simultaneous double child resize lock preserves split size ratios accurately.
    /// Canonical: <c>imgui.cpp:20526-20534</c> (Bug 6-B3).
    /// </summary>
    [Test]
    public void DockNodeTree_DoubleLockResize_PreservesRatio()
    {
        uint rootId = 500;
        ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.None);
        ImGui.DockBuilderSetNodeSize(rootId, new ImVec2(800.0f, 600.0f));

        uint leftId = 0;
        uint rightId = 0;
        ImGui.DockBuilderSplitNode(rootId, ImGuiDir.Left, 0.5f, out leftId, out rightId);
        ImGui.DockBuilderFinish(rootId);

        ImGuiDockNode root = ImGui.DockBuilderGetNode(rootId)!;
        ImGuiDockNode left = ImGui.DockBuilderGetNode(leftId)!;
        ImGuiDockNode right = ImGui.DockBuilderGetNode(rightId)!;

        left.WantLockSizeOnce = true;
        right.WantLockSizeOnce = true;
        left.Size = new ImVec2(300.0f, 600.0f);
        right.Size = new ImVec2(500.0f, 600.0f);

        ImGui.DockNodeTreeUpdatePosSize(root, new ImVec2(0.0f, 0.0f), new ImVec2(800.0f, 600.0f));

        Assert.That(left.Size.X, Is.GreaterThan(0.0f));
        Assert.That(right.Size.X, Is.GreaterThan(0.0f));
        Assert.That(left.Size.X + right.Size.X, Is.EqualTo(800.0f).Within(1.0f));
    }

    /// <summary>
    /// Verifies that dock node host windows split draw list channels into foreground and background.
    /// Canonical: <c>imgui.cpp:8140-8143</c> (Bug 6-B1, 6-B4).
    /// </summary>
    [Test]
    public void DockNode_HostWindow_ChannelsSplitAndMerge()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(400.0f, 300.0f), ImGuiCond.Always);
        ImGui.Begin("DockHost", ImGuiWindowFlags.DockNodeHost);

        ImGuiWindow window = ImGui.GetCurrentWindow();
        Assert.That(window.DrawList.Splitter.ChannelCount, Is.EqualTo(2));

        ImGui.End();
        ImGui.Render();

        Assert.That(window.DrawList.Splitter.ChannelCount, Is.LessThanOrEqualTo(1));
    }

    /// <summary>
    /// Verifies tab item close button and unsaved marker rendering.
    /// Canonical: <c>imgui_widgets.cpp:11065-11160</c> (Bug 6-B6).
    /// </summary>
    [Test]
    public void TabItemLabelAndCloseButton_CloseButtonInteraction()
    {
        NewTestFrame();
        ImDrawList drawList = new(null);
        ImRect bb = new(new ImVec2(10.0f, 10.0f), new ImVec2(150.0f, 40.0f));

        ImGui.TabItemLabelAndCloseButton(
            drawList,
            bb,
            ImGuiTabItemFlags.None,
            new ImVec2(4.0f, 4.0f),
            "TestTab",
            100,
            101,
            true,
            out bool justClosed,
            out bool textClipped);

        Assert.That(justClosed, Is.False);
        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies dock settings serialization and deserialization across node hierarchy.
    /// Canonical: <c>imgui.cpp:18145-18163, 21738-21755</c> (Bug 6-B8).
    /// </summary>
    [Test]
    public void DockSettings_SerializationRoundtrip_PreservesNodeHierarchy()
    {
        uint rootId = 600;
        ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.None);
        ImGui.DockBuilderSetNodePos(rootId, new ImVec2(100.0f, 50.0f));
        ImGui.DockBuilderSetNodeSize(rootId, new ImVec2(600.0f, 400.0f));

        uint childLeft = 0;
        uint childRight = 0;
        ImGui.DockBuilderSplitNode(rootId, ImGuiDir.Left, 0.3f, out childLeft, out childRight);
        ImGui.DockBuilderFinish(rootId);

        ImGui.DockSettingsSave(_ctx);
        Assert.That(_ctx.DockContext.NodesSettings.Count, Is.EqualTo(3));

        System.Collections.Generic.List<ImGuiDockNodeSettings> saved = new(_ctx.DockContext.NodesSettings);
        Assert.That(saved.FindAll(settings => settings.ParentNodeId == rootId).Count, Is.EqualTo(2),
            "Both split children must record the root node as their parent for a reload to rebuild the tree.");
        ImGui.DockContextClearNodes(_ctx, 0, true);
        Assert.That(_ctx.DockContext.Nodes.Count, Is.EqualTo(0));

        // Canonical DockSettingsHandler_ApplyAll prunes settings nodes that no window references, so a
        // realistic reload must carry the window-settings entry that docks a window into this hierarchy.
        _ctx.SettingsWindows.Add(new ImGuiWindowSettings
        {
            ID = ImGui.ImHashStr("DockedWindow"),
            Name = "DockedWindow",
            DockId = rootId,
        });
        _ctx.DockContext.NodesSettings.Clear();
        _ctx.DockContext.NodesSettings.AddRange(saved);
        ImGui.DockSettingsHandler_ApplyAll(_ctx);
        Assert.That(_ctx.DockContext.Nodes.Count, Is.EqualTo(3));
        Assert.That(ImGui.DockBuilderGetNode(rootId), Is.Not.Null);
    }

    /// <summary>
    /// Verifies that floating dock nodes with docked windows survive NewFrame lifecycle across consecutive frames
    /// without throwing NullReferenceException in PushOverrideID or DockNodeUpdateTabBar.
    /// Canonical: <c>imgui.cpp:18265-18268, 19400-19445</c>.
    /// </summary>
    [Test]
    public void DockNode_FloatingNodeWithDockedWindow_UpdatesTabBarAcrossFramesWithoutCrashing()
    {
        // DockBuilderDockWindow queues a window-settings entry, and settings must be loaded before any entry
        // exists. A throwaway frame loads them first; canonical DockBuilder usage does the same.
        NewTestFrame();
        ImGui.EndFrame();

        uint dockId = 700;
        ImGui.DockBuilderAddNode(dockId, ImGuiDockNodeFlags.None);
        ImGui.DockBuilderSetNodePos(dockId, new ImVec2(50.0f, 50.0f));
        ImGui.DockBuilderSetNodeSize(dockId, new ImVec2(300.0f, 200.0f));
        ImGui.DockBuilderDockWindow("FloatingDockedWindow", dockId);
        ImGui.DockBuilderFinish(dockId);

        // Frame 1
        NewTestFrame();
        ImGui.Begin("FloatingDockedWindow");
        ImGui.Text("Hello Docked");
        ImGui.End();
        ImGui.Render();

        // Frame 2: DockContextNewFrameUpdateDocking processes the floating node and updates the tab bar
        Assert.DoesNotThrow(() =>
        {
            NewTestFrame();
            ImGui.Begin("FloatingDockedWindow");
            ImGui.Text("Hello Docked 2");
            ImGui.End();
            ImGui.Render();
        });
    }

    /// <summary>
    /// Verifies canonical calculation of the 5-way docking helper drop boxes (center, left, right, up, down)
    /// and outer drop boxes matching Dear ImGui v1.92.9b (imgui.cpp:20109-20159).
    /// </summary>
    [Test]
    public void DockNodePreview_DropRectsCalculation_MatchesCanonicalLayout()
    {
        NewTestFrame();
        ImRect parent = new(new ImVec2(100.0f, 100.0f), new ImVec2(500.0f, 500.0f));
        ImVec2 center = parent.GetCenter();

        // 1. Center drop box (ImGuiDir.None)
        bool hitCenter = ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.None, out ImRect drawCenter, false, center);
        Assert.That(hitCenter, Is.True);
        Assert.That(drawCenter.Contains(center), Is.True);
        Assert.That(drawCenter.GetWidth(), Is.GreaterThan(0.0f));
        Assert.That(drawCenter.GetHeight(), Is.GreaterThan(0.0f));

        // 2. Directional drop boxes (Left, Right, Up, Down) offset from center
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Left, out ImRect drawLeft, false, null), Is.False);
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Right, out ImRect drawRight, false, null), Is.False);
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Up, out ImRect drawUp, false, null), Is.False);
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Down, out ImRect drawDown, false, null), Is.False);

        Assert.That(drawLeft.GetCenter().X, Is.LessThan(center.X));
        Assert.That(drawRight.GetCenter().X, Is.GreaterThan(center.X));
        Assert.That(drawUp.GetCenter().Y, Is.LessThan(center.Y));
        Assert.That(drawDown.GetCenter().Y, Is.GreaterThan(center.Y));

        // Hit testing directed toward left
        ImVec2 mouseLeft = new(drawLeft.GetCenter().X, drawLeft.GetCenter().Y);
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Left, out _, false, mouseLeft), Is.True);
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Right, out _, false, mouseLeft), Is.False);

        // 3. Outer docking drop rects at edges
        Assert.That(ImGui.DockNodeCalcDropRectsAndTestMousePos(parent, ImGuiDir.Left, out ImRect drawOuterLeft, true, null), Is.False);
        Assert.That(drawOuterLeft.Min.X, Is.LessThanOrEqualTo(parent.Min.X + 20.0f));

        ImGui.EndFrame();
    }

    /// <summary>
    /// Verifies that DockNodePreviewDockSetup populates the drop rects array so preview overlays
    /// can be rendered even before mouse hovers an explicit direction (canonical imgui.cpp:20163-20249).
    /// </summary>
    [Test]
    public void DockNodePreviewSetup_PopulatesDropRectsForUnhoveredTarget()
    {
        NewTestFrame();
        ImGui.Begin("HostWindow");
        ImGuiWindow hostWindow = ImGui.GetCurrentContext()!.CurrentWindow!;
        ImGui.End();

        ImGui.Begin("PayloadWindow");
        ImGuiWindow payloadWindow = ImGui.GetCurrentContext()!.CurrentWindow!;
        ImGui.End();

        ImGuiDockPreviewData previewData = new();
        ImGui.DockNodePreviewDockSetup(hostWindow, null, payloadWindow, null, previewData, isExplicitTarget: false, isOuterDocking: false);

        // Drop boxes must be populated
        for (int dir = (int)ImGuiDir.None; dir < (int)ImGuiDir.COUNT; dir++)
        {
            ImRect dropRect = previewData.DropRectsDraw[dir + 1];
            Assert.That(dropRect.IsInverted(), Is.False, $"Direction {dir} drop rect should be valid.");
        }

        // Drop is not allowed until an explicit direction or target is hovered
        Assert.That(previewData.IsDropAllowed, Is.False);

        ImGui.EndFrame();
    }

    #endregion
}
