#nullable enable
/*================================================================================================
 * DEAR IMGUI MULTI-SELECT AND BOX-SELECT
 *================================================================================================
 * ROLE IN ENGINE:
 * Implements the canonical Dear ImGui multi-selection and box-selection subsystem of the
 * Hikari.ImGui port (v1.92.9b-docking). Multi-selection lets a caller submit a scope of items with
 * stable selection data, drive Ctrl/Shift range selection and keyboard navigation, and receive a
 * list of selection requests that describe what changed. Box-selection adds a drag rectangle that
 * toggles the selection of every multi-select item it overlaps, including items that are outside the
 * ordinary clip rectangle.
 *
 * SUBSYSTEM CONNECTIONS & DATA FLOW:
 *
 *   ┌──────────────────────────────────────┐
 *   │ ImGui::BeginMultiSelect()            │
 *   │ • pushes focus scope, binds storage  │
 *   └──────────────────────────────────────┘
 *                │ CurrentMultiSelect
 *                ▼
 *   ┌──────────────────────────────────────┐
 *   │ ImGuiMultiSelectTempData             │
 *   │ • IO.Requests, flags, key mods       │
 *   └──────────────────────────────────────┘
 *                │ header/footer
 *                ▼
 *   ┌──────────────────────────────────────┐
 *   │ Selectable / Checkbox item submission│
 *   │ • SetNextItemSelectionUserData()     │
 *   └──────────────────────────────────────┘
 *                │ IO
 *                ▼
 *   ┌──────────────────────────────────────┐
 *   │ ImGuiSelectionBasicStorage           │
 *   │ • ApplyRequests / GetNextSelectedItem│
 *   └──────────────────────────────────────┘
 *
 * STYLIZED USAGE EXAMPLE:
 *
 *   ImGuiMultiSelectIO io = ImGui.BeginMultiSelect(ImGuiMultiSelectFlags.None, -1, items.Count);
 *   ImGuiSelectionBasicStorage selection = new();
 *   selection.AdapterIndexToStorageId = static (_, idx) => (uint)idx;
 *   selection.ApplyRequests(io);
 *   for (int n = 0; n < items.Count; n++)
 *   {
 *       ImGui.SetNextItemSelectionUserData(n);
 *       bool selected = selection.Contains((uint)n);
 *       if (ImGui.Selectable(items[n], selected))
 *           ImGui.MarkItemEdited();
 *   }
 *   selection.ApplyRequests(ImGui.EndMultiSelect());
 *
 * THREADING & LIFECYCLE NOTES:
 * - Every type in this file is plain single-threaded mutable state owned by the current
 *   ImGuiContext. No internal synchronization is provided; the frame-submitting thread is the only
 *   writer.
 * - The IO object returned by BeginMultiSelect()/EndMultiSelect() is owned by the context and is
 *   only valid until the next call to either function. The temporary-data entries are retained across
 *   frames so that their request buffers keep their capacity.
 * - ImGuiSelectionBasicStorage and ImGuiSelectionExternalStorage are caller-owned helpers; they are
 *   not registered on the context and are released with the caller's own storage.
 *
 * Canonical reference: ThirdParty/imgui/imgui_widgets.cpp:7798-8830 and
 * ThirdParty/imgui/imgui_internal.h:1930-2018 (v1.92.9b-docking, commit b48d1af).
 *================================================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Hikari.ImGui;

#region Public multi-select types

/// <summary>
/// The selection request list produced by <c>BeginMultiSelect()</c> and <c>EndMultiSelect()</c>, with
/// the range endpoints that describe the current selection scope
/// (<c>ThirdParty/imgui/imgui.h:3233-3242</c>).
/// </summary>
/// <remarks>
/// The instance is owned by the context's stacked temporary data and is only valid until the next
/// call to <c>BeginMultiSelect()</c> or <c>EndMultiSelect()</c>. <see cref="Requests"/> is cleared in
/// place so its capacity is preserved across frames.
/// </remarks>
public sealed class ImGuiMultiSelectIO
{
    /// <summary>
    /// Gets the requests the application should apply to its selection data.
    /// (<c>ThirdParty/imgui/imgui.h:3235</c>)
    /// </summary>
    public List<ImGuiSelectionRequest> Requests { get; } = new();

    /// <summary>
    /// Gets or sets the source item of the current range selection, or <c>-1</c> when there is none.
    /// (<c>ThirdParty/imgui/imgui.h:3236</c>)
    /// </summary>
    /// <remarks>
    /// A clipped list must keep this item submitted so range selection can be evaluated; the application
    /// usually ensures that through <c>ImGuiListClipper.IncludeItemByIndex()</c>.
    /// </remarks>
    public long RangeSrcItem { get; set; } = -1;

    /// <summary>
    /// Gets or sets the selection user data of the navigation identifier, or <c>-1</c> when unknown.
    /// (<c>ThirdParty/imgui/imgui.h:3237</c>)
    /// </summary>
    /// <remarks>
    /// Used by applications that delete items to restore navigation focus to a surviving item.
    /// </remarks>
    public long NavIdItem { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last known selection state of <see cref="NavIdItem"/>.
    /// (<c>ThirdParty/imgui/imgui.h:3238</c>)
    /// </summary>
    public bool NavIdSelected { get; set; }

    /// <summary>
    /// Gets or sets a request to discard the stored range source before the next frame.
    /// (<c>ThirdParty/imgui/imgui.h:3239</c>)
    /// </summary>
    /// <remarks>
    /// The application sets this before calling <c>EndMultiSelect()</c> when the range source item was
    /// deleted.
    /// </remarks>
    public bool RangeSrcReset { get; set; }

    /// <summary>
    /// Gets or sets the item count copied from <c>BeginMultiSelect()</c> for the convenience of the
    /// request-application handler. (<c>ThirdParty/imgui/imgui.h:3240</c>)
    /// </summary>
    public int ItemsCount { get; set; } = -1;
}

/// <summary>
/// One selection change requested by the multi-select subsystem
/// (<c>ThirdParty/imgui/imgui.h:3253-3262</c>).
/// </summary>
/// <remarks>
/// Requests are value types so a request list can be produced without per-request heap allocation.
/// A range request stores its endpoints already ordered left-to-right; <see cref="RangeDirection"/>
/// preserves the direction in which the user produced the range.
/// </remarks>
public struct ImGuiSelectionRequest
{
    /// <summary>
    /// Gets or sets the request kind. (<c>ThirdParty/imgui/imgui.h:3256</c>)
    /// </summary>
    public ImGuiSelectionRequestType Type { get; set; }

    /// <summary>
    /// Gets or sets whether the requested items become selected; <c>false</c> clears them.
    /// (<c>ThirdParty/imgui/imgui.h:3257</c>)
    /// </summary>
    public bool Selected { get; set; }

    /// <summary>
    /// Gets or sets the direction of a range request: <c>+1</c> when the first endpoint was produced
    /// before the last, <c>-1</c> otherwise. (<c>ThirdParty/imgui/imgui.h:3258</c>)
    /// </summary>
    public sbyte RangeDirection { get; set; }

    /// <summary>
    /// Gets or sets the first endpoint of a range request, inclusive.
    /// (<c>ThirdParty/imgui/imgui.h:3259</c>)
    /// </summary>
    public long RangeFirstItem { get; set; }

    /// <summary>
    /// Gets or sets the last endpoint of a range request, inclusive.
    /// (<c>ThirdParty/imgui/imgui.h:3260</c>)
    /// </summary>
    public long RangeLastItem { get; set; }
}

/// <summary>
/// A convenience selection store backed by an <see cref="ImGuiStorage"/> keyed by persistent item
/// identifier (<c>ThirdParty/imgui/imgui.h:3279-3300</c>).
/// </summary>
/// <remarks>
/// <para>
/// The helper receives indices through the multi-select API and converts each index to a persistent
/// item identifier with <see cref="AdapterIndexToStorageId"/>. Selection order is only tracked when
/// <see cref="PreserveOrder"/> is set; the store otherwise makes no order guarantee.
/// </para>
/// <para>
/// The store is caller-owned and is not registered on the context. It is plain single-threaded state
/// with no internal synchronization.
/// </para>
/// </remarks>
public class ImGuiSelectionBasicStorage
{
    /// <summary>
    /// Gets the number of selected items currently held by this store.
    /// (<c>ThirdParty/imgui/imgui.h:3282</c>)
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="GetNextSelectedItem"/> returns items in the
    /// order they were selected. (<c>ThirdParty/imgui/imgui.h:3283</c>)
    /// </summary>
    public bool PreserveOrder { get; set; }

    /// <summary>
    /// Gets or sets caller data passed back to <see cref="AdapterIndexToStorageId"/>.
    /// (<c>ThirdParty/imgui/imgui.h:3284</c>)
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// Gets or sets the adapter that maps an index to its persistent item identifier.
    /// (<c>ThirdParty/imgui/imgui.h:3285</c>)
    /// </summary>
    /// <remarks>
    /// The default adapter returns the index cast to an identifier, which is the simplest possible
    /// mapping and is what the canonical sample adapter does.
    /// </remarks>
    public Func<ImGuiSelectionBasicStorage, int, uint> AdapterIndexToStorageId { get; set; }

    /// <summary>
    /// Gets or sets the increasing counter used to record selection order.
    /// (<c>ThirdParty/imgui/imgui.h:3286</c>)
    /// </summary>
    /// <remarks>
    /// The counter always starts above zero so that a stored value of zero unambiguously means
    /// "unselected"; this mirrors canonical <c>_SelectionOrder</c>. It is internal rather than private so
    /// the batch helpers and the test fixture can observe it, without widening the public surface.
    /// </remarks>
    internal int _SelectionOrder { get; set; }

    /// <summary>
    /// Gets the identifier-keyed selection set.
    /// (<c>ThirdParty/imgui/imgui.h:3287</c>)
    /// </summary>
    /// <remarks>
    /// This is the port's <see cref="ImGuiStorage"/> reuse of the canonical key-to-value store. It is
    /// internal because callers are expected to iterate with <see cref="GetNextSelectedItem"/> or query
    /// with <see cref="Contains"/>; the batch helpers below access it directly.
    /// </remarks>
    internal ImGuiStorage _Storage { get; } = new();

    /// <summary>
    /// Initializes an empty selection store whose default adapter maps an index directly to its
    /// identifier. (<c>ThirdParty/imgui/imgui_widgets.cpp:8640-8648</c>)
    /// </summary>
    public ImGuiSelectionBasicStorage()
    {
        Size = 0;
        PreserveOrder = false;
        UserData = null;
        AdapterIndexToStorageId = static (_, idx) => (uint)idx;
        _SelectionOrder = 1;
    }

    /// <summary>
    /// Removes every selected item. (<c>ThirdParty/imgui/imgui_widgets.cpp:8650-8655</c>)
    /// </summary>
    public void Clear()
    {
        Size = 0;
        _SelectionOrder = 1;
        _Storage.Data.Clear();
    }

    /// <summary>
    /// Exchanges the selection contents of two stores.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8657-8662</c>)
    /// </summary>
    /// <param name="r">The store to exchange contents with.</param>
    public void Swap(ImGuiSelectionBasicStorage r)
    {
        (Size, r.Size) = (r.Size, Size);
        (_SelectionOrder, r._SelectionOrder) = (r._SelectionOrder, _SelectionOrder);
        _Storage.SwapData(r._Storage);
    }

    /// <summary>
    /// Queries whether an item identifier is selected.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8664-8667</c>)
    /// </summary>
    /// <param name="id">The persistent item identifier.</param>
    /// <returns>True when the item is selected.</returns>
    public bool Contains(uint id)
    {
        return _Storage.GetInt(id, 0) != 0;
    }

    /// <summary>
    /// Converts an index to its persistent item identifier using the configured adapter.
    /// (<c>ThirdParty/imgui/imgui.h:3299</c>)
    /// </summary>
    /// <param name="idx">The index passed to the multi-select API.</param>
    /// <returns>The persistent identifier produced by <see cref="AdapterIndexToStorageId"/>.</returns>
    public uint GetStorageIdFromIndex(int idx)
    {
        return AdapterIndexToStorageId(this, idx);
    }

    /// <summary>
    /// Adds or removes a single item. Usually called by <see cref="ApplyRequests"/>; applications may
    /// also use it directly. (<c>ThirdParty/imgui/imgui_widgets.cpp:8703-8708</c>)
    /// </summary>
    /// <param name="id">The persistent item identifier.</param>
    /// <param name="selected">True to select, false to clear.</param>
    public void SetItemSelected(uint id, bool selected)
    {
        ImGuiStoragePair pair = _Storage.GetIntRef(id, 0);
        if (selected && pair.ValueInt == 0)
        {
            pair.ValueInt = _SelectionOrder++;
            Size++;
        }
        else if (!selected && pair.ValueInt != 0)
        {
            pair.ValueInt = 0;
            Size--;
        }
    }

    /// <summary>
    /// Iterates the selected identifiers.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8678-8701</c>)
    /// </summary>
    /// <param name="opaqueIt">
    /// The iteration cursor. Initialize it to <c>-1</c> before the first call; each call advances it.
    /// </param>
    /// <param name="outId">Receives the next selected identifier, or zero when the iteration is done.</param>
    /// <returns>True while a selected identifier was produced.</returns>
    /// <remarks>
    /// When <see cref="PreserveOrder"/> is set the store is sorted by selection order before the first
    /// item is produced and re-sorted by identifier when the iteration completes, exactly as canonical
    /// does. The cursor is an index into the port's contiguous pair buffer.
    /// </remarks>
    public bool GetNextSelectedItem(ref int opaqueIt, out uint outId)
    {
        // The canonical iterator is a pointer into the sorted contiguous buffer. This port uses the
        // buffer index, with -1 marking the not-yet-started state that canonical spells as a null
        // pointer.
        if (PreserveOrder && opaqueIt < 0)
            _Storage.Data.Sort(static (lhs, rhs) => lhs.ValueInt.CompareTo(rhs.ValueInt));

        int it = opaqueIt < 0 ? 0 : opaqueIt;
        while (it < _Storage.Data.Count && _Storage.Data[it].ValueInt == 0)
            it++;

        bool hasMore = it < _Storage.Data.Count;
        opaqueIt = hasMore ? it + 1 : it;
        outId = hasMore ? _Storage.Data[it].Key : 0;
        if (PreserveOrder && !hasMore)
            _Storage.BuildSortByKey();
        return hasMore;
    }

    /// <summary>
    /// Applies every request produced by <c>BeginMultiSelect()</c> and <c>EndMultiSelect()</c> to this
    /// store. (<c>ThirdParty/imgui/imgui_widgets.cpp:8730-8800</c>)
    /// </summary>
    /// <param name="msIo">The IO instance whose requests are applied.</param>
    /// <remarks>
    /// The batch path follows canonical: a select-all appends every identifier unsorted and sorts once,
    /// while a wide range append inserts with an explicit selection-order value so that a backward range
    /// keeps its order. The store must receive an item count from
    /// <c>BeginMultiSelect()</c> for the select-all request to be meaningful.
    /// </remarks>
    public void ApplyRequests(ImGuiMultiSelectIO msIo)
    {
        ImAssert.Check(msIo.ItemsCount != -1, "Missing value for items_count in BeginMultiSelect() call!");
        ImAssert.Check(AdapterIndexToStorageId != null);

        for (int requestN = 0; requestN < msIo.Requests.Count; requestN++)
        {
            ImGuiSelectionRequest req = msIo.Requests[requestN];
            if (req.Type == ImGuiSelectionRequestType.SetAll)
            {
                Clear();
                if (req.Selected)
                {
                    _Storage.Data.Capacity = Math.Max(_Storage.Data.Capacity, msIo.ItemsCount);
                    int sizeBeforeAmends = _Storage.Data.Count;
                    for (int idx = 0; idx < msIo.ItemsCount; idx++, _SelectionOrder++)
                        BatchSetItemSelected(this, GetStorageIdFromIndex(idx), req.Selected, sizeBeforeAmends, _SelectionOrder);
                    BatchFinish(this, req.Selected, sizeBeforeAmends);
                }
            }
            else if (req.Type == ImGuiSelectionRequestType.SetRange)
            {
                int selectionChanges = (int)req.RangeLastItem - (int)req.RangeFirstItem + 1;
                if (selectionChanges == 1 || selectionChanges < Size / 100)
                {
                    // Sorted insertion is cheaper for short ranges.
                    for (int idx = (int)req.RangeFirstItem; idx <= (int)req.RangeLastItem; idx++)
                        SetItemSelected(GetStorageIdFromIndex(idx), req.Selected);
                }
                else
                {
                    // Append insertion plus a single sort is cheaper for wide ranges. The selection order
                    // walks in the request direction so a backward range keeps its order.
                    int sizeBeforeAmends = _Storage.Data.Count;
                    int selectionOrder = _SelectionOrder + ((req.RangeDirection < 0) ? selectionChanges - 1 : 0);
                    for (int idx = (int)req.RangeFirstItem; idx <= (int)req.RangeLastItem; idx++, selectionOrder += req.RangeDirection)
                        BatchSetItemSelected(this, GetStorageIdFromIndex(idx), req.Selected, sizeBeforeAmends, selectionOrder);
                    if (req.Selected)
                        _SelectionOrder += selectionChanges;
                    BatchFinish(this, req.Selected, sizeBeforeAmends);
                }
            }
        }
    }

    /// <summary>
    /// Inserts or clears one item against a known sorted prefix without re-sorting after every change.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8710-8723</c>)
    /// </summary>
    /// <param name="selection">The store being amended.</param>
    /// <param name="id">The persistent item identifier.</param>
    /// <param name="selected">True to select, false to clear.</param>
    /// <param name="sizeBeforeAmends">The count of already-sorted entries at the start of the batch.</param>
    /// <param name="selectionOrder">The selection-order value to store for a newly selected item.</param>
    /// <remarks>
    /// Newly selected identifiers are appended past <paramref name="sizeBeforeAmends"/> and only sorted
    /// by <see cref="BatchFinish"/> once the batch completes.
    /// </remarks>
    private static void BatchSetItemSelected(ImGuiSelectionBasicStorage selection, uint id, bool selected, int sizeBeforeAmends, int selectionOrder)
    {
        List<ImGuiStoragePair> data = selection._Storage.Data;
        int index = LowerBound(data, sizeBeforeAmends, id);
        bool isContained = index < sizeBeforeAmends && data[index].Key == id;
        if (selected == (isContained && data[index].ValueInt != 0))
            return;

        if (selected && !isContained)
            data.Add(new ImGuiStoragePair { Key = id, ValueInt = selectionOrder });
        else if (isContained)
            data[index].ValueInt = selected ? selectionOrder : 0;
        selection.Size += selected ? 1 : -1;
    }

    /// <summary>
    /// Restores the identifier ordering after a batch of selections.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8725-8730</c>)
    /// </summary>
    /// <param name="selection">The store whose entries were amended.</param>
    /// <param name="selected">Whether the batch selected items.</param>
    /// <param name="sizeBeforeAmends">The selected count at the start of the batch.</param>
    private static void BatchFinish(ImGuiSelectionBasicStorage selection, bool selected, int sizeBeforeAmends)
    {
        if (selected && selection.Size != sizeBeforeAmends)
            selection._Storage.BuildSortByKey();
    }

    /// <summary>
    /// Returns the first index in the sorted prefix whose key is not less than <paramref name="key"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2065-2080</c>)
    /// </summary>
    /// <param name="data">The pair buffer to search.</param>
    /// <param name="end">The exclusive end of the sorted prefix.</param>
    /// <param name="key">The identifier to locate.</param>
    /// <returns>The insertion index in the range <c>[0, end]</c>.</returns>
    private static int LowerBound(List<ImGuiStoragePair> data, int end, uint key)
    {
        int low = 0;
        int count = end;
        while (count > 0)
        {
            int half = count >> 1;
            int mid = low + half;
            if (data[mid].Key < key)
            {
                low = mid + 1;
                count -= half + 1;
            }
            else
            {
                count = half;
            }
        }
        return low;
    }
}

/// <summary>
/// A convenience adapter that applies multi-selection requests to caller-owned storage by invoking a
/// callback for each item (<c>ThirdParty/imgui/imgui.h:3302-3314</c>).
/// </summary>
/// <remarks>
/// The helper makes no assumption about the underlying storage; the caller decides how an index maps
/// to an item and writes the selection state in <see cref="AdapterSetItemSelected"/>. It is
/// caller-owned and plain single-threaded state with no internal synchronization.
/// </remarks>
public sealed class ImGuiSelectionExternalStorage
{
    /// <summary>
    /// Gets or sets caller data passed back to <see cref="AdapterSetItemSelected"/>.
    /// (<c>ThirdParty/imgui/imgui.h:3306</c>)
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// Gets or sets the callback that applies a selection state to one item.
    /// (<c>ThirdParty/imgui/imgui.h:3307</c>)
    /// </summary>
    public Action<ImGuiSelectionExternalStorage, int, bool>? AdapterSetItemSelected { get; set; }

    /// <summary>
    /// Initializes an external storage helper with no adapter.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8804-8808</c>)
    /// </summary>
    public ImGuiSelectionExternalStorage()
    {
        UserData = null;
        AdapterSetItemSelected = null;
    }

    /// <summary>
    /// Applies every request through <see cref="AdapterSetItemSelected"/>.
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8810-8829</c>)
    /// </summary>
    /// <param name="msIo">The IO instance whose requests are applied.</param>
    /// <remarks>
    /// The item count passed to <c>BeginMultiSelect()</c> bounds a select-all request. A set-range
    /// request iterates its inclusive endpoints.
    /// </remarks>
    public void ApplyRequests(ImGuiMultiSelectIO msIo)
    {
        ImAssert.Check(AdapterSetItemSelected != null);
        for (int requestN = 0; requestN < msIo.Requests.Count; requestN++)
        {
            ImGuiSelectionRequest req = msIo.Requests[requestN];
            if (req.Type == ImGuiSelectionRequestType.SetAll)
            {
                for (int idx = 0; idx < msIo.ItemsCount; idx++)
                    AdapterSetItemSelected!(this, idx, req.Selected);
            }
            else if (req.Type == ImGuiSelectionRequestType.SetRange)
            {
                for (int idx = (int)req.RangeFirstItem; idx <= (int)req.RangeLastItem; idx++)
                    AdapterSetItemSelected!(this, idx, req.Selected);
            }
        }
    }
}

#endregion

#region Internal multi-select state

/// <summary>
/// The single active box-selection state, covering both the persistent drag data and the transient
/// clip-unlock geometry (<c>ThirdParty/imgui/imgui_internal.h:1933-1962</c>).
/// </summary>
/// <remarks>
/// <para>
/// Only one box-selection can be active at a time, so the context owns exactly one instance. The
/// relative start and end positions are stored in window-content space so the rectangle survives
/// scrolling; the absolute previous and current rectangles are derived every frame from those
/// positions and the live mouse position.
/// </para>
/// <para>
/// <see cref="UnclipMode"/> and <see cref="UnclipRect"/> are written by <c>BeginBoxSelect()</c> and
/// consumed by the multi-select-capable widgets while the drag is active. The record is plain
/// single-threaded mutable state owned by the context.
/// </para>
/// </remarks>
public sealed class ImGuiBoxSelectState
{
    /// <summary>
    /// Gets or sets the identifier shared by the box-selection and the active identifier while the
    /// drag is active. (<c>ThirdParty/imgui/imgui_internal.h:1936</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the drag rectangle is currently active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1937</c>)
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an initial click is a candidate for starting a
    /// box-selection. (<c>ThirdParty/imgui/imgui_internal.h:1938</c>)
    /// </summary>
    public bool IsStarting { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the starting click landed on empty space rather than an
    /// item. (<c>ThirdParty/imgui/imgui_internal.h:1939</c>)
    /// </summary>
    public bool IsStartedFromVoid { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the first overlapped item should set the navigation
    /// identifier once. (<c>ThirdParty/imgui/imgui_internal.h:1940</c>)
    /// </summary>
    public bool IsStartedSetNavIdOnce { get; set; }

    /// <summary>
    /// Gets or sets a request to clear the selection, raised when a drag starts from empty space
    /// without Ctrl or Shift. (<c>ThirdParty/imgui/imgui_internal.h:1941</c>)
    /// </summary>
    public bool RequestClear { get; set; }

    /// <summary>
    /// Gets or sets the key modifiers latched when the box-selection started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1942</c>)
    /// </summary>
    public ImGuiKey KeyMods { get; set; }

    /// <summary>
    /// Gets or sets the start position in window-content relative space.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1943</c>)
    /// </summary>
    public ImVec2 StartPosRel { get; set; }

    /// <summary>
    /// Gets or sets the previous end position in window-content relative space, clamped to the
    /// scrolling view. (<c>ThirdParty/imgui/imgui_internal.h:1944</c>)
    /// </summary>
    public ImVec2 EndPosRel { get; set; }

    /// <summary>
    /// Gets or sets the sub-pixel scrolling accumulator used to keep scrolling stable at high frame
    /// rates. (<c>ThirdParty/imgui/imgui_internal.h:1945</c>)
    /// </summary>
    public ImVec2 ScrollAccum { get; set; }

    /// <summary>
    /// Gets or sets the window containing the box-selection, or <c>null</c> while inactive.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1946</c>)
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether widgets should ignore clipping for the difference
    /// rectangle. This transient field is written by <c>BeginBoxSelect()</c> and cleared by
    /// <c>EndBoxSelect()</c>. (<c>ThirdParty/imgui/imgui_internal.h:1949</c>)
    /// </summary>
    public bool UnclipMode { get; set; }

    /// <summary>
    /// Gets or sets the merged rectangle where item clipping may be temporarily disabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1950</c>)
    /// </summary>
    public ImRect UnclipRect { get; set; }

    /// <summary>
    /// Gets the per-axis versions of <see cref="UnclipRect"/>: index zero covers the horizontal
    /// change, index one the vertical change. (<c>ThirdParty/imgui/imgui_internal.h:1951</c>)
    /// </summary>
    public ImRect[] UnclipRects { get; } = new ImRect[2];

    /// <summary>
    /// Gets or sets the selection rectangle of the previous frame in absolute coordinates.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1952</c>)
    /// </summary>
    public ImRect BoxSelectRectPrev { get; set; }

    /// <summary>
    /// Gets or sets the selection rectangle of the current frame in absolute coordinates.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1953</c>)
    /// </summary>
    public ImRect BoxSelectRectCurr { get; set; }
}

/// <summary>
/// Persistent per-scope multi-selection state that survives across frames while a selection is alive
/// (<c>ThirdParty/imgui/imgui_internal.h:2006-2018</c>).
/// </summary>
/// <remarks>
/// Instances live in the context's <c>MultiSelectStorage</c> pool keyed by the selection scope
/// identifier. The range and navigation source items are remembered so Shift-selection and focus
/// restoration can be resolved on later frames.
/// </remarks>
public sealed class ImGuiMultiSelectState
{
    /// <summary>
    /// Gets or sets the window that owns the selection scope.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2008</c>)
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the selection scope identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2009</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which the scope was submitted, used for garbage collection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2010</c>)
    /// </summary>
    public int LastFrameActive { get; set; }

    /// <summary>
    /// Gets or sets the last known selection size supplied by the caller, or <c>-1</c> when unknown.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2011</c>)
    /// </summary>
    public int LastSelectionSize { get; set; }

    /// <summary>
    /// Gets or sets the selection state of the range source item: <c>-1</c> unknown, <c>0</c> unselected,
    /// <c>1</c> selected. (<c>ThirdParty/imgui/imgui_internal.h:2012</c>)
    /// </summary>
    public sbyte RangeSelected { get; set; }

    /// <summary>
    /// Gets or sets the selection state of the navigation item: <c>-1</c> unknown, <c>0</c> unselected,
    /// <c>1</c> selected. (<c>ThirdParty/imgui/imgui_internal.h:2013</c>)
    /// </summary>
    public sbyte NavIdSelected { get; set; }

    /// <summary>
    /// Gets or sets the selection user data that anchors a range selection, or <c>-1</c> when unset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2014</c>)
    /// </summary>
    public long RangeSrcItem { get; set; } = -1;

    /// <summary>
    /// Gets or sets the selection user data of the last focused item, or <c>-1</c> when unset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2015</c>)
    /// </summary>
    public long NavIdItem { get; set; } = -1;

    /// <summary>
    /// Initializes the record to the canonical empty state
    /// (<c>ThirdParty/imgui/imgui_internal.h:2017</c>).
    /// </summary>
    public ImGuiMultiSelectState()
    {
        Window = null;
        ID = 0;
        LastFrameActive = 0;
        LastSelectionSize = 0;
        RangeSelected = -1;
        NavIdSelected = -1;
        RangeSrcItem = -1;
        NavIdItem = -1;
    }
}

/// <summary>
/// Temporary multi-selection data for one active scope, alive between a matching
/// <c>BeginMultiSelect()</c> and <c>EndMultiSelect()</c>
/// (<c>ThirdParty/imgui/imgui_internal.h:1966-2003</c>).
/// </summary>
/// <remarks>
/// <para>
/// Instances are stacked on the context and reused across frames, so <see cref="Clear"/> clears every
/// field except the request buffer's capacity. <see cref="IO"/> is kept as the first field, mirroring
/// the canonical static assertion that <c>Clear()</c> relies on.
/// </para>
/// <para>
/// The record is plain single-threaded mutable state owned by the context.
/// </para>
/// </remarks>
public sealed class ImGuiMultiSelectTempData
{
    /// <summary>
    /// Gets the IO structure returned to the application and filled with selection requests.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1968</c>)
    /// </summary>
    public ImGuiMultiSelectIO IO { get; } = new();

    /// <summary>
    /// Gets or sets the persistent storage bound to this scope.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1969</c>)
    /// </summary>
    public ImGuiMultiSelectState? Storage { get; set; }

    /// <summary>
    /// Gets or sets the focus scope identifier captured when the scope opened.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1970</c>)
    /// </summary>
    public uint FocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the effective multi-select flags after canonical normalization.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1971</c>)
    /// </summary>
    public ImGuiMultiSelectFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the cursor position at the start of the scope, used to measure a
    /// <c>ScopeRect</c>. (<c>ThirdParty/imgui/imgui_internal.h:1972</c>)
    /// </summary>
    public ImVec2 ScopeRectMin { get; set; }

    /// <summary>
    /// Gets or sets the window cursor maximum saved when the scope opened and restored when it closes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1973</c>)
    /// </summary>
    public ImVec2 BackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the box-selection following this scope, or zero when box-select
    /// is disabled. (<c>ThirdParty/imgui/imgui_internal.h:1976</c>)
    /// </summary>
    public uint BoxSelectId { get; set; }

    /// <summary>
    /// Gets or sets the key modifiers latched when the scope opened, so a keyboard range selection does
    /// not require holding the modifiers for an extra frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1977</c>)
    /// </summary>
    public ImGuiKey KeyMods { get; set; }

    /// <summary>
    /// Gets or sets the pending select-all operation: <c>-1</c> none, <c>0</c> clear all, <c>1</c>
    /// select all. (<c>ThirdParty/imgui/imgui_internal.h:1978</c>)
    /// </summary>
    public sbyte LoopRequestSetAll { get; set; } = -1;

    /// <summary>
    /// Gets or sets a value indicating that the request list has switched from the begin-phase to the
    /// end-phase of the scope. (<c>ThirdParty/imgui/imgui_internal.h:1979</c>)
    /// </summary>
    public bool IsEndIO { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the selection scope is focused, which enables keyboard
    /// range selection. (<c>ThirdParty/imgui/imgui_internal.h:1980</c>)
    /// </summary>
    public bool IsFocused { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the scope is performing a keyboard-driven range selection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1981</c>)
    /// </summary>
    public bool IsKeyboardSetRange { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the navigation item was submitted this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1982</c>)
    /// </summary>
    public bool NavIdPassedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the stored range source item was submitted this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1983</c>)
    /// </summary>
    public bool RangeSrcPassedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the range destination item was submitted this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1984</c>)
    /// </summary>
    public bool RangeDstPassedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the last known selection size is one or unknown, which
    /// allows a narrower drag-and-drop behavior. (<c>ThirdParty/imgui/imgui_internal.h:1985</c>)
    /// </summary>
    public bool IsSoleOrUnknownSelectionSize { get; set; }

    /// <summary>
    /// Initializes the record to its cleared state
    /// (<c>ThirdParty/imgui/imgui_internal.h:1987</c>).
    /// </summary>
    public ImGuiMultiSelectTempData()
    {
        Clear();
    }

    /// <summary>
    /// Clears every field while preserving the capacity of <see cref="ImGuiMultiSelectIO.Requests"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1988</c>)
    /// </summary>
    public void Clear()
    {
        ClearIO();
        Storage = null;
        FocusScopeId = 0;
        Flags = ImGuiMultiSelectFlags.None;
        ScopeRectMin = default;
        BackupCursorMaxPos = default;
        BoxSelectId = 0;
        KeyMods = ImGuiKey.None;
        LoopRequestSetAll = 0;
        IsEndIO = false;
        IsFocused = false;
        IsKeyboardSetRange = false;
        NavIdPassedBy = false;
        RangeSrcPassedBy = false;
        RangeDstPassedBy = false;
        IsSoleOrUnknownSelectionSize = false;
    }

    /// <summary>
    /// Clears only the IO fields, preserving the request buffer allocation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1989</c>)
    /// </summary>
    public void ClearIO()
    {
        IO.Requests.Clear();
        IO.RangeSrcItem = -1;
        IO.NavIdItem = -1;
        IO.NavIdSelected = false;
        IO.RangeSrcReset = false;
    }
}

#endregion

public static partial class ImGui
{
    #region [SECTION] Widgets: Box-Select support

    /// <summary>
    /// Returns the active box-selection state when <paramref name="id"/> identifies it, or <c>null</c>
    /// otherwise (<c>ThirdParty/imgui/imgui_internal.h:3978</c>).
    /// </summary>
    /// <param name="id">The box-selection identifier.</param>
    /// <returns>The active state record, or <c>null</c> when it is not the active box-selection.</returns>
    internal static ImGuiBoxSelectState? GetBoxSelectState(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        return (id != 0 && g.BoxSelectState.ID == id && g.BoxSelectState.IsActive) ? g.BoxSelectState : null;
    }

    /// <summary>
    /// Records the initial click of a possible box-selection and latches the geometry and modifier keys
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7813-7822</c>).
    /// </summary>
    /// <param name="id">The box-selection identifier.</param>
    /// <param name="clickedItem">
    /// The selection user data of the item under the click, or <c>-1</c> when the click landed on empty
    /// space.
    /// </param>
    private static void BoxSelectPreStartDrag(uint id, long clickedItem)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiBoxSelectState bs = g.BoxSelectState;
        bs.ID = id;
        bs.IsStarting = true;
        bs.IsStartedFromVoid = clickedItem == -1;
        bs.IsStartedSetNavIdOnce = bs.IsStartedFromVoid;
        bs.KeyMods = g.IO.KeyMods;
        bs.StartPosRel = bs.EndPosRel = WindowPosAbsToRel(g.CurrentWindow!, g.IO.MousePos);
        bs.ScrollAccum = new ImVec2(0.0f, 0.0f);
    }

    /// <summary>
    /// Promotes a starting click to a live box-selection drag and takes the active identifier
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7824-7835</c>).
    /// </summary>
    /// <param name="bs">The box-selection state to activate.</param>
    /// <param name="window">The window that owns the drag.</param>
    private static void BoxSelectActivateDrag(ImGuiBoxSelectState bs, ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!;
        bs.IsActive = true;
        bs.Window = window;
        bs.IsStarting = false;
        SetActiveID(bs.ID, window);
        SetActiveIdUsingAllKeyboardKeys();
        if (bs.IsStartedFromVoid && (bs.KeyMods & (ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift)) == 0)
            bs.RequestClear = true;
    }

    /// <summary>
    /// Deactivates an active box-selection drag and releases the active identifier
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7837-7847</c>).
    /// </summary>
    /// <param name="bs">The box-selection state to deactivate.</param>
    private static void BoxSelectDeactivateDrag(ImGuiBoxSelectState bs)
    {
        ImGuiContext g = GetCurrentContext()!;
        bs.IsActive = bs.IsStarting = false;
        if (g.ActiveId == bs.ID)
            ClearActiveID();
        bs.ID = 0;
    }

    /// <summary>
    /// Scrolls a window while the box-selection drag approaches an edge of the inner rectangle
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7849-7875</c>).
    /// </summary>
    /// <param name="bs">The active box-selection state.</param>
    /// <param name="window">The window to scroll; it must be the box-selection window.</param>
    /// <param name="innerRect">The rectangle inside which the mouse does not scroll.</param>
    private static void BoxSelectScrollWithMouseDrag(ImGuiBoxSelectState bs, ImGuiWindow window, ImRect innerRect)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(bs.Window == window);

        for (int axis = 0; axis < 2; axis++)
        {
            float mousePos = axis == 0 ? g.IO.MousePos.X : g.IO.MousePos.Y;
            float innerMin = axis == 0 ? innerRect.Min.X : innerRect.Min.Y;
            float innerMax = axis == 0 ? innerRect.Max.X : innerRect.Max.Y;
            float distance = (mousePos > innerMax) ? mousePos - innerMax : (mousePos < innerMin) ? mousePos - innerMin : 0.0f;
            float scrollCurr = axis == 0 ? window.Scroll.X : window.Scroll.Y;
            float scrollMax = axis == 0 ? window.ScrollMax.X : window.ScrollMax.Y;
            if (distance == 0.0f
                || (distance < 0.0f && scrollCurr < 0.0f)
                || (distance > 0.0f && scrollCurr >= scrollMax))
                continue;

            // The scroll speed scales from x1 to x4 with the distance past the edge, remapped from the
            // canonical ImLinearRemapClamp(font_size, font_size * 5, 1, 4, abs(distance)).
            float speedMultiplier = 1.0f;
            float remapMin = g.FontSize;
            float remapMax = g.FontSize * 5.0f;
            if (remapMax != remapMin)
            {
                float r = (MathF.Abs(distance) - remapMin) / (remapMax - remapMin);
                r = Math.Clamp(r, 0.0f, 1.0f);
                speedMultiplier = 1.0f + 3.0f * r;
            }

            float scrollStep = g.FontSize * 35.0f * speedMultiplier * MathF.Sign(distance) * g.IO.DeltaTime;
            float accum = (axis == 0 ? bs.ScrollAccum.X : bs.ScrollAccum.Y) + scrollStep;

            // Accumulate whole pixels so high frame rates still produce a discrete scroll step.
            float scrollStepInt = MathF.Floor(accum);
            if (scrollStepInt == 0.0f)
            {
                if (axis == 0)
                    bs.ScrollAccum = new ImVec2(accum, bs.ScrollAccum.Y);
                else
                    bs.ScrollAccum = new ImVec2(bs.ScrollAccum.X, accum);
                continue;
            }

            if (axis == 0)
            {
                SetScrollX(window, scrollCurr + scrollStepInt);
                bs.ScrollAccum = new ImVec2(accum - scrollStepInt, bs.ScrollAccum.Y);
            }
            else
            {
                SetScrollY(window, scrollCurr + scrollStepInt);
                bs.ScrollAccum = new ImVec2(bs.ScrollAccum.X, accum - scrollStepInt);
            }
        }
    }

    /// <summary>
    /// Starts or updates a box-selection drag, computing the previous and current selection rectangles
    /// and the clip-unlock geometry (<c>ThirdParty/imgui/imgui_widgets.cpp:7877-7970</c>).
    /// </summary>
    /// <param name="scopeRect">The rectangle that bounds the selection scope.</param>
    /// <param name="window">The window that owns the scope.</param>
    /// <param name="boxSelectId">The identifier that ties the scope to its box-selection.</param>
    /// <param name="msFlags">The effective multi-select flags.</param>
    /// <returns>True while a box-selection drag is active.</returns>
    /// <remarks>
    /// A starting click is promoted to an active drag once the mouse has moved past the drag threshold.
    /// Releasing the left button deactivates the drag. While active, the rectangles are recomputed every
    /// frame, and when the changed area reaches outside the window clip rectangle the merged
    /// <see cref="ImGuiBoxSelectState.UnclipRect"/> lets widgets evaluate items that ordinary clipping
    /// would skip.
    /// </remarks>
    internal static bool BeginBoxSelect(ImRect scopeRect, ImGuiWindow window, uint boxSelectId, ImGuiMultiSelectFlags msFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiBoxSelectState bs = g.BoxSelectState;
        KeepAliveID(boxSelectId);
        if (bs.ID != boxSelectId)
            return false;

        // IsStarting is set by MultiSelectItemFooter() when considering a possible box-select. Validate
        // it here and lock the geometry.
        bs.UnclipMode = false;
        bs.RequestClear = false;
        if (bs.IsStarting && IsMouseDragPastThreshold(ImGuiMouseButton.Left))
            BoxSelectActivateDrag(bs, window);
        else if ((bs.IsStarting || bs.IsActive) && !g.IO.MouseDown[0])
            BoxSelectDeactivateDrag(bs);
        if (!bs.IsActive)
            return false;

        // The previous rectangle is reprojected for the current frame because the relative positions are
        // stored in scrolling space.
        ImVec2 startPosAbs = WindowPosRelToAbs(window, bs.StartPosRel);
        ImVec2 prevEndPosAbs = WindowPosRelToAbs(window, bs.EndPosRel);
        ImVec2 currEndPosAbs = g.IO.MousePos;
        if ((msFlags & ImGuiMultiSelectFlags.ScopeWindow) != 0)
            currEndPosAbs = new ImVec2(
                Math.Clamp(currEndPosAbs.X, scopeRect.Min.X, scopeRect.Max.X),
                Math.Clamp(currEndPosAbs.Y, scopeRect.Min.Y, scopeRect.Max.Y));
        bs.BoxSelectRectPrev = new ImRect(
            new ImVec2(MathF.Min(startPosAbs.X, prevEndPosAbs.X), MathF.Min(startPosAbs.Y, prevEndPosAbs.Y)),
            new ImVec2(MathF.Max(startPosAbs.X, prevEndPosAbs.X), MathF.Max(startPosAbs.Y, prevEndPosAbs.Y)));
        bs.BoxSelectRectCurr = new ImRect(
            new ImVec2(MathF.Min(startPosAbs.X, currEndPosAbs.X), MathF.Min(startPosAbs.Y, currEndPosAbs.Y)),
            new ImVec2(MathF.Max(startPosAbs.X, currEndPosAbs.X), MathF.Max(startPosAbs.Y, currEndPosAbs.Y)));

        // Compute the area that differs between the previous and current rectangles. Both per-axis
        // rectangles are always updated while a drag is active so widgets can rely on them.
        if ((msFlags & (ImGuiMultiSelectFlags.BoxSelect1d | ImGuiMultiSelectFlags.BoxSelect2d)) != 0)
        {
            ImRect inverted = new(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);
            bs.UnclipRects[0] = inverted;
            bs.UnclipRects[1] = inverted;
            for (int side = 0; side < 2; side++)
            {
                ImVec2 dMin = side == 0
                    ? new ImVec2(MathF.Min(bs.BoxSelectRectCurr.Min.X, bs.BoxSelectRectPrev.Min.X), MathF.Min(bs.BoxSelectRectCurr.Min.Y, bs.BoxSelectRectPrev.Min.Y))
                    : new ImVec2(MathF.Min(bs.BoxSelectRectCurr.Max.X, bs.BoxSelectRectPrev.Max.X), MathF.Min(bs.BoxSelectRectCurr.Max.Y, bs.BoxSelectRectPrev.Max.Y));
                ImVec2 dMax = side == 0
                    ? new ImVec2(MathF.Max(bs.BoxSelectRectCurr.Min.X, bs.BoxSelectRectPrev.Min.X), MathF.Max(bs.BoxSelectRectCurr.Min.Y, bs.BoxSelectRectPrev.Min.Y))
                    : new ImVec2(MathF.Max(bs.BoxSelectRectCurr.Max.X, bs.BoxSelectRectPrev.Max.X), MathF.Max(bs.BoxSelectRectCurr.Max.Y, bs.BoxSelectRectPrev.Max.Y));
                if (dMin.X != dMax.X)
                {
                    bs.UnclipRects[0].AddX(dMin.X);
                    bs.UnclipRects[0].AddX(dMax.X);
                }
                if (dMin.Y != dMax.Y)
                {
                    bs.UnclipRects[1].AddY(dMin.Y);
                    bs.UnclipRects[1].AddY(dMax.Y);
                }
            }

            ImRect boxSelectIntersection = bs.BoxSelectRectPrev;
            boxSelectIntersection.Add(bs.BoxSelectRectCurr);
            if ((msFlags & ImGuiMultiSelectFlags.BoxSelect2d) != 0
                && (bs.BoxSelectRectPrev.Min.X != bs.BoxSelectRectCurr.Min.X || bs.BoxSelectRectPrev.Max.X != bs.BoxSelectRectCurr.Max.X))
            {
                bs.UnclipRects[0].AddY(boxSelectIntersection.Min.Y);
                bs.UnclipRects[0].AddY(boxSelectIntersection.Max.Y);
            }
            if ((msFlags & (ImGuiMultiSelectFlags.BoxSelect1d | ImGuiMultiSelectFlags.BoxSelect2d)) != 0
                && (bs.BoxSelectRectPrev.Min.Y != bs.BoxSelectRectCurr.Min.Y || bs.BoxSelectRectPrev.Max.Y != bs.BoxSelectRectCurr.Max.Y))
            {
                bs.UnclipRects[1].AddX(boxSelectIntersection.Min.X);
                bs.UnclipRects[1].AddX(boxSelectIntersection.Max.X);
            }

            // Merge both rectangles into one. The merged rectangle is built in a local because ImRect is
            // a value type and mutating a property-backed rectangle in place would only change a copy.
            ImRect mergedUnclipRect = bs.UnclipRects[0];
            mergedUnclipRect.Add(bs.UnclipRects[1]);
            bs.UnclipRect = mergedUnclipRect;
            if (!bs.UnclipRect.IsInverted() && (!window.ClipRect.Contains(bs.UnclipRect.Min) || !window.ClipRect.Contains(bs.UnclipRect.Max)))
                bs.UnclipMode = true;
            if (bs.UnclipMode && g.CurrentTable != null)
            {
                // The table helper receives the rectangle by reference, but the state rectangle is a
                // property, so it is copied in and written back.
                ImRect unclipRect = bs.UnclipRect;
                TableApplyExternalUnclipRect(g.CurrentTable, ref unclipRect);
                bs.UnclipRect = unclipRect;
            }
        }

        return true;
    }

    /// <summary>
    /// Ends an active box-selection frame by rendering the selection rectangle and, when scrolling is
    /// enabled, scrolling the window while the mouse sits near an edge
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:7972-8006</c>).
    /// </summary>
    /// <param name="scopeRect">The rectangle that bounds the selection scope.</param>
    /// <param name="msFlags">The effective multi-select flags.</param>
    internal static void EndBoxSelect(ImRect scopeRect, ImGuiMultiSelectFlags msFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiBoxSelectState bs = g.BoxSelectState;
        ImAssert.Check(bs.IsActive);
        bs.UnclipMode = false;

        // Render the selection rectangle on the front-most visible child, so it is not hidden by a
        // decorated child window.
        bs.EndPosRel = WindowPosAbsToRel(window, new ImVec2(
            Math.Clamp(g.IO.MousePos.X, scopeRect.Min.X, scopeRect.Max.X),
            Math.Clamp(g.IO.MousePos.Y, scopeRect.Min.Y, scopeRect.Max.Y)));
        ImRect boxSelectRect = bs.BoxSelectRectCurr;
        boxSelectRect.ClipWith(scopeRect);
        ImGuiWindow drawWindow = FindFrontMostVisibleChildWindow(window);
        drawWindow.DrawList.AddRectFilled(boxSelectRect.Min, boxSelectRect.Max, GetColorU32(ImGuiCol.SeparatorHovered, 0.30f));
        drawWindow.DrawList.AddRect(boxSelectRect.Min, boxSelectRect.Max, GetColorU32(ImGuiCol.NavCursor));

        bool enableScroll = (msFlags & ImGuiMultiSelectFlags.ScopeWindow) != 0
            && (msFlags & ImGuiMultiSelectFlags.BoxSelectNoScroll) == 0;
        if (enableScroll)
        {
            ImRect scrollRect = scopeRect;
            scrollRect.Expand(-g.FontSize);
            if (!scrollRect.Contains(g.IO.MousePos))
                BoxSelectScrollWithMouseDrag(bs, window, scrollRect);
        }
    }

    /// <summary>
    /// Returns the deepest visible child window of <paramref name="window"/>, or the window itself when
    /// it has no visible children (<c>ThirdParty/imgui/imgui.cpp:6979-6985</c>).
    /// </summary>
    /// <param name="window">The window to descend from.</param>
    /// <returns>The front-most visible child window.</returns>
    public static ImGuiWindow FindFrontMostVisibleChildWindow(ImGuiWindow window)
    {
        for (int n = window.DC.ChildWindows.Count - 1; n >= 0; n--)
        {
            ImGuiWindow child = window.DC.ChildWindows[n];
            if (IsWindowActiveAndVisible(child))
                return FindFrontMostVisibleChildWindow(child);
        }
        return window;
    }

    #endregion

    #region [SECTION] Widgets: Multi-Select support

    /// <summary>
    /// Returns the persistent selection storage bound to a selection scope identifier, or <c>null</c>
    /// when no scope has been opened for that identifier
    /// (<c>ThirdParty/imgui/imgui_internal.h:3979</c>).
    /// </summary>
    /// <param name="id">The multi-selection scope identifier.</param>
    /// <returns>The scope's selection storage, or <c>null</c> when the identifier is unknown.</returns>
    /// <remarks>
    /// The storage lives in the context's multi-select pool and survives between frames so that
    /// range and navigation state outlive a single scope. Canonical returns the pool entry without
    /// creating one, which is why an unknown identifier yields <c>null</c> rather than new storage.
    /// Like canonical, this lookup has no in-tree caller at this revision; it exists for internal-API
    /// parity so callers outside the selection implementation can inspect a scope's stored state.
    /// </remarks>
    internal static ImGuiMultiSelectState? GetMultiSelectState(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.MultiSelectStorage.GetByKey(id);
    }

    /// <summary>
    /// Writes the current selection requests to the debug log when the selection event channel is
    /// enabled (<c>ThirdParty/imgui/imgui_widgets.cpp:8012-8023</c>).
    /// </summary>
    /// <param name="function">Name of the caller, recorded with each request.</param>
    /// <param name="io">The IO instance whose requests are described.</param>
    /// <remarks>
    /// The debug log sink is owned by the metrics and debug chunk; this port keeps the canonical call
    /// structure and the request classification, but writes nothing until that sink exists.
    /// </remarks>
    private static void DebugLogMultiSelectRequests(string function, ImGuiMultiSelectIO io)
    {
        // The debug log sink is owned by the metrics and debug chunk; the canonical classification is
        // kept as a structural hook and produces no output until that sink exists.
        _ = function;
        _ = io;
    }

    /// <summary>
    /// Returns the rectangle that bounds a multi-select scope: either the measured scope rectangle or
    /// the window inner clip rectangle (<c>ThirdParty/imgui/imgui_widgets.cpp:8025-8046</c>).
    /// </summary>
    /// <param name="ms">The active temporary multi-select data.</param>
    /// <param name="window">The window that owns the scope.</param>
    /// <returns>The scope rectangle in absolute screen coordinates.</returns>
    private static ImRect CalcScopeRect(ImGuiMultiSelectTempData ms, ImGuiWindow window)
    {
        if ((ms.Flags & ImGuiMultiSelectFlags.ScopeRect) != 0)
        {
            // A ScopeRect is only valid in EndMultiSelect(), after CursorMaxPos has measured the scope.
            return new ImRect(ms.ScopeRectMin, new ImVec2(
                MathF.Max(window.DC.CursorMaxPos.X, ms.ScopeRectMin.X),
                MathF.Max(window.DC.CursorMaxPos.Y, ms.ScopeRectMin.Y)));
        }

        ImRect scopeRect = window.InnerClipRect;
        scopeRect.Min = new ImVec2(
            MathF.Min(scopeRect.Min.X + window.DecoInnerSizeX1, scopeRect.Max.X),
            MathF.Min(scopeRect.Min.Y + window.DecoInnerSizeY1, scopeRect.Max.Y));
        return scopeRect;
    }

    /// <summary>
    /// Opens a multi-selection scope and returns the begin-phase IO. The caller submits items with
    /// <c>SetNextItemSelectionUserData()</c> between this call and <c>EndMultiSelect()</c>
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8053-8181</c>).
    /// </summary>
    /// <param name="flags">The multi-select flags.</param>
    /// <param name="selectionSize">
    /// The current selection size, or <c>-1</c> when unknown. Used to skip clearing shortcuts on an empty
    /// selection.
    /// </param>
    /// <param name="itemsCount">
    /// The item count copied into the IO for the application's request handler, or <c>-1</c> when unknown.
    /// </param>
    /// <returns>The IO instance owned by the scope; it is valid until the next multi-select call.</returns>
    /// <remarks>
    /// The scope identifier is the current window's top identifier-stack value. Focus scope and storage
    /// are bound here so the item header and footer can resolve range and navigation state. Box-selection
    /// is advanced here, before items are submitted, so the unclip rectangles are available to them.
    /// </remarks>
    public static ImGuiMultiSelectIO BeginMultiSelect(ImGuiMultiSelectFlags flags, int selectionSize = -1, int itemsCount = -1)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        g.MultiSelectTempDataStacked++;
        if (g.MultiSelectTempDataStacked > g.MultiSelectTempData.Count)
            g.MultiSelectTempData.Add(new ImGuiMultiSelectTempData());
        ImGuiMultiSelectTempData ms = g.MultiSelectTempData[g.MultiSelectTempDataStacked - 1];
        g.CurrentMultiSelect = ms;

        if ((flags & (ImGuiMultiSelectFlags.ScopeWindow | ImGuiMultiSelectFlags.ScopeRect)) == 0)
            flags |= ImGuiMultiSelectFlags.ScopeWindow;
        if ((flags & ImGuiMultiSelectFlags.SingleSelect) != 0)
            flags &= ~(ImGuiMultiSelectFlags.BoxSelect2d | ImGuiMultiSelectFlags.BoxSelect1d);
        if ((flags & ImGuiMultiSelectFlags.BoxSelect2d) != 0)
            flags &= ~ImGuiMultiSelectFlags.BoxSelect1d;

        // Work around the fact that this scope overrides CursorMaxPos, which would lose size measurement
        // for a table cell still being measured.
        if (g.CurrentTable != null)
        {
            ImGuiTable table = g.CurrentTable;
            if (!table.IsLayoutLocked)
                TableUpdateLayout(table);
            else if (table.CurrentColumn != -1)
                TableEndCell(table);
        }

        uint id = window.IDStack[^1];
        ms.Clear();
        ms.FocusScopeId = id;
        ms.Flags = flags;
        ms.BackupCursorMaxPos = window.DC.CursorMaxPos;
        ms.ScopeRectMin = window.DC.CursorPos;
        if ((flags & ImGuiMultiSelectFlags.ScopeRect) != 0)
            window.DC.CursorMaxPos = ms.ScopeRectMin;
        PushFocusScope(ms.FocusScopeId);
        ms.IsFocused = IsInNavFocusRoute(g.CurrentFocusScopeId);
        if ((flags & ImGuiMultiSelectFlags.ScopeWindow) != 0)
            window.DC.NavLayersActiveMask = (short)((ushort)window.DC.NavLayersActiveMask | (1 << (int)ImGuiNavLayer.Main));

        // Latch the keyboard modifiers observed with the navigation move, so a range selection does not
        // require holding them for an extra frame.
        ms.KeyMods = g.NavJustMovedToId != 0
            ? (g.NavJustMovedToIsTabbing ? ImGuiKey.None : g.NavJustMovedToKeyMods)
            : g.IO.KeyMods;
        if ((flags & ImGuiMultiSelectFlags.NoRangeSelect) != 0)
            ms.KeyMods &= ~ImGuiKey.ImGuiMod_Shift;

        ImGuiMultiSelectState storage = g.MultiSelectStorage.GetOrAddByKey(id);
        storage.ID = id;
        storage.LastFrameActive = g.FrameCount;
        storage.LastSelectionSize = selectionSize;
        storage.Window = window;
        ms.Storage = storage;

        ms.IO.Requests.Clear();
        ms.IO.RangeSrcItem = storage.RangeSrcItem;
        ms.IO.NavIdItem = storage.NavIdItem;
        ms.IO.NavIdSelected = storage.NavIdSelected == 1;
        ms.IO.ItemsCount = itemsCount;

        // Clear when navigation moves within the scope, compared by focus scope so several selections can
        // share one window.
        bool requestClear = false;
        bool requestSelectAll = false;
        if (g.NavJustMovedToId != 0 && g.NavJustMovedToFocusScopeId == ms.FocusScopeId && g.NavJustMovedToHasSelectionData)
        {
            if ((ms.KeyMods & ImGuiKey.ImGuiMod_Shift) != 0)
                ms.IsKeyboardSetRange = true;
            if (ms.IsKeyboardSetRange)
                ImAssert.Check(storage.RangeSrcItem != -1, "Range selection requires a range source item.");
            if ((ms.KeyMods & (ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift)) == 0
                && (flags & (ImGuiMultiSelectFlags.NoAutoClear | ImGuiMultiSelectFlags.NoAutoSelect)) == 0)
                requestClear = true;
        }
        else if (g.NavJustMovedFromFocusScopeId == ms.FocusScopeId)
        {
            if ((ms.KeyMods & (ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift)) == 0
                && (flags & (ImGuiMultiSelectFlags.NoAutoClear | ImGuiMultiSelectFlags.NoAutoSelect)) == 0)
                requestClear = true;
        }

        // Advance an active box-selection so the unclip rectangles are ready for the items below.
        ImGuiBoxSelectState bs = g.BoxSelectState;
        if ((flags & (ImGuiMultiSelectFlags.BoxSelect1d | ImGuiMultiSelectFlags.BoxSelect2d)) != 0)
        {
            ms.BoxSelectId = GetID("##BoxSelect");
            if (BeginBoxSelect(CalcScopeRect(ms, window), window, ms.BoxSelectId, flags))
                requestClear |= bs.RequestClear;
        }

        if (ms.IsFocused)
        {
            // Clear selection on Escape. The shortcut is only claimed when the selection is not empty, so a
            // later press can leave the child window; an active box-selection passes its identifier to
            // bypass the all-keyboard-keys lock.
            if ((flags & ImGuiMultiSelectFlags.ClearOnEscape) != 0)
            {
                if (selectionSize != 0 || bs.IsActive)
                {
                    if (Shortcut(ImGuiKey.Escape, ImGuiInputFlags.None, bs.IsActive ? bs.ID : 0))
                    {
                        requestClear = true;
                        if (bs.IsActive)
                            BoxSelectDeactivateDrag(bs);
                    }
                }
            }

            // Select all on Ctrl+A.
            if ((flags & ImGuiMultiSelectFlags.SingleSelect) == 0 && (flags & ImGuiMultiSelectFlags.NoSelectAll) == 0)
            {
                if (Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.A))
                    requestSelectAll = true;
            }
        }

        if (requestClear || requestSelectAll)
        {
            MultiSelectAddSetAll(ms, requestSelectAll);
            if (!requestSelectAll)
                storage.LastSelectionSize = 0;
        }
        ms.LoopRequestSetAll = requestSelectAll ? (sbyte)1 : requestClear ? (sbyte)0 : (sbyte)-1;
        ms.IsSoleOrUnknownSelectionSize = storage.LastSelectionSize == 1 || storage.LastSelectionSize == -1;

        if ((g.DebugLogFlags & ImGuiDebugLogFlags.EventSelection) != 0)
            DebugLogMultiSelectRequests("BeginMultiSelect", ms.IO);

        return ms.IO;
    }

    /// <summary>
    /// Closes the current multi-selection scope and returns the end-phase IO, containing the range
    /// requests accumulated while items were submitted
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8183-8280</c>).
    /// </summary>
    /// <returns>The IO instance owned by the scope; it is valid until the next multi-select call.</returns>
    /// <remarks>
    /// The unwinding order mirrors canonical: reset range and navigation sources that were not submitted,
    /// finish the box-selection, unwind a table row opened by the scope, restore the saved cursor maximum,
    /// pop the focus scope, and restore the parent temporary data.
    /// </remarks>
    public static ImGuiMultiSelectIO EndMultiSelect()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiMultiSelectTempData ms = g.CurrentMultiSelect!;
        ImGuiMultiSelectState storage = ms.Storage!;
        ImGuiWindow window = g.CurrentWindow!;
        ImAssert.Check(ms.FocusScopeId == g.CurrentFocusScopeId, "EndMultiSelect() FocusScope mismatch!");
        ImAssert.Check(storage.Window == g.CurrentWindow);
        ImAssert.Check(g.MultiSelectTempDataStacked > 0 && ReferenceEquals(g.MultiSelectTempData[g.MultiSelectTempDataStacked - 1], g.CurrentMultiSelect));

        ImRect scopeRect = CalcScopeRect(ms, window);
        if (ms.IsFocused)
        {
            // The range source is reset when the application requested it or when the item was not
            // submitted. Reading the storage would give the end-of-scope state, which is why the IO copy is
            // tested instead.
            if (ms.IO.RangeSrcReset || (!ms.RangeSrcPassedBy && ms.IO.RangeSrcItem != -1))
                storage.RangeSrcItem = -1;
            if (!ms.NavIdPassedBy && storage.NavIdItem != -1)
            {
                storage.NavIdItem = -1;
                storage.NavIdSelected = -1;
            }

            if ((ms.Flags & (ImGuiMultiSelectFlags.BoxSelect1d | ImGuiMultiSelectFlags.BoxSelect2d)) != 0
                && GetBoxSelectState(ms.BoxSelectId) != null)
            {
                EndBoxSelect(scopeRect, ms.Flags);
            }
        }

        if (!ms.IsEndIO)
            ms.IO.Requests.Clear();

        // Clear the selection when the scope is clicked on empty space. The drag-threshold test allows
        // box-selection to start instead of clearing.
        bool scopeHovered = window.InnerRect.Contains(g.IO.MousePos) && IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        if (scopeHovered && (ms.Flags & ImGuiMultiSelectFlags.ScopeRect) != 0)
            scopeHovered &= scopeRect.Contains(g.IO.MousePos);
        if (scopeHovered && g.HoveredId == 0 && g.ActiveId == 0)
        {
            if ((ms.Flags & (ImGuiMultiSelectFlags.BoxSelect1d | ImGuiMultiSelectFlags.BoxSelect2d)) != 0)
            {
                if (!g.BoxSelectState.IsActive && !g.BoxSelectState.IsStarting && g.IO.MouseClickedCount[0] == 1)
                {
                    BoxSelectPreStartDrag(ms.BoxSelectId, -1);
                    FocusWindow(window, ImGuiFocusRequestFlags.UnlessBelowModal);
                    SetHoveredID(ms.BoxSelectId);
                    if ((ms.Flags & ImGuiMultiSelectFlags.ScopeRect) != 0)
                        SetNavID(0, ImGuiNavLayer.Main, ms.FocusScopeId, new ImRect(g.IO.MousePos, g.IO.MousePos));
                }
            }

            if ((ms.Flags & ImGuiMultiSelectFlags.ClearOnClickVoid) != 0)
            {
                if (IsMouseReleased(ImGuiMouseButton.Left) && !IsMouseDragPastThreshold(ImGuiMouseButton.Left) && g.IO.KeyMods == ImGuiKey.None)
                    MultiSelectAddSetAll(ms, false);
            }
        }

        if ((ms.Flags & ImGuiMultiSelectFlags.NavWrapX) != 0)
        {
            ImAssert.Check((ms.Flags & ImGuiMultiSelectFlags.ScopeWindow) != 0); // Only supported at window scope.
            NavMoveRequestTryWrapping(GetCurrentWindow(), ImGuiNavMoveFlags.WrapX);
        }

        // Unwind.
        if (g.CurrentTable != null && g.CurrentTable.IsInsideRow)
            TableEndRow(g.CurrentTable);
        window.DC.CursorMaxPos = new ImVec2(
            MathF.Max(ms.BackupCursorMaxPos.X, window.DC.CursorMaxPos.X),
            MathF.Max(ms.BackupCursorMaxPos.Y, window.DC.CursorMaxPos.Y));
        PopFocusScope();

        if ((g.DebugLogFlags & ImGuiDebugLogFlags.EventSelection) != 0)
            DebugLogMultiSelectRequests("EndMultiSelect", ms.IO);

        ms.FocusScopeId = 0;
        ms.Flags = ImGuiMultiSelectFlags.None;
        g.MultiSelectTempDataStacked--;
        g.CurrentMultiSelect = g.MultiSelectTempDataStacked > 0 ? g.MultiSelectTempData[g.MultiSelectTempDataStacked - 1] : null;

        return ms.IO;
    }

    /// <summary>
    /// Queues the selection user data that identifies the next submitted item within a multi-select
    /// scope (<c>ThirdParty/imgui/imgui_widgets.cpp:8282-8304</c>).
    /// </summary>
    /// <param name="selectionUserData">
    /// The item's selection user data; <c>-1</c> is invalid. Canonical uses an index for most
    /// applications, but any stable 64-bit value is accepted.
    /// </param>
    /// <remarks>
    /// This also queues the focus scope identifier and marks the next item as a multi-select item with
    /// selection data, and it lets the active scope note that its range source was submitted.
    /// </remarks>
    public static void SetNextItemSelectionUserData(long selectionUserData)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NextItemData.SelectionUserData = selectionUserData;
        g.NextItemData.FocusScopeId = g.CurrentFocusScopeId;

        ImGuiMultiSelectTempData? ms = g.CurrentMultiSelect;
        if (ms != null)
        {
            // Track the range source up front so the range can be evaluated without the clipper.
            g.NextItemData.ItemFlagsSet |= (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData
                | (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_IsMultiSelect;
            if (ms.IO.RangeSrcItem == selectionUserData)
                ms.RangeSrcPassedBy = true;
        }
        else
        {
            g.NextItemData.ItemFlagsSet |= (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData;
        }
    }

    /// <summary>
    /// Applies the pending select-all request and range-selection state to an item before its button
    /// behavior runs, and adjusts the button flags to support drag-and-drop of a multi-item selection
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8292-8362</c>).
    /// </summary>
    /// <param name="id">The item identifier.</param>
    /// <param name="selected">The item's selection state, updated in place.</param>
    /// <param name="buttonFlags">
    /// The item's button flags, updated in place. Canonical accepts a null pointer to skip the flag
    /// adjustment; callers that do not pass the flags to <c>ButtonBehavior()</c> can pass a discarded
    /// local.
    /// </param>
    /// <remarks>
    /// When the scope is focused this resolves the visible per-item selection from the pending clear or
    /// select-all operation and from a keyboard range selection. The button-flag adjustment delays the
    /// press to mouse-up for an already-selected item so that multi-item drag-and-drop can start.
    /// </remarks>
    internal static void MultiSelectItemHeader(uint id, ref bool selected, ref int buttonFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiMultiSelectTempData ms = g.CurrentMultiSelect!;

        if (ms.IsFocused)
        {
            ImGuiMultiSelectState storage = ms.Storage!;
            long itemData = g.NextItemData.SelectionUserData;
            ImAssert.Check(g.NextItemData.FocusScopeId == g.CurrentFocusScopeId, "Forgot to call SetNextItemSelectionUserData() prior to item, required in BeginMultiSelect()/EndMultiSelect() scope");

            // Apply the pending clear/select-all request for items the application has not processed yet.
            if (ms.LoopRequestSetAll != -1)
                selected = ms.LoopRequestSetAll == 1;

            if (ms.IsKeyboardSetRange)
            {
                ImAssert.Check(id != 0 && (ms.KeyMods & ImGuiKey.ImGuiMod_Shift) != 0);
                bool isRangeDst = !ms.RangeDstPassedBy && g.NavJustMovedToId == id;
                if (isRangeDst)
                    ms.RangeDstPassedBy = true;
                if (isRangeDst && storage.RangeSrcItem == -1)
                {
                    storage.RangeSrcItem = itemData;
                    storage.RangeSelected = selected ? (sbyte)1 : (sbyte)0;
                }
                bool isRangeSrc = storage.RangeSrcItem == itemData;
                if (isRangeSrc || isRangeDst || ms.RangeSrcPassedBy != ms.RangeDstPassedBy)
                {
                    ImAssert.Check(storage.RangeSrcItem != -1 && storage.RangeSelected != -1);
                    selected = storage.RangeSelected != 0;
                }
                else if ((ms.KeyMods & ImGuiKey.ImGuiMod_Ctrl) == 0 && (ms.Flags & ImGuiMultiSelectFlags.NoAutoClear) == 0)
                {
                    selected = false;
                }
            }
        }

        // Alter button behavior flags: delaying the press of an already-selected item to mouse-up allows
        // dragging a multi-item selection without clearing it.
        buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoHoveredOnFocus;
        buttonFlags &= ~((int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease);
        if ((ms.Flags & ImGuiMultiSelectFlags.SelectOnClickAlways) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick;
        else if ((ms.Flags & ImGuiMultiSelectFlags.SelectOnClickRelease) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease;
        else // ImGuiMultiSelectFlags_SelectOnAuto
            buttonFlags |= (!selected || (g.ActiveId == id && g.ActiveIdHasBeenPressedBefore))
                ? (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick
                : (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease;
    }

    /// <summary>
    /// Applies navigation auto-selection, box-select toggling, right-click selection, and the
    /// Ctrl/Shift selection rules to an item after its button behavior runs, then records the item's
    /// selection state for range and navigation tracking
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8364-8584</c>).
    /// </summary>
    /// <param name="id">The item identifier.</param>
    /// <param name="selected">The item's selection state, updated in place.</param>
    /// <param name="pressed">The item's pressed state, updated in place.</param>
    /// <param name="extraFlags">
    /// Extra flags OR-ed with the scope flags; the checkbox path passes
    /// <see cref="ImGuiMultiSelectFlags.CheckboxMode_"/>.
    /// </param>
    /// <remarks>
    /// The selection requests are emitted here, once per changed item, and the request buffer is switched
    /// to the end-phase on the first call so the begin-phase requests do not survive on an item that emits
    /// its own request.
    /// </remarks>
    internal static void MultiSelectItemFooter(uint id, ref bool selected, ref bool pressed, ImGuiMultiSelectFlags extraFlags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        bool pressedValue = pressed;
        ImGuiMultiSelectTempData ms = g.CurrentMultiSelect!;
        ImGuiMultiSelectState storage = ms.Storage!;
        if (pressedValue)
            ms.IsFocused = true;

        bool hovered = false;
        if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HoveredRect) != 0)
            hovered = IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup);
        if (!ms.IsFocused && !hovered)
            return;

        long itemData = g.NextItemData.SelectionUserData;

        ImGuiMultiSelectFlags flags = ms.Flags | extraFlags;
        bool isSingleSelect = (flags & ImGuiMultiSelectFlags.SingleSelect) != 0;
        bool isCtrl = (ms.KeyMods & ImGuiKey.ImGuiMod_Ctrl) != 0;
        bool isShift = (ms.KeyMods & ImGuiKey.ImGuiMod_Shift) != 0;

        bool applyToRangeSrc = false;

        if (g.NavId == id && storage.RangeSrcItem == -1)
            applyToRangeSrc = true;
        if (!ms.IsEndIO)
        {
            ms.IO.Requests.Clear();
            ms.IsEndIO = true;
        }

        // Auto-select as navigation moves through the list.
        if (g.NavJustMovedToId == id)
        {
            if ((flags & ImGuiMultiSelectFlags.NoAutoSelect) == 0)
            {
                if (isCtrl && isShift)
                    pressedValue = true;
                else if (!isCtrl)
                    selected = pressedValue = true;
            }
            else
            {
                // With NoAutoSelect, Shift+keyboard copies the source state instead of selecting.
                if (isShift)
                    pressedValue = true;
                else if (!isCtrl)
                    applyToRangeSrc = true;
            }
        }

        if (applyToRangeSrc)
        {
            storage.RangeSrcItem = itemData;
            storage.RangeSelected = selected ? (sbyte)1 : (sbyte)0;
        }

        // Box-select toggle handling.
        if (ms.BoxSelectId != 0)
        {
            ImGuiBoxSelectState? bs = GetBoxSelectState(ms.BoxSelectId);
            if (bs != null)
            {
                ImRect itemRect = g.LastItemData.Rect;
                if (!window.DC.NavIsScrollPushableX && g.CurrentTable != null && g.CurrentTable.CurrentColumn != -1)
                {
                    // The current clip rectangle may be wider than the column when SpanAllColumns is used,
                    // so account for both the column bounds and the item clip rectangle.
                    ImGuiTableColumn column = g.CurrentTable.Columns[g.CurrentTable.CurrentColumn];
                    float clipMinX = (g.LastItemData.ItemFlags & (ImGuiItemFlags)ImGuiItemStatusFlags.HasClipRect) != 0
                        ? g.LastItemData.ClipRect.Min.X : window.ClipRect.Min.X;
                    float clipMaxX = (g.LastItemData.ItemFlags & (ImGuiItemFlags)ImGuiItemStatusFlags.HasClipRect) != 0
                        ? g.LastItemData.ClipRect.Max.X : window.ClipRect.Max.X;
                    if (clipMinX != clipMaxX)
                    {
                        itemRect.Min.X = MathF.Max(itemRect.Min.X, MathF.Min(column.MinX, clipMinX));
                        itemRect.Max.X = MathF.Min(itemRect.Max.X, MathF.Max(column.MaxX, clipMaxX));
                    }
                    else
                    {
                        itemRect.Min.X = MathF.Max(itemRect.Min.X, column.MinX);
                        itemRect.Max.X = MathF.Min(itemRect.Max.X, column.MaxX);
                    }
                }

                bool rectOverlapCurr = bs.BoxSelectRectCurr.Overlaps(itemRect);
                bool rectOverlapPrev = bs.BoxSelectRectPrev.Overlaps(itemRect);
                if ((rectOverlapCurr && !rectOverlapPrev && !selected) || (rectOverlapPrev && !rectOverlapCurr))
                {
                    if (storage.LastSelectionSize <= 0 && bs.IsStartedSetNavIdOnce)
                    {
                        pressedValue = true;
                        bs.IsStartedSetNavIdOnce = false;
                    }
                    else
                    {
                        selected = !selected;
                        MultiSelectAddSetRange(ms, selected, +1, itemData, itemData);
                    }
                    storage.LastSelectionSize = Math.Max(storage.LastSelectionSize + 1, 1);
                }
            }
        }

        // Right-click selects an item on mouse-down, which is the behavior context menus rely on.
        if (hovered && IsMouseClicked(ImGuiMouseButton.Right)
            && (flags & (ImGuiMultiSelectFlags.NoAutoSelect | ImGuiMultiSelectFlags.NoSelectOnRightClick)) == 0)
        {
            if (g.ActiveId != 0 && g.ActiveId != id)
                ClearActiveID();
            SetFocusID(id, window);
            if (!pressedValue && !selected)
            {
                pressedValue = true;
                isCtrl = isShift = false;
            }
        }

        // Unlike Space, Enter does not alter selection unless the item is unselected.
        bool enterPressed = pressedValue && g.NavActivateId == id && (g.NavActivateFlags & ImGuiActivateFlags.PreferInput) != 0;

        if (pressedValue && (!enterPressed || !selected))
        {
            ImGuiInputSource inputSource = (g.NavJustMovedToId == id || g.NavActivateId == id) ? g.NavInputSource : ImGuiInputSource.Mouse;
            if ((flags & (ImGuiMultiSelectFlags.BoxSelect1d | ImGuiMultiSelectFlags.BoxSelect2d)) != 0
                && !g.BoxSelectState.IsActive && !g.BoxSelectState.IsStarting
                && inputSource == ImGuiInputSource.Mouse && g.IO.MouseClickedCount[0] == 1)
            {
                BoxSelectPreStartDrag(ms.BoxSelectId, itemData);
            }

            if ((flags & ImGuiMultiSelectFlags.NoAutoClear) == 0)
            {
                bool requestClear = false;
                if (isSingleSelect)
                    requestClear = true;
                else if ((inputSource == ImGuiInputSource.Mouse || g.NavActivateId == id) && !isCtrl)
                    requestClear = (flags & ImGuiMultiSelectFlags.NoAutoClearOnReselect) != 0 ? !selected : true;
                else if ((inputSource == ImGuiInputSource.Keyboard || inputSource == ImGuiInputSource.Gamepad) && isShift && !isCtrl)
                    requestClear = true;
                if (requestClear)
                    MultiSelectAddSetAll(ms, false);
            }

            int rangeDirection;
            bool rangeSelected;
            if (isShift && !isSingleSelect)
            {
                if (storage.RangeSrcItem == -1)
                    storage.RangeSrcItem = itemData;
                if ((flags & ImGuiMultiSelectFlags.CheckboxMode_) == 0)
                {
                    // Shift+Arrow always selects; Ctrl+Shift+Arrow copies the source selection state.
                    rangeSelected = (isCtrl && storage.RangeSelected != -1) ? storage.RangeSelected != 0 : true;
                }
                else
                {
                    // In checkbox mode Shift+Arrow copies the source state, while Shift+Click copies the
                    // target state.
                    rangeSelected = ms.IsKeyboardSetRange
                        ? (storage.RangeSelected != -1 ? storage.RangeSelected != 0 : true)
                        : !selected;
                }
                rangeDirection = ms.RangeSrcPassedBy ? +1 : -1;
            }
            else
            {
                // Ctrl inverts the selection, otherwise the item is always selected.
                selected = (flags & ImGuiMultiSelectFlags.CheckboxMode_) == 0 ? (isCtrl ? !selected : true) : !selected;
                storage.RangeSrcItem = itemData;
                rangeSelected = selected;
                rangeDirection = +1;
            }
            MultiSelectAddSetRange(ms, rangeSelected, rangeDirection, storage.RangeSrcItem, itemData);
        }

        // Store the source item's selection state for a later Ctrl+Shift copy.
        if (storage.RangeSrcItem == itemData)
            storage.RangeSelected = selected ? (sbyte)1 : (sbyte)0;

        // Store the focused item's selection state.
        if (g.NavId == id)
        {
            storage.NavIdItem = itemData;
            storage.NavIdSelected = selected ? (sbyte)1 : (sbyte)0;
        }
        if (storage.NavIdItem == itemData)
            ms.NavIdPassedBy = true;

        pressed = pressedValue;
    }

    /// <summary>
    /// Replaces the request list with a single select-all or clear-all request
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8586-8591</c>).
    /// </summary>
    /// <param name="ms">The active temporary multi-select data.</param>
    /// <param name="selected">True to select all items, false to clear the selection.</param>
    internal static void MultiSelectAddSetAll(ImGuiMultiSelectTempData ms, bool selected)
    {
        ImGuiSelectionRequest req = new()
        {
            Type = ImGuiSelectionRequestType.SetAll,
            Selected = selected,
            RangeDirection = 0,
            RangeFirstItem = -1,
            RangeLastItem = -1,
        };
        ms.IO.Requests.Clear();
        ms.IO.Requests.Add(req);
    }

    /// <summary>
    /// Appends a range request, storing its endpoints already ordered left-to-right and recording the
    /// direction in which the user produced the range
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8593-8634</c>).
    /// </summary>
    /// <param name="ms">The active temporary multi-select data.</param>
    /// <param name="selected">True to select the range, false to clear it.</param>
    /// <param name="rangeDir">The direction of the range: <c>+1</c> or <c>-1</c>.</param>
    /// <param name="firstItem">The first endpoint as the caller produced it.</param>
    /// <param name="lastItem">The last endpoint as the caller produced it.</param>
    /// <remarks>
    /// Unlike an older canonical revision this port does not merge contiguous single-item ranges, matching
    /// the current reference where merging is disabled to stay correct under coarse clipping.
    /// </remarks>
    internal static void MultiSelectAddSetRange(ImGuiMultiSelectTempData ms, bool selected, int rangeDir, long firstItem, long lastItem)
    {
        ImGuiSelectionRequest req = new()
        {
            Type = ImGuiSelectionRequestType.SetRange,
            Selected = selected,
            RangeDirection = (sbyte)rangeDir,
            RangeFirstItem = rangeDir > 0 ? firstItem : lastItem,
            RangeLastItem = rangeDir > 0 ? lastItem : firstItem,
        };
        ms.IO.Requests.Add(req);
    }

    /// <summary>
    /// Shows one persistent multi-select state record in the debug tools
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:8614-8632</c>).
    /// </summary>
    /// <param name="storage">The state record to display.</param>
    /// <remarks>
    /// The record is shown dimmed when it has not been active for the last two frames. The canonical debug
    /// window that calls this is owned by the metrics and debug chunk.
    /// </remarks>
    internal static void DebugNodeMultiSelectState(ImGuiMultiSelectState storage)
    {
        bool isActive = storage.LastFrameActive >= GetFrameCount() - 2;
        if (!isActive)
            PushStyleColor(ImGuiCol.Text, GetStyleColorVec4(ImGuiCol.TextDisabled));
        bool open = TreeNode((object)storage.ID, "MultiSelect 0x{0:X8} in '{1}'{2}", storage.ID, storage.Window != null ? storage.Window.Name : "N/A", isActive ? string.Empty : " *Inactive*");
        if (!isActive)
            PopStyleColor();
        if (!open)
            return;
        Text("RangeSrcItem = {0}, RangeSelected = {1}", storage.RangeSrcItem, storage.RangeSelected);
        Text("NavIdItem = {0}, NavIdSelected = {1}", storage.NavIdItem, storage.NavIdSelected);
        Text("LastSelectionSize = {0}", storage.LastSelectionSize);
        TreePop();
    }

    /// <summary>
    /// Pushes a focus scope identifier and records the owning window
    /// (<c>ThirdParty/imgui/imgui.cpp:9487-9495</c>).
    /// </summary>
    /// <param name="id">The focus scope identifier to push.</param>
    public static void PushFocusScope(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.FocusScopeStack.Add(new ImGuiFocusScopeData { ID = id, WindowID = g.CurrentWindow!.ID });
        g.CurrentFocusScopeId = id;
    }

    /// <summary>
    /// Pops the current focus scope and restores the enclosing scope
    /// (<c>ThirdParty/imgui/imgui.cpp:9497-9503</c>).
    /// </summary>
    public static void PopFocusScope()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.StackSizesInBeginForCurrentWindow == null
            || g.FocusScopeStack.Count > g.StackSizesInBeginForCurrentWindow.SizeOfFocusScopeStack,
            "Calling PopFocusScope() too many times!");
        g.FocusScopeStack.RemoveAt(g.FocusScopeStack.Count - 1);
        g.CurrentFocusScopeId = g.FocusScopeStack.Count > 0 ? g.FocusScopeStack[^1].ID : 0;
    }

    /// <summary>
    /// Returns whether a focus scope is on the current navigation focus route
    /// (<c>ThirdParty/imgui/imgui.cpp:9505-9514</c>).
    /// </summary>
    /// <param name="focusScopeId">The focus scope identifier to test.</param>
    /// <returns>True when the identifier is the focused scope or lies on the focus route.</returns>
    public static bool IsInNavFocusRoute(uint focusScopeId)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavFocusScopeId == focusScopeId)
            return true;
        for (int n = 0; n < g.NavFocusRoute.Count; n++)
        {
            if (g.NavFocusRoute[n].ID == focusScopeId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Converts an absolute screen position to window-content relative space
    /// (<c>ThirdParty/imgui/imgui_internal.h:3604</c>).
    /// </summary>
    /// <param name="window">The window whose content origin is subtracted.</param>
    /// <param name="p">The absolute position.</param>
    /// <returns>The position relative to the window content origin.</returns>
    private static ImVec2 WindowPosAbsToRel(ImGuiWindow window, ImVec2 p)
    {
        ImVec2 offset = window.DC.CursorStartPos;
        return new ImVec2(p.X - offset.X, p.Y - offset.Y);
    }

    /// <summary>
    /// Converts a window-content relative position back to absolute screen space
    /// (<c>ThirdParty/imgui/imgui_internal.h:3605</c>).
    /// </summary>
    /// <param name="window">The window whose content origin is added.</param>
    /// <param name="p">The relative position.</param>
    /// <returns>The absolute position.</returns>
    private static ImVec2 WindowPosRelToAbs(ImGuiWindow window, ImVec2 p)
    {
        ImVec2 offset = window.DC.CursorStartPos;
        return new ImVec2(p.X + offset.X, p.Y + offset.Y);
    }

    #endregion
}
