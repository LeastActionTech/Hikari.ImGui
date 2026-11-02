#nullable enable
/*================================================================================================
 * LIST CLIPPER
 *================================================================================================
 * ROLE IN ENGINE:
 * Implements the canonical ImGuiListClipper helper that lets a caller submit only the items that
 * intersect a window's visible clip rectangle instead of every item in a long list. The clipper
 * never owns the items themselves: the caller draws the items named by DisplayStart/DisplayEnd on
 * each Step() and the clipper advances the window cursor to the right vertical position so that
 * scrolling, keyboard navigation, tables, legacy columns, and box-selection keep working.
 *
 * SUBSYSTEM CONNECTIONS & DATA FLOW:
 *
 *    ┌───────────────────────────────────────────────────────────────┐
 *    │ ImGuiListClipper (caller-owned)                               │
 *    │ • DisplayStart / DisplayEnd / ItemsCount / ItemsHeight        │
 *    │ • StartPosY / StartSeekOffsetY (double-precision cursor math) │
 *    └───────────────────────────────────────────────────────────────┘
 *          │ Begin() acquires, End() releases                       ▲
 *          ▼                                                        │
 *    ┌───────────────────────────────────────────────────────────────┐
 *    │ ImGuiContext.ClipperTempData (per-frame stacked pool)         │
 *    │ • ImGuiListClipperData: StepNo, ItemsFrozen, Ranges           │
 *    └───────────────────────────────────────────────────────────────┘
 *          │ reads window/table/nav/box-select state                ▲
 *          ▼                                                        │
 *    ┌───────────────────────────────────────────────────────────────┐
 *    │ ImGuiWindow.DC cursor, ImGuiTable rows, legacy columns        │
 *    └───────────────────────────────────────────────────────────────┘
 *
 * STYLIZED USAGE EXAMPLE:
 *
 *   var clipper = new ImGuiListClipper();
 *   clipper.Begin(itemsCount, ImGui.GetTextLineHeightWithSpacing());
 *   while (clipper.Step())
 *       for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
 *           ImGui.Text($"item {i}");
 *
 * THREADING & LIFECYCLE NOTES:
 * - Every clipper operates on the single frame-submitting thread and borrows temporary data from the
 *   context it was begun in. No internal synchronization is provided.
 * - Begin() must be paired with Step() returning false, or with an explicit End(); End() restores the
 *   borrowed temporary data to the context pool.
 *================================================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Hikari.ImGui;

#region Range and temporary data

/// <summary>
/// One contiguous range of item indices, or one range of absolute Y positions that still has to be
/// converted to item indices once the item height is known
/// (<c>ThirdParty/imgui/imgui_internal.h:1712-1722</c>).
/// </summary>
/// <remarks>
/// <see cref="Max"/> is exclusive. When <see cref="PosToIndexConvert"/> is set, <see cref="Min"/>
/// and <see cref="Max"/> hold absolute Y positions rather than indices, and the two offset fields are
/// applied after conversion so that the range can be nudged outward for keyboard navigation.
/// </remarks>
public sealed class ImGuiListClipperRange
{
    /// <summary>
    /// Gets or sets the inclusive lower bound, or the lower Y position while
    /// <see cref="PosToIndexConvert"/> is set. (<c>ThirdParty/imgui/imgui_internal.h:1714</c>)
    /// </summary>
    public int Min { get; set; }

    /// <summary>
    /// Gets or sets the exclusive upper bound, or the upper Y position while
    /// <see cref="PosToIndexConvert"/> is set. (<c>ThirdParty/imgui/imgui_internal.h:1715</c>)
    /// </summary>
    public int Max { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Min"/> and <see cref="Max"/> are absolute Y
    /// positions that must be converted to item indices, rather than item indices already.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1716</c>)
    /// </summary>
    public bool PosToIndexConvert { get; set; }

    /// <summary>
    /// Gets or sets the amount added to the converted minimum index, in the inclusive range -128..127.
    /// This lets a keyboard navigation request extend the visible range by one item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1717</c>)
    /// </summary>
    public sbyte PosToIndexOffsetMin { get; set; }

    /// <summary>
    /// Gets or sets the amount added to the converted maximum index, in the inclusive range -128..127.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1718</c>)
    /// </summary>
    public sbyte PosToIndexOffsetMax { get; set; }

    /// <summary>
    /// Creates an index range. (<c>ThirdParty/imgui/imgui_internal.h:1720</c>)
    /// </summary>
    /// <param name="min">Inclusive first item index.</param>
    /// <param name="max">Exclusive last item index.</param>
    /// <returns>A range whose bounds are item indices.</returns>
    public static ImGuiListClipperRange FromIndices(int min, int max)
    {
        return new ImGuiListClipperRange { Min = min, Max = max, PosToIndexConvert = false };
    }

    /// <summary>
    /// Creates a position range that is converted to item indices once the item height is known.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1721</c>)
    /// </summary>
    /// <param name="y1">Lower absolute Y position; truncated toward zero into <see cref="Min"/>.</param>
    /// <param name="y2">Upper absolute Y position; truncated toward zero into <see cref="Max"/>.</param>
    /// <param name="offMin">Offset added after conversion to the minimum index.</param>
    /// <param name="offMax">Offset added after conversion to the maximum index.</param>
    /// <returns>A range whose bounds are Y positions.</returns>
    public static ImGuiListClipperRange FromPositions(float y1, float y2, int offMin, int offMax)
    {
        return new ImGuiListClipperRange
        {
            Min = (int)y1,
            Max = (int)y2,
            PosToIndexConvert = true,
            PosToIndexOffsetMin = (sbyte)offMin,
            PosToIndexOffsetMax = (sbyte)offMax,
        };
    }
}

/// <summary>
/// Scratch data for one active clipper, pooled by <see cref="ImGuiContext"/> so that a frame does not
/// allocate a new range list per clipper. The pool entry is reused across clippers and frames
/// (<c>ThirdParty/imgui/imgui_internal.h:1725-1734</c>).
/// </summary>
/// <remarks>
/// A pool entry is owned by the clipper named by <see cref="ListClipper"/> until that clipper calls
/// <c>End()</c>. Nesting is supported: each nested clipper takes the next pool entry, and the entry
/// below has its <see cref="ListClipper"/> back-pointer repaired when the nested clipper ends.
/// </remarks>
public sealed class ImGuiListClipperData
{
    /// <summary>
    /// Gets or sets the clipper currently borrowing this entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1727</c>)
    /// </summary>
    public ImGuiListClipper? ListClipper { get; set; }

    /// <summary>
    /// Gets or sets the horizontal-scroll precision loss of the window cursor at <c>Begin()</c>. It
    /// is subtracted while converting Y positions to indices and added back while seeking.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1728</c>)
    /// </summary>
    public float LossynessOffset { get; set; }

    /// <summary>
    /// Gets or sets the index of the next range to submit within <see cref="Ranges"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1729</c>)
    /// </summary>
    public int StepNo { get; set; }

    /// <summary>
    /// Gets or sets the number of table frozen rows that have already been submitted unclipped.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1730</c>)
    /// </summary>
    public int ItemsFrozen { get; set; }

    /// <summary>
    /// Gets the visible and navigation ranges computed for this clipper, in submission order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1731</c>)
    /// </summary>
    public List<ImGuiListClipperRange> Ranges { get; } = new();

    /// <summary>
    /// Rebinds this pooled entry to a clipper and clears its per-use range state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1733</c>)
    /// </summary>
    /// <param name="clipper">The clipper that will borrow this entry.</param>
    public void Reset(ImGuiListClipper clipper)
    {
        ListClipper = clipper;
        StepNo = 0;
        ItemsFrozen = 0;
        Ranges.Clear();
    }
}

#endregion

#region ImGuiListClipper

/// <summary>
/// Submits only the items that intersect the visible clip rectangle and keeps the window cursor at the
/// matching vertical position (<c>ThirdParty/imgui/imgui.h:3028-3068</c>,
/// <c>ThirdParty/imgui/imgui.cpp:3340-3660</c>).
/// </summary>
/// <remarks>
/// <para>
/// The caller drives the clipper with a <c>while (clipper.Step())</c> loop and draws the items in
/// <c>[DisplayStart, DisplayEnd)</c> on each iteration. The first iteration always exposes one item so
/// the clipper can measure its height when <c>itemsHeight</c> was not supplied.
/// </para>
/// <para>
/// <c>StartPosY</c>, <c>StartSeekOffsetY</c>, and the item height are combined in double precision
/// when seeking, so very tall lists and very large scroll offsets keep their accuracy.
/// </para>
/// <para>
/// This canonical type declares a destructor that calls <c>End()</c>. The port has no deterministic
/// destructor, so a caller must exhaust <c>Step()</c> or call <c>End()</c> explicitly; the tests and
/// the demo always do. Tracing the context from a finalizer would be unsafe and is deliberately not
/// attempted.
/// </para>
/// <para>
/// This type is plain single-threaded mutable state and provides no internal synchronization.
/// </para>
/// </remarks>
public sealed class ImGuiListClipper
{
    #region Constants

    /// <summary>
    /// The float magnitude above which consecutive integers can no longer be represented exactly, so
    /// the clipper must fall back to assuming the previous line height. Mirrors canonical
    /// <c>ImIsFloatAboveGuaranteedIntegerPrecision</c> (<c>ThirdParty/imgui/imgui_internal.h:557</c>).
    /// </summary>
    private const float GuaranteedIntegerPrecisionLimit = 16777216.0f;

    #endregion

    #region Properties and state fields

    /// <summary>
    /// Gets or sets the first item to display; it is updated by each call to <see cref="Step"/>.
    /// (<c>ThirdParty/imgui/imgui.h:3030</c>)
    /// </summary>
    public int DisplayStart { get; set; }

    /// <summary>
    /// Gets or sets the exclusive end of the items to display.
    /// (<c>ThirdParty/imgui/imgui.h:3031</c>)
    /// </summary>
    public int DisplayEnd { get; set; }

    /// <summary>
    /// Gets or sets caller-owned storage that the clipper never reads.
    /// (<c>ThirdParty/imgui/imgui.h:3032</c>)
    /// </summary>
    public int UserIndex { get; set; }

    /// <summary>
    /// Gets or sets the number of items in the list, or <see cref="int.MaxValue"/> when it is unknown.
    /// (<c>ThirdParty/imgui/imgui.h:3033</c>)
    /// </summary>
    public int ItemsCount { get; set; }

    /// <summary>
    /// Gets or sets the height of one item. A value of zero or less asks the clipper to infer it from
    /// the first submitted item. (<c>ThirdParty/imgui/imgui.h:3034</c>)
    /// </summary>
    public float ItemsHeight { get; set; }

    /// <summary>
    /// Gets or sets the clipper behavior flags. (<c>ThirdParty/imgui/imgui.h:3035</c>)
    /// </summary>
    public ImGuiListClipperFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the window cursor Y position captured at <c>Begin()</c>, in double precision so
    /// that seeking stays accurate in very tall windows. (<c>ThirdParty/imgui/imgui.h:3036</c>)
    /// </summary>
    public double StartPosY { get; set; }

    /// <summary>
    /// Gets or sets the correction applied while seeking: the window precision loss minus the frozen
    /// rows already accounted for. (<c>ThirdParty/imgui/imgui.h:3037</c>)
    /// </summary>
    public double StartSeekOffsetY { get; set; }

    /// <summary>
    /// Gets or sets the parent UI context captured at <c>Begin()</c>.
    /// (<c>ThirdParty/imgui/imgui.h:3038</c>)
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the pooled temporary data borrowed from <see cref="Ctx"/>, or <see langword="null"/>
    /// while the clipper is inactive. (<c>ThirdParty/imgui/imgui.h:3039</c>)
    /// </summary>
    public ImGuiListClipperData? TempData { get; set; }

    #endregion

    #region Construction

    /// <summary>
    /// Initializes an inactive clipper. Mirrors the canonical constructor that zero-clears the struct
    /// (<c>ThirdParty/imgui/imgui.cpp:3392-3395</c>).
    /// </summary>
    public ImGuiListClipper()
    {
        DisplayStart = 0;
        DisplayEnd = 0;
        UserIndex = 0;
        ItemsCount = 0;
        ItemsHeight = 0.0f;
        Flags = ImGuiListClipperFlags.None;
        StartPosY = 0.0;
        StartSeekOffsetY = 0.0;
        Ctx = null;
        TempData = null;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Starts a clipping pass over <paramref name="itemsCount"/> items
    /// (<c>ThirdParty/imgui/imgui.cpp:3402-3428</c>).
    /// </summary>
    /// <param name="itemsCount">Number of items, or <see cref="int.MaxValue"/> when unknown; in the
    /// unknown case the cursor is not advanced on the final step and the caller must seek manually.</param>
    /// <param name="itemsHeight">Distance between items, or a value of zero or less to infer it from the
    /// first submitted item, typically <c>GetTextLineHeightWithSpacing()</c>.</param>
    /// <remarks>
    /// Any table row still open is closed first because the clipper cannot run inside a table cell. The
    /// cursor Y position and the window precision loss are captured, then a temporary-data entry is
    /// acquired from the context pool; the pool grows and is reused but is not shrunk during the frame.
    /// </remarks>
    public void Begin(int itemsCount, float itemsHeight = -1.0f)
    {
        Ctx = ImGui.GetCurrentContext();
        ImGuiContext g = Ctx!;
        ImGuiWindow window = g.CurrentWindow!;

        if (g.CurrentTable is ImGuiTable table && table.IsInsideRow)
            ImGui.TableEndRow(table);

        StartPosY = window.DC.CursorPos.Y;
        ItemsHeight = itemsHeight;
        ItemsCount = itemsCount;
        DisplayStart = -1;
        DisplayEnd = 0;

        // Acquire a pooled entry. ClipperTempDataStacked is also the count of active clippers, so the
        // entry at index (stacked - 1) always belongs to the newest clipper.
        if (++g.ClipperTempDataStacked > g.ClipperTempData.Count)
            g.ClipperTempData.Add(new ImGuiListClipperData());
        ImGuiListClipperData data = g.ClipperTempData[g.ClipperTempDataStacked - 1];
        data.Reset(this);
        data.LossynessOffset = window.DC.CursorStartPosLossyness.Y;
        TempData = data;
        StartSeekOffsetY = data.LossynessOffset;
    }

    /// <summary>
    /// Ends the clipping pass, seeks the cursor to the end of the list, and returns the borrowed
    /// temporary data to the context pool (<c>ThirdParty/imgui/imgui.cpp:3430-3453</c>).
    /// </summary>
    /// <remarks>
    /// Canonical permits calling <c>End()</c> at any point; the cursor is sought to the end rather than
    /// asserting that the caller already reached it. When a nested clipper ends, the entry below it has
    /// its back-pointer repaired because the pool slot was handed back.
    /// </remarks>
    public void End()
    {
        if (TempData is ImGuiListClipperData data)
        {
            ImGuiContext g = Ctx!;
            if (ItemsCount >= 0 && ItemsCount < int.MaxValue && DisplayStart >= 0)
                SeekCursorForItem(ItemsCount);

            data.StepNo = data.Ranges.Count;
            if (--g.ClipperTempDataStacked > 0)
            {
                data = g.ClipperTempData[g.ClipperTempDataStacked - 1];
                data.ListClipper!.TempData = data;
            }
            TempData = null;
        }

        // Clear the visible range so that a reused clipper past its last Step() does not expose a
        // stale range, and mark the clipper inactive.
        DisplayStart = DisplayEnd = ItemsCount;
        ItemsCount = -1;
    }

    /// <summary>
    /// Advances to the next visible range. Call until it returns <see langword="false"/>
    /// (<c>ThirdParty/imgui/imgui.cpp:3648-3660</c>).
    /// </summary>
    /// <returns><see langword="true"/> while a range remains to be submitted.</returns>
    /// <remarks>
    /// <see cref="Step"/> calls <see cref="End"/> on the final call, so an explicit <see cref="End"/> is
    /// only needed when the loop is abandoned early.
    /// </remarks>
    public bool Step()
    {
        bool ret = StepInternal(this);
        if (ret && DisplayStart >= DisplayEnd)
            ret = false;
        if (!ret)
            End();
        return ret;
    }

    /// <summary>
    /// Marks one item as never clipped regardless of visibility
    /// (<c>ThirdParty/imgui/imgui.h:3064</c>).
    /// </summary>
    /// <param name="itemIndex">Index of the item to always submit.</param>
    public void IncludeItemByIndex(int itemIndex)
    {
        IncludeItemsByIndex(itemIndex, itemIndex + 1);
    }

    /// <summary>
    /// Marks a half-open range of items as never clipped regardless of visibility. Must be called after
    /// <c>Begin()</c> and before the first <c>Step()</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:3454-3463</c>).
    /// </summary>
    /// <param name="itemBegin">Inclusive first item index.</param>
    /// <param name="itemEnd">Exclusive last item index.</param>
    public void IncludeItemsByIndex(int itemBegin, int itemEnd)
    {
        ImGuiListClipperData data = TempData!;
        ImAssert.Check(DisplayStart < 0, "IncludeItemsByIndex() must be called after Begin() and before the first Step().");
        ImAssert.Check(itemBegin <= itemEnd, "IncludeItemsByIndex() requires itemBegin <= itemEnd.");
        if (itemBegin < itemEnd)
            data.Ranges.Add(ImGuiListClipperRange.FromIndices(itemBegin, itemEnd));
    }

    /// <summary>
    /// Moves the window cursor to the vertical position of the given item
    /// (<c>ThirdParty/imgui/imgui.cpp:3466-3473</c>).
    /// </summary>
    /// <param name="itemIndex">Index of the item whose position the cursor should take.</param>
    /// <remarks>
    /// The step loop calls this automatically. The only reason for a caller to call it directly is the
    /// indeterminate mode: after passing <see cref="int.MaxValue"/> to <c>Begin()</c>, the caller seeks
    /// once the final item count is known.
    /// </remarks>
    public void SeekCursorForItem(int itemIndex)
    {
        // The add and multiply are performed in double precision so that seeking remains accurate for
        // very large ranges. StartSeekOffsetY cancels out the frozen-row offset and the window
        // precision loss; it is stored rather than inferred so that a seek is still possible after the
        // temporary data has been released.
        float posY = (float)((double)StartPosY + StartSeekOffsetY + (double)itemIndex * ItemsHeight);
        SeekCursorAndSetupPrevLine(posY, ItemsHeight);
    }

    #endregion

    #region Step implementation

    /// <summary>
    /// Computes and returns the next visible range, updating the window cursor and the table or legacy
    /// column state as needed. Ports canonical <c>ImGuiListClipper_StepInternal</c> step for step
    /// (<c>ThirdParty/imgui/imgui.cpp:3475-3646</c>).
    /// </summary>
    /// <param name="clipper">The clipper being stepped.</param>
    /// <returns><see langword="true"/> when a non-empty range is available.</returns>
    private static bool StepInternal(ImGuiListClipper clipper)
    {
        ImGuiContext g = clipper.Ctx!;
        ImGuiWindow window = g.CurrentWindow!;
        if (clipper.TempData == null)
            return false;
        ImGuiListClipperData data = clipper.TempData;

        ImGuiTable? table = g.CurrentTable;
        if (table != null && table.IsInsideRow)
            ImGui.TableEndRow(table);

        // No items, or the host window decides that clipping is unnecessary.
        if (clipper.ItemsCount == 0 || GetSkipItemForListClipping())
            return false;

        // While a table is still in its frozen-row state, submit frozen rows one at a time and unclipped.
        if (data.StepNo == 0 && table != null && !table.IsUnfrozenRows)
        {
            clipper.DisplayStart = data.ItemsFrozen;
            clipper.DisplayEnd = Math.Min(data.ItemsFrozen + 1, clipper.ItemsCount);
            if (clipper.DisplayStart < clipper.DisplayEnd)
                data.ItemsFrozen++;
            return true;
        }

        // Step 0 exposes the first item (or range) so that its height can be measured when the caller
        // did not supply an item height.
        bool calcClipping = false;
        if (data.StepNo == 0)
        {
            clipper.StartPosY = window.DC.CursorPos.Y;
            if (clipper.ItemsHeight <= 0.0f)
            {
                data.Ranges.Insert(0, ImGuiListClipperRange.FromIndices(data.ItemsFrozen, data.ItemsFrozen + 1));
                clipper.DisplayStart = Math.Max(data.Ranges[0].Min, data.ItemsFrozen);
                clipper.DisplayEnd = Math.Min(data.Ranges[0].Max, clipper.ItemsCount);
                data.StepNo = 1;
                return true;
            }
            calcClipping = true;
        }

        // Step 1 infers the item height from the cursor movement caused by the first submitted item.
        if (clipper.ItemsHeight <= 0.0f)
        {
            ImAssert.Check(data.StepNo == 1, "Item height inference requires the first step's submission.");
            bool affectedByFloatingPointPrecision =
                IsFloatAboveGuaranteedIntegerPrecision((float)clipper.StartPosY)
                || IsFloatAboveGuaranteedIntegerPrecision(window.DC.CursorPos.Y);
            if (affectedByFloatingPointPrecision)
            {
                // Mitigation for very large offsets: assume the previous line's height plus spacing.
                // The canonical fallback does not allow for multi-line entries.
                clipper.ItemsHeight = window.DC.PrevLineSize.Y + g.Style.ItemSpacing.Y;
                window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, (float)(clipper.StartPosY + clipper.ItemsHeight));
            }
            else
            {
                clipper.ItemsHeight = (float)(window.DC.CursorPos.Y - clipper.StartPosY) / (float)(clipper.DisplayEnd - clipper.DisplayStart);
            }

            if (clipper.ItemsHeight == 0.0f && clipper.ItemsCount == int.MaxValue)
                return false;
            if (clipper.ItemsHeight <= 0.0f)
                return false;
            calcClipping = true;
        }

        int alreadySubmitted = clipper.DisplayEnd;
        if (calcClipping)
        {
            // Store the seek offset so that SeekCursorForItem() still works after the temporary data is
            // released. It cancels the frozen rows and the window precision loss.
            clipper.StartSeekOffsetY = (double)data.LossynessOffset - data.ItemsFrozen * (double)clipper.ItemsHeight;

            if (g.LogEnabled)
            {
                // Logging must not clip anything away.
                data.Ranges.Add(ImGuiListClipperRange.FromIndices(0, clipper.ItemsCount));
            }
            else
            {
                // Keyboard/gamepad navigation extends the range by one item on the leading and trailing
                // edges so the navigation code can see the item it is moving toward.
                bool isNavRequest = g.NavMoveScoringItems && g.NavWindow != null && g.NavWindow.RootWindowForNav == window.RootWindowForNav;
                int navOffMin = (isNavRequest && g.NavMoveClipDir == ImGuiDir.Up) ? -1 : 0;
                int navOffMax = (isNavRequest && g.NavMoveClipDir == ImGuiDir.Down) ? 1 : 0;
                if (isNavRequest)
                {
                    data.Ranges.Add(ImGuiListClipperRange.FromPositions(g.NavScoringRect.Min.Y, g.NavScoringRect.Max.Y, navOffMin, navOffMax));
                    if (!g.NavScoringNoClipRect.IsInverted())
                        data.Ranges.Add(ImGuiListClipperRange.FromPositions(g.NavScoringNoClipRect.Min.Y, g.NavScoringNoClipRect.Max.Y, navOffMin, navOffMax));
                }
                if (isNavRequest && (g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0 && g.NavTabbingDir == -1)
                    data.Ranges.Add(ImGuiListClipperRange.FromIndices(clipper.ItemsCount - 1, clipper.ItemsCount));

                // Add the focused or active item.
                ImRect navRectAbs = ImGui.WindowRectRelToAbs(window, window.NavRectRel[0]);
                if (g.NavId != 0 && window.NavLastIds[0] == g.NavId)
                    data.Ranges.Add(ImGuiListClipperRange.FromPositions(navRectAbs.Min.Y, navRectAbs.Max.Y, 0, 0));

                float minY = window.ClipRect.Min.Y;
                float maxY = window.ClipRect.Max.Y;

                // Add the box-selection range. Half an ItemSpacing is applied on each side because
                // Selectable() uses half spacing and the overlap comparison of the box-selection is
                // sensitive to that half-unit difference.
                ImGuiBoxSelectState bs = g.BoxSelectState;
                if (bs.IsActive && bs.Window == window)
                {
                    float padY = g.Style.ItemSpacing.Y;
                    minY -= padY;
                    maxY += padY;
                    if (bs.UnclipMode)
                        data.Ranges.Add(ImGuiListClipperRange.FromPositions(bs.UnclipRect.Min.Y - padY, bs.UnclipRect.Max.Y + padY, 0, 0));
                }

                // Add the main visible range.
                data.Ranges.Add(ImGuiListClipperRange.FromPositions(minY, maxY, navOffMin, navOffMax));
            }

            // Convert position ranges to item index ranges. When a starting position lies beyond the
            // last item, Min is pinned to (ItemsCount - 1); this keeps most forms of wrapping working.
            // Selectable() extra padding makes ranges unaligned with exact items, so the flooring and
            // ceiling below intentionally tend to include two items rather than one.
            for (int n = 0; n < data.Ranges.Count; n++)
            {
                ImGuiListClipperRange range = data.Ranges[n];
                if (!range.PosToIndexConvert)
                    continue;
                int m1 = (int)(((double)range.Min - window.DC.CursorPos.Y - data.LossynessOffset) / clipper.ItemsHeight);
                int m2 = (int)((((double)range.Max - window.DC.CursorPos.Y - data.LossynessOffset) / clipper.ItemsHeight) + 0.999999f);
                range.Min = Clamp(alreadySubmitted + m1 + range.PosToIndexOffsetMin, alreadySubmitted, clipper.ItemsCount - 1);
                range.Max = Clamp(alreadySubmitted + m2 + range.PosToIndexOffsetMax, range.Min + 1, clipper.ItemsCount);
                range.PosToIndexConvert = false;
                data.Ranges[n] = range;
            }
            SortAndFuseRanges(data.Ranges, data.StepNo);
        }

        // Step 0 or 1 onward: display the next range in line.
        while (data.StepNo < data.Ranges.Count)
        {
            clipper.DisplayStart = Math.Max(data.Ranges[data.StepNo].Min, alreadySubmitted);
            clipper.DisplayEnd = Math.Min(data.Ranges[data.StepNo].Max, clipper.ItemsCount);
            data.StepNo++;
            if (clipper.DisplayStart >= clipper.DisplayEnd)
                continue;
            if (clipper.DisplayStart > alreadySubmitted)
                clipper.SeekCursorForItem(clipper.DisplayStart);
            return true;
        }

        // After the last step, advance the cursor to the end of the list. Indeterminate counts leave
        // the cursor alone because their item height is unknown.
        if (clipper.ItemsCount < int.MaxValue)
            clipper.SeekCursorForItem(clipper.ItemsCount);

        return false;
    }

    /// <summary>
    /// Moves the window cursor to <paramref name="posY"/> and repairs the previous-line state so that
    /// <c>SetScrollHereY()</c>, legacy columns, and tables behave as if the skipped items had been
    /// submitted. Mirrors canonical <c>ImGuiListClipper_SeekCursorAndSetupPrevLine</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:3357-3389</c>).
    /// </summary>
    /// <param name="posY">Absolute Y position to seek to.</param>
    /// <param name="lineHeight">Height of one item, used for the previous-line fields.</param>
    private void SeekCursorAndSetupPrevLine(float posY, float lineHeight)
    {
        ImGuiContext g = Ctx!;
        ImGuiWindow window = g.CurrentWindow!;
        float offY = posY - window.DC.CursorPos.Y;

        ImVec2 cursorPos = window.DC.CursorPos;
        cursorPos.Y = posY;
        window.DC.CursorPos = cursorPos;

        ImVec2 cursorMaxPos = window.DC.CursorMaxPos;
        cursorMaxPos.Y = Math.Max(window.DC.CursorMaxPos.Y, posY - g.Style.ItemSpacing.Y);
        window.DC.CursorMaxPos = cursorMaxPos;

        // These fields let SetScrollHereY() work after the clipper finishes.
        ImVec2 cursorPosPrevLine = window.DC.CursorPosPrevLine;
        cursorPosPrevLine.Y = window.DC.CursorPos.Y - lineHeight;
        window.DC.CursorPosPrevLine = cursorPosPrevLine;

        ImVec2 prevLineSize = window.DC.PrevLineSize;
        prevLineSize.Y = lineHeight - g.Style.ItemSpacing.Y;
        window.DC.PrevLineSize = prevLineSize;

        if (window.DC.CurrentColumns is ImGuiOldColumns columns)
            columns.LineMinY = window.DC.CursorPos.Y; // Keeps legacy cell Y positions correct.

        if (g.CurrentTable is ImGuiTable table)
        {
            if (table.IsInsideRow)
                ImGui.TableEndRow(table);
            int rowIncrease = (int)((offY / lineHeight) + 0.5f);
            if (rowIncrease > 0 && (Flags & ImGuiListClipperFlags.NoSetTableRowCounters) == 0)
            {
                // The clipper height may differ from the actual table row height; the flag lets a
                // caller opt out of adjusting the row counters when that difference matters.
                table.CurrentRow += rowIncrease;
                table.RowBgColorCounter += rowIncrease;
            }
            table.RowPosY2 = window.DC.CursorPos.Y;
        }
    }

    /// <summary>
    /// Sorts the ranges beginning at <paramref name="offset"/> by their minimum and fuses overlapping
    /// or touching ranges. Ports canonical <c>ImGuiListClipper_SortAndFuseRanges</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:3340-3355</c>).
    /// </summary>
    /// <param name="ranges">Range list to sort and fuse in place.</param>
    /// <param name="offset">Index of the first range to consider; earlier entries are left untouched.</param>
    /// <remarks>
    /// A bubble sort is used because only two or three ranges are present in practice.
    /// </remarks>
    private static void SortAndFuseRanges(List<ImGuiListClipperRange> ranges, int offset)
    {
        if (ranges.Count - offset <= 1)
            return;

        for (int sortEnd = ranges.Count - offset - 1; sortEnd > 0; --sortEnd)
        {
            for (int i = offset; i < sortEnd + offset; ++i)
            {
                if (ranges[i].Min > ranges[i + 1].Min)
                {
                    ImGuiListClipperRange swap = ranges[i];
                    ranges[i] = ranges[i + 1];
                    ranges[i + 1] = swap;
                }
            }
        }

        for (int i = 1 + offset; i < ranges.Count; i++)
        {
            ImAssert.Check(!ranges[i].PosToIndexConvert && !ranges[i - 1].PosToIndexConvert, "Position ranges must be converted before they are fused.");
            if (ranges[i - 1].Max < ranges[i].Min)
                continue;
            ranges[i - 1].Min = Math.Min(ranges[i - 1].Min, ranges[i].Min);
            ranges[i - 1].Max = Math.Max(ranges[i - 1].Max, ranges[i].Max);
            ranges.RemoveAt(i);
            i--;
        }
    }

    /// <summary>
    /// Reports whether item clipping is currently skipped because the host table hides its rows or the
    /// window itself is skipped. Mirrors canonical <c>GetSkipItemForListClipping</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:3331-3336</c>).
    /// </summary>
    /// <returns><see langword="true"/> when the clipper must submit nothing.</returns>
    private static bool GetSkipItemForListClipping()
    {
        ImGuiContext g = ImGui.GetCurrentContext()!;
        return g.CurrentTable != null ? g.CurrentTable.HostSkipItems : g.CurrentWindow!.SkipItems;
    }

    /// <summary>
    /// Reports whether a float lies outside the range in which consecutive integers are exactly
    /// representable. Mirrors canonical <c>ImIsFloatAboveGuaranteedIntegerPrecision</c>
    /// (<c>ThirdParty/imgui/imgui_internal.h:557</c>).
    /// </summary>
    /// <param name="value">Value to test.</param>
    /// <returns><see langword="true"/> when the value is at or beyond 2^24 in magnitude.</returns>
    private static bool IsFloatAboveGuaranteedIntegerPrecision(float value)
    {
        return value <= -GuaranteedIntegerPrecisionLimit || value >= GuaranteedIntegerPrecisionLimit;
    }

    /// <summary>
    /// Clamps an integer without throwing when the bounds are inverted, matching canonical
    /// <c>ImClamp</c> (<c>ThirdParty/imgui/imgui_internal.h</c>). The clipper intentionally produces
    /// inverted bounds in a few corner cases.
    /// </summary>
    /// <param name="value">Value to clamp.</param>
    /// <param name="minimum">Lower bound.</param>
    /// <param name="maximum">Upper bound.</param>
    /// <returns>The clamped value.</returns>
    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : (value > maximum ? maximum : value);
    }

    #endregion
}

#endregion
