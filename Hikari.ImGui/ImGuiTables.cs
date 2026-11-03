// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

using System.Diagnostics;
using System.Globalization;

namespace Hikari.ImGui;

/// <summary>
/// Provides immediate-mode GUI operations, context and frame lifecycle management, windowing,
/// layout, input routing, styling, widgets, tables, and docking facilities.
/// </summary>
public static partial class ImGui
{
    #region Table constants

    /// <summary>
    /// Draw-channel index that receives the table background geometry.
    /// </summary>
    private const int TABLE_DRAW_CHANNEL_BG0 = 0;

    /// <summary>
    /// Width in pixels of the table outer and inner borders.
    /// </summary>
    private const float TABLE_BORDER_SIZE = 1.0f;

    #endregion


    #region SECTION: TABLE CORE LIFECYCLE AND LAYOUT

    #region Tables: Main code

    /// <summary>
    /// Draw-channel index that receives row backgrounds, cell backgrounds, and borders.
    /// </summary>
    private const int TableDrawChannelBg0 = 0;

    /// <summary>
    /// Draw-channel index that receives background geometry above the frozen row region.
    /// </summary>
    private const int TableDrawChannelBg2Frozen = 1;

    /// <summary>
    /// Draw-channel index used for all foreground geometry when the table requests no clipping.
    /// </summary>
    private const int TableDrawChannelNoClip = 2;

    /// <summary>
    /// Thickness of the outer and inner table borders, in pixels.
    /// </summary>
    private const float TableBorderSize = 1.0f;

    /// <summary>
    /// Half-width of the interactive separator that resizes a column.
    /// </summary>
    private const float TableResizeSeparatorHalfThickness = 4.0f;

    /// <summary>
    /// Delay before the resize separator shows hover feedback, in seconds.
    /// </summary>
    private const float TableResizeSeparatorFeedbackTimer = 0.06f;

    /// <summary>
    /// Lowest host-window size accepted by <c>BeginTableEx()</c> when the caller passes no explicit
    /// outer size. ( = <c>IMGUI_WINDOW_HARD_MIN_SIZE</c>)
    /// </summary>
    private const float TableWindowHardMinSize = 4.0f;

    /// <summary>
    /// Maximum number of columns a table may declare.
    /// ( = <c>IMGUI_TABLE_MAX_COLUMNS</c>)
    /// </summary>
    private const int TableMaxColumns = 512;

    /// <summary>
    /// Packed color used by canonical <c>IM_COL32_DISABLE</c> to mean "no explicit background".
    /// </summary>
    private const uint TableColorDisable = 0x01000000u;

    /// <summary>
    /// Smallest positive normal single-precision value, matching canonical <c>FLT_MIN</c>.
    /// </summary>
    private const float FloatMin = 1.17549435e-38f;

    /// <summary>
    /// Applies the one-time flag adjustments that canonical performs when a table begins.
    /// </summary>
    /// <param name="flags">The caller-supplied table flags.</param>
    /// <param name="outerWindow">The hosting window, consulted for auto-resize and saved-settings inheritance.</param>
    /// <returns>The adjusted table flags.</returns>
    /// <remarks>
    /// The adjustments select a default sizing policy, enforce inner vertical borders for resizable
    /// tables, drop host-extension limits when scrolling is requested, give
    /// <c>NoBordersInBodyUntilResize</c> priority over <c>NoBordersInBody</c>, and inherit
    /// <c>NoSavedSettings</c> when the root host window suppresses persistence.
    /// </remarks>
    private static ImGuiTableFlags TableFixFlags(ImGuiTableFlags flags, ImGuiWindow outerWindow)
    {
        // Default sizing policy: fixed-fit for horizontal scrolling or an auto-resizing host, else
        // equal stretched columns.
        if ((flags & ImGuiTableFlags.SizingMask_) == 0)
            flags |= ((flags & ImGuiTableFlags.ScrollX) != 0 || (outerWindow.Flags & ImGuiWindowFlags.AlwaysAutoResize) != 0)
                ? ImGuiTableFlags.SizingFixedFit
                : ImGuiTableFlags.SizingStretchSame;

        // Equal fixed widths cannot guarantee that every column stays visible.
        if ((flags & ImGuiTableFlags.SizingMask_) == ImGuiTableFlags.SizingFixedSame)
            flags |= ImGuiTableFlags.NoKeepColumnsVisible;

        // Resizing separators require inner vertical borders to be visible.
        if ((flags & ImGuiTableFlags.Resizable) != 0)
            flags |= ImGuiTableFlags.BordersInnerV;

        // Scrolling tables own their extents, so host-extension limits do not apply.
        if ((flags & (ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY)) != 0)
            flags &= ~(ImGuiTableFlags.NoHostExtendX | ImGuiTableFlags.NoHostExtendY);

        if ((flags & ImGuiTableFlags.NoBordersInBodyUntilResize) != 0)
            flags &= ~ImGuiTableFlags.NoBordersInBody;

        // Nothing user-visible is persisted when the table cannot be resized, reordered, hidden or sorted.
        if ((flags & (ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Sortable)) == 0)
            flags |= ImGuiTableFlags.NoSavedSettings;

        if ((outerWindow.RootWindow!.Flags & ImGuiWindowFlags.NoSavedSettings) != 0)
            flags |= ImGuiTableFlags.NoSavedSettings;

        return flags;
    }

    /// <summary>
    /// Resolves a table from its identifier by scanning the context's table pool.
    /// </summary>
    /// <param name="id">The table identifier.</param>
    /// <returns>The matching table, or <see langword="null"/> when no table owns the identifier.</returns>
    /// <remarks>
    /// Canonical performs the lookup through <c>g.Tables.GetByKey()</c>; this port keeps tables in a
    /// list and resolves by linear scan, which is acceptable because tables are removed only by the
    /// explicit debug and garbage-collection paths.
    /// </remarks>
    internal static ImGuiTable? TableFindByID(uint id)
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.Tables.Count; n++)
        {
            if (g.Tables[n].ID == id)
                return g.Tables[n];
        }
        return null;
    }

    /// <summary>
    /// Resolves the index of a table inside the context's table pool.
    ///
    /// </summary>
    /// <param name="table">The table to locate.</param>
    /// <returns>The zero-based pool index, or <c>-1</c> when the table is not pooled.</returns>
    private static int TableGetIndex(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        return g.Tables.IndexOf(table);
    }

    /// <summary>
    /// Resolves a table from its index inside the context's table pool.
    ///
    /// </summary>
    /// <param name="index">The zero-based pool index.</param>
    /// <returns>The pooled table, or <see langword="null"/> when the index is out of range.</returns>
    private static ImGuiTable? TableGetByIndex(int index)
    {
        ImGuiContext g = GetContext();
        return index >= 0 && index < g.Tables.Count ? g.Tables[index] : null;
    }

    /// <summary>
    /// Finds the table that owns the identifier, appending a fresh table to the pool when none does.
    ///
    /// </summary>
    /// <param name="id">The table identifier.</param>
    /// <returns>The existing or newly pooled table.</returns>
    private static ImGuiTable TableGetOrAddByID(uint id)
    {
        ImGuiTable? existing = TableFindByID(id);
        if (existing != null)
            return existing;
        ImGuiContext g = GetContext();
        ImGuiTable table = new ImGuiTable();
        g.Tables.Add(table);
        return table;
    }

    /// <summary>
    /// Begins a table addressed by a string identifier.
    /// </summary>
    /// <param name="strId">String identifier hashed into the table identifier.</param>
    /// <param name="columnsCount">Number of columns the table declares.</param>
    /// <param name="flags">Table behavior flags.</param>
    /// <param name="outerSize">Requested outer size; a zero component defers to the host content region.</param>
    /// <param name="innerWidth">Horizontal scroll width when <see cref="ImGuiTableFlags.ScrollX"/> is set.</param>
    /// <returns><see langword="true"/> when the table began and <see cref="EndTable"/> must be called.</returns>
    public static bool BeginTable(string strId, int columnsCount, ImGuiTableFlags flags = 0, ImVec2 outerSize = default, float innerWidth = 0.0f)
    {
        uint id = GetID(strId);
        return BeginTableEx(strId, id, columnsCount, flags, outerSize, innerWidth);
    }

    /// <summary>
    /// Begins a table addressed by a pre-resolved identifier, acquiring pooled storage, temporary
    /// buffers, and the inner child window used for scrolling.
    /// </summary>
    /// <param name="name">String identifier used to name the inner child window.</param>
    /// <param name="id">Pre-resolved table identifier.</param>
    /// <param name="columnsCount">Number of columns the table declares.</param>
    /// <param name="flags">Table behavior flags, patched by <see cref="TableFixFlags"/>.</param>
    /// <param name="outerSize">Requested outer size; a zero component defers to the host content region.</param>
    /// <param name="innerWidth">Horizontal scroll width when <see cref="ImGuiTableFlags.ScrollX"/> is set.</param>
    /// <returns><see langword="true"/> when the table began and <see cref="EndTable"/> must be called.</returns>
    /// <remarks>
    /// <para>
    /// The function captures the host window's cursor, indent, clip and line metrics so that
    /// <see cref="EndTable"/> can restore them, and installs itself as <c>g.CurrentTable</c> so nested
    /// tables stack correctly.
    /// </para>
    /// <para>
    /// Settings load and save are realized by the settings-lifetime owner (part 04); the backing
    /// <c>.ini</c> serialization handlers are owned by Chunk 13 and are inert until that chunk lands.
    /// </para>
    /// </remarks>
    internal static bool BeginTableEx(string name, uint id, int columnsCount, ImGuiTableFlags flags, ImVec2 outerSize, float innerWidth)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow outerWindow = GetCurrentWindow();
        // A host window that skips items suppresses the table entirely, which also makes a mismatched
        // EndTable() assertion more visible.
        if (outerWindow.SkipItems)
            return false;

        ImAssert.Check(columnsCount > 0 && columnsCount < TableMaxColumns);
        if ((flags & ImGuiTableFlags.ScrollX) != 0)
            ImAssert.Check(innerWidth >= 0.0f);

        // A scrolling table owns a child window so it can scroll independently of its host. An explicit
        // outer size lets a fully clipped table early out before its pooled data is touched.
        bool useChildWindow = (flags & (ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY)) != 0;
        ImVec2 availSize = GetContentRegionAvail();
        ImVec2 actualOuterSize = TruncateVector(CalcItemSize(
            outerSize,
            MathF.Max(availSize.X, TableWindowHardMinSize),
            useChildWindow ? MathF.Max(availSize.Y, TableWindowHardMinSize) : 0.0f));
        ImVec2 cursorPos = outerWindow.DC.CursorPos;
        ImRect outerRect = new(cursorPos, cursorPos + actualOuterSize);
        bool outerWindowIsMeasuringSize = outerWindow.AutoFitFramesX > 0 || outerWindow.AutoFitFramesY > 0;
        if (useChildWindow && IsClippedEx(outerRect, 0) && !outerWindowIsMeasuringSize)
        {
            ItemSize(outerRect);
            ItemAdd(outerRect, id);
            g.NextWindowData.ClearFlags();
            return false;
        }

        if (g.DebugBreakInTable == id)
            System.Diagnostics.Debugger.Break();

        // Acquire pooled table state and one level of temporary data. Temporary data is stacked so
        // nested tables each keep their own draw splitter and host-window backups.
        ImGuiTable table = TableGetOrAddByID(id);
        int tableIdx = TableGetIndex(table);
        g.TablesTempDataStacked++;
        if (g.TablesTempDataStacked > g.TablesTempData.Count)
            g.TablesTempData.Add(new ImGuiTableTempData());
        ImGuiTableTempData tempData = table.TempData = g.TablesTempData[g.TablesTempDataStacked - 1];
        tempData.TableIndex = tableIdx;
        tempData.ReconcileColumnsRequests.Clear();
        table.DrawSplitter = tempData.DrawSplitter;
        table.DrawSplitter.Clear();

        // Patch flags and initialize the per-frame fields. The instance number distinguishes several
        // tables sharing one identifier within a single frame.
        table.IsDefaultSizingPolicy = (flags & ImGuiTableFlags.SizingMask_) == 0;
        flags = TableFixFlags(flags, outerWindow);

        int previousFrameActive = table.LastFrameActive;
        int instanceNo = (previousFrameActive != g.FrameCount) ? 0 : table.InstanceCurrent + 1;
        ImGuiTableFlags previousFlags = table.Flags;
        bool isNewTable = previousFrameActive == -1;
        table.ID = id;
        table.Flags = flags;
        table.LastFrameActive = g.FrameCount;
        table.OuterWindow = table.InnerWindow = outerWindow;
        table.ColumnsCount = columnsCount;
        table.IsLayoutLocked = false;
        table.InnerWidth = innerWidth;
        table.NavLayer = (sbyte)outerWindow.DC.NavLayerCurrent;
        table.IsNewTable = previousFrameActive == -1;
        tempData.UserOuterSize = outerSize;

        // Instance data: instance zero uses the table identifier itself; later instances derive a
        // stable seed so their resize identifiers do not collide.
        uint instanceId;
        table.InstanceCurrent = (short)instanceNo;
        if (instanceNo > 0)
        {
            ImAssert.Check(table.ColumnsCount == columnsCount, "BeginTable(): Cannot change columns count mid-frame while preserving same ID");
            while (table.InstanceDataExtra.Count < instanceNo)
                table.InstanceDataExtra.Add(new ImGuiTableInstanceData());
            instanceId = GetIDWithSeed(instanceNo, GetIDWithSeed("##Instances", null, id));
        }
        else
        {
            instanceId = id;
        }
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        tableInstance.TableInstanceID = instanceId;

        // When not using a child window, WorkRect.Max grows as content is appended.
        if (useChildWindow)
        {
            // Suppress the vertical scrollbar when only horizontal scrolling is wanted. A tiny positive
            // content height reproduces canonical FLT_MIN because a zero height would be treated as
            // "unspecified".
            ImVec2 overrideContentSize = new(float.MaxValue, float.MaxValue);
            if ((flags & ImGuiTableFlags.ScrollX) != 0 && (flags & ImGuiTableFlags.ScrollY) == 0)
                overrideContentSize.Y = FloatMin;
            if ((flags & ImGuiTableFlags.ScrollX) != 0 && innerWidth > 0.0f)
                overrideContentSize.X = innerWidth;
            if (overrideContentSize.X != float.MaxValue || overrideContentSize.Y != float.MaxValue)
            {
                SetNextWindowContentSize(new ImVec2(
                    overrideContentSize.X != float.MaxValue ? overrideContentSize.X : 0.0f,
                    overrideContentSize.Y != float.MaxValue ? overrideContentSize.Y : 0.0f));
            }

            // Reset the scroll offset when scrolling is being (re)activated.
            if ((previousFlags & (ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY)) == 0)
            {
                if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasScroll) == 0)
                    SetNextWindowScroll(new ImVec2(0.0f, 0.0f));
            }

            // Create the scrolling region with no border and no window padding, forwarding any child or
            // window flags the caller queued.
            ImGuiChildFlags childChildFlags = (g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasChildFlags) != 0
                ? g.NextWindowData.ChildFlags
                : ImGuiChildFlags.None;
            ImGuiWindowFlags childWindowFlags = (g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasWindowFlags) != 0
                ? g.NextWindowData.WindowFlags
                : ImGuiWindowFlags.None;
            if ((flags & ImGuiTableFlags.ScrollX) != 0)
                childWindowFlags |= ImGuiWindowFlags.HorizontalScrollbar;
            BeginChildEx(name, instanceId, outerRect.GetSize(), childChildFlags, childWindowFlags);

            ImGuiWindow innerWindow = table.InnerWindow = GetCurrentWindow();
            table.WorkRect = innerWindow.WorkRect;
            table.OuterRect = innerWindow.Rect();
            table.InnerRect = innerWindow.InnerRect;
            ImAssert.Check(
                innerWindow.WindowPadding.X == 0.0f && innerWindow.WindowPadding.Y == 0.0f && innerWindow.WindowBorderSize == 0.0f,
                "BeginTable(): child window must have zero padding and border.");

            // Allow submission while the host window is auto-fitting its size.
            if (innerWindow.SkipItems && outerWindowIsMeasuringSize)
                innerWindow.SkipItems = false;

            // Keep the decoration width of several instances consistent so stretched columns align.
            if (instanceNo == 0)
            {
                table.HasScrollbarYPrev = table.HasScrollbarYCurr;
                table.HasScrollbarYCurr = false;
            }
            table.HasScrollbarYCurr |= innerWindow.ScrollbarY;
        }
        else
        {
            // For non-scrolling tables WorkRect == OuterRect == InnerRect. The final height is only
            // known in EndTable().
            table.WorkRect = table.OuterRect = table.InnerRect = outerRect;
            table.HasScrollbarYPrev = table.HasScrollbarYCurr = false;
            table.InnerWindow!.DC.TreeDepth++; // Links tree lines across a table.
        }

        // A standardized identifier scope for child-using and non-child-using tables.
        PushOverrideID(id);
        if (instanceNo > 0)
            PushOverrideID(instanceId);

        // Backup the host-window members the table is about to modify.
        ImGuiWindow hostWindow = table.InnerWindow!;
        table.HostIndentX = hostWindow.DC.Indent.X;
        table.HostClipRect = hostWindow.ClipRect;
        table.HostSkipItems = hostWindow.SkipItems;
        tempData.WindowID = hostWindow.ID;
        tempData.HostBackupWorkRect = hostWindow.WorkRect;
        tempData.HostBackupParentWorkRect = hostWindow.ParentWorkRect;
        tempData.HostBackupColumnsOffset = outerWindow.DC.ColumnsOffset;
        tempData.HostBackupPrevLineSize = hostWindow.DC.PrevLineSize;
        tempData.HostBackupCurrLineSize = hostWindow.DC.CurrLineSize;
        tempData.HostBackupCursorMaxPos = hostWindow.DC.CursorMaxPos;
        tempData.HostBackupItemWidth = outerWindow.DC.ItemWidth;
        tempData.HostBackupItemWidthStackSize = outerWindow.DC.ItemWidthStack.Count;
        hostWindow.DC.PrevLineSize = hostWindow.DC.CurrLineSize = new ImVec2(0.0f, 0.0f);

        // Keep the outer borders from overlapping content in a scrolling table by shrinking the host
        // clip rectangle. Non-scrolling tables rely on TableMergeDrawChannels() instead.
        if (hostWindow != outerWindow)
        {
            const float borderSize = TableBorderSize;
            ImRect hostClipRect = table.HostClipRect;
            if ((flags & ImGuiTableFlags.BordersOuterV) != 0)
            {
                hostClipRect.Min.X = MathF.Min(hostClipRect.Min.X + borderSize, hostClipRect.Max.X);
                if (hostWindow.DecoOuterSizeX2 == 0.0f)
                    hostClipRect.Max.X = MathF.Max(hostClipRect.Max.X - borderSize, hostClipRect.Min.X);
            }
            if ((flags & ImGuiTableFlags.BordersOuterH) != 0)
            {
                hostClipRect.Min.Y = MathF.Min(hostClipRect.Min.Y + borderSize, hostClipRect.Max.Y);
                if (hostWindow.DecoOuterSizeY2 == 0.0f)
                    hostClipRect.Max.Y = MathF.Max(hostClipRect.Max.Y - borderSize, hostClipRect.Min.Y);
            }
            table.HostClipRect = hostClipRect;
        }

        // Padding and spacing. Outer padding is omitted by default, inner padding is present unless
        // inner borders already provide the separation.
        bool padOuterX = (flags & ImGuiTableFlags.NoPadOuterX) != 0
            ? false
            : (flags & ImGuiTableFlags.PadOuterX) != 0 || (flags & ImGuiTableFlags.BordersOuterV) != 0;
        bool padInnerX = (flags & ImGuiTableFlags.NoPadInnerX) == 0;
        float innerSpacingForBorder = (flags & ImGuiTableFlags.BordersInnerV) != 0 ? TableBorderSize : 0.0f;
        float innerSpacingExplicit = (padInnerX && (flags & ImGuiTableFlags.BordersInnerV) == 0) ? g.Style.CellPadding.X : 0.0f;
        float innerPaddingExplicit = (padInnerX && (flags & ImGuiTableFlags.BordersInnerV) != 0) ? g.Style.CellPadding.X : 0.0f;
        table.CellSpacingX1 = innerSpacingExplicit + innerSpacingForBorder;
        table.CellSpacingX2 = innerSpacingExplicit;
        table.CellPaddingX = innerPaddingExplicit;

        float outerPaddingForBorder = (flags & ImGuiTableFlags.BordersOuterV) != 0 ? TableBorderSize : 0.0f;
        float outerPaddingExplicit = padOuterX ? g.Style.CellPadding.X : 0.0f;
        table.OuterPaddingX = (outerPaddingForBorder + outerPaddingExplicit) - table.CellPaddingX;

        table.CurrentColumn = -1;
        table.CurrentRow = -1;
        table.RowBgColorCounter = 0;
        table.LastRowFlags = ImGuiTableRowFlags.None;
        ImRect innerClipRect = (hostWindow == outerWindow) ? table.WorkRect : hostWindow.ClipRect;
        innerClipRect.ClipWith(table.WorkRect); // Honors inner_width.
        innerClipRect.ClipWithFull(table.HostClipRect);
        innerClipRect.Max.Y = (flags & ImGuiTableFlags.NoHostExtendY) != 0
            ? MathF.Min(innerClipRect.Max.Y, hostWindow.WorkRect.Max.Y)
            : table.HostClipRect.Max.Y;
        table.InnerClipRect = innerClipRect;

        table.RowPosY1 = table.RowPosY2 = table.WorkRect.Min.Y;
        table.RowTextBaseline = 0.0f; // Cleared again by TableBeginRow().
        table.RowCellPaddingY = 0.0f;
        table.FreezeRowsRequest = table.FreezeRowsCount = 0; // Configured by TableSetupScrollFreeze().
        table.FreezeColumnsRequest = table.FreezeColumnsCount = 0;
        table.IsUnfrozenRows = true;
        table.DeclColumnsCount = table.AngledHeadersCount = 0;
        if (previousFrameActive + 1 < g.FrameCount)
            table.IsActiveIdInTable = false;
        table.AngledHeadersHeight = 0.0f;
        tempData.AngledHeadersExtraWidth = 0.0f;

        // Opaque colors let overlapping grid lines render without seams.
        table.BorderColorStrong = GetColorU32(ImGuiCol.TableBorderStrong);
        table.BorderColorLight = GetColorU32(ImGuiCol.TableBorderLight);

        // Make the table ambient so later table calls resolve it, and record its pool index in the host
        // window so SetCurrentWindow() can restore it.
        g.CurrentTable = table;
        hostWindow.DC.NavIsScrollPushableX = false;
        outerWindow.DC.CurrentTableIdx = tableIdx;
        if (hostWindow != outerWindow)
            hostWindow.DC.CurrentTableIdx = tableIdx;

        if ((previousFlags & ImGuiTableFlags.Reorderable) != 0 && (flags & ImGuiTableFlags.Reorderable) == 0)
            table.IsResetDisplayOrderRequest = true;

        // Mark the table as used for garbage collection.
        while (g.TablesLastTimeActive.Count <= tableIdx)
            g.TablesLastTimeActive.Add(-1.0f);
        g.TablesLastTimeActive[tableIdx] = (float)g.Time;
        tempData.LastTimeActive = (float)g.Time;
        table.MemoryCompacted = false;

        // Detach the previous column storage when the declared column count changes so that
        // TableBeginInitMemory() can rebuild it and TableReconcileColumns() can match by column ID.
        int oldColumnsCount = table.Columns.Count;
        if (oldColumnsCount != 0 && oldColumnsCount != columnsCount)
        {
            tempData.OldColumnsRawData = table.RawData; // Inert sentinel; the list below carries the data.
            tempData.OldColumnsData = table.Columns; // Keep the previous list itself.
            for (int n = 0; n < tempData.OldColumnsData.Count; n++)
                tempData.OldColumnsData[n].IsNeedReconcileSrc = true;
            table.RawData = IntPtr.Zero;
            table.Columns = new List<ImGuiTableColumn>();
        }
        if (table.Columns.Count == 0)
        {
            TableBeginInitMemory(table, columnsCount);
            table.IsInitializing = true;
        }
        if (table.IsResetAllRequest)
            TableResetSettings(table);
        if (table.IsInitializing)
        {
            if (isNewTable)
            {
                table.SettingsOffset = -1;
                table.IsSettingsRequestLoad = true;
            }
            table.IsSortSpecsDirty = true;
            table.IsSettingsDirty = true; // Records itself into the layout file even in default state.
            table.IsReconcileMode = false;
            table.InstanceInteracted = -1;
            table.ContextPopupColumn = -1;
            table.ReorderColumn = table.ReorderColumnDstOrder = table.ResizedColumn = table.LastResizedColumn = -1;
            table.AutoFitSingleColumn = -1;
            table.HoveredColumnBody = table.HoveredColumnBorder = -1;
            for (int n = 0; n < columnsCount; n++)
            {
                ImGuiTableColumn column = table.Columns[n];
                if (tempData.OldColumnsData.Count > 0 && n < tempData.OldColumnsData.Count)
                {
                    // Preserve the previous frame's column data across a topology change; the reconcile
                    // path matches it later by column identifier.
                    column.CopyFrom(tempData.OldColumnsData[n]);
                }
                else
                {
                    float widthAuto = column.WidthAuto;
                    column.CopyFrom(new ImGuiTableColumn());
                    column.WidthAuto = widthAuto;
                    column.IsPreserveWidthAuto = true; // Avoids a visible flicker when reinitializing.
                    column.IsEnabled = column.IsUserEnabled = column.IsUserEnabledNextFrame = true;
                    column.DisplayOrder = (short)n;
                }
                table.DisplayOrderToIndex[n] = column.DisplayOrder;
            }
        }

        // Load settings.
        if (table.IsSettingsRequestLoad)
            TableLoadSettings(table);

        // Suppress output until TableNextRow()/TableNextColumn() locks the layout, so stray output
        // between BeginTable() and the first row cannot leak into the table.
        hostWindow.SkipItems = true;

        // Column name offsets are invalid until layout or the first TableSetupColumn() call.
        if (table.ColumnsNames.Size > 0)
            table.ColumnsNames.Clear();

        return true;
    }

    /// <summary>
    /// Allocates the per-table column, display-order, cell-data, and mask storage.
    /// </summary>
    /// <param name="table">Table whose storage is initialized.</param>
    /// <param name="columnsCount">Number of columns to allocate.</param>
    /// <remarks>
    /// Canonical packs all buffers into one arena referenced by <c>RawData</c>. This port keeps each
    /// buffer as a separately owned collection, so <c>RawData</c> stays an opaque zero handle and the
    /// column list itself carries the initialization state. Every buffer is replaced rather than
    /// cleared in place so a detached previous-frame snapshot survives.
    /// </remarks>
    internal static void TableBeginInitMemory(ImGuiTable table, int columnsCount)
    {
        List<ImGuiTableColumn> columns = new(columnsCount);
        List<short> displayOrderToIndex = new(columnsCount);
        List<ImGuiTableCellData> rowCellData = new(columnsCount);
        for (int n = 0; n < columnsCount; n++)
        {
            columns.Add(new ImGuiTableColumn());
            displayOrderToIndex.Add(0);
            rowCellData.Add(new ImGuiTableCellData());
        }
        table.Columns = columns;
        table.DisplayOrderToIndex = displayOrderToIndex;
        table.RowCellData = rowCellData;
        table.EnabledMaskByDisplayOrder.Create(columnsCount);
        table.EnabledMaskByIndex.Create(columnsCount);
        table.VisibleMaskByIndex.Create(columnsCount);
    }

    /// <summary>
    /// Applies the resize, reorder, display-order reset, and visibility reset requests queued by the
    /// previous frame or by user interaction.
    /// </summary>
    /// <param name="table">Table whose queued requests are applied.</param>
    /// <remarks>
    /// Requests are processed only for instance zero so that several tables sharing one identifier
    /// apply each interaction exactly once. The display-order and visibility requests are raised by
    /// the context menu owned by part 03.
    /// </remarks>
    internal static void TableApplyQueuedRequests(ImGuiTable table)
    {
        if (table.InstanceCurrent == 0)
        {
            if (table.ResizedColumn != -1 && table.ResizedColumnNextWidth != float.MaxValue)
                TableSetColumnWidth(table.ResizedColumn, table.ResizedColumnNextWidth);
            table.LastResizedColumn = table.ResizedColumn;
            table.ResizedColumnNextWidth = float.MaxValue;
            table.ResizedColumn = -1;

            // A single-column auto-fit is a special case for stretch columns and fixed columns under the
            // equal-width policy.
            if (table.AutoFitSingleColumn != -1)
            {
                TableSetColumnWidth(table.AutoFitSingleColumn, table.Columns[table.AutoFitSingleColumn].WidthAuto);
                table.AutoFitSingleColumn = -1;
            }
        }

        if (table.InstanceCurrent == 0)
        {
            table.LastHeldHeaderColumn = table.HeldHeaderColumn;
            table.HeldHeaderColumn = -1;
            if (table.ReorderColumn != -1 && table.ReorderColumnDstOrder != -1)
            {
                TableSetColumnDisplayOrder(table, table.ReorderColumn, table.ReorderColumnDstOrder);
                table.ReorderColumnDstOrder = -1;
            }

            // Release the reorder interaction once the mouse is no longer held.
            ImGuiContext g = GetContext();
            if (g.ActiveId == 0)
                table.ReorderColumn = -1;
        }

        if (table.IsResetDisplayOrderRequest)
        {
            for (int n = 0; n < table.ColumnsCount; n++)
                table.DisplayOrderToIndex[n] = table.Columns[n].DisplayOrder = (short)n;
            table.IsResetDisplayOrderRequest = false;
            table.IsSettingsDirty = true;
        }
        if (table.IsResetVisibilityRequest)
        {
            for (int n = 0; n < table.Columns.Count; n++)
            {
                ImGuiTableColumn column = table.Columns[n];
                column.IsUserEnabled = column.IsUserEnabledNextFrame = (column.Flags & ImGuiTableColumnFlags.DefaultHide) == 0;
            }
            table.IsResetVisibilityRequest = false;
            table.IsSettingsDirty = true;
        }
    }

    /// <summary>
    /// Moves a column to a new display order immediately, shifting the columns in between.
    /// </summary>
    /// <param name="table">Table being reordered.</param>
    /// <param name="columnN">Index of the column being moved.</param>
    /// <param name="dstOrder">Destination display order.</param>
    /// <remarks>
    /// The display order is stored both on each column and in the table's order-to-index map; the map
    /// is rebuilt from the columns after the shift. Interactive callers should prefer
    /// <see cref="TableQueueSetColumnDisplayOrder"/>, which applies the frozen-column and
    /// <c>NoReorder</c> constraints.
    /// </remarks>
    internal static void TableSetColumnDisplayOrder(ImGuiTable table, int columnN, int dstOrder)
    {
        ImAssert.Check(columnN >= 0 && columnN < table.ColumnsCount);
        ImAssert.Check(dstOrder >= 0 && dstOrder < table.ColumnsCount);

        ImGuiTableColumn srcColumn = table.Columns[columnN];
        int srcOrder = srcColumn.DisplayOrder;
        if (srcOrder == dstOrder)
            return;
        int reorderDir = (dstOrder < srcOrder) ? -1 : +1;

        srcColumn.DisplayOrder = (short)dstOrder;
        for (int orderN = srcOrder + reorderDir; orderN != dstOrder + reorderDir; orderN += reorderDir)
            table.Columns[table.DisplayOrderToIndex[orderN]].DisplayOrder -= (short)reorderDir;

        for (int n = 0; n < table.ColumnsCount; n++)
            table.DisplayOrderToIndex[table.Columns[n].DisplayOrder] = (short)n;
        table.IsSettingsDirty = true;
    }

    /// <summary>
    /// Clamps a requested display order to the range that interactive reordering may reach.
    /// </summary>
    /// <param name="table">Table being reordered.</param>
    /// <param name="srcOrder">Current display order of the column.</param>
    /// <param name="dstOrder">Requested display order.</param>
    /// <returns>The clamped display order.</returns>
    /// <remarks>
    /// Reordering may not cross the frozen-column barrier, because frozen columns must stay within
    /// their leading display-order range, and it stops before any column tagged
    /// <see cref="ImGuiTableColumnFlags.NoReorder"/>.
    /// </remarks>
    private static int TableGetMaxDisplayOrderAllowed(ImGuiTable table, int srcOrder, int dstOrder)
    {
        dstOrder = Math.Clamp(dstOrder, 0, table.ColumnsCount - 1);
        if (srcOrder == dstOrder)
            return dstOrder;

        if (table.FreezeColumnsRequest > 0)
            dstOrder = (srcOrder < table.FreezeColumnsRequest)
                ? Math.Min(dstOrder, table.FreezeColumnsRequest - 1)
                : Math.Max(dstOrder, table.FreezeColumnsRequest);

        int reorderDir = (srcOrder < dstOrder) ? +1 : -1;
        for (int orderN = srcOrder; (srcOrder < dstOrder && orderN <= dstOrder) || (dstOrder < srcOrder && orderN >= dstOrder); orderN += reorderDir)
        {
            if ((table.Columns[table.DisplayOrderToIndex[orderN]].Flags & ImGuiTableColumnFlags.NoReorder) != 0)
            {
                dstOrder = (orderN == srcOrder) ? srcOrder : orderN - reorderDir;
                break;
            }
        }
        return dstOrder;
    }

    /// <summary>
    /// Queues a display-order change requested by user interaction, applying the reachable-range
    /// constraints.
    /// </summary>
    /// <param name="table">Table being reordered.</param>
    /// <param name="columnN">Index of the column being moved.</param>
    /// <param name="dstOrder">Requested destination display order.</param>
    /// <remarks>
    /// The request is stored rather than applied so the move lands on a layout boundary. A request
    /// that resolves to the current order before layout is still recorded, because a reconcile pass
    /// may legitimately call this function first.
    /// </remarks>
    internal static void TableQueueSetColumnDisplayOrder(ImGuiTable table, int columnN, int dstOrder)
    {
        int srcOrder = table.Columns[columnN].DisplayOrder;
        table.ReorderColumn = (short)columnN;
        table.ReorderColumnDstOrder = -1;
        dstOrder = TableGetMaxDisplayOrderAllowed(table, srcOrder, dstOrder);
        if (table.IsLayoutLocked && dstOrder == srcOrder)
            return;
        table.ReorderColumnDstOrder = (short)dstOrder;
    }

    /// <summary>
    /// Locks column states and positions for the frame: reconciles moved columns, applies enabled and
    /// display-order state, computes requested and final widths, resolves clipping and hover, sets up
    /// draw channels, and runs the default context menu and sort rebuild.
    /// </summary>
    /// <param name="table">Table being laid out; its <c>IsLayoutLocked</c> flag must be clear.</param>
    /// <remarks>
    /// <para>
    /// The function is invoked on the first <c>TableNextRow()</c>/<c>TableNextColumn()</c> call, or
    /// from <see cref="EndTable"/> when the user never advanced to a row, so column setup calls and
    /// settings have already been submitted.
    /// </para>
    /// <para>
    /// The canonical structure and ordering of the thirteen layout parts are preserved exactly.
    /// </para>
    /// </remarks>
    internal static void TableUpdateLayout(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        ImGuiTableTempData tempData = table.TempData!;
        ImAssert.Check(!table.IsLayoutLocked);
        int columnsCount = table.ColumnsCount;

        // Reconcile columns that moved during a topology change, then release the detached snapshot.
        if (tempData.ReconcileColumnsRequests.Count > 0)
            TableReconcileColumns(table);
        if (tempData.OldColumnsData.Count > 0)
        {
            tempData.OldColumnsRawData = IntPtr.Zero;
            tempData.OldColumnsData = new List<ImGuiTableColumn>();
        }

        // Apply columns settings.
        if (table.IsSettingsRequestLoad)
            TableLoadSettingsForColumns(table);
        if (table.IsInitializing || table.IsSettingsRequestLoad)
        {
            for (int n = 0; n < table.Columns.Count; n++)
            {
                ImGuiTableColumn column = table.Columns[n];
                ImGuiTableFlags initFlags;
                if (table.IsSettingsRequestLoad)
                    initFlags = column.IsLoadedSettings ? ~table.SettingsLoadedFlags : ~(ImGuiTableFlags)0;
                else
                    initFlags = column.IsJustCreated ? ~(ImGuiTableFlags)0 : 0;
                TableInitColumnDefaults(table, column, initFlags);
            }
            TableFixDisplayOrder(table); // Runs even for non-reorderable tables because settings may reorder.
            table.IsSettingsRequestLoad = false;
        }

        TableApplyQueuedRequests(table);

        // DPI/font-resize handling: scale stored width requests so the layout remains proportionate.
        float newRefScaleUnit = g.FontSize;
        if (table.RefScale != 0.0f && table.RefScale != newRefScaleUnit)
        {
            float scaleFactor = newRefScaleUnit / table.RefScale;
            for (int n = 0; n < columnsCount; n++)
                table.Columns[n].WidthRequest *= scaleFactor;
        }
        table.RefScale = newRefScaleUnit;

        ImGuiTableFlags tableSizingPolicy = table.Flags & ImGuiTableFlags.SizingMask_;
        table.IsDefaultDisplayOrder = table.IsDefaultVisibility = true;
        table.ColumnsEnabledCount = 0;
        for (int n = 0; n < columnsCount; n++)
        {
            table.EnabledMaskByIndex.ClearBit(n);
            table.EnabledMaskByDisplayOrder.ClearBit(n);
        }
        table.LeftMostEnabledColumn = -1;
        table.MinColumnWidth = MathF.Max(1.0f, g.Style.FramePadding.X * 1.0f);

        // [Part 1] Apply/lock enabled and order state, calculate auto/ideal widths, and count
        // fixed/stretch columns. Columns are processed in display order to build the prev/next links.
        int countFixed = 0;
        int countStretch = 0;
        int prevVisibleColumnIdx = -1;
        bool hasAutoFitRequest = false;
        bool hasResizable = false;
        float stretchSumWidthAuto = 0.0f;
        float fixedMaxWidthAuto = 0.0f;
        for (int orderN = 0; orderN < columnsCount; orderN++)
        {
            int columnN = table.DisplayOrderToIndex[orderN];
            ImGuiTableColumn column = table.Columns[columnN];

            // Columns not resubmitted through TableSetupColumn() this frame are reset to defaults.
            if (table.DeclColumnsCount <= columnN)
            {
                TableSetupColumnFlags(table, column, ImGuiTableColumnFlags.None);
                column.NameOffset = -1;
                column.ID = column.UserData = 0;
                column.InitStretchWeightOrWidth = -1.0f;
            }

            // Update enabled state and mark settings and sort specs dirty when it changes.
            if ((table.Flags & ImGuiTableFlags.Hideable) == 0 || (column.Flags & ImGuiTableColumnFlags.NoHide) != 0)
                column.IsUserEnabledNextFrame = true;
            if (column.IsUserEnabled != column.IsUserEnabledNextFrame)
            {
                column.IsUserEnabled = column.IsUserEnabledNextFrame;
                table.IsSettingsDirty = true;
            }
            column.IsEnabled = column.IsUserEnabled && (column.Flags & ImGuiTableColumnFlags.Disabled) == 0;
            column.IsJustCreated = false;

            if (column.IsEnabled != ((column.Flags & ImGuiTableColumnFlags.DefaultHide) == 0))
                table.IsDefaultVisibility = false;
            if (columnN != orderN)
                table.IsDefaultDisplayOrder = false;

            if (column.SortOrder != -1 && !column.IsEnabled)
                table.IsSortSpecsDirty = true;
            if (column.SortOrder > 0 && (table.Flags & ImGuiTableFlags.SortMulti) == 0)
                table.IsSortSpecsDirty = true;

            // Queue an auto-fit for columns without an explicit size.
            bool startAutoFit = (column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0
                ? column.WidthRequest < 0.0f
                : column.StretchWeight < 0.0f;
            if (startAutoFit)
                column.AutoFitQueue = column.CannotSkipItemsQueue = (1 << 3) - 1; // Fit for three frames.

            if (!column.IsEnabled)
            {
                column.IndexWithinEnabledSet = -1;
                continue;
            }

            // Mark the column enabled and link it to its neighbors in the enabled set.
            column.PrevEnabledColumn = (short)prevVisibleColumnIdx;
            column.NextEnabledColumn = -1;
            if (prevVisibleColumnIdx != -1)
                table.Columns[prevVisibleColumnIdx].NextEnabledColumn = (short)columnN;
            else
                table.LeftMostEnabledColumn = (short)columnN;
            column.IndexWithinEnabledSet = table.ColumnsEnabledCount++;
            table.EnabledMaskByIndex.SetBit(columnN);
            table.EnabledMaskByDisplayOrder.SetBit(column.DisplayOrder);
            prevVisibleColumnIdx = columnN;
            ImAssert.Check(column.IndexWithinEnabledSet <= column.DisplayOrder);

            // Combine content width from regular rows and headers unless headers are excluded.
            if (!column.IsPreserveWidthAuto && table.InstanceCurrent == 0)
                column.WidthAuto = TableGetColumnWidthAuto(table, column);

            bool columnIsResizable = (column.Flags & ImGuiTableColumnFlags.NoResize) == 0;
            if (columnIsResizable)
                hasResizable = true;
            if ((column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0 && column.InitStretchWeightOrWidth > 0.0f && !columnIsResizable)
                column.WidthAuto = column.InitStretchWeightOrWidth;

            if (column.AutoFitQueue != 0x00)
                hasAutoFitRequest = true;
            if ((column.Flags & ImGuiTableColumnFlags.WidthStretch) != 0)
            {
                stretchSumWidthAuto += column.WidthAuto;
                countStretch++;
            }
            else
            {
                fixedMaxWidthAuto = MathF.Max(fixedMaxWidthAuto, column.WidthAuto);
                countFixed++;
            }
        }
        if ((table.Flags & ImGuiTableFlags.Sortable) != 0 && table.SortSpecsCount == 0 && (table.Flags & ImGuiTableFlags.SortTristate) == 0)
            table.IsSortSpecsDirty = true;
        table.RightMostEnabledColumn = (short)prevVisibleColumnIdx;
        ImAssert.Check(table.LeftMostEnabledColumn >= 0 && table.RightMostEnabledColumn >= 0);

        // [Part 2] Disable child-window clipping while fitting columns so the first visible frame is not
        // required for auto-fit to converge.
        if (hasAutoFitRequest && table.OuterWindow != table.InnerWindow)
            table.InnerWindow!.SkipItems = false;
        if (hasAutoFitRequest)
            table.IsSettingsDirty = true;

        // [Part 3] Fix column flags, latch width requests, and record stretched-column extremes.
        float sumWidthRequests = 0.0f;
        float stretchSumWeights = 0.0f;
        table.LeftMostStretchedColumn = table.RightMostStretchedColumn = -1;
        for (int columnN = 0; columnN < columnsCount; columnN++)
        {
            if (!table.EnabledMaskByIndex.TestBit(columnN))
                continue;
            ImGuiTableColumn column = table.Columns[columnN];

            bool columnIsResizable = (column.Flags & ImGuiTableColumnFlags.NoResize) == 0;
            if ((column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0)
            {
                // Apply the equal-width policy for columns that are still auto-fitting or that cannot be
                // resized.
                float widthAuto = column.WidthAuto;
                if (tableSizingPolicy == ImGuiTableFlags.SizingFixedSame && (column.AutoFitQueue != 0x00 || !columnIsResizable))
                    widthAuto = fixedMaxWidthAuto;

                if (column.AutoFitQueue != 0x00)
                    column.WidthRequest = widthAuto;
                else if ((column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0 && !columnIsResizable && column.IsRequestOutput)
                    column.WidthRequest = widthAuto;

                // Increase the minimum size during the init frame to bias auto-fitting widgets such as
                // wrapped text less severely.
                if (column.AutoFitQueue > 0x01 && table.IsInitializing && !column.IsPreserveWidthAuto)
                    column.WidthRequest = MathF.Max(column.WidthRequest, table.MinColumnWidth * 4.0f);
                sumWidthRequests += column.WidthRequest;
            }
            else
            {
                // Initialize the stretch weight from an explicit value, a proportional share of the auto
                // widths, or an equal share.
                if (column.AutoFitQueue != 0x00 || column.StretchWeight < 0.0f || !columnIsResizable)
                {
                    if (column.InitStretchWeightOrWidth > 0.0f)
                        column.StretchWeight = column.InitStretchWeightOrWidth;
                    else if (tableSizingPolicy == ImGuiTableFlags.SizingStretchProp)
                        column.StretchWeight = (column.WidthAuto / stretchSumWidthAuto) * countStretch;
                    else
                        column.StretchWeight = 1.0f;
                }

                stretchSumWeights += column.StretchWeight;
                if (table.LeftMostStretchedColumn == -1 || table.Columns[table.LeftMostStretchedColumn].DisplayOrder > column.DisplayOrder)
                    table.LeftMostStretchedColumn = (short)columnN;
                if (table.RightMostStretchedColumn == -1 || table.Columns[table.RightMostStretchedColumn].DisplayOrder < column.DisplayOrder)
                    table.RightMostStretchedColumn = (short)columnN;
            }
            column.IsPreserveWidthAuto = false;
            sumWidthRequests += table.CellPaddingX * 2.0f;
        }
        table.ColumnsEnabledFixedCount = (short)countFixed;
        table.ColumnsStretchSumWeights = stretchSumWeights;

        // [Part 4] Apply final widths based on requested widths.
        ImRect workRect = table.WorkRect;
        float widthSpacings = (table.OuterPaddingX * 2.0f) + (table.CellSpacingX1 + table.CellSpacingX2) * (table.ColumnsEnabledCount - 1);
        float widthRemoved = (table.HasScrollbarYPrev && !table.InnerWindow!.ScrollbarY) ? g.Style.ScrollbarSize : 0.0f;
        float widthAvail = MathF.Max(1.0f,
            (((table.Flags & ImGuiTableFlags.ScrollX) != 0 && table.InnerWidth == 0.0f) ? table.InnerClipRect.GetWidth() : workRect.GetWidth()) - widthRemoved);
        float widthAvailForStretchedColumns = widthAvail - widthSpacings - sumWidthRequests;
        float widthRemainingForStretchedColumns = widthAvailForStretchedColumns;
        table.ColumnsGivenWidth = widthSpacings + (table.CellPaddingX * 2.0f) * table.ColumnsEnabledCount;
        for (int columnN = 0; columnN < columnsCount; columnN++)
        {
            if (!table.EnabledMaskByIndex.TestBit(columnN))
                continue;
            ImGuiTableColumn column = table.Columns[columnN];

            if ((column.Flags & ImGuiTableColumnFlags.WidthStretch) != 0)
            {
                float weightRatio = column.StretchWeight / stretchSumWeights;
                column.WidthRequest = TruncateToInt(MathF.Max(widthAvailForStretchedColumns * weightRatio, table.MinColumnWidth) + 0.01f);
                widthRemainingForStretchedColumns -= column.WidthRequest;
            }

            // [Resize Rule 1] The right-most visible column cannot be resized when a stretch column
            // exists, because its resize would have no sibling to absorb the change.
            if (column.NextEnabledColumn == -1 && table.LeftMostStretchedColumn != -1)
                column.Flags |= ImGuiTableColumnFlags.NoDirectResize_;

            column.WidthGiven = TruncateToInt(MathF.Max(column.WidthRequest, table.MinColumnWidth));
            table.ColumnsGivenWidth += column.WidthGiven;
        }

        // [Part 5] Redistribute the rounding remainder right-to-left so the cursor matches resizing.
        if (widthRemainingForStretchedColumns >= 1.0f && (table.Flags & ImGuiTableFlags.PreciseWidths) == 0)
        {
            for (int orderN = columnsCount - 1; stretchSumWeights > 0.0f && widthRemainingForStretchedColumns >= 1.0f && orderN >= 0; orderN--)
            {
                if (!table.EnabledMaskByDisplayOrder.TestBit(orderN))
                    continue;
                ImGuiTableColumn column = table.Columns[table.DisplayOrderToIndex[orderN]];
                if ((column.Flags & ImGuiTableColumnFlags.WidthStretch) == 0)
                    continue;
                column.WidthRequest += 1.0f;
                column.WidthGiven += 1.0f;
                widthRemainingForStretchedColumns -= 1.0f;
            }
        }

        // Determine whether the table as a whole is hovered. The active identifier is cleared
        // temporarily so an item inside the table does not block the hit test.
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        tableInstance.HoveredRowLast = tableInstance.HoveredRowNext;
        tableInstance.HoveredRowNext = -1;
        table.HoveredColumnBody = table.HoveredColumnBorder = -1;
        ImRect mouseHitRect = new(
            table.OuterRect.Min.X,
            table.OuterRect.Min.Y,
            table.OuterRect.Max.X,
            MathF.Max(table.OuterRect.Max.Y, table.OuterRect.Min.Y + tableInstance.LastOuterHeight));
        uint backupActiveId = g.ActiveId;
        g.ActiveId = 0;
        bool isHoveringTable = ItemHoverable(mouseHitRect, 0, ImGuiItemFlags.None);
        g.ActiveId = backupActiveId;

        // Skew the probe X inside the angled-header band so angled labels hover their own column.
        float mouseSkewedX = g.IO.MousePos.X;
        if (table.AngledHeadersHeight > 0.0f)
        {
            if (g.IO.MousePos.Y >= table.OuterRect.Min.Y && g.IO.MousePos.Y <= table.OuterRect.Min.Y + table.AngledHeadersHeight)
                mouseSkewedX += TruncateToInt((table.OuterRect.Min.Y + table.AngledHeadersHeight - g.IO.MousePos.Y) * table.AngledHeadersSlope);
        }

        // [Part 6] Set final position, offset, skip/clip state, and clipping rectangles, and detect the
        // hovered column. Columns are walked in display order so the host clip rect advances past the
        // frozen columns.
        bool hasAtLeastOneColumnRequestingOutput = false;
        bool offsetXFrozen = table.FreezeColumnsCount > 0;
        float offsetX = ((table.FreezeColumnsCount > 0) ? table.OuterRect.Min.X : workRect.Min.X) + table.OuterPaddingX - table.CellSpacingX1;
        ImRect hostClipRect = table.InnerClipRect;
        for (int n = 0; n < columnsCount; n++)
            table.VisibleMaskByIndex.ClearBit(n);
        for (int orderN = 0; orderN < columnsCount; orderN++)
        {
            int columnN = table.DisplayOrderToIndex[orderN];
            ImGuiTableColumn column = table.Columns[columnN];

            // The initial nav layer uses the effective frozen-row count so the header line changes layer
            // once its rows are actually frozen.
            column.NavLayerCurrent = (sbyte)(table.FreezeRowsCount > 0 ? ImGuiNavLayer.Menu : (ImGuiNavLayer)table.NavLayer);

            if (offsetXFrozen && table.FreezeColumnsCount == orderN)
            {
                offsetX += workRect.Min.X - table.OuterRect.Min.X;
                offsetXFrozen = false;
            }

            column.Flags &= ~ImGuiTableColumnFlags.StatusMask_;

            if (!table.EnabledMaskByDisplayOrder.TestBit(orderN))
            {
                // Hidden column: collapse its width but keep a valid clip rectangle height so the
                // clipper does not misbehave.
                column.MinX = column.MaxX = column.WorkMinX = offsetX;
                ImRect hiddenClip = column.ClipRect;
                hiddenClip.Min.X = offsetX;
                hiddenClip.Max.X = offsetX;
                hiddenClip.Min.Y = workRect.Min.Y;
                hiddenClip.Max.Y = float.MaxValue;
                hiddenClip.ClipWithFull(hostClipRect);
                column.ClipRect = hiddenClip;
                column.WidthGiven = 0.0f;
                column.IsVisibleX = column.IsVisibleY = column.IsRequestOutput = false;
                column.IsSkipItems = true;
                column.ItemWidth = 1.0f;
                continue;
            }

            column.MinX = offsetX;

            // Bound the width by the position-dependent maximum and the minimum column width.
            column.WidthMax = TableCalcMaxColumnWidth(table, columnN);
            column.WidthGiven = MathF.Min(column.WidthGiven, column.WidthMax);
            column.WidthGiven = MathF.Max(column.WidthGiven, MathF.Min(column.WidthRequest, table.MinColumnWidth));
            column.MaxX = offsetX + column.WidthGiven + table.CellSpacingX1 + table.CellSpacingX2 + table.CellPaddingX * 2.0f;

            // The clip minimum matches the column left edge, and the clip maximum includes padding so
            // headers receive a continuous hover highlight and sorting arrow region.
            float previousInstanceWorkMinX = column.WorkMinX;
            column.WorkMinX = column.MinX + table.CellPaddingX + table.CellSpacingX1;
            column.WorkMaxX = column.MaxX - table.CellPaddingX - table.CellSpacingX2;
            column.ItemWidth = TruncateToInt(column.WidthGiven * 0.65f);
            ImRect visibleClip = column.ClipRect;
            visibleClip.Min.X = column.MinX;
            visibleClip.Min.Y = workRect.Min.Y;
            visibleClip.Max.X = column.MaxX;
            visibleClip.Max.Y = float.MaxValue;
            visibleClip.ClipWithFull(hostClipRect);
            column.ClipRect = visibleClip;

            // Vertical clipping is intentionally disabled: it would reduce the submitted content width
            // fed back to the host window for auto-resize and reported-size purposes.
            column.IsVisibleX = column.ClipRect.Max.X > column.ClipRect.Min.X;
            column.IsVisibleY = true;
            bool isVisible = column.IsVisibleX;
            if (isVisible)
                table.VisibleMaskByIndex.SetBit(columnN);

            column.IsRequestOutput = isVisible || column.AutoFitQueue != 0 || column.CannotSkipItemsQueue != 0;
            column.IsSkipItems = !column.IsEnabled || table.HostSkipItems;
            if (column.IsSkipItems)
                ImAssert.Check(!isVisible);
            if (column.IsRequestOutput && !column.IsSkipItems)
                hasAtLeastOneColumnRequestingOutput = true;

            column.Flags |= ImGuiTableColumnFlags.IsEnabled;
            if (isVisible)
                column.Flags |= ImGuiTableColumnFlags.IsVisible;
            if (column.SortOrder != -1)
                column.Flags |= ImGuiTableColumnFlags.IsSorted;

            if (isHoveringTable && mouseSkewedX >= column.ClipRect.Min.X && mouseSkewedX < column.ClipRect.Max.X)
            {
                column.Flags |= ImGuiTableColumnFlags.IsHovered;
                table.HoveredColumnBody = (short)columnN;
            }

            // Reset content widths for instance zero; later instances shift the absolute stored values
            // by the change in work position.
            if (table.InstanceCurrent == 0)
            {
                column.ContentMaxXFrozen = column.WorkMinX;
                column.ContentMaxXUnfrozen = column.WorkMinX;
                column.ContentMaxXHeadersUsed = column.WorkMinX;
                column.ContentMaxXHeadersIdeal = column.WorkMinX;
            }
            else
            {
                float offsetFromPreviousInstance = column.WorkMinX - previousInstanceWorkMinX;
                column.ContentMaxXFrozen += offsetFromPreviousInstance;
                column.ContentMaxXUnfrozen += offsetFromPreviousInstance;
                column.ContentMaxXHeadersUsed += offsetFromPreviousInstance;
                column.ContentMaxXHeadersIdeal += offsetFromPreviousInstance;
            }

            // Decrement the auto-fit queues only after the host window had a chance to submit items.
            if (!table.HostSkipItems && table.InstanceCurrent == 0)
            {
                column.AutoFitQueue >>= 1;
                column.CannotSkipItemsQueue >>= 1;
            }

            if (orderN < table.FreezeColumnsCount)
                hostClipRect.Min.X = ClampValue(column.MaxX + TableBorderSize, hostClipRect.Min.X, hostClipRect.Max.X);

            offsetX += column.WidthGiven + table.CellSpacingX1 + table.CellSpacingX2 + table.CellPaddingX * 2.0f;
        }

        // Keep one column submitting output so a clipper-aware user can still advance rows and report
        // the table's total content height.
        if (!hasAtLeastOneColumnRequestingOutput)
        {
            table.Columns[table.LeftMostEnabledColumn].IsRequestOutput = true;
            table.Columns[table.LeftMostEnabledColumn].IsSkipItems = false;
        }

        // [Part 7] Detect hovering of the unused space after the right-most column and drop the
        // resizable flag when no column is actually resizable.
        float unusedX1 = MathF.Max(table.WorkRect.Min.X, table.Columns[table.RightMostEnabledColumn].ClipRect.Max.X);
        if (isHoveringTable && table.HoveredColumnBody == -1)
        {
            if (mouseSkewedX >= unusedX1)
                table.HoveredColumnBody = (short)columnsCount;
        }
        if (!hasResizable && (table.Flags & ImGuiTableFlags.Resizable) != 0)
            table.Flags &= ~ImGuiTableFlags.Resizable;

        table.IsActiveIdAliveBeforeTable = g.ActiveIdIsAlive != 0;

        // [Part 8] Lock the right-most extent late so fixed-column tables do not claim more width than
        // their contents need.
        if (table.RightMostStretchedColumn != -1)
            table.Flags &= ~ImGuiTableFlags.NoHostExtendX;
        if ((table.Flags & ImGuiTableFlags.NoHostExtendX) != 0)
        {
            ImRect outerRect = table.OuterRect;
            outerRect.Max.X = unusedX1;
            table.OuterRect = outerRect;
            ImRect tableWorkRect = table.WorkRect;
            tableWorkRect.Max.X = unusedX1;
            table.WorkRect = tableWorkRect;
            ImRect tableInnerClipRect = table.InnerClipRect;
            tableInnerClipRect.Max.X = MathF.Min(tableInnerClipRect.Max.X, unusedX1);
            table.InnerClipRect = tableInnerClipRect;
        }
        table.InnerWindow!.ParentWorkRect = table.WorkRect;
        table.BorderX1 = table.InnerClipRect.Min.X;
        table.BorderX2 = table.InnerClipRect.Max.X;

        // Publish the work-rect bottom for GetContentRegionAvail(); row and cell code updates it again.
        float windowContentMaxY;
        if ((table.Flags & ImGuiTableFlags.NoHostExtendY) != 0)
            windowContentMaxY = table.OuterRect.Max.Y;
        else
            windowContentMaxY = MathF.Max(
                table.InnerWindow.ContentRegionRect.Max.Y,
                (table.Flags & ImGuiTableFlags.ScrollY) != 0 ? 0.0f : table.OuterRect.Max.Y);
        ImRect hostWorkRect = table.InnerWindow.WorkRect;
        hostWorkRect.Max.Y = ClampValue(windowContentMaxY - g.Style.CellPadding.Y, table.InnerWindow.WorkRect.Min.Y, table.InnerWindow.WorkRect.Max.Y);
        table.InnerWindow.WorkRect = hostWorkRect;

        // [Part 9] Allocate draw channels and background clip rectangles.
        TableSetupDrawChannels(table);

        // [Part 10] Hit-test the column resize borders.
        if ((table.Flags & ImGuiTableFlags.Resizable) != 0)
            TableUpdateBorders(table);
        tableInstance.LastTopHeadersRowHeight = 0.0f;
        table.IsLayoutLocked = true;
        table.IsUsingHeaders = false;

        // Highlight the column named by the open context menu, or the hovered column when requested.
        table.HighlightColumnHeader = -1;
        if (table.IsContextPopupOpen && table.ContextPopupColumn != -1 && table.InstanceInteracted == table.InstanceCurrent)
        {
            table.HighlightColumnHeader = table.ContextPopupColumn;
        }
        else if ((table.Flags & ImGuiTableFlags.HighlightHoveredColumn) != 0 && table.HoveredColumnBody != -1 && table.HoveredColumnBody != columnsCount && table.HoveredColumnBorder == -1)
        {
            if (g.ActiveId == 0 || table.IsActiveIdInTable || g.DragDropActive)
                table.HighlightColumnHeader = table.HoveredColumnBody;
        }

        // [Part 11] Default context menu, drawn only for the interacted instance. The context-menu
        // implementation is owned by part 03.
        if (!table.DisableDefaultContextMenu && TableBeginContextMenuPopup(table))
        {
            TableDrawDefaultContextMenu(table, table.Flags);
            EndPopup();
        }

        // [Part 12] Sanitize and build sort specs before they can be used for display, for the case
        // where sorting changed before the header row. Owned by part 03.
        if (table.IsSortSpecsDirty && (table.Flags & ImGuiTableFlags.Sortable) != 0)
            TableSortSpecsBuild(table);

        // [Part 13] Publish inner-window decoration sizes so scrolling and navigation account for the
        // frozen rows and columns.
        if (table.FreezeColumnsRequest > 0)
            table.InnerWindow.DecoInnerSizeX1 = table.Columns[table.DisplayOrderToIndex[table.FreezeColumnsRequest - 1]].MaxX - table.OuterRect.Min.X;
        if (table.FreezeRowsRequest > 0)
            table.InnerWindow.DecoInnerSizeY1 = tableInstance.LastFrozenHeight;
        tableInstance.LastFrozenHeight = 0.0f;

        // Multi-select box-select support: when the active box-selection targets the inner window in
        // unclip mode, re-flag any column overlapping the unclip rectangle so it still emits output after
        // layout locked its skip state.
        ImGuiWindow layoutInnerWindow = table.InnerWindow;
        ImGuiBoxSelectState boxSelect = g.BoxSelectState;
        if (boxSelect.Window == layoutInnerWindow && boxSelect.UnclipMode)
        {
            ImRect unclipRect = boxSelect.UnclipRect;
            TableApplyExternalUnclipRect(table, ref unclipRect);
            boxSelect.UnclipRect = unclipRect;
        }

        // Initial state: select the no-clip channel, or push the table clip rectangle for regular
        // rendering.
        if ((table.Flags & ImGuiTableFlags.NoClip) != 0)
            table.DrawSplitter!.SetCurrentChannel(table.InnerWindow.DrawList, TableDrawChannelNoClip);
        else
            table.InnerWindow.DrawList.PushClipRect(table.InnerWindow.InnerClipRect.Min, table.InnerWindow.InnerClipRect.Max, false);
    }

    /// <summary>
    /// Re-flags columns so a box-selection rectangle overlapping them still requests output after
    /// layout has locked their skip state.
    /// </summary>
    /// <param name="table">Table whose columns are updated.</param>
    /// <param name="rect">Box-selection rectangle in screen coordinates.</param>
    /// <remarks>
    /// Called from <see cref="TableUpdateLayout"/> while the active box-selection targets the table's
    /// inner window in unclip mode, so columns overlapped by a selection drag are re-flagged before the
    /// layout locks their skip state.
    /// </remarks>
    internal static void TableApplyExternalUnclipRect(ImGuiTable table, ref ImRect rect)
    {
        if (rect.IsInverted())
            return;
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            ImGuiTableColumn column = table.Columns[columnN];
            if (!column.IsRequestOutput)
            {
                if (rect.Overlaps(new ImRect(column.MinX, table.WorkRect.Min.Y, column.MaxX, float.MaxValue)))
                    column.IsRequestOutput = true;
            }
        }
    }

    /// <summary>
    /// Hit-tests the interactive resize separators on the right edge of each resizable column.
    /// </summary>
    /// <param name="table">The resizable table being laid out.</param>
    /// <remarks>
    /// The hit rectangle uses the previous frame's outer height and header height because the current
    /// frame's height is not yet final; only interaction with the border is affected, while the border
    /// itself is drawn from the current frame's extents in <see cref="TableDrawBorders"/>. Double-click
    /// queues a single-column auto-fit, and holding starts a resize whose width is applied in
    /// <see cref="EndTable"/>.
    /// </remarks>
    internal static void TableUpdateBorders(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        ImAssert.Check((table.Flags & ImGuiTableFlags.Resizable) != 0);

        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        float hitHalfWidth = TruncateToInt(TableResizeSeparatorHalfThickness * g.CurrentDpiScale);
        float hitY1 = (table.FreezeRowsCount >= 1 ? table.OuterRect.Min.Y : table.WorkRect.Min.Y) + table.AngledHeadersHeight;
        float hitY2Body = MathF.Max(table.OuterRect.Max.Y, hitY1 + tableInstance.LastOuterHeight - table.AngledHeadersHeight);
        float hitY2Head = hitY1 + tableInstance.LastTopHeadersRowHeight;

        for (int orderN = 0; orderN < table.ColumnsCount; orderN++)
        {
            if (!table.EnabledMaskByDisplayOrder.TestBit(orderN))
                continue;

            int columnN = table.DisplayOrderToIndex[orderN];
            ImGuiTableColumn column = table.Columns[columnN];
            if ((column.Flags & (ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoDirectResize_)) != 0)
                continue;

            float borderY2Hit = (table.Flags & ImGuiTableFlags.NoBordersInBody) != 0 ? hitY2Head : hitY2Body;
            if ((table.Flags & ImGuiTableFlags.NoBordersInBody) != 0 && !table.IsUsingHeaders)
                continue;
            if (!column.IsVisibleX && table.LastResizedColumn != columnN)
                continue;

            uint columnId = TableGetColumnResizeID(table, columnN, table.InstanceCurrent);
            ImRect hitRect = new(column.MaxX - hitHalfWidth, hitY1, column.MaxX + hitHalfWidth, borderY2Hit);
            ItemAdd(hitRect, columnId, null, ImGuiItemFlags.NoNav);

            bool pressed = ButtonBehavior(
                hitRect,
                columnId,
                out bool hovered,
                out bool held,
                // The public and private button-flag bits live in two enum types in this port, so they are
                // merged on their integer representation, matching the canonical single bit field.
                (ImGuiButtonFlags)((int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_FlattenChildren
                    | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick
                    | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDoubleClick
                    | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus));
            if (pressed && IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                TableSetColumnWidthAutoSingle(table, columnN);
                ClearActiveID();
                held = false;
            }
            if (held)
            {
                // While resizing down, lock the minimum content X2 so the drag cannot collapse past the
                // right-most enabled column's extents.
                if (table.LastResizedColumn == -1)
                    table.ResizeLockMinContentsX2 = table.RightMostEnabledColumn != -1 ? table.Columns[table.RightMostEnabledColumn].MaxX : -float.MaxValue;
                table.ResizedColumn = (short)columnN;
                table.InstanceInteracted = table.InstanceCurrent;
            }
            if ((hovered && g.HoveredIdTimer > TableResizeSeparatorFeedbackTimer) || held)
            {
                table.HoveredColumnBorder = (short)columnN;
                SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            }
        }
    }

    /// <summary>
    /// Ends the current table: locks layout, finalizes height and scroll extents, draws borders,
    /// merges draw channels, restores host-window state, saves settings, and unwinds the temporary-data
    /// stack.
    /// </summary>
    /// <remarks>
    /// The matching settings writer is provided by the settings-lifetime owner (part 04); its backing
    /// layout-file storage is owned by Chunk 13.
    /// </remarks>
    public static void EndTable()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "EndTable() call should only be done while in BeginTable() scope!");
            return;
        }

        // The user may never have called TableNextRow()/TableNextColumn(); run layout so borders and
        // all other paths behave consistently.
        if (!table.IsLayoutLocked)
            TableUpdateLayout(table);

        ImGuiTableFlags flags = table.Flags;
        ImGuiWindow innerWindow = table.InnerWindow!;
        ImGuiWindow outerWindow = table.OuterWindow!;
        ImGuiTableTempData tempData = table.TempData!;
        ImAssert.Check(innerWindow == g.CurrentWindow && innerWindow.ID == tempData.WindowID);
        ImAssert.Check(outerWindow == innerWindow || outerWindow == innerWindow.ParentWindow);

        if (table.IsInsideRow)
            TableEndRow(table);

        // Context menu opened from the columns body. Owned by part 03.
        if ((flags & ImGuiTableFlags.ContextMenuInBody) != 0)
        {
            if (table.HoveredColumnBody != -1 && !IsAnyItemHovered() && IsMouseReleased(ImGuiMouseButton.Right))
                TableOpenContextMenu(table.HoveredColumnBody);
        }

        // Finalize the table height. Rounding the final position is important because row heights are
        // not rounded.
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        innerWindow.DC.PrevLineSize = tempData.HostBackupPrevLineSize;
        innerWindow.DC.CurrLineSize = tempData.HostBackupCurrLineSize;
        innerWindow.DC.CursorMaxPos = tempData.HostBackupCursorMaxPos;
        float innerContentMaxY = MathF.Ceiling(table.RowPosY2);
        ImAssert.Check(table.RowPosY2 == innerWindow.DC.CursorPos.Y);
        if (innerWindow != outerWindow)
        {
            innerWindow.DC.CursorMaxPos = new ImVec2(innerWindow.DC.CursorMaxPos.X, innerContentMaxY);
        }
        else if ((flags & ImGuiTableFlags.NoHostExtendY) == 0)
        {
            // Canonical patches both rectangles to the same value, computed from the outer rectangle's
            // pre-patch bottom.
            float patchedMaxY = MathF.Max(table.OuterRect.Max.Y, innerContentMaxY);
            ImRect outerRect = table.OuterRect;
            outerRect.Max.Y = patchedMaxY;
            table.OuterRect = outerRect;
            ImRect innerRect = table.InnerRect;
            innerRect.Max.Y = patchedMaxY;
            table.InnerRect = innerRect;
        }
        ImRect tableWorkRect = table.WorkRect;
        tableWorkRect.Max.Y = MathF.Max(tableWorkRect.Max.Y, table.OuterRect.Max.Y);
        table.WorkRect = tableWorkRect;
        tableInstance.LastOuterHeight = table.OuterRect.GetHeight();

        // Publish the inner horizontal scrolling extent.
        if ((table.Flags & ImGuiTableFlags.ScrollX) != 0)
        {
            float outerPaddingForBorder = (table.Flags & ImGuiTableFlags.BordersOuterV) != 0 ? TableBorderSize : 0.0f;
            float maxPosX = innerWindow.DC.CursorMaxPos.X;
            if (table.RightMostEnabledColumn != -1)
                maxPosX = MathF.Max(maxPosX, table.Columns[table.RightMostEnabledColumn].WorkMaxX + table.CellPaddingX + table.OuterPaddingX - outerPaddingForBorder);
            if (table.ResizedColumn != -1)
                maxPosX = MathF.Max(maxPosX, table.ResizeLockMinContentsX2);
            innerWindow.DC.CursorMaxPos = new ImVec2(maxPosX + table.TempData!.AngledHeadersExtraWidth, innerWindow.DC.CursorMaxPos.Y);
        }

        // Pop the table clip rectangle. The port's PopClipRect() also refreshes the window's logical
        // clip rectangle from the draw-list stack, matching the canonical _ClipRectStack.back() read.
        if ((flags & ImGuiTableFlags.NoClip) == 0)
            PopClipRect();

        // Draw borders owned by this table.
        if ((flags & ImGuiTableFlags.Borders) != 0)
            TableDrawBorders(table);

        // Flatten channels and merge compatible draw calls.
        ImDrawListSplitter splitter = table.DrawSplitter!;
        splitter.SetCurrentChannel(innerWindow.DrawList, 0);
        if ((table.Flags & ImGuiTableFlags.NoClip) == 0)
            TableMergeDrawChannels(table);
        splitter.Merge(innerWindow.DrawList);

        // Recompute the ideal auto-fit width so an auto-resizing host can react without waiting for the
        // next frame.
        float autoFitWidthForFixed = 0.0f;
        float autoFitWidthForStretched = 0.0f;
        float autoFitWidthForStretchedMin = 0.0f;
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            if (!table.EnabledMaskByIndex.TestBit(columnN))
                continue;
            ImGuiTableColumn column = table.Columns[columnN];
            float columnWidthRequest = ((column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0 && (column.Flags & ImGuiTableColumnFlags.NoResize) == 0)
                ? column.WidthRequest
                : TableGetColumnWidthAuto(table, column);
            if ((column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0)
                autoFitWidthForFixed += columnWidthRequest;
            else
                autoFitWidthForStretched += columnWidthRequest;
            if ((column.Flags & ImGuiTableColumnFlags.WidthStretch) != 0 && (column.Flags & ImGuiTableColumnFlags.NoResize) != 0)
                autoFitWidthForStretchedMin = MathF.Max(autoFitWidthForStretchedMin, columnWidthRequest / (column.StretchWeight / table.ColumnsStretchSumWeights));
        }
        float widthSpacings = (table.OuterPaddingX * 2.0f) + (table.CellSpacingX1 + table.CellSpacingX2) * (table.ColumnsEnabledCount - 1);
        table.ColumnsAutoFitWidth = widthSpacings + (table.CellPaddingX * 2.0f) * table.ColumnsEnabledCount
            + autoFitWidthForFixed + MathF.Max(autoFitWidthForStretched, autoFitWidthForStretchedMin);

        // Update scrolling: non-horizontal tables never scroll horizontally, and releasing a resize
        // scrolls the affected column back into sight.
        if ((table.Flags & ImGuiTableFlags.ScrollX) == 0 && innerWindow != outerWindow)
        {
            innerWindow.Scroll = new ImVec2(0.0f, innerWindow.Scroll.Y);
        }
        else if (table.LastResizedColumn != -1 && table.ResizedColumn == -1 && innerWindow.ScrollbarX && table.InstanceInteracted == table.InstanceCurrent)
        {
            float neighborWidthToKeepVisible = table.MinColumnWidth + table.CellPaddingX * 2.0f;
            ImGuiTableColumn column = table.Columns[table.LastResizedColumn];
            if (column.MaxX < table.InnerClipRect.Min.X)
                SetScrollFromPosX(innerWindow, column.MaxX - innerWindow.Pos.X - neighborWidthToKeepVisible, 1.0f);
            else if (column.MaxX > table.InnerClipRect.Max.X)
                SetScrollFromPosX(innerWindow, column.MaxX - innerWindow.Pos.X + neighborWidthToKeepVisible, 1.0f);
        }

        // Apply an in-progress resize at the end of the frame so the new width lands on the next layout.
        if (table.ResizedColumn != -1 && table.InstanceCurrent == table.InstanceInteracted)
        {
            ImGuiTableColumn column = table.Columns[table.ResizedColumn];
            float newX2 = g.IO.MousePos.X - g.ActiveIdClickOffset.X + TruncateToInt(TableResizeSeparatorHalfThickness * g.CurrentDpiScale);
            float newWidth = TruncateToInt(newX2 - column.MinX - table.CellSpacingX1 - table.CellPaddingX * 2.0f);
            table.ResizedColumnNextWidth = newWidth;
        }

        table.IsActiveIdInTable = g.ActiveIdIsAlive != 0 && !table.IsActiveIdAliveBeforeTable;

        // Pop the identifier scope, guarding against mismatched pushes.
        ImAssert.Check(innerWindow.IDStack[^1] == tableInstance.TableInstanceID, "Mismatching PushID/PopID!");
        ImAssert.Check(outerWindow.DC.ItemWidthStack.Count >= tempData.HostBackupItemWidthStackSize, "Too many PopItemWidth!");
        if (table.InstanceCurrent > 0)
            PopID();
        PopID();

        // Restore the host-window members that BeginTableEx() and the layout modified.
        ImVec2 backupOuterMaxPos = outerWindow.DC.CursorMaxPos;
        innerWindow.WorkRect = tempData.HostBackupWorkRect;
        innerWindow.ParentWorkRect = tempData.HostBackupParentWorkRect;
        innerWindow.SkipItems = table.HostSkipItems;
        outerWindow.DC.CursorPos = table.OuterRect.Min;
        outerWindow.DC.ItemWidth = tempData.HostBackupItemWidth;
        while (outerWindow.DC.ItemWidthStack.Count > tempData.HostBackupItemWidthStackSize)
            outerWindow.DC.ItemWidthStack.RemoveAt(outerWindow.DC.ItemWidthStack.Count - 1);
        outerWindow.DC.ColumnsOffset = tempData.HostBackupColumnsOffset;

        // Lay out the table in the outer window. A scrolling table uses EndChild(); a plain table
        // submits its outer rectangle directly.
        if (innerWindow != outerWindow)
        {
            short backupNavLayersActiveMask = innerWindow.DC.NavLayersActiveMask;
            innerWindow.DC.NavLayersActiveMask = (short)(backupNavLayersActiveMask | (short)(1 << table.NavLayer)); // An empty table must navigate consistently.
            g.CurrentTable = null; // Avoid error recovery recursing through EndChild().
            EndChild();
            g.CurrentTable = table;
            innerWindow.DC.NavLayersActiveMask = backupNavLayersActiveMask;
        }
        else
        {
            innerWindow.DC.TreeDepth--;
            ItemSize(table.OuterRect.GetSize());
            ItemAdd(table.OuterRect, 0);
        }

        // Override the declared content extents so an auto-resizing host sizes correctly without adding
        // a scrollbar it does not need.
        if ((table.Flags & ImGuiTableFlags.NoHostExtendX) != 0)
        {
            ImAssert.Check((table.Flags & ImGuiTableFlags.ScrollX) == 0);
            ImVec2 outerMaxPos = outerWindow.DC.CursorMaxPos;
            outerMaxPos.X = MathF.Max(backupOuterMaxPos.X, table.OuterRect.Min.X + table.ColumnsAutoFitWidth);
            outerWindow.DC.CursorMaxPos = outerMaxPos;
        }
        else if (tempData.UserOuterSize.X <= 0.0f)
        {
            float outerContentMaxX = table.OuterRect.Min.X + table.ColumnsAutoFitWidth;
            float decorationSize = table.TempData!.AngledHeadersExtraWidth + ((innerWindow != outerWindow) ? innerWindow.ScrollbarSizes.X : 0.0f);
            ImVec2 idealMaxPos = outerWindow.DC.IdealMaxPos;
            idealMaxPos.X = MathF.Max(idealMaxPos.X, outerContentMaxX + decorationSize - tempData.UserOuterSize.X);
            outerWindow.DC.IdealMaxPos = idealMaxPos;
            ImVec2 outerMaxPos = outerWindow.DC.CursorMaxPos;
            outerMaxPos.X = MathF.Max(backupOuterMaxPos.X, MathF.Min(table.OuterRect.Max.X, outerContentMaxX + decorationSize));
            outerWindow.DC.CursorMaxPos = outerMaxPos;
        }
        else
        {
            ImVec2 outerMaxPos = outerWindow.DC.CursorMaxPos;
            outerMaxPos.X = MathF.Max(backupOuterMaxPos.X, table.OuterRect.Max.X);
            outerWindow.DC.CursorMaxPos = outerMaxPos;
        }
        if (tempData.UserOuterSize.Y <= 0.0f)
        {
            float outerContentSizeY = (innerWindow == outerWindow)
                ? innerContentMaxY - table.InnerRect.Min.Y
                : innerContentMaxY - innerWindow.DC.CursorStartPos.Y;
            float outerContentMaxY = table.OuterRect.Min.Y + outerContentSizeY;
            float decorationSize = innerWindow != outerWindow ? innerWindow.ScrollbarSizes.Y : 0.0f;
            ImVec2 idealMaxPos = outerWindow.DC.IdealMaxPos;
            idealMaxPos.Y = MathF.Max(idealMaxPos.Y, outerContentMaxY + decorationSize - tempData.UserOuterSize.Y);
            outerWindow.DC.IdealMaxPos = idealMaxPos;
            ImVec2 outerMaxPos = outerWindow.DC.CursorMaxPos;
            outerMaxPos.Y = MathF.Max(backupOuterMaxPos.Y, MathF.Min(table.OuterRect.Max.Y, outerContentMaxY + decorationSize));
            outerWindow.DC.CursorMaxPos = outerMaxPos;
        }
        else
        {
            ImVec2 outerMaxPos = outerWindow.DC.CursorMaxPos;
            outerMaxPos.Y = MathF.Max(backupOuterMaxPos.Y, table.OuterRect.Max.Y);
            outerWindow.DC.CursorMaxPos = outerMaxPos;
        }

        // Save settings.
        if (table.IsSettingsDirty)
            TableSaveSettings(table);
        table.IsInitializing = false;

        // Clear or restore the ambient table, rebinding a parent table's temporary data when nested.
        ImAssert.Check(g.CurrentWindow == outerWindow && g.CurrentTable == table);
        ImAssert.Check(g.TablesTempDataStacked > 0);
        g.TablesTempDataStacked--;
        ImGuiTableTempData? parentTempData = g.TablesTempDataStacked > 0 ? g.TablesTempData[g.TablesTempDataStacked - 1] : null;
        g.CurrentTable = (parentTempData != null && parentTempData.WindowID == outerWindow.ID)
            ? TableGetByIndex(parentTempData.TableIndex)
            : null;
        if (g.CurrentTable != null)
        {
            g.CurrentTable.TempData = parentTempData;
            g.CurrentTable.DrawSplitter = parentTempData!.DrawSplitter;
        }
        outerWindow.DC.CurrentTableIdx = g.CurrentTable != null ? TableGetIndex(g.CurrentTable) : -1;
        NavUpdateCurrentWindowIsScrollPushableX();
    }

    /// <summary>
    /// Applies the per-column default values selected by <paramref name="initMask"/>.
    /// </summary>
    /// <param name="table">Owning table, consulted for reorderability and column index.</param>
    /// <param name="column">Column whose defaults are initialized.</param>
    /// <param name="initMask">Table flags selecting which sections to initialize.</param>
    /// <remarks>
    /// Canonical declares the mask parameter as <c>ImGuiTableColumnFlags</c> at
    /// , yet the call site passes <c>ImGuiTableFlags</c>
    /// values and the body compares against <c>ImGuiTableFlags_Resizable</c> and its siblings. The port
    /// therefore declares the parameter as <see cref="ImGuiTableFlags"/>, the type of the value that is
    /// actually consumed.
    /// </remarks>
    internal static void TableInitColumnDefaults(ImGuiTable table, ImGuiTableColumn column, ImGuiTableFlags initMask)
    {
        ImGuiTableColumnFlags flags = column.Flags;
        if ((initMask & ImGuiTableFlags.Resizable) != 0)
        {
            float initWidthOrWeight = column.InitStretchWeightOrWidth;
            column.WidthRequest = ((flags & ImGuiTableColumnFlags.WidthFixed) != 0 && initWidthOrWeight > 0.0f) ? initWidthOrWeight : -1.0f;
            column.StretchWeight = (initWidthOrWeight > 0.0f && (flags & ImGuiTableColumnFlags.WidthStretch) != 0) ? initWidthOrWeight : -1.0f;
            if (initWidthOrWeight > 0.0f)
                column.AutoFitQueue = 0x00; // An explicit size disables auto-fit.
        }
        if ((initMask & ImGuiTableFlags.Reorderable) != 0)
            column.DisplayOrder = (short)((table.Flags & ImGuiTableFlags.Reorderable) != 0 ? -1 : table.Columns.IndexOf(column));
        if ((initMask & ImGuiTableFlags.Hideable) != 0)
            column.IsUserEnabled = column.IsUserEnabledNextFrame = (flags & ImGuiTableColumnFlags.DefaultHide) == 0;
        if ((initMask & ImGuiTableFlags.Sortable) != 0)
        {
            // Several columns using DefaultSort are reassigned unique sort orders when the specs are built.
            column.SortOrder = (flags & ImGuiTableColumnFlags.DefaultSort) != 0 ? (short)0 : (short)-1;
            column.SortDirection = (flags & ImGuiTableColumnFlags.DefaultSort) != 0
                ? ((flags & ImGuiTableColumnFlags.PreferSortDescending) != 0 ? ImGuiSortDirection.Descending : ImGuiSortDirection.Ascending)
                : ImGuiSortDirection.None;
        }
    }

    #endregion

    #region Tables: Simple accessors

    /// <summary>
    /// Returns the number of columns declared by the current table.
    /// </summary>
    /// <returns>The declared column count, or zero when no table is active.</returns>
    public static int TableGetColumnCount()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        return table != null ? table.ColumnsCount : 0;
    }

    /// <summary>
    /// Returns the index of the current column in the ambient table.
    /// </summary>
    /// <returns>The current column index, or zero when no table is active.</returns>
    /// <remarks>
    /// The indexed accessor is owned by this part even though the column-advance functions that update
    /// the current column belong to part 02.
    /// </remarks>
    public static int TableGetColumnIndex()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        return table != null ? table.CurrentColumn : 0;
    }

    /// <summary>
    /// Returns the index of the current row in the ambient table.
    /// </summary>
    /// <returns>The current row index, or zero when no table is active.</returns>
    /// <remarks>
    /// The indexed accessor is owned by this part even though the row-advance functions that update the
    /// current row belong to part 02.
    /// </remarks>
    public static int TableGetRowIndex()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        return table != null ? table.CurrentRow : 0;
    }

    /// <summary>
    /// Returns the name declared for a column of the current table.
    /// </summary>
    /// <param name="columnN">Column index, or a negative value to use the current column.</param>
    /// <returns>The column name, or <see langword="null"/> when no table is active.</returns>
    public static string? TableGetColumnName(int columnN = -1)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
            return null;
        if (columnN < 0)
            columnN = table.CurrentColumn;
        return TableGetColumnName(table, columnN);
    }

    /// <summary>
    /// Returns the name declared for a column of a specific table.
    /// </summary>
    /// <param name="table">Table that owns the column.</param>
    /// <param name="columnN">Column index.</param>
    /// <returns>The column name, or an empty string when the offset is not valid yet or unset.</returns>
    /// <remarks>
    /// Canonical returns a pointer into the table's contiguous name buffer. The port's buffer stores the
    /// same NUL-terminated bytes, so the name is decoded into a string at the offset; an offset is
    /// invalid before layout when the column was never submitted through <c>TableSetupColumn()</c>.
    /// </remarks>
    internal static string TableGetColumnName(ImGuiTable table, int columnN)
    {
        if (!table.IsLayoutLocked && columnN >= table.DeclColumnsCount)
            return string.Empty; // The name offset is not valid yet.
        ImGuiTableColumn column = table.Columns[columnN];
        if (column.NameOffset == -1)
            return string.Empty;
        List<byte> buffer = table.ColumnsNames.Buf;
        System.Text.StringBuilder builder = new();
        for (int n = column.NameOffset; n < buffer.Count && buffer[n] != 0; n++)
            builder.Append((char)buffer[n]);
        return builder.ToString();
    }

    /// <summary>
    /// Changes the user-accessible enabled state of a column.
    /// </summary>
    /// <param name="columnN">Column index, or a negative value to use the current column.</param>
    /// <param name="enabled">The requested enabled state, applied on the next layout.</param>
    /// <remarks>
    /// The table must be declared <see cref="ImGuiTableFlags.Hideable"/> because this manipulates the
    /// user-visible state. The <c>Disabled</c> column flag is an overriding master disable that is not
    /// reachable through this function.
    /// </remarks>
    public static void TableSetColumnEnabled(int columnN, bool enabled)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        ImAssert.Check((table.Flags & ImGuiTableFlags.Hideable) != 0);
        if (columnN < 0)
            columnN = table.CurrentColumn;
        ImAssert.Check(columnN >= 0 && columnN < table.ColumnsCount);
        table.Columns[columnN].IsUserEnabledNextFrame = enabled;
    }

    /// <summary>
    /// Returns the column flags of the current table, including the read-only status bits.
    /// </summary>
    /// <param name="columnN">Column index, or a negative value to use the current column. Passing the
    /// declared column count queries the unused space after the right-most column.</param>
    /// <returns>The column flags, or <see cref="ImGuiTableColumnFlags.None"/> when no table is active.</returns>
    public static ImGuiTableColumnFlags TableGetColumnFlags(int columnN = -1)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
            return ImGuiTableColumnFlags.None;
        if (columnN < 0)
            columnN = table.CurrentColumn;
        if (columnN == table.ColumnsCount)
            return table.HoveredColumnBody == columnN ? ImGuiTableColumnFlags.IsHovered : ImGuiTableColumnFlags.None;
        return table.Columns[columnN].Flags;
    }

    /// <summary>
    /// Returns the background rectangle of a cell in the current row.
    /// </summary>
    /// <param name="table">Table that owns the column.</param>
    /// <param name="columnN">Column index.</param>
    /// <returns>The cell rectangle, clamped to the table work rect.</returns>
    /// <remarks>
    /// The bottom edge is only exact when a minimum row height was provided to <c>TableNextRow()</c>;
    /// otherwise the row height is unknown until the row ends.
    /// </remarks>
    internal static ImRect TableGetCellBgRect(ImGuiTable table, int columnN)
    {
        ImGuiTableColumn column = table.Columns[columnN];
        float x1 = MathF.Max(column.MinX, table.WorkRect.Min.X);
        float x2 = MathF.Min(column.MaxX, table.WorkRect.Max.X);
        return new ImRect(x1, table.RowPosY1, x2, table.RowPosY2);
    }

    /// <summary>
    /// Returns the identifier used for the resize border on the right side of a column.
    /// </summary>
    /// <param name="table">Table that owns the column.</param>
    /// <param name="columnN">Column index.</param>
    /// <param name="instanceNo">Table instance number; instance zero uses the table identifier.</param>
    /// <returns>The resize identifier.</returns>
    /// <remarks>
    /// Ownership note: this member is listed for part 01 as a private layout helper and is also named
    /// in part 02's ownership table; it is defined here because <see cref="TableUpdateBorders"/> calls
    /// it, and the assembly must keep a single definition.
    /// </remarks>
    internal static uint TableGetColumnResizeID(ImGuiTable table, int columnN, int instanceNo = 0)
    {
        ImAssert.Check(columnN >= 0 && columnN < table.ColumnsCount);
        uint instanceId = TableGetInstanceID(table, instanceNo);
        return instanceId + 1 + (uint)columnN;
    }

    /// <summary>
    /// Returns the per-instance table data for the requested instance number.
    /// </summary>
    /// <param name="table">Table that owns the instance data.</param>
    /// <param name="instanceNo">Instance number; instance zero is stored inline.</param>
    /// <returns>The mutable instance data.</returns>
    internal static ImGuiTableInstanceData TableGetInstanceData(ImGuiTable table, int instanceNo)
    {
        if (instanceNo == 0)
            return table.InstanceDataFirst;
        return table.InstanceDataExtra[instanceNo - 1];
    }

    /// <summary>
    /// Returns the stable identifier of a table instance.
    /// </summary>
    /// <param name="table">Table that owns the instance data.</param>
    /// <param name="instanceNo">Instance number; instance zero uses the table identifier.</param>
    /// <returns>The instance identifier.</returns>
    internal static uint TableGetInstanceID(ImGuiTable table, int instanceNo)
    {
        return TableGetInstanceData(table, instanceNo).TableInstanceID;
    }

    /// <summary>
    /// Returns the currently hovered table column of the ambient table.
    /// </summary>
    /// <returns>The hovered column index, the declared column count when the unused trailing region is
    /// hovered, or <c>-1</c> when no table is active or hovered.</returns>
    public static int TableGetHoveredColumn()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
            return -1;
        return table.HoveredColumnBody;
    }

    /// <summary>
    /// Returns the row hovered during the previous frame in the ambient table.
    /// </summary>
    /// <returns>The previously hovered row index, or <c>-1</c> when no table is active.</returns>
    /// <remarks>
    /// The value has one frame of latency, which keeps it internal rather than public.
    /// </remarks>
    internal static int TableGetHoveredRow()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
            return -1;
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        return tableInstance.HoveredRowLast;
    }

    /// <summary>
    /// Queues a background color for a cell, a row background layer, or a row selection layer.
    /// </summary>
    /// <param name="target">Which background layer to color.</param>
    /// <param name="color">Packed color value; <c>IM_COL32_DISABLE</c> clears the request.</param>
    /// <param name="columnN">Column index for <see cref="ImGuiTableBgTarget.CellBg"/>, or a negative
    /// value to use the current column.</param>
    /// <remarks>
    /// Cell and row backgrounds cannot be drawn immediately because the row height is unknown; the
    /// requests are recorded and consumed by the row-ending code owned by part 02.
    /// </remarks>
    public static void TableSetBgColor(ImGuiTableBgTarget target, uint color, int columnN = -1)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        ImAssert.Check(target != ImGuiTableBgTarget.None);

        if (color == TableColorDisable)
            color = 0;

        switch (target)
        {
            case ImGuiTableBgTarget.CellBg:
            {
                if (table.RowPosY1 > table.InnerClipRect.Max.Y) // Discard; the row is below the table.
                    return;
                if (columnN == -1)
                    columnN = table.CurrentColumn;
                if (!table.VisibleMaskByIndex.TestBit(columnN))
                    return;
                if (table.RowCellDataCurrent < 0 || table.RowCellData[table.RowCellDataCurrent].Column != columnN)
                    table.RowCellDataCurrent++;
                ImGuiTableCellData cellData = table.RowCellData[table.RowCellDataCurrent];
                cellData.BgColor = color;
                cellData.Column = (short)columnN;
                break;
            }
            case ImGuiTableBgTarget.RowBg0:
            case ImGuiTableBgTarget.RowBg1:
            {
                if (table.RowPosY1 > table.InnerClipRect.Max.Y) // Discard; the row is below the table.
                    return;
                ImAssert.Check(columnN == -1);
                int bgIdx = target == ImGuiTableBgTarget.RowBg1 ? 1 : 0;
                table.RowBgColor[bgIdx] = color;
                break;
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiTableBgTarget value.");
                break;
        }
    }

    #endregion

    #region Tables: Columns width management

    /// <summary>
    /// Computes the maximum content width a column may occupy at its current position.
    /// </summary>
    /// <param name="table">Table being laid out.</param>
    /// <param name="columnN">Column index.</param>
    /// <returns>The maximum width, or <see cref="float.MaxValue"/> when unbounded.</returns>
    /// <remarks>
    /// Frozen columns under horizontal scrolling are bounded so they cannot reach beyond the visible
    /// region, and non-scrolling tables reserve room for every remaining enabled column so that all
    /// columns stay visible.
    /// </remarks>
    internal static float TableCalcMaxColumnWidth(ImGuiTable table, int columnN)
    {
        ImGuiTableColumn column = table.Columns[columnN];
        float maxWidth = float.MaxValue;
        float minColumnDistance = table.MinColumnWidth + table.CellPaddingX * 2.0f + table.CellSpacingX1 + table.CellSpacingX2;
        if ((table.Flags & ImGuiTableFlags.ScrollX) != 0)
        {
            if (column.DisplayOrder < table.FreezeColumnsRequest)
            {
                maxWidth = (table.InnerClipRect.Max.X - (table.FreezeColumnsRequest - column.DisplayOrder) * minColumnDistance) - column.MinX;
                maxWidth = maxWidth - table.OuterPaddingX - table.CellPaddingX - table.CellSpacingX2;
            }
        }
        else if ((table.Flags & ImGuiTableFlags.NoKeepColumnsVisible) == 0)
        {
            maxWidth = table.WorkRect.Max.X - (table.ColumnsEnabledCount - column.IndexWithinEnabledSet - 1) * minColumnDistance - column.MinX;
            maxWidth -= table.CellSpacingX2;
            maxWidth -= table.CellPaddingX * 2.0f;
            maxWidth -= table.OuterPaddingX;
        }
        return maxWidth;
    }

    /// <summary>
    /// Computes the automatic width of a column from its recorded content extents.
    /// </summary>
    /// <param name="table">Table being laid out.</param>
    /// <param name="column">Column whose automatic width is computed.</param>
    /// <returns>The automatic width, never smaller than the minimum column width.</returns>
    /// <remarks>
    /// The body extent and header extent are combined unless <c>NoHeaderWidth</c> excludes headers.
    /// Non-resizable fixed columns preserve an explicitly requested width.
    /// </remarks>
    internal static float TableGetColumnWidthAuto(ImGuiTable table, ImGuiTableColumn column)
    {
        float contentWidthBody = MathF.Max(column.ContentMaxXFrozen, column.ContentMaxXUnfrozen) - column.WorkMinX;
        float contentWidthHeaders = column.ContentMaxXHeadersIdeal - column.WorkMinX;
        float widthAuto = contentWidthBody;
        if ((column.Flags & ImGuiTableColumnFlags.NoHeaderWidth) == 0)
            widthAuto = MathF.Max(widthAuto, contentWidthHeaders);

        if ((column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0 && column.InitStretchWeightOrWidth > 0.0f)
        {
            if ((table.Flags & ImGuiTableFlags.Resizable) == 0 || (column.Flags & ImGuiTableColumnFlags.NoResize) != 0)
                widthAuto = column.InitStretchWeightOrWidth;
        }

        return MathF.Max(widthAuto, table.MinColumnWidth);
    }

    /// <summary>
    /// Requests a new width for a column, redistributing the change so the total width is preserved.
    /// </summary>
    /// <param name="columnN">Column index.</param>
    /// <param name="width">Requested inner column width, excluding padding.</param>
    /// <remarks>
    /// The width is clamped to the minimum column width and the column's position-dependent maximum.
    /// Resizing a fixed column that does not follow a stretch column is an offsetting resize; other
    /// cases move width between the column and its next (or, when absent, previous) enabled sibling and
    /// recompute stretch weights.
    /// </remarks>
    internal static void TableSetColumnWidth(int columnN, float width)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null || table.IsLayoutLocked)
            return;
        ImAssert.Check(columnN >= 0 && columnN < table.ColumnsCount);
        ImGuiTableColumn column0 = table.Columns[columnN];
        float column0Width = width;

        // Constrain early, comparing both requested and given widths so a stuck column is not overwritten.
        ImAssert.Check(table.MinColumnWidth > 0.0f);
        float minWidth = table.MinColumnWidth;
        float maxWidth = MathF.Max(minWidth, column0.WidthMax); // Not TableCalcMaxColumnWidth(), which needs MinX from the last instance.
        column0Width = Math.Clamp(column0Width, minWidth, maxWidth);
        if (column0.WidthGiven == column0Width || column0.WidthRequest == column0Width)
            return;

        ImGuiTableColumn? column1 = column0.NextEnabledColumn != -1 ? table.Columns[column0.NextEnabledColumn] : null;

        // The offsetting path covers all-fixed tables and fixed columns that precede every stretch
        // column; it simply rewrites the requested width.
        if ((column0.Flags & ImGuiTableColumnFlags.WidthFixed) != 0)
        {
            if (column1 == null || table.LeftMostStretchedColumn == -1 || table.Columns[table.LeftMostStretchedColumn].DisplayOrder >= column0.DisplayOrder)
            {
                column0.WidthRequest = column0Width;
                table.IsSettingsDirty = true;
                return;
            }
        }

        // Fall back to the previous column, used when auto-fitting the right-most stretch column.
        if (column1 == null)
            column1 = column0.PrevEnabledColumn != -1 ? table.Columns[column0.PrevEnabledColumn] : null;
        if (column1 == null)
            return;

        // Preserve the pair's total width while moving the requested change.
        float column1Width = MathF.Max(column1.WidthRequest - (column0Width - column0.WidthRequest), minWidth);
        column0Width = column0.WidthRequest + column1.WidthRequest - column1Width;
        ImAssert.Check(column0Width > 0.0f && column1Width > 0.0f);
        column0.WidthRequest = column0Width;
        column1.WidthRequest = column1Width;
        if (((column0.Flags | column1.Flags) & ImGuiTableColumnFlags.WidthStretch) != 0)
            TableUpdateColumnsWeightFromWidth(table);
        table.IsSettingsDirty = true;
    }

    /// <summary>
    /// Queues an auto-fit for a single column, to be applied on the next layout.
    /// </summary>
    /// <param name="table">Table that owns the column.</param>
    /// <param name="columnN">Column index.</param>
    internal static void TableSetColumnWidthAutoSingle(ImGuiTable table, int columnN)
    {
        ImGuiTableColumn column = table.Columns[columnN];
        if (!column.IsEnabled)
            return;
        column.CannotSkipItemsQueue = 1 << 0;
        table.AutoFitSingleColumn = (short)columnN;
    }

    /// <summary>
    /// Queues an auto-fit for every enabled or stretch column.
    /// </summary>
    /// <param name="table">Table whose columns are auto-fitted.</param>
    /// <remarks>
    /// Hidden stretch columns are included because resetting their weight would otherwise be
    /// impossible.
    /// </remarks>
    internal static void TableSetColumnWidthAutoAll(ImGuiTable table)
    {
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            ImGuiTableColumn column = table.Columns[columnN];
            if (!column.IsEnabled && (column.Flags & ImGuiTableColumnFlags.WidthStretch) == 0)
                continue;
            column.CannotSkipItemsQueue = 1 << 0;
            column.AutoFitQueue = 1 << 1;
        }
    }

    /// <summary>
    /// Recomputes stretch weights from the columns' current requested widths so their relative
    /// proportions are preserved.
    /// </summary>
    /// <param name="table">Table whose stretch columns are reweighted.</param>
    internal static void TableUpdateColumnsWeightFromWidth(ImGuiTable table)
    {
        ImAssert.Check(table.LeftMostStretchedColumn != -1 && table.RightMostStretchedColumn != -1);

        // Measure the existing visible weight and width.
        float visibleWeight = 0.0f;
        float visibleWidth = 0.0f;
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            ImGuiTableColumn column = table.Columns[columnN];
            if (!column.IsEnabled || (column.Flags & ImGuiTableColumnFlags.WidthStretch) == 0)
                continue;
            ImAssert.Check(column.StretchWeight > 0.0f);
            visibleWeight += column.StretchWeight;
            visibleWidth += column.WidthRequest;
        }
        ImAssert.Check(visibleWeight > 0.0f && visibleWidth > 0.0f);

        // Reapply the same total weight distributed by the new widths.
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            ImGuiTableColumn column = table.Columns[columnN];
            if (!column.IsEnabled || (column.Flags & ImGuiTableColumnFlags.WidthStretch) == 0)
                continue;
            column.StretchWeight = (column.WidthRequest / visibleWidth) * visibleWeight;
            ImAssert.Check(column.StretchWeight > 0.0f);
        }
    }

    #endregion

    #region Tables: Drawing

    /// <summary>
    /// Switches the draw list to the per-row background channel above the frozen row region, saving the
    /// window's clip rectangle.
    /// </summary>
    /// <remarks>
    /// The background channel is widget-facing, so it relies on the regular clip rectangle rather than
    /// the manually clipped background channels used for rows, cells, and borders.
    /// </remarks>
    internal static void TablePushBackgroundChannel()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiTable table = g.CurrentTable!;
        table.HostBackupInnerClipRect = window.ClipRect;
        SetWindowClipRectBeforeSetChannel(window, table.Bg2ClipRectForDrawCmd);
        table.DrawSplitter!.SetCurrentChannel(window.DrawList, table.Bg2DrawChannelCurrent);
    }

    /// <summary>
    /// Restores the window clip rectangle saved by <see cref="TablePushBackgroundChannel"/> and returns
    /// to the current column's channel.
    /// </summary>
    internal static void TablePopBackgroundChannel()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiTable table = g.CurrentTable!;
        SetWindowClipRectBeforeSetChannel(window, table.HostBackupInnerClipRect);
        table.DrawSplitter!.SetCurrentChannel(window.DrawList, table.Columns[table.CurrentColumn].DrawChannelCurrent);
    }

    /// <summary>
    /// Switches the draw list to a column's clip rectangle and draw channel.
    /// </summary>
    /// <param name="columnN">Column whose channel becomes current.</param>
    /// <remarks>
    /// The function is a no-op when the table disables clipping, because all columns then share the
    /// no-clip channel.
    /// </remarks>
    internal static void TablePushColumnChannel(int columnN)
    {
        ImGuiContext g = GetContext();
        ImGuiTable table = g.CurrentTable!;
        if ((table.Flags & ImGuiTableFlags.NoClip) != 0)
            return;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiTableColumn column = table.Columns[columnN];
        SetWindowClipRectBeforeSetChannel(window, column.ClipRect);
        table.DrawSplitter!.SetCurrentChannel(window.DrawList, column.DrawChannelCurrent);
    }

    /// <summary>
    /// Restores the current column's clip rectangle and draw channel.
    /// </summary>
    /// <remarks>
    /// The function is a no-op when clipping is disabled or when no column is current, the latter
    /// because <c>TreePop()</c> may be called after <c>TableNextRow()</c>.
    /// </remarks>
    internal static void TablePopColumnChannel()
    {
        ImGuiContext g = GetContext();
        ImGuiTable table = g.CurrentTable!;
        if ((table.Flags & ImGuiTableFlags.NoClip) != 0 || table.CurrentColumn == -1)
            return;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiTableColumn column = table.Columns[table.CurrentColumn];
        SetWindowClipRectBeforeSetChannel(window, column.ClipRect);
        table.DrawSplitter!.SetCurrentChannel(window.DrawList, column.DrawChannelCurrent);
    }

    /// <summary>
    /// Allocates the draw channels that isolate row backgrounds, per-column content, the widget-facing
    /// background channel, and any dummy channel, and initializes the background clip rectangles.
    /// </summary>
    /// <param name="table">Table whose channels are set up.</param>
    /// <remarks>
    /// Channels follow storage order so that reordering columns does not inflate dormant memory. When
    /// rows are frozen each column owns two channels: one before the freeze line and one after.
    /// </remarks>
    internal static void TableSetupDrawChannels(ImGuiTable table)
    {
        int freezeRowMultiplier = table.FreezeRowsCount > 0 ? 2 : 1;
        int channelsForRow = (table.Flags & ImGuiTableFlags.NoClip) != 0 ? 1 : table.ColumnsEnabledCount;
        int channelsForBg = 1 + 1 * freezeRowMultiplier;
        bool masksDiffer = false;
        for (int n = 0; n < table.ColumnsCount; n++)
        {
            if (table.VisibleMaskByIndex.TestBit(n) != table.EnabledMaskByIndex.TestBit(n))
            {
                masksDiffer = true;
                break;
            }
        }
        int channelsForDummy = (table.ColumnsEnabledCount < table.ColumnsCount || masksDiffer) ? 1 : 0;
        int channelsTotal = channelsForBg + (channelsForRow * freezeRowMultiplier) + channelsForDummy;
        table.DrawSplitter!.Split(table.InnerWindow!.DrawList, channelsTotal);
        table.DummyDrawChannel = (ushort)(channelsForDummy > 0 ? channelsTotal - 1 : -1);
        table.Bg2DrawChannelCurrent = TableDrawChannelBg2Frozen;
        table.Bg2DrawChannelUnfrozen = (ushort)(table.FreezeRowsCount > 0 ? 2 + channelsForRow : TableDrawChannelBg2Frozen);

        int drawChannelCurrent = 2;
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            ImGuiTableColumn column = table.Columns[columnN];
            if (column.IsVisibleX && column.IsVisibleY)
            {
                column.DrawChannelFrozen = (ushort)drawChannelCurrent;
                column.DrawChannelUnfrozen = (ushort)(drawChannelCurrent + (table.FreezeRowsCount > 0 ? channelsForRow + 1 : 0));
                if ((table.Flags & ImGuiTableFlags.NoClip) == 0)
                    drawChannelCurrent++;
            }
            else
            {
                column.DrawChannelFrozen = column.DrawChannelUnfrozen = table.DummyDrawChannel;
            }
            column.DrawChannelCurrent = column.DrawChannelFrozen;
        }

        // The background clip rectangles start matching the host so background geometry can merge by
        // default; the frozen region shrinks them when rows unfreeze.
        table.BgClipRect = table.InnerClipRect;
        table.Bg0ClipRectForDrawCmd = table.OuterWindow!.ClipRect;
        table.Bg2ClipRectForDrawCmd = table.HostClipRect;
        ImAssert.Check(table.BgClipRect.Min.Y <= table.BgClipRect.Max.Y);
    }

    /// <summary>
    /// Reorders and retargets the per-column draw channels into up to four merge groups so the
    /// splitter's merge pass can coalesce them into fewer draw calls.
    /// </summary>
    /// <param name="table">Table whose channels are merged.</param>
    /// <remarks>
    /// Four groups are distinguished by whether a row freeze and a column freeze are active. A channel
    /// joins a group only when it holds exactly one draw command and its content did not stray past the
    /// column clip rectangle. This function only reorders channels and overwrites their clip
    /// rectangles; the splitter's own merge performs the coalescing.
    /// </remarks>
    internal static void TableMergeDrawChannels(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        ImDrawListSplitter splitter = table.DrawSplitter!;
        bool hasFreezeV = table.FreezeRowsCount > 0;
        bool hasFreezeH = table.FreezeColumnsCount > 0;
        ImAssert.Check(splitter.CurrentChannel == 0);

        // Track the clip rectangle and member count of each merge group, plus a working membership mask.
        // Canonical allocates these from the shared temporary buffer; the port uses sized bit vectors.
        int maxDrawChannels = 4 + table.ColumnsCount * 2;
        ImRect[] mergeClipRects =
        {
            new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue),
            new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue),
            new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue),
            new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue),
        };
        int[] mergeChannelsCounts = new int[4];
        ImBitVector[] mergeMasks = new ImBitVector[4];
        for (int n = 0; n < 4; n++)
        {
            mergeMasks[n] = new ImBitVector();
            mergeMasks[n].Create(maxDrawChannels);
        }
        ImBitVector remainingMask = new ImBitVector();
        remainingMask.Create(maxDrawChannels);
        int mergeGroupMask = 0;

        // 1. Scan the channels and note which ones can be merged.
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            if (!table.VisibleMaskByIndex.TestBit(columnN))
                continue;
            ImGuiTableColumn column = table.Columns[columnN];

            int mergeGroupSubCount = hasFreezeV ? 2 : 1;
            for (int mergeGroupSubN = 0; mergeGroupSubN < mergeGroupSubCount; mergeGroupSubN++)
            {
                int channelNo = mergeGroupSubN == 0 ? column.DrawChannelFrozen : column.DrawChannelUnfrozen;

                // A channel with several draw commands is left alone; a trailing empty command is dropped
                // first, mirroring PopUnusedDrawCmd().
                ImDrawChannel srcChannel = splitter.Channels[channelNo];
                if (srcChannel.CmdBuffer.Count > 0 && srcChannel.CmdBuffer[^1].ElemCount == 0 && srcChannel.CmdBuffer[^1].UserCallback == null)
                    srcChannel.CmdBuffer.RemoveAt(srcChannel.CmdBuffer.Count - 1);
                if (srcChannel.CmdBuffer.Count != 1)
                    continue;

                // The channel can merge only when its content did not stray past its clip rectangle.
                if ((column.Flags & ImGuiTableColumnFlags.NoClip) == 0)
                {
                    float contentMaxX;
                    if (!hasFreezeV)
                        contentMaxX = MathF.Max(column.ContentMaxXUnfrozen, column.ContentMaxXHeadersUsed);
                    else if (mergeGroupSubN == 0)
                        contentMaxX = MathF.Max(column.ContentMaxXFrozen, column.ContentMaxXHeadersUsed);
                    else
                        contentMaxX = column.ContentMaxXUnfrozen;
                    if (contentMaxX > column.ClipRect.Max.X)
                        continue;
                }

                int mergeGroupN = (hasFreezeH && columnN < table.FreezeColumnsCount ? 0 : 1)
                    + (hasFreezeV && mergeGroupSubN == 0 ? 0 : 2);
                ImAssert.Check(channelNo < maxDrawChannels);
                mergeMasks[mergeGroupN].SetBit(channelNo);
                mergeChannelsCounts[mergeGroupN]++;
                mergeClipRects[mergeGroupN].Add(srcChannel.CmdBuffer[0].ClipRect);
                mergeGroupMask |= 1 << mergeGroupN;
            }

            // Invalidate the current channel; the frozen and unfrozen indices are kept for inspection.
            column.DrawChannelCurrent = unchecked((ushort)(-1));
        }

        // 2. Rewrite the channel list in the preferred order.
        if (mergeGroupMask != 0)
        {
            // Channels 0 (row background) and 1 (frozen background) never move.
            const int leadingDrawChannels = 2;
            List<ImDrawChannel> tempChannels = g.DrawChannelsTempMergeBuffer;
            int needed = splitter.ChannelCount - leadingDrawChannels;
            while (tempChannels.Count < needed)
                tempChannels.Add(new ImDrawChannel());
            int tempWrite = 0;

            for (int n = leadingDrawChannels; n < splitter.ChannelCount; n++)
                remainingMask.SetBit(n);
            remainingMask.ClearBit(table.Bg2DrawChannelUnfrozen);
            ImAssert.Check(!hasFreezeV || table.Bg2DrawChannelUnfrozen != TableDrawChannelBg2Frozen);
            int remainingCount = splitter.ChannelCount - (hasFreezeV ? leadingDrawChannels + 1 : leadingDrawChannels);
            ImRect hostRect = table.HostClipRect;

            for (int mergeGroupN = 0; mergeGroupN < 4; mergeGroupN++)
            {
                if (mergeChannelsCounts[mergeGroupN] > 0)
                {
                    int mergeChannelsCount = mergeChannelsCounts[mergeGroupN];
                    ImRect mergeClipRect = mergeClipRects[mergeGroupN];

                    // Extend the group outward to the host extents so outer-padding offsets do not prevent
                    // otherwise compatible channels from merging.
                    if ((mergeGroupN & 1) == 0 || !hasFreezeH)
                        mergeClipRect.Min.X = MathF.Min(mergeClipRect.Min.X, hostRect.Min.X);
                    if ((mergeGroupN & 2) == 0 || !hasFreezeV)
                        mergeClipRect.Min.Y = MathF.Min(mergeClipRect.Min.Y, hostRect.Min.Y);
                    if ((mergeGroupN & 1) != 0)
                        mergeClipRect.Max.X = MathF.Max(mergeClipRect.Max.X, hostRect.Max.X);
                    if ((mergeGroupN & 2) != 0 && (table.Flags & ImGuiTableFlags.NoHostExtendY) == 0)
                        mergeClipRect.Max.Y = MathF.Max(mergeClipRect.Max.Y, hostRect.Max.Y);

                    remainingCount -= mergeChannelsCount;
                    for (int n = 0; n < splitter.ChannelCount; n++)
                    {
                        if (mergeMasks[mergeGroupN].TestBit(n))
                            remainingMask.ClearBit(n);
                    }
                    for (int n = 0; n < splitter.ChannelCount && mergeChannelsCount != 0; n++)
                    {
                        if (!mergeMasks[mergeGroupN].TestBit(n))
                            continue;
                        mergeMasks[mergeGroupN].ClearBit(n);
                        mergeChannelsCount--;

                        ImDrawChannel channel = splitter.Channels[n];
                        ImAssert.Check(channel.CmdBuffer.Count == 1 && mergeClipRect.Contains(new ImRect(channel.CmdBuffer[0].ClipRect)));
                        ImDrawCmd cmd = channel.CmdBuffer[0];
                        cmd.ClipRect = new ImVec4(mergeClipRect.Min.X, mergeClipRect.Min.Y, mergeClipRect.Max.X, mergeClipRect.Max.Y);
                        channel.CmdBuffer[0] = cmd;

                        ImDrawChannel destination = tempChannels[tempWrite++];
                        destination.CmdBuffer = channel.CmdBuffer;
                        destination.IdxBuffer = channel.IdxBuffer;
                    }
                }

                // The unfrozen background channel belongs between the two frozen/unfrozen column groups.
                if (mergeGroupN == 1 && hasFreezeV)
                {
                    ImDrawChannel bg2Channel = splitter.Channels[table.Bg2DrawChannelUnfrozen];
                    ImDrawChannel destination = tempChannels[tempWrite++];
                    destination.CmdBuffer = bg2Channel.CmdBuffer;
                    destination.IdxBuffer = bg2Channel.IdxBuffer;
                }
            }

            // Append the channels that could not be merged, preserving their relative order.
            for (int n = 0; n < splitter.ChannelCount && remainingCount != 0; n++)
            {
                if (!remainingMask.TestBit(n))
                    continue;
                ImDrawChannel channel = splitter.Channels[n];
                ImDrawChannel destination = tempChannels[tempWrite++];
                destination.CmdBuffer = channel.CmdBuffer;
                destination.IdxBuffer = channel.IdxBuffer;
                remainingCount--;
            }

            // Copy the reordered buffers back after the two leading channels. The temporary wrappers are
            // distinct objects, so swapping references cannot alias a buffer that is still being read.
            for (int n = 0; n < needed; n++)
            {
                splitter.Channels[leadingDrawChannels + n].CmdBuffer = tempChannels[n].CmdBuffer;
                splitter.Channels[leadingDrawChannels + n].IdxBuffer = tempChannels[n].IdxBuffer;
            }
        }
    }

    /// <summary>
    /// Selects the color of the border to the right of a column: active while resized, hovered while
    /// hovered, strong at the frozen separator or when body borders are suppressed, else light.
    /// </summary>
    /// <param name="table">Table being drawn.</param>
    /// <param name="orderN">Display order of the column.</param>
    /// <param name="columnN">Index of the column.</param>
    /// <returns>The packed border color.</returns>
    private static uint TableGetColumnBorderCol(ImGuiTable table, int orderN, int columnN)
    {
        bool isHovered = table.HoveredColumnBorder == columnN;
        bool isResized = table.ResizedColumn == columnN && table.InstanceInteracted == table.InstanceCurrent;
        bool isFrozenSeparator = table.FreezeColumnsCount == orderN + 1;
        if (isResized || isHovered)
            return GetColorU32(isResized ? ImGuiCol.SeparatorActive : ImGuiCol.SeparatorHovered);
        if (isFrozenSeparator || (table.Flags & (ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.NoBordersInBodyUntilResize)) != 0)
            return table.BorderColorStrong;
        return table.BorderColorLight;
    }

    /// <summary>
    /// Draws the inner vertical borders with resize feedback, the outer border, and the bottom-most row
    /// border.
    /// </summary>
    /// <param name="table">Table whose borders are drawn.</param>
    internal static void TableDrawBorders(ImGuiTable table)
    {
        ImGuiWindow innerWindow = table.InnerWindow!;
        if (!table.OuterWindow!.ClipRect.Overlaps(table.OuterRect))
            return;

        ImDrawList innerDrawList = innerWindow.DrawList;
        table.DrawSplitter!.SetCurrentChannel(innerDrawList, TableDrawChannelBg0);
        innerDrawList.PushClipRect(table.Bg0ClipRectForDrawCmd.Min, table.Bg0ClipRectForDrawCmd.Max, false);

        // Draw the inner borders and resize feedback.
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        const float borderSize = TableBorderSize;
        float drawY1 = MathF.Max(
            table.InnerRect.Min.Y,
            (table.FreezeRowsCount >= 1 ? table.InnerRect.Min.Y : table.WorkRect.Min.Y) + table.AngledHeadersHeight)
            + ((table.Flags & ImGuiTableFlags.BordersOuterH) != 0 ? 1.0f : 0.0f);
        float drawY2Body = table.InnerRect.Max.Y;
        float drawY2Head = table.IsUsingHeaders
            ? MathF.Min(table.InnerRect.Max.Y, (table.FreezeRowsCount >= 1 ? table.InnerRect.Min.Y : table.WorkRect.Min.Y) + tableInstance.LastTopHeadersRowHeight)
            : drawY1;
        if ((table.Flags & ImGuiTableFlags.BordersInnerV) != 0)
        {
            for (int orderN = 0; orderN < table.ColumnsCount; orderN++)
            {
                if (!table.EnabledMaskByDisplayOrder.TestBit(orderN))
                    continue;

                int columnN = table.DisplayOrderToIndex[orderN];
                ImGuiTableColumn column = table.Columns[columnN];
                bool isHovered = table.HoveredColumnBorder == columnN;
                bool isResized = table.ResizedColumn == columnN && table.InstanceInteracted == table.InstanceCurrent;
                bool isResizable = (column.Flags & (ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoDirectResize_)) == 0;
                bool isFrozenSeparator = table.FreezeColumnsCount == orderN + 1;
                if (column.MaxX > table.InnerClipRect.Max.X && !isResized)
                    continue;

                // Decide whether the right-most column border is worth drawing.
                if (column.NextEnabledColumn == -1 && !isResizable)
                {
                    if ((table.Flags & ImGuiTableFlags.SizingMask_) != ImGuiTableFlags.SizingFixedSame || (table.Flags & ImGuiTableFlags.NoHostExtendX) != 0)
                        continue;
                }
                if (column.MaxX <= column.ClipRect.Min.X)
                    continue;

                // The border is drawn into the inner window so the right-most column is not clipped.
                float drawY2 = drawY2Head;
                if (isFrozenSeparator)
                    drawY2 = drawY2Body;
                else if ((table.Flags & ImGuiTableFlags.NoBordersInBodyUntilResize) != 0 && (isHovered || isResized))
                    drawY2 = drawY2Body;
                else if ((table.Flags & (ImGuiTableFlags.NoBordersInBodyUntilResize | ImGuiTableFlags.NoBordersInBody)) == 0)
                    drawY2 = drawY2Body;
                if (drawY2 > drawY1)
                    innerDrawList.AddLineV(column.MaxX, drawY1, drawY2, TableGetColumnBorderCol(table, orderN, columnN), borderSize);
            }
        }

        // Draw the outer border, offset by one pixel so it is not hidden behind cells or scrollbars.
        if ((table.Flags & ImGuiTableFlags.BordersOuter) != 0)
        {
            ImRect outerBorder = table.OuterRect;
            uint outerCol = table.BorderColorStrong;
            if ((table.Flags & ImGuiTableFlags.BordersOuter) == ImGuiTableFlags.BordersOuter)
            {
                innerDrawList.AddRect(outerBorder.Min, outerBorder.Max, outerCol, 0.0f, borderSize);
            }
            else if ((table.Flags & ImGuiTableFlags.BordersOuterV) != 0)
            {
                innerDrawList.AddLineV(outerBorder.Min.X, outerBorder.Min.Y, outerBorder.Max.Y, outerCol, borderSize);
                innerDrawList.AddLineV(outerBorder.Max.X, outerBorder.Min.Y, outerBorder.Max.Y, outerCol, borderSize);
            }
            else if ((table.Flags & ImGuiTableFlags.BordersOuterH) != 0)
            {
                innerDrawList.AddLineH(outerBorder.Min.X, outerBorder.Max.X, outerBorder.Min.Y, outerCol, borderSize);
                innerDrawList.AddLineH(outerBorder.Min.X, outerBorder.Max.X, outerBorder.Max.Y - borderSize, outerCol, borderSize);
            }
        }

        // Draw the bottom-most row border when the row height stops above the outer border.
        if ((table.Flags & ImGuiTableFlags.BordersInnerH) != 0 && table.RowPosY2 < table.OuterRect.Max.Y)
        {
            float hInset = (table.Flags & ImGuiTableFlags.BordersOuterV) != 0 ? borderSize : 0.0f;
            float borderY = table.RowPosY2;
            if (borderY >= table.BgClipRect.Min.Y && borderY < table.BgClipRect.Max.Y)
                innerDrawList.AddLineH(table.BorderX1 + hInset, table.BorderX2 - hInset, borderY, table.BorderColorLight, borderSize);
        }

        innerDrawList.PopClipRect();
    }

    #endregion

    #endregion


    #region SECTION: COLUMNS, ROWS, CELLS, AND WIDTHS

    #region SECTION: Tables: Column setup and geometry

    /// <summary>
    /// Normalizes the flags supplied for one column, deriving the width policy, resize, sort, and
    /// indentation defaults from the owning table and rebuilding the ordered list of sort directions
    /// available to the column.
    /// </summary>
    /// <param name="table">Table that owns the column; its flags select the sizing policy and the
    /// resizable, sortable, and reorderable capabilities.</param>
    /// <param name="column">Column whose <see cref="ImGuiTableColumn.Flags"/> are replaced by the
    /// normalized value; its existing status bits are preserved.</param>
    /// <param name="flags">Raw flags passed to <c>TableSetupColumn()</c>, or
    /// <see cref="ImGuiTableColumnFlags.None"/> when the column was not resubmitted this frame.</param>
    /// <remarks>
    /// <para>
    /// The status bits (<see cref="ImGuiTableColumnFlags.StatusMask_"/>) describe the column's resolved
    /// state rather than a request, so they are carried over from the previous value while every other
    /// bit is recomputed. The width policy defaults to fixed under the fixed sizing policies and to
    /// stretch otherwise, matching the column sizing policy commentary at the top of the canonical file.
    /// </para>
    /// <para>
    /// The available-direction list is packed two bits per entry and is the source consulted by
    /// <see cref="TableGetColumnAvailSortDirection"/> and the header click path; it is empty unless the
    /// table is <see cref="ImGuiTableFlags.Sortable"/>.
    /// </para>
    /// </remarks>
    internal static void TableSetupColumnFlags(ImGuiTable table, ImGuiTableColumn column, ImGuiTableColumnFlags flags)
    {
        // Sizing policy: an explicit width flag wins; otherwise the table policy is inherited.
        if ((flags & ImGuiTableColumnFlags.WidthMask_) == 0)
        {
            ImGuiTableFlags tableSizingPolicy = table.Flags & ImGuiTableFlags.SizingMask_;
            if (tableSizingPolicy == ImGuiTableFlags.SizingFixedFit || tableSizingPolicy == ImGuiTableFlags.SizingFixedSame)
                flags |= ImGuiTableColumnFlags.WidthFixed;
            else
                flags |= ImGuiTableColumnFlags.WidthStretch;
        }
        else
        {
            // Only one of WidthStretch and WidthFixed may be selected.
            int widthFlags = (int)(flags & ImGuiTableColumnFlags.WidthMask_);
            ImAssert.Check(widthFlags != 0 && (widthFlags & (widthFlags - 1)) == 0);
        }

        // Resize is only reachable when the table itself is resizable.
        if ((table.Flags & ImGuiTableFlags.Resizable) == 0)
            flags |= ImGuiTableColumnFlags.NoResize;

        // Sorting: excluding both directions is equivalent to excluding sorting entirely.
        if ((flags & ImGuiTableColumnFlags.NoSortAscending) != 0 && (flags & ImGuiTableColumnFlags.NoSortDescending) != 0)
            flags |= ImGuiTableColumnFlags.NoSort;

        // Indentation defaults to enabled on the first column and disabled on the others.
        if ((flags & ImGuiTableColumnFlags.IndentMask_) == 0)
        {
            // Canonical resolves the index from the column pointer with index_from_ptr(); the port holds
            // columns in a list and resolves the index by identity. The scan is bounded by the column
            // count and this path runs once per declared column per layout, not per item.
            flags |= table.Columns.IndexOf(column) == 0
                ? ImGuiTableColumnFlags.IndentEnable
                : ImGuiTableColumnFlags.IndentDisable;
        }

        // Alignment defaulting is commented out in canonical. and is therefore
        // not reproduced here.

        // Preserve the resolved status bits while replacing the request bits.
        column.Flags = flags | (column.Flags & ImGuiTableColumnFlags.StatusMask_);

        // Build the ordered list of available sort directions, two bits per entry.
        column.SortDirectionsAvailCount = column.SortDirectionsAvailMask = column.SortDirectionsAvailList = 0;
        if ((table.Flags & ImGuiTableFlags.Sortable) != 0)
        {
            int count = 0;
            int mask = 0;
            int list = 0;
            if ((flags & ImGuiTableColumnFlags.PreferSortAscending) != 0 && (flags & ImGuiTableColumnFlags.NoSortAscending) == 0)
            {
                mask |= 1 << (int)ImGuiSortDirection.Ascending;
                list |= (int)ImGuiSortDirection.Ascending << (count << 1);
                count++;
            }
            if ((flags & ImGuiTableColumnFlags.PreferSortDescending) != 0 && (flags & ImGuiTableColumnFlags.NoSortDescending) == 0)
            {
                mask |= 1 << (int)ImGuiSortDirection.Descending;
                list |= (int)ImGuiSortDirection.Descending << (count << 1);
                count++;
            }
            if ((flags & ImGuiTableColumnFlags.PreferSortAscending) == 0 && (flags & ImGuiTableColumnFlags.NoSortAscending) == 0)
            {
                mask |= 1 << (int)ImGuiSortDirection.Ascending;
                list |= (int)ImGuiSortDirection.Ascending << (count << 1);
                count++;
            }
            if ((flags & ImGuiTableColumnFlags.PreferSortDescending) == 0 && (flags & ImGuiTableColumnFlags.NoSortDescending) == 0)
            {
                mask |= 1 << (int)ImGuiSortDirection.Descending;
                list |= (int)ImGuiSortDirection.Descending << (count << 1);
                count++;
            }
            if ((table.Flags & ImGuiTableFlags.SortTristate) != 0 || count == 0)
            {
                mask |= 1 << (int)ImGuiSortDirection.None;
                count++;
            }
            column.SortDirectionsAvailList = (byte)list;
            column.SortDirectionsAvailMask = (byte)mask;
            column.SortDirectionsAvailCount = (byte)count;
            TableFixColumnSortDirection(table, column);
        }
    }

    /// <summary>
    /// Applies one resolved column setup request to the live column at <paramref name="idx"/>: enforces
    /// the width/weight policy rules, counts angled headers, normalizes the flags, and stores the
    /// identity and size request.
    /// </summary>
    /// <param name="table">Table that owns the column.</param>
    /// <param name="idx">Live column index that receives the request.</param>
    /// <param name="id">Column identifier hashed from the label, or zero for an unnamed column.</param>
    /// <param name="nameOffset">Offset of the NUL-terminated name in the table's name buffer, or -1.</param>
    /// <param name="flags">Raw column flags as passed by the application.</param>
    /// <param name="initWidthOrWeight">Content width for a fixed column or weight for a stretched column;
    /// a value at or below zero means automatic.</param>
    /// <param name="userData">Opaque value surfaced through the sort specifications.</param>
    /// <remarks>
    /// <para>
    /// The sizing-policy guard reproduces the canonical check verbatim, including its use of the table
    /// <c>ScrollX</c> bit as seen through the column flag word. That test
    /// is intentionally preserved rather than corrected: the two enumerations share bit values, and the
    /// canonical condition is the specification.
    /// </para>
    /// <para>
    /// This is the single write point shared by the fast setup path and the reconcile path, so a column
    /// moved by a topology change receives exactly the same treatment as a freshly declared one.
    /// </para>
    /// </remarks>
    private static void TableSetupColumnApply(ImGuiTable table, int idx, uint id, short nameOffset, ImGuiTableColumnFlags flags, float initWidthOrWeight, uint userData)
    {
        ImGuiTableColumn column = table.Columns[idx];

        // A width or weight may only be supplied when a sizing policy was chosen explicitly, to avoid
        // silently storing a width where a weight is expected (or the reverse).
        if (table.IsDefaultSizingPolicy
            && (flags & ImGuiTableColumnFlags.WidthMask_) == 0
            && ((int)flags & (int)ImGuiTableFlags.ScrollX) == 0)
        {
            if (!(initWidthOrWeight <= 0.0f))
            {
                AssertUserError(false, "TableSetupColumn(): can only specify width/weight if sizing policy is set explicitly in either Table or Column.");
                return;
            }
        }

        // Supplying a width automatically enforces the fixed policy even though the flag normalization
        // below would otherwise default to stretch under a non-fixed table policy.
        if ((flags & ImGuiTableColumnFlags.WidthMask_) == 0 && initWidthOrWeight > 0.0f)
        {
            ImGuiTableFlags sizingPolicy = table.Flags & ImGuiTableFlags.SizingMask_;
            if (sizingPolicy == ImGuiTableFlags.SizingFixedFit || sizingPolicy == ImGuiTableFlags.SizingFixedSame)
                flags |= ImGuiTableColumnFlags.WidthFixed;
        }
        if ((flags & ImGuiTableColumnFlags.AngledHeader) != 0)
        {
            flags |= ImGuiTableColumnFlags.NoHeaderLabel;
            table.AngledHeadersCount++;
        }

        TableSetupColumnFlags(table, column, flags);
        column.ID = id;
        column.UserData = userData;
        column.NameOffset = nameOffset;
        column.InitStretchWeightOrWidth = initWidthOrWeight;
    }

    /// <summary>
    /// Declares one column of the table that is being submitted. Must be called after <c>BeginTable()</c>
    /// and before the first row.
    /// </summary>
    /// <param name="label">Column label. A null or empty label declares an unnamed column whose
    /// identifier is zero.</param>
    /// <param name="flags">Per-column flags; status bits
    /// (<see cref="ImGuiTableColumnFlags.StatusMask_"/>) must not be supplied.</param>
    /// <param name="initWidthOrWeight">Content width for a fixed column or weight for a stretched column;
    /// a value at or below zero requests an automatic size.</param>
    /// <param name="userData">Opaque identifier surfaced later as
    /// <see cref="ImGuiTableColumnSortSpecs.ColumnUserID"/>.</param>
    /// <remarks>
    /// <para>
    /// The label is appended to the table's contiguous name buffer together with its NUL terminator,
    /// exactly as canonical does, so <see cref="TableGetColumnName(ImGuiTable, int)"/> can decode it. A
    /// column whose identifier changes from the previous frame switches the table into reconcile mode:
    /// the request is deferred to <see cref="TableUpdateLayout"/>, which calls
    /// <see cref="TableReconcileColumns"/> so that a moved or renamed column keeps its previous width,
    /// order, and visibility. The common fast path applies the request immediately.
    /// </para>
    /// <para>
    /// The name offset is stored on the live column even on the deferred path so that
    /// <see cref="TableGetColumnName(int)"/> works between <c>BeginTable()</c> and layout.
    /// </para>
    /// </remarks>
    public static void TableSetupColumn(string? label, ImGuiTableColumnFlags flags = 0, float initWidthOrWeight = 0.0f, uint userData = 0)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        if (table.DeclColumnsCount >= table.ColumnsCount)
        {
            AssertUserError(false, "TableSetupColumn(): called too many times!");
            return;
        }
        if (table.IsLayoutLocked)
        {
            AssertUserError(false, "TableSetupColumn(): need to call before first row!");
            return;
        }
        ImAssert.Check((flags & ImGuiTableColumnFlags.StatusMask_) == 0);

        // Store the name, terminated with a NUL byte, in the shared contiguous buffer.
        short nameOffset = -1;
        if (!string.IsNullOrEmpty(label))
        {
            nameOffset = (short)table.ColumnsNames.Size;
            table.ColumnsNames.Append(label!);
            table.ColumnsNames.Append("\0");
        }
        uint columnId = !string.IsNullOrEmpty(label) ? ImHashStr(label!) : 0u;

        // When the identifier changes, or a column moved, defer the request until layout.
        int columnIdx = table.DeclColumnsCount++;
        ImGuiTableColumn column = table.Columns[columnIdx];

        if (!table.IsReconcileMode && column.ID != columnId && !table.IsNewTable)
        {
            table.IsReconcileMode = true;
            table.TempData!.ReconcileColumnsRequests.EnsureCapacity(table.ColumnsCount - columnIdx);
        }

        // Fast and common path: apply immediately and clear the pending-reconcile markers.
        if (!table.IsReconcileMode)
        {
            TableSetupColumnApply(table, columnIdx, columnId, nameOffset, flags, initWidthOrWeight, userData);
            column.IsNeedReconcileSrc = column.IsNeedReconcileDst = false;
            if (columnIdx < table.TempData!.OldColumnsData.Count)
                table.TempData!.OldColumnsData[columnIdx].IsNeedReconcileSrc = false;
            return;
        }

        // Reconcile path: record the request and apply it after the previous-frame columns have been
        // matched, because the live column list may have been truncated or partially reset already.
        ImGuiTableReconcileColumnData reconcileData = new();
        table.TempData!.ReconcileColumnsRequests.Add(reconcileData);
        reconcileData.ID = columnId;
        reconcileData.NameOffset = nameOffset;
        reconcileData.Flags = flags;
        reconcileData.InitWidthOrWeight = initWidthOrWeight;
        reconcileData.UserData = userData;
        reconcileData.ColumnNewIdx = (short)columnIdx;
        reconcileData.ColumnOldIdx = -1;
        column.NameOffset = nameOffset; // Allow TableGetColumnName() to work before layout.
        column.IsNeedReconcileSrc = column.IsNeedReconcileDst = true;
    }

    /// <summary>
    /// Matches the deferred column setup requests against the previous frame's columns and applies them,
    /// preserving each surviving column's geometry, order, visibility, and sort state across a topology
    /// change such as an insertion, removal, rename, or move.
    /// </summary>
    /// <param name="table">Table whose queued reconcile requests are processed.</param>
    /// <remarks>
    /// <para>
    /// Matching is performed in two passes, mirroring canonical: first every request with a non-zero
    /// identifier is matched against an unmatched source column with the same identifier, then the
    /// remaining requests and source columns are paired sequentially. Each request copies the matched
    /// source column into its snapshot holder with <see cref="ImGuiTableColumn.CopyFrom"/> because the
    /// port models a column as a reference type where canonical copies a value.
    /// </para>
    /// <para>
    /// The final pass restores the snapshot into the live column before re-applying the request, which is
    /// why the snapshot is retained rather than copied directly into the live column during matching.
    /// </para>
    /// </remarks>
    internal static void TableReconcileColumns(ImGuiTable table)
    {
        ImGuiTableTempData tempData = table.TempData!;

        List<ImGuiTableColumn> dstColumns = table.Columns;
        List<ImGuiTableColumn> srcColumns = tempData.OldColumnsData.Count == 0 ? table.Columns : tempData.OldColumnsData;
        List<ImGuiTableReconcileColumnData> reconcileRequests = tempData.ReconcileColumnsRequests;

        // Pass 1: match by column identifier.
        int matches = 0;
        for (int requestN = 0; requestN < reconcileRequests.Count; requestN++)
        {
            ImGuiTableReconcileColumnData reconcileData = reconcileRequests[requestN];
            if (reconcileData.ID == 0)
                continue;
            for (int srcN = 0; srcN < srcColumns.Count; srcN++)
            {
                ImGuiTableColumn srcColumn = srcColumns[srcN];
                if (srcColumn.ID == reconcileData.ID && srcColumn.IsNeedReconcileSrc)
                {
                    ImGuiTableColumn dstColumn = dstColumns[reconcileData.ColumnNewIdx];
                    srcColumn.IsNeedReconcileSrc = dstColumn.IsNeedReconcileDst = false;
                    reconcileData.ColumnOldIdx = (short)srcN;
                    reconcileData.ColumnOldData.CopyFrom(srcColumn);
                    matches++;
                    break;
                }
            }
        }

        // Pass 2: pair the remaining entries sequentially in their original order.
        int dstIdx = 0;
        if (matches != reconcileRequests.Count)
        {
            for (int srcN = 0; srcN < srcColumns.Count; srcN++)
            {
                ImGuiTableColumn srcColumn = srcColumns[srcN];
                if (!srcColumn.IsNeedReconcileSrc)
                    continue;
                while (dstIdx < reconcileRequests.Count && reconcileRequests[dstIdx].ColumnOldIdx != -1)
                    dstIdx++;
                if (dstIdx == reconcileRequests.Count)
                    break;
                ImGuiTableReconcileColumnData reconcileData = reconcileRequests[dstIdx];
                ImGuiTableColumn dstColumn = dstColumns[reconcileData.ColumnNewIdx];
                srcColumn.IsNeedReconcileSrc = dstColumn.IsNeedReconcileDst = false;
                reconcileData.ColumnOldIdx = (short)srcN;
                reconcileData.ColumnOldData.CopyFrom(srcColumn);
            }
        }

        // Final pass: restore each snapshot into the live column, then re-apply the request. Applying the
        // request after the restore is what keeps the resolved flags consistent with the restored state.
        for (int requestN = 0; requestN < reconcileRequests.Count; requestN++)
        {
            ImGuiTableReconcileColumnData reconcileData = reconcileRequests[requestN];
            // A request whose source was not found restores a default-constructed column, which marks it
            // as just created so the layout applies defaults for it.
            dstColumns[reconcileData.ColumnNewIdx].CopyFrom(reconcileData.ColumnOldData);
            TableSetupColumnApply(table, reconcileData.ColumnNewIdx, reconcileData.ID, reconcileData.NameOffset, reconcileData.Flags, reconcileData.InitWidthOrWeight, reconcileData.UserData);
            // Logged after the copy so the name that is reported is the one the new column now carries.
            DebugLogEvent(
                ImGuiDebugLogFlags.EventTable,
                "[table] - old {0} -> new {1} \"{2}\"\n",
                reconcileData.ColumnOldIdx,
                reconcileData.ColumnNewIdx,
                TableGetColumnName(table, reconcileData.ColumnNewIdx));
        }
        TableFixDisplayOrder(table);
        table.IsSettingsDirty = true;
        table.IsReconcileMode = false;
        reconcileRequests.Clear(); // The per-frame temporary data rejects further requests until next frame.
    }

    /// <summary>
    /// Configures the number of frozen columns and rows for the current table.
    /// </summary>
    /// <param name="columns">Number of leading columns to freeze; ignored unless the table scrolls
    /// horizontally.</param>
    /// <param name="rows">Number of leading rows to freeze; ignored unless the table scrolls
    /// vertically.</param>
    /// <remarks>
    /// <para>
    /// The request is stored separately from the active count: a frozen region collapses to zero while the
    /// corresponding scroll offset is zero, so the columns or rows behave as unfrozen at the scroll origin
    /// and freeze as soon as the user scrolls.
    /// </para>
    /// <para>
    /// <see cref="ImGuiTable.IsUnfrozenRows"/> is set here, before layout, because
    /// <c>ImGuiListClipper</c> consults it.
    /// </para>
    /// </remarks>
    public static void TableSetupScrollFreeze(int columns, int rows)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        ImAssert.Check(!table.IsLayoutLocked, "TableSetupScrollFreeze(): need to call before first row!");
        ImAssert.Check(columns >= 0 && columns < TableMaxColumns);
        ImAssert.Check(rows >= 0 && rows < 128); // Arbitrary canonical limit.

        ImGuiWindow innerWindow = table.InnerWindow!;
        table.FreezeColumnsRequest = (table.Flags & ImGuiTableFlags.ScrollX) != 0
            ? (short)Math.Min(columns, table.ColumnsCount)
            : (short)0;
        table.FreezeColumnsCount = innerWindow.Scroll.X != 0.0f ? table.FreezeColumnsRequest : (short)0;
        table.FreezeRowsRequest = (table.Flags & ImGuiTableFlags.ScrollY) != 0 ? (short)rows : (short)0;
        table.FreezeRowsCount = innerWindow.Scroll.Y != 0.0f ? table.FreezeRowsRequest : (short)0;
        table.IsUnfrozenRows = table.FreezeRowsCount == 0;
    }

    #endregion

    #region SECTION: Tables: Row changes

    /// <summary>
    /// Starts a new row, locking layout on the first call after <c>BeginTable()</c> and closing the
    /// previous row when one is open.
    /// </summary>
    /// <param name="rowFlags">Row flags; <see cref="ImGuiTableRowFlags.Headers"/> marks a header row,
    /// which receives the header background and does not advance the alternating-row counter.</param>
    /// <param name="rowMinHeight">Minimum row height honored by the layout; the actual height can be
    /// larger because per-cell clipping prevents a guaranteed maximum.</param>
    /// <remarks>
    /// <para>
    /// The vertical cell padding is re-read from the current style on every row, which is why changing
    /// <c>Style.CellPadding</c> between rows takes effect immediately.
    /// </para>
    /// <para>
    /// Output is disabled until the application enters a column, so stray items submitted between
    /// <c>TableNextRow()</c> and the first column are ignored rather than leaked into the table.
    /// </para>
    /// </remarks>
    public static void TableNextRow(ImGuiTableRowFlags rowFlags = 0, float rowMinHeight = 0.0f)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        // Canonical asserts that TableNextRow is only called within BeginTable scope.
        AssertUserError(table != null, "TableNextRow() call should only be done while in BeginTable() scope!");
        if (table == null)
            return;

        if (!table.IsLayoutLocked)
            TableUpdateLayout(table);
        if (table.IsInsideRow)
            TableEndRow(table);

        table.LastRowFlags = table.RowFlags;
        table.RowFlags = rowFlags;
        table.RowCellPaddingY = g.Style.CellPadding.Y;
        table.RowMinHeight = rowMinHeight;
        TableBeginRow(table);

        // Honor the requested minimum height. A per-row maximum cannot be guaranteed because that would
        // require a unique clipping rectangle per cell.
        table.RowPosY2 += table.RowCellPaddingY * 2.0f;
        table.RowPosY2 = MathF.Max(table.RowPosY2, table.RowPosY1 + rowMinHeight);

        table.InnerWindow!.SkipItems = true;
    }

    /// <summary>
    /// Begins row bookkeeping after <see cref="TableNextRow"/> has resolved the row flags and padding:
    /// advances the row index, clears the per-row cell background and line state, locks the row indent,
    /// and applies the header background for header rows.
    /// </summary>
    /// <param name="table">Table whose new row begins.</param>
    /// <remarks>
    /// The line state is reset so that <c>SameLine()</c> can be used to share a line between columns,
    /// including from the first column. When the table has frozen rows, the first row is anchored at the
    /// outer rectangle top rather than continuing from the previous cursor position.
    /// </remarks>
    internal static void TableBeginRow(ImGuiTable table)
    {
        ImGuiWindow window = table.InnerWindow!;
        ImAssert.Check(!table.IsInsideRow);

        // New row.
        table.CurrentRow++;
        table.CurrentColumn = -1;
        table.RowBgColor[0] = table.RowBgColor[1] = TableColorDisable;
        table.RowCellDataCurrent = -1;
        table.IsInsideRow = true;

        // Begin frozen rows: the first frozen row restarts at the outer rectangle top.
        float nextY1 = table.RowPosY2;
        if (table.CurrentRow == 0 && table.FreezeRowsCount > 0)
        {
            nextY1 = table.OuterRect.Min.Y;
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, nextY1);
        }

        table.RowPosY1 = table.RowPosY2 = nextY1;
        table.RowTextBaseline = 0.0f;
        table.RowIndentOffsetX = window.DC.Indent.X - table.HostIndentX; // Lock the row indent.

        window.DC.PrevLineTextBaseOffset = 0.0f;
        // This allows SameLine() to share the line size between columns.
        window.DC.CursorPosPrevLine = new ImVec2(window.DC.CursorPos.X, window.DC.CursorPos.Y + table.RowCellPaddingY);
        window.DC.PrevLineSize = window.DC.CurrLineSize = new ImVec2(0.0f, 0.0f);
        window.DC.IsSameLine = window.DC.IsSetPos = false;
        window.DC.CursorMaxPos = new ImVec2(window.DC.CursorMaxPos.X, nextY1);

        // A non-transparent header background lets repeated overlays blend while the header is dragged.
        if ((table.RowFlags & ImGuiTableRowFlags.Headers) != 0)
        {
            TableSetBgColor(ImGuiTableBgTarget.RowBg0, GetColorU32(ImGuiCol.TableHeaderBg));
            if (table.CurrentRow == 0)
                table.IsUsingHeaders = true;
        }
    }

    /// <summary>
    /// Closes the current row: ends the open cell, positions the cursor at the row bottom, draws the row
    /// and cell backgrounds and the row borders, performs the frozen-row to unfrozen-row transition, and
    /// advances the alternating-row counter.
    /// </summary>
    /// <param name="table">Table whose current row is finalized.</param>
    /// <remarks>
    /// <para>
    /// The frozen-row transition happens here rather than in <see cref="TableBeginRow"/> so that
    /// <c>ImGuiListClipper</c> observes the row end and the new cursor position. While unfreezing, the
    /// clip rectangles and every column's draw channel switch from the frozen to the unfrozen variant and
    /// the row's Y coordinates are translated from the frozen origin into the scrolled coordinate space.
    /// </para>
    /// <para>
    /// Backgrounds and borders are clipped on the CPU so a single draw-command clip rectangle can serve
    /// them all. Cell backgrounds are drawn from the transient requests recorded by
    /// <see cref="TableSetBgColor"/> during the row.
    /// </para>
    /// </remarks>
    internal static void TableEndRow(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        ImAssert.Check(window == table.InnerWindow);
        ImAssert.Check(table.IsInsideRow);

        if (table.CurrentColumn != -1)
        {
            TableEndCell(table);
            table.CurrentColumn = -1;
        }

        // Logging.: separate the row from the next one in the capture.
        if (g.LogEnabled)
            LogRenderedText(null, "|");

        // Position the cursor at the row bottom so clipping calculations can use it. The next
        // TableBeginCell() repositions it to account for vertical padding.
        window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, table.RowPosY2);

        // Row background fill.
        float bgY1 = table.RowPosY1;
        float bgY2 = table.RowPosY2;
        bool unfreezeRowsActual = table.CurrentRow + 1 == table.FreezeRowsCount;
        bool unfreezeRowsRequest = table.CurrentRow + 1 == table.FreezeRowsRequest;
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        if ((table.RowFlags & ImGuiTableRowFlags.Headers) != 0
            && (table.CurrentRow == 0 || (table.LastRowFlags & ImGuiTableRowFlags.Headers) != 0))
        {
            tableInstance.LastTopHeadersRowHeight += bgY2 - bgY1;
        }

        bool isVisible = bgY2 >= table.InnerClipRect.Min.Y && bgY1 <= table.InnerClipRect.Max.Y;
        if (isVisible)
        {
            // Update the data consumed by TableGetHoveredRow(), which intentionally lags one frame.
            if (table.HoveredColumnBody != -1
                && g.IO.MousePos.Y >= bgY1
                && g.IO.MousePos.Y < bgY2
                && tableInstance.HoveredRowNext < 0)
            {
                tableInstance.HoveredRowNext = table.CurrentRow;
            }

            // Decide the row background colors: explicit requests win over the alternating row colors.
            uint bgCol0 = 0;
            uint bgCol1 = 0;
            if (table.RowBgColor[0] != TableColorDisable)
                bgCol0 = table.RowBgColor[0];
            else if ((table.Flags & ImGuiTableFlags.RowBg) != 0)
                bgCol0 = GetColorU32((table.RowBgColorCounter & 1) != 0 ? ImGuiCol.TableRowBgAlt : ImGuiCol.TableRowBg);
            if (table.RowBgColor[1] != TableColorDisable)
                bgCol1 = table.RowBgColor[1];

            // Decide the top border color: a header above the row makes the separation strong.
            uint topBorderCol = 0;
            float borderSize = TableBorderSize;
            if (table.CurrentRow > 0 && (table.Flags & ImGuiTableFlags.BordersInnerH) != 0)
            {
                topBorderCol = (table.LastRowFlags & ImGuiTableRowFlags.Headers) != 0
                    ? table.BorderColorStrong
                    : table.BorderColorLight;
            }

            bool drawCellBgColor = table.RowCellDataCurrent >= 0;
            bool drawStrongBottomBorder = unfreezeRowsActual;
            if ((bgCol0 | bgCol1 | topBorderCol) != 0 || drawStrongBottomBorder || drawCellBgColor)
            {
                // TableEndRow() is always followed by a clipping-rectangle change, so the smallest
                // possible overwrite is performed here instead of SetWindowClipRectBeforeSetChannel().
                if ((table.Flags & ImGuiTableFlags.NoClip) == 0)
                {
                    window.DrawList._cmdHeaderClipRect = new ImVec4(
                        table.Bg0ClipRectForDrawCmd.Min.X,
                        table.Bg0ClipRectForDrawCmd.Min.Y,
                        table.Bg0ClipRectForDrawCmd.Max.X,
                        table.Bg0ClipRectForDrawCmd.Max.Y);
                }
                table.DrawSplitter!.SetCurrentChannel(window.DrawList, TableDrawChannelBg0);
            }

            // Draw the row background. Soft clipping lets backgrounds and borders share one clip rect.
            if (bgCol0 != 0 || bgCol1 != 0)
            {
                ImRect rowRect = new(table.WorkRect.Min.X, bgY1, table.WorkRect.Max.X, bgY2);
                rowRect.ClipWith(table.BgClipRect);
                if (bgCol0 != 0 && rowRect.Min.Y < rowRect.Max.Y)
                    window.DrawList.AddRectFilled(rowRect.Min, rowRect.Max, bgCol0);
                if (bgCol1 != 0 && rowRect.Min.Y < rowRect.Max.Y)
                    window.DrawList.AddRectFilled(rowRect.Min, rowRect.Max, bgCol1);
            }

            // Draw the deferred cell backgrounds recorded during the row.
            if (drawCellBgColor)
            {
                for (int cellN = 0; cellN <= table.RowCellDataCurrent; cellN++)
                {
                    ImGuiTableCellData cellData = table.RowCellData[cellN];
                    ImGuiTableColumn column = table.Columns[cellData.Column];
                    ImRect cellBgRect = TableGetCellBgRect(table, cellData.Column);
                    cellBgRect.ClipWith(table.BgClipRect);
                    // Clip the first column after a frozen one while scrolling, and never past the column.
                    cellBgRect.Min = new ImVec2(MathF.Max(cellBgRect.Min.X, column.ClipRect.Min.X), cellBgRect.Min.Y);
                    cellBgRect.Max = new ImVec2(MathF.Min(cellBgRect.Max.X, column.MaxX), cellBgRect.Max.Y);
                    if (cellBgRect.Min.Y < cellBgRect.Max.Y)
                        window.DrawList.AddRectFilled(cellBgRect.Min, cellBgRect.Max, cellData.BgColor);
                }
            }

            // Draw the top inner border.
            if (topBorderCol != 0 && bgY1 >= table.BgClipRect.Min.Y && bgY1 < table.BgClipRect.Max.Y)
                window.DrawList.AddLineH(table.BorderX1, table.BorderX2, bgY1, topBorderCol, borderSize);

            // Draw the strong bottom border at the row unfreezing mark.
            if (drawStrongBottomBorder && bgY2 >= table.BgClipRect.Min.Y && bgY2 < table.BgClipRect.Max.Y)
                window.DrawList.AddLineH(table.BorderX1, table.BorderX2, bgY2, table.BorderColorStrong, borderSize);
        }

        // End frozen rows: past the last frozen row line, teleport the cursor and alter the clipping
        // rectangle. This must run at row end so the list clipper can mark the row end and observe the
        // new cursor position.
        if (unfreezeRowsRequest)
        {
            ImAssert.Check(table.FreezeRowsRequest > 0);
            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                table.Columns[columnN].NavLayerCurrent = table.NavLayer;
            float y0 = MathF.Max(table.RowPosY2 + 1, table.InnerClipRect.Min.Y);
            tableInstance.LastFrozenHeight = y0 - table.OuterRect.Min.Y;

            if (unfreezeRowsActual)
            {
                ImAssert.Check(!table.IsUnfrozenRows);
                table.IsUnfrozenRows = true;

                // BgClipRect starts as InnerClipRect; reduce it now and keep the draw-command copy equal.
                // The port's rectangle members are value-typed properties, so the canonical in-place
                // component writes are lowered to whole-value read-modify-write operations.
                ImRect bgClipRect = table.BgClipRect;
                bgClipRect.Min = new ImVec2(bgClipRect.Min.X, MathF.Min(y0, table.InnerClipRect.Max.Y));
                bgClipRect.Max = new ImVec2(bgClipRect.Max.X, table.InnerClipRect.Max.Y);
                table.BgClipRect = bgClipRect;
                ImRect bg2ClipRect = table.Bg2ClipRectForDrawCmd;
                bg2ClipRect.Min = bgClipRect.Min;
                bg2ClipRect.Max = bgClipRect.Max;
                table.Bg2ClipRectForDrawCmd = bg2ClipRect;
                table.Bg2DrawChannelCurrent = table.Bg2DrawChannelUnfrozen;
                ImAssert.Check(table.Bg2ClipRectForDrawCmd.Min.Y <= table.Bg2ClipRectForDrawCmd.Max.Y);

                // Translate the row into the scrolled coordinate space; keep its height.
                float rowHeight = table.RowPosY2 - table.RowPosY1;
                table.RowPosY2 = table.WorkRect.Min.Y + table.RowPosY2 - table.OuterRect.Min.Y;
                window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, table.RowPosY2);
                table.RowPosY1 = table.RowPosY2 - rowHeight;
                for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                {
                    ImGuiTableColumn column = table.Columns[columnN];
                    column.DrawChannelCurrent = column.DrawChannelUnfrozen;
                    ImRect columnClipRect = column.ClipRect;
                    columnClipRect.Min = new ImVec2(columnClipRect.Min.X, table.Bg2ClipRectForDrawCmd.Min.Y);
                    column.ClipRect = columnClipRect;
                }

                // Update the clip rectangle ahead of TableBeginCell() so the clipper sees the new Min.y.
                SetWindowClipRectBeforeSetChannel(window, table.Columns[0].ClipRect);
                table.DrawSplitter!.SetCurrentChannel(window.DrawList, table.Columns[0].DrawChannelCurrent);
            }
        }

        // Header rows do not participate in the alternating-row background sequence.
        if ((table.RowFlags & ImGuiTableRowFlags.Headers) == 0)
            table.RowBgColorCounter++;
        table.IsInsideRow = false;
    }

    #endregion

    #region SECTION: Tables: Columns changes

    /// <summary>
    /// Appends the following items into a specific column of the current row.
    /// </summary>
    /// <param name="columnN">Column index to enter, which must be within the table's column count.</param>
    /// <returns>The value of <see cref="ImGuiTableColumn.IsRequestOutput"/> for the column, indicating
    /// whether the application should submit its items. A column that can contribute the tallest cell of
    /// the row must be submitted even when this is <see langword="false"/>.</returns>
    /// <remarks>
    /// Entering a different column closes the open cell first. An invalid index reports a user error and
    /// returns <see langword="false"/>; the previously open cell has already been closed at that point,
    /// matching the canonical ordering.
    /// </remarks>
    public static bool TableSetColumnIndex(int columnN)
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
            return false;

        if (table.CurrentColumn != columnN)
        {
            if (table.CurrentColumn != -1)
                TableEndCell(table);
            if (columnN < 0 || columnN >= table.ColumnsCount)
            {
                AssertUserError(false, "TableSetColumnIndex() invalid column index!");
                return false;
            }
            TableBeginCell(table, columnN);
        }

        return table.Columns[columnN].IsRequestOutput;
    }

    /// <summary>
    /// Appends the following items into the next column, wrapping to a new row after the last column.
    /// </summary>
    /// <returns>The value of <see cref="ImGuiTableColumn.IsRequestOutput"/> for the entered column.</returns>
    /// <remarks>
    /// This function drives the row/column cycle for applications that submit a uniform sequence of cells
    /// without computing every item rectangle explicitly.
    /// </remarks>
    public static bool TableNextColumn()
    {
        ImGuiContext g = GetContext();
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
            return false;

        if (table.IsInsideRow && table.CurrentColumn + 1 < table.ColumnsCount)
        {
            if (table.CurrentColumn != -1)
                TableEndCell(table);
            TableBeginCell(table, table.CurrentColumn + 1);
        }
        else
        {
            TableNextRow();
            TableBeginCell(table, 0);
        }

        return table.Columns[table.CurrentColumn].IsRequestOutput;
    }

    /// <summary>
    /// Enters one column: positions the cursor at the cell origin accounting for padding and indentation,
    /// narrows the host work and item-width state to the column, and selects the column's draw channel and
    /// clip rectangle. Called very frequently, so the port keeps the canonical ordering and avoids
    /// unnecessary work.
    /// </summary>
    /// <param name="table">Table that owns the column.</param>
    /// <param name="columnN">Column index to enter.</param>
    /// <remarks>
    /// The vertical component of the work rectangle is set only here and during layout; the horizontal
    /// bounds come from the column so that layout helpers such as <c>SameLine()</c> behave per cell. The
    /// host window's skip state is replaced by the column's, so a clipped or disabled column suppresses
    /// item submission without affecting its neighbors.
    /// </remarks>
    internal static void TableBeginCell(ImGuiTable table, int columnN)
    {
        ImGuiContext g = GetContext();
        ImGuiTableColumn column = table.Columns[columnN];
        ImGuiWindow window = table.InnerWindow!;
        table.CurrentColumn = columnN;

        // Start position is roughly the cell rectangle minimum plus padding plus indent.
        float startX = column.WorkMinX;
        if ((column.Flags & ImGuiTableColumnFlags.IndentEnable) != 0)
            startX += table.RowIndentOffsetX; // The row-locked indent, not the live window indent.

        window.DC.CursorPos = new ImVec2(startX, table.RowPosY1 + table.RowCellPaddingY);
        window.DC.CursorMaxPos = new ImVec2(window.DC.CursorPos.X, window.DC.CursorMaxPos.Y);
        window.DC.ColumnsOffset = new ImVec1(startX - window.Pos.X - window.DC.Indent.X);
        window.DC.CursorPosPrevLine = new ImVec2(window.DC.CursorPos.X, window.DC.CursorPosPrevLine.Y);
        window.DC.CurrLineTextBaseOffset = table.RowTextBaseline;
        window.DC.NavLayerCurrent = (ImGuiNavLayer)column.NavLayerCurrent;

        // WorkRect.Max.y is set only once during layout; the horizontal bounds belong to the column.
        // The port's rectangle members are value-typed properties, so the canonical component writes
        // are lowered to a whole-value read-modify-write.
        ImRect workRect = window.WorkRect;
        workRect.Min = new ImVec2(column.WorkMinX, window.DC.CursorPos.Y);
        workRect.Max = new ImVec2(column.WorkMaxX, workRect.Max.Y);
        window.WorkRect = workRect;
        window.DC.ItemWidth = column.ItemWidth;

        window.SkipItems = column.IsSkipItems;
        if (column.IsSkipItems)
        {
            g.LastItemData.ID = 0;
            g.LastItemData.StatusFlags = 0;
        }

        // Also see TablePushColumnChannel().
        if ((table.Flags & ImGuiTableFlags.NoClip) != 0)
        {
            table.DrawSplitter!.SetCurrentChannel(window.DrawList, TableDrawChannelNoClip);
        }
        else
        {
            SetWindowClipRectBeforeSetChannel(window, column.ClipRect);
            table.DrawSplitter!.SetCurrentChannel(window.DrawList, column.DrawChannelCurrent);
        }

        // Logging.: separate cells in the capture and force the next
        // captured run onto a fresh line.
        if (g.LogEnabled && !column.IsSkipItems)
        {
            LogRenderedText(window.DC.CursorPos, "|");
            g.LogLinePosY = float.MaxValue;
        }
    }

    /// <summary>
    /// Leaves the current column: reports the maximum content position reached so the layout can infer the
    /// column's content width and the row height, saves the column's item width, and propagates the text
    /// baseline to the row.
    /// </summary>
    /// <param name="table">Table whose current column is finalized.</param>
    /// <remarks>
    /// Canonical selects the content-maximum accumulator through a pointer so that header, frozen, and
    /// unfrozen rows share one code path. The port cannot take a reference to a property, so the three
    /// cases are written as explicit branches with identical arithmetic.
    /// </remarks>
    internal static void TableEndCell(ImGuiTable table)
    {
        ImGuiTableColumn column = table.Columns[table.CurrentColumn];
        ImGuiWindow window = table.InnerWindow!;

        if (window.DC.IsSetPos)
            ErrorCheckUsingSetCursorPosToExtendParentBoundaries();

        // Report the maximum content X so the column's content width can be inferred.
        if ((table.RowFlags & ImGuiTableRowFlags.Headers) != 0)
            column.ContentMaxXHeadersUsed = MathF.Max(column.ContentMaxXHeadersUsed, window.DC.CursorMaxPos.X);
        else if (table.IsUnfrozenRows)
            column.ContentMaxXUnfrozen = MathF.Max(column.ContentMaxXUnfrozen, window.DC.CursorMaxPos.X);
        else
            column.ContentMaxXFrozen = MathF.Max(column.ContentMaxXFrozen, window.DC.CursorMaxPos.X);

        if (column.IsEnabled)
            table.RowPosY2 = MathF.Max(table.RowPosY2, window.DC.CursorMaxPos.Y + table.RowCellPaddingY);
        column.ItemWidth = window.DC.ItemWidth;

        // Propagate the text baseline from the last line of the cell to the whole row.
        table.RowTextBaseline = MathF.Max(table.RowTextBaseline, window.DC.PrevLineTextBaseOffset);
    }

    #endregion

    #region SECTION: Tables: Settings, in-memory lifetime

    /// <summary>
    /// Resets one settings record in place for a newly created or recycled table entry.
    /// </summary>
    /// <param name="settings">Record to initialize; every field is overwritten.</param>
    /// <param name="id">Table identifier the record binds to.</param>
    /// <param name="columnsCount">Number of columns the record describes.</param>
    /// <param name="columnsCountMax">Number of per-column entries to allocate; the record may later be
    /// recycled for a table with fewer columns, but not one with more.</param>
    /// <remarks>
    /// Canonical placement-constructs the header and each trailing column entry. The port rebinds the
    /// explicit <see cref="ImGuiTableSettings.ColumnSettings"/> list, so the entry capacity is expressed as
    /// the list length rather than as a chunk size.
    /// </remarks>
    private static void TableSettingsInit(ImGuiTableSettings settings, uint id, int columnsCount, int columnsCountMax)
    {
        settings.SaveFlags = ImGuiTableFlags.None;
        settings.RefScale = 0.0f;
        settings.LastUsedDate = default;
        settings.ColumnSettings.Clear();
        for (int n = 0; n < columnsCountMax; n++)
            settings.ColumnSettings.Add(new ImGuiTableColumnSettings());
        settings.ID = id;
        settings.ColumnsCount = (short)columnsCount;
        settings.ColumnsCountMax = (short)columnsCountMax;
        settings.WantApply = true;
    }

    /// <summary>
    /// Binds or creates the settings record for a table identifier.
    /// </summary>
    /// <param name="id">Table identifier the record is keyed by.</param>
    /// <param name="columnsCount">Number of columns the table currently declares.</param>
    /// <returns>The bound or newly created settings record; it remains owned by
    /// <see cref="ImGuiContext.SettingsTables"/>.</returns>
    /// <remarks>
    /// An existing record is recycled when its capacity still fits the requested column count; otherwise
    /// it is invalidated by clearing its identifier, which leaves it for
    /// <see cref="TableGcCompactSettings"/> to drop, and a fresh record is appended. Canonical allocates
    /// the record from a chunk stream; the port appends a plain object to the context list, and
    /// <see cref="ImGuiTable.SettingsOffset"/> indexes that list.
    /// </remarks>
    internal static ImGuiTableSettings TableSettingsCreate(uint id, int columnsCount)
    {
        ImGuiContext g = GetContext();
        ImGuiTableSettings? settings = TableSettingsFindByID(id);
        if (settings != null)
        {
            if (settings.ColumnsCountMax >= columnsCount)
            {
                TableSettingsInit(settings, id, columnsCount, settings.ColumnsCountMax); // Recycle.
                return settings;
            }
            settings.ID = 0; // Invalidate: the record cannot hold the larger column count.
        }
        settings = new ImGuiTableSettings();
        g.SettingsTables.Add(settings);
        TableSettingsInit(settings, id, columnsCount, columnsCount);
        return settings;
    }

    /// <summary>
    /// Finds the settings record bound to a table identifier.
    /// </summary>
    /// <param name="id">Table identifier to look up.</param>
    /// <returns>The matching record, or <see langword="null"/> when no record exists.</returns>
    /// <remarks>
    /// The scan is linear over the context's settings list, matching the canonical implementation's
    /// deliberate simplicity; the count is bounded by the number of distinct tables seen this session.
    /// </remarks>
    internal static ImGuiTableSettings? TableSettingsFindByID(uint id)
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.SettingsTables.Count; n++)
        {
            if (g.SettingsTables[n].ID == id)
                return g.SettingsTables[n];
        }
        return null;
    }

    /// <summary>
    /// Returns the settings record currently bound to a table, or <see langword="null"/> when the table is
    /// not bound.
    /// </summary>
    /// <param name="table">Table whose bound record is requested.</param>
    /// <returns>The bound record, or <see langword="null"/> when <see cref="ImGuiTable.SettingsOffset"/>
    /// is -1.</returns>
    /// <remarks>
    /// The assert verifies that the binding still points at this table's record, which catches a stale
    /// offset after a settings-list compaction.
    /// </remarks>
    internal static ImGuiTableSettings? TableGetBoundSettings(ImGuiTable table)
    {
        if (table.SettingsOffset == -1)
            return null;
        ImGuiContext g = GetContext();
        ImGuiTableSettings settings = g.SettingsTables[table.SettingsOffset];
        ImAssert.Check(settings.ID == table.ID);
        return settings;
    }

    /// <summary>
    /// Restores a table to its default state, discarding loaded settings without reloading them from the
    /// layout file.
    /// </summary>
    /// <param name="table">Table to reset.</param>
    /// <remarks>
    /// <see cref="ImGuiTable.IsInitializing"/> makes the next layout re-run the per-column default
    /// initialization, while clearing <see cref="ImGuiTable.SettingsLoadedFlags"/> marks the freshly
    /// initialized data as originally submitted rather than loaded, matching the canonical comment that
    /// the initialized data becomes definitive.
    /// </remarks>
    internal static void TableResetSettings(ImGuiTable table)
    {
        table.IsInitializing = table.IsSettingsDirty = true;
        table.IsResetAllRequest = false;
        table.IsSettingsRequestLoad = false; // Do not reload from the layout file.
        table.SettingsLoadedFlags = ImGuiTableFlags.None;
    }

    /// <summary>
    /// Compares two columns for display-order repair: primary key is the display order interpreted as an
    /// unsigned value so that -1 sorts last, secondary key is the column index.
    /// </summary>
    /// <param name="table">Table that owns both columns.</param>
    /// <param name="lhsIdx">Index of the left-hand column.</param>
    /// <param name="rhsIdx">Index of the right-hand column.</param>
    /// <returns>A negative value when <paramref name="lhsIdx"/> sorts first, a positive value when
    /// <paramref name="rhsIdx"/> sorts first.</returns>
    /// <remarks>
    /// Canonical passes a two-field record through <c>ImQsort</c>; the port keeps the same ordering as a
    /// direct index comparison so the sort can run on an index array without an allocation per element.
    /// </remarks>
    private static int TableFixDisplayOrderComparer(ImGuiTable table, int lhsIdx, int rhsIdx)
    {
        // Interpret the signed display order as unsigned so -1 becomes the largest value and sorts last.
        int orderDelta = (ushort)table.Columns[lhsIdx].DisplayOrder - (ushort)table.Columns[rhsIdx].DisplayOrder;
        return orderDelta > 0 ? 1 : orderDelta < 0 ? -1 : lhsIdx > rhsIdx ? 1 : -1;
    }

    /// <summary>
    /// Repairs invalid display-order data by compacting gaps, preserving relative order, and
    /// deduplicating equal orders, then rebuilds the reverse display-order map.
    /// </summary>
    /// <param name="table">Table whose column display order is normalized.</param>
    /// <remarks>
    /// <para>
    /// The repair is called during initialization and settings loading, after a topology reconcile, and
    /// after settings loaded from the layout file may have produced duplicate or gapped orders. It also
    /// runs for tables that are not reorderable, because loaded settings can still carry an order.
    /// </para>
    /// <para>
    /// Canonical sorts a scratch array carved out of the context's temporary buffer. This port keeps the
    /// temporary buffer reserved for text formatting, so it sorts a small <see cref="int"/> index array
    /// with an insertion sort instead. The array is bounded by the column count (at most
    /// <c>TableMaxColumns</c>) and this path runs only on initialization, settings load, and topology
    /// changes rather than per item.
    /// </para>
    /// </remarks>
    internal static void TableFixDisplayOrder(ImGuiTable table)
    {
        if ((table.Flags & ImGuiTableFlags.Reorderable) == 0)
        {
            for (int n = 0; n < table.ColumnsCount; n++)
                table.DisplayOrderToIndex[n] = table.Columns[n].DisplayOrder = (short)n;
            return;
        }

        int count = table.ColumnsCount;
        int[] order = new int[count];
        for (int n = 0; n < count; n++)
            order[n] = n;

        // Insertion sort with the canonical comparator: equal display orders fall back to index order.
        for (int n = 1; n < count; n++)
        {
            int idx = order[n];
            int m = n - 1;
            while (m >= 0 && TableFixDisplayOrderComparer(table, order[m], idx) > 0)
            {
                order[m + 1] = order[m];
                m--;
            }
            order[m + 1] = idx;
        }

        for (int n = 0; n < count; n++)
            table.Columns[order[n]].DisplayOrder = (short)n;
        for (int n = 0; n < count; n++)
            table.DisplayOrderToIndex[table.Columns[n].DisplayOrder] = (short)n;
    }

    /// <summary>
    /// Serializes one table's current layout into its settings record, creating or rebinding the record
    /// as needed, and marks the settings dirty.
    /// </summary>
    /// <param name="table">Table whose layout is saved.</param>
    /// <remarks>
    /// <para>
    /// The saved flags record which categories actually differ from the defaults so the emitted entry can
    /// omit unchanged columns. A record that cannot hold the current column count is invalidated and a new
    /// one is created, matching canonical's chunk-capacity rule.
    /// </para>
    /// <para>
    /// Only the flags that the table itself enables are kept, because a table that is not hideable cannot
    /// have meaningful visibility data restored.
    /// </para>
    /// </remarks>
    internal static void TableSaveSettings(ImGuiTable table)
    {
        table.IsSettingsDirty = false;
        if ((table.Flags & ImGuiTableFlags.NoSavedSettings) != 0)
            return;

        ImGuiContext g = GetContext();
        ImGuiTableSettings? settings = TableGetBoundSettings(table);
        if (settings != null && table.ColumnsCount > settings.ColumnsCountMax)
        {
            settings.ID = 0; // Invalidate: the record cannot hold the larger column count.
            settings = null;
        }
        if (settings == null)
        {
            settings = TableSettingsCreate(table.ID, table.ColumnsCount);
            table.SettingsOffset = g.SettingsTables.IndexOf(settings);
        }
        settings.ColumnsCount = (short)table.ColumnsCount;
        settings.LastUsedDate = g.SessionDate;

        ImAssert.Check(settings.ID == table.ID);
        ImAssert.Check(settings.ColumnsCount == table.ColumnsCount
            && settings.ColumnsCountMax >= settings.ColumnsCount);

        bool saveRefScale = false;
        settings.SaveFlags = ImGuiTableFlags.None;
        for (int n = 0; n < table.ColumnsCount; n++)
        {
            ImGuiTableColumn column = table.Columns[n];
            ImGuiTableColumnSettings columnSettings = settings.ColumnSettings[n];
            bool isStretch = (column.Flags & ImGuiTableColumnFlags.WidthStretch) != 0;
            float widthOrWeight = isStretch ? column.StretchWeight : column.WidthRequest;
            columnSettings.ID = column.ID;
            columnSettings.WidthOrWeight = widthOrWeight;
            columnSettings.Index = (short)n;
            columnSettings.DisplayOrder = column.DisplayOrder;
            columnSettings.SortOrder = column.SortOrder;
            columnSettings.SortDirection = column.SortDirection;
            columnSettings.IsEnabled = column.IsUserEnabled ? (sbyte)1 : (sbyte)0;
            columnSettings.IsStretch = isStretch;
            if (!isStretch)
                saveRefScale = true;

            // Data that still matches the default is omitted from the entry so the file stays small; the
            // sort order is always saved when present because canonical has no cheap default comparison.
            if (widthOrWeight != column.InitStretchWeightOrWidth)
                settings.SaveFlags |= ImGuiTableFlags.Resizable;
            if (column.DisplayOrder != n)
                settings.SaveFlags |= ImGuiTableFlags.Reorderable;
            if (column.SortOrder != -1)
                settings.SaveFlags |= ImGuiTableFlags.Sortable;
            if (column.IsUserEnabled != ((column.Flags & ImGuiTableColumnFlags.DefaultHide) == 0))
                settings.SaveFlags |= ImGuiTableFlags.Hideable;
        }
        settings.SaveFlags &= table.Flags;
        settings.RefScale = saveRefScale ? table.RefScale : 0.0f;

        MarkIniSettingsDirty();
    }

    /// <summary>
    /// Binds a table to its saved settings record, if one exists, and prepares the per-column load.
    /// </summary>
    /// <param name="table">Table requesting its settings.</param>
    /// <remarks>
    /// A column-count change is tolerated: the record is still bound and the table is marked dirty so the
    /// layout can re-save under the new count. The per-column values are applied later by
    /// <see cref="TableLoadSettingsForColumns"/> during layout.
    /// </remarks>
    internal static void TableLoadSettings(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        if ((table.Flags & ImGuiTableFlags.NoSavedSettings) != 0)
        {
            table.IsSettingsRequestLoad = false;
            return;
        }

        ImGuiTableSettings? settings;
        if (table.SettingsOffset == -1)
        {
            settings = TableSettingsFindByID(table.ID);
            if (settings == null)
                return;
            if (settings.ColumnsCount != table.ColumnsCount)
                table.IsSettingsDirty = true;
            table.SettingsOffset = g.SettingsTables.IndexOf(settings);
        }
        else
        {
            settings = TableGetBoundSettings(table);
            if (settings == null)
                return;
        }

        table.SettingsLoadedFlags = settings.SaveFlags;
        table.RefScale = settings.RefScale;
        settings.LastUsedDate = g.SessionDate;
    }

    /// <summary>
    /// Matches the saved per-column entries to the table's live columns and applies them.
    /// </summary>
    /// <param name="table">Table receiving the saved column state.</param>
    /// <remarks>
    /// Columns are matched first by position while the identifiers still agree, then by identifier, then by
    /// submission order for whatever remains. The three-phase match is what lets a reordered table keep its
    /// widths and a renamed column keep its position.
    /// </remarks>
    internal static void TableLoadSettingsForColumns(ImGuiTable table)
    {
        for (int n = 0; n < table.ColumnsCount; n++)
            table.Columns[n].IsLoadedSettings = false;
        ImGuiTableSettings? settings = TableGetBoundSettings(table);
        if (settings == null)
            return;

        // Loading always establishes the submitted order, so reorderability is considered active.
        table.SettingsLoadedFlags |= ImGuiTableFlags.Reorderable;

        // Fast path: matching identifiers in the same order.
        int matches = 0;
        for (int n = 0; n < table.ColumnsCount; n++)
        {
            if (n >= settings.ColumnsCount || settings.ColumnSettings[n].ID != table.Columns[n].ID)
                break;
            TableLoadSettingsForColumn(table.Columns[n], settings.ColumnSettings[n], settings.SaveFlags);
            matches++;
        }
        if (matches == settings.ColumnsCount)
            return;

        int settingsStartN = matches;
        for (int n = settingsStartN; n < settings.ColumnsCount; n++)
            settings.ColumnSettings[n].IsLoaded = false;

        // Second pass: match named columns by identifier.
        for (int n = 0; n < table.ColumnsCount; n++)
        {
            ImGuiTableColumn column = table.Columns[n];
            if (column.ID == 0 || column.IsLoadedSettings)
                continue;
            for (int m = settingsStartN; m < settings.ColumnsCount; m++)
            {
                if (settings.ColumnSettings[m].ID == column.ID && !settings.ColumnSettings[m].IsLoaded)
                {
                    TableLoadSettingsForColumn(column, settings.ColumnSettings[m], settings.SaveFlags);
                    matches++;
                    break;
                }
            }
        }

        // Third pass: remaining entries are matched sequentially to the remaining columns.
        int dstIdx = 0;
        for (int n = settingsStartN; n < settings.ColumnsCount; n++)
        {
            if (settings.ColumnSettings[n].IsLoaded)
                continue;
            while (dstIdx < table.ColumnsCount && table.Columns[dstIdx].IsLoadedSettings)
                dstIdx++;
            if (dstIdx >= table.ColumnsCount)
                break;
            TableLoadSettingsForColumn(table.Columns[dstIdx], settings.ColumnSettings[n], settings.SaveFlags);
            dstIdx++;
        }
    }

    /// <summary>
    /// Applies one saved column entry to one live column.
    /// </summary>
    /// <param name="column">Live column receiving the saved state.</param>
    /// <param name="columnSettings">Saved entry to apply.</param>
    /// <param name="loadFlags">The categories the saved record actually carries.</param>
    /// <remarks>
    /// A width is applied only when the record carries resizable data, because an auto-fit width would
    /// otherwise pin a column to whatever size it happened to have on the save frame. Display order falls
    /// back to the saved index when reorderable data is absent, which seeds the default order correctly.
    /// </remarks>
    internal static void TableLoadSettingsForColumn(ImGuiTableColumn column, ImGuiTableColumnSettings columnSettings, ImGuiTableFlags loadFlags)
    {
        column.IsLoadedSettings = true;
        columnSettings.IsLoaded = true;
        if ((loadFlags & ImGuiTableFlags.Resizable) != 0)
        {
            if (columnSettings.IsStretch)
                column.StretchWeight = columnSettings.WidthOrWeight;
            else
                column.WidthRequest = columnSettings.WidthOrWeight;
            column.AutoFitQueue = 0;
        }
        if ((loadFlags & ImGuiTableFlags.Reorderable) != 0)
            column.DisplayOrder = columnSettings.DisplayOrder;
        else
            column.DisplayOrder = columnSettings.Index;
        if ((loadFlags & ImGuiTableFlags.Hideable) != 0 && columnSettings.IsEnabled != -1)
            column.IsUserEnabled = column.IsUserEnabledNextFrame = columnSettings.IsEnabled == 1;
        column.SortOrder = columnSettings.SortOrder;
        column.SortDirection = columnSettings.SortDirection;
    }

    /// <summary>
    /// Clears every table's settings binding and empties the settings list.
    /// </summary>
    /// <param name="ctx">The context being cleaned.</param>
    /// <param name="handler">The table settings handler, unused.</param>
    internal static void TableSettingsHandler_ClearAll(ImGuiContext ctx, ImGuiSettingsHandler handler)
    {
        _ = handler;
        for (int n = 0; n < ctx.Tables.Count; n++)
            ctx.Tables[n].SettingsOffset = -1;
        ctx.SettingsTables.Clear();
    }

    /// <summary>
    /// Applies date-based discard and stamping to the table settings entries.
    /// </summary>
    /// <param name="ctx">The context being cleaned.</param>
    /// <param name="handler">The table settings handler, unused.</param>
    /// <param name="args">The cleanup options.</param>
    /// <remarks>
    /// A discarded entry has its identifier cleared rather than being removed, because live tables may
    /// still hold a list index; the next compaction drops it.
    /// </remarks>
    internal static void TableSettingsHandler_Cleanup(ImGuiContext ctx, ImGuiSettingsHandler handler, ImGuiSettingsCleanupArgs args)
    {
        _ = handler;
        for (int n = 0; n < ctx.Tables.Count; n++)
            ctx.Tables[n].SettingsOffset = -1;
        for (int n = 0; n < ctx.SettingsTables.Count; n++)
        {
            ImGuiTableSettings settings = ctx.SettingsTables[n];
            bool isValid = settings.LastUsedDate.IsValid();
            if (args._DiscardOlderThanDate != 0 && settings.LastUsedDate.Unpack() < args._DiscardOlderThanDate)
                settings.ID = 0;
            else if (args.DiscardWhenMissingDate && !isValid)
                settings.ID = 0;
            else if (args.SetCurrentSessionDateToAll || (args.SetCurrentSessionDateWhenMissingDate && !isValid))
                settings.LastUsedDate = ctx.SessionDate;
        }
    }

    /// <summary>
    /// Requests that every live table reload its settings.
    /// </summary>
    /// <param name="ctx">The context being applied.</param>
    /// <param name="handler">The table settings handler, unused.</param>
    internal static void TableSettingsHandler_ApplyAll(ImGuiContext ctx, ImGuiSettingsHandler handler)
    {
        _ = handler;
        for (int n = 0; n < ctx.Tables.Count; n++)
        {
            ctx.Tables[n].IsSettingsRequestLoad = true;
            ctx.Tables[n].SettingsOffset = -1;
        }
    }

    /// <summary>
    /// Opens one `[Table][0xID,count]` entry for reading.
    /// </summary>
    /// <param name="ctx">The context being read into.</param>
    /// <param name="handler">The table settings handler, unused.</param>
    /// <param name="name">The entry name, holding the identifier and column count.</param>
    /// <returns>The created settings record, or <see langword="null"/> for a malformed or out-of-range
    /// header, which makes the parser ignore the entry's lines.</returns>
    internal static object? TableSettingsHandler_ReadOpen(ImGuiContext ctx, ImGuiSettingsHandler handler, string name)
    {
        _ = ctx;
        _ = handler;
        int comma = name.IndexOf(',');
        if (comma < 0)
            return null;
        if (!TryParseTableId(name.AsSpan(0, comma), out uint id) || !int.TryParse(
                name.AsSpan(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int columnsCount))
            return null;
        if (columnsCount <= 0 || columnsCount >= TableMaxColumns)
            return null;
        return TableSettingsCreate(id, columnsCount);
    }

    /// <summary>
    /// Reads one line of a table settings entry: the reference scale, the last-used date, or a column
    /// record.
    /// </summary>
    /// <param name="ctx">The context being read into.</param>
    /// <param name="handler">The table settings handler, unused.</param>
    /// <param name="entry">The settings record returned by
    /// <see cref="TableSettingsHandler_ReadOpen"/>.</param>
    /// <param name="line">The line to parse; unknown keys are ignored.</param>
    internal static void TableSettingsHandler_ReadLine(ImGuiContext ctx, ImGuiSettingsHandler handler, object entry, string line)
    {
        _ = ctx;
        _ = handler;
        ImGuiTableSettings settings = (ImGuiTableSettings)entry;
        if (line.StartsWith("RefScale=", StringComparison.Ordinal))
        {
            if (float.TryParse(line.AsSpan(9), NumberStyles.Float, CultureInfo.InvariantCulture, out float refScale))
                settings.RefScale = refScale;
            return;
        }
        if (line.StartsWith("LastUsed=", StringComparison.Ordinal))
        {
            if (int.TryParse(line.AsSpan(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastUsed))
                settings.LastUsedDate = new ImGuiPackedDate((uint)lastUsed);
            return;
        }
        if (!line.StartsWith("Column ", StringComparison.Ordinal))
            return;

        // "Column 0  UserID=0x42AD2D21 Width=100 Visible=1 Order=0 Sort=0v"
        int cursor = 7;
        while (cursor < line.Length && line[cursor] == ' ')
            cursor++;
        int numberStart = cursor;
        while (cursor < line.Length && char.IsAsciiDigit(line[cursor]))
            cursor++;
        if (cursor == numberStart
            || !int.TryParse(line.AsSpan(numberStart, cursor - numberStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out int columnN))
            return;
        if (columnN < 0 || columnN >= settings.ColumnsCount)
            return;

        ImGuiTableColumnSettings column = settings.ColumnSettings[columnN];
        column.Index = (short)columnN;
        foreach (string token in line[cursor..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = token.IndexOf('=');
            if (equals <= 0)
                continue;
            string key = token[..equals];
            ReadOnlySpan<char> value = token.AsSpan(equals + 1);
            switch (key)
            {
                case "Width":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width))
                    {
                        column.WidthOrWeight = width;
                        column.IsStretch = false;
                        settings.SaveFlags |= ImGuiTableFlags.Resizable;
                    }
                    break;
                case "Weight":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float weight))
                    {
                        column.WidthOrWeight = weight;
                        column.IsStretch = true;
                        settings.SaveFlags |= ImGuiTableFlags.Resizable;
                    }
                    break;
                case "Visible":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int visible))
                    {
                        column.IsEnabled = (sbyte)visible;
                        settings.SaveFlags |= ImGuiTableFlags.Hideable;
                    }
                    break;
                case "Order":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
                    {
                        column.DisplayOrder = (short)order;
                        settings.SaveFlags |= ImGuiTableFlags.Reorderable;
                    }
                    break;
                case "Sort":
                    if (value.Length >= 1 && int.TryParse(value[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sortOrder))
                    {
                        column.SortOrder = (short)sortOrder;
                        column.SortDirection = value[^1] == '^' ? ImGuiSortDirection.Descending : ImGuiSortDirection.Ascending;
                        settings.SaveFlags |= ImGuiTableFlags.Sortable;
                    }
                    break;
                case "ID":
                    if (TryParseTableId(value, out uint id))
                        column.ID = id;
                    break;
                case "UserID":
                    // Canonical removed this legacy field on 2025/11/12; it is accepted and ignored.
                    break;
            }
        }
    }

    /// <summary>
    /// Writes every table settings entry to the output buffer.
    /// </summary>
    /// <param name="ctx">The context being written.</param>
    /// <param name="handler">The table settings handler supplying the section type name.</param>
    /// <param name="buf">The output buffer receiving the `[Table]` sections.</param>
    /// <remarks>
    /// An entry is always emitted even when every category flag is clear, because the entry itself records
    /// that the table exists with default settings. Individual columns are emitted only when at least one
    /// of their categories is being saved.
    /// </remarks>
    internal static void TableSettingsHandler_WriteAll(ImGuiContext ctx, ImGuiSettingsHandler handler, ImGuiTextBuffer buf)
    {
        for (int n = 0; n < ctx.SettingsTables.Count; n++)
        {
            ImGuiTableSettings settings = ctx.SettingsTables[n];
            if (settings.ID == 0)
                continue;

            bool saveSize = (settings.SaveFlags & ImGuiTableFlags.Resizable) != 0;
            bool saveVisible = (settings.SaveFlags & ImGuiTableFlags.Hideable) != 0;
            bool saveOrder = (settings.SaveFlags & ImGuiTableFlags.Reorderable) != 0;
            bool saveSort = (settings.SaveFlags & ImGuiTableFlags.Sortable) != 0;

            buf.AppendF("[{0}][0x{1:X8},{2}]\n", handler.TypeName, settings.ID, settings.ColumnsCount);
            if (settings.RefScale != 0.0f)
                buf.AppendF("RefScale={0}\n", settings.RefScale);
            for (int columnN = 0; columnN < settings.ColumnsCount; columnN++)
            {
                ImGuiTableColumnSettings column = settings.ColumnSettings[columnN];
                bool saveColumn = saveSize || saveVisible || saveOrder || (saveSort && column.SortOrder != -1);
                if (!saveColumn)
                    continue;
                buf.AppendF("Column {0,-2}", columnN);
                if (saveSize && column.IsStretch)
                    buf.AppendF(" Weight={0:F4}", column.WidthOrWeight);
                if (saveSize && !column.IsStretch)
                    buf.AppendF(" Width={0}", (int)column.WidthOrWeight);
                if (saveVisible)
                    buf.AppendF(" Visible={0}", column.IsEnabled);
                if (saveOrder)
                    buf.AppendF(" Order={0}", column.DisplayOrder);
                if (saveSort && column.SortOrder != -1)
                    buf.AppendF(" Sort={0}{1}", column.SortOrder, column.SortDirection == ImGuiSortDirection.Ascending ? 'v' : '^');
                if (column.ID != 0)
                    buf.AppendF(" ID=0x{0:X8}", column.ID);
                buf.Append("\n");
            }
            if (ctx.IO.ConfigIniSettingsSaveLastUsedDate)
            {
                int lastUsedDate = settings.LastUsedDate.Unpack();
                if (lastUsedDate != 0)
                    buf.AppendF("LastUsed={0:D8}\n", lastUsedDate);
            }
            buf.Append("\n");
        }
    }

    /// <summary>
    /// Registers the `Table` settings handler.
    /// (, declared )
    /// </summary>
    internal static void TableSettingsAddSettingsHandler()
    {
        ImGuiSettingsHandler handler = new()
        {
            TypeName = "Table",
            TypeHash = ImHashStr("Table"),
            ClearAllFn = TableSettingsHandler_ClearAll,
            CleanupFn = TableSettingsHandler_Cleanup,
            ReadOpenFn = TableSettingsHandler_ReadOpen,
            ReadLineFn = TableSettingsHandler_ReadLine,
            ApplyAllFn = TableSettingsHandler_ApplyAll,
            WriteAllFn = TableSettingsHandler_WriteAll,
        };
        AddSettingsHandler(handler);
    }

    /// <summary>
    /// Parses a table identifier written as an `0x`-prefixed hexadecimal value.
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <param name="id">The parsed identifier.</param>
    /// <returns><see langword="true"/> when the text was a valid hexadecimal identifier.</returns>
    private static bool TryParseTableId(ReadOnlySpan<char> span, out uint id)
    {
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            span = span[2..];
        return uint.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out id);
    }

    #endregion

    #region SECTION: Tables: Garbage Collection

    /// <summary>
    /// Compacts the context's table settings list by dropping invalidated records, shrinking each
    /// surviving record's column array to the count actually in use, and rebinding every live table to
    /// re-find its record by identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Canonical measures each live record with <c>TableSettingsCalcChunkSize(settings->ColumnsCount)</c>
    /// and returns early when the total already equals the chunk stream size. The port expresses the same
    /// test over the managed representation: compaction is needed only when an invalidated entry exists or
    /// a surviving record holds more column entries than its current count requires, which is how a table
    /// that shrank leaves spare capacity behind.
    /// </para>
    /// <para>
    /// Every live table is rebound to -1 after compaction because the list indices that
    /// <see cref="ImGuiTable.SettingsOffset"/> holds are invalidated by the removal; the next load or save
    /// re-binds by identifier through <see cref="TableSettingsFindByID"/>.
    /// </para>
    /// </remarks>
    internal static void TableGcCompactSettings()
    {
        ImGuiContext g = GetContext();

        bool needsCompaction = false;
        for (int n = 0; n < g.SettingsTables.Count; n++)
        {
            ImGuiTableSettings settings = g.SettingsTables[n];
            if (settings.ID == 0 || settings.ColumnSettings.Count != settings.ColumnsCount)
            {
                needsCompaction = true;
                break;
            }
        }
        if (!needsCompaction)
            return;

        // Compact the live records into place and shrink each record's column array to the count in use.
        int writeIndex = 0;
        for (int n = 0; n < g.SettingsTables.Count; n++)
        {
            ImGuiTableSettings settings = g.SettingsTables[n];
            if (settings.ID == 0)
                continue;
            if (settings.ColumnSettings.Count > settings.ColumnsCount)
                settings.ColumnSettings.RemoveRange(settings.ColumnsCount, settings.ColumnSettings.Count - settings.ColumnsCount);
            settings.ColumnsCountMax = settings.ColumnsCount;
            g.SettingsTables[writeIndex++] = settings;
        }
        g.SettingsTables.RemoveRange(writeIndex, g.SettingsTables.Count - writeIndex);

        // List indices changed, so every binding is stale; tables re-find their record by identifier.
        for (int n = 0; n < g.Tables.Count; n++)
            g.Tables[n].SettingsOffset = -1;
    }

    /// <summary>
    /// Removes table data from the context pool. Ports canonical <c>ImGui::TableRemove</c>.
    /// </summary>
    public static void TableRemove(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        int tableIdx = g.Tables.IndexOf(table);
        g.Tables.Remove(table);
        if (tableIdx >= 0 && tableIdx < g.TablesLastTimeActive.Count)
            g.TablesLastTimeActive[tableIdx] = -1.0f;
    }

    /// <summary>
    /// Free up and compact internal table transient buffers when unused.
    /// Ports canonical <c>ImGui::TableGcCompactTransientBuffers</c>.
    /// </summary>
    public static void TableGcCompactTransientBuffers(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        table.SortSpecs.Specs = null;
        table.SortSpecsMulti.Clear();
        table.IsSortSpecsDirty = true;
        table.ColumnsNames.Clear();
        table.MemoryCompacted = true;
        for (int n = 0; n < table.ColumnsCount; n++)
            table.Columns[n].NameOffset = -1;
        int tableIdx = g.Tables.IndexOf(table);
        if (tableIdx >= 0 && tableIdx < g.TablesLastTimeActive.Count)
            g.TablesLastTimeActive[tableIdx] = -1.0f;
    }

    /// <summary>
    /// Compacts table temp data transient buffers. Ports canonical <c>ImGui::TableGcCompactTransientBuffers</c>.
    /// </summary>
    public static void TableGcCompactTransientBuffers(ImGuiTableTempData tempData)
    {
        tempData.AngledHeadersRequests.Clear();
        tempData.DrawSplitter.ClearFreeMemory();
        tempData.LastTimeActive = -1.0f;
    }

    #endregion

    #endregion


    #region SECTION: HEADERS, SORTING, AND CONTEXT MENU

    #region SECTION: Tables: Sorting

    /// <summary>
    /// Returns the current sort specifications for the table that is being submitted, or
    /// <see langword="null"/> when there is no table scope or the table was not created with
    /// <see cref="ImGuiTableFlags.Sortable"/>. Canvases canonical <c>ImGui::TableGetSortSpecs</c>
    ///.
    /// </summary>
    /// <returns>
    /// The table's live <see cref="ImGuiTableSortSpecs"/> record, whose <see cref="ImGuiTableSortSpecs.Specs"/>
    /// view and <see cref="ImGuiTableSortSpecs.SpecsCount"/> are refreshed by
    /// <see cref="TableSortSpecsBuild"/>. The record belongs to the table and must not be retained across
    /// frames or past a subsequent <c>BeginTable</c> call.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Layout is forced with <see cref="TableUpdateLayout"/> when it has not run yet, because layout can
    /// change <c>IsSortSpecsDirty</c> (for example when a hidden sorted column is disabled) even if the
    /// caller never submitted a header row.
    /// </para>
    /// <para>
    /// The caller contract matches canonical: when <see cref="ImGuiTableSortSpecs.SpecsDirty"/> is set the
    /// caller sorts its own data and then clears the flag, otherwise the sort would repeat every frame
    /// </para>
    /// </remarks>
    public static ImGuiTableSortSpecs? TableGetSortSpecs()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        if (table == null || (table.Flags & ImGuiTableFlags.Sortable) == 0)
            return null;

        // Require layout in case the caller never submitted TableHeadersRow(); layout may clear sort
        // orders of disabled columns and therefore alter IsSortSpecsDirty.
        if (!table.IsLayoutLocked)
            TableUpdateLayout(table);

        TableSortSpecsBuild(table);
        return table.SortSpecs;
    }

    /// <summary>
    /// Reads the n-th available sort direction packed into a column's two-bit direction list. Canvases
    /// canonical <c>TableGetColumnAvailSortDirection</c>.
    /// </summary>
    /// <param name="column">The column whose available directions are queried.</param>
    /// <param name="n">The zero-based index into the available-direction list; must be smaller than
    /// <see cref="ImGuiTableColumn.SortDirectionsAvailCount"/>.</param>
    /// <returns>The direction stored in the two-bit slot.</returns>
    /// <remarks>
    /// The direction list is packed two bits per entry in
    /// <see cref="ImGuiTableColumn.SortDirectionsAvailList"/>, so the slot index is shifted left by one
    /// before masking with <c>0x03</c>.
    /// </remarks>
    private static ImGuiSortDirection TableGetColumnAvailSortDirection(ImGuiTableColumn column, int n)
    {
        ImAssert.Check(n < column.SortDirectionsAvailCount);
        return (ImGuiSortDirection)((column.SortDirectionsAvailList >> (n << 1)) & 0x03);
    }

    /// <summary>
    /// Repairs a column's sort direction when it currently names a direction that the column no longer
    /// allows, for example after a <see cref="ImGuiTableColumnFlags.NoSortAscending"/> or
    /// <see cref="ImGuiTableColumnFlags.NoSortDescending"/> flag was applied. Canvases canonical
    /// <c>ImGui::TableFixColumnSortDirection</c>.,
    ///.
    /// </summary>
    /// <param name="table">The table that owns <paramref name="column"/>; marked dirty when repaired.</param>
    /// <param name="column">The column to repair. A column that is unsorted or whose direction is still
    /// available is left untouched.</param>
    /// <remarks>
    /// The repair replaces the direction with the first available one and marks
    /// <see cref="ImGuiTable.IsSortSpecsDirty"/> so the exposed specifications are rebuilt
    /// </remarks>
    internal static void TableFixColumnSortDirection(ImGuiTable table, ImGuiTableColumn column)
    {
        if (column.SortOrder == -1
            || (column.SortDirectionsAvailMask & (1 << (int)column.SortDirection)) != 0)
            return;
        column.SortDirection = TableGetColumnAvailSortDirection(column, 0);
        table.IsSortSpecsDirty = true;
    }

    /// <summary>
    /// Calculates the sort direction that a click on the column header would apply next. Canvases
    /// canonical <c>ImGui::TableGetColumnNextSortDirection</c>
    ///.
    /// </summary>
    /// <param name="column">The column whose available directions drive the cycle.</param>
    /// <returns>The first available direction for an unsorted column, otherwise the next available
    /// direction in cyclic order.</returns>
    /// <remarks>
    /// The canonical ordering is pinned by a static assertion: <c>None == 0</c>, <c>Ascending == 1</c>,
    /// <c>Descending == 2</c>. The scan is bounded to three entries because
    /// a column can expose at most three directions, and an unreachable direction is a programming error
    ///. <see cref="ImGuiTableColumnFlags.PreferSortAscending"/> is
    /// never consulted here because ascending is the default and therefore already the first entry.
    /// </remarks>
    internal static ImGuiSortDirection TableGetColumnNextSortDirection(ImGuiTableColumn column)
    {
        ImAssert.Check(column.SortDirectionsAvailCount > 0);
        if (column.SortOrder == -1)
            return TableGetColumnAvailSortDirection(column, 0);
        for (int n = 0; n < 3; n++)
            if (column.SortDirection == TableGetColumnAvailSortDirection(column, n))
                return TableGetColumnAvailSortDirection(column, (n + 1) % column.SortDirectionsAvailCount);
        ImAssert.Check(false);
        return ImGuiSortDirection.None;
    }

    /// <summary>
    /// Applies a sort direction to one column, optionally appending it to an existing multi-sort instead
    /// of replacing the whole sort. Canvases canonical <c>ImGui::TableSetColumnSortDirection</c>
    ///.
    /// </summary>
    /// <param name="columnN">The index of the column to sort.</param>
    /// <param name="sortDirection">The requested direction. <see cref="ImGuiSortDirection.None"/> clears
    /// the column's sort order and is only accepted under <see cref="ImGuiTableFlags.SortTristate"/>.</param>
    /// <param name="appendToSortSpecs">The canonical "hold shift to append" request, which is honored only
    /// under <see cref="ImGuiTableFlags.SortMulti"/>.</param>
    /// <remarks>
    /// <para>
    /// The canonical implementation derives the next contiguous sort order from the current maximum, then
    /// clears or renumbers every column through
    /// <see cref="TableFixColumnSortDirection"/>. Both
    /// <see cref="ImGuiTable.IsSettingsDirty"/>, consumed by the in-memory settings owner (Chunk 13), and
    /// <see cref="ImGuiTable.IsSortSpecsDirty"/>, consumed by <see cref="TableSortSpecsBuild"/>, are set.
    /// </para>
    /// </remarks>
    internal static void TableSetColumnSortDirection(int columnN, ImGuiSortDirection sortDirection, bool appendToSortSpecs)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        ImAssert.Check(table != null);
        if (table == null)
            return;

        if ((table.Flags & ImGuiTableFlags.SortMulti) == 0)
            appendToSortSpecs = false;
        if ((table.Flags & ImGuiTableFlags.SortTristate) == 0)
            ImAssert.Check(sortDirection != ImGuiSortDirection.None);

        short sortOrderMax = 0;
        if (appendToSortSpecs)
            for (int otherColumnN = 0; otherColumnN < table.ColumnsCount; otherColumnN++)
                sortOrderMax = (short)Math.Max(sortOrderMax, table.Columns[otherColumnN].SortOrder);

        ImGuiTableColumn column = table.Columns[columnN];
        column.SortDirection = sortDirection;
        if (column.SortDirection == ImGuiSortDirection.None)
            column.SortOrder = -1;
        else if (column.SortOrder == -1 || !appendToSortSpecs)
            column.SortOrder = appendToSortSpecs ? (short)(sortOrderMax + 1) : (short)0;

        for (int otherColumnN = 0; otherColumnN < table.ColumnsCount; otherColumnN++)
        {
            ImGuiTableColumn otherColumn = table.Columns[otherColumnN];
            if (otherColumn != column && !appendToSortSpecs)
                otherColumn.SortOrder = -1;
            TableFixColumnSortDirection(table, otherColumn);
        }
        table.IsSettingsDirty = true;
        table.IsSortSpecsDirty = true;
    }

    /// <summary>
    /// Validates and normalizes the columns' sort orders before the public sort specifications are
    /// published: hidden columns are dropped, gaps and duplicates are compacted, single-sort tables keep
    /// only the first order, and a default column is chosen when nothing is sorted. Canvases canonical
    /// <c>ImGui::TableSortSpecsSanitize</c>.,
    ///.
    /// </summary>
    /// <param name="table">The table whose sort orders are normalized and whose
    /// <see cref="ImGuiTable.SortSpecsCount"/> is written.</param>
    /// <remarks>
    /// <para>
    /// A 64-bit mask records which sort-order slots are taken so gaps and duplicates can be detected with
    /// a single comparison. Compaction repeatedly selects the column
    /// with the smallest order and rewrites it densely from zero.
    /// </para>
    /// <para>
    /// When no column is sorted and the table is not tristate, the first enabled column that is not marked
    /// <see cref="ImGuiTableColumnFlags.NoSort"/> receives order zero and its first available direction
    /// </para>
    /// </remarks>
    internal static void TableSortSpecsSanitize(ImGuiTable table)
    {
        ImAssert.Check((table.Flags & ImGuiTableFlags.Sortable) != 0);

        // Clear SortOrder from hidden columns and verify that no sort-order slot is duplicated.
        int sortOrderCount = 0;
        ulong sortOrderMask = 0;
        for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
        {
            ImGuiTableColumn column = table.Columns[columnN];
            if (column.SortOrder != -1 && !column.IsEnabled)
                column.SortOrder = -1;
            if (column.SortOrder == -1)
                continue;
            sortOrderCount++;
            if (column.SortOrder < 64)
                sortOrderMask |= 1UL << column.SortOrder;
            ImAssert.Check(sortOrderCount < 64);
        }

        bool needFixLinearize = (1UL << sortOrderCount) != (sortOrderMask + 1);
        bool needFixSingleSortOrder = sortOrderCount > 1 && (table.Flags & ImGuiTableFlags.SortMulti) == 0;
        if (needFixLinearize || needFixSingleSortOrder)
        {
            Span<bool> visited = table.ColumnsCount <= 512 ? stackalloc bool[table.ColumnsCount] : new bool[table.ColumnsCount];
            for (int sortN = 0; sortN < sortOrderCount; sortN++)
            {
                // Rewrite the order fields so they have no gap or duplicate. For example when order 0
                // disappears and orders 1.2 remain, they are rewritten as 0.1
                int columnWithSmallestSortOrder = -1;
                for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                    if (!visited[columnN] && table.Columns[columnN].SortOrder != -1)
                        if (columnWithSmallestSortOrder == -1
                            || table.Columns[columnN].SortOrder < table.Columns[columnWithSmallestSortOrder].SortOrder)
                            columnWithSmallestSortOrder = columnN;
                ImAssert.Check(columnWithSmallestSortOrder != -1);
                visited[columnWithSmallestSortOrder] = true;
                table.Columns[columnWithSmallestSortOrder].SortOrder = (short)sortN;

                // Keep only the first order when the table does not allow multi-sort
                if (needFixSingleSortOrder)
                {
                    sortOrderCount = 1;
                    for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                        if (columnN != columnWithSmallestSortOrder)
                            table.Columns[columnN].SortOrder = -1;
                    break;
                }
            }
        }

        // Fallback default sort order, used when no column requested one through
        // ImGuiTableColumnFlags.DefaultSort.
        if (sortOrderCount == 0 && (table.Flags & ImGuiTableFlags.SortTristate) == 0)
            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
            {
                ImGuiTableColumn column = table.Columns[columnN];
                if (column.IsEnabled && (column.Flags & ImGuiTableColumnFlags.NoSort) == 0)
                {
                    sortOrderCount = 1;
                    column.SortOrder = 0;
                    column.SortDirection = TableGetColumnAvailSortDirection(column, 0);
                    break;
                }
            }

        table.SortSpecsCount = (short)sortOrderCount;
    }

    /// <summary>
    /// Rebuilds and publishes the table's public sort specifications, marking them dirty for the caller
    /// when the underlying sort orders changed. Canvases canonical <c>ImGui::TableSortSpecsBuild</c>
    ///.
    /// </summary>
    /// <param name="table">The table whose <see cref="ImGuiTable.SortSpecs"/> record is refreshed.</param>
    /// <remarks>
    /// <para>
    /// Canonical points the exposed <c>Specs</c> pointer either at the table's single specification or at
    /// the raw data of its multi-sort vector. Because
    /// <see cref="ImGuiTableSortSpecs.Specs"/> is a managed array view in this port, the array is reused
    /// while its length still matches <see cref="ImGuiTable.SortSpecsCount"/> and is only reallocated when
    /// the count changes. The multi-sort backing list is resized with element reuse for the same reason.
    /// </para>
    /// <para>
    /// <see cref="ImGuiTableSortSpecs.SpecsDirty"/> is set for the caller and
    /// <see cref="ImGuiTable.IsSortSpecsDirty"/> is cleared for the library exactly as canonical
    ///. The entries are written only on a dirty rebuild, indexed by
    /// each column's <see cref="ImGuiTableColumn.SortOrder"/>.
    /// </para>
    /// </remarks>
    internal static void TableSortSpecsBuild(ImGuiTable table)
    {
        bool dirty = table.IsSortSpecsDirty;
        if (dirty)
        {
            TableSortSpecsSanitize(table);

            // Canonical resize() of the multi-sort vector; the managed list reuses its elements so no
            // allocation happens once the table has reached its maximum sort count.
            int multiCount = table.SortSpecsCount <= 1 ? 0 : table.SortSpecsCount;
            while (table.SortSpecsMulti.Count > multiCount)
                table.SortSpecsMulti.RemoveAt(table.SortSpecsMulti.Count - 1);
            while (table.SortSpecsMulti.Count < multiCount)
                table.SortSpecsMulti.Add(new ImGuiTableColumnSortSpecs());

            table.SortSpecs.SpecsDirty = true; // Mark as dirty for the caller.
            table.IsSortSpecsDirty = false;    // Mark as not dirty for the library.
        }

        // Choose the backing storage: null for zero specs, the single specification for one, or the
        // multi-sort array view otherwise.
        ImGuiTableColumnSortSpecs[]? sortSpecs;
        if (table.SortSpecsCount == 0)
        {
            sortSpecs = null;
        }
        else if (table.SortSpecsCount == 1)
        {
            if (table.SortSpecs.Specs == null || table.SortSpecs.Specs.Length != 1)
                table.SortSpecs.Specs = new ImGuiTableColumnSortSpecs[1];
            sortSpecs = table.SortSpecs.Specs;
            sortSpecs[0] = table.SortSpecsSingle;
        }
        else
        {
            if (table.SortSpecs.Specs == null || table.SortSpecs.Specs.Length != table.SortSpecsCount)
                table.SortSpecs.Specs = new ImGuiTableColumnSortSpecs[table.SortSpecsCount];
            sortSpecs = table.SortSpecs.Specs;
            for (int n = 0; n < table.SortSpecsCount; n++)
                sortSpecs[n] = table.SortSpecsMulti[n];
        }

        if (dirty && sortSpecs != null)
            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
            {
                ImGuiTableColumn column = table.Columns[columnN];
                if (column.SortOrder == -1)
                    continue;
                ImAssert.Check(column.SortOrder < table.SortSpecsCount);
                ImGuiTableColumnSortSpecs sortSpec = sortSpecs[column.SortOrder];
                sortSpec.ColumnUserID = column.UserData;
                sortSpec.ColumnIndex = (short)columnN;
                sortSpec.SortOrder = column.SortOrder;
                sortSpec.SortDirection = column.SortDirection;
            }

        table.SortSpecs.Specs = sortSpecs;
        table.SortSpecs.SpecsCount = table.SortSpecsCount;
    }

    #endregion

    #region SECTION: Tables: Headers

    /// <summary>
    /// Calculates the uniform height of a header row so that short labels still fill the tallest cell.
    /// Canvases canonical <c>ImGui::TableGetHeaderRowHeight</c>
    ///.
    /// </summary>
    /// <returns>The row height: the larger of the font size and every displayed enabled header label's
    /// height, plus twice the vertical cell padding.</returns>
    /// <remarks>
    /// Columns marked <see cref="ImGuiTableColumnFlags.NoHeaderLabel"/> are excluded because
    /// <see cref="TableHeadersRow"/> submits an empty label for them
    /// </remarks>
    internal static float TableGetHeaderRowHeight()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        ImAssert.Check(table != null);
        float rowHeight = g.FontSize;
        if (table != null)
            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                if (table.EnabledMaskByIndex.TestBit(columnN))
                    if ((table.Columns[columnN].Flags & ImGuiTableColumnFlags.NoHeaderLabel) == 0)
                        rowHeight = MathF.Max(rowHeight, CalcTextSize(TableGetColumnName(table, columnN), false, -1.0f).Y);
        return rowHeight + g.Style.CellPadding.Y * 2.0f;
    }

    /// <summary>
    /// Calculates the widest angled-header label so <see cref="TableAngledHeadersRowEx"/> can derive its
    /// row height from it. Canvases canonical <c>ImGui::TableGetHeaderAngledMaxLabelWidth</c>
    ///.
    /// </summary>
    /// <returns>The widest enabled angled-header label plus twice the vertical cell padding, which acts as
    /// the horizontal padding for a rotated label.</returns>
    /// <remarks>
    /// The label is measured with <c>hide_text_after_double_hash</c> enabled because the identifier suffix
    /// is not drawn.
    /// </remarks>
    internal static float TableGetHeaderAngledMaxLabelWidth()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        ImAssert.Check(table != null);
        float width = 0.0f;
        if (table != null)
            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
                if (table.EnabledMaskByIndex.TestBit(columnN))
                    if ((table.Columns[columnN].Flags & ImGuiTableColumnFlags.AngledHeader) != 0)
                        width = MathF.Max(width, CalcTextSize(TableGetColumnName(table, columnN), true, -1.0f).X);
        return width + g.Style.CellPadding.Y * 2.0f; // Swapped padding: horizontal for a rotated label.
    }

    /// <summary>
    /// Submits a full header row by calling <see cref="TableHeader"/> once per column name declared through
    /// <c>TableSetupColumn</c>, then opens the table context menu from the unused area to the right of the
    /// last column. Canvases canonical <c>ImGui::TableHeadersRow</c>
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout is forced here so that a debugger can step into <c>TableUpdateLayout</c>; the normal path
    /// would have already run it from <c>TableNextRow</c>. When the host
    /// window is fully clipped the row is still opened but no header cell is submitted
    /// </para>
    /// <para>
    /// A column is submitted even when <see cref="TableSetColumnIndex"/> reports it as not requested, as
    /// long as it is the column currently being dragged, so a reorder drag keeps its source header alive
    /// </para>
    /// <para>
    /// Only the context menu is opened from this helper; the popup body itself is submitted by the caller
    /// through <see cref="TableBeginContextMenuPopup"/> and <see cref="TableDrawDefaultContextMenu"/>.
    /// </para>
    /// </remarks>
    public static void TableHeadersRow()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }

        if (!table.IsLayoutLocked)
            TableUpdateLayout(table);

        float rowHeight = TableGetHeaderRowHeight();
        TableNextRow(ImGuiTableRowFlags.Headers, rowHeight);
        float rowY1 = GetCursorScreenPos().Y;
        if (table.HostSkipItems) // Merely an optimization, callers may skip this in their own code.
            return;

        int columnsCount = TableGetColumnCount();
        for (int columnN = 0; columnN < columnsCount; columnN++)
        {
            if (!TableSetColumnIndex(columnN) && table.LastHeldHeaderColumn != columnN)
                continue;

            // Pushing an identifier makes empty and unnamed headers distinct, and gives automation a
            // stable identifier path per column.
            string name = (TableGetColumnFlags(columnN) & ImGuiTableColumnFlags.NoHeaderLabel) != 0
                ? ""
                : TableGetColumnName(table, columnN);
            PushID(columnN);
            TableHeader(name);
            PopID();
        }

        // Allow opening the context menu from the unused area to the right of the last column.
        ImVec2 mousePos = GetMousePos();
        if (IsMouseReleased(ImGuiMouseButton.Right) && TableGetHoveredColumn() == columnsCount)
            if (mousePos.Y >= rowY1 && mousePos.Y < rowY1 + rowHeight)
                TableOpenContextMenu(columnsCount); // Opens a non-column-specific popup.
    }

    /// <summary>
    /// Submits one column header cell: background, optional sort arrow and multi-sort index, the
    /// CPU-clipped label, the reorder drag source, and the click-to-sort interaction. Canvases canonical
    /// <c>ImGui::TableHeader</c>., declared in
    /// </summary>
    /// <param name="label">The header text. A <see langword="null"/> value is treated as an empty label,
    /// matching canonical. The portion before a <c>##</c> marker is measured and rendered.</param>
    /// <remarks>
    /// <para>
    /// The label is clipped on the CPU rather than by the GPU so that adjacent header cells usually merge
    /// into one draw command; consequently <c>SameLine</c> cannot be used after a header
    /// </para>
    /// <para>
    /// The unclipped header width is fed to <see cref="ImGuiTableColumn.ContentMaxXHeadersUsed"/> and
    /// <see cref="ImGuiTableColumn.ContentMaxXHeadersIdeal"/> without writing on the cursor maximum so the
    /// column still participates in draw-command merging.
    /// </para>
    /// <para>
    /// Reorder drag queues display-order changes owned by the column reorder path; drag &amp; drop
    /// (Chunk 8) is only observed through <see cref="ImGuiContext.DragDropActive"/> to suppress reordering
    /// while a drag is active, and no drag &amp; drop behavior is implemented here.
    /// </para>
    /// <para>
    /// The canonical test-engine item registration
    /// (<c>IMGUI_TEST_ENGINE_ITEM_INFO</c>, ) is not ported, because this port
    /// has no test-engine instrumentation layer.
    /// </para>
    /// </remarks>
    public static void TableHeader(string? label)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        ImAssert.Check(table.CurrentColumn != -1);
        int columnN = table.CurrentColumn;
        ImGuiTableColumn column = table.Columns[columnN];

        // Label
        label ??= "";
        string labelDisplayEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelDisplayEnd, false, -1.0f);
        ImVec2 labelPos = window.DC.CursorPos;

        // If a row height was already requested, use it.
        ImRect cellR = TableGetCellBgRect(table, columnN);
        float labelHeight = MathF.Max(labelSize.Y, table.RowMinHeight - table.RowCellPaddingY * 2.0f);

        // Ideal size for the sort-order arrow and the optional multi-sort index.
        float wArrow = 0.0f;
        float wSortText = 0.0f;
        bool sortArrow = false;
        string sortOrderSuf = "";
        const float ArrowScale = 0.65f;
        if ((table.Flags & ImGuiTableFlags.Sortable) != 0 && (column.Flags & ImGuiTableColumnFlags.NoSort) == 0)
        {
            wArrow = TruncateToInt(g.FontSize * ArrowScale + g.Style.FramePadding.X);
            if (column.SortOrder != -1)
                sortArrow = true;
            if (column.SortOrder > 0)
            {
                sortOrderSuf = ImFormatString("%d", column.SortOrder + 1);
                wSortText = g.Style.ItemInnerSpacing.X + CalcTextSize(sortOrderSuf, false, -1.0f).X;
            }
        }

        // Feed the unclipped width to the column without writing on CursorMaxPos, so the column is still
        // considered for draw-command merging. When an arrow is displayed the used width claims the whole
        // cell.
        float maxPosX = labelPos.X + labelSize.X + wSortText + wArrow;
        column.ContentMaxXHeadersUsed = MathF.Max(column.ContentMaxXHeadersUsed, sortArrow ? cellR.Max.X : MathF.Min(maxPosX, cellR.Max.X));
        column.ContentMaxXHeadersIdeal = MathF.Max(column.ContentMaxXHeadersIdeal, maxPosX);

        // Keep the header highlighted while the context menu is open.
        uint id = GetID(label);
        ImRect bb = new ImRect(cellR.Min.X, cellR.Min.Y, cellR.Max.X, MathF.Max(cellR.Max.Y, cellR.Min.Y + labelHeight + g.Style.CellPadding.Y * 2.0f));
        ItemSize(new ImVec2(0.0f, labelHeight)); // Declare no unclipped width; it was fed above.
        if (!ItemAdd(bb, id))
            return;

        // AllowOverlap is used because the header covers the whole cell while still allowing subsequent
        // items to be submitted inside it.
        bool highlight = table.HighlightColumnHeader == columnN;
        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held, ImGuiButtonFlags.AllowOverlap);
        if (held || hovered || highlight)
        {
            uint col = GetColorU32(held ? ImGuiCol.HeaderActive : hovered ? ImGuiCol.HeaderHovered : ImGuiCol.Header);
            TableSetBgColor(ImGuiTableBgTarget.CellBg, col, table.CurrentColumn);
        }
        else
        {
            // Submit a single cell background when no full header row was submitted.
            if ((table.RowFlags & ImGuiTableRowFlags.Headers) == 0)
                TableSetBgColor(ImGuiTableBgTarget.CellBg, GetColorU32(ImGuiCol.TableHeaderBg), table.CurrentColumn);
        }
        RenderNavCursor(bb, id, ImGuiNavRenderCursorFlags.Compact, 0.0f);
        if (held)
            table.HeldHeaderColumn = (short)columnN;
        window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, window.DC.CursorPos.Y - g.Style.ItemSpacing.Y * 0.5f);

        // Drag to reorder columns. While a column moves it jumps to the other side of the mouse, so the
        // horizontal mouse delta is tested as well, and hidden columns are crossed by following the
        // previous/next enabled links; TableQueueSetColumnDisplayOrder validates the queued order
        if (held && (table.Flags & ImGuiTableFlags.Reorderable) != 0 && IsMouseDragging(ImGuiMouseButton.Left) && !g.DragDropActive)
        {
            table.InstanceInteracted = table.InstanceCurrent;
            if (g.IO.MouseDelta.X < 0.0f && g.IO.MousePos.X < cellR.Min.X)
                if (column.PrevEnabledColumn != -1)
                {
                    ImGuiTableColumn prevColumn = table.Columns[column.PrevEnabledColumn];
                    TableQueueSetColumnDisplayOrder(table, columnN, prevColumn.DisplayOrder);
                }
            if (g.IO.MouseDelta.X > 0.0f && g.IO.MousePos.X > cellR.Max.X)
                if (column.NextEnabledColumn != -1)
                {
                    ImGuiTableColumn nextColumn = table.Columns[column.NextEnabledColumn];
                    TableQueueSetColumnDisplayOrder(table, columnN, nextColumn.DisplayOrder);
                }
        }

        // Sort-order arrow and click-to-sort. The label is only clipped for a visible arrow; auto-fit
        // still accounts for a possible arrow, but a manually narrowed column is not clipped for an arrow
        // that is not displayed.
        float ellipsisMax = MathF.Max(cellR.Max.X - (sortArrow ? wArrow + wSortText : 0.0f), labelPos.X);
        if ((table.Flags & ImGuiTableFlags.Sortable) != 0 && (column.Flags & ImGuiTableColumnFlags.NoSort) == 0)
        {
            if (column.SortOrder != -1)
            {
                float x = MathF.Max(cellR.Min.X, cellR.Max.X - wArrow - wSortText);
                float y = labelPos.Y;
                if (column.SortOrder > 0)
                {
                    PushStyleColor(ImGuiCol.Text, GetColorU32(ImGuiCol.Text, 0.70f));
                    RenderText(new ImVec2(x + g.Style.ItemInnerSpacing.X, y), sortOrderSuf);
                    PopStyleColor();
                    x += wSortText;
                }
                RenderArrow(window.DrawList, new ImVec2(x, y), GetColorU32(ImGuiCol.Text),
                    column.SortDirection == ImGuiSortDirection.Ascending ? ImGuiDir.Up : ImGuiDir.Down, ArrowScale);
            }

            // Handle clicking on the column header to adjust the sort order.
            if (pressed && table.ReorderColumn != columnN)
            {
                ImGuiSortDirection sortDirection = TableGetColumnNextSortDirection(column);
                TableSetColumnSortDirection(columnN, sortDirection, g.IO.KeyShift);
            }
        }

        // Render the clipped label so that in most situations all header cells merge into a single draw
        // command.
        RenderTextEllipsis(window.DrawList, labelPos, new ImVec2(ellipsisMax, bb.Max.Y), ellipsisMax, label, labelDisplayEnd, labelSize);

        bool textClipped = labelSize.X > (ellipsisMax - labelPos.X);
        if (textClipped && hovered && g.ActiveId == 0)
            SetItemTooltip("%s", labelDisplayEnd);

        // BeginPopupContextItem() is not used because the popup must stay open even after the column is
        // hidden.
        if (IsPopupOpenRequestForItem(ImGuiPopupFlags.None, id))
            TableOpenContextMenu(columnN);
    }

    /// <summary>
    /// Builds the angled-header requests from the enabled columns that declare
    /// <see cref="ImGuiTableColumnFlags.AngledHeader"/> and submits them through
    /// <see cref="TableAngledHeadersRowEx"/>. Canvases canonical <c>ImGui::TableAngledHeadersRow</c>
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This row must be the first row of the table, which is enforced by
    /// <see cref="TableAngledHeadersRowEx"/>. The highlighted column is taken from the last held header or
    /// the layout's highlight column, and otherwise from the column hovered in the first row when the mouse
    /// is not over a resize border and no unrelated item is active
    /// </para>
    /// <para>
    /// The request list reuses its existing <see cref="ImGuiTableHeaderData"/> instances instead of
    /// clearing it first, because canonical stores the requests by value while the port's structure is a
    /// reference type; reusing avoids a per-frame allocation while producing the same compact sequence.
    /// </para>
    /// </remarks>
    public static void TableAngledHeadersRow()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        ImGuiTableTempData tempData = table.TempData!;
        List<ImGuiTableHeaderData> requests = tempData.AngledHeadersRequests;
        if (requests.Capacity < table.ColumnsEnabledCount)
            requests.Capacity = table.ColumnsEnabledCount;

        // Which column needs the highlight?
        uint rowId = GetID("##AngledHeaders");
        ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, table.InstanceCurrent);
        int highlightColumnN = table.LastHeldHeaderColumn != -1 ? table.LastHeldHeaderColumn : table.HighlightColumnHeader;
        if (highlightColumnN == -1 && table.HoveredColumnBody != -1)
            if (tableInstance.HoveredRowLast == 0 && table.HoveredColumnBorder == -1
                && (g.ActiveId == 0 || g.ActiveId == rowId || table.IsActiveIdInTable || g.DragDropActive))
                highlightColumnN = table.HoveredColumnBody;

        // Build the compact request sequence in display order.
        uint colHeaderBg = GetColorU32(ImGuiCol.TableHeaderBg);
        uint colText = GetColorU32(ImGuiCol.Text);
        int requestCount = 0;
        for (int orderN = 0; orderN < table.ColumnsCount; orderN++)
            if (table.EnabledMaskByDisplayOrder.TestBit(orderN))
            {
                int columnN = table.DisplayOrderToIndex[orderN];
                ImGuiTableColumn column = table.Columns[columnN];
                if ((column.Flags & ImGuiTableColumnFlags.AngledHeader) == 0) // Cannot rely on IsVisible here.
                    continue;

                ImGuiTableHeaderData request;
                if (requestCount < requests.Count)
                {
                    request = requests[requestCount];
                }
                else
                {
                    request = new ImGuiTableHeaderData();
                    requests.Add(request);
                }
                request.Index = (short)columnN;
                request.TextColor = colText;
                request.BgColor0 = colHeaderBg;
                request.BgColor1 = columnN == highlightColumnN ? GetColorU32(ImGuiCol.Header) : 0u;
                requestCount++;
            }
        while (requests.Count > requestCount)
            requests.RemoveAt(requests.Count - 1);

        // Render the row.
        TableAngledHeadersRowEx(rowId, g.Style.TableAngledHeadersAngle, 0.0f, requests, requests.Count);
    }

    /// <summary>
    /// Submits the rotated, multi-line labels of an angled header row. Canvases canonical
    /// <c>ImGui::TableAngledHeadersRowEx</c>.,
    ///.
    /// </summary>
    /// <param name="rowId">The identifier of the row; it is also used as the button identifier covering the
    /// whole row.</param>
    /// <param name="angle">The label rotation in radians, expressed in the easier-to-reason-about range
    /// <c>(-PI/2. +PI/2)</c>; a negative angle flips the label.</param>
    /// <param name="maxLabelWidth">The clip width of each label, or zero to derive it from
    /// <see cref="TableGetHeaderAngledMaxLabelWidth"/>.</param>
    /// <param name="data">The requests to draw, fed left to right.</param>
    /// <param name="dataCount">The number of valid entries in <paramref name="data"/>.</param>
    /// <remarks>
    /// <para>
    /// The vertices submitted by <c>RenderTextEllipsis</c> for each line are rotated and offset in place
    /// through <see cref="ShadeVertsTransformPos"/>, so each line follows the horizontal cell border
    /// instead of the whole text block being rotated.
    /// </para>
    /// <para>
    /// The extra width consumed beyond the right-most enabled column is stored in
    /// <see cref="ImGuiTableTempData.AngledHeadersExtraWidth"/> for <c>EndTable</c> to account for
    /// </para>
    /// </remarks>
    internal static void TableAngledHeadersRowEx(uint rowId, float angle, float maxLabelWidth, List<ImGuiTableHeaderData> data, int dataCount)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        ImGuiWindow window = g.CurrentWindow!;
        ImDrawList drawList = window.DrawList;
        if (table == null)
        {
            AssertUserError(false, "Call should only be done while in BeginTable() scope!");
            return;
        }
        ImAssert.Check(table.CurrentRow == -1, "Must be first row");

        if (maxLabelWidth == 0.0f)
            maxLabelWidth = TableGetHeaderAngledMaxLabelWidth();

        // The angle argument is expressed in (-PI/2. +PI/2) as it is easier to think about for the
        // caller; the actual rotation angle points the labels upward.
        bool flipLabel = angle < 0.0f;
        angle -= MathF.PI * 0.5f;
        float cosA = MathF.Cos(angle);
        float sinA = MathF.Sin(angle);
        float labelCosA = flipLabel ? MathF.Cos(angle + MathF.PI) : cosA;
        float labelSinA = flipLabel ? MathF.Sin(angle + MathF.PI) : sinA;
        ImVec2 unitRight = new ImVec2(cosA, sinA);

        // Calculate the base metrics and set the angled-header data before the first TableNextRow() call,
        // because TableUpdateLayout() consumes them.
        float headerHeight = g.FontSize + g.Style.CellPadding.X * 2.0f;
        float rowHeight = TruncateToInt(MathF.Abs(ImRotate(new ImVec2(maxLabelWidth, flipLabel ? +headerHeight : -headerHeight), cosA, sinA).Y));
        table.AngledHeadersHeight = rowHeight;
        table.AngledHeadersSlope = sinA != 0.0f ? cosA / sinA : 0.0f;
        ImVec2 headerAngledVector = unitRight * (rowHeight / -sinA); // Bottom-left to top-left, bottom-right to top-right.

        // Declare the row, then override and draw the row background manually.
        TableNextRow(ImGuiTableRowFlags.Headers, rowHeight);
        TableNextColumn();
        ImRect rowR = new ImRect(table.WorkRect.Min.X, table.BgClipRect.Min.Y, table.WorkRect.Max.X, table.RowPosY2);
        table.DrawSplitter!.SetCurrentChannel(drawList, TABLE_DRAW_CHANNEL_BG0);
        float clipRectMinX = table.BgClipRect.Min.X;
        if (table.FreezeColumnsCount > 0)
            clipRectMinX = MathF.Max(clipRectMinX, table.Columns[table.DisplayOrderToIndex[table.FreezeColumnsCount - 1]].MaxX);
        TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0, -1); // Cancel the automatic header background.
        PushClipRect(table.BgClipRect.Min, table.BgClipRect.Max, false); // Span all columns.
        drawList.AddRectFilled(new ImVec2(table.BgClipRect.Min.X, rowR.Min.Y), new ImVec2(table.BgClipRect.Max.X, rowR.Max.Y), GetColorU32(ImGuiCol.TableHeaderBg, 0.25f));
        PushClipRect(new ImVec2(clipRectMinX, table.BgClipRect.Min.Y), table.BgClipRect.Max, true); // Skip frozen columns.

        ButtonBehavior(rowR, rowId, out _, out _);
        KeepAliveID(rowId);

        float ascentScaled = g.FontBaked!.Ascent * g.FontBakedScale;
        float lineOffForAscentX = (MathF.Max((g.FontSize - ascentScaled) * 0.5f, 0.0f) / -sinA) * (flipLabel ? -1.0f : 1.0f);
        ImVec2 padding = g.Style.CellPadding; // The swapped component is always used for angled headers.
        ImVec2 align = g.Style.TableAngledHeadersTextAlign;

        // Draw the backgrounds and labels in the first pass, then all column borders in the second.
        float maxX = -float.MaxValue;
        for (int pass = 0; pass < 2; pass++)
            for (int orderN = 0; orderN < dataCount; orderN++)
            {
                ImGuiTableHeaderData request = data[orderN];
                int columnN = request.Index;
                ImGuiTableColumn column = table.Columns[columnN];

                // The header quadrilateral is the cell's baseline plus the angled vector. Four locals are
                // used instead of a four-element array to avoid a per-header heap allocation.
                ImVec2 bgShape0 = new ImVec2(column.MaxX, rowR.Max.Y);
                ImVec2 bgShape1 = new ImVec2(column.MinX, rowR.Max.Y);
                ImVec2 bgShape2 = bgShape1 + headerAngledVector;
                ImVec2 bgShape3 = bgShape0 + headerAngledVector;
                if (pass == 0)
                {
                    // Draw the shape, then the optional highlight on top.
                    drawList.AddQuadFilled(bgShape0, bgShape1, bgShape2, bgShape3, request.BgColor0);
                    drawList.AddQuadFilled(bgShape0, bgShape1, bgShape2, bgShape3, request.BgColor1);
                    maxX = MathF.Max(maxX, bgShape3.X);

                    // Draw the label at an offset where RenderTextXXX() will not apply the current clip
                    // rect, then transform the submitted vertices to their final rotated position. Multiple
                    // lines are handled manually so each line follows the horizontal border
                    string labelName = TableGetColumnName(table, columnN);
                    string labelNameEnd = FindRenderedTextEnd(labelName);
                    float lineOffStepX = g.FontSize / -sinA;
                    int labelLines = TextCountLines(labelNameEnd);

                    // Left/right alignment.
                    float lineOffCurrX = flipLabel ? (labelLines - 1) * lineOffStepX : 0.0f;
                    float lineOffForAlignX = MathF.Floor(MathF.Max((((column.MaxX - column.MinX) - padding.X * 2.0f) - (labelLines * lineOffStepX)), 0.0f) * align.X);
                    lineOffCurrX += lineOffForAlignX - lineOffForAscentX;

                    // Register the header width.
                    column.ContentMaxXHeadersUsed = column.ContentMaxXHeadersIdeal = column.WorkMinX + MathF.Ceiling(labelLines * lineOffStepX - lineOffForAlignX);

                    int lineStart = 0;
                    while (lineStart < labelNameEnd.Length)
                    {
                        int newlineIndex = labelNameEnd.IndexOf('\n', lineStart);
                        int lineEnd = newlineIndex < 0 ? labelNameEnd.Length : newlineIndex;
                        string line = labelNameEnd.Substring(lineStart, lineEnd - lineStart);

                        // FIXME: individual line clipping for the right-most column is broken for negative
                        // angles.
                        ImVec2 labelSize = CalcTextSize(line, false, -1.0f);
                        float clipWidth = maxLabelWidth - padding.Y; // padding.Y*2 would be symmetrical but hide more text.
                        float clipHeight = MathF.Min(labelSize.Y, column.ClipRect.Max.X - column.WorkMinX - lineOffCurrX);
                        ImRect clipR = new ImRect(window.ClipRect.Min, window.ClipRect.Min + new ImVec2(clipWidth, clipHeight));
                        int vtxIdxBegin = drawList.VtxBuffer.Count;
                        PushStyleColor(ImGuiCol.Text, request.TextColor);
                        RenderTextEllipsis(drawList, clipR.Min, clipR.Max, clipR.Max.X, line, line, labelSize);
                        PopStyleColor();
                        int vtxIdxEnd = drawList.VtxBuffer.Count;

                        // Up/down alignment.
                        float availableSpace = MathF.Max(clipWidth - labelSize.X + MathF.Abs(padding.X * cosA) * 2.0f - MathF.Abs(padding.Y * sinA) * 2.0f, 0.0f);
                        float verticalOffset = availableSpace * align.Y * (flipLabel ? -1.0f : 1.0f);

                        // Rotate and offset the submitted label vertices.
                        ImVec2 pivotIn = new ImVec2(window.ClipRect.Min.X - verticalOffset, window.ClipRect.Min.Y + labelSize.Y);
                        ImVec2 pivotOut = new ImVec2(column.WorkMinX, rowR.Max.Y);
                        lineOffCurrX += flipLabel ? -lineOffStepX : lineOffStepX;
                        pivotOut += unitRight * padding.Y;
                        if (flipLabel)
                            pivotOut += unitRight * (clipWidth - MathF.Max(0.0f, clipWidth - labelSize.X));
                        pivotOut.X += flipLabel ? lineOffCurrX + lineOffStepX : lineOffCurrX;
                        ShadeVertsTransformPos(drawList, vtxIdxBegin, vtxIdxEnd, pivotIn, labelCosA, labelSinA, pivotOut);

                        lineStart = lineEnd + 1;
                    }
                }
                if (pass == 1)
                {
                    // Draw the column border on the header's leading edge.
                    drawList.AddLine(bgShape0 - new ImVec2(TABLE_BORDER_SIZE * 0.5f, 0.0f), bgShape3 - new ImVec2(TABLE_BORDER_SIZE * 0.5f, 0.0f), TableGetColumnBorderCol(table, orderN, columnN), TABLE_BORDER_SIZE);
                }
            }
        PopClipRect();
        PopClipRect();
        table.TempData!.AngledHeadersExtraWidth = MathF.Max(0.0f, maxX - table.Columns[table.RightMostEnabledColumn].MaxX);
    }

    /// <summary>
    /// Counts the lines of a rendered label, not counting a trailing carriage return as an extra line.
    /// Canvases canonical <c>ImTextCountLines</c>.
    /// </summary>
    /// <param name="text">The rendered label text.</param>
    /// <returns>The number of newline-separated lines; zero for an empty string.</returns>
    /// <remarks>
    /// Canonical takes a begin/end pointer pair; a managed string already carries its length, so only the
    /// text is passed and the scan stops at the string end. This helper is private to the angled-header
    /// drawing path it serves.
    /// </remarks>
    private static int TextCountLines(string text)
    {
        int count = 0;
        int start = 0;
        while (start < text.Length)
        {
            int newlineIndex = text.IndexOf('\n', start);
            start = newlineIndex < 0 ? text.Length : newlineIndex + 1;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Rotates a range of already-submitted vertices around a pivot and translates them to an output
    /// pivot. Canvases canonical <c>ImGui::ShadeVertsTransformPos</c>
    /// </summary>
    /// <param name="drawList">The draw list whose shared vertex buffer is transformed.</param>
    /// <param name="vertStartIdx">The first vertex index of the range.</param>
    /// <param name="vertEndIdx">One past the last vertex index of the range.</param>
    /// <param name="pivotIn">The pivot subtracted before rotation.</param>
    /// <param name="cosA">The cosine of the rotation angle.</param>
    /// <param name="sinA">The sine of the rotation angle.</param>
    /// <param name="pivotOut">The pivot added after rotation.</param>
    /// <remarks>
    /// The port's draw-list splitter shares one vertex buffer across channels, so the live vertex count
    /// observed between two text submissions is the equivalent of canonical <c>draw_list-&gt;_VtxCurrentIdx</c>.
    /// The helper is private to the angled-header drawing path that needs it; canonical declares it
    /// <c>IMGUI_API</c>. and it is otherwise absent from the port.
    /// </remarks>
    public static void ShadeVertsTransformPos(ImDrawList drawList, int vertStartIdx, int vertEndIdx, ImVec2 pivotIn, float cosA, float sinA, ImVec2 pivotOut)
    {
        for (int n = vertStartIdx; n < vertEndIdx; n++)
        {
            ImDrawVert vertex = drawList.VtxBuffer[n];
            vertex.Pos = ImRotate(vertex.Pos - pivotIn, cosA, sinA) + pivotOut;
            drawList.VtxBuffer[n] = vertex;
        }
    }

    #endregion

    #region SECTION: Tables: Context Menu

    /// <summary>
    /// Opens the table's context-menu popup, optionally for a specific column. Canvases canonical
    /// <c>ImGui::TableOpenContextMenu</c>., declared in
    /// </summary>
    /// <param name="columnN">The column the menu applies to, or <c>-1</c> for a menu that is not specific
    /// to any column. <c>-1</c> resolves to the current column when one is selected, and a value equal to
    /// the column count (the "unused area" convention returned by <c>TableGetHoveredColumn</c>) normalizes
    /// back to <c>-1</c>.</param>
    /// <remarks>
    /// The popup is only opened when the table requested at least one of
    /// <see cref="ImGuiTableFlags.Resizable"/>, <see cref="ImGuiTableFlags.Reorderable"/>, or
    /// <see cref="ImGuiTableFlags.Hideable"/>; the popup identifier is seeded with the table identifier so
    /// two tables cannot collide.
    /// </remarks>
    internal static void TableOpenContextMenu(int columnN = -1)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTable? table = g.CurrentTable;
        ImAssert.Check(table != null);
        if (table == null)
            return;
        if (columnN == -1 && table.CurrentColumn != -1) // When called within a column, use that one.
            columnN = table.CurrentColumn;
        if (columnN == table.ColumnsCount)               // To facilitate using with TableGetHoveredColumn().
            columnN = -1;
        ImAssert.Check(columnN >= -1 && columnN < table.ColumnsCount);
        if ((table.Flags & (ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable)) != 0)
        {
            table.IsContextPopupOpen = true;
            table.ContextPopupColumn = (short)columnN;
            table.InstanceInteracted = table.InstanceCurrent;
            uint contextMenuId = ImHashStr("##ContextMenu", 0, table.ID);
            OpenPopupEx(contextMenuId, ImGuiPopupFlags.None);
        }
    }

    /// <summary>
    /// Begins the table's context-menu popup when it was opened for the currently active table instance.
    /// Canvases canonical <c>ImGui::TableBeginContextMenuPopup</c>
    ///.
    /// </summary>
    /// <param name="table">The table whose context-menu popup is begun.</param>
    /// <returns><see langword="true"/> when the popup became the current menu and the caller must call
    /// <c>EndPopup</c>; <see langword="false"/> otherwise.</returns>
    /// <remarks>
    /// The popup is anchored to the interacted instance so a menu opened in one table instance does not
    /// appear in another. It is auto-resized and unsaved; the in-memory settings owner (Chunk 13) is
    /// therefore not involved. When <c>BeginPopupEx</c> fails the open request is cleared
    /// </remarks>
    internal static bool TableBeginContextMenuPopup(ImGuiTable table)
    {
        if (!table.IsContextPopupOpen || table.InstanceCurrent != table.InstanceInteracted)
            return false;
        uint contextMenuId = ImHashStr("##ContextMenu", 0, table.ID);
        if (BeginPopupEx(contextMenuId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings))
            return true;
        table.IsContextPopupOpen = false;
        return false;
    }

    /// <summary>
    /// Submits one context-menu entry specialized for column visibility and reordering: it selects on
    /// release, spans the available width, stays highlighted while being dragged, stops toggling once it has
    /// been dragged, and draws a check mark for the selected state. Canvases canonical static
    /// <c>MenuItemForColumnReorder</c>.
    /// </summary>
    /// <param name="label">The entry label, which also supplies the identifier.</param>
    /// <param name="selected">Whether the column is currently shown.</param>
    /// <param name="enabled">False disables the entry and dims its check mark.</param>
    /// <returns><see langword="true"/> when the entry was activated and the column has not been dragged,
    /// in which case the caller flips the column's pending visibility.</returns>
    /// <remarks>
    /// This is a copy of <c>MenuItem</c> rather than a call to it so that the private
    /// <c>SelectOnRelease</c> behavior can be requested. <c>Disabled</c> is
    /// applied through <c>BeginDisabled</c>/<c>EndDisabled</c> so the check mark is dimmed as well, and it
    /// is not forwarded through the selectable flags. The canonical
    /// test-engine item registration. is not ported, because this port has no
    /// test-engine instrumentation layer.
    /// </remarks>
    private static bool MenuItemForColumnReorder(string label, bool selected, bool enabled)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        ImVec2 labelSize = CalcTextSize(label, true, -1.0f);
        ImGuiMenuColumns offsets = window.DC.MenuColumns;
        float checkmarkW = TruncateToInt(g.FontSize * 1.20f);
        float minW = offsets.DeclColumns(0.0f, labelSize.X, 0.0f, checkmarkW); // Feedback for the next frame.
        float stretchW = MathF.Max(0.0f, GetContentRegionAvail().X - minW);
        ImVec2 textPos = new ImVec2(window.DC.CursorPos.X, window.DC.CursorPos.Y + window.DC.CurrLineTextBaseOffset);

        uint id = GetID(label);
        ImGuiSelectableFlags selectableFlags = (ImGuiSelectableFlags)((int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SelectOnRelease
            | (int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SpanAvailWidth);
        if (g.ActiveId == id)
            selectableFlags |= ImGuiSelectableFlags.Highlight; // Stays highlighted while dragging.
        bool hasBeenMoved = g.ActiveId == id && g.ActiveIdHasBeenEditedBefore; // But disable toggling once moved.

        BeginDisabled(!enabled); // Not ImGuiSelectableFlags_Disabled, so the check mark is also affected.
        bool ret = Selectable(label, false, selectableFlags, new ImVec2(minW, labelSize.Y)) && !hasBeenMoved;
        if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Visible) != 0 && selected)
            RenderCheckMark(window.DrawList,
                textPos + new ImVec2(offsets.OffsetMark + stretchW + g.FontSize * 0.40f, g.FontSize * 0.134f * 0.5f),
                GetColorU32(ImGuiCol.Text), g.FontSize * 0.866f);
        EndDisabled();

        return ret;
    }

    /// <summary>
    /// Submits the default table context-menu body: a sizing section, a reset section, and a
    /// visibility section that also supports drag reordering. Canvases canonical
    /// <c>ImGui::TableDrawDefaultContextMenu</c>.,
    ///.
    /// </summary>
    /// <param name="table">The table whose context menu is drawn.</param>
    /// <param name="flagsForSectionToDisplay">The flag subset that selects which sections are emitted;
    /// normally <see cref="ImGuiTable.Flags"/>, so a custom context menu can call this helper with a
    /// reduced flag set.</param>
    /// <remarks>
    /// <para>
    /// The sizing section resets one column or all columns through the width setters owned by the
    /// columns/widths part. The reset section only raises the
    /// display-order and visibility reset requests that layout consumes next frame
    /// </para>
    /// <para>
    /// Canonical contains a sorting section guarded by <c>#if 0</c> and therefore compiled out; it is
    /// intentionally not ported, so the default menu never calls
    /// <see cref="TableSetColumnSortDirection"/>.
    /// </para>
    /// <para>
    /// The visibility section disables hiding the last shown column, disables destinations outside the
    /// range allowed during a reorder drag, and estimates a destination order from the drag distance before
    /// queueing it. Auto-close of popups is suppressed for the whole
    /// section so dragging an entry does not close the menu.
    /// </para>
    /// </remarks>
    internal static void TableDrawDefaultContextMenu(ImGuiTable table, ImGuiTableFlags flagsForSectionToDisplay)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        bool wantSeparator = false;
        int contextColumnN = table.ContextPopupColumn >= 0 && table.ContextPopupColumn < table.ColumnsCount
            ? table.ContextPopupColumn
            : -1;
        ImGuiTableColumn? contextColumn = contextColumnN != -1 ? table.Columns[contextColumnN] : null;

        // Sizing
        if ((flagsForSectionToDisplay & ImGuiTableFlags.Resizable) != 0)
        {
            if (contextColumn != null)
            {
                bool canResize = (contextColumn.Flags & ImGuiTableColumnFlags.NoResize) == 0 && contextColumn.IsEnabled;
                if (MenuItem(LocalizeGetMsg(ImGuiLocKey.TableSizeOne), null, false, canResize)) // "###SizeOne"
                    TableSetColumnWidthAutoSingle(table, contextColumnN);
            }

            string sizeAllDesc;
            if (table.ColumnsEnabledFixedCount == table.ColumnsEnabledCount && (table.Flags & ImGuiTableFlags.SizingMask_) != ImGuiTableFlags.SizingFixedSame)
                sizeAllDesc = LocalizeGetMsg(ImGuiLocKey.TableSizeAllFit);        // "###SizeAll" All fixed.
            else
                sizeAllDesc = LocalizeGetMsg(ImGuiLocKey.TableSizeAllDefault);    // "###SizeAll" All stretch or mixed.
            if (MenuItem(sizeAllDesc))
                TableSetColumnWidthAutoAll(table);
            wantSeparator = true;
        }

        // Reset order/visibility and other requests.
        if ((flagsForSectionToDisplay & (ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable)) != 0)
            if (BeginMenu(LocalizeGetMsg(ImGuiLocKey.TableReset)))
            {
                // Canonical keeps a "Reset all" item commented out because requesting auto-fit and altering
                // the sort order in the same frame can exhibit a sizing glitch
                if ((flagsForSectionToDisplay & ImGuiTableFlags.Reorderable) != 0)
                    if (MenuItem(LocalizeGetMsg(ImGuiLocKey.TableResetOrder), null, false, !table.IsDefaultDisplayOrder)) // Cannot be hidden or it would break drag reordering.
                        table.IsResetDisplayOrderRequest = true;
                if ((flagsForSectionToDisplay & ImGuiTableFlags.Hideable) != 0)
                    if (MenuItem(LocalizeGetMsg(ImGuiLocKey.TableResetVisibility), null, false, !table.IsDefaultVisibility))
                        table.IsResetVisibilityRequest = true;
                EndMenu();
            }

        // Hiding / visibility
        if ((flagsForSectionToDisplay & ImGuiTableFlags.Hideable) != 0)
        {
            if (wantSeparator)
                Separator();
            wantSeparator = true;

            // While reordering, the allowed destination range is computed once here instead of once per
            // item, because the query itself performs a sweep scan and is assumed to yield a single range
            bool isReordering = g.ActiveId != 0 && g.ActiveIdWindow == g.CurrentWindow
                && table.ReorderColumn != -1 && g.ActiveIdHasBeenEditedBefore;
            int reorderSrcOrder = isReordering ? table.Columns[table.ReorderColumn].DisplayOrder : -1;
            int reorderMinOrder = isReordering ? TableGetMaxDisplayOrderAllowed(table, reorderSrcOrder, 0) : 0;
            int reorderMaxOrder = isReordering ? TableGetMaxDisplayOrderAllowed(table, reorderSrcOrder, table.ColumnsCount - 1) : table.ColumnsCount - 1;
            PushItemFlag(ImGuiItemFlags.AutoClosePopups, false);
            for (int orderN = 0; orderN < table.ColumnsCount; orderN++)
            {
                int columnN = table.DisplayOrderToIndex[orderN];
                ImGuiTableColumn column = table.Columns[columnN];
                if ((column.Flags & ImGuiTableColumnFlags.Disabled) != 0)
                    continue;

                string name = TableGetColumnName(table, columnN);
                if (string.IsNullOrEmpty(name))
                    name = "<Unknown>";

                // Make sure the last active column cannot be hidden, and that a reorder drag cannot target
                // an out-of-range destination.
                bool menuItemEnabled = (column.Flags & ImGuiTableColumnFlags.NoHide) == 0;
                if (column.IsUserEnabled && table.ColumnsEnabledCount <= 1)
                    menuItemEnabled = false;
                if (isReordering && (column.DisplayOrder < reorderMinOrder || column.DisplayOrder > reorderMaxOrder))
                    menuItemEnabled = false;
                if (MenuItemForColumnReorder(name, column.IsUserEnabled, menuItemEnabled))
                    column.IsUserEnabledNextFrame = !column.IsUserEnabled;

                // Drag to reorder. Columns marked NoHide cannot be reordered
                if (IsItemActive() && IsMouseDragging(ImGuiMouseButton.Left) && g.ActiveIdSource == ImGuiInputSource.Mouse
                    && (table.Flags & ImGuiTableFlags.Reorderable) != 0)
                {
                    g.ActiveIdHasBeenEditedBefore = true; // Disable the toggle and dim the allowed targets.
                    table.ReorderColumn = (short)columnN;
                    if (!IsItemHovered())
                    {
                        int reorderDir = g.IO.MousePos.Y < (g.LastItemData.Rect.Min.Y + g.LastItemData.Rect.Max.Y) * 0.5f ? -1 : +1;
                        float reorderAmount = (reorderDir < 0 ? g.LastItemData.Rect.Min.Y - g.IO.MousePos.Y : g.IO.MousePos.Y - g.LastItemData.Rect.Max.Y) / g.LastItemData.Rect.GetHeight();
                        int dstOrder = column.DisplayOrder + (int)MathF.Ceiling(reorderAmount) * reorderDir; // Validated and clamped by the queue.
                        TableQueueSetColumnDisplayOrder(table, columnN, dstOrder);
                    }
                }
            }
            PopItemFlag();
        }
    }

    #endregion

    #endregion

    #region [SECTION] Legacy Columns

    private const float COLUMNS_HIT_RECT_HALF_THICKNESS = 4.0f;

    /// <summary>
    /// Gets the count of columns in the current columns block, or 1 if no columns block is active.
    /// </summary>
    /// <returns>Active columns count.</returns>
    public static int GetColumnsCount()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        return window?.DC.CurrentColumns != null ? window.DC.CurrentColumns.Count : 1;
    }

    /// <summary>
    /// Converts a normalized column offset to absolute pixels within the columns container.
    /// </summary>
    /// <param name="columns">Columns container.</param>
    /// <param name="offset_norm">Normalized offset in [0, 1].</param>
    /// <returns>Pixel offset from column minimum.</returns>
    public static float GetColumnOffsetFromNorm(ImGuiOldColumns columns, float offset_norm)
    {
        return offset_norm * (columns.OffMaxX - columns.OffMinX);
    }

    /// <summary>
    /// Converts an absolute pixel offset to normalized coordinate within the columns container.
    /// </summary>
    /// <param name="columns">Columns container.</param>
    /// <param name="offset">Pixel offset from column minimum.</param>
    /// <returns>Normalized offset in [0, 1].</returns>
    public static float GetColumnNormFromOffset(ImGuiOldColumns columns, float offset)
    {
        return offset / (columns.OffMaxX - columns.OffMinX);
    }

    /// <summary>
    /// Computes the layout offset of an old-style column currently undergoing resize interaction.
    /// </summary>
    /// <param name="columns">The old column set being queried.</param>
    /// <param name="columnIndex">The zero-based index of the column.</param>
    /// <returns>The offset, in pixels, of the column boundary.</returns>
    private static float GetDraggedColumnOffset(ImGuiOldColumns columns, int columnIndex)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        ImAssert.Check(columnIndex > 0);
        ImAssert.Check(g.ActiveId == columns.ID + (uint)columnIndex);

        float x = g.IO.MousePos.X - g.ActiveIdClickOffset.X + MathF.Truncate(COLUMNS_HIT_RECT_HALF_THICKNESS * g.CurrentDpiScale) - window.Pos.X;
        x = MathF.Max(x, GetColumnOffset(columnIndex - 1) + g.Style.ColumnsMinSpacing);
        if ((columns.Flags & ImGuiOldColumnFlags.NoPreserveWidths) != 0)
            x = MathF.Min(x, GetColumnOffset(columnIndex + 1) - g.Style.ColumnsMinSpacing);

        return x;
    }

    /// <summary>
    /// Gets the horizontal offset in pixels of a column from the window position.
    /// </summary>
    /// <param name="column_index">Target column index or -1 for current column.</param>
    /// <returns>Column pixel offset.</returns>
    public static float GetColumnOffset(int column_index = -1)
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        ImGuiOldColumns? columns = window?.DC.CurrentColumns;
        if (columns == null)
            return 0.0f;

        if (column_index < 0)
            column_index = columns.Current;
        ImAssert.Check(column_index < columns.Columns.Count);

        float t = columns.Columns[column_index].OffsetNorm;
        float xOffset = columns.OffMinX + (columns.OffMaxX - columns.OffMinX) * t;
        return xOffset;
    }

    /// <summary>
    /// Calculates the width of an old-style column, optionally evaluating state prior to resize dragging.
    /// </summary>
    /// <param name="columns">The old column set being queried.</param>
    /// <param name="columnIndex">The zero-based index of the column.</param>
    /// <param name="beforeResize"><see langword="true"/> to query width prior to active resizing; otherwise, <see langword="false"/>.</param>
    /// <returns>The width, in pixels, of the column.</returns>
    private static float GetColumnWidthEx(ImGuiOldColumns columns, int columnIndex, bool beforeResize = false)
    {
        if (columnIndex < 0)
            columnIndex = columns.Current;

        float offsetNorm;
        if (beforeResize)
            offsetNorm = columns.Columns[columnIndex + 1].OffsetNormBeforeResize - columns.Columns[columnIndex].OffsetNormBeforeResize;
        else
            offsetNorm = columns.Columns[columnIndex + 1].OffsetNorm - columns.Columns[columnIndex].OffsetNorm;
        return GetColumnOffsetFromNorm(columns, offsetNorm);
    }

    /// <summary>
    /// Gets the width of a column in pixels.
    /// </summary>
    /// <param name="column_index">Target column index or -1 for current column.</param>
    /// <returns>Column width in pixels.</returns>
    public static float GetColumnWidth(int column_index = -1)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiOldColumns? columns = window.DC.CurrentColumns;
        if (columns == null)
            return GetContentRegionAvail().X;

        if (column_index < 0)
            column_index = columns.Current;
        return GetColumnOffsetFromNorm(columns, columns.Columns[column_index + 1].OffsetNorm - columns.Columns[column_index].OffsetNorm);
    }

    /// <summary>
    /// Sets the width of a column in pixels.
    /// </summary>
    /// <param name="column_index">Target column index or -1 for current column.</param>
    /// <param name="width">Target column width in pixels.</param>
    public static void SetColumnWidth(int column_index, float width)
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        ImGuiOldColumns? columns = window?.DC.CurrentColumns;
        AssertUserError(columns != null, "SetColumnWidth() called outside of columns block!");
        if (columns == null)
            return;

        if (column_index < 0)
            column_index = columns.Current;
        SetColumnOffset(column_index + 1, GetColumnOffset(column_index) + width);
    }

    /// <summary>
    /// Sets the pixel offset of a column from the window position.
    /// </summary>
    /// <param name="column_index">Target column index.</param>
    /// <param name="offset_x">Pixel offset.</param>
    public static void SetColumnOffset(int column_index, float offset_x)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiOldColumns? columns = window.DC.CurrentColumns;
        AssertUserError(columns != null, "SetColumnOffset() called outside of columns block!");
        if (columns == null)
            return;

        if (column_index < 0)
            column_index = columns.Current;
        ImAssert.Check(column_index < columns.Columns.Count);

        bool preserveWidth = (columns.Flags & ImGuiOldColumnFlags.NoPreserveWidths) == 0 && (column_index < columns.Count - 1);
        float width = preserveWidth ? GetColumnWidthEx(columns, column_index, columns.IsBeingResized) : 0.0f;

        if ((columns.Flags & ImGuiOldColumnFlags.NoForceWithinWindow) == 0)
            offset_x = MathF.Min(offset_x, columns.OffMaxX - g.Style.ColumnsMinSpacing * (columns.Count - column_index));
        columns.Columns[column_index].OffsetNorm = GetColumnNormFromOffset(columns, offset_x - columns.OffMinX);

        if (preserveWidth)
            SetColumnOffset(column_index + 1, offset_x + MathF.Max(g.Style.ColumnsMinSpacing, width));
    }

    /// <summary>
    /// Gets the current column index (0 to Count - 1).
    /// </summary>
    /// <returns>Current column index.</returns>
    public static int GetColumnIndex()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        return window?.DC.CurrentColumns?.Current ?? 0;
    }

    /// <summary>
    /// Pushes the clipping rectangle for a column.
    /// </summary>
    /// <param name="column_index">Column index or -1 for current column.</param>
    public static void PushColumnClipRect(int column_index = -1)
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        ImGuiOldColumns? columns = window?.DC.CurrentColumns;
        if (columns == null)
            return;
        if (column_index < 0)
            column_index = columns.Current;

        ImGuiOldColumnData column = columns.Columns[column_index];
        PushClipRect(column.ClipRect.Min, column.ClipRect.Max, false);
    }

    /// <summary>
    /// Switches draw channel to the background channel of the current columns block.
    /// </summary>
    public static void PushColumnsBackground()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        ImGuiOldColumns? columns = window?.DC.CurrentColumns;
        if (window == null || columns == null || columns.Count == 1)
            return;

        columns.HostBackupClipRect = window.ClipRect;
        SetWindowClipRectBeforeSetChannel(window, columns.HostInitialClipRect);
        columns.Splitter.SetCurrentChannel(window.DrawList, 0);
    }

    /// <summary>
    /// Restores draw channel from the background channel to the active column channel.
    /// </summary>
    public static void PopColumnsBackground()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        ImGuiOldColumns? columns = window?.DC.CurrentColumns;
        if (window == null || columns == null || columns.Count == 1)
            return;

        SetWindowClipRectBeforeSetChannel(window, columns.HostBackupClipRect);
        columns.Splitter.SetCurrentChannel(window.DrawList, columns.Current + 1);
    }

    /// <summary>
    /// Finds or creates a legacy columns container in window storage.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="id">Columns container ID.</param>
    /// <returns>Columns container.</returns>
    public static ImGuiOldColumns FindOrCreateColumns(ImGuiWindow window, uint id)
    {
        for (int n = 0; n < window.ColumnsStorage.Count; n++)
            if (window.ColumnsStorage[n].ID == id)
                return window.ColumnsStorage[n];

        ImGuiOldColumns columns = new() { ID = id };
        window.ColumnsStorage.Add(columns);
        return columns;
    }

    /// <summary>
    /// Generates a unique identifier for a columns container.
    /// </summary>
    /// <param name="str_id">Optional identifier string.</param>
    /// <param name="count">Column count.</param>
    /// <returns>Unique 32-bit identifier.</returns>
    public static uint GetColumnsID(string? str_id, int count)
    {
        ImGuiWindow window = GetCurrentWindow();
        PushID(0x11223347 + (str_id != null ? 0 : count));
        uint id = window.GetID(str_id ?? "columns");
        PopID();
        return id;
    }

    /// <summary>
    /// Begins a legacy columns layout region.
    /// </summary>
    /// <param name="str_id">Optional columns identifier string.</param>
    /// <param name="count">Number of columns.</param>
    /// <param name="flags">Columns behavior flags.</param>
    public static void BeginColumns(string? str_id, int count, ImGuiOldColumnFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = GetCurrentWindow();

        ImAssert.Check(count >= 1);
        ImAssert.Check(window.DC.CurrentColumns == null); // Nested columns are currently not supported

        uint id = GetColumnsID(str_id, count);
        ImGuiOldColumns columns = FindOrCreateColumns(window, id);
        ImAssert.Check(columns.ID == id);
        columns.Current = 0;
        columns.Count = count;
        columns.Flags = flags;
        window.DC.CurrentColumns = columns;
        window.DC.NavIsScrollPushableX = false;

        columns.HostCursorPosY = window.DC.CursorPos.Y;
        columns.HostCursorMaxPosX = window.DC.CursorMaxPos.X;
        columns.HostInitialClipRect = window.ClipRect;
        columns.HostBackupParentWorkRect = window.ParentWorkRect;
        window.ParentWorkRect = window.WorkRect;

        float columnPadding = g.Style.ItemSpacing.X;
        float halfClipExtendX = MathF.Truncate(MathF.Max(window.WindowPadding.X * 0.5f, window.WindowBorderSize));
        float max1 = window.WorkRect.Max.X + columnPadding - MathF.Max(columnPadding - window.WindowPadding.X, 0.0f);
        float max2 = window.WorkRect.Max.X + halfClipExtendX;
        columns.OffMinX = window.DC.Indent.X - columnPadding + MathF.Max(columnPadding - window.WindowPadding.X, 0.0f);
        columns.OffMaxX = MathF.Max(MathF.Min(max1, max2) - window.Pos.X, columns.OffMinX + 1.0f);
        columns.LineMinY = columns.LineMaxY = window.DC.CursorPos.Y;

        if (columns.Columns.Count != 0 && columns.Columns.Count != count + 1)
            columns.Columns.Clear();

        columns.IsFirstFrame = (columns.Columns.Count == 0);
        if (columns.Columns.Count == 0)
        {
            for (int n = 0; n < count + 1; n++)
            {
                ImGuiOldColumnData col = new()
                {
                    OffsetNorm = n / (float)count
                };
                columns.Columns.Add(col);
            }
        }

        for (int n = 0; n < count; n++)
        {
            ImGuiOldColumnData col = columns.Columns[n];
            float clipX1 = (float)(int)(window.Pos.X + GetColumnOffset(n) + 0.5f);
            float clipX2 = (float)(int)(window.Pos.X + GetColumnOffset(n + 1) - 1.0f + 0.5f);
            col.ClipRect = new ImRect(clipX1, -float.MaxValue, clipX2, float.MaxValue);
            col.ClipRect.ClipWithFull(window.ClipRect);
        }

        if (columns.Count > 1)
        {
            columns.Splitter.Split(window.DrawList, 1 + columns.Count);
            columns.Splitter.SetCurrentChannel(window.DrawList, 1);
            PushColumnClipRect(0);
        }

        float offset0 = GetColumnOffset(columns.Current);
        float offset1 = GetColumnOffset(columns.Current + 1);
        float colWidth = offset1 - offset0;
        PushItemWidth(colWidth * 0.65f);
        window.DC.ColumnsOffset = new ImVec1(MathF.Max(columnPadding - window.WindowPadding.X, 0.0f));
        window.DC.CursorPos = new ImVec2(MathF.Truncate(window.Pos.X + window.DC.Indent.X + window.DC.ColumnsOffset.X), window.DC.CursorPos.Y);
        window.WorkRect = new ImRect(window.WorkRect.Min, new ImVec2(window.Pos.X + offset1 - columnPadding, window.ContentRegionRect.Max.Y));
    }

    /// <summary>
    /// Advances cursor to the next column in the active columns layout container.
    /// </summary>
    public static void NextColumn()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems || window.DC.CurrentColumns == null)
            return;

        ImGuiContext g = GImGui!;
        ImGuiOldColumns columns = window.DC.CurrentColumns;

        if (columns.Count == 1)
        {
            window.DC.CursorPos = new ImVec2(MathF.Truncate(window.Pos.X + window.DC.Indent.X + window.DC.ColumnsOffset.X), window.DC.CursorPos.Y);
            ImAssert.Check(columns.Current == 0);
            return;
        }

        if (++columns.Current == columns.Count)
            columns.Current = 0;

        PopItemWidth();

        ImGuiOldColumnData col = columns.Columns[columns.Current];
        SetWindowClipRectBeforeSetChannel(window, col.ClipRect);
        columns.Splitter.SetCurrentChannel(window.DrawList, columns.Current + 1);

        float columnPadding = g.Style.ItemSpacing.X;
        columns.LineMaxY = MathF.Max(columns.LineMaxY, window.DC.CursorPos.Y);
        if (columns.Current > 0)
        {
            window.DC.ColumnsOffset = new ImVec1(GetColumnOffset(columns.Current) - window.DC.Indent.X + columnPadding);
        }
        else
        {
            window.DC.ColumnsOffset = new ImVec1(MathF.Max(columnPadding - window.WindowPadding.X, 0.0f));
            window.DC.IsSameLine = false;
            columns.LineMinY = columns.LineMaxY;
        }
        window.DC.CursorPos = new ImVec2(MathF.Truncate(window.Pos.X + window.DC.Indent.X + window.DC.ColumnsOffset.X), columns.LineMinY);
        window.DC.CurrLineSize = ImVec2.Zero;
        window.DC.CurrLineTextBaseOffset = 0.0f;

        float offset0 = GetColumnOffset(columns.Current);
        float offset1 = GetColumnOffset(columns.Current + 1);
        float colWidth = offset1 - offset0;
        PushItemWidth(colWidth * 0.65f);
        window.WorkRect = new ImRect(window.WorkRect.Min, new ImVec2(window.Pos.X + offset1 - columnPadding, window.WorkRect.Max.Y));
    }

    /// <summary>
    /// Ends the active legacy columns layout container, restores clipping and renders borders.
    /// </summary>
    public static void EndColumns()
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = GetCurrentWindow();
        ImGuiOldColumns? columns = window.DC.CurrentColumns;
        AssertUserError(columns != null, "Mismatched BeginColumns()/EndColumns()!");
        if (columns == null)
            return;

        PopItemWidth();
        if (columns.Count > 1)
        {
            PopClipRect();
            columns.Splitter.Merge(window.DrawList);
        }

        ImGuiOldColumnFlags flags = columns.Flags;
        columns.LineMaxY = MathF.Max(columns.LineMaxY, window.DC.CursorPos.Y);
        window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, columns.LineMaxY);
        if ((flags & ImGuiOldColumnFlags.GrowParentContentsSize) == 0)
            window.DC.CursorMaxPos = new ImVec2(columns.HostCursorMaxPosX, window.DC.CursorMaxPos.Y);

        bool isBeingResized = false;
        if ((flags & ImGuiOldColumnFlags.NoBorder) == 0 && !window.SkipItems)
        {
            float y1 = MathF.Max(columns.HostCursorPosY, window.ClipRect.Min.Y);
            float y2 = MathF.Min(window.DC.CursorPos.Y, window.ClipRect.Max.Y);
            int draggingColumn = -1;
            for (int n = 1; n < columns.Count; n++)
            {
                ImGuiOldColumnData col = columns.Columns[n];
                float x = window.Pos.X + GetColumnOffset(n);
                uint columnId = columns.ID + (uint)n;
                float columnHitHw = MathF.Truncate(COLUMNS_HIT_RECT_HALF_THICKNESS * g.CurrentDpiScale);
                ImRect columnHitRect = new(new ImVec2(x - columnHitHw, y1), new ImVec2(x + columnHitHw, y2));
                if (!ItemAdd(columnHitRect, columnId, null, ImGuiItemFlags.NoNav))
                    continue;

                bool hovered = false, held = false;
                if ((flags & ImGuiOldColumnFlags.NoResize) == 0)
                {
                    ButtonBehavior(columnHitRect, columnId, out hovered, out held);
                    if (hovered || held)
                        SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                    if (held && (col.Flags & ImGuiOldColumnFlags.NoResize) == 0)
                        draggingColumn = n;
                }

                uint color = GetColorU32(held ? ImGuiCol.SeparatorActive : hovered ? ImGuiCol.SeparatorHovered : ImGuiCol.Separator);
                float xi = MathF.Truncate(x);
                window.DrawList.AddLine(new ImVec2(xi, y1 + 1.0f), new ImVec2(xi, y2), color);
            }

            if (draggingColumn != -1)
            {
                if (!columns.IsBeingResized)
                {
                    for (int n = 0; n < columns.Count + 1; n++)
                        columns.Columns[n].OffsetNormBeforeResize = columns.Columns[n].OffsetNorm;
                }
                columns.IsBeingResized = isBeingResized = true;
                float x = GetDraggedColumnOffset(columns, draggingColumn);
                SetColumnOffset(draggingColumn, x);
            }
        }
        columns.IsBeingResized = isBeingResized;

        window.WorkRect = window.ParentWorkRect;
        window.ParentWorkRect = columns.HostBackupParentWorkRect;
        window.DC.CurrentColumns = null;
        window.DC.ColumnsOffset = new ImVec1(0.0f);
        window.DC.CursorPos = new ImVec2(MathF.Truncate(window.Pos.X + window.DC.Indent.X + window.DC.ColumnsOffset.X), window.DC.CursorPos.Y);
        NavUpdateCurrentWindowIsScrollPushableX();
    }

    /// <summary>
    /// Submits or configures a legacy columns layout container.
    /// </summary>
    /// <param name="count">Number of columns.</param>
    /// <param name="id">Optional identifier string.</param>
    /// <param name="borders">Whether to render separating borders.</param>
    public static void Columns(int count = 1, string? id = null, bool borders = true)
    {
        ImGuiWindow window = GetCurrentWindow();
        ImAssert.Check(count >= 1);

        ImGuiOldColumnFlags flags = borders ? ImGuiOldColumnFlags.None : ImGuiOldColumnFlags.NoBorder;
        ImGuiOldColumns? columns = window.DC.CurrentColumns;
        if (columns != null && columns.Count == count && columns.Flags == flags)
            return;

        if (columns != null)
            EndColumns();

        if (count != 1)
            BeginColumns(id, count, flags);
    }

    #endregion

    #region SECTION: TABLE DEBUG INSPECTORS

    /// <summary>
    /// Names a table's sizing policy for the debug inspector.
    /// </summary>
    /// <param name="sizingPolicy">The table flags whose sizing bits are inspected.</param>
    /// <returns>The canonical policy name, or <c>"N/A"</c> when no single policy is set.</returns>
    private static string DebugNodeTableGetSizingPolicyDesc(ImGuiTableFlags sizingPolicy)
    {
        ImGuiTableFlags policy = sizingPolicy & ImGuiTableFlags.SizingMask_;
        if (policy == ImGuiTableFlags.SizingFixedFit)
            return "FixedFit";
        if (policy == ImGuiTableFlags.SizingFixedSame)
            return "FixedSame";
        if (policy == ImGuiTableFlags.SizingStretchProp)
            return "StretchProp";
        if (policy == ImGuiTableFlags.SizingStretchSame)
            return "StretchSame";
        return "N/A";
    }

    /// <summary>
    /// Reports one table: its geometry, sizing, padding, hovered and dragged columns, per-instance data, and
    /// one detailed row per column, followed by its bound settings record when one exists.
    /// (, declared )
    /// </summary>
    /// <param name="table">The table to report.</param>
    /// <remarks>
    /// <para>
    /// A table that has not been submitted for two frames is shown through the disabled style and marked
    /// inactive; canonical notes that a table which early-outs because it is fully clipped will also appear
    /// inactive here, so the marking is a hint rather than a defect report.
    /// </para>
    /// <para>
    /// The per-column stretch percentages are normalized against the sum of every stretching column's weight,
    /// which is what makes the reported distribution comparable across tables.
    /// </para>
    /// <para>
    /// The "Clear settings" button only queues the reset request, matching canonical: the reset is applied by
    /// the table layout at the start of the next frame rather than in the middle of this one.
    /// </para>
    /// </remarks>
    internal static void DebugNodeTable(ImGuiTable table)
    {
        ImGuiContext g = GetContext();
        bool isActive = table.LastFrameActive >= g.FrameCount - 2;
        if (!isActive)
            PushStyleColor(ImGuiCol.Text, GetStyleColorVec4(ImGuiCol.TextDisabled));
        bool open = TreeNode(
            table,
            "Table 0x{0:X8} ({1} columns, in '{2}'){3}",
            table.ID,
            table.ColumnsCount,
            table.OuterWindow != null ? table.OuterWindow.Name : "NULL",
            isActive ? string.Empty : " *Inactive*");
        if (!isActive)
            PopStyleColor();
        if (IsItemHovered() && table.OuterWindow != null)
            GetForegroundDrawList(table.OuterWindow).AddRect(table.OuterRect.Min, table.OuterRect.Max, 0xFF00FFFF);
        if (IsItemVisible() && table.HoveredColumnBody != -1 && table.OuterWindow != null)
            GetForegroundDrawList(table.OuterWindow).AddRect(GetItemRectMin(), GetItemRectMax(), 0xFF00FFFF);
        if (!open)
            return;

        if (table.InstanceCurrent > 0)
            Text("** {0} instances of same table! Some data below will refer to last instance.", table.InstanceCurrent + 1);
        if (g.IO.ConfigDebugIsDebuggerPresent)
        {
            if (DebugBreakButton("**DebugBreak**", "in BeginTable()"))
                g.DebugBreakInTable = table.ID;
            SameLine();
        }

        bool clearSettings = SmallButton("Clear settings");
        BulletText(
            "OuterRect: Pos: ({0:F1},{1:F1}) Size: ({2:F1},{3:F1}) Sizing: '{4}'",
            table.OuterRect.Min.X,
            table.OuterRect.Min.Y,
            table.OuterRect.GetWidth(),
            table.OuterRect.GetHeight(),
            DebugNodeTableGetSizingPolicyDesc(table.Flags));
        BulletText(
            "ColumnsGivenWidth: {0:F1}, ColumnsAutoFitWidth: {1:F1}, InnerWidth: {2:F1}{3}",
            table.ColumnsGivenWidth,
            table.ColumnsAutoFitWidth,
            table.InnerWidth,
            table.InnerWidth == 0.0f ? " (auto)" : string.Empty);
        BulletText(
            "CellPaddingX: {0:F1}, CellSpacingX: {1:F1}/{2:F1}, OuterPaddingX: {3:F1}",
            table.CellPaddingX,
            table.CellSpacingX1,
            table.CellSpacingX2,
            table.OuterPaddingX);
        BulletText("HoveredColumnBody: {0}, HoveredColumnBorder: {1}", table.HoveredColumnBody, table.HoveredColumnBorder);
        BulletText(
            "ResizedColumn: {0}, HeldHeaderColumn: {1}, ReorderColumn: {2}",
            table.LastResizedColumn,
            table.LastHeldHeaderColumn,
            table.ReorderColumn);
        for (int n = 0; n < table.InstanceCurrent + 1; n++)
        {
            ImGuiTableInstanceData tableInstance = TableGetInstanceData(table, n);
            BulletText("Instance {0}: HoveredRow: {1}, LastOuterHeight: {2:F2}", n, tableInstance.HoveredRowLast, tableInstance.LastOuterHeight);
        }

        float sumWeights = 0.0f;
        for (int n = 0; n < table.ColumnsCount; n++)
        {
            if ((table.Columns[n].Flags & ImGuiTableColumnFlags.WidthStretch) != 0)
                sumWeights += table.Columns[n].StretchWeight;
        }

        for (int n = 0; n < table.ColumnsCount; n++)
        {
            ImGuiTableColumn column = table.Columns[n];
            string name = TableGetColumnName(table, n);
            string buf = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Column {0} order {1} '{2}': offset {3:+0.00;-0.00;0.00} to {4:+0.00;-0.00;0.00}{5}\n"
                + "Enabled: {6}, VisibleX/Y: {7}/{8}, RequestOutput: {9}, SkipItems: {10}, DrawChannels: {11},{12}\n"
                + "WidthGiven: {13:F1}, Request/Auto: {14:F1}/{15:F1}, StretchWeight: {16:F3} ({17:F1}%)\n"
                + "MinX: {18:F1}, MaxX: {19:F1} ({20:+0.0;-0.0;0.0}), ClipRect: {21:F1} to {22:F1} (+{23:F1})\n"
                + "ContentWidth: {24:F1},{25:F1}, HeadersUsed/Ideal {26:F1}/{27:F1}\n"
                + "Sort: {28}{29}, UserData: 0x{30:X8}, Flags: 0x{31:X4}: {32}{33}{34}..",
                n,
                column.DisplayOrder,
                name,
                column.MinX - table.WorkRect.Min.X,
                column.MaxX - table.WorkRect.Min.X,
                n < table.FreezeColumnsRequest ? " (Frozen)" : string.Empty,
                column.IsEnabled ? 1 : 0,
                column.IsVisibleX ? 1 : 0,
                column.IsVisibleY ? 1 : 0,
                column.IsRequestOutput ? 1 : 0,
                column.IsSkipItems ? 1 : 0,
                column.DrawChannelFrozen,
                column.DrawChannelUnfrozen,
                column.WidthGiven,
                column.WidthRequest,
                column.WidthAuto,
                column.StretchWeight,
                column.StretchWeight > 0.0f ? column.StretchWeight / sumWeights * 100.0f : 0.0f,
                column.MinX,
                column.MaxX,
                column.MaxX - column.MinX,
                column.ClipRect.Min.X,
                column.ClipRect.Max.X,
                column.ClipRect.Max.X - column.ClipRect.Min.X,
                column.ContentMaxXFrozen - column.WorkMinX,
                column.ContentMaxXUnfrozen - column.WorkMinX,
                column.ContentMaxXHeadersUsed - column.WorkMinX,
                column.ContentMaxXHeadersIdeal - column.WorkMinX,
                column.SortOrder,
                column.SortDirection == ImGuiSortDirection.Ascending ? " (Asc)" : column.SortDirection == ImGuiSortDirection.Descending ? " (Des)" : string.Empty,
                column.UserData,
                (int)column.Flags,
                (column.Flags & ImGuiTableColumnFlags.WidthStretch) != 0 ? "WidthStretch " : string.Empty,
                (column.Flags & ImGuiTableColumnFlags.WidthFixed) != 0 ? "WidthFixed " : string.Empty,
                (column.Flags & ImGuiTableColumnFlags.NoResize) != 0 ? "NoResize " : string.Empty);
            Bullet();
            Selectable(buf);
            if (IsItemHovered() && table.OuterWindow != null)
            {
                // Outline the whole column strip so the reported data can be tied to a visible region.
                ImRect r = new(column.MinX, table.OuterRect.Min.Y, column.MaxX, table.OuterRect.Max.Y);
                GetForegroundDrawList(table.OuterWindow).AddRect(r.Min, r.Max, 0xFF00FFFF);
            }
        }

        ImGuiTableSettings? settings = TableGetBoundSettings(table);
        if (settings != null)
            DebugNodeTableSettings(settings, table);
        if (clearSettings)
            table.IsResetAllRequest = true; // Queue a call to TableResetSettings().
        TreePop();
    }

    /// <summary>
    /// Reports one persisted table settings record, optionally tied back to a live table.
    /// (, declared )
    /// </summary>
    /// <param name="settings">The settings record to report.</param>
    /// <param name="table">The live table the record belongs to, or <see langword="null"/> to resolve it by identifier.</param>
    /// <remarks>
    /// <para>
    /// A record with no identifier is an unbound or transient record; canonical scopes it with the record's
    /// own identity rather than an identifier. The managed port scopes it with the record object and uses a
    /// stable node identifier, because a record without an identifier has no value to key on.
    /// </para>
    /// <para>
    /// Hovering the node outlines the live table's outer rectangle when one can be resolved, which is how a
    /// maintainer confirms that a persisted record still corresponds to a table in the frame.
    /// </para>
    /// </remarks>
    internal static void DebugNodeTableSettings(ImGuiTableSettings settings, ImGuiTable? table)
    {
        if (settings.ID == 0)
            PushID(settings);
        bool open = TreeNode(
            $"##TableSettings{settings.ID:X8}",
            "Settings 0x{0:X8} ({1} columns)",
            settings.ID,
            settings.ColumnsCount);
        bool hovered = IsItemHovered();
        if (hovered && table == null && settings.ID != 0)
            table = TableFindByID(settings.ID);
        if (hovered && table?.OuterWindow != null)
            GetForegroundDrawList(table.OuterWindow).AddRect(table.OuterRect.Min, table.OuterRect.Max, 0xFF00FFFF);

        if (open)
        {
            BulletText("SaveFlags: 0x{0:X8}", (int)settings.SaveFlags);
            BulletText("ColumnsCount: {0} (max {1})", settings.ColumnsCount, settings.ColumnsCountMax);
            for (int n = 0; n < settings.ColumnsCount; n++)
            {
                ImGuiTableColumnSettings columnSettings = settings.ColumnSettings[n];
                ImGuiSortDirection sortDir = columnSettings.SortOrder != -1
                    ? columnSettings.SortDirection
                    : ImGuiSortDirection.None;
                BulletText(
                    "Column {0} Order {1} SortOrder {2,2} {3} Vis {4} {5} {6,7:F3} ID 0x{7:X8}",
                    n,
                    columnSettings.DisplayOrder,
                    columnSettings.SortOrder,
                    sortDir == ImGuiSortDirection.Ascending ? "Asc" : sortDir == ImGuiSortDirection.Descending ? "Des" : "---",
                    columnSettings.IsEnabled,
                    columnSettings.IsStretch ? "Weight" : "Width ",
                    columnSettings.WidthOrWeight,
                    columnSettings.ID);
            }
            TreePop();
        }
        if (settings.ID == 0)
            PopID();
    }

    #endregion

}
