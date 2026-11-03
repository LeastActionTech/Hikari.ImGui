// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical multi-selection and box-selection subsystem in
/// <c>Hikari.ImGui/ImGuiMultiSelect.cs</c>. Coverage includes scope lifecycle and flag
/// normalization, the request lists produced by <c>BeginMultiSelect()</c>/<c>EndMultiSelect()</c>
/// (select-all, clear and range requests with their endpoints and direction), the selection state
/// and toggled-selection reporting of a submitted <c>Selectable</c> and <c>TreeNode</c>, and the
/// box-selection state machine with its clip-unlock rectangle.
/// </summary>
/// <remarks>
/// Every test drives a real <see cref="ImGuiContext"/> with queued mouse position and button events.
/// </remarks>
[TestFixture]
public class MultiSelectTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);

    /// <summary>Number of selectable items submitted by the fixture's scope frames.</summary>
    private const int ItemCount = 3;

    /// <summary>Labels used for the three selectable items; kept stable so item identifiers persist.</summary>
    private static readonly string[] ItemLabels = { "A", "B", "C" };


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
    /// that window is current.
    /// </summary>
    /// <param name="body">The submissions and assertions to run while the window is current.</param>
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

    /// <summary>
    /// Runs one frame containing a complete multi-select scope and returns the end-phase IO.
    /// </summary>
    /// <param name="flags">The multi-select flags passed to <c>BeginMultiSelect()</c>.</param>
    /// <param name="submitItems">Submits the selectable items between the begin and end calls.</param>
    /// <returns>The IO returned by <c>EndMultiSelect()</c>.</returns>
    private static ImGuiMultiSelectIO MultiSelectFrame(ImGuiMultiSelectFlags flags, Action submitItems)
    {
        ImGuiMultiSelectIO endIo = null!;
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(flags, -1, ItemCount);
            submitItems();
            endIo = ImGui.EndMultiSelect();
        });
        return endIo;
    }

    /// <summary>
    /// Submits the three selectable items with the supplied selection states and records each item's
    /// center.
    /// </summary>
    /// <param name="selected">Per-item selection states passed to <c>Selectable()</c>.</param>
    /// <param name="centers">
    /// Receives the item centers when non-null. Only the first layout frame needs the geometry.
    /// </param>
    private static void SubmitItems(bool[] selected, ImVec2[]? centers)
    {
        for (int n = 0; n < ItemCount; n++)
        {
            ImGui.SetNextItemSelectionUserData(n);
            ImGui.Selectable(ItemLabels[n], selected[n]);
            if (centers != null)
                centers[n] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        }
    }

    #endregion

    #region Scope lifecycle and flag normalization

    /// <summary>
    /// Verifies that <c>BeginMultiSelect()</c> binds the scope identifier, storage and item count,
    /// that an unqualified flag word defaults to window scope, and that <c>EndMultiSelect()</c>
    /// restores the parent scope.
    /// </summary>
    [Test]
    public void BeginMultiSelect_BindsScopeAndNormalizesFlags()
    {
        uint windowScopeId = 0;
        ImGuiMultiSelectIO io = null!;
        bool storageBound = false;
        int itemsCount = 0;
        bool scopeWindow = false;
        int stackedDuringScope = 0;

        WindowFrame(() =>
        {
            ImGuiWindow window = _ctx.CurrentWindow!;
            windowScopeId = window.IDStack[^1];
            io = ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, 7, 5);
            ImGuiMultiSelectTempData ms = _ctx.CurrentMultiSelect!;
            storageBound = ms.Storage != null && ms.Storage.ID == windowScopeId && ReferenceEquals(ms.Storage.Window, window);
            itemsCount = io.ItemsCount;
            scopeWindow = (ms.Flags & ImGuiMultiSelectFlags.ScopeWindow) != 0;
            stackedDuringScope = _ctx.MultiSelectTempDataStacked;
            ImGui.EndMultiSelect();
        });

        Assert.That(storageBound, Is.True);
        Assert.That(itemsCount, Is.EqualTo(5));
        Assert.That(scopeWindow, Is.True);
        Assert.That(stackedDuringScope, Is.EqualTo(1));
        Assert.That(_ctx.MultiSelectTempDataStacked, Is.EqualTo(0));
        Assert.That(_ctx.CurrentMultiSelect, Is.Null);
        Assert.That(io.Requests.Count, Is.EqualTo(0));
        Assert.That(io.RangeSrcItem, Is.EqualTo(-1));
        Assert.That(io.NavIdItem, Is.EqualTo(-1));
    }

    /// <summary>
    /// Verifies that the single-select flag drops both box-selection flags, mirroring canonical
    /// normalization.
    /// </summary>
    [Test]
    public void BeginMultiSelect_SingleSelect_RemovesBoxSelectFlags()
    {
        ImGuiMultiSelectFlags effective = 0;
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.SingleSelect | ImGuiMultiSelectFlags.BoxSelect2d | ImGuiMultiSelectFlags.BoxSelect1d);
            effective = _ctx.CurrentMultiSelect!.Flags;
            ImGui.EndMultiSelect();
        });

        Assert.That((effective & ImGuiMultiSelectFlags.SingleSelect) != 0, Is.True);
        Assert.That((effective & ImGuiMultiSelectFlags.BoxSelect1d) == 0, Is.True);
        Assert.That((effective & ImGuiMultiSelectFlags.BoxSelect2d) == 0, Is.True);
    }

    #endregion

    #region Request emission and Selectable integration

    /// <summary>
    /// Verifies that pressing an unselected item emits a clear-all request followed by a single-item
    /// range request, and that the item reports a toggled selection.
    /// </summary>
    [Test]
    public void Selectable_Press_EmitsClearAndSingleItemRange()
    {
        bool[] selected = new bool[ItemCount];
        ImVec2[] centers = new ImVec2[ItemCount];
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, centers));

        _ctx.IO.AddMousePosEvent(centers[0].X, centers[0].Y);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));

        bool toggledOnPress = false;
        long queuedUserData = -1;
        _ctx.IO.AddMouseButtonEvent(0, true);
        ImGuiMultiSelectIO pressIo = MultiSelectFrame(ImGuiMultiSelectFlags.None, () =>
        {
            ImGui.SetNextItemSelectionUserData(0);
            ImGui.Selectable(ItemLabels[0], selected[0]);
            toggledOnPress = ImGui.IsItemToggledSelection();
            queuedUserData = _ctx.NextItemData.SelectionUserData;
            ImGui.SetNextItemSelectionUserData(1);
            ImGui.Selectable(ItemLabels[1], selected[1]);
            ImGui.SetNextItemSelectionUserData(2);
            ImGui.Selectable(ItemLabels[2], selected[2]);
        });

        Assert.That(queuedUserData, Is.EqualTo(0));
        Assert.That(toggledOnPress, Is.True);
        Assert.That(pressIo.Requests.Count, Is.EqualTo(2));
        Assert.That(pressIo.Requests[0].Type, Is.EqualTo(ImGuiSelectionRequestType.SetAll));
        Assert.That(pressIo.Requests[0].Selected, Is.False);
        Assert.That(pressIo.Requests[1].Type, Is.EqualTo(ImGuiSelectionRequestType.SetRange));
        Assert.That(pressIo.Requests[1].Selected, Is.True);
        Assert.That(pressIo.Requests[1].RangeDirection, Is.EqualTo(1));
        Assert.That(pressIo.Requests[1].RangeFirstItem, Is.EqualTo(0));
        Assert.That(pressIo.Requests[1].RangeLastItem, Is.EqualTo(0));

        _ctx.IO.AddMouseButtonEvent(0, false);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));
    }

    /// <summary>
    /// Verifies that a Shift press extends the selection from the stored range source to the pressed
    /// item, producing endpoints in ascending order and a positive direction.
    /// </summary>
    [Test]
    public void ShiftPress_EmitsRangeAcrossItems()
    {
        bool[] selected = new bool[ItemCount];
        ImVec2[] centers = new ImVec2[ItemCount];
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, centers));

        // First press selects item 0 and stores it as the range source.
        _ctx.IO.AddMousePosEvent(centers[0].X, centers[0].Y);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));
        _ctx.IO.AddMouseButtonEvent(0, true);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));
        _ctx.IO.AddMouseButtonEvent(0, false);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));

        // Shift-press item 2: the range spans 0.2 with a positive direction.
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Shift, true);
        _ctx.IO.AddMousePosEvent(centers[2].X, centers[2].Y);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));
        _ctx.IO.AddMouseButtonEvent(0, true);
        int requestCount = 0;
        ImGuiSelectionRequest clearRequest = default;
        ImGuiSelectionRequest rangeRequest = default;
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, -1, ItemCount);
            ImGui.SetNextItemSelectionUserData(0);
            ImGui.Selectable(ItemLabels[0], selected[0]);
            ImGui.SetNextItemSelectionUserData(1);
            ImGui.Selectable(ItemLabels[1], selected[1]);
            ImGui.SetNextItemSelectionUserData(2);
            ImGui.Selectable(ItemLabels[2], selected[2]);
            ImGuiMultiSelectIO io = ImGui.EndMultiSelect();
            requestCount = io.Requests.Count;
            if (requestCount >= 2)
            {
                clearRequest = io.Requests[0];
                rangeRequest = io.Requests[1];
            }
        });
        _ctx.IO.AddMouseButtonEvent(0, false);
        MultiSelectFrame(ImGuiMultiSelectFlags.None, () => SubmitItems(selected, null));
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Shift, false);

        Assert.That(requestCount, Is.EqualTo(2));
        Assert.That(clearRequest.Type, Is.EqualTo(ImGuiSelectionRequestType.SetAll));
        Assert.That(clearRequest.Selected, Is.False);
        Assert.That(rangeRequest.Type, Is.EqualTo(ImGuiSelectionRequestType.SetRange));
        Assert.That(rangeRequest.Selected, Is.True);
        Assert.That(rangeRequest.RangeDirection, Is.EqualTo(1));
        Assert.That(rangeRequest.RangeFirstItem, Is.EqualTo(0));
        Assert.That(rangeRequest.RangeLastItem, Is.EqualTo(2));

        // Applying the requests yields the three-item selection.
        ImGuiSelectionBasicStorage selection = new();
        ImGuiMultiSelectIO applyIo = new() { ItemsCount = ItemCount };
        applyIo.Requests.Add(clearRequest);
        applyIo.Requests.Add(rangeRequest);
        selection.ApplyRequests(applyIo);
        Assert.That(selection.Size, Is.EqualTo(3));
        Assert.That(selection.Contains(0), Is.True);
        Assert.That(selection.Contains(2), Is.True);
    }

    /// <summary>
    /// Verifies that the clear-on-click-void flag emits a single clear-all request when empty space
    /// in the scope is released without dragging.
    /// </summary>
    [Test]
    public void ClearOnClickVoid_Release_EmitsClearAll()
    {
        bool[] selected = new bool[ItemCount];
        MultiSelectFrame(
            ImGuiMultiSelectFlags.ScopeWindow | ImGuiMultiSelectFlags.ClearOnClickVoid,
            () => SubmitItems(selected, null));

        ImVec2 empty = new(WindowOrigin.X + 300.0f, WindowOrigin.Y + 300.0f);
        _ctx.IO.AddMousePosEvent(empty.X, empty.Y);
        MultiSelectFrame(
            ImGuiMultiSelectFlags.ScopeWindow | ImGuiMultiSelectFlags.ClearOnClickVoid,
            () => SubmitItems(selected, null));
        _ctx.IO.AddMouseButtonEvent(0, true);
        MultiSelectFrame(
            ImGuiMultiSelectFlags.ScopeWindow | ImGuiMultiSelectFlags.ClearOnClickVoid,
            () => SubmitItems(selected, null));
        _ctx.IO.AddMouseButtonEvent(0, false);
        ImGuiMultiSelectIO releaseIo = MultiSelectFrame(
            ImGuiMultiSelectFlags.ScopeWindow | ImGuiMultiSelectFlags.ClearOnClickVoid,
            () => SubmitItems(selected, null));

        Assert.That(releaseIo.Requests.Count, Is.EqualTo(1));
        Assert.That(releaseIo.Requests[0].Type, Is.EqualTo(ImGuiSelectionRequestType.SetAll));
        Assert.That(releaseIo.Requests[0].Selected, Is.False);
    }

    #endregion

    #region Tree node integration

    /// <summary>
    /// Verifies that a <c>TreeNode</c> submitted inside a multi-select scope becomes a multi-select item:
    /// its open behavior is promoted to open-on-arrow and open-on-double-click, clicking the label
    /// selects the node instead of toggling it open, and the scope emits the canonical clear-all request
    /// followed by a single-item range request.
    /// <c>, 7067-7077, 7124-7131</c>.
    /// </summary>
    [Test]
    public void TreeNode_PressOnLabel_SelectsInsteadOfTogglingOpen()
    {
        const int nodeItemCount = 1;
        bool nodeOpen = false;
        ImRect nodeRect = default;

        // Layout frame: resolve the node rectangle so the later press can target the label rather than
        // the arrow, which toggles the node open instead of selecting it.
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, -1, nodeItemCount);
            ImGui.SetNextItemSelectionUserData(0);
            nodeOpen = ImGui.TreeNodeEx("Node", ImGuiTreeNodeFlags.None);
            nodeRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.EndMultiSelect();
        });

        // Hover the right edge of the node: outside the arrow hit area, which spans one font size plus
        // padding from the node origin.
        ImVec2 labelPoint = new(nodeRect.Max.X - 2.0f, (nodeRect.Min.Y + nodeRect.Max.Y) * 0.5f);
        _ctx.IO.AddMousePosEvent(labelPoint.X, labelPoint.Y);
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, -1, nodeItemCount);
            ImGui.SetNextItemSelectionUserData(0);
            ImGui.TreeNodeEx("Node", ImGuiTreeNodeFlags.None);
            ImGui.EndMultiSelect();
        });

        // Press on the label and inspect the same frame's submission. The multi-select IO and its request
        // buffer belong to the scope and are reused every frame, so the requests are copied out while the
        // frame that produced them is still current.
        _ctx.IO.AddMouseButtonEvent(0, true);
        bool toggledSelection = false;
        bool toggledOpen = false;
        bool isMultiSelectItem = false;
        long queuedUserData = -1;
        List<ImGuiSelectionRequest> requests = new();
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, -1, nodeItemCount);
            ImGui.SetNextItemSelectionUserData(0);
            nodeOpen = ImGui.TreeNodeEx("Node", ImGuiTreeNodeFlags.None);
            toggledSelection = ImGui.IsItemToggledSelection();
            toggledOpen = ImGui.IsItemToggledOpen();
            isMultiSelectItem = (_ctx.LastItemData.ItemFlags
                & (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_IsMultiSelect) != 0;
            queuedUserData = _ctx.NextItemData.SelectionUserData;
            requests.AddRange(ImGui.EndMultiSelect().Requests);
        });

        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(() =>
        {
            ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, -1, nodeItemCount);
            ImGui.SetNextItemSelectionUserData(0);
            ImGui.TreeNodeEx("Node", ImGuiTreeNodeFlags.None);
            ImGui.EndMultiSelect();
        });

        // The node stays closed because the label press selects rather than toggles.
        Assert.That(nodeOpen, Is.False);
        Assert.That(toggledOpen, Is.False);
        Assert.That(isMultiSelectItem, Is.True);
        Assert.That(queuedUserData, Is.EqualTo(0));
        Assert.That(toggledSelection, Is.True);

        // The request sequence and its endpoints identify the pressed node exactly.
        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.That(requests[0].Type, Is.EqualTo(ImGuiSelectionRequestType.SetAll));
        Assert.That(requests[0].Selected, Is.False);
        Assert.That(requests[1].Type, Is.EqualTo(ImGuiSelectionRequestType.SetRange));
        Assert.That(requests[1].Selected, Is.True);
        Assert.That(requests[1].RangeDirection, Is.EqualTo(1));
        Assert.That(requests[1].RangeFirstItem, Is.EqualTo(0));
        Assert.That(requests[1].RangeLastItem, Is.EqualTo(0));
    }

    #endregion

    #region Selection storage helpers

    /// <summary>
    /// Verifies <c>ImGuiSelectionBasicStorage.ApplyRequests</c> for a select-all followed by a range
    /// clear, together with concrete <c>Size</c>, <c>Contains</c> and iteration results.
    ///  and <c>:8678-8701</c>.
    /// </summary>
    [Test]
    public void SelectionBasicStorage_ApplySelectAllAndRangeClear_UpdatesSelection()
    {
        ImGuiSelectionBasicStorage selection = new();

        ImGuiMultiSelectIO selectAllIo = new() { ItemsCount = 4 };
        selectAllIo.Requests.Add(new ImGuiSelectionRequest
        {
            Type = ImGuiSelectionRequestType.SetAll,
            Selected = true,
            RangeDirection = 0,
            RangeFirstItem = -1,
            RangeLastItem = -1,
        });
        selection.ApplyRequests(selectAllIo);

        Assert.That(selection.Size, Is.EqualTo(4));
        Assert.That(selection.Contains(0), Is.True);
        Assert.That(selection.Contains(3), Is.True);
        Assert.That(selection.Contains(4), Is.False);

        List<uint> iterated = new();
        int iterator = -1;
        while (selection.GetNextSelectedItem(ref iterator, out uint id))
            iterated.Add(id);
        Assert.That(iterated, Is.EqualTo(new List<uint> { 0, 1, 2, 3 }));

        ImGuiMultiSelectIO rangeIo = new() { ItemsCount = 4 };
        rangeIo.Requests.Add(new ImGuiSelectionRequest
        {
            Type = ImGuiSelectionRequestType.SetRange,
            Selected = false,
            RangeDirection = 1,
            RangeFirstItem = 1,
            RangeLastItem = 2,
        });
        selection.ApplyRequests(rangeIo);

        Assert.That(selection.Size, Is.EqualTo(2));
        Assert.That(selection.Contains(0), Is.True);
        Assert.That(selection.Contains(1), Is.False);
        Assert.That(selection.Contains(2), Is.False);
        Assert.That(selection.Contains(3), Is.True);
    }

    /// <summary>
    /// Verifies that <c>PreserveOrder</c> returns items in selection order and restores the
    /// identifier ordering when the iteration completes.
    /// </summary>
    [Test]
    public void SelectionBasicStorage_PreserveOrder_IteratesInSelectionOrder()
    {
        ImGuiSelectionBasicStorage selection = new() { PreserveOrder = true };
        selection.SetItemSelected(5, true);
        selection.SetItemSelected(3, true);
        selection.SetItemSelected(4, true);

        List<uint> iterated = new();
        int iterator = -1;
        while (selection.GetNextSelectedItem(ref iterator, out uint id))
            iterated.Add(id);

        Assert.That(iterated, Is.EqualTo(new List<uint> { 5, 3, 4 }));
        Assert.That(selection._Storage.Data[0].Key, Is.EqualTo(3));
        Assert.That(selection.Size, Is.EqualTo(3));
    }

    /// <summary>
    /// Verifies that <c>Swap</c> exchanges the two selections, including stores of different sizes.
    /// </summary>
    [Test]
    public void SelectionBasicStorage_Swap_ExchangesContents()
    {
        ImGuiSelectionBasicStorage a = new();
        a.SetItemSelected(1, true);
        a.SetItemSelected(2, true);
        ImGuiSelectionBasicStorage b = new();
        b.SetItemSelected(9, true);

        a.Swap(b);

        Assert.That(a.Size, Is.EqualTo(1));
        Assert.That(a.Contains(9), Is.True);
        Assert.That(a.Contains(1), Is.False);
        Assert.That(b.Size, Is.EqualTo(2));
        Assert.That(b.Contains(1), Is.True);
        Assert.That(b.Contains(2), Is.True);
        Assert.That(b.Contains(9), Is.False);
    }

    /// <summary>
    /// Verifies that <c>ImGuiSelectionExternalStorage.ApplyRequests</c> invokes the adapter for every
    /// item of a select-all request and for each endpoint of a range request.
    /// </summary>
    [Test]
    public void SelectionExternalStorage_ApplyRequests_InvokesAdapter()
    {
        List<(int Index, bool Selected)> applied = new();
        ImGuiSelectionExternalStorage external = new()
        {
            AdapterSetItemSelected = (_, index, selected) => applied.Add((index, selected)),
        };

        ImGuiMultiSelectIO selectAllIo = new() { ItemsCount = 3 };
        selectAllIo.Requests.Add(new ImGuiSelectionRequest
        {
            Type = ImGuiSelectionRequestType.SetAll,
            Selected = true,
        });
        external.ApplyRequests(selectAllIo);
        Assert.That(applied, Is.EqualTo(new List<(int, bool)> { (0, true), (1, true), (2, true) }));

        ImGuiMultiSelectIO rangeIo = new() { ItemsCount = 3 };
        rangeIo.Requests.Add(new ImGuiSelectionRequest
        {
            Type = ImGuiSelectionRequestType.SetRange,
            Selected = false,
            RangeDirection = 1,
            RangeFirstItem = 1,
            RangeLastItem = 2,
        });
        external.ApplyRequests(rangeIo);
        Assert.That(applied, Is.EqualTo(new List<(int, bool)>
        {
            (0, true), (1, true), (2, true), (1, false), (2, false),
        }));
    }

    #endregion

    #region Box selection

    /// <summary>
    /// Verifies the box-selection state transitions from a synthesized drag: a press in void space
    /// marks the drag as starting, moving past the drag threshold activates it and computes the
    /// clip-unlock rectangle, and releasing deactivates it.
    /// </summary>
    [Test]
    public void BoxSelect_PressDragRelease_TransitionsAndComputesUnclipRect()
    {
        ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.ScopeWindow | ImGuiMultiSelectFlags.BoxSelect1d;
        bool[] selected = new bool[ItemCount];
        MultiSelectFrame(flags, () => SubmitItems(selected, null));

        // Press in void space below the items: EndMultiSelect() records the candidate start.
        ImVec2 empty = new(WindowOrigin.X + 300.0f, WindowOrigin.Y + 300.0f);
        _ctx.IO.AddMousePosEvent(empty.X, empty.Y);
        MultiSelectFrame(flags, () => SubmitItems(selected, null));
        _ctx.IO.AddMouseButtonEvent(0, true);
        MultiSelectFrame(flags, () => SubmitItems(selected, null));

        ImGuiBoxSelectState bs = _ctx.BoxSelectState;
        Assert.That(bs.IsStarting, Is.True);
        Assert.That(bs.IsActive, Is.False);
        Assert.That(bs.ID, Is.Not.EqualTo(0));

        // Drag far past the window: BeginBoxSelect() activates the drag and updates the rectangles.
        ImVec2 far = new(2000.0f, 2000.0f);
        _ctx.IO.AddMousePosEvent(far.X, far.Y);

        bool activeDuringDrag = false;
        ImRect unclipRect = default;
        bool unclipMode = false;
        ImRect boxSelectRectCurr = default;
        ImVec2 scopeMax = default;
        ImRect windowClip = default;
        uint windowId = 0;

        MultiSelectFrame(flags, () =>
        {
            ImGuiWindow window = _ctx.CurrentWindow!;
            activeDuringDrag = bs.IsActive;
            unclipRect = bs.UnclipRect;
            unclipMode = bs.UnclipMode;
            boxSelectRectCurr = bs.BoxSelectRectCurr;
            windowId = window.ID;
            scopeMax = window.InnerClipRect.Max;
            windowClip = window.ClipRect;
            SubmitItems(selected, null);
        });

        Assert.That(activeDuringDrag, Is.True);
        Assert.That(bs.IsActive, Is.True);
        Assert.That(bs.IsStarting, Is.False);
        Assert.That(bs.Window, Is.Not.Null);
        Assert.That(bs.Window!.ID, Is.EqualTo(windowId));

        // The current rectangle and the merged unclip rectangle span the start point to the clamped end.
        Assert.That(boxSelectRectCurr.Min.X, Is.EqualTo(empty.X).Within(0.5f));
        Assert.That(boxSelectRectCurr.Min.Y, Is.EqualTo(empty.Y).Within(0.5f));
        Assert.That(boxSelectRectCurr.Max.X, Is.EqualTo(scopeMax.X).Within(0.5f));
        Assert.That(boxSelectRectCurr.Max.Y, Is.EqualTo(scopeMax.Y).Within(0.5f));
        Assert.That(unclipRect.IsInverted(), Is.False);
        Assert.That(unclipRect.Min.X, Is.EqualTo(empty.X).Within(0.5f));
        Assert.That(unclipRect.Min.Y, Is.EqualTo(empty.Y).Within(0.5f));
        Assert.That(unclipRect.Max.X, Is.EqualTo(scopeMax.X).Within(0.5f));
        Assert.That(unclipRect.Max.Y, Is.EqualTo(scopeMax.Y).Within(0.5f));

        bool expectedUnclipMode = !windowClip.Contains(unclipRect.Min) || !windowClip.Contains(unclipRect.Max);
        Assert.That(unclipMode, Is.EqualTo(expectedUnclipMode));
        Assert.That(unclipMode, Is.True);

        // Release: the next BeginBoxSelect() deactivates the drag.
        _ctx.IO.AddMouseButtonEvent(0, false);
        MultiSelectFrame(flags, () => SubmitItems(selected, null));

        Assert.That(bs.IsActive, Is.False);
        Assert.That(bs.IsStarting, Is.False);
        Assert.That(bs.ID, Is.EqualTo(0));
        Assert.That(bs.UnclipMode, Is.False);
    }

    #endregion
}
