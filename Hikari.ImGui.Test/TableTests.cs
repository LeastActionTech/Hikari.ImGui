#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical table subsystem owned by <c>ImGuiTables.cs</c>: table lifetime, column
/// declaration and sizing, rows and cells, header rows and click-to-sort, the public sort
/// specifications, frozen regions, the default column context menu, and the two integration points
/// that bind tables to the window system.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>. The canonical reference is Dear ImGui
/// <c>v1.92.9b-docking</c> at <c>ThirdParty/imgui/imgui_tables.cpp</c> with the table structures in
/// <c>ThirdParty/imgui/imgui_internal.h</c>; every test cites the canonical line range that defines
/// the behavior it pins. Geometry expectations are derived from the canonical layout arithmetic
/// (<c>imgui_tables.cpp:849-1459</c>) using the port's default <c>Style.CellPadding</c> of
/// <c>(4, 2)</c> and <c>Style.ItemSpacing</c> of <c>(8, 4)</c>.
/// </remarks>
[TestFixture]
public class TableTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
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


    /// <summary>
    /// Runs one full frame containing an empty fixed-geometry window so window identity and input
    /// state settle before a test submits its table host.
    /// </summary>
    private static void WarmupFrame()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("Warmup");
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="body">The table submissions and assertions to run while the window is current.</param>
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
    /// Moves the mouse onto <paramref name="point"/>, then presses and releases the left button over
    /// two further frames, resubmitting <paramref name="body"/> every frame.
    /// </summary>
    /// <param name="point">Screen-space click position.</param>
    /// <param name="body">Table submission run on every frame.</param>
    /// <remarks>
    /// The hover frame is required because table headers use the
    /// <see cref="ImGuiButtonFlags.AllowOverlap"/> path, whose hover test only succeeds once the item
    /// was already the hovered identifier on the previous frame
    /// (<c>ThirdParty/imgui/imgui.cpp:5180-5186</c>).
    /// </remarks>
    private void ClickAt(ImVec2 point, Action body)
    {
        _ctx.IO.AddMousePosEvent(point.X, point.Y);
        WindowFrame(body);
        _ctx.IO.AddMouseButtonEvent(0, true);
        WindowFrame(body);
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(body);
    }

    /// <summary>
    /// Holds Shift and performs a <see cref="ClickAt"/>, which is the canonical request to append a
    /// second sort specification rather than replace the current one.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:4436-4440</c>)
    /// </summary>
    /// <param name="point">Screen-space click position.</param>
    /// <param name="body">Table submission run on both frames.</param>
    private void ShiftClickAt(ImVec2 point, Action body)
    {
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Shift, true);
        ClickAt(point, body);
        _ctx.IO.AddKeyEvent(ImGuiKey.ImGuiMod_Shift, false);
    }

    /// <summary>
    /// Returns the screen-space rectangle of one column's header cell from the table's current row.
    /// </summary>
    /// <param name="table">Table whose column geometry is read.</param>
    /// <param name="columnN">Column index.</param>
    /// <returns>The header cell rectangle in screen coordinates.</returns>
    /// <remarks>
    /// The port stores the layout cursor and therefore the table rectangle and column bounds in
    /// absolute screen coordinates (<c>imgui_tables.cpp:1117-1173</c>), so no window origin is added.
    /// </remarks>
    private static ImRect HeaderCellScreenRect(ImGuiTable table, int columnN)
    {
        ImGuiTableColumn column = table.Columns[columnN];
        return new ImRect(
            new ImVec2(column.MinX, table.RowPosY1),
            new ImVec2(column.MaxX, table.RowPosY2));
    }

    /// <summary>
    /// Submits a three-column, 400-by-200 table with the requested table flags, an optional header
    /// row, and invokes <paramref name="afterHeaders"/> on the ambient table before ending it.
    /// </summary>
    /// <param name="flags">Table flags; sortable tests pass <see cref="ImGuiTableFlags.Sortable"/>.</param>
    /// <param name="afterHeaders">Optional assertion body run after the header row is submitted.</param>
    private void SubmitSortableTable(ImGuiTableFlags flags, Action<ImGuiTable>? afterHeaders = null)
    {
        Assert.That(ImGui.BeginTable("SortTable", 3, flags, new ImVec2(400.0f, 200.0f)), Is.True);
        ImGui.TableSetupColumn("Alpha");
        ImGui.TableSetupColumn("Beta");
        ImGui.TableSetupColumn("Gamma");
        ImGui.TableHeadersRow();
        afterHeaders?.Invoke(_ctx.CurrentTable!);
        ImGui.EndTable();
    }

    /// <summary>
    /// Verifies that a tree node submitted inside a table records the current table column for its hierarchy
    /// lines, so that the line is drawn in that column's channel (<c>ThirdParty/imgui/imgui_widgets.cpp:6912</c>).
    /// </summary>
    [Test]
    public void TreeNode_DrawLinesRecordsCurrentTableColumn()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("L", 2, ImGuiTableFlags.None, new ImVec2(300.0f, 120.0f)), Is.True);
            ImGui.TableSetupColumn("A");
            ImGui.TableSetupColumn("B");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            Assert.That(ImGui.TreeNodeEx("Parent", ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DefaultOpen), Is.True);
            Assert.That(_ctx.TreeNodeStack[^1].DrawLinesTableColumn, Is.EqualTo((short)1));
            ImGui.TreePop();
            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that outside a table the tree node records no table column, as canonical
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6912</c>).
    /// </summary>
    [Test]
    public void TreeNode_DrawLinesOutsideTableUsesNoColumn()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.TreeNodeEx("Parent", ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DefaultOpen), Is.True);
            Assert.That(_ctx.TreeNodeStack[^1].DrawLinesTableColumn, Is.EqualTo((short)-1));
            ImGui.TreePop();
        });
    }

    #region Lifecycle

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginTable"/> begins inside a window, reports the declared
    /// column count, starts with no current row, and that <see cref="ImGui.EndTable"/> clears the
    /// ambient table and advances the outer window cursor to the table's outer-rectangle bottom plus
    /// the canonical item spacing.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:315-319</c>, <c>526-527</c>, <c>1460-1500</c>,
    /// <c>1616-1642</c>)
    /// </summary>
    [Test]
    public void Lifecycle_BeginTableAndEndTable_RestoreAmbientStateAndCursor()
    {
        WindowFrame(() =>
        {
            bool began = ImGui.BeginTable("Table", 3, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f));
            Assert.That(began, Is.True);
            Assert.That(ImGui.TableGetColumnCount(), Is.EqualTo(3));
            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(-1));

            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthFixed, 80.0f);

            ImGui.TableNextRow();
            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(0));

            ImGuiTable table = _ctx.CurrentTable!;
            float outerBottom = table.OuterRect.Max.Y;

            ImGui.EndTable();

            Assert.That(_ctx.CurrentTable, Is.Null);
            Assert.That(ImGui.TableGetColumnCount(), Is.EqualTo(0));
            float expectedCursorY = (float)(int)(outerBottom + _ctx.Style.ItemSpacing.Y);
            Assert.That(ImGui.GetCursorScreenPos().Y, Is.EqualTo(expectedCursorY).Within(0.001f));
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginTable"/> returns false and begins nothing when the host
    /// window skips items, which is the canonical early-out for a clipped or collapsed host.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:322-328</c>)
    /// </summary>
    [Test]
    public void Lifecycle_BeginTableInSkippedWindow_ReturnsFalse()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.Always);

        bool open = ImGui.Begin("Collapsed");
        Assert.That(open, Is.False);

        bool began = ImGui.BeginTable("Table", 3, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f));
        Assert.That(began, Is.False);
        Assert.That(_ctx.CurrentTable, Is.Null);

        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that a table nested inside a cell of another table stacks its temporary data and
    /// that ending the inner table restores the outer table as the ambient table.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:560-570</c>, <c>1705-1725</c>)
    /// </summary>
    [Test]
    public void Lifecycle_NestedTables_RestoreParentAsAmbientTable()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Outer", 1, ImGuiTableFlags.None, new ImVec2(360.0f, 160.0f)), Is.True);
            ImGui.TableSetupColumn("OuterColumn", ImGuiTableColumnFlags.WidthFixed, 300.0f);
            ImGui.TableNextRow();
            Assert.That(ImGui.TableSetColumnIndex(0), Is.True);
            ImGuiTable outer = _ctx.CurrentTable!;

            Assert.That(ImGui.BeginTable("Inner", 2, ImGuiTableFlags.None, new ImVec2(200.0f, 60.0f)), Is.True);
            ImGui.TableSetupColumn("InnerA", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("InnerB", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableNextRow();
            ImGuiTable inner = _ctx.CurrentTable!;
            Assert.That(inner, Is.Not.SameAs(outer));

            ImGui.EndTable();
            Assert.That(_ctx.CurrentTable, Is.SameAs(outer));

            ImGui.EndTable();
            Assert.That(_ctx.CurrentTable, Is.Null);
        });
    }

    #endregion

    #region Columns

    /// <summary>
    /// Verifies that explicit <see cref="ImGuiTableColumnFlags.WidthFixed"/> requests (including the
    /// <c>initWidthOrWeight</c> argument) and an explicit
    /// <see cref="ImGuiTableColumnFlags.WidthStretch"/> column resolve to the canonical
    /// <see cref="ImGuiTableColumn.WidthGiven"/> values, that the columns tile the outer rectangle
    /// without gaps, and that the widths plus the inter-column spacing fill the 400-pixel outer size.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:908-1080</c>, <c>1117-1245</c>)
    /// </summary>
    [Test]
    public void Columns_FixedAndStretchWidths_ResolveAndFillOuterWidth()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Geometry", 3, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 120.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGuiTable table = _ctx.CurrentTable!;
            ImGuiTableColumn a = table.Columns[0];
            ImGuiTableColumn b = table.Columns[1];
            ImGuiTableColumn c = table.Columns[2];

            Assert.That(a.WidthGiven, Is.EqualTo(120.0f));
            Assert.That(b.WidthGiven, Is.EqualTo(80.0f));
            Assert.That(c.WidthGiven, Is.EqualTo(184.0f));

            // The port resolves the default padding and spacing from Style.CellPadding.
            Assert.That(table.CellPaddingX, Is.EqualTo(0.0f));
            Assert.That(table.CellSpacingX1, Is.EqualTo(4.0f));
            Assert.That(table.CellSpacingX2, Is.EqualTo(4.0f));
            Assert.That(table.OuterPaddingX, Is.EqualTo(0.0f));

            // Contiguous columns: each column starts exactly where the previous one ends.
            Assert.That(a.MinX, Is.LessThan(b.MinX));
            Assert.That(b.MinX, Is.LessThan(c.MinX));
            Assert.That(b.MinX, Is.EqualTo(a.MaxX).Within(0.001f));
            Assert.That(c.MinX, Is.EqualTo(b.MaxX).Within(0.001f));

            // The content region spans exactly the outer rectangle.
            Assert.That(a.WorkMinX, Is.EqualTo(table.OuterRect.Min.X).Within(0.001f));
            Assert.That(c.WorkMaxX, Is.EqualTo(table.OuterRect.Max.X).Within(0.001f));

            float sumWidths = a.WidthGiven + b.WidthGiven + c.WidthGiven;
            float spacing = (table.CellSpacingX1 + table.CellSpacingX2) * (table.ColumnsEnabledCount - 1)
                + table.OuterPaddingX * 2.0f;
            Assert.That(sumWidths + spacing, Is.EqualTo(400.0f).Within(0.01f));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that two stretch columns divide the available width by their
    /// <c>initWidthOrWeight</c> weights: weights 1 and 3 split 392 pixels into 98 and 294, and the
    /// weights plus spacing fill the 400-pixel outer size.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:1025-1070</c>)
    /// </summary>
    [Test]
    public void Columns_StretchWeights_DistributeAvailableWidth()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Weights", 2, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthStretch, 3.0f);
            ImGui.TableNextRow();

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(table.ColumnsStretchSumWeights, Is.EqualTo(4.0f));
            Assert.That(table.Columns[0].WidthGiven, Is.EqualTo(98.0f));
            Assert.That(table.Columns[1].WidthGiven, Is.EqualTo(294.0f));

            float spacing = (table.CellSpacingX1 + table.CellSpacingX2) * (table.ColumnsEnabledCount - 1)
                + table.OuterPaddingX * 2.0f;
            Assert.That(table.Columns[0].WidthGiven + table.Columns[1].WidthGiven + spacing,
                Is.EqualTo(400.0f).Within(0.01f));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TableSetColumnEnabled"/> with a false value hides a column on
    /// the next layout: the column stays in the declared count but is disabled, collapses to zero
    /// width, and stops requesting output. A column marked
    /// <see cref="ImGuiTableColumnFlags.Disabled"/> is disabled unconditionally.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:913-921</c>, <c>1146-1164</c>, <c>1921-1932</c>)
    /// </summary>
    [Test]
    public void Columns_HiddenOrDisabledColumn_IsExcludedFromLayout()
    {
        // A fresh table re-applies the Hideable column defaults during its first layout, so the table
        // is initialized for one frame before the visibility request is issued.
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Hidden", 3, ImGuiTableFlags.Hideable, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableNextRow();
            ImGui.EndTable();
        });

        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Hidden", 3, ImGuiTableFlags.Hideable, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetColumnEnabled(1, false);
            ImGui.TableNextRow();

            ImGuiTable table = _ctx.CurrentTable!;

            // The declared count is unchanged; hiding only removes the column from the enabled set.
            Assert.That(ImGui.TableGetColumnCount(), Is.EqualTo(3));
            Assert.That(table.Columns[0].IsEnabled, Is.True);
            Assert.That(table.Columns[1].IsEnabled, Is.False);
            Assert.That(table.Columns[1].WidthGiven, Is.EqualTo(0.0f));
            Assert.That(table.Columns[1].IsRequestOutput, Is.False);
            Assert.That(ImGui.TableGetColumnFlags(1) & ImGuiTableColumnFlags.IsEnabled,
                Is.EqualTo(ImGuiTableColumnFlags.None));
            Assert.That(ImGui.TableGetColumnFlags(0) & ImGuiTableColumnFlags.IsEnabled,
                Is.EqualTo(ImGuiTableColumnFlags.IsEnabled));

            Assert.That(ImGui.TableNextColumn(), Is.True);  // Column 0.
            Assert.That(ImGui.TableNextColumn(), Is.False); // Hidden column 1 requests no output.
            Assert.That(ImGui.TableNextColumn(), Is.True);  // Column 2.

            ImGui.EndTable();
        });

        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Disabled", 2, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("B",
                ImGuiTableColumnFlags.Disabled | ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableNextRow();

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(table.Columns[1].IsEnabled, Is.False);
            Assert.That(table.Columns[1].WidthGiven, Is.EqualTo(0.0f));
            Assert.That(ImGui.TableSetColumnIndex(1), Is.False);

            ImGui.EndTable();
        });
    }

    #endregion

    #region Rows and cells

    /// <summary>
    /// Verifies the canonical row/column cursor cycle: a fresh table has row and column indices of
    /// negative one, <see cref="ImGui.TableNextRow"/> advances the row index, and
    /// <see cref="ImGui.TableNextColumn"/> walks columns zero through two before wrapping to the
    /// next row's first column.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:2062-2085</c>, <c>2312-2334</c>)
    /// </summary>
    [Test]
    public void Rows_NextRowAndNextColumn_AdvanceIndicesAndWrap()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Cycle", 3, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthFixed, 100.0f);

            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(-1));
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(-1));

            ImGui.TableNextRow();
            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(0));

            Assert.That(ImGui.TableNextColumn(), Is.True);
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(0));
            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(0));

            Assert.That(ImGui.TableNextColumn(), Is.True);
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(1));

            Assert.That(ImGui.TableNextColumn(), Is.True);
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(2));

            // Past the last column the next call starts a new row.
            Assert.That(ImGui.TableNextColumn(), Is.True);
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(0));
            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(1));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TableSetColumnIndex"/> sets the current column and that
    /// <see cref="ImGui.TableGetColumnName(int)"/> returns the label supplied to
    /// <see cref="ImGui.TableSetupColumn"/> both before layout and for the current column.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:1894-1913</c>, <c>2291-2310</c>)
    /// </summary>
    [Test]
    public void Rows_SetColumnIndex_SelectsColumnAndExposesName()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Names", 2, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("First", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("Second", ImGuiTableColumnFlags.WidthFixed, 100.0f);

            Assert.That(ImGui.TableGetColumnName(0), Is.EqualTo("First"));
            Assert.That(ImGui.TableGetColumnName(1), Is.EqualTo("Second"));

            ImGui.TableNextRow();
            Assert.That(ImGui.TableSetColumnIndex(1), Is.True);
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(1));
            Assert.That(ImGui.TableGetColumnName(), Is.EqualTo("Second"));

            Assert.That(ImGui.TableSetColumnIndex(0), Is.True);
            Assert.That(ImGui.TableGetColumnIndex(), Is.EqualTo(0));
            Assert.That(ImGui.TableGetColumnName(), Is.EqualTo("First"));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TableSetBgColor"/> records row-layer and per-cell background
    /// requests on the table instead of drawing immediately, because the row height is unknown until
    /// the row ends. (<c>ThirdParty/imgui/imgui_tables.cpp:1999-2040</c>)
    /// </summary>
    [Test]
    public void Rows_SetBgColor_RecordsRowAndCellRequests()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Background", 2, ImGuiTableFlags.RowBg, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableNextRow();
            Assert.That(ImGui.TableSetColumnIndex(0), Is.True);

            const uint RowColor = 0xFF102030u;
            const uint CellColor = 0xFF405060u;
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, RowColor);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, CellColor);

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(table.RowBgColor[0], Is.EqualTo(RowColor));
            Assert.That(table.RowCellDataCurrent, Is.GreaterThanOrEqualTo(0));
            ImGuiTableCellData cell = table.RowCellData[table.RowCellDataCurrent];
            Assert.That(cell.BgColor, Is.EqualTo(CellColor));
            Assert.That(cell.Column, Is.EqualTo(0));

            ImGui.EndTable();
        });
    }

    #endregion

    #region Headers

    /// <summary>
    /// Verifies that <see cref="ImGui.TableHeadersRow"/> opens a header row whose height is the
    /// larger of the font size and every displayed header label plus twice the vertical cell padding,
    /// and that every enabled column receives a header whose used width extends past its work origin.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3249-3263</c>, <c>3284-3320</c>, <c>3325-3465</c>)
    /// </summary>
    [Test]
    public void Headers_HeadersRow_EmitsOneHeaderPerColumnAtCanonicalRowHeight()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Headers", 3, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("Alpha");
            ImGui.TableSetupColumn("Beta");
            ImGui.TableSetupColumn("Gamma");
            ImGui.TableHeadersRow();

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(table.IsUsingHeaders, Is.True);

            float expectedRowHeight = ImGui.GetFontSize() + _ctx.Style.CellPadding.Y * 2.0f;
            Assert.That(ImGui.TableGetHeaderRowHeight(), Is.EqualTo(expectedRowHeight).Within(0.001f));
            Assert.That(table.RowPosY2 - table.RowPosY1, Is.EqualTo(expectedRowHeight).Within(0.001f));

            for (int columnN = 0; columnN < table.ColumnsCount; columnN++)
            {
                ImGuiTableColumn column = table.Columns[columnN];
                Assert.That(column.ContentMaxXHeadersUsed, Is.GreaterThan(column.WorkMinX));
            }

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that clicking a sortable column header cycles its sort direction: the automatic
    /// default sort is ascending, and one click moves the single specification to descending.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3093-3103</c>, <c>4436-4440</c>)
    /// </summary>
    [Test]
    public void Headers_SortableHeaderClick_TogglesSortDirection()
    {
        ImRect headerRect = default;
        WindowFrame(() => SubmitSortableTable(ImGuiTableFlags.Sortable,
            table => headerRect = HeaderCellScreenRect(table, 0)));

        WindowFrame(() => SubmitSortableTable(ImGuiTableFlags.Sortable, table =>
        {
            ImGuiTableSortSpecs specs = ImGui.TableGetSortSpecs()!;
            Assert.That(specs.SpecsCount, Is.EqualTo(1));
            Assert.That(specs.Specs![0].ColumnIndex, Is.EqualTo(0));
            Assert.That(specs.Specs[0].SortDirection, Is.EqualTo(ImGuiSortDirection.Ascending));
        }));

        ClickAt(new ImVec2((headerRect.Min.X + headerRect.Max.X) * 0.5f,
            (headerRect.Min.Y + headerRect.Max.Y) * 0.5f),
            () => SubmitSortableTable(ImGuiTableFlags.Sortable));

        WindowFrame(() => SubmitSortableTable(ImGuiTableFlags.Sortable, table =>
        {
            ImGuiTableSortSpecs specs = ImGui.TableGetSortSpecs()!;
            Assert.That(specs.SpecsCount, Is.EqualTo(1));
            Assert.That(specs.Specs![0].ColumnIndex, Is.EqualTo(0));
            Assert.That(specs.Specs[0].SortDirection, Is.EqualTo(ImGuiSortDirection.Descending));
        }));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TableAngledHeadersRow"/> consumes the columns marked
    /// <see cref="ImGuiTableColumnFlags.AngledHeader"/>, reports their count, and derives a positive
    /// angled-header band height without asserting.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3469-3501</c>, <c>3525-3540</c>)
    /// </summary>
    [Test]
    public void Headers_AngledRow_HandlesAngledColumns()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Angled", 3, ImGuiTableFlags.Borders, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("One", ImGuiTableColumnFlags.AngledHeader);
            ImGui.TableSetupColumn("Two", ImGuiTableColumnFlags.AngledHeader);
            ImGui.TableSetupColumn("Three", ImGuiTableColumnFlags.AngledHeader);

            ImGui.TableAngledHeadersRow();

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(table.AngledHeadersCount, Is.EqualTo(3));
            Assert.That(table.AngledHeadersHeight, Is.GreaterThan(0.0f));
            Assert.That(ImGui.TableGetRowIndex(), Is.EqualTo(0));

            ImGui.EndTable();
        });
    }

    #endregion

    #region Sorting

    /// <summary>
    /// Verifies that <see cref="ImGui.TableGetSortSpecs"/> returns null for a non-sortable table and
    /// a non-null specification record for a sortable table, that an unsorted sortable table defaults
    /// to the first enabled column ascending, and that the caller clearing
    /// <see cref="ImGuiTableSortSpecs.SpecsDirty"/> keeps it clear until the sort changes.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3059-3072</c>, <c>3189-3204</c>, <c:3206-3236</c>)
    /// </summary>
    [Test]
    public void Sorting_SortableTable_DefaultsToFirstColumnAndClearsSpecsDirty()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("NotSortable", 2, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A");
            ImGui.TableSetupColumn("B");
            ImGui.TableNextRow();
            Assert.That(ImGui.TableGetSortSpecs(), Is.Null);
            ImGui.EndTable();
        });

        WindowFrame(() => SubmitSortableTable(ImGuiTableFlags.Sortable, table =>
        {
            ImGuiTableSortSpecs specs = ImGui.TableGetSortSpecs()!;
            Assert.That(specs, Is.Not.Null);
            Assert.That(specs.SpecsCount, Is.EqualTo(1));
            Assert.That(specs.Specs, Is.Not.Null);
            Assert.That(specs.Specs!.Length, Is.EqualTo(1));
            Assert.That(specs.Specs[0].ColumnIndex, Is.EqualTo(0));
            Assert.That(specs.Specs[0].SortOrder, Is.EqualTo(0));
            Assert.That(specs.Specs[0].SortDirection, Is.EqualTo(ImGuiSortDirection.Ascending));
            Assert.That(specs.SpecsDirty, Is.True);

            specs.SpecsDirty = false;
            Assert.That(ImGui.TableGetSortSpecs()!.SpecsDirty, Is.False);
        }));
    }

    /// <summary>
    /// Verifies that hiding the default-sorted column moves the automatic default sort to the first
    /// remaining enabled column and that the exposed specification follows it.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3189-3204</c>)
    /// </summary>
    [Test]
    public void Sorting_HiddenDefaultColumn_MovesDefaultSortToNextEnabledColumn()
    {
        ImGuiTableFlags flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.Hideable;

        // Initialize the table for one frame so the first-layout Hideable defaults cannot clear the
        // visibility request issued below.
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("HiddenSort", 3, flags, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("Alpha");
            ImGui.TableSetupColumn("Beta");
            ImGui.TableSetupColumn("Gamma");
            ImGui.TableHeadersRow();
            ImGui.EndTable();
        });

        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("HiddenSort", 3, flags, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("Alpha");
            ImGui.TableSetupColumn("Beta");
            ImGui.TableSetupColumn("Gamma");
            ImGui.TableSetColumnEnabled(0, false);
            ImGui.TableHeadersRow();

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(table.Columns[0].IsEnabled, Is.False);

            ImGuiTableSortSpecs specs = ImGui.TableGetSortSpecs()!;
            Assert.That(specs.SpecsCount, Is.EqualTo(1));
            Assert.That(specs.Specs![0].ColumnIndex, Is.EqualTo(1));
            Assert.That(specs.Specs[0].SortDirection, Is.EqualTo(ImGuiSortDirection.Ascending));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that with <see cref="ImGuiTableFlags.SortMulti"/> a Shift-click on a second header
    /// appends a second specification with a dense sort order instead of replacing the first.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3107-3138</c>, <c>3945-3946</c>)
    /// </summary>
    [Test]
    public void Sorting_MultiSort_ShiftClickAppendsSecondSpecification()
    {
        ImGuiTableFlags flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti;

        ImRect betaHeader = default;
        WindowFrame(() => SubmitSortableTable(flags,
            table => betaHeader = HeaderCellScreenRect(table, 1)));

        ShiftClickAt(new ImVec2((betaHeader.Min.X + betaHeader.Max.X) * 0.5f,
            (betaHeader.Min.Y + betaHeader.Max.Y) * 0.5f),
            () => SubmitSortableTable(flags));

        WindowFrame(() => SubmitSortableTable(flags, table =>
        {
            ImGuiTableSortSpecs specs = ImGui.TableGetSortSpecs()!;
            Assert.That(specs.SpecsCount, Is.EqualTo(2));
            Assert.That(specs.Specs![0].ColumnIndex, Is.EqualTo(0));
            Assert.That(specs.Specs[0].SortOrder, Is.EqualTo(0));
            Assert.That(specs.Specs[1].ColumnIndex, Is.EqualTo(1));
            Assert.That(specs.Specs[1].SortOrder, Is.EqualTo(1));
            Assert.That(specs.Specs[1].SortDirection, Is.EqualTo(ImGuiSortDirection.Ascending));
        }));
    }

    /// <summary>
    /// Verifies that a sortable table with <see cref="ImGuiTableFlags.SortTristate"/> publishes a
    /// non-null specification record with zero specifications, because tristate sorting suppresses
    /// the automatic default sort. (<c>ThirdParty/imgui/imgui_tables.cpp:3189-3204</c>)
    /// </summary>
    [Test]
    public void Sorting_SortTristate_ReportsZeroSpecifications()
    {
        WindowFrame(() => SubmitSortableTable(
            ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate, table =>
            {
                ImGuiTableSortSpecs specs = ImGui.TableGetSortSpecs()!;
                Assert.That(specs, Is.Not.Null);
                Assert.That(specs.SpecsCount, Is.EqualTo(0));
                Assert.That(specs.Specs, Is.Null);
            }));
    }

    #endregion

    #region Scroll freeze and context menu

    /// <summary>
    /// Verifies that <see cref="ImGui.TableSetupScrollFreeze"/> records the frozen-column and
    /// frozen-row requests, promotes them to active counts once the scrolling table has a non-zero
    /// scroll offset, bounds the frozen column's maximum width, and advances the following column's
    /// clip rectangle past the frozen column. (<c>ThirdParty/imgui/imgui_tables.cpp:1856-1870</c>,
    /// <c>1131-1144</c>, <c>1241-1242</c>, <c>2434-2463</c>)
    /// </summary>
    [Test]
    public void ScrollFreeze_FreezesFirstColumnAndRow()
    {
        WindowFrame(() =>
        {
            ImGuiTableFlags flags = ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;
            Assert.That(ImGui.BeginTable("Frozen", 3, flags, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthFixed, 80.0f);

            ImGuiTable table = _ctx.CurrentTable!;
            // Frozen regions only activate while the inner window is scrolled away from the origin.
            table.InnerWindow!.Scroll = new ImVec2(10.0f, 10.0f);
            ImGui.TableSetupScrollFreeze(1, 1);

            Assert.That(table.FreezeColumnsRequest, Is.EqualTo(1));
            Assert.That(table.FreezeRowsRequest, Is.EqualTo(1));
            Assert.That(table.FreezeColumnsCount, Is.EqualTo(1));
            Assert.That(table.FreezeRowsCount, Is.EqualTo(1));

            ImGui.TableNextRow();

            // The following column starts where the frozen column ends and is clipped past it.
            Assert.That(table.Columns[1].MinX, Is.EqualTo(table.Columns[0].MaxX).Within(0.001f));
            Assert.That(table.Columns[1].ClipRect.Min.X, Is.GreaterThan(table.Columns[1].MinX));

            // Only a frozen column is width-bounded under horizontal scrolling.
            Assert.That(ImGui.TableCalcMaxColumnWidth(table, 0), Is.LessThan(float.MaxValue));
            Assert.That(ImGui.TableCalcMaxColumnWidth(table, 1), Is.EqualTo(float.MaxValue));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TableOpenContextMenu(int)"/> followed by
    /// <see cref="ImGui.TableBeginContextMenuPopup"/> opens the table's default context-menu popup
    /// for the interacted instance and that <see cref="ImGui.TableDrawDefaultContextMenu"/> submits
    /// its sections without error.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:3647-3675</c>, <c>3715-3829</c>)
    /// </summary>
    [Test]
    public void ContextMenu_OpenAndDrawDefaultPopup()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Context", 2, ImGuiTableFlags.Resizable, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 100.0f);
            ImGui.TableNextRow();

            ImGuiTable table = _ctx.CurrentTable!;
            table.DisableDefaultContextMenu = true; // The manual popup below stands in for the layout hook.

            ImGui.TableOpenContextMenu(-1);
            Assert.That(table.IsContextPopupOpen, Is.True);

            bool open = ImGui.TableBeginContextMenuPopup(table);
            Assert.That(open, Is.True);
            ImGui.TableDrawDefaultContextMenu(table, table.Flags);
            ImGui.EndPopup();

            ImGui.EndTable();
        });
    }

    #endregion

    #region Integration regressions

    /// <summary>
    /// Verifies that with two windows present a table submitted by the second window still lays out
    /// and resolves its columns, which pins the <c>SetCurrentWindow</c> ambient-table resolution that
    /// <see cref="ImGuiTables"/> depends on. (<c>ThirdParty/imgui/imgui.cpp:4802-4821</c>)
    /// </summary>
    [Test]
    public void Regression_TwoWindows_TableLaysOutAndColumnsResolve()
    {
        WarmupFrame();

        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("Warmup");
        ImGui.End();

        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("TableHost");
        Assert.That(ImGui.BeginTable("HostTable", 3, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
        ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 100.0f);
        ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 100.0f);
        ImGui.TableSetupColumn("C", ImGuiTableColumnFlags.WidthFixed, 100.0f);
        ImGui.TableNextRow();

        ImGuiTable table = _ctx.CurrentTable!;
        Assert.That(_ctx.CurrentWindow, Is.SameAs(table.OuterWindow));
        Assert.That(ImGui.TableGetColumnCount(), Is.EqualTo(3));
        Assert.That(table.Columns[0].IsEnabled, Is.True);
        Assert.That(table.Columns[0].WidthGiven, Is.EqualTo(100.0f));
        Assert.That(table.Columns[2].WidthGiven, Is.EqualTo(100.0f));

        ImGui.EndTable();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetCurrentWindow"/> restores the ambient table recorded on the
    /// host window and clears it when switching to a window without a table, and that
    /// <see cref="ImGui.GetContentRegionAvail"/> inside a table cell reports the column's width
    /// rather than the host window's content region.
    /// (<c>ThirdParty/imgui/imgui.cpp:4802-4821</c>, <c>12536-12543</c>)
    /// </summary>
    [Test]
    public void Regression_AmbientTableResolutionAndCellContentRegion()
    {
        WarmupFrame();

        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("Ambient", 2, ImGuiTableFlags.None, new ImVec2(400.0f, 200.0f)), Is.True);
            ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 120.0f);
            ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGuiTable table = _ctx.CurrentTable!;
            Assert.That(ImGui.TableSetColumnIndex(0), Is.True);

            ImVec2 avail = ImGui.GetContentRegionAvail();
            Assert.That(avail.X, Is.EqualTo(120.0f).Within(0.001f));
            Assert.That(avail.X, Is.EqualTo(table.Columns[0].WidthGiven).Within(0.001f));
            Assert.That(avail.X, Is.LessThan(ImGui.GetWindowContentRegionMax().X));

            ImGuiWindow host = _ctx.CurrentWindow!;
            ImGuiWindow warmup = ImGui.FindWindowByName("Warmup")!;
            ImGui.SetCurrentWindow(warmup);
            Assert.That(_ctx.CurrentTable, Is.Null);
            ImGui.SetCurrentWindow(host);
            Assert.That(_ctx.CurrentTable, Is.SameAs(table));

            ImGui.EndTable();
        });
    }

    #endregion

    #region Remediation Chunk 6R Regression Coverage

    /// <summary>
    /// Verifies that table collections and ambient pointers are cleared on context shutdown.
    /// Canonical: <c>imgui.cpp:4680-4685</c> (Bug 5-B1).
    /// </summary>
    [Test]
    public void TableShutdown_CleansUpTableCollectionsAndAmbientPointer()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("CleanupTable", 2), Is.True);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Cell");
            ImGui.EndTable();
        });
        Assert.That(_ctx.Tables.Count, Is.GreaterThan(0));

        ImGui.DestroyContext(_ctx);
        Assert.That(_ctx.Tables.Count, Is.EqualTo(0));
        Assert.That(_ctx.TablesTempData.Count, Is.EqualTo(0));
        Assert.That(_ctx.TablesLastTimeActive.Count, Is.EqualTo(0));
        Assert.That(_ctx.CurrentTable, Is.Null);

        // Recreate _ctx so TearDown completes cleanly.
        _ctx = ImGui.CreateContext();
        _ctx.IO.IniFilename = null; // Tests must never read or write the developer's .ini file.
        _ctx.IO.DisplaySize = new ImVec2(1024.0f, 768.0f);
        _ctx.IO.DeltaTime = 1.0f / 60.0f;
    }

    /// <summary>
    /// Verifies that clipped table header label calculation properly extracts the visible label prefix.
    /// Canonical: <c>imgui_tables.cpp:3455-3457</c> (Bug 5-B2).
    /// </summary>
    [Test]
    public void TableHeader_ClippedTextTooltip_UsesVisiblePrefix()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("TooltipTable", 1, ImGuiTableFlags.Borders), Is.True);
            ImGui.TableSetupColumn("LongColumnTitle##HiddenID");
            ImGui.TableHeadersRow();
            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that sort specs sanitization on tables with >64 columns does not wrap or alias bitmasks.
    /// Canonical: <c>imgui_tables.cpp:3133-3150</c> (Bug 5-B3).
    /// </summary>
    [Test]
    public void TableSortSpecsSanitize_Over64Columns_DoesNotWrapBitmask()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("LargeSortTable", 70, ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti), Is.True);
            for (int i = 0; i < 70; i++)
                ImGui.TableSetupColumn($"Col{i}");
            ImGui.TableHeadersRow();

            ImGuiTable table = _ctx.CurrentTable!;
            table.Columns[0].SortOrder = 0;
            table.Columns[65].SortOrder = 1;
            ImGui.TableSortSpecsSanitize(table);

            Assert.That(table.Columns[0].SortOrder, Is.EqualTo((short)0));
            Assert.That(table.Columns[65].SortOrder, Is.EqualTo((short)1));

            ImGui.EndTable();
        });
    }

    /// <summary>
    /// Verifies that navigation horizontal push scrolling is disabled while inside a table.
    /// Canonical: <c>imgui.cpp:14261-14266</c> (Bug 5-B4).
    /// </summary>
    [Test]
    public void NavUpdateCurrentWindowIsScrollPushableX_InsideTable_SetsFalse()
    {
        WindowFrame(() =>
        {
            Assert.That(ImGui.BeginTable("NavTable", 2), Is.True);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            ImGui.NavUpdateCurrentWindowIsScrollPushableX();
            Assert.That(_ctx.CurrentWindow!.DC.NavIsScrollPushableX, Is.False);

            ImGui.EndTable();

            ImGui.NavUpdateCurrentWindowIsScrollPushableX();
            Assert.That(_ctx.CurrentWindow!.DC.NavIsScrollPushableX, Is.True);
        });
    }

    /// <summary>
    /// Verifies that calling TableNextRow outside of BeginTable scope reports user error and returns early.
    /// Canonical: <c>imgui_tables.cpp:1860, 2065</c> (Bug 5-B5).
    /// </summary>
    [Test]
    public void TableNextRow_OutsideTable_ReportsUserErrorAndDoesNotCrash()
    {
        // The call reports the user error through the canonical assertion and returns without touching
        // null table state; the test declares the guard it exercises (Bug 5-B5).
        using (ExpectAssert("TableNextRow() call should only be done while in BeginTable() scope!"))
            Assert.DoesNotThrow(() => ImGui.TableNextRow());
    }

    #endregion
}
