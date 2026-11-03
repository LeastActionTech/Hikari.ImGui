// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical Tabs and legacy Columns subsystems owned by <c>ImGuiDocking.cs</c> (tab bar
/// and tab item family) and <c>ImGuiTables.cs</c> (legacy Columns). Coverage includes tab-bar
/// lifecycle, first-tab selection, <c>TabItemButton</c> interaction, tab-name resolution, close
/// semantics for normal and unsaved-document tabs, tab sizing, pre-registered dock tabs, column
/// counting and cycling, column offset and width editing, persistence into window storage, and the
/// automatic closure of an open columns set by <c>End()</c>.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/> against canonical Dear ImGui.
/// </remarks>
[TestFixture]
public class TabsAndColumnsTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);


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
    /// <param name="body">The tab-bar or columns submissions and assertions to run while the window is current.</param>
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
    /// three further frames, resubmitting <paramref name="body"/> every frame.
    /// </summary>
    /// <param name="point">Screen-space click position.</param>
    /// <param name="body">Submission run on every frame.</param>
    /// <remarks>
    /// The hover frame is required because tab items and the scrolling arrow buttons use the
    /// <see cref="ImGuiButtonFlags.AllowOverlap"/> path, whose hover test only succeeds once the item
    /// was already the hovered identifier on the previous frame
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

    #endregion

    #region Tabs

    /// <summary>
    /// Verifies that <see cref="ImGui.BeginTabBar"/> binds the context's current tab bar, registers it
    /// in the pool, and that <see cref="ImGui.EndTabBar"/> restores the previous (null) current tab bar.
    /// </summary>
    [Test]
    public void BeginTabBar_EndTabBar_BindsAndRestoresCurrentTabBar()
    {
        bool open = false;
        bool currentDuringBar = false;
        bool restoredAfterEnd = false;
        ImGuiTabBar? bar = null;
        uint barId = 0;

        WindowFrame(() =>
        {
            open = ImGui.BeginTabBar("bar");
            bar = _ctx.CurrentTabBar;
            currentDuringBar = bar != null;
            barId = bar?.ID ?? 0;
            ImGui.EndTabBar();
            restoredAfterEnd = _ctx.CurrentTabBar == null;
        });

        Assert.That(open, Is.True);
        Assert.That(currentDuringBar, Is.True);
        Assert.That(restoredAfterEnd, Is.True);
        Assert.That(ImGui.TabBarFindByID(barId), Is.SameAs(bar));
        Assert.That(_ctx.TabBars.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that a newly appearing tab is selected through the canonical deferred
    /// <c>NextSelectedTabId</c> path so its content becomes visible on the second frame.
    /// Mirrors canonical <c>AutoSelectNewTabs</c>.
    /// </summary>
    [Test]
    public void BeginTabItem_AutoSelectNewTabs_SelectsFirstTabOnSecondFrame()
    {
        bool firstFrameA = true;
        bool firstFrameB = true;
        bool secondFrameA = false;
        bool secondFrameB = true;

        void Body(bool recordFirst)
        {
            if (!ImGui.BeginTabBar("bar", ImGuiTabBarFlags.AutoSelectNewTabs))
                return;
            bool a = ImGui.BeginTabItem("A");
            if (a)
                ImGui.EndTabItem();
            bool b = ImGui.BeginTabItem("B");
            if (b)
                ImGui.EndTabItem();
            ImGui.EndTabBar();
            if (recordFirst)
            {
                firstFrameA = a;
                firstFrameB = b;
            }
            else
            {
                secondFrameA = a;
                secondFrameB = b;
            }
        }

        WindowFrame(() => Body(true));
        WindowFrame(() => Body(false));

        Assert.That(firstFrameA, Is.False);
        Assert.That(firstFrameB, Is.False);
        Assert.That(secondFrameA, Is.True);
        Assert.That(secondFrameB, Is.False);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TabItemButton"/> is submitted and reports a press, which requires
    /// the canonical <c>p_open == NULL</c> sentinel rather than an early-out. The button is a new tab on
    /// the first frame, so its real rectangle is only available from the second frame.
    /// </summary>
    [Test]
    public void TabItemButton_IsSubmittedAndReportsPress()
    {
        ImRect buttonRect = new();
        bool clicked = false;

        void Body()
        {
            if (!ImGui.BeginTabBar("bar", ImGuiTabBarFlags.AutoSelectNewTabs))
                return;
            if (ImGui.TabItemButton("?"))
                clicked = true;
            buttonRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.EndTabBar();
        }

        WindowFrame(Body); // Frame 1: tab appears, empty placeholder rectangle.
        WindowFrame(Body); // Frame 2: real rectangle available.

        Assert.That(buttonRect.GetWidth(), Is.GreaterThan(0.0f), "TabItemButton must submit a non-empty item.");

        ImVec2 center = new(
            (buttonRect.Min.X + buttonRect.Max.X) * 0.5f,
            (buttonRect.Min.Y + buttonRect.Max.Y) * 0.5f);
        ClickAt(center, Body);

        Assert.That(clicked, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TabBarGetTabName"/> resolves a submitted manual tab to its label
    /// and reports <c>"N/A"</c> for a tab whose name has not been recorded this frame.
    /// </summary>
    [Test]
    public void TabBarGetTabName_ResolvesLabelAndMissingName()
    {
        string submittedName = "";
        string missingName = "";

        WindowFrame(() =>
        {
            if (!ImGui.BeginTabBar("bar", ImGuiTabBarFlags.AutoSelectNewTabs))
                return;
            ImGui.BeginTabItem("Alpha");
            ImGuiTabBar bar = _ctx.CurrentTabBar!;
            ImGuiTabItem tab = bar.Tabs[bar.LastTabItemIdx];
            submittedName = ImGui.TabBarGetTabName(bar, tab);
            ImGui.EndTabItem();
            ImGui.EndTabBar();

            ImGuiTabBar unusedBar = _ctx.TabBars.GetOrAddByKey(0xBEEFu);
            missingName = ImGui.TabBarGetTabName(unusedBar, new ImGuiTabItem());
        });

        Assert.That(submittedName, Is.EqualTo("Alpha"));
        Assert.That(missingName, Is.EqualTo("N/A"));
    }

    /// <summary>
    /// Verifies that closing a normal selected tab defers removal to the next layout pass by setting
    /// <c>WantClose</c>, clearing the selected identifiers, and invalidating the visible frame instead
    /// of erasing the item immediately.
    /// Mirrors canonical <c>TabBarCloseTab</c>.
    /// </summary>
    [Test]
    public void TabBarCloseTab_NormalSelectedTab_DefersRemoval()
    {
        bool wantClose = false;
        bool stillPresent = false;
        bool selectionCleared = false;
        bool visibleInvalidated = false;

        void Body()
        {
            if (!ImGui.BeginTabBar("bar", ImGuiTabBarFlags.AutoSelectNewTabs))
                return;
            if (ImGui.BeginTabItem("A"))
            {
                ImGuiTabBar bar = _ctx.CurrentTabBar!;
                ImGuiTabItem tab = bar.Tabs[bar.LastTabItemIdx];
                ImGui.TabBarCloseTab(bar, tab);
                wantClose = tab.WantClose;
                stillPresent = bar.Tabs.Contains(tab);
                selectionCleared = bar.SelectedTabId == 0 && bar.NextSelectedTabId == 0;
                visibleInvalidated = tab.LastFrameVisible == -1;
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        WindowFrame(Body); // Frame 1.
        WindowFrame(Body); // Frame 2: tab selected; closure request recorded.

        Assert.That(wantClose, Is.True);
        Assert.That(stillPresent, Is.True);
        Assert.That(selectionCleared, Is.True);
        Assert.That(visibleInvalidated, Is.True);
    }

    /// <summary>
    /// Verifies that closing an unsaved-document tab does not set <c>WantClose</c>, giving the owner a
    /// chance to confirm the closure.
    /// </summary>
    [Test]
    public void TabBarCloseTab_UnsavedDocument_DoesNotRequestClose()
    {
        bool wantClose = true;
        bool stillPresent = false;

        void Body()
        {
            if (!ImGui.BeginTabBar("bar", ImGuiTabBarFlags.AutoSelectNewTabs))
                return;
            if (ImGui.BeginTabItem("Draft", ImGuiTabItemFlags.UnsavedDocument))
                ImGui.EndTabItem();
            ImGuiTabBar bar = _ctx.CurrentTabBar!;
            ImGuiTabItem tab = bar.Tabs[bar.LastTabItemIdx];
            ImGui.TabBarCloseTab(bar, tab);
            wantClose = tab.WantClose;
            stillPresent = bar.Tabs.Contains(tab);
            ImGui.EndTabBar();
        }

        WindowFrame(Body);
        WindowFrame(Body);

        Assert.That(wantClose, Is.False);
        Assert.That(stillPresent, Is.True);
    }

    /// <summary>
    /// Verifies the canonical no-close-button width arithmetic, including the one-pixel pad, and the
    /// clamp to <see cref="ImGui.TabBarCalcMaxTabWidth"/>.
    /// </summary>
    [Test]
    public void TabItemCalcSize_UsesCanonicalPaddingAndClamp()
    {
        float noCloseWidth = 0.0f;
        float closeWidth = 0.0f;
        float clampedWidth = 0.0f;
        float expectedNoClose = 0.0f;
        float expectedClose = 0.0f;

        WindowFrame(() =>
        {
            ImGuiStyle style = ImGui.GetStyle();
            ImVec2 labelSize = ImGui.CalcTextSize("Tab", true);
            noCloseWidth = ImGui.TabItemCalcSize("Tab", false).X;
            closeWidth = ImGui.TabItemCalcSize("Tab", true).X;
            clampedWidth = ImGui.TabItemCalcSize(new string('W', 400), false).X;
            expectedNoClose = labelSize.X + style.FramePadding.X + style.FramePadding.X + 1.0f;
            expectedClose = labelSize.X + style.FramePadding.X + style.FramePadding.X + style.ItemInnerSpacing.X + ImGui.GetFontSize();
        });

        Assert.That(noCloseWidth, Is.EqualTo(expectedNoClose).Within(0.001f));
        Assert.That(closeWidth, Is.EqualTo(expectedClose).Within(0.001f));
        Assert.That(clampedWidth, Is.EqualTo(ImGui.TabBarCalcMaxTabWidth()).Within(0.001f));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TabBarAddTab"/> initializes <c>LastFrameVisible</c> so a
    /// pre-registered tab survives the next layout pass, and that the no-close-button flag is derived
    /// from the window.
    /// </summary>
    [Test]
    public void TabBarAddTab_InitializesVisibleFrameAndCloseFlag()
    {
        int lastFrameVisible = -2;
        bool noCloseButton = false;
        ImGuiTabItemFlags storedFlags = 0;

        WindowFrame(() =>
        {
            ImGuiWindow window = ImGui.GetCurrentWindow();
            window.HasCloseButton = false;
            ImGuiTabBar bar = _ctx.TabBars.GetOrAddByKey(0xADDu);
            bar.CurrFrameVisible = -1;
            ImGui.TabBarAddTab(bar, ImGuiTabItemFlags.None, window);
            ImGuiTabItem tab = bar.Tabs[^1];
            lastFrameVisible = tab.LastFrameVisible;
            storedFlags = tab.Flags;
            noCloseButton = (storedFlags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_NoCloseButton) != 0;
            window.HasCloseButton = true;
        });

        Assert.That(lastFrameVisible, Is.GreaterThanOrEqualTo(0));
        Assert.That(noCloseButton, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.TabItemSpacing"/> submits an invisible button tab, which requires
    /// the canonical <c>p_open == NULL</c> sentinel. Before that sentinel was represented, the call was
    /// inert and never registered an item.
    /// </summary>
    [Test]
    public void TabItemSpacing_RegistersInvisibleButtonTab()
    {
        int tabCount = 0;
        bool isButton = false;
        bool isInvisible = false;

        WindowFrame(() =>
        {
            if (!ImGui.BeginTabBar("bar", ImGuiTabBarFlags.AutoSelectNewTabs))
                return;
            ImGui.TabItemSpacing("gap", ImGuiTabItemFlags.None, 40.0f);
            ImGuiTabBar bar = _ctx.CurrentTabBar!;
            tabCount = bar.Tabs.Count;
            if (tabCount > 0)
            {
                ImGuiTabItem tab = bar.Tabs[bar.LastTabItemIdx];
                isButton = (tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) != 0;
                isInvisible = (tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Invisible) != 0;
            }
            ImGui.EndTabBar();
        });

        Assert.That(tabCount, Is.EqualTo(1));
        Assert.That(isButton, Is.True);
        Assert.That(isInvisible, Is.True);
    }

    #endregion

    #region Legacy columns

    /// <summary>
    /// Verifies column counting, the initial current index, consecutive advancement, and the wrap back
    /// to the first column after the last one.
    /// Mirrors canonical <c>NextColumn</c>.
    /// </summary>
    [Test]
    public void Columns_CountAndNextColumn_WrapsToFirstColumn()
    {
        int count = 0;
        int index0 = -1;
        int index1 = -1;
        int index2 = -1;
        int indexWrap = -1;

        WindowFrame(() =>
        {
            ImGui.Columns(3, "cols", true);
            count = ImGui.GetColumnsCount();
            index0 = ImGui.GetColumnIndex();
            ImGui.NextColumn();
            index1 = ImGui.GetColumnIndex();
            ImGui.NextColumn();
            index2 = ImGui.GetColumnIndex();
            ImGui.NextColumn();
            indexWrap = ImGui.GetColumnIndex();
            ImGui.Columns(1);
        });

        Assert.That(count, Is.EqualTo(3));
        Assert.That(index0, Is.EqualTo(0));
        Assert.That(index1, Is.EqualTo(1));
        Assert.That(index2, Is.EqualTo(2));
        Assert.That(indexWrap, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies pixel offset and width editing round-trips through the normalized column coordinates.
    /// Mirrors canonical <c>GetColumnOffset</c>, <c>SetColumnOffset</c>,
    /// <c>GetColumnWidth</c>, <c>SetColumnWidth</c>).
    /// </summary>
    [Test]
    public void Columns_SetOffsetAndWidth_RoundTrips()
    {
        float offset2 = 0.0f;
        float width1 = 0.0f;
        float normRoundTrip = 0.0f;

        WindowFrame(() =>
        {
            ImGui.BeginColumns("c", 3);
            ImGui.SetColumnOffset(2, 200.0f);
            offset2 = ImGui.GetColumnOffset(2);
            ImGui.SetColumnWidth(1, 90.0f);
            width1 = ImGui.GetColumnWidth(1);

            ImGuiOldColumns columns = ImGui.GetCurrentWindow().DC.CurrentColumns!;
            float norm = ImGui.GetColumnNormFromOffset(columns, 137.5f);
            normRoundTrip = ImGui.GetColumnOffsetFromNorm(columns, norm);
            ImGui.EndColumns();
        });

        Assert.That(offset2, Is.EqualTo(200.0f).Within(0.001f));
        Assert.That(width1, Is.EqualTo(90.0f).Within(0.001f));
        Assert.That(normRoundTrip, Is.EqualTo(137.5f).Within(0.001f));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.End"/> force-closes a columns set the caller left open, so the
    /// current columns pointer does not leak past the window. The set itself remains in window storage
    /// for the next frame.
    /// Mirrors canonical <c>EndColumns</c>.
    /// </summary>
    [Test]
    public void End_ForceClosesOpenColumnsWithoutDiscardingStorage()
    {
        ImGuiWindow? window = null;
        ImGuiOldColumns? closedColumns = null;

        WindowFrame(() =>
        {
            window = ImGui.GetCurrentWindow();
            ImGui.BeginColumns("open", 2);
            closedColumns = window.DC.CurrentColumns;
            ImGui.Text("content");
            // Deliberately no EndColumns()/Columns(1).
        });

        Assert.That(window, Is.Not.Null);
        Assert.That(window!.DC.CurrentColumns, Is.Null, "End() must close the open columns set.");
        Assert.That(window.ColumnsStorage.Count, Is.EqualTo(1));
        Assert.That(window.ColumnsStorage[0], Is.SameAs(closedColumns));
    }

    /// <summary>
    /// Verifies that a columns set keyed by the same identifier is reused across frames from the
    /// window's columns storage rather than recreated.
    /// Mirrors canonical <c>FindOrCreateColumns</c>.
    /// </summary>
    [Test]
    public void Columns_SameIdentifier_ReusesStoredSetAcrossFrames()
    {
        ImGuiOldColumns? first = null;
        ImGuiOldColumns? second = null;

        WindowFrame(() =>
        {
            ImGui.Columns(3, "persist", true);
            first = ImGui.GetCurrentWindow().DC.CurrentColumns;
            ImGui.Columns(1);
        });
        WindowFrame(() =>
        {
            ImGui.Columns(3, "persist", true);
            second = ImGui.GetCurrentWindow().DC.CurrentColumns;
            ImGui.Columns(1);
        });

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
        Assert.That(ImGui.FindWindowByName("Window")!.ColumnsStorage.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// Exercises the span-all-columns paths added to the legacy columns subsystem: a horizontal
    /// separator and a <c>SpanAllColumns</c> selectable must switch to the columns background channel
    /// and restore it without unbalancing the draw-list splitter or the window clip stack.
    /// </summary>
    [Test]
    public void Columns_SpanAllColumnsItems_RestoreChannelState()
    {
        int countAfterEnd = -1;
        bool currentColumnsCleared = false;

        WindowFrame(() =>
        {
            ImGui.Columns(3, "span", true);
            ImGui.Separator();
            ImGui.Selectable("spanning", false, ImGuiSelectableFlags.SpanAllColumns);
            ImGui.NextColumn();
            ImGui.Text("second");
            ImGui.Columns(1);
            countAfterEnd = ImGui.GetColumnsCount();
            currentColumnsCleared = ImGui.GetCurrentWindow().DC.CurrentColumns == null;
        });

        Assert.That(countAfterEnd, Is.EqualTo(1));
        Assert.That(currentColumnsCleared, Is.True);
    }

    #endregion
}