// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Hikari.ImGui;

namespace Hikari.ImGui.Demo;

/// <summary>
/// ImGui Demo sections covering Layout &amp; Scrolling, Popups &amp; Modals, Tables, Inputs &amp; Focus,
/// and core Example Applications.
/// Ports canonical Dear ImGui implementation.
/// </summary>
public static partial class ImGuiDemo
{
    #region Helper Utilities

    /// <summary>
    /// Displays a small disabled '(?)' mark with a wrapped tooltip when hovered.
    /// </summary>
    /// <param name="desc">Description text to display in the tooltip.</param>
    public static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.BeginItemTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// Temporarily tightens frame padding and item spacing for dense demo control rows.
    /// </summary>
    public static void PushStyleCompact()
    {
        ImGuiStyle style = ImGui.GetStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(style.FramePadding.X, (float)(int)(style.FramePadding.Y * 0.60f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(style.ItemSpacing.X, (float)(int)(style.ItemSpacing.Y * 0.60f)));
    }

    /// <summary>
    /// Pops the compact style pushed by <see cref="PushStyleCompact"/>.
    /// </summary>
    public static void PopStyleCompact()
    {
        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// Type-safe wrapper for <see cref="ImGui.CheckboxFlags(string, ref int, int)"/> on enum flag sets.
    /// </summary>
    public static bool CheckboxFlags<T>(string label, ref T flags, T flagsValue) where T : struct, Enum
    {
        int val = Convert.ToInt32(flags);
        int flagVal = Convert.ToInt32(flagsValue);
        bool ret = ImGui.CheckboxFlags(label, ref val, flagVal);
        if (ret)
            flags = (T)Enum.ToObject(typeof(T), val);
        return ret;
    }

    private static bool s_menuOptionsEnabled = true;

    /// <summary>Value shared by the "Value" slider and the "Input" field of the example menu's Options submenu.</summary>
    private static float s_menuOptionValue = 0.5f;

    /// <summary>Selected item index of the "Combo" row of the example menu's Options submenu.</summary>
    private static int s_menuOptionCombo = 0;

    /// <summary>State of the "SomeOption" checkbox appended to the example menu's Options submenu.</summary>
    private static bool s_menuAppendedOption = true;

    /// <summary>
    /// Showcase menu used across menus, child windows, and modals.
    /// </summary>
    public static void ShowExampleMenuFile()
    {
        ImGui.MenuItem("(demo menu)", null, false, false);
        if (ImGui.MenuItem("New")) { }
        if (ImGui.MenuItem("Open", "Ctrl+O")) { }
        if (ImGui.BeginMenu("Open Recent"))
        {
            ImGui.MenuItem("fish_hat.c");
            ImGui.MenuItem("fish_hat.inl");
            ImGui.MenuItem("fish_hat.h");
            if (ImGui.BeginMenu("More.."))
            {
                ImGui.MenuItem("Hello");
                ImGui.MenuItem("Sailor");
                if (ImGui.BeginMenu("Recurse.."))
                {
                    ShowExampleMenuFile();
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
        if (ImGui.MenuItem("Save", "Ctrl+S")) { }
        if (ImGui.MenuItem("Save As..")) { }

        ImGui.Separator();
        if (ImGui.BeginMenu("Options"))
        {
            ImGui.MenuItem("Enabled", "", ref s_menuOptionsEnabled);

            // A child window embedded in a menu demonstrates that scrollable content works there too.
            ImGui.BeginChild("child", new ImVec2(0, ImGui.GetTextLineHeightWithSpacing() * 5.0f), ImGuiChildFlags.Borders);
            for (int i = 0; i < 10; i++)
                ImGui.Text($"Scrolling Text {i}");
            ImGui.EndChild();

            ImGui.SliderFloat("Value", ref s_menuOptionValue, 0.0f, 1.0f);
            ImGui.InputFloat("Input", ref s_menuOptionValue, 0.1f);
            ImGui.Combo("Combo", ref s_menuOptionCombo, "Yes\0No\0Maybe\0\0");
            ImGui.EndMenu();
        }

        // The Colors submenu paints a swatch of each style color beside its canonical name.
        if (ImGui.BeginMenu("Colors"))
        {
            float swatchSize = ImGui.GetTextLineHeight();
            for (int i = 0; i < (int)ImGuiCol.COUNT; i++)
            {
                string colorName = ImGui.GetStyleColorName((ImGuiCol)i);
                ImVec2 swatchPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(swatchPos, new ImVec2(swatchPos.X + swatchSize, swatchPos.Y + swatchSize), ImGui.GetColorU32((ImGuiCol)i));
                ImGui.Dummy(new ImVec2(swatchSize, swatchSize));
                ImGui.SameLine();
                ImGui.MenuItem(colorName);
            }
            ImGui.EndMenu();
        }

        // A menu may be appended to after it has already been created; this second BeginMenu("Options")
        // call adds a row to the submenu built above. Real applications may do this from distant code sites.
        if (ImGui.BeginMenu("Options"))
        {
            ImGui.Checkbox("SomeOption", ref s_menuAppendedOption);
            ImGui.EndMenu();
        }

        // A menu can be submitted disabled; the body below is intentionally never entered.
        if (ImGui.BeginMenu("Disabled", false))
        {
            ImGui.EndMenu();
        }
        if (ImGui.MenuItem("Checked", null, true)) { }
        ImGui.Separator();
        if (ImGui.MenuItem("Quit", "Alt+F4")) { }
    }

    #endregion

    #region Layout & Scrolling (Lines 4621-5506)

    private static bool s_layoutDisableMouseWheel = false;
    private static bool s_layoutDisableMenu = false;
    private static int s_layoutDrawLines = 3;
    private static int s_layoutMaxHeightInLines = 10;
    private static int s_layoutOffsetX = 0;
    private static bool s_layoutOverrideBgColor = true;
    private static ImGuiChildFlags s_layoutChildFlags = ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX | ImGuiChildFlags.ResizeY;

    private static float s_widthF = 0.0f;
    private static bool s_widthShowIndented = true;

    /// <summary>First of the four "Rich" checkbox row toggles of the Basic Horizontal Layout demonstration.</summary>
    private static bool s_horizontalCheck1 = false;

    /// <summary>Second of the four "Rich" checkbox row toggles of the Basic Horizontal Layout demonstration.</summary>
    private static bool s_horizontalCheck2 = false;

    /// <summary>Third of the four "Rich" checkbox row toggles of the Basic Horizontal Layout demonstration.</summary>
    private static bool s_horizontalCheck3 = false;

    /// <summary>Fourth of the four "Rich" checkbox row toggles of the Basic Horizontal Layout demonstration.</summary>
    private static bool s_horizontalCheck4 = false;

    /// <summary>Value of the "X" slider in the Basic Horizontal Layout demonstration.</summary>
    private static float s_horizontalFloat0 = 1.0f;

    /// <summary>Value of the "Y" slider in the Basic Horizontal Layout demonstration.</summary>
    private static float s_horizontalFloat1 = 2.0f;

    /// <summary>Value of the "Z" slider in the Basic Horizontal Layout demonstration.</summary>
    private static float s_horizontalFloat2 = 3.0f;

    /// <summary>Selected item index of the "Combo" of the Basic Horizontal Layout demonstration.</summary>
    private static int s_horizontalComboItem = -1;

    /// <summary>Four independent list box selections of the Basic Horizontal Layout demonstration.</summary>
    private static readonly int[] s_horizontalListSelection = { 0, 1, 2, 3 };

    private static int s_scrollTrackItem = 50;
    private static bool s_scrollEnableTrack = true;
    private static bool s_scrollEnableExtraDecorations = false;
    private static float s_scrollToOffPx = 0.0f;
    private static float s_scrollToPosPx = 200.0f;

    /// <summary>Line count of the miscellaneous horizontal scrolling demonstration's slider.</summary>
    private static int s_scrollLines = 7;

    /// <summary>Whether the "Horizontal contents size demo window" is shown.</summary>
    private static bool s_scrollShowContentsSizeWindow = false;

    /// <summary>Whether the contents-size demonstration window enables a horizontal scrollbar.</summary>
    private static bool s_scrollContentsSizeShowHScrollbar = true;

    /// <summary>Whether the contents-size demonstration window shows its 300-wide button.</summary>
    private static bool s_scrollContentsSizeShowButton = true;

    /// <summary>Whether the contents-size demonstration window shows its tree nodes.</summary>
    private static bool s_scrollContentsSizeShowTreeNodes = true;

    /// <summary>Whether the contents-size demonstration window shows wrapped text.</summary>
    private static bool s_scrollContentsSizeShowTextWrapped = false;

    /// <summary>Whether the contents-size demonstration window shows its table and legacy columns.</summary>
    private static bool s_scrollContentsSizeShowColumns = true;

    /// <summary>Whether the contents-size demonstration window shows its tab bar.</summary>
    private static bool s_scrollContentsSizeShowTabBar = true;

    /// <summary>Whether the contents-size demonstration window shows a child window.</summary>
    private static bool s_scrollContentsSizeShowChild = false;

    /// <summary>Whether the contents-size demonstration window overrides its content size.</summary>
    private static bool s_scrollContentsSizeExplicit = false;

    /// <summary>Explicit content width, in pixels, of the contents-size demonstration window.</summary>
    private static float s_scrollContentsSizeX = 300.0f;

    private static ImVec2 s_clipSize = new(100.0f, 100.0f);
    private static ImVec2 s_clipOffset = new(30.0f, 30.0f);

    /// <summary>Whether the Overlap Mode demonstration calls SetNextItemAllowOverlap() before its items.</summary>
    private static bool s_layoutEnableAllowOverlap = true;

    /// <summary>
    /// Demonstrates child windows, widget widths, basic horizontal layout, groups,
    /// text baseline alignment, scrolling (vertical &amp; horizontal), clipping, and overlap mode.
    /// </summary>
    public static void DemoWindowLayout()
    {
        if (!ImGui.CollapsingHeader("Layout & Scrolling"))
            return;

        // -------------------------------------------------------------------------
        // Child windows
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Child windows"))
        {
            ImGui.SeparatorText("Child windows");
            HelpMarker("Use child windows to begin into a self-contained independent scrolling/clipping regions within a host window.");

            ImGui.Checkbox("Disable Mouse Wheel", ref s_layoutDisableMouseWheel);
            ImGui.Checkbox("Disable Menu", ref s_layoutDisableMenu);

            // Child 1: no border, enable horizontal scrollbar
            {
                ImGuiWindowFlags windowFlags = ImGuiWindowFlags.HorizontalScrollbar;
                if (s_layoutDisableMouseWheel)
                    windowFlags |= ImGuiWindowFlags.NoScrollWithMouse;

                ImGui.BeginChild("ChildL", new ImVec2(ImGui.GetContentRegionAvail().X * 0.5f, 260.0f), ImGuiChildFlags.None, windowFlags);
                for (int i = 0; i < 100; i++)
                    ImGui.Text($"{i:D4}: scrollable region");
                ImGui.EndChild();
            }

            ImGui.SameLine();

            // Child 2: rounded border + menu bar
            {
                ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None;
                if (s_layoutDisableMouseWheel)
                    windowFlags |= ImGuiWindowFlags.NoScrollWithMouse;
                if (!s_layoutDisableMenu)
                    windowFlags |= ImGuiWindowFlags.MenuBar;

                ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5.0f);
                ImGui.BeginChild("ChildR", new ImVec2(0.0f, 260.0f), ImGuiChildFlags.Borders, windowFlags);
                if (!s_layoutDisableMenu && ImGui.BeginMenuBar())
                {
                    if (ImGui.BeginMenu("Menu"))
                    {
                        ShowExampleMenuFile();
                        ImGui.EndMenu();
                    }
                    ImGui.EndMenuBar();
                }

                if (ImGui.BeginTable("split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings))
                {
                    for (int i = 0; i < 100; i++)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Button($"{i:D3}", new ImVec2(-float.Epsilon, 0.0f));
                    }
                    ImGui.EndTable();
                }
                ImGui.EndChild();
                ImGui.PopStyleVar();
            }

            // Child 3: manual-resize
            ImGui.SeparatorText("Manual-resize");
            {
                HelpMarker("Drag bottom border to resize. Double-click bottom border to auto-fit to vertical contents.");
                ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBg));
                if (ImGui.BeginChild("ResizableChild", new ImVec2(-float.Epsilon, ImGui.GetTextLineHeightWithSpacing() * 8), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeY))
                {
                    for (int n = 0; n < 10; n++)
                        ImGui.Text($"Line {n:D4}");
                }
                ImGui.PopStyleColor();
                ImGui.EndChild();
            }

            // Child 4: auto-resizing height with a limit
            ImGui.SeparatorText("Auto-resize with constraints");
            {
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
                ImGui.DragInt("Lines Count", ref s_layoutDrawLines, 0.2f);
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
                ImGui.DragInt("Max Height (in Lines)", ref s_layoutMaxHeightInLines, 0.2f);

                ImGui.SetNextWindowSizeConstraints(
                    new ImVec2(0.0f, ImGui.GetTextLineHeightWithSpacing() * 1),
                    new ImVec2(float.MaxValue, ImGui.GetTextLineHeightWithSpacing() * s_layoutMaxHeightInLines));

                if (ImGui.BeginChild("ConstrainedChild", new ImVec2(-float.Epsilon, 0.0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
                {
                    for (int n = 0; n < s_layoutDrawLines; n++)
                        ImGui.Text($"Line {n:D4}");
                }
                ImGui.EndChild();
            }

            ImGui.SeparatorText("Misc/Advanced");
            {
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
                ImGui.DragInt("Offset X", ref s_layoutOffsetX, 1.0f, -1000, 1000);
                ImGui.Checkbox("Override ChildBg color", ref s_layoutOverrideBgColor);
                CheckboxFlags("ImGuiChildFlags_Borders", ref s_layoutChildFlags, ImGuiChildFlags.Borders);
                CheckboxFlags("ImGuiChildFlags_AlwaysUseWindowPadding", ref s_layoutChildFlags, ImGuiChildFlags.AlwaysUseWindowPadding);
                CheckboxFlags("ImGuiChildFlags_ResizeX", ref s_layoutChildFlags, ImGuiChildFlags.ResizeX);
                CheckboxFlags("ImGuiChildFlags_ResizeY", ref s_layoutChildFlags, ImGuiChildFlags.ResizeY);
                CheckboxFlags("ImGuiChildFlags_FrameStyle", ref s_layoutChildFlags, ImGuiChildFlags.FrameStyle);
                ImGui.SameLine();
                HelpMarker("Style the child window like a framed item: use FrameBg, FrameRounding, FrameBorderSize, FramePadding.");

                if ((s_layoutChildFlags & ImGuiChildFlags.FrameStyle) != 0)
                    s_layoutOverrideBgColor = false;

                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + s_layoutOffsetX);
                if (s_layoutOverrideBgColor)
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new ImVec4(1.0f, 0.0f, 0.0f, 0.4f));

                ImGui.BeginChild("Red", new ImVec2(200, 100), s_layoutChildFlags, ImGuiWindowFlags.None);
                if (s_layoutOverrideBgColor)
                    ImGui.PopStyleColor();

                for (int n = 0; n < 50; n++)
                    ImGui.Text($"Some test {n}");
                ImGui.EndChild();

                bool childHovered = ImGui.IsItemHovered();
                ImVec2 childMin = ImGui.GetItemRectMin();
                ImVec2 childMax = ImGui.GetItemRectMax();
                ImGui.Text($"Hovered: {childHovered}");
                ImGui.Text($"Rect of child window is: ({childMin.X:F0},{childMin.Y:F0}) ({childMax.X:F0},{childMax.Y:F0})");
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Widgets Width
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Widgets Width"))
        {
            ImGui.Checkbox("Show indented items", ref s_widthShowIndented);

            // SetNextItemWidth() sets the width of a single upcoming item; PushItemWidth()/PopItemWidth()
            // surrounds a group of items. Real code should pick widths proportional to the font size.
            ImGui.Text("SetNextItemWidth/PushItemWidth(100)");
            ImGui.SameLine(); HelpMarker("Fixed width.");
            ImGui.PushItemWidth(100);
            ImGui.DragFloat("float##1b", ref s_widthF);
            if (s_widthShowIndented)
            {
                ImGui.Indent();
                ImGui.DragFloat("float (indented)##1b", ref s_widthF);
                ImGui.Unindent();
            }
            ImGui.PopItemWidth();

            ImGui.Text("SetNextItemWidth/PushItemWidth(-100)");
            ImGui.SameLine(); HelpMarker("Align to right edge minus 100");
            ImGui.PushItemWidth(-100);
            ImGui.DragFloat("float##2a", ref s_widthF);
            if (s_widthShowIndented)
            {
                ImGui.Indent();
                ImGui.DragFloat("float (indented)##2b", ref s_widthF);
                ImGui.Unindent();
            }
            ImGui.PopItemWidth();

            ImGui.Text("SetNextItemWidth/PushItemWidth(GetContentRegionAvail().x * 0.5f)");
            ImGui.SameLine(); HelpMarker("Half of available width.\n(~ right-cursor_pos)\n(works within a column set)");
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
            ImGui.DragFloat("float##3a", ref s_widthF);
            if (s_widthShowIndented)
            {
                ImGui.Indent();
                ImGui.DragFloat("float (indented)##3b", ref s_widthF);
                ImGui.Unindent();
            }
            ImGui.PopItemWidth();

            ImGui.Text("SetNextItemWidth/PushItemWidth(-GetContentRegionAvail().x * 0.5f)");
            ImGui.SameLine(); HelpMarker("Align to right edge minus half");
            ImGui.PushItemWidth(-ImGui.GetContentRegionAvail().X * 0.5f);
            ImGui.DragFloat("float##4a", ref s_widthF);
            if (s_widthShowIndented)
            {
                ImGui.Indent();
                ImGui.DragFloat("float (indented)##4b", ref s_widthF);
                ImGui.Unindent();
            }
            ImGui.PopItemWidth();

            ImGui.Text("SetNextItemWidth/PushItemWidth(-Min(GetContentRegionAvail().x * 0.40f, GetFontSize() * 12))");
            ImGui.PushItemWidth(-MathF.Min(ImGui.GetFontSize() * 12, ImGui.GetContentRegionAvail().X * 0.40f));
            ImGui.DragFloat("float##5a", ref s_widthF);
            if (s_widthShowIndented)
            {
                ImGui.Indent();
                ImGui.DragFloat("float (indented)##5b", ref s_widthF);
                ImGui.Unindent();
            }
            ImGui.PopItemWidth();

            // PushItemWidth() surrounds six items here; SetNextItemWidth() before each would be equivalent.
            ImGui.Text("SetNextItemWidth/PushItemWidth(-FLT_MIN)");
            ImGui.SameLine(); HelpMarker("Align to right edge");
            ImGui.PushItemWidth(-float.Epsilon);
            ImGui.DragFloat("##float6a", ref s_widthF);
            if (s_widthShowIndented)
            {
                ImGui.Indent();
                ImGui.DragFloat("float (indented)##6b", ref s_widthF);
                ImGui.Unindent();
            }
            ImGui.PopItemWidth();

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Basic Horizontal Layout
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Basic Horizontal Layout"))
        {
            ImGui.TextWrapped("(Use ImGui::SameLine() to keep adding items to the right of the preceding item)");

            // Text
            ImGui.Text("Two items: Hello"); ImGui.SameLine();
            ImGui.TextColored(new ImVec4(1, 1, 0, 1), "Sailor");

            // Adjust spacing
            ImGui.Text("More spacing: Hello"); ImGui.SameLine(0.0f, 20.0f);
            ImGui.TextColored(new ImVec4(1, 1, 0, 1), "Sailor");

            // Button
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Normal buttons"); ImGui.SameLine();
            ImGui.Button("Banana"); ImGui.SameLine();
            ImGui.Button("Apple"); ImGui.SameLine();
            ImGui.Button("Corniflower");

            // Button
            ImGui.Text("Small buttons"); ImGui.SameLine();
            ImGui.SmallButton("Like this one"); ImGui.SameLine();
            ImGui.Text("can fit within a text block.");

            // Aligned to arbitrary position. Easy/cheap column.
            ImGui.Text("Aligned");
            ImGui.SameLine(150.0f); ImGui.Text("x=150");
            ImGui.SameLine(300.0f); ImGui.Text("x=300");
            ImGui.Text("Aligned");
            ImGui.SameLine(150.0f); ImGui.SmallButton("x=150");
            ImGui.SameLine(300.0f); ImGui.SmallButton("x=300");

            // Checkbox
            ImGui.Checkbox("My", ref s_horizontalCheck1); ImGui.SameLine();
            ImGui.Checkbox("Tailor", ref s_horizontalCheck2); ImGui.SameLine();
            ImGui.Checkbox("Is", ref s_horizontalCheck3); ImGui.SameLine();
            ImGui.Checkbox("Rich", ref s_horizontalCheck4);

            // Various
            ImGui.PushItemWidth(ImGui.CalcTextSize("AAAAAAA").X);
            string[] horizontalItems = { "AAAA", "BBBB", "CCCC", "DDDD" };
            ImGui.Combo("Combo", ref s_horizontalComboItem, horizontalItems); ImGui.SameLine();
            ImGui.SliderFloat("X", ref s_horizontalFloat0, 0.0f, 5.0f); ImGui.SameLine();
            ImGui.SliderFloat("Y", ref s_horizontalFloat1, 0.0f, 5.0f); ImGui.SameLine();
            ImGui.SliderFloat("Z", ref s_horizontalFloat2, 0.0f, 5.0f);

            ImGui.Text("Lists:");
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) ImGui.SameLine();
                ImGui.PushID(i);
                ImGui.ListBox("", ref s_horizontalListSelection[i], horizontalItems, horizontalItems.Length);
                ImGui.PopID();
            }
            ImGui.PopItemWidth();

            // Dummy
            ImVec2 buttonSize = new(40, 40);
            ImGui.Button("A", buttonSize); ImGui.SameLine();
            ImGui.Dummy(buttonSize); ImGui.SameLine();
            ImGui.Button("B", buttonSize);

            // Manually wrapping. Automatic layout wrapping is not provided, so the row break is computed here
            // from the expected position of the next button.
            ImGui.Text("Manual wrapping:");
            ImGuiStyle style = ImGui.GetStyle();
            int buttonsCount = 20;
            float windowVisibleX2 = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            for (int n = 0; n < buttonsCount; n++)
            {
                ImGui.PushID(n);
                ImGui.Button("Box", buttonSize);
                float lastButtonX2 = ImGui.GetItemRectMax().X;
                float nextButtonX2 = lastButtonX2 + style.ItemSpacing.X + buttonSize.X; // Expected position if next button was on same line
                if (n + 1 < buttonsCount && nextButtonX2 < windowVisibleX2)
                    ImGui.SameLine();
                ImGui.PopID();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Groups
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Groups"))
        {
            HelpMarker(
                "BeginGroup() basically locks the horizontal position for new line. "
                + "EndGroup() bundles the whole group so that you can use \"item\" functions such as "
                + "IsItemHovered()/IsItemActive() or SameLine() etc. on the whole group.");
            ImGui.BeginGroup();
            {
                ImGui.BeginGroup();
                ImGui.Button("AAA");
                ImGui.SameLine();
                ImGui.Button("BBB");
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.Button("CCC");
                ImGui.Button("DDD");
                ImGui.EndGroup();
                ImGui.SameLine();
                ImGui.Button("EEE");
                ImGui.EndGroup();
                ImGui.SetItemTooltip("First group hovered");
            }
            // The group size is captured and reused so the following widgets share its footprint.
            ImVec2 size = ImGui.GetItemRectSize();
            float[] values = { 0.5f, 0.20f, 0.80f, 0.60f, 0.25f };
            ImGui.PlotHistogram("##values", values, values.Length, 0, null, 0.0f, 1.0f, size);

            ImGui.Button("ACTION", new ImVec2((size.X - ImGui.GetStyle().ItemSpacing.X) * 0.5f, size.Y));
            ImGui.SameLine();
            ImGui.Button("REACTION", new ImVec2((size.X - ImGui.GetStyle().ItemSpacing.X) * 0.5f, size.Y));
            ImGui.EndGroup();
            ImGui.SameLine();

            ImGui.Button("LEVERAGE\nBUZZWORD", size);
            ImGui.SameLine();

            if (ImGui.BeginListBox("List", size))
            {
                ImGui.Selectable("Selected", true);
                ImGui.Selectable("Not Selected", false);
                ImGui.EndListBox();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Text Baseline Alignment
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Text Baseline Alignment"))
        {
            {
                ImGui.BulletText("Text baseline:");
                ImGui.SameLine(); HelpMarker(
                    "This is testing the vertical alignment that gets applied on text to keep it aligned with widgets. "
                    + "Lines only composed of text or \"small\" widgets use less vertical space than lines with framed widgets.");
                ImGui.Indent();

                ImGui.Text("KO Blahblah"); ImGui.SameLine();
                ImGui.Button("Some framed item"); ImGui.SameLine();
                HelpMarker("Baseline of button will look misaligned with text..");

                // A line that starts with text calls AlignTextToFramePadding() so the text baseline is moved
                // down by FramePadding.y ahead of the upcoming framed widget.
                ImGui.AlignTextToFramePadding();
                ImGui.Text("OK Blahblah"); ImGui.SameLine();
                ImGui.Button("Some framed item##2"); ImGui.SameLine();
                HelpMarker("We call AlignTextToFramePadding() to vertically align the text baseline by +FramePadding.y");

                // SmallButton() uses the same vertical padding as Text.
                ImGui.Button("TEST##1"); ImGui.SameLine();
                ImGui.Text("TEST"); ImGui.SameLine();
                ImGui.SmallButton("TEST##2");

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Text aligned to framed item"); ImGui.SameLine();
                ImGui.Button("Item##1"); ImGui.SameLine();
                ImGui.Text("Item"); ImGui.SameLine();
                ImGui.SmallButton("Item##2"); ImGui.SameLine();
                ImGui.Button("Item##3");

                ImGui.Unindent();
            }

            ImGui.Spacing();

            {
                ImGui.BulletText("Multi-line text:");
                ImGui.Indent();
                ImGui.Text("One\nTwo\nThree"); ImGui.SameLine();
                ImGui.Text("Hello\nWorld"); ImGui.SameLine();
                ImGui.Text("Banana");

                ImGui.Text("Banana"); ImGui.SameLine();
                ImGui.Text("Hello\nWorld"); ImGui.SameLine();
                ImGui.Text("One\nTwo\nThree");

                ImGui.Button("HOP##1"); ImGui.SameLine();
                ImGui.Text("Banana"); ImGui.SameLine();
                ImGui.Text("Hello\nWorld"); ImGui.SameLine();
                ImGui.Text("Banana");

                ImGui.Button("HOP##2"); ImGui.SameLine();
                ImGui.Text("Hello\nWorld"); ImGui.SameLine();
                ImGui.Text("Banana");
                ImGui.Unindent();
            }

            ImGui.Spacing();

            {
                ImGui.BulletText("Misc items:");
                ImGui.Indent();

                // SmallButton() sets FramePadding to zero; the text baseline aligns with the previous Button baseline.
                ImGui.Button("80x80", new ImVec2(80, 80));
                ImGui.SameLine();
                ImGui.Button("50x50", new ImVec2(50, 50));
                ImGui.SameLine();
                ImGui.Button("Button()");
                ImGui.SameLine();
                ImGui.SmallButton("SmallButton()");

                // The node follows a button and has odd indent, so DrawLinesNone disables the hierarchy outline.
                float spacing = ImGui.GetStyle().ItemInnerSpacing.X;
                ImGui.Button("Button##1"); // Will make line higher
                ImGui.SameLine(0.0f, spacing);
                if (ImGui.TreeNodeEx("Node##1", ImGuiTreeNodeFlags.DrawLinesNone))
                {
                    // Placeholder tree data
                    for (int i = 0; i < 6; i++)
                        ImGui.BulletText("Item %d..", i);
                    ImGui.TreePop();
                }

                float padding = (float)(int)(ImGui.GetFontSize() * 1.20f); // Large padding
                ImGui.PushStyleVarY(ImGuiStyleVar.FramePadding, padding);
                ImGui.Button("Button##2");
                ImGui.PopStyleVar();
                ImGui.SameLine(0.0f, spacing);
                if (ImGui.TreeNodeEx("Node##2", ImGuiTreeNodeFlags.DrawLinesNone))
                    ImGui.TreePop();

                // Text nodes are vertically aligned lower so they center with the upcoming widget.
                ImGui.AlignTextToFramePadding();

                // SameLine() after a TreeNode must be called before adding the node contents.
                bool nodeOpen = ImGui.TreeNode("Node##3");
                ImGui.SameLine(0.0f, spacing); ImGui.Button("Button##3");
                if (nodeOpen)
                {
                    // Placeholder tree data
                    for (int i = 0; i < 6; i++)
                        ImGui.BulletText("Item %d..", i);
                    ImGui.TreePop();
                }

                // Bullet
                ImGui.Button("Button##4");
                ImGui.SameLine(0.0f, spacing);
                ImGui.BulletText("Bullet text");

                ImGui.AlignTextToFramePadding();
                ImGui.BulletText("Node");
                ImGui.SameLine(0.0f, spacing); ImGui.Button("Button##5");
                ImGui.Unindent();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Scrolling
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Scrolling"))
        {
            HelpMarker("Use SetScrollHereY() or SetScrollFromPosY() to scroll to a given vertical position.");

            ImGui.Checkbox("Decoration", ref s_scrollEnableExtraDecorations);

            ImGui.PushItemWidth(ImGui.GetFontSize() * 10);
            s_scrollEnableTrack |= ImGui.DragInt("##item", ref s_scrollTrackItem, 0.25f, 0, 99, "Item = %d");
            ImGui.SameLine();
            ImGui.Checkbox("Track", ref s_scrollEnableTrack);

            bool scrollToOff = ImGui.DragFloat("##off", ref s_scrollToOffPx, 1.00f, 0, float.MaxValue, "+%.0f px");
            ImGui.SameLine();
            scrollToOff |= ImGui.Button("Scroll Offset");

            bool scrollToPos = ImGui.DragFloat("##pos", ref s_scrollToPosPx, 1.00f, -10, float.MaxValue, "X/Y = %.0f px");
            ImGui.SameLine();
            scrollToPos |= ImGui.Button("Scroll To Pos");
            ImGui.PopItemWidth();

            if (scrollToOff || scrollToPos)
                s_scrollEnableTrack = false;

            ImGuiStyle style = ImGui.GetStyle();
            float childW = (ImGui.GetContentRegionAvail().X - 4 * style.ItemSpacing.X) / 5;
            if (childW < 1.0f)
                childW = 1.0f;

            ImGui.PushID("##VerticalScrolling");
            string[] names = { "Top", "25%", "Center", "75%", "Bottom" };
            for (int i = 0; i < 5; i++)
            {
                if (i > 0) ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextUnformatted(names[i]);

                ImGuiWindowFlags childFlags = s_scrollEnableExtraDecorations ? ImGuiWindowFlags.MenuBar : ImGuiWindowFlags.None;
                bool childVisible = ImGui.BeginChild($"Child_{i}", new ImVec2(childW, 200.0f), ImGuiChildFlags.Borders, childFlags);
                if (ImGui.BeginMenuBar())
                {
                    ImGui.TextUnformatted("abc");
                    ImGui.EndMenuBar();
                }
                if (scrollToOff)
                    ImGui.SetScrollY(s_scrollToOffPx);
                if (scrollToPos)
                    ImGui.SetScrollFromPosY(ImGui.GetCursorStartPos().Y + s_scrollToPosPx, i * 0.25f);
                if (childVisible)
                {
                    for (int item = 0; item < 100; item++)
                    {
                        if (s_scrollEnableTrack && item == s_scrollTrackItem)
                        {
                            ImGui.TextColored(new ImVec4(1, 1, 0, 1), $"Item {item}");
                            ImGui.SetScrollHereY(i * 0.25f);
                        }
                        else
                        {
                            ImGui.Text($"Item {item}");
                        }
                    }
                }
                float scrollY = ImGui.GetScrollY();
                float scrollMaxY = ImGui.GetScrollMaxY();
                ImGui.EndChild();
                ImGui.Text($"{scrollY:F0}/{scrollMaxY:F0}");
                ImGui.EndGroup();
            }
            ImGui.PopID();

            // Horizontal scroll functions
            ImGui.Spacing();
            HelpMarker(
                "Use SetScrollHereX() or SetScrollFromPosX() to scroll to a given horizontal position.\n\n"
                + "Because the clipping rectangle of most window hides half worth of WindowPadding on the "
                + "left/right, using SetScrollFromPosX(+1) will usually result in clipped text whereas the "
                + "equivalent SetScrollFromPosY(+1) wouldn't.");
            ImGui.PushID("##HorizontalScrolling");
            string[] horizontalNames = { "Left", "25%", "Center", "75%", "Right" };
            for (int i = 0; i < 5; i++)
            {
                float childHeight = ImGui.GetTextLineHeight() + style.ScrollbarSize + style.WindowPadding.Y * 2.0f;
                ImGuiWindowFlags childFlags = ImGuiWindowFlags.HorizontalScrollbar | (s_scrollEnableExtraDecorations ? ImGuiWindowFlags.AlwaysVerticalScrollbar : ImGuiWindowFlags.None);
                uint childId = ImGui.GetID(i);
                bool childVisible = ImGui.BeginChild(childId, new ImVec2(-100, childHeight), ImGuiChildFlags.Borders, childFlags);
                if (scrollToOff)
                    ImGui.SetScrollX(s_scrollToOffPx);
                if (scrollToPos)
                    ImGui.SetScrollFromPosX(ImGui.GetCursorStartPos().X + s_scrollToPosPx, i * 0.25f);
                if (childVisible) // Avoid calling SetScrollHereX when running with culled items
                {
                    for (int item = 0; item < 100; item++)
                    {
                        if (item > 0)
                            ImGui.SameLine();
                        if (s_scrollEnableTrack && item == s_scrollTrackItem)
                        {
                            ImGui.TextColored(new ImVec4(1, 1, 0, 1), "Item %d", item);
                            ImGui.SetScrollHereX(i * 0.25f); // 0.0f:left, 0.5f:center, 1.0f:right
                        }
                        else
                        {
                            ImGui.Text("Item %d", item);
                        }
                    }
                }
                float scrollX = ImGui.GetScrollX();
                float scrollMaxX = ImGui.GetScrollMaxX();
                ImGui.EndChild();
                ImGui.SameLine();
                ImGui.Text("%s\n%.0f/%.0f", horizontalNames[i], scrollX, scrollMaxX);
                ImGui.Spacing();
            }
            ImGui.PopID();

            // Miscellaneous Horizontal Scrolling Demo
            HelpMarker(
                "Horizontal scrolling for a window is enabled via the ImGuiWindowFlags.HorizontalScrollbar flag.\n\n"
                + "You may want to also explicitly specify content width by using SetNextWindowContentWidth() before Begin().");
            ImGui.SliderInt("Lines", ref s_scrollLines, 1, 15);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(2.0f, 1.0f));
            ImVec2 scrollingChildSize = new(0, ImGui.GetFrameHeightWithSpacing() * 7 + 30);
            ImGui.BeginChild("scrolling", scrollingChildSize, ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
            for (int line = 0; line < s_scrollLines; line++)
            {
                // Buttons and SameLine() stand in for a real timeline. A real application would position widgets
                // with SetCursorPos/SetCursorScreenPos, or use the lower-level ImDrawList API.
                int numButtons = 10 + ((line & 1) != 0 ? line * 9 : line * 3);
                float baseW = ImGui.GetFontSize() * 3;
                for (int n = 0; n < numButtons; n++)
                {
                    if (n > 0) ImGui.SameLine();
                    ImGui.PushID(n + line * 1000);
                    string label = (n % 15) == 0 ? "FizzBuzz" : (n % 3) == 0 ? "Fizz" : (n % 5) == 0 ? "Buzz" : $"{n}";
                    float hue = n * 0.05f;
                    ImGui.PushStyleColor(ImGuiCol.Button, ImColor.HSV(hue, 0.6f, 0.6f).Value);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImColor.HSV(hue, 0.7f, 0.7f).Value);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImColor.HSV(hue, 0.8f, 0.8f).Value);
                    ImGui.Button(label, new ImVec2(baseW + MathF.Sin(line + n) * baseW * 0.5f, 0.0f));
                    ImGui.PopStyleColor(3);
                    ImGui.PopID();
                }
            }
            float miscScrollX = ImGui.GetScrollX();
            float miscScrollMaxX = ImGui.GetScrollMaxX();
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            float scrollXDelta = 0.0f;
            ImGui.SmallButton("<<");
            if (ImGui.IsItemActive())
                scrollXDelta = -ImGui.GetIO().DeltaTime * 1000.0f;
            ImGui.SameLine();
            ImGui.Text("Scroll from code"); ImGui.SameLine();
            ImGui.SmallButton(">>");
            if (ImGui.IsItemActive())
                scrollXDelta = +ImGui.GetIO().DeltaTime * 1000.0f;
            ImGui.SameLine();
            ImGui.Text("%.0f/%.0f", miscScrollX, miscScrollMaxX);
            if (scrollXDelta != 0.0f)
            {
                // BeginChild() re-enters the context of the child window created above so it can be scrolled
                // after the cursor has already left it.
                ImGui.BeginChild("scrolling");
                ImGui.SetScrollX(ImGui.GetScrollX() + scrollXDelta);
                ImGui.EndChild();
            }
            ImGui.Spacing();

            ImGui.Checkbox("Show Horizontal contents size demo window", ref s_scrollShowContentsSizeWindow);

            if (s_scrollShowContentsSizeWindow)
            {
                if (s_scrollContentsSizeExplicit)
                    ImGui.SetNextWindowContentSize(new ImVec2(s_scrollContentsSizeX, 0.0f));
                ImGui.Begin("Horizontal contents size demo window", ref s_scrollShowContentsSizeWindow, s_scrollContentsSizeShowHScrollbar ? ImGuiWindowFlags.HorizontalScrollbar : ImGuiWindowFlags.None);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(2, 0));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(2, 0));
                HelpMarker(
                    "Test how different widgets react and impact the work rectangle growing when horizontal scrolling is enabled.\n\n"
                    + "Use 'Metrics->Tools->Show windows rectangles' to visualize rectangles.");
                ImGui.Checkbox("H-scrollbar", ref s_scrollContentsSizeShowHScrollbar);
                ImGui.Checkbox("Button", ref s_scrollContentsSizeShowButton);            // Will grow contents size (unless explicitly overwritten)
                ImGui.Checkbox("Tree nodes", ref s_scrollContentsSizeShowTreeNodes);    // Will grow contents size and display highlight over full width
                ImGui.Checkbox("Text wrapped", ref s_scrollContentsSizeShowTextWrapped);// Will grow and use contents size
                ImGui.Checkbox("Columns", ref s_scrollContentsSizeShowColumns);          // Will use contents size
                ImGui.Checkbox("Tab bar", ref s_scrollContentsSizeShowTabBar);          // Will use contents size
                ImGui.Checkbox("Child", ref s_scrollContentsSizeShowChild);              // Will grow and use contents size
                ImGui.Checkbox("Explicit content size", ref s_scrollContentsSizeExplicit);
                ImGui.Text("Scroll %.1f/%.1f %.1f/%.1f", ImGui.GetScrollX(), ImGui.GetScrollMaxX(), ImGui.GetScrollY(), ImGui.GetScrollMaxY());
                if (s_scrollContentsSizeExplicit)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.CalcTextSize("123456").X);
                    ImGui.DragFloat("##csx", ref s_scrollContentsSizeX);
                    ImVec2 p = ImGui.GetCursorScreenPos();
                    ImDrawList drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(p, new ImVec2(p.X + 10, p.Y + 10), 0xFFFFFFFF);
                    drawList.AddRectFilled(new ImVec2(p.X + s_scrollContentsSizeX - 10, p.Y), new ImVec2(p.X + s_scrollContentsSizeX, p.Y + 10), 0xFFFFFFFF);
                    ImGui.Dummy(new ImVec2(0, 10));
                }
                ImGui.PopStyleVar(2);
                ImGui.Separator();
                if (s_scrollContentsSizeShowButton)
                    ImGui.Button("this is a 300-wide button", new ImVec2(300, 0));
                if (s_scrollContentsSizeShowTreeNodes)
                {
                    bool open = true;
                    if (ImGui.TreeNode("this is a tree node"))
                    {
                        if (ImGui.TreeNode("another one of those tree node..."))
                        {
                            ImGui.Text("Some tree contents");
                            ImGui.TreePop();
                        }
                        ImGui.TreePop();
                    }
                    ImGui.CollapsingHeader("CollapsingHeader", ref open);
                }
                if (s_scrollContentsSizeShowTextWrapped)
                    ImGui.TextWrapped("This text should automatically wrap on the edge of the work rectangle.");
                if (s_scrollContentsSizeShowColumns)
                {
                    ImGui.Text("Tables:");
                    if (ImGui.BeginTable("table", 4, ImGuiTableFlags.Borders))
                    {
                        for (int n = 0; n < 4; n++)
                        {
                            ImGui.TableNextColumn();
                            ImGui.Text("Width %.2f", ImGui.GetContentRegionAvail().X);
                        }
                        ImGui.EndTable();
                    }
                    ImGui.Text("Columns:");
                    ImGui.Columns(4);
                    for (int n = 0; n < 4; n++)
                    {
                        ImGui.Text("Width %.2f", ImGui.GetColumnWidth());
                        ImGui.NextColumn();
                    }
                    ImGui.Columns(1);
                }
                if (s_scrollContentsSizeShowTabBar && ImGui.BeginTabBar("Hello"))
                {
                    if (ImGui.BeginTabItem("OneOneOne")) { ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("TwoTwoTwo")) { ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("ThreeThreeThree")) { ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("FourFourFour")) { ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
                if (s_scrollContentsSizeShowChild)
                {
                    ImGui.BeginChild("child", new ImVec2(0, 0), ImGuiChildFlags.Borders);
                    ImGui.EndChild();
                }
                ImGui.End();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Text Clipping
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Text Clipping"))
        {
            DragFloat2("size", ref s_clipSize, 0.5f, 1.0f, 200.0f, "%.0f");
            ImGui.TextWrapped("(Click and drag to scroll)");

            HelpMarker(
                "(Left) Using ImGui::PushClipRect():\n"
                + "Will alter ImGui hit-testing logic + ImDrawList rendering.\n"
                + "(use this if you want your clipping rectangle to affect interactions)\n\n"
                + "(Center) Using ImDrawList::PushClipRect():\n"
                + "Will alter ImDrawList rendering only.\n"
                + "(use this as a shortcut if you are only using ImDrawList calls)\n\n"
                + "(Right) Using ImDrawList::AddText() with a fine ClipRect:\n"
                + "Will alter only this specific ImDrawList::AddText() rendering.\n"
                + "This is often used internally to avoid altering the clipping rectangle and minimize draw calls.");

            for (int n = 0; n < 3; n++)
            {
                if (n > 0)
                    ImGui.SameLine();

                ImGui.PushID(n);
                ImGui.InvisibleButton("##canvas", s_clipSize);
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    s_clipOffset.X += ImGui.GetIO().MouseDelta.X;
                    s_clipOffset.Y += ImGui.GetIO().MouseDelta.Y;
                }
                ImGui.PopID();

                if (!ImGui.IsItemVisible())
                    continue;

                ImVec2 p0 = ImGui.GetItemRectMin();
                ImVec2 p1 = ImGui.GetItemRectMax();
                string textStr = "Line 1 hello\nLine 2 clip me!";
                ImVec2 textPos = new(p0.X + s_clipOffset.X, p0.Y + s_clipOffset.Y);
                ImDrawList drawList = ImGui.GetWindowDrawList();

                switch (n)
                {
                    case 0:
                        ImGui.PushClipRect(p0, p1, true);
                        drawList.AddRectFilled(p0, p1, 0xFF785A5A);
                        drawList.AddText(textPos, 0xFFFFFFFF, textStr);
                        ImGui.PopClipRect();
                        break;
                    case 1:
                        drawList.PushClipRect(p0, p1, true);
                        drawList.AddRectFilled(p0, p1, 0xFF785A5A);
                        drawList.AddText(textPos, 0xFFFFFFFF, textStr);
                        drawList.PopClipRect();
                        break;
                    case 2:
                        drawList.AddRectFilled(p0, p1, 0xFF785A5A);
                        ImFont? activeFont = ImGui.GetFont();
                        if (activeFont != null)
                            drawList.AddText(activeFont, ImGui.GetFontSize(), textPos, 0xFFFFFFFF, textStr, 0.0f, new ImVec4(p0.X, p0.Y, p1.X, p1.Y));
                        break;
                }
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Overlap Mode
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Overlap Mode"))
        {
            HelpMarker(
                "Hit-testing is by default performed in item submission order, which generally is perceived as 'back-to-front'.\n\n"
                + "By using SetNextItemAllowOverlap() you can notify that an item may be overlapped by another. "
                + "Doing so alters the hovering logic: items using AllowOverlap mode requires an extra frame to accept hovered state.");
            ImGui.Checkbox("Enable AllowOverlap", ref s_layoutEnableAllowOverlap);

            ImVec2 button1Pos = ImGui.GetCursorScreenPos();
            ImVec2 button2Pos = new(button1Pos.X + 50.0f, button1Pos.Y + 50.0f);
            if (s_layoutEnableAllowOverlap)
                ImGui.SetNextItemAllowOverlap();
            ImGui.Button("Button 1", new ImVec2(80, 80));
            ImGui.SetCursorScreenPos(button2Pos);
            ImGui.Button("Button 2", new ImVec2(80, 80));

            // Width-spanning items typically use this. Selectable() has a dedicated
            // ImGuiSelectableFlags.AllowOverlap shortcut; SetNextItemAllowOverlap() is used here for demonstration.
            if (s_layoutEnableAllowOverlap)
                ImGui.SetNextItemAllowOverlap();
            ImGui.Selectable("Some Selectable", false);
            ImGui.SameLine();
            ImGui.SmallButton("++");

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowColumns()

    /// <summary>Selected row index of the "With border" columns demo table.</summary>
    private static int s_columnsSelected = -1;

    /// <summary>Whether the "Borders" columns demo draws horizontal separators.</summary>
    private static bool s_columnsHBorders = true;

    /// <summary>Whether the "Borders" columns demo draws vertical separators.</summary>
    private static bool s_columnsVBorders = true;

    /// <summary>Column count of the "Borders" columns demo, clamped to the canonical [2, 10] range.</summary>
    private static int s_columnsCount = 4;

    /// <summary>First editable float of the "Mixed items" columns demo.</summary>
    private static float s_columnsFoo = 1.0f;

    /// <summary>Second editable float of the "Mixed items" columns demo.</summary>
    private static float s_columnsBar = 1.0f;

    /// <summary>
    /// Demonstrates the legacy <c>Columns()</c> API with basic, bordered, mixed-item, word-wrapping,
    /// and tree-content layouts.
    /// </summary>
    public static void DemoWindowColumns()
    {
        bool open = ImGui.TreeNode("Legacy Columns API");
        ImGui.SameLine();
        HelpMarker("Columns() is an old API! Prefer using the more flexible and powerful BeginTable() API!");
        if (!open)
            return;

        // Basic columns
        if (ImGui.TreeNode("Basic"))
        {
            ImGui.Text("Without border:");
            ImGui.Columns(3, "mycolumns3", false); // 3-ways, no border
            ImGui.Separator();
            for (int n = 0; n < 14; n++)
            {
                string label = $"Item {n}";
                if (ImGui.Selectable(label)) { }
                ImGui.NextColumn();
            }
            ImGui.Columns(1);
            ImGui.Separator();

            ImGui.Text("With border:");
            ImGui.Columns(4, "mycolumns"); // 4-ways, with border
            ImGui.Separator();
            ImGui.Text("ID"); ImGui.NextColumn();
            ImGui.Text("Name"); ImGui.NextColumn();
            ImGui.Text("Path"); ImGui.NextColumn();
            ImGui.Text("Hovered"); ImGui.NextColumn();
            ImGui.Separator();
            string[] names = { "One", "Two", "Three" };
            string[] paths = { "/path/one", "/path/two", "/path/three" };
            for (int i = 0; i < 3; i++)
            {
                string label = $"{i:D4}";
                if (ImGui.Selectable(label, s_columnsSelected == i, ImGuiSelectableFlags.SpanAllColumns))
                    s_columnsSelected = i;
                bool hovered = ImGui.IsItemHovered();
                ImGui.NextColumn();
                ImGui.Text(names[i]); ImGui.NextColumn();
                ImGui.Text(paths[i]); ImGui.NextColumn();
                ImGui.Text("%d", hovered ? 1 : 0); ImGui.NextColumn();
            }
            ImGui.Columns(1);
            ImGui.Separator();
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Borders"))
        {
            const int linesCount = 3;
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            ImGui.DragInt("##columns_count", ref s_columnsCount, 0.1f, 2, 10, "%d columns");
            if (s_columnsCount < 2)
                s_columnsCount = 2;
            ImGui.SameLine();
            ImGui.Checkbox("horizontal", ref s_columnsHBorders);
            ImGui.SameLine();
            ImGui.Checkbox("vertical", ref s_columnsVBorders);
            ImGui.Columns(s_columnsCount, null, s_columnsVBorders);
            for (int i = 0; i < s_columnsCount * linesCount; i++)
            {
                if (s_columnsHBorders && ImGui.GetColumnIndex() == 0)
                    ImGui.Separator();
                ImGui.PushID(i);
                char c = (char)('a' + i);
                ImGui.Text("%c%c%c", c, c, c);
                ImGui.Text("Width %.2f", ImGui.GetColumnWidth());
                ImGui.Text("Avail %.2f", ImGui.GetContentRegionAvail().X);
                ImGui.Text("Offset %.2f", ImGui.GetColumnOffset());
                ImGui.Text("Long text that is likely to clip");
                ImGui.Button("Button", new ImVec2(-float.Epsilon, 0.0f));
                ImGui.PopID();
                ImGui.NextColumn();
            }
            ImGui.Columns(1);
            if (s_columnsHBorders)
                ImGui.Separator();
            ImGui.TreePop();
        }

        // Create multiple items in a same cell before switching to next column
        if (ImGui.TreeNode("Mixed items"))
        {
            ImGui.Columns(3, "mixed");
            ImGui.Separator();

            ImGui.Text("Hello");
            ImGui.Button("Banana");
            ImGui.NextColumn();

            ImGui.Text("ImGui");
            ImGui.Button("Apple");
            ImGui.InputFloat("red", ref s_columnsFoo, 0.05f, 0, "%.3f");
            ImGui.Text("An extra line here.");
            ImGui.NextColumn();

            ImGui.Text("Sailor");
            ImGui.Button("Corniflower");
            ImGui.InputFloat("blue", ref s_columnsBar, 0.05f, 0, "%.3f");
            ImGui.NextColumn();

            if (ImGui.CollapsingHeader("Category A")) { ImGui.Text("Blah blah blah"); }
            ImGui.NextColumn();
            if (ImGui.CollapsingHeader("Category B")) { ImGui.Text("Blah blah blah"); }
            ImGui.NextColumn();
            if (ImGui.CollapsingHeader("Category C")) { ImGui.Text("Blah blah blah"); }
            ImGui.NextColumn();
            ImGui.Columns(1);
            ImGui.Separator();
            ImGui.TreePop();
        }

        // Word wrapping
        if (ImGui.TreeNode("Word-wrapping"))
        {
            ImGui.Columns(2, "word-wrapping");
            ImGui.Separator();
            ImGui.TextWrapped("The quick brown fox jumps over the lazy dog.");
            ImGui.TextWrapped("Hello Left");
            ImGui.NextColumn();
            ImGui.TextWrapped("The quick brown fox jumps over the lazy dog.");
            ImGui.TextWrapped("Hello Right");
            ImGui.Columns(1);
            ImGui.Separator();
            ImGui.TreePop();
        }

        // Horizontal scrolling across a wide, vertically clipped region.
        if (ImGui.TreeNode("Horizontal Scrolling"))
        {
            ImGui.SetNextWindowContentSize(new ImVec2(1500.0f, 0.0f));
            ImVec2 childSize = new(0, ImGui.GetFontSize() * 20.0f);
            if (ImGui.BeginChild("##ScrollingRegion", childSize, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Columns(10);

                // The clipper bounds the rows submitted to the visible region of the 2000-line list.
                int itemsCount = 2000;
                ImGuiListClipper clipper = new();
                clipper.Begin(itemsCount);
                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        for (int j = 0; j < 10; j++)
                        {
                            ImGui.Text($"Line {i} Column {j}...");
                            ImGui.NextColumn();
                        }
                }
                ImGui.Columns(1);
            }
            ImGui.EndChild();
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Tree"))
        {
            ImGui.Columns(2, "tree", true);
            for (int x = 0; x < 3; x++)
            {
                bool open1 = ImGui.TreeNode(x, "Node%d", x);
                ImGui.NextColumn();
                ImGui.Text("Node contents");
                ImGui.NextColumn();
                if (open1)
                {
                    for (int y = 0; y < 3; y++)
                    {
                        bool open2 = ImGui.TreeNode(y, "Node%d.%d", x, y);
                        ImGui.NextColumn();
                        ImGui.Text("Node contents");
                        if (open2)
                        {
                            ImGui.Text("Even more contents");
                            if (ImGui.TreeNode("Tree in column"))
                            {
                                ImGui.Text("The quick brown fox jumps over the lazy dog");
                                ImGui.TreePop();
                            }
                        }
                        ImGui.NextColumn();
                        if (open2)
                            ImGui.TreePop();
                    }
                    ImGui.TreePop();
                }
            }
            ImGui.Columns(1);
            ImGui.TreePop();
        }

        ImGui.TreePop();
    }

    #endregion

    #region Popups & Modals (Lines 5507-5969)

    private static int s_popupSelectedFish = -1;
    private static readonly string[] s_popupFishNames = { "Bream", "Haddock", "Mackerel", "Pollock", "Tilefish" };
    private static readonly bool[] s_popupFishToggles = { true, false, false, false, false };

    private static int s_contextSelected = -1;
    private static float s_contextValue = 0.5f;
    private static string s_contextName = "Label1";

    private static bool s_modalDontAsk = false;
    private static int s_modalComboItem = 1;
    private static ImVec4 s_modalColor = new(0.4f, 0.7f, 0.0f, 0.5f);

    /// <summary>
    /// Demonstrates popups, toggle popups, context menus, and modal dialogs.
    /// </summary>
    public static void DemoWindowPopups()
    {
        if (!ImGui.CollapsingHeader("Popups & Modal windows"))
            return;

        // -------------------------------------------------------------------------
        // Popups
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Popups"))
        {
            ImGui.TextWrapped("When a popup is active, it inhibits interacting with windows behind it. Clicking outside closes it.");

            if (ImGui.Button("Select.."))
                ImGui.OpenPopup("my_select_popup");
            ImGui.SameLine();
            ImGui.TextUnformatted(s_popupSelectedFish == -1 ? "<None>" : s_popupFishNames[s_popupSelectedFish]);
            if (ImGui.BeginPopup("my_select_popup"))
            {
                ImGui.SeparatorText("Aquarium");
                for (int i = 0; i < s_popupFishNames.Length; i++)
                {
                    if (ImGui.Selectable(s_popupFishNames[i]))
                        s_popupSelectedFish = i;
                }
                ImGui.EndPopup();
            }

            if (ImGui.Button("Toggle.."))
                ImGui.OpenPopup("my_toggle_popup");
            if (ImGui.BeginPopup("my_toggle_popup"))
            {
                for (int i = 0; i < s_popupFishNames.Length; i++)
                    ImGui.MenuItem(s_popupFishNames[i], "", ref s_popupFishToggles[i]);
                if (ImGui.BeginMenu("Sub-menu"))
                {
                    ImGui.MenuItem("Click me");
                    ImGui.EndMenu();
                }
                ImGui.Separator();
                ImGui.Text("Tooltip here");
                ImGui.SetItemTooltip("I am a tooltip over a popup");

                // Popups may be stacked: a popup opened from within another popup is submitted at its own level.
                if (ImGui.Button("Stacked Popup"))
                    ImGui.OpenPopup("another popup");
                if (ImGui.BeginPopup("another popup"))
                {
                    for (int i = 0; i < s_popupFishNames.Length; i++)
                        ImGui.MenuItem(s_popupFishNames[i], "", ref s_popupFishToggles[i]);
                    if (ImGui.BeginMenu("Sub-menu"))
                    {
                        ImGui.MenuItem("Click me");
                        if (ImGui.Button("Stacked Popup"))
                            ImGui.OpenPopup("another popup");
                        if (ImGui.BeginPopup("another popup"))
                        {
                            ImGui.Text("I am the last one here.");
                            ImGui.EndPopup();
                        }
                        ImGui.EndMenu();
                    }
                    ImGui.EndPopup();
                }
                ImGui.EndPopup();
            }

            if (ImGui.Button("With a menu.."))
                ImGui.OpenPopup("my_file_popup");
            if (ImGui.BeginPopup("my_file_popup", ImGuiWindowFlags.MenuBar))
            {
                if (ImGui.BeginMenuBar())
                {
                    if (ImGui.BeginMenu("File"))
                    {
                        ShowExampleMenuFile();
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu("Edit"))
                    {
                        ImGui.MenuItem("Dummy");
                        ImGui.EndMenu();
                    }
                    ImGui.EndMenuBar();
                }
                ImGui.Text("Hello from popup!");
                ImGui.Button("This is a dummy button..");
                ImGui.EndPopup();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Context menus
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Context menus"))
        {
            HelpMarker("\"Context\" functions associate a Popup to a given Item or Window identifier.");

            // Example 1: Context menu on selectable items
            string[] contextItems = { "Label1", "Label2", "Label3", "Label4", "Label5" };
            for (int n = 0; n < contextItems.Length; n++)
            {
                if (ImGui.Selectable(contextItems[n], s_contextSelected == n))
                    s_contextSelected = n;
                if (ImGui.BeginPopupContextItem())
                {
                    s_contextSelected = n;
                    ImGui.Text($"This is a popup for \"{contextItems[n]}\"!");
                    if (ImGui.Button("Close"))
                        ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
                ImGui.SetItemTooltip("Right-click to open popup");
            }

            // Example 2: Context menu with explicit ID on text
            HelpMarker("Text() elements don't have stable identifiers so we need to provide one.");
            ImGui.Text($"Value = {s_contextValue:F3} <-- (1) right-click this text");
            if (ImGui.BeginPopupContextItem("my popup"))
            {
                if (ImGui.Selectable("Set to zero")) s_contextValue = 0.0f;
                if (ImGui.Selectable("Set to PI")) s_contextValue = MathF.PI;
                ImGui.SetNextItemWidth(-float.Epsilon);
                ImGui.DragFloat("##Value", ref s_contextValue, 0.1f, 0.0f, 0.0f);
                ImGui.EndPopup();
            }

            ImGui.Text("(2) Or right-click this text");
            ImGui.OpenPopupOnItemClick("my popup", ImGuiPopupFlags.MouseButtonRight);

            if (ImGui.Button("(3) Or click this button"))
                ImGui.OpenPopup("my popup");

            // Example 3: Changing label with stable ID
            HelpMarker("Showcase using a popup ID linked to item ID, with the item having a changing label + stable ID using the ### operator.");
            string btnLabel = $"Button: {s_contextName}###Button";
            ImGui.Button(btnLabel);
            if (ImGui.BeginPopupContextItem())
            {
                ImGui.Text("Edit name:");
                ImGui.InputText("##edit", ref s_contextName);
                if (ImGui.Button("Close"))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.SameLine(); ImGui.Text("(<-- right-click here)");

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Modals
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Modals"))
        {
            ImGui.TextWrapped("Modal windows block interaction outside until dismissed.");

            if (ImGui.Button("Delete.."))
                ImGui.OpenPopup("Delete?");

            ImVec2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new ImVec2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal("Delete?", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("All those beautiful files will be deleted.\nThis operation cannot be undone!");
                ImGui.Separator();

                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(0, 0));
                ImGui.Checkbox("Don't ask me next time", ref s_modalDontAsk);
                ImGui.PopStyleVar();

                if (ImGui.Button("OK", new ImVec2(120, 0)))
                    ImGui.CloseCurrentPopup();
                ImGui.SetItemDefaultFocus();
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new ImVec2(120, 0)))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            if (ImGui.Button("Stacked modals.."))
                ImGui.OpenPopup("Stacked 1");

            if (ImGui.BeginPopupModal("Stacked 1", ImGuiWindowFlags.MenuBar))
            {
                if (ImGui.BeginMenuBar())
                {
                    if (ImGui.BeginMenu("File"))
                    {
                        if (ImGui.MenuItem("Some menu item")) { }
                        ImGui.EndMenu();
                    }
                    ImGui.EndMenuBar();
                }
                ImGui.Text("Hello from Stacked The First\nUsing style.Colors[ImGuiCol_ModalWindowDimBg] behind it.");

                string[] comboItems = { "aaaa", "bbbb", "cccc", "dddd", "eeee" };
                ImGui.Combo("Combo", ref s_modalComboItem, comboItems);
                ColorEdit4("Color", ref s_modalColor);

                if (ImGui.Button("Add another modal.."))
                    ImGui.OpenPopup("Stacked 2");

                bool modal2Open = true;
                if (ImGui.BeginPopupModal("Stacked 2", ref modal2Open))
                {
                    ImGui.Text("Hello from Stacked The Second!");
                    ColorEdit4("Color", ref s_modalColor);
                    if (ImGui.Button("Close"))
                        ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }

                if (ImGui.Button("Close"))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Menus inside a regular window
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Menus inside a regular window"))
        {
            ImGui.TextWrapped("Below we are testing adding menu items to a regular window.");
            ImGui.Separator();
            ImGui.MenuItem("Menu item", "Ctrl+M");
            if (ImGui.BeginMenu("Menu inside a regular window"))
            {
                ShowExampleMenuFile();
                ImGui.EndMenu();
            }
            ImGui.Separator();
            ImGui.TreePop();
        }
    }

    #endregion

    #region Tables & Columns (Lines 5970-7874)

    private sealed class TableDemoItem
    {
        internal int ID { get; set; }
        internal string Name { get; set; } = string.Empty;
        internal int Quantity { get; set; }
    }

    /// <summary>
    /// Column user identifiers handed to <c>TableSetupColumn</c> so that the sort comparator can tell
    /// the demonstration columns apart through <c>ColumnUserID</c>. Mirrors the canonical
    /// <c>MyItemColumnID</c> enumeration.
    /// </summary>
    private enum TableDemoColumnId : uint
    {
        ID = 0,
        Name = 1,
        Action = 2,
        Quantity = 3,
        Description = 4,
    }

    /// <summary>One entry of the dummy file-system rendered by the "Tree view" table subsection.</summary>
    /// <remarks>
    /// <see cref="ChildIdx"/> and <see cref="ChildCount"/> index into
    /// <see cref="ImGuiDemo.s_tableTreeNodes"/> and are negative for leaf entries, exactly as in the
    /// canonical <c>MyTreeNode</c> structure.
    /// </remarks>
    private sealed class TableDemoTreeNode
    {
        internal string Name { get; set; } = string.Empty;
        internal string Type { get; set; } = string.Empty;
        internal int Size { get; set; }
        internal int ChildIdx { get; set; }
        internal int ChildCount { get; set; }
    }

    /// <summary>
    /// Item names shared by the "Sorting" and "Advanced" table subsections, matching the canonical
    /// <c>template_items_names</c> array.
    /// </summary>
    private static readonly string[] s_templateNames =
    {
        "Banana", "Apple", "Cherry", "Watermelon", "Grapefruit", "Strawberry", "Mango",
        "Kiwi", "Orange", "Pineapple", "Blueberry", "Plum", "Coconut", "Pear", "Apricot",
    };
    private static List<TableDemoItem>? s_tableItems;
    private static ImGuiTableFlags s_tableSortingFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti | ImGuiTableFlags.RowBg |
        ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV | ImGuiTableFlags.NoBordersInBody |
        ImGuiTableFlags.ScrollY;

    private static ImGuiTableFlags s_tableBordersFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg;
    private static bool s_tableDisableIndent = false;

    /// <summary>Contents kind submitted by the rows of the "Borders, background" table: plain text.</summary>
    private const int TableBordersContentsText = 0;

    /// <summary>Contents kind submitted by the rows of the "Borders, background" table: full-width button.</summary>
    private const int TableBordersContentsFillButton = 1;

    /// <summary>Whether the "Borders, background" table submits visible column headers.</summary>
    private static bool s_tableBordersDisplayHeaders = false;

    /// <summary>Selected contents kind of the "Borders, background" table.</summary>
    private static int s_tableBordersContentsType = TableBordersContentsText;

    /// <summary>Flags of the "Resizable, stretch" table.</summary>
    private static ImGuiTableFlags s_tableStretchFlags =
        ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersOuter |
        ImGuiTableFlags.BordersV | ImGuiTableFlags.ContextMenuInBody;

    /// <summary>Flags of the "Resizable, fixed" table.</summary>
    private static ImGuiTableFlags s_tableFixedFlags =
        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersOuter |
        ImGuiTableFlags.BordersV | ImGuiTableFlags.ContextMenuInBody;

    /// <summary>Flags shared by the two "Resizable, mixed" tables.</summary>
    private static ImGuiTableFlags s_tableMixedFlags =
        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders |
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable;

    /// <summary>Flags of the "Reorderable, hideable, with headers" table.</summary>
    private static ImGuiTableFlags s_tableReorderableFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable |
        ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV;

    /// <summary>Flags of the first "Padding" table, which exercises per-edge padding.</summary>
    private static ImGuiTableFlags s_tablePaddingFlags = ImGuiTableFlags.BordersV;

    /// <summary>Whether the first "Padding" table submits visible column headers.</summary>
    private static bool s_tablePaddingShowHeaders = false;

    /// <summary>Flags of the second "Padding" table, which varies style.CellPadding.</summary>
    private static ImGuiTableFlags s_tablePaddingFlags2 = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg;

    /// <summary>Cell padding applied by the second "Padding" table, in pixels.</summary>
    private static readonly float[] s_tablePaddingCellPadding = { 0.0f, 0.0f };

    /// <summary>Whether the second "Padding" table draws the widget frame background.</summary>
    private static bool s_tablePaddingShowWidgetFrameBg = true;

    /// <summary>Editable text of each 3x5 cell of the second "Padding" table.</summary>
    private static readonly string[] s_tablePaddingTextBufs = CreateTablePaddingTextBufs();

    /// <summary>Flags shared by the four "Sizing policies" tables.</summary>
    private static ImGuiTableFlags s_tableSizingFlags =
        ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ContextMenuInBody;

    /// <summary>Sizing policy selected for each of the four "Sizing policies" tables.</summary>
    private static readonly ImGuiTableFlags[] s_tableSizingPolicies =
    {
        ImGuiTableFlags.SizingFixedFit,
        ImGuiTableFlags.SizingFixedSame,
        ImGuiTableFlags.SizingStretchProp,
        ImGuiTableFlags.SizingStretchSame,
    };

    /// <summary>Contents kind of the advanced "Sizing policies" table: available width text.</summary>
    private const int TableSizingContentsShowWidth = 0;

    /// <summary>Contents kind of the advanced "Sizing policies" table: short label text.</summary>
    private const int TableSizingContentsShortText = 1;

    /// <summary>Contents kind of the advanced "Sizing policies" table: two-line long text.</summary>
    private const int TableSizingContentsLongText = 2;

    /// <summary>Contents kind of the advanced "Sizing policies" table: button.</summary>
    private const int TableSizingContentsButton = 3;

    /// <summary>Contents kind of the advanced "Sizing policies" table: full-width button.</summary>
    private const int TableSizingContentsFillButton = 4;

    /// <summary>Contents kind of the advanced "Sizing policies" table: editable text input.</summary>
    private const int TableSizingContentsInputText = 5;

    /// <summary>Flags of the advanced "Sizing policies" table.</summary>
    private static ImGuiTableFlags s_tableSizingAdvancedFlags =
        ImGuiTableFlags.ScrollY | ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;

    /// <summary>Selected contents kind of the advanced "Sizing policies" table.</summary>
    private static int s_tableSizingAdvancedContentsType = TableSizingContentsShowWidth;

    /// <summary>Column count of the advanced "Sizing policies" table.</summary>
    private static int s_tableSizingAdvancedColumnCount = 3;

    /// <summary>Editable text used by the "InputText" contents of the advanced "Sizing policies" table.</summary>
    private static string s_tableSizingAdvancedTextBuf = string.Empty;

    /// <summary>Flags of the "Vertical scrolling, with clipping" table.</summary>
    private static ImGuiTableFlags s_tableScrollYFlags =
        ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter |
        ImGuiTableFlags.BordersV | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable;

    /// <summary>Flags of the "Horizontal scrolling" table.</summary>
    private static ImGuiTableFlags s_tableScrollXFlags =
        ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg |
        ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV | ImGuiTableFlags.Resizable |
        ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable;

    /// <summary>Frozen column count of the "Horizontal scrolling" table.</summary>
    private static int s_tableScrollXFreezeCols = 1;

    /// <summary>Frozen row count of the "Horizontal scrolling" table.</summary>
    private static int s_tableScrollXFreezeRows = 1;

    /// <summary>Flags of the "Stretch + ScrollX" table.</summary>
    private static ImGuiTableFlags s_tableScrollXStretchFlags =
        ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
        ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.ContextMenuInBody;

    /// <summary>Inner width passed to the "Stretch + ScrollX" table.</summary>
    private static float s_tableScrollXInnerWidth = 1000.0f;

    /// <summary>Column names shared by the "Columns flags" tables.</summary>
    private static readonly string[] s_tableColumnNames = { "One", "Two", "Three" };

    /// <summary>Per-column input flags of the "Columns flags" demonstration.</summary>
    private static readonly ImGuiTableColumnFlags[] s_tableColumnsFlags =
    {
        ImGuiTableColumnFlags.DefaultSort,
        ImGuiTableColumnFlags.None,
        ImGuiTableColumnFlags.DefaultHide,
    };

    /// <summary>Per-column status flags reported by TableGetColumnFlags() for the "Columns flags" demonstration.</summary>
    private static readonly ImGuiTableColumnFlags[] s_tableColumnsFlagsOut = new ImGuiTableColumnFlags[3];

    /// <summary>Flags of the first "Columns widths" table.</summary>
    private static ImGuiTableFlags s_tableWidthsFlags1 = ImGuiTableFlags.Borders | ImGuiTableFlags.NoBordersInBodyUntilResize;

    /// <summary>Flags of the second "Columns widths" table.</summary>
    private static ImGuiTableFlags s_tableWidthsFlags2 = ImGuiTableFlags.None;

    /// <summary>Flags of the first "Outer size" table, which exposes how NoHostExtendX and NoHostExtendY interact.</summary>
    private static ImGuiTableFlags s_tableOuterSizeFlags =
        ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.ContextMenuInBody |
        ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX;

    /// <summary>Flags of the "Background color" table, limited to row and cell background decorations.</summary>
    private static ImGuiTableFlags s_tableBackgroundFlags = ImGuiTableFlags.RowBg;

    /// <summary>Row background kind of the "Background color" table: none, flat, or gradient.</summary>
    private static int s_tableBackgroundRowType = 1;

    /// <summary>Row background target of the "Background color" table: RowBg0 or RowBg1.</summary>
    private static int s_tableBackgroundRowTarget = 1;

    /// <summary>Cell background kind of the "Background color" table: none or blue.</summary>
    private static int s_tableBackgroundCellType = 1;

    /// <summary>
    /// Base tree-node flags of the "Tree view" table subsection. The checkboxes below the subsection
    /// header toggle the span and draw-line bits demonstrated there.
    /// </summary>
    private static ImGuiTreeNodeFlags s_tableTreeViewFlags =
        ImGuiTreeNodeFlags.SpanAllColumns | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.DrawLinesFull;

    /// <summary>Flags of the "Tree view" table, which uses vertical borders and a borderless body.</summary>
    private static ImGuiTableFlags s_tableTreeViewTableFlags =
        ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.Resizable |
        ImGuiTableFlags.RowBg | ImGuiTableFlags.NoBordersInBody;

    /// <summary>
    /// Dummy file-system entries rendered by the "Tree view" table subsection. Each folder entry spans
    /// a contiguous run of <see cref="TableDemoTreeNode.ChildCount"/> entries starting at
    /// <see cref="TableDemoTreeNode.ChildIdx"/>.
    /// </summary>
    private static readonly TableDemoTreeNode[] s_tableTreeNodes =
    {
        new() { Name = "Root with Long Name",           Type = "Folder",      Size = -1,     ChildIdx = 1, ChildCount = 3 },
        new() { Name = "Music",                         Type = "Folder",      Size = -1,     ChildIdx = 4, ChildCount = 2 },
        new() { Name = "Textures",                      Type = "Folder",      Size = -1,     ChildIdx = 6, ChildCount = 3 },
        new() { Name = "desktop.ini",                   Type = "System file", Size = 1024,   ChildIdx = -1, ChildCount = -1 },
        new() { Name = "File1_a.wav",                   Type = "Audio file",  Size = 123000, ChildIdx = -1, ChildCount = -1 },
        new() { Name = "File1_b.wav",                   Type = "Audio file",  Size = 456000, ChildIdx = -1, ChildCount = -1 },
        new() { Name = "Image001.png",                  Type = "Image file",  Size = 203128, ChildIdx = -1, ChildCount = -1 },
        new() { Name = "Copy of Image001.png",          Type = "Image file",  Size = 203256, ChildIdx = -1, ChildCount = -1 },
        new() { Name = "Copy of Image001 (Final2).png", Type = "Image file",  Size = 203512, ChildIdx = -1, ChildCount = -1 },
    };

    /// <summary>Editable slider value shared by every slider of the "Item width" table subsection.</summary>
    private static float s_tableItemWidthDummy = 0.0f;

    /// <summary>Per-column check-all state of the "Custom headers" table subsection.</summary>
    private static readonly bool[] s_tableCustomHeaderSelected = new bool[3];

    /// <summary>Flags of the "Angled headers" table.</summary>
    private static ImGuiTableFlags s_tableAngledHeaderFlags =
        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
        ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.HighlightHoveredColumn;

    /// <summary>Per-column flags applied to every angled-header column after the frozen first column.</summary>
    private static ImGuiTableColumnFlags s_tableAngledHeaderColumnFlags =
        ImGuiTableColumnFlags.AngledHeader | ImGuiTableColumnFlags.WidthFixed;

    /// <summary>Checkbox state of the "Angled headers" table, one entry per track-column intersection.</summary>
    private static readonly bool[] s_tableAngledHeaderBools = new bool[14 * 12];

    /// <summary>Frozen column count of the "Angled headers" table.</summary>
    private static int s_tableAngledHeaderFrozenCols = 1;

    /// <summary>Frozen row count of the "Angled headers" table.</summary>
    private static int s_tableAngledHeaderFrozenRows = 2;

    /// <summary>Flags of the first "Context menus" table, which uses the built-in table context menu.</summary>
    private static ImGuiTableFlags s_tableContextMenuFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Borders | ImGuiTableFlags.ContextMenuInBody;

    /// <summary>Flags of the "Synced instances" tables, which share an identifier and therefore their settings.</summary>
    private static ImGuiTableFlags s_tableSyncedFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings;

    /// <summary>Flags of the "Advanced" table, which exposes most table features at once.</summary>
    private static ImGuiTableFlags s_tableAdvancedFlags =
        ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti | ImGuiTableFlags.RowBg |
        ImGuiTableFlags.Borders | ImGuiTableFlags.NoBordersInBody |
        ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;

    /// <summary>Per-column base flags OR-ed into every column of the "Advanced" table.</summary>
    private static ImGuiTableColumnFlags s_tableAdvancedColumnsBaseFlags = ImGuiTableColumnFlags.None;

    /// <summary>Contents kind of the "Advanced" first column: plain text.</summary>
    private const int TableAdvancedContentsText = 0;

    /// <summary>Contents kind of the "Advanced" first column: button.</summary>
    private const int TableAdvancedContentsButton = 1;

    /// <summary>Contents kind of the "Advanced" first column: small button.</summary>
    private const int TableAdvancedContentsSmallButton = 2;

    /// <summary>Contents kind of the "Advanced" first column: full-width button.</summary>
    private const int TableAdvancedContentsFillButton = 3;

    /// <summary>Contents kind of the "Advanced" first column: single-column selectable.</summary>
    private const int TableAdvancedContentsSelectable = 4;

    /// <summary>Contents kind of the "Advanced" first column: selectable spanning the whole row.</summary>
    private const int TableAdvancedContentsSelectableSpanRow = 5;

    /// <summary>Human-readable names of the "Advanced" first-column contents kinds, in canonical order.</summary>
    private static readonly string[] s_tableAdvancedContentsTypeNames =
    {
        "Text", "Button", "SmallButton", "FillButton", "Selectable", "Selectable (span row)",
    };

    /// <summary>Selected contents kind of the "Advanced" first column.</summary>
    private static int s_tableAdvancedContentsType = TableAdvancedContentsSelectableSpanRow;

    /// <summary>Frozen column count of the "Advanced" table.</summary>
    private static int s_tableAdvancedFreezeCols = 1;

    /// <summary>Frozen row count of the "Advanced" table.</summary>
    private static int s_tableAdvancedFreezeRows = 1;

    /// <summary>Number of items shown by the "Advanced" table, editable from its Options node.</summary>
    private static int s_tableAdvancedItemsCount = s_templateNames.Length * 2;

    /// <summary>Outer size of the "Advanced" table as an X/Y pair, editable from its Options node.</summary>
    private static readonly float[] s_tableAdvancedOuterSize = new float[2];

    /// <summary>Whether the "Advanced" table passes its outer size to BeginTable().</summary>
    private static bool s_tableAdvancedOuterSizeEnabled = true;

    /// <summary>Minimum row height of the "Advanced" table, in pixels; zero selects automatic height.</summary>
    private static float s_tableAdvancedRowMinHeight = 0.0f;

    /// <summary>Inner width of the "Advanced" table while ScrollX is active.</summary>
    private static float s_tableAdvancedInnerWidth = 0.0f;

    /// <summary>Whether the "Advanced" table submits visible column headers.</summary>
    private static bool s_tableAdvancedShowHeaders = true;

    /// <summary>Whether the "Advanced" description column wraps its text.</summary>
    private static bool s_tableAdvancedShowWrappedText = false;

    /// <summary>Items of the "Advanced" table; allocated lazily because their names depend on the template list.</summary>
    private static List<TableDemoItem>? s_tableAdvancedItems;

    /// <summary>Identifiers of the selected "Advanced" rows; the canonical demo shadows this with a vector of ints.</summary>
    private static readonly List<int> s_tableAdvancedSelection = new();

    /// <summary>Set when the "Advanced" table must re-sort, either from sort-spec changes or edited quantities.</summary>
    private static bool s_tableAdvancedItemsNeedSort = false;

    /// <summary>Whether the "Advanced" table shows its draw-command and scroll debug details.</summary>
    private static bool s_tableAdvancedShowDebugDetails = false;

    /// <summary>Set after the font-dependent "Advanced" field values have been sized for the current font.</summary>
    private static bool s_tableAdvancedStateInitialized = false;

    /// <summary>
    /// Allocates the editable cell text of the second "Padding" table, one entry per 3x5 cell.
    /// </summary>
    /// <returns>A new array holding the initial "edit me" text of every cell.</returns>
    private static string[] CreateTablePaddingTextBufs()
    {
        string[] bufs = new string[3 * 5];
        for (int n = 0; n < bufs.Length; n++)
            bufs[n] = "edit me";
        return bufs;
    }

    /// <summary>
    /// Shows a combo box selecting the sizing policy bits of a table flags value, together with a
    /// tooltip describing every available policy.
    /// </summary>
    /// <param name="pFlags">Table flags whose sizing policy bits are edited in place.</param>
    private static void EditTableSizingFlags(ref ImGuiTableFlags pFlags)
    {
        (ImGuiTableFlags Value, string Name, string Tooltip)[] policies =
        {
            (ImGuiTableFlags.None, "Default", "Use default sizing policy:\n- ImGuiTableFlags_SizingFixedFit if ScrollX is on or if host window has ImGuiWindowFlags_AlwaysAutoResize.\n- ImGuiTableFlags_SizingStretchSame otherwise."),
            (ImGuiTableFlags.SizingFixedFit, "ImGuiTableFlags_SizingFixedFit", "Columns default to _WidthFixed (if resizable) or _WidthAuto (if not resizable), matching contents width."),
            (ImGuiTableFlags.SizingFixedSame, "ImGuiTableFlags_SizingFixedSame", "Columns are all the same width, matching the maximum contents width.\nImplicitly disable ImGuiTableFlags_Resizable and enable ImGuiTableFlags_NoKeepColumnsVisible."),
            (ImGuiTableFlags.SizingStretchProp, "ImGuiTableFlags_SizingStretchProp", "Columns default to _WidthStretch with weights proportional to their widths."),
            (ImGuiTableFlags.SizingStretchSame, "ImGuiTableFlags_SizingStretchSame", "Columns default to _WidthStretch with same weights."),
        };
        int idx;
        for (idx = 0; idx < policies.Length; idx++)
            if (policies[idx].Value == (pFlags & ImGuiTableFlags.SizingMask_))
                break;
        string previewText = idx < policies.Length
            ? (idx > 0 ? policies[idx].Name.Substring("ImGuiTableFlags".Length) : policies[idx].Name)
            : string.Empty;
        if (ImGui.BeginCombo("Sizing Policy", previewText))
        {
            for (int n = 0; n < policies.Length; n++)
                if (ImGui.Selectable(policies[n].Name, idx == n))
                    pFlags = (pFlags & ~ImGuiTableFlags.SizingMask_) | policies[n].Value;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.BeginItemTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 50.0f);
            for (int m = 0; m < policies.Length; m++)
            {
                ImGui.Separator();
                ImGui.Text("%s:", policies[m].Name);
                ImGui.Separator();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().IndentSpacing * 0.5f);
                ImGui.TextUnformatted(policies[m].Tooltip);
            }
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// Shows the per-column input flag checkboxes of the "Columns flags" demonstration.
    /// </summary>
    /// <param name="pFlags">Column flags edited in place.</param>
    private static void EditTableColumnsFlags(ref ImGuiTableColumnFlags pFlags)
    {
        CheckboxFlags("_Disabled", ref pFlags, ImGuiTableColumnFlags.Disabled); ImGui.SameLine(); HelpMarker("Master disable flag (also hide from context menu)");
        CheckboxFlags("_DefaultHide", ref pFlags, ImGuiTableColumnFlags.DefaultHide);
        CheckboxFlags("_DefaultSort", ref pFlags, ImGuiTableColumnFlags.DefaultSort);
        if (CheckboxFlags("_WidthStretch", ref pFlags, ImGuiTableColumnFlags.WidthStretch))
            pFlags &= ~(ImGuiTableColumnFlags.WidthMask_ ^ ImGuiTableColumnFlags.WidthStretch);
        if (CheckboxFlags("_WidthFixed", ref pFlags, ImGuiTableColumnFlags.WidthFixed))
            pFlags &= ~(ImGuiTableColumnFlags.WidthMask_ ^ ImGuiTableColumnFlags.WidthFixed);
        CheckboxFlags("_NoResize", ref pFlags, ImGuiTableColumnFlags.NoResize);
        CheckboxFlags("_NoReorder", ref pFlags, ImGuiTableColumnFlags.NoReorder);
        CheckboxFlags("_NoHide", ref pFlags, ImGuiTableColumnFlags.NoHide);
        CheckboxFlags("_NoClip", ref pFlags, ImGuiTableColumnFlags.NoClip);
        CheckboxFlags("_NoSort", ref pFlags, ImGuiTableColumnFlags.NoSort);
        CheckboxFlags("_NoSortAscending", ref pFlags, ImGuiTableColumnFlags.NoSortAscending);
        CheckboxFlags("_NoSortDescending", ref pFlags, ImGuiTableColumnFlags.NoSortDescending);
        CheckboxFlags("_NoHeaderLabel", ref pFlags, ImGuiTableColumnFlags.NoHeaderLabel);
        CheckboxFlags("_NoHeaderWidth", ref pFlags, ImGuiTableColumnFlags.NoHeaderWidth);
        CheckboxFlags("_PreferSortAscending", ref pFlags, ImGuiTableColumnFlags.PreferSortAscending);
        CheckboxFlags("_PreferSortDescending", ref pFlags, ImGuiTableColumnFlags.PreferSortDescending);
        CheckboxFlags("_IndentEnable", ref pFlags, ImGuiTableColumnFlags.IndentEnable); ImGui.SameLine(); HelpMarker("Default for column 0");
        CheckboxFlags("_IndentDisable", ref pFlags, ImGuiTableColumnFlags.IndentDisable); ImGui.SameLine(); HelpMarker("Default for column >0");
        CheckboxFlags("_AngledHeader", ref pFlags, ImGuiTableColumnFlags.AngledHeader);
    }

    /// <summary>
    /// Shows the read-only column status flags reported by TableGetColumnFlags().
    /// </summary>
    /// <param name="flags">Column status flags to display.</param>
    private static void ShowTableColumnsStatusFlags(ImGuiTableColumnFlags flags)
    {
        CheckboxFlags("_IsEnabled", ref flags, ImGuiTableColumnFlags.IsEnabled);
        CheckboxFlags("_IsVisible", ref flags, ImGuiTableColumnFlags.IsVisible);
        CheckboxFlags("_IsSorted", ref flags, ImGuiTableColumnFlags.IsSorted);
        CheckboxFlags("_IsHovered", ref flags, ImGuiTableColumnFlags.IsHovered);
    }

    /// <summary>
    /// Sorts a demonstration item list according to the current table sort specifications. The canonical
    /// demo stores the specifications in a static field because <c>qsort</c> cannot receive user data;
    /// this port passes them directly to <see cref="List{T}.Sort(Comparison{T})"/>, which removes the
    /// need for shared state.
    /// </summary>
    /// <param name="sortSpecs">Active sort specifications returned by TableGetSortSpecs().</param>
    /// <param name="items">Item list reordered in place.</param>
    private static void SortWithSortSpecs(ImGuiTableSortSpecs sortSpecs, List<TableDemoItem> items)
    {
        if (items.Count > 1)
            items.Sort((a, b) => CompareWithSortSpecs(a, b, sortSpecs));
    }

    /// <summary>
    /// Compares two demonstration items using the table sort specifications, honouring each
    /// specification's direction and falling back to the item identifier so that the ordering is total.
    /// </summary>
    /// <param name="a">Left item.</param>
    /// <param name="b">Right item.</param>
    /// <param name="sortSpecs">Active sort specifications that select the compared columns.</param>
    /// <returns>A negative value when <paramref name="a"/> sorts first, a positive value when
    /// <paramref name="b"/> sorts first, and zero when the two items compare equal.</returns>
    /// <remarks>
    /// The canonical comparison resolves the Description column against the item name
    ///.; that behaviour is transcribed here rather than corrected, because
    /// the demo doubles as the reference for the sorting callbacks. Unknown column identifiers leave
    /// the running comparison unchanged, matching the canonical <c>IM_ASSERT(0)</c> fall-through on
    /// </remarks>
    private static int CompareWithSortSpecs(TableDemoItem a, TableDemoItem b, ImGuiTableSortSpecs sortSpecs)
    {
        ImGuiTableColumnSortSpecs[]? specs = sortSpecs.Specs;
        for (int n = 0; n < sortSpecs.SpecsCount && specs != null; n++)
        {
            ImGuiTableColumnSortSpecs spec = specs[n];
            int delta = spec.ColumnUserID switch
            {
                (uint)TableDemoColumnId.ID => a.ID - b.ID,
                (uint)TableDemoColumnId.Name => string.CompareOrdinal(a.Name, b.Name),
                (uint)TableDemoColumnId.Quantity => a.Quantity - b.Quantity,
                (uint)TableDemoColumnId.Description => string.CompareOrdinal(a.Name, b.Name),
                _ => 0,
            };
            if (delta > 0)
                return spec.SortDirection == ImGuiSortDirection.Ascending ? +1 : -1;
            if (delta < 0)
                return spec.SortDirection == ImGuiSortDirection.Ascending ? -1 : +1;
        }

        // The canonical comparison always returns a discriminating result because qsort is unstable.
        return a.ID - b.ID;
    }

    /// <summary>
    /// Renders one entry of the "Tree view" dummy file-system, recursing into folder children.
    /// </summary>
    /// <param name="node">Entry to submit for the current table row.</param>
    /// <param name="allNodes">Complete entry array that child indices resolve against.</param>
    /// <param name="treeNodeFlagsBase">Base tree-node flags configured by the subsection checkboxes.</param>
    /// <remarks>
    /// The canonical <c>ImGuiTreeNodeFlags_LabelSpanAllColumns</c> bit is demonstrated only on the
    /// root entry, so it is cleared for every other row before the tree node is submitted.
    /// </remarks>
    private static void DisplayTableTreeNode(TableDemoTreeNode node, TableDemoTreeNode[] allNodes, ImGuiTreeNodeFlags treeNodeFlagsBase)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        bool isFolder = node.ChildCount > 0;

        ImGuiTreeNodeFlags nodeFlags = treeNodeFlagsBase;
        if (!ReferenceEquals(node, allNodes[0]))
            nodeFlags &= ~ImGuiTreeNodeFlags.LabelSpanAllColumns; // Only demonstrate this on the root node.

        if (isFolder)
        {
            bool open = ImGui.TreeNodeEx(node.Name, nodeFlags);
            if ((nodeFlags & ImGuiTreeNodeFlags.LabelSpanAllColumns) == 0)
            {
                ImGui.TableNextColumn();
                ImGui.TextDisabled("--");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(node.Type);
            }
            if (open)
            {
                for (int childN = 0; childN < node.ChildCount; childN++)
                    DisplayTableTreeNode(allNodes[node.ChildIdx + childN], allNodes, treeNodeFlagsBase);
                ImGui.TreePop();
            }
        }
        else
        {
            ImGui.TreeNodeEx(node.Name, nodeFlags | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet | ImGuiTreeNodeFlags.NoTreePushOnOpen);
            ImGui.TableNextColumn();
            ImGui.Text("%d", node.Size);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(node.Type);
        }
    }

    /// <summary>
    /// Demonstrates the three table submission patterns, border and background flags, resizable, reorderable,
    /// hideable, padding, sizing-policy, outer-size, scroll-freeze, clipping, column-flag, column-width,
    /// nested-table, row-height, background-color, tree-view, item-width, header, angled-header, context-menu,
    /// synced-instance, sorting, and advanced-options features.
    /// </summary>
    public static void DemoWindowTables()
    {
        if (!ImGui.CollapsingHeader("Tables & Columns"))
            return;

        // Using those as a base value to create width/height that are factor of the size of our font
        float textBaseWidth = ImGui.CalcTextSize("A").X;
        float textBaseHeight = ImGui.GetTextLineHeightWithSpacing();

        // The "Advanced" default outer size depends on the font metrics, so it is sized on the first
        // frame rather than in a static field initializer.
        if (!s_tableAdvancedStateInitialized)
        {
            s_tableAdvancedOuterSize[0] = 0.0f;
            s_tableAdvancedOuterSize[1] = textBaseHeight * 12.0f;
            s_tableAdvancedStateInitialized = true;
        }

        ImGui.PushID("Tables");

        int openAction = -1;
        if (ImGui.Button("Expand all"))
            openAction = 1;
        ImGui.SameLine();
        if (ImGui.Button("Collapse all"))
            openAction = 0;
        ImGui.SameLine();

        // Options
        ImGui.Checkbox("Disable tree indentation", ref s_tableDisableIndent);
        ImGui.SameLine();
        HelpMarker("Disable the indenting of tree nodes so demo tables can use the full window width.");
        ImGui.Separator();
        if (s_tableDisableIndent)
            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 0.0f);

        // About Styling of tables
        // Most settings are configured on a per-table basis via the flags passed to BeginTable() and the
        // TableSetupColumn() APIs. A few settings are shared and part of the ImGuiStyle structure:
        //   style.CellPadding                          // Padding within each cell
        //   style.Colors[ImGuiCol.TableHeaderBg]       // Table header background
        //   style.Colors[ImGuiCol.TableBorderStrong]   // Table outer and header borders
        //   style.Colors[ImGuiCol.TableBorderLight]    // Table inner borders
        //   style.Colors[ImGuiCol.TableRowBg]          // Table row background when ImGuiTableFlags_RowBg is enabled (even rows)
        //   style.Colors[ImGuiCol.TableRowBgAlt]       // Table row background when ImGuiTableFlags_RowBg is enabled (odd rows)

        // Demos
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Basic"))
        {
            // Here three different ways to output a table are showcased. They are very simple variations of a same thing!

            // [Method 1] Using TableNextRow() to create a new row, and TableSetColumnIndex() to select the column.
            // In many situations, this is the most flexible and easy to use pattern.
            HelpMarker("Using TableNextRow() + calling TableSetColumnIndex() _before_ each cell, in a loop.");
            if (ImGui.BeginTable("table1", 3))
            {
                for (int row = 0; row < 4; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Row %d Column %d", row, column);
                    }
                }
                ImGui.EndTable();
            }

            // [Method 2] Using TableNextColumn() called multiple times, instead of using a for loop + TableSetColumnIndex().
            // This is generally more convenient when the code manually submits the contents of each column.
            HelpMarker("Using TableNextRow() + calling TableNextColumn() _before_ each cell, manually.");
            if (ImGui.BeginTable("table2", 3))
            {
                for (int row = 0; row < 4; row++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Row %d", row);
                    ImGui.TableNextColumn();
                    ImGui.Text("Some contents");
                    ImGui.TableNextColumn();
                    ImGui.Text("123.456");
                }
                ImGui.EndTable();
            }

            // [Method 3] TableNextColumn() is called _before_ each cell and TableNextRow() is never called,
            // as TableNextColumn() automatically wraps around and creates new rows as needed.
            // This is generally more convenient when every cell contains the same type of data.
            HelpMarker(
                "Only using TableNextColumn(), which tends to be convenient for tables where every cell contains " +
                "the same type of contents.\n This is also more similar to the old NextColumn() function of the " +
                "Columns API, and provided to facilitate the Columns->Tables API transition.");
            if (ImGui.BeginTable("table3", 3))
            {
                for (int item = 0; item < 14; item++)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text("Item %d", item);
                }
                ImGui.EndTable();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Borders, background
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Borders, background"))
        {
            // Expose a few Borders related flags interactively.
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_RowBg", ref s_tableBordersFlags, ImGuiTableFlags.RowBg);
            CheckboxFlags("ImGuiTableFlags_Borders", ref s_tableBordersFlags, ImGuiTableFlags.Borders);
            ImGui.SameLine(); HelpMarker("ImGuiTableFlags_Borders\n = ImGuiTableFlags_BordersInnerV\n | ImGuiTableFlags_BordersOuterV\n | ImGuiTableFlags_BordersInnerH\n | ImGuiTableFlags_BordersOuterH");
            ImGui.Indent();

            CheckboxFlags("ImGuiTableFlags_BordersH", ref s_tableBordersFlags, ImGuiTableFlags.BordersH);
            ImGui.Indent();
            CheckboxFlags("ImGuiTableFlags_BordersOuterH", ref s_tableBordersFlags, ImGuiTableFlags.BordersOuterH);
            CheckboxFlags("ImGuiTableFlags_BordersInnerH", ref s_tableBordersFlags, ImGuiTableFlags.BordersInnerH);
            ImGui.Unindent();

            CheckboxFlags("ImGuiTableFlags_BordersV", ref s_tableBordersFlags, ImGuiTableFlags.BordersV);
            ImGui.Indent();
            CheckboxFlags("ImGuiTableFlags_BordersOuterV", ref s_tableBordersFlags, ImGuiTableFlags.BordersOuterV);
            CheckboxFlags("ImGuiTableFlags_BordersInnerV", ref s_tableBordersFlags, ImGuiTableFlags.BordersInnerV);
            ImGui.Unindent();

            CheckboxFlags("ImGuiTableFlags_BordersOuter", ref s_tableBordersFlags, ImGuiTableFlags.BordersOuter);
            CheckboxFlags("ImGuiTableFlags_BordersInner", ref s_tableBordersFlags, ImGuiTableFlags.BordersInner);
            ImGui.Unindent();

            ImGui.AlignTextToFramePadding(); ImGui.Text("Cell contents:");
            ImGui.SameLine(); ImGui.RadioButton("Text", ref s_tableBordersContentsType, TableBordersContentsText);
            ImGui.SameLine(); ImGui.RadioButton("FillButton", ref s_tableBordersContentsType, TableBordersContentsFillButton);
            ImGui.Checkbox("Display headers", ref s_tableBordersDisplayHeaders);
            CheckboxFlags("ImGuiTableFlags_NoBordersInBody", ref s_tableBordersFlags, ImGuiTableFlags.NoBordersInBody); ImGui.SameLine(); HelpMarker("Disable vertical borders in columns Body (borders will always appear in Headers)");
            PopStyleCompact();

            if (ImGui.BeginTable("table1", 3, s_tableBordersFlags))
            {
                // Display headers so their interaction with borders can be inspected.
                // (Headers are not the main purpose of this section of the demo, so they are not elaborated on here.
                // See other sections for details.)
                if (s_tableBordersDisplayHeaders)
                {
                    ImGui.TableSetupColumn("One");
                    ImGui.TableSetupColumn("Two");
                    ImGui.TableSetupColumn("Three");
                    ImGui.TableHeadersRow();
                }

                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        string buf = $"Hello {column},{row}";
                        if (s_tableBordersContentsType == TableBordersContentsText)
                            ImGui.TextUnformatted(buf);
                        else if (s_tableBordersContentsType == TableBordersContentsFillButton)
                            ImGui.Button(buf, new ImVec2(-float.Epsilon, 0.0f));
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Resizable, stretch
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Resizable, stretch"))
        {
            // By default, if ScrollX is not enabled the sizing policy for each column is "Stretch".
            // All columns maintain a sizing weight, and they occupy all available width.
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableStretchFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_BordersV", ref s_tableStretchFlags, ImGuiTableFlags.BordersV);
            ImGui.SameLine(); HelpMarker(
                "Using the _Resizable flag automatically enables the _BordersInnerV flag as well, " +
                "this is why the resize borders are still showing when unchecking this.");
            PopStyleCompact();

            if (ImGui.BeginTable("table1", 3, s_tableStretchFlags))
            {
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Hello %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Resizable, fixed
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Resizable, fixed"))
        {
            // ImGuiTableFlags_SizingFixedFit is used even though _ScrollX is not set, so columns adopt the
            // "Fixed" policy and maintain a fixed width regardless of the whole available width (unless the
            // table is small). If there is not enough available width to fit all columns, they are resized down.
            HelpMarker(
                "Using _Resizable + _SizingFixedFit flags.\n" +
                "Fixed-width columns generally makes more sense if you want to use horizontal scrolling.\n\n" +
                "Double-click a column border to auto-fit the column to its contents.");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_NoHostExtendX", ref s_tableFixedFlags, ImGuiTableFlags.NoHostExtendX);
            PopStyleCompact();

            if (ImGui.BeginTable("table1", 3, s_tableFixedFlags))
            {
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Hello %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Resizable, mixed
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Resizable, mixed"))
        {
            HelpMarker(
                "Using TableSetupColumn() to alter resizing policy on a per-column basis.\n\n" +
                "When combining Fixed and Stretch columns, generally you only want one, maybe two trailing " +
                "columns to use _WidthStretch.");
            if (ImGui.BeginTable("table1", 3, s_tableMixedFlags))
            {
                ImGui.TableSetupColumn("AAA", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("BBB", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("CCC", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("%s %d,%d", (column == 2) ? "Stretch" : "Fixed", column, row);
                    }
                }
                ImGui.EndTable();
            }
            if (ImGui.BeginTable("table2", 6, s_tableMixedFlags))
            {
                ImGui.TableSetupColumn("AAA", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("BBB", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("CCC", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide);
                ImGui.TableSetupColumn("DDD", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("EEE", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("FFF", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide);
                ImGui.TableHeadersRow();
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 6; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("%s %d,%d", (column >= 3) ? "Stretch" : "Fixed", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Reorderable, hideable, with headers
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Reorderable, hideable, with headers"))
        {
            HelpMarker(
                "Click and drag column headers to reorder columns.\n\n" +
                "Right-click on a header to open a context menu.");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableReorderableFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_Reorderable", ref s_tableReorderableFlags, ImGuiTableFlags.Reorderable);
            CheckboxFlags("ImGuiTableFlags_Hideable", ref s_tableReorderableFlags, ImGuiTableFlags.Hideable);
            CheckboxFlags("ImGuiTableFlags_NoBordersInBody", ref s_tableReorderableFlags, ImGuiTableFlags.NoBordersInBody);
            CheckboxFlags("ImGuiTableFlags_NoBordersInBodyUntilResize", ref s_tableReorderableFlags, ImGuiTableFlags.NoBordersInBodyUntilResize); ImGui.SameLine(); HelpMarker("Disable vertical borders in columns Body until hovered for resize (borders will always appear in Headers)");
            CheckboxFlags("ImGuiTableFlags_HighlightHoveredColumn", ref s_tableReorderableFlags, ImGuiTableFlags.HighlightHoveredColumn);
            PopStyleCompact();

            if (ImGui.BeginTable("table1", 3, s_tableReorderableFlags))
            {
                // Submit column names with TableSetupColumn() and call TableHeadersRow() to create a row with
                // a header in each column.
                // (Later sections show how TableSetupColumn() has other uses, optional flags, sizing weight etc.)
                ImGui.TableSetupColumn("One");
                ImGui.TableSetupColumn("Two");
                ImGui.TableSetupColumn("Three");
                ImGui.TableHeadersRow();
                for (int row = 0; row < 6; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Hello %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }

            // Use outer_size.x == 0.0f instead of default to make the table as tight as possible
            // (only valid when no scrolling and no stretch column)
            if (ImGui.BeginTable("table2", 3, s_tableReorderableFlags | ImGuiTableFlags.SizingFixedFit, new ImVec2(0.0f, 0.0f)))
            {
                ImGui.TableSetupColumn("One");
                ImGui.TableSetupColumn("Two");
                ImGui.TableSetupColumn("Three");
                ImGui.TableHeadersRow();
                for (int row = 0; row < 6; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Fixed %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Padding
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Padding"))
        {
            // First example: showcase use of padding flags and effect of BorderOuterV/BorderInnerV on X padding.
            // BorderOuterH/BorderInnerH are not exposed here because they have no effect on X padding.
            HelpMarker(
                "We often want outer padding activated when any using features which makes the edges of a column visible:\n" +
                "e.g.:\n" +
                "- BorderOuterV\n" +
                "- any form of row selection\n" +
                "Because of this, activating BorderOuterV sets the default to PadOuterX. " +
                "Using PadOuterX or NoPadOuterX you can override the default.\n\n" +
                "Actual padding values are using style.CellPadding.\n\n" +
                "In this demo we don't show horizontal borders to emphasize how they don't affect default horizontal padding.");

            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_PadOuterX", ref s_tablePaddingFlags, ImGuiTableFlags.PadOuterX);
            ImGui.SameLine(); HelpMarker("Enable outer-most padding (default if ImGuiTableFlags_BordersOuterV is set)");
            CheckboxFlags("ImGuiTableFlags_NoPadOuterX", ref s_tablePaddingFlags, ImGuiTableFlags.NoPadOuterX);
            ImGui.SameLine(); HelpMarker("Disable outer-most padding (default if ImGuiTableFlags_BordersOuterV is not set)");
            CheckboxFlags("ImGuiTableFlags_NoPadInnerX", ref s_tablePaddingFlags, ImGuiTableFlags.NoPadInnerX);
            ImGui.SameLine(); HelpMarker("Disable inner padding between columns (double inner padding if BordersOuterV is on, single inner padding if BordersOuterV is off)");
            CheckboxFlags("ImGuiTableFlags_BordersOuterV", ref s_tablePaddingFlags, ImGuiTableFlags.BordersOuterV);
            CheckboxFlags("ImGuiTableFlags_BordersInnerV", ref s_tablePaddingFlags, ImGuiTableFlags.BordersInnerV);
            ImGui.Checkbox("show_headers", ref s_tablePaddingShowHeaders);
            PopStyleCompact();

            if (ImGui.BeginTable("table_padding", 3, s_tablePaddingFlags))
            {
                if (s_tablePaddingShowHeaders)
                {
                    ImGui.TableSetupColumn("One");
                    ImGui.TableSetupColumn("Two");
                    ImGui.TableSetupColumn("Three");
                    ImGui.TableHeadersRow();
                }

                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        if (row == 0)
                        {
                            ImGui.Text("Avail %.2f", ImGui.GetContentRegionAvail().X);
                        }
                        else
                        {
                            string buf = $"Hello {column},{row}";
                            ImGui.Button(buf, new ImVec2(-float.Epsilon, 0.0f));
                        }
                        // if ((ImGui.TableGetColumnFlags() & ImGuiTableColumnFlags.IsHovered) != 0)
                        //     ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, IM_COL32(0, 100, 0, 255));
                    }
                }
                ImGui.EndTable();
            }

            // Second example: set style.CellPadding to (0.0) or a custom value.
            HelpMarker("Setting style.CellPadding to (0,0) or a custom value.");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Borders", ref s_tablePaddingFlags2, ImGuiTableFlags.Borders);
            CheckboxFlags("ImGuiTableFlags_BordersH", ref s_tablePaddingFlags2, ImGuiTableFlags.BordersH);
            CheckboxFlags("ImGuiTableFlags_BordersV", ref s_tablePaddingFlags2, ImGuiTableFlags.BordersV);
            CheckboxFlags("ImGuiTableFlags_BordersInner", ref s_tablePaddingFlags2, ImGuiTableFlags.BordersInner);
            CheckboxFlags("ImGuiTableFlags_BordersOuter", ref s_tablePaddingFlags2, ImGuiTableFlags.BordersOuter);
            CheckboxFlags("ImGuiTableFlags_RowBg", ref s_tablePaddingFlags2, ImGuiTableFlags.RowBg);
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tablePaddingFlags2, ImGuiTableFlags.Resizable);
            ImGui.Checkbox("show_widget_frame_bg", ref s_tablePaddingShowWidgetFrameBg);
            ImGui.SliderFloat2("CellPadding", s_tablePaddingCellPadding, 0.0f, 10.0f, "%.0f");
            PopStyleCompact();

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new ImVec2(s_tablePaddingCellPadding[0], s_tablePaddingCellPadding[1]));
            if (ImGui.BeginTable("table_padding_2", 3, s_tablePaddingFlags2))
            {
                if (!s_tablePaddingShowWidgetFrameBg)
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, 0u);
                for (int cell = 0; cell < 3 * 5; cell++)
                {
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-float.Epsilon);
                    ImGui.PushID(cell);
                    ImGui.InputText("##cell", ref s_tablePaddingTextBufs[cell]);
                    ImGui.PopID();
                }
                if (!s_tablePaddingShowWidgetFrameBg)
                    ImGui.PopStyleColor();
                ImGui.EndTable();
            }
            ImGui.PopStyleVar();

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Sizing policies
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Sizing policies"))
        {
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableSizingFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_NoHostExtendX", ref s_tableSizingFlags, ImGuiTableFlags.NoHostExtendX);
            PopStyleCompact();

            for (int tableN = 0; tableN < 4; tableN++)
            {
                ImGui.PushID(tableN);
                ImGui.SetNextItemWidth(textBaseWidth * 30);
                EditTableSizingFlags(ref s_tableSizingPolicies[tableN]);

                // To make it easier to understand the different sizing policies, each policy displays one table
                // where the columns have equal contents width, and one where the columns have different contents width.
                if (ImGui.BeginTable("table1", 3, s_tableSizingPolicies[tableN] | s_tableSizingFlags))
                {
                    for (int row = 0; row < 3; row++)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text("Oh dear");
                        ImGui.TableNextColumn(); ImGui.Text("Oh dear");
                        ImGui.TableNextColumn(); ImGui.Text("Oh dear");
                    }
                    ImGui.EndTable();
                }
                if (ImGui.BeginTable("table2", 3, s_tableSizingPolicies[tableN] | s_tableSizingFlags))
                {
                    for (int row = 0; row < 3; row++)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text("AAAA");
                        ImGui.TableNextColumn(); ImGui.Text("BBBBBBBB");
                        ImGui.TableNextColumn(); ImGui.Text("CCCCCCCCCCCC");
                    }
                    ImGui.EndTable();
                }
                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Advanced");
            ImGui.SameLine();
            HelpMarker(
                "This section allows you to interact and see the effect of various sizing policies " +
                "depending on whether Scroll is enabled and the contents of your columns.");

            PushStyleCompact();
            ImGui.PushID("Advanced");
            ImGui.PushItemWidth(textBaseWidth * 30);
            EditTableSizingFlags(ref s_tableSizingAdvancedFlags);
            ImGui.Combo("Contents", ref s_tableSizingAdvancedContentsType, "Show width\0Short Text\0Long Text\0Button\0Fill Button\0InputText\0");
            if (s_tableSizingAdvancedContentsType == TableSizingContentsFillButton)
            {
                ImGui.SameLine();
                HelpMarker(
                    "Be mindful that using right-alignment (e.g. size.x = -FLT_MIN) creates a feedback loop " +
                    "where contents width can feed into auto-column width can feed into contents width.");
            }
            ImGui.DragInt("Columns", ref s_tableSizingAdvancedColumnCount, 0.1f, 1, 64, "%d", ImGuiSliderFlags.AlwaysClamp);
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableSizingAdvancedFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_PreciseWidths", ref s_tableSizingAdvancedFlags, ImGuiTableFlags.PreciseWidths);
            ImGui.SameLine(); HelpMarker("Disable distributing remainder width to stretched columns (width allocation on a 100-wide table with 3 columns: Without this flag: 33,33,34. With this flag: 33,33,33). With larger number of columns, resizing will appear to be less smooth.");
            CheckboxFlags("ImGuiTableFlags_ScrollX", ref s_tableSizingAdvancedFlags, ImGuiTableFlags.ScrollX);
            CheckboxFlags("ImGuiTableFlags_ScrollY", ref s_tableSizingAdvancedFlags, ImGuiTableFlags.ScrollY);
            CheckboxFlags("ImGuiTableFlags_NoClip", ref s_tableSizingAdvancedFlags, ImGuiTableFlags.NoClip);
            ImGui.PopItemWidth();
            ImGui.PopID();
            PopStyleCompact();

            if (ImGui.BeginTable("table2", s_tableSizingAdvancedColumnCount, s_tableSizingAdvancedFlags, new ImVec2(0.0f, textBaseHeight * 7)))
            {
                for (int cell = 0; cell < 10 * s_tableSizingAdvancedColumnCount; cell++)
                {
                    ImGui.TableNextColumn();
                    int column = ImGui.TableGetColumnIndex();
                    int row = ImGui.TableGetRowIndex();

                    ImGui.PushID(cell);
                    string label = $"Hello {column},{row}";
                    switch (s_tableSizingAdvancedContentsType)
                    {
                        case TableSizingContentsShortText: ImGui.TextUnformatted(label); break;
                        case TableSizingContentsLongText: ImGui.Text("Some %s text %d,%d\nOver two lines..", column == 0 ? "long" : "longeeer", column, row); break;
                        case TableSizingContentsShowWidth: ImGui.Text("W: %.1f", ImGui.GetContentRegionAvail().X); break;
                        case TableSizingContentsButton: ImGui.Button(label); break;
                        case TableSizingContentsFillButton: ImGui.Button(label, new ImVec2(-float.Epsilon, 0.0f)); break;
                        case TableSizingContentsInputText: ImGui.SetNextItemWidth(-float.Epsilon); ImGui.InputText("##", ref s_tableSizingAdvancedTextBuf); break;
                    }
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Vertical scrolling, with clipping
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Vertical scrolling, with clipping"))
        {
            HelpMarker(
                "Here we activate ScrollY, which will create a child window container to allow hosting scrollable contents.\n\n" +
                "We also demonstrate using ImGuiListClipper to virtualize the submission of many items.");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_ScrollY", ref s_tableScrollYFlags, ImGuiTableFlags.ScrollY);
            PopStyleCompact();

            // When using ScrollX or ScrollY we need to specify a size for our table container!
            // Otherwise by default the table will fit all available space, like a BeginChild() call.
            ImVec2 outerSize = new(0.0f, textBaseHeight * 8);
            if (ImGui.BeginTable("table_scrolly", 3, s_tableScrollYFlags, outerSize))
            {
                ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                ImGui.TableSetupColumn("One", ImGuiTableColumnFlags.None);
                ImGui.TableSetupColumn("Two", ImGuiTableColumnFlags.None);
                ImGui.TableSetupColumn("Three", ImGuiTableColumnFlags.None);
                ImGui.TableHeadersRow();

                // Demonstrate using clipper for large vertical lists
                ImGuiListClipper clipper = new();
                clipper.Begin(1000);
                while (clipper.Step())
                {
                    for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                    {
                        ImGui.TableNextRow();
                        for (int column = 0; column < 3; column++)
                        {
                            ImGui.TableSetColumnIndex(column);
                            ImGui.Text("Hello %d,%d", column, row);
                        }
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Horizontal scrolling
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Horizontal scrolling"))
        {
            HelpMarker(
                "When ScrollX is enabled, the default sizing policy becomes ImGuiTableFlags_SizingFixedFit, " +
                "as automatically stretching columns doesn't make much sense with horizontal scrolling.\n\n" +
                "Also note that as of the current version, you will almost always want to enable ScrollY along with ScrollX, " +
                "because the container window won't automatically extend vertically to fix contents " +
                "(this may be improved in future versions).");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableScrollXFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_ScrollX", ref s_tableScrollXFlags, ImGuiTableFlags.ScrollX);
            CheckboxFlags("ImGuiTableFlags_ScrollY", ref s_tableScrollXFlags, ImGuiTableFlags.ScrollY);
            ImGui.SetNextItemWidth(ImGui.GetFrameHeight());
            ImGui.DragInt("freeze_cols", ref s_tableScrollXFreezeCols, 0.2f, 0, 9, null, ImGuiSliderFlags.NoInput);
            ImGui.SetNextItemWidth(ImGui.GetFrameHeight());
            ImGui.DragInt("freeze_rows", ref s_tableScrollXFreezeRows, 0.2f, 0, 9, null, ImGuiSliderFlags.NoInput);
            PopStyleCompact();

            // When using ScrollX or ScrollY we need to specify a size for our table container!
            // Otherwise by default the table will fit all available space, like a BeginChild() call.
            ImVec2 outerSize = new(0.0f, textBaseHeight * 8);
            if (ImGui.BeginTable("table_scrollx", 7, s_tableScrollXFlags, outerSize))
            {
                ImGui.TableSetupScrollFreeze(s_tableScrollXFreezeCols, s_tableScrollXFreezeRows);
                ImGui.TableSetupColumn("Line #", ImGuiTableColumnFlags.NoHide); // Make the first column not hideable to match our use of TableSetupScrollFreeze()
                ImGui.TableSetupColumn("One");
                ImGui.TableSetupColumn("Two");
                ImGui.TableSetupColumn("Three");
                ImGui.TableSetupColumn("Four");
                ImGui.TableSetupColumn("Five");
                ImGui.TableSetupColumn("Six");
                ImGui.TableHeadersRow();
                for (int row = 0; row < 20; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 7; column++)
                    {
                        // Both TableNextColumn() and TableSetColumnIndex() return true when a column is visible or
                        // performing width measurement. Because here it is known that:
                        // - A) all columns contribute the same to row height
                        // - B) column 0 is always visible,
                        // only this one column is always submitted and the others can be skipped.
                        // More advanced per-column clipping behaviors may benefit from polling the status flags via
                        // TableGetColumnFlags().
                        if (!ImGui.TableSetColumnIndex(column) && column > 0)
                            continue;
                        if (column == 0)
                            ImGui.Text("Line %d", row);
                        else
                            ImGui.Text("Hello world %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Stretch + ScrollX");
            ImGui.SameLine();
            HelpMarker(
                "Showcase using Stretch columns + ScrollX together: " +
                "this is rather unusual and only makes sense when specifying an 'inner_width' for the table!\n" +
                "Without an explicit value, inner_width is == outer_size.x and therefore using Stretch columns " +
                "along with ScrollX doesn't make sense.");
            PushStyleCompact();
            ImGui.PushID("flags3");
            ImGui.PushItemWidth(textBaseWidth * 30);
            CheckboxFlags("ImGuiTableFlags_ScrollX", ref s_tableScrollXStretchFlags, ImGuiTableFlags.ScrollX);
            ImGui.DragFloat("inner_width", ref s_tableScrollXInnerWidth, 1.0f, 0.0f, float.MaxValue, "%.1f");
            ImGui.PopItemWidth();
            ImGui.PopID();
            PopStyleCompact();
            if (ImGui.BeginTable("table2", 7, s_tableScrollXStretchFlags, outerSize, s_tableScrollXInnerWidth))
            {
                for (int cell = 0; cell < 20 * 7; cell++)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text("Hello world %d,%d", ImGui.TableGetColumnIndex(), ImGui.TableGetRowIndex());
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Columns flags
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Columns flags"))
        {
            // Create a first table just to show all the options/flags to make visible in the example.
            const int columnCount = 3;

            if (ImGui.BeginTable("table_columns_flags_checkboxes", columnCount, ImGuiTableFlags.None))
            {
                PushStyleCompact();
                for (int column = 0; column < columnCount; column++)
                {
                    ImGui.TableNextColumn();
                    ImGui.PushID(column);
                    ImGui.AlignTextToFramePadding(); // FIXME-TABLE: Workaround for wrong text baseline propagation across columns
                    ImGui.Text("'%s'", s_tableColumnNames[column]);
                    ImGui.Spacing();
                    ImGui.Text("Input flags:");
                    EditTableColumnsFlags(ref s_tableColumnsFlags[column]);
                    ImGui.Spacing();
                    ImGui.Text("Output flags:");
                    ImGui.BeginDisabled();
                    ShowTableColumnsStatusFlags(s_tableColumnsFlagsOut[column]);
                    ImGui.EndDisabled();
                    ImGui.PopID();
                }
                PopStyleCompact();
                ImGui.EndTable();
            }

            // Create the real table the example cares about.
            // A scrolling table is used to be able to showcase the difference between the _IsEnabled and _IsVisible
            // flags above, otherwise in a non-scrolling table columns are always visible (unless using
            // ImGuiTableFlags_NoKeepColumnsVisible + resizing the parent window down).
            ImGuiTableFlags flags
                = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV
                | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable | ImGuiTableFlags.Sortable;
            ImVec2 outerSize = new(0.0f, textBaseHeight * 9);
            if (ImGui.BeginTable("table_columns_flags", columnCount, flags, outerSize))
            {
                bool hasAngledHeader = false;
                for (int column = 0; column < columnCount; column++)
                {
                    hasAngledHeader |= (s_tableColumnsFlags[column] & ImGuiTableColumnFlags.AngledHeader) != 0;
                    ImGui.TableSetupColumn(s_tableColumnNames[column], s_tableColumnsFlags[column]);
                }
                if (hasAngledHeader)
                    ImGui.TableAngledHeadersRow();
                ImGui.TableHeadersRow();
                for (int column = 0; column < columnCount; column++)
                    s_tableColumnsFlagsOut[column] = ImGui.TableGetColumnFlags(column);
                float indentStep = (float)((int)textBaseWidth / 2);
                for (int row = 0; row < 8; row++)
                {
                    // Add some indentation to demonstrate usage of per-column IndentEnable/IndentDisable flags.
                    ImGui.Indent(indentStep);
                    ImGui.TableNextRow();
                    for (int column = 0; column < columnCount; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("%s %s", (column == 0) ? "Indented" : "Hello", ImGui.TableGetColumnName(column));
                    }
                }
                ImGui.Unindent(indentStep * 8.0f);

                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Columns widths
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Columns widths"))
        {
            HelpMarker("Using TableSetupColumn() to setup default width.");

            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableWidthsFlags1, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_NoBordersInBodyUntilResize", ref s_tableWidthsFlags1, ImGuiTableFlags.NoBordersInBodyUntilResize);
            PopStyleCompact();
            if (ImGui.BeginTable("table1", 3, s_tableWidthsFlags1))
            {
                // ImGuiTableFlags_SizingFixedFit could also be set on the table, in which case all columns would
                // default to ImGuiTableColumnFlags_WidthFixed.
                ImGui.TableSetupColumn("one", ImGuiTableColumnFlags.WidthFixed, 100.0f); // Default to 100.0f
                ImGui.TableSetupColumn("two", ImGuiTableColumnFlags.WidthFixed, 200.0f); // Default to 200.0f
                ImGui.TableSetupColumn("three", ImGuiTableColumnFlags.WidthFixed);       // Default to auto
                ImGui.TableHeadersRow();
                for (int row = 0; row < 4; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        if (row == 0)
                            ImGui.Text("(w: %5.1f)", ImGui.GetContentRegionAvail().X);
                        else
                            ImGui.Text("Hello %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }

            HelpMarker(
                "Using TableSetupColumn() to setup explicit width.\n\nUnless _NoKeepColumnsVisible is set, " +
                "fixed columns with set width may still be shrunk down if there's not enough space in the host.");

            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_NoKeepColumnsVisible", ref s_tableWidthsFlags2, ImGuiTableFlags.NoKeepColumnsVisible);
            CheckboxFlags("ImGuiTableFlags_BordersInnerV", ref s_tableWidthsFlags2, ImGuiTableFlags.BordersInnerV);
            CheckboxFlags("ImGuiTableFlags_BordersOuterV", ref s_tableWidthsFlags2, ImGuiTableFlags.BordersOuterV);
            PopStyleCompact();
            if (ImGui.BeginTable("table2", 4, s_tableWidthsFlags2))
            {
                // ImGuiTableFlags_SizingFixedFit could also be set on the table, in which case all columns would
                // default to ImGuiTableColumnFlags_WidthFixed.
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 100.0f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, textBaseWidth * 15.0f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, textBaseWidth * 30.0f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, textBaseWidth * 15.0f);
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 4; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        if (row == 0)
                            ImGui.Text("(w: %5.1f)", ImGui.GetContentRegionAvail().X);
                        else
                            ImGui.Text("Hello %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Nested tables
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Nested tables"))
        {
            HelpMarker("This demonstrates embedding a table into another table cell.");

            if (ImGui.BeginTable("table_nested1", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable))
            {
                ImGui.TableSetupColumn("A0");
                ImGui.TableSetupColumn("A1");
                ImGui.TableHeadersRow();

                ImGui.TableNextColumn();
                ImGui.Text("A0 Row 0");
                {
                    float rowsHeight = (textBaseHeight * 2.0f) + (ImGui.GetStyle().CellPadding.Y * 2.0f);
                    if (ImGui.BeginTable("table_nested2", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable))
                    {
                        ImGui.TableSetupColumn("B0");
                        ImGui.TableSetupColumn("B1");
                        ImGui.TableHeadersRow();

                        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowsHeight);
                        ImGui.TableNextColumn();
                        ImGui.Text("B0 Row 0");
                        ImGui.TableNextColumn();
                        ImGui.Text("B1 Row 0");
                        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowsHeight);
                        ImGui.TableNextColumn();
                        ImGui.Text("B0 Row 1");
                        ImGui.TableNextColumn();
                        ImGui.Text("B1 Row 1");

                        ImGui.EndTable();
                    }
                }
                ImGui.TableNextColumn(); ImGui.Text("A1 Row 0");
                ImGui.TableNextColumn(); ImGui.Text("A0 Row 1");
                ImGui.TableNextColumn(); ImGui.Text("A1 Row 1");
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Row height
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Row height"))
        {
            HelpMarker(
                "You can pass a 'min_row_height' to TableNextRow().\n\nRows are padded with 'style.CellPadding.y' on top and bottom, " +
                "so effectively the minimum row height will always be >= 'style.CellPadding.y * 2.0f'.\n\n" +
                "We cannot honor a _maximum_ row height as that would require a unique clipping rectangle per row.");
            if (ImGui.BeginTable("table_row_height", 1, ImGuiTableFlags.Borders))
            {
                for (int row = 0; row < 8; row++)
                {
                    float minRowHeight = (float)(int)(textBaseHeight * 0.30f * row + ImGui.GetStyle().CellPadding.Y * 2.0f);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, minRowHeight);
                    ImGui.TableNextColumn();
                    ImGui.Text("min_row_height = %.2f", minRowHeight);
                }
                ImGui.EndTable();
            }

            HelpMarker(
                "Showcase using SameLine(0,0) to share Current Line Height between cells.\n\n" +
                "Please note that Tables Row Height is not the same thing as Current Line Height, " +
                "as a table cell may contains multiple lines.");
            if (ImGui.BeginTable("table_share_lineheight", 2, ImGuiTableFlags.Borders))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.ColorButton("##1", new ImVec4(0.13f, 0.26f, 0.40f, 1.0f), ImGuiColorEditFlags.None, new ImVec2(40, 40));
                ImGui.TableNextColumn();
                ImGui.Text("Line 1");
                ImGui.Text("Line 2");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.ColorButton("##2", new ImVec4(0.13f, 0.26f, 0.40f, 1.0f), ImGuiColorEditFlags.None, new ImVec2(40, 40));
                ImGui.TableNextColumn();
                ImGui.SameLine(0.0f, 0.0f); // Reuse line height from previous column
                ImGui.Text("Line 1, with SameLine(0,0)");
                ImGui.Text("Line 2");

                ImGui.EndTable();
            }

            HelpMarker("Showcase altering CellPadding.y between rows. Note that CellPadding.x is locked for the entire table.");
            if (ImGui.BeginTable("table_changing_cellpadding_y", 1, ImGuiTableFlags.Borders))
            {
                ImGuiStyle style = ImGui.GetStyle();
                for (int row = 0; row < 8; row++)
                {
                    if ((row % 3) == 2)
                        ImGui.PushStyleVarY(ImGuiStyleVar.CellPadding, 20.0f);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None);
                    ImGui.TableNextColumn();
                    ImGui.Text("CellPadding.y = %.2f", style.CellPadding.Y);
                    if ((row % 3) == 2)
                        ImGui.PopStyleVar();
                }
                ImGui.EndTable();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Outer size
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Outer size"))
        {
            // Showcasing use of ImGuiTableFlags_NoHostExtendX and ImGuiTableFlags_NoHostExtendY
            // Important to that note how the two flags have slightly different behaviors!
            ImGui.Text("Using NoHostExtendX and NoHostExtendY:");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_NoHostExtendX", ref s_tableOuterSizeFlags, ImGuiTableFlags.NoHostExtendX);
            ImGui.SameLine(); HelpMarker("Make outer width auto-fit to columns, overriding outer_size.x value.\n\nOnly available when ScrollX/ScrollY are disabled and Stretch columns are not used.");
            CheckboxFlags("ImGuiTableFlags_NoHostExtendY", ref s_tableOuterSizeFlags, ImGuiTableFlags.NoHostExtendY);
            ImGui.SameLine(); HelpMarker("Make outer height stop exactly at outer_size.y (prevent auto-extending table past the limit).\n\nOnly available when ScrollX/ScrollY are disabled. Data below the limit will be clipped and not visible.");
            PopStyleCompact();

            ImVec2 outerSize = new ImVec2(0.0f, textBaseHeight * 5.5f);
            if (ImGui.BeginTable("table1", 3, s_tableOuterSizeFlags, outerSize))
            {
                for (int row = 0; row < 10; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text("Cell %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.SameLine();
            ImGui.Text("Hello!");

            ImGui.Spacing();

            ImGui.Text("Using explicit size:");
            if (ImGui.BeginTable("table2", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, new ImVec2(textBaseWidth * 30, 0.0f)))
            {
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text("Cell %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.SameLine();
            if (ImGui.BeginTable("table3", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, new ImVec2(textBaseWidth * 30, 0.0f)))
            {
                float rowsHeight = textBaseHeight * 1.5f + ImGui.GetStyle().CellPadding.Y * 2.0f;
                for (int row = 0; row < 3; row++)
                {
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowsHeight);
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text("Cell %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Background color
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Background color"))
        {
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_Borders", ref s_tableBackgroundFlags, ImGuiTableFlags.Borders);
            CheckboxFlags("ImGuiTableFlags_RowBg", ref s_tableBackgroundFlags, ImGuiTableFlags.RowBg);
            ImGui.SameLine(); HelpMarker("ImGuiTableFlags_RowBg automatically sets RowBg0 to alternative colors pulled from the Style.");
            ImGui.Combo("row bg type", ref s_tableBackgroundRowType, "None\0Red\0Gradient\0");
            ImGui.Combo("row bg target", ref s_tableBackgroundRowTarget, "RowBg0\0RowBg1\0"); ImGui.SameLine(); HelpMarker("Target RowBg0 to override the alternating odd/even colors,\nTarget RowBg1 to blend with them.");
            ImGui.Combo("cell bg type", ref s_tableBackgroundCellType, "None\0Blue\0"); ImGui.SameLine(); HelpMarker("We are colorizing cells to B1->C2 here.");
            Debug.Assert(s_tableBackgroundRowType >= 0 && s_tableBackgroundRowType <= 2);
            Debug.Assert(s_tableBackgroundRowTarget >= 0 && s_tableBackgroundRowTarget <= 1);
            Debug.Assert(s_tableBackgroundCellType >= 0 && s_tableBackgroundCellType <= 1);
            PopStyleCompact();

            if (ImGui.BeginTable("table1", 5, s_tableBackgroundFlags))
            {
                for (int row = 0; row < 6; row++)
                {
                    ImGui.TableNextRow();

                    // Demonstrate setting a row background color with 'ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBgX,.)'
                    // A transparent color is used so the color behind remains visible when RowBg1 is targeted and
                    // ImGuiTableFlags_RowBg already targeted RowBg0.
                    if (s_tableBackgroundRowType != 0)
                    {
                        ImVec4 rowBgColor = s_tableBackgroundRowType == 1
                            ? new ImVec4(0.7f, 0.3f, 0.3f, 0.65f)
                            : new ImVec4(0.2f + row * 0.1f, 0.2f, 0.2f, 0.65f); // Flat or Gradient?
                        ImGui.TableSetBgColor((ImGuiTableBgTarget)((int)ImGuiTableBgTarget.RowBg0 + s_tableBackgroundRowTarget), ImGui.GetColorU32(rowBgColor));
                    }

                    // Fill cells
                    for (int column = 0; column < 5; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("%c%c", (char)('A' + row), (char)('0' + column));

                        // Change background of Cells B1->C2
                        // Demonstrate setting a cell background color with 'ImGui::TableSetBgColor(ImGuiTableBgTarget_CellBg,.)'
                        // (the CellBg color will be blended over the RowBg and ColumnBg colors)
                        if (row >= 1 && row <= 2 && column >= 1 && column <= 2 && s_tableBackgroundCellType == 1)
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new ImVec4(0.3f, 0.3f, 0.7f, 0.65f)));
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Tree view
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Tree view"))
        {
            CheckboxFlags("ImGuiTreeNodeFlags_SpanFullWidth", ref s_tableTreeViewFlags, ImGuiTreeNodeFlags.SpanFullWidth);
            CheckboxFlags("ImGuiTreeNodeFlags_SpanLabelWidth", ref s_tableTreeViewFlags, ImGuiTreeNodeFlags.SpanLabelWidth);
            CheckboxFlags("ImGuiTreeNodeFlags_SpanAllColumns", ref s_tableTreeViewFlags, ImGuiTreeNodeFlags.SpanAllColumns);
            CheckboxFlags("ImGuiTreeNodeFlags_LabelSpanAllColumns", ref s_tableTreeViewFlags, ImGuiTreeNodeFlags.LabelSpanAllColumns);
            ImGui.SameLine(); HelpMarker("Useful if you know that you aren't displaying contents in other columns");

            HelpMarker("See \"Columns flags\" section to configure how indentation is applied to individual columns.");
            if (ImGui.BeginTable("3ways", 3, s_tableTreeViewTableFlags))
            {
                // The first column will use the default _WidthStretch when ScrollX is Off and _WidthFixed when ScrollX is On
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoHide);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, textBaseWidth * 12.0f);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, textBaseWidth * 18.0f);
                ImGui.TableHeadersRow();

                DisplayTableTreeNode(s_tableTreeNodes[0], s_tableTreeNodes, s_tableTreeViewFlags);

                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Item width
        // -------------------------------------------------------------------------
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Item width"))
        {
            HelpMarker(
                "Showcase using PushItemWidth() and how it is preserved on a per-column basis.\n\n" +
                "Note that on auto-resizing non-resizable fixed columns, querying the content width for " +
                "e.g. right-alignment doesn't make sense.");
            if (ImGui.BeginTable("table_item_width", 3, ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn("small");
                ImGui.TableSetupColumn("half");
                ImGui.TableSetupColumn("right-align");
                ImGui.TableHeadersRow();

                for (int row = 0; row < 3; row++)
                {
                    ImGui.TableNextRow();
                    if (row == 0)
                    {
                        // Setup ItemWidth once (instead of setting up every time, which is also possible but less efficient)
                        ImGui.TableSetColumnIndex(0);
                        ImGui.PushItemWidth(textBaseWidth * 3.0f); // Small
                        ImGui.TableSetColumnIndex(1);
                        ImGui.PushItemWidth(-ImGui.GetContentRegionAvail().X * 0.5f);
                        ImGui.TableSetColumnIndex(2);
                        ImGui.PushItemWidth(-float.Epsilon); // Right-aligned
                    }

                    // Draw our contents
                    ImGui.PushID(row);
                    ImGui.TableSetColumnIndex(0);
                    ImGui.SliderFloat("float0", ref s_tableItemWidthDummy, 0.0f, 1.0f);
                    ImGui.TableSetColumnIndex(1);
                    ImGui.SliderFloat("float1", ref s_tableItemWidthDummy, 0.0f, 1.0f);
                    ImGui.TableSetColumnIndex(2);
                    ImGui.SliderFloat("##float2", ref s_tableItemWidthDummy, 0.0f, 1.0f); // No visible label since right-aligned
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // Demonstrate using TableHeader() calls instead of TableHeadersRow()
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Custom headers"))
        {
            const int ColumnsCount = 3;
            if (ImGui.BeginTable("table_custom_headers", ColumnsCount, ImGuiTableFlags.Borders | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable))
            {
                ImGui.TableSetupColumn("Apricot");
                ImGui.TableSetupColumn("Banana");
                ImGui.TableSetupColumn("Cherry");

                // Dummy entire-column selection storage
                // Instead of calling TableHeadersRow() custom headers are submitted manually.
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                for (int column = 0; column < ColumnsCount; column++)
                {
                    ImGui.TableSetColumnIndex(column);
                    string? columnName = ImGui.TableGetColumnName(column); // Retrieve name passed to TableSetupColumn()
                    ImGui.PushID(column);
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(0, 0));
                    ImGui.Checkbox("##checkall", ref s_tableCustomHeaderSelected[column]);
                    ImGui.PopStyleVar();
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                    ImGui.TableHeader(columnName);
                    ImGui.PopID();
                }

                // Submit table contents
                for (int row = 0; row < 5; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < 3; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Selectable($"Cell {column},{row}", s_tableCustomHeaderSelected[column]);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // Demonstrate using ImGuiTableColumnFlags_AngledHeader flag to create angled headers
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Angled headers"))
        {
            string[] columnNames = { "Track", "cabasa", "ride", "smash", "tom-hi", "tom-mid", "tom-low", "hihat-o", "hihat-c", "snare-s", "snare-c", "clap", "rim", "kick" };
            int columnsCount = columnNames.Length;
            int rowsCount = 12;

            CheckboxFlags("_ScrollX", ref s_tableAngledHeaderFlags, ImGuiTableFlags.ScrollX);
            CheckboxFlags("_ScrollY", ref s_tableAngledHeaderFlags, ImGuiTableFlags.ScrollY);
            CheckboxFlags("_Resizable", ref s_tableAngledHeaderFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("_Sortable", ref s_tableAngledHeaderFlags, ImGuiTableFlags.Sortable);
            CheckboxFlags("_NoBordersInBody", ref s_tableAngledHeaderFlags, ImGuiTableFlags.NoBordersInBody);
            CheckboxFlags("_HighlightHoveredColumn", ref s_tableAngledHeaderFlags, ImGuiTableFlags.HighlightHoveredColumn);
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            ImGui.SliderInt("Frozen columns", ref s_tableAngledHeaderFrozenCols, 0, 2);
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            ImGui.SliderInt("Frozen rows", ref s_tableAngledHeaderFrozenRows, 0, 2);
            CheckboxFlags("Disable header contributing to column width", ref s_tableAngledHeaderColumnFlags, ImGuiTableColumnFlags.NoHeaderWidth);

            if (ImGui.TreeNode("Style settings"))
            {
                ImGui.SameLine();
                HelpMarker("Giving access to some ImGuiStyle value in this demo for convenience.");
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
                ImGuiStyle style = ImGui.GetStyle();
                float angledHeadersAngle = style.TableAngledHeadersAngle;
                ImGui.SliderAngle("style.TableAngledHeadersAngle", ref angledHeadersAngle, -50.0f, +50.0f);
                style.TableAngledHeadersAngle = angledHeadersAngle;
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
                float[] angledHeadersTextAlign = { style.TableAngledHeadersTextAlign.X, style.TableAngledHeadersTextAlign.Y };
                ImGui.SliderFloat2("style.TableAngledHeadersTextAlign", angledHeadersTextAlign, 0.0f, 1.0f, "%.2f");
                style.TableAngledHeadersTextAlign = new ImVec2(angledHeadersTextAlign[0], angledHeadersTextAlign[1]);
                ImGui.TreePop();
            }

            if (ImGui.BeginTable("table_angled_headers", columnsCount, s_tableAngledHeaderFlags, new ImVec2(0.0f, textBaseHeight * 12)))
            {
                ImGui.TableSetupColumn(columnNames[0], ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoReorder);
                for (int n = 1; n < columnsCount; n++)
                    ImGui.TableSetupColumn(columnNames[n], s_tableAngledHeaderColumnFlags);
                ImGui.TableSetupScrollFreeze(s_tableAngledHeaderFrozenCols, s_tableAngledHeaderFrozenRows);

                ImGui.TableAngledHeadersRow(); // Draw angled headers for all columns with the ImGuiTableColumnFlags_AngledHeader flag.
                ImGui.TableHeadersRow();       // Draw remaining headers and allow access to context-menu and other functions.
                for (int row = 0; row < rowsCount; row++)
                {
                    ImGui.PushID(row);
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Track %d", row);
                    for (int column = 1; column < columnsCount; column++)
                        if (ImGui.TableSetColumnIndex(column))
                        {
                            ImGui.PushID(column);
                            ImGui.Checkbox("", ref s_tableAngledHeaderBools[row * columnsCount + column]);
                            ImGui.PopID();
                        }
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // Demonstrate creating custom context menus inside columns,
        // while playing it nice with context menus provided by TableHeadersRow()/TableHeader()
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Context menus"))
        {
            HelpMarker(
                "By default, right-clicking over a TableHeadersRow()/TableHeader() line will open the default context-menu.\n" +
                "Using ImGuiTableFlags_ContextMenuInBody we also allow right-clicking over columns body.");
            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_ContextMenuInBody", ref s_tableContextMenuFlags, ImGuiTableFlags.ContextMenuInBody);
            PopStyleCompact();

            // Context Menus: first example
            // [1.1] Right-click on the TableHeadersRow() line to open the default table context menu.
            // [1.2] Right-click in columns also open the default table context menu (if ImGuiTableFlags_ContextMenuInBody is set)
            const int ColumnsCount = 3;
            if (ImGui.BeginTable("table_context_menu", ColumnsCount, s_tableContextMenuFlags))
            {
                ImGui.TableSetupColumn("One");
                ImGui.TableSetupColumn("Two");
                ImGui.TableSetupColumn("Three");

                // [1.1] Right-click on the TableHeadersRow() line to open the default table context menu.
                ImGui.TableHeadersRow();

                // Submit dummy contents
                for (int row = 0; row < 4; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < ColumnsCount; column++)
                    {
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Cell %d,%d", column, row);
                    }
                }
                ImGui.EndTable();
            }

            // Context Menus: second example
            // [2.1] Right-click on the TableHeadersRow() line to open the default table context menu.
            // [2.2] Right-click on the "." to open a custom popup
            // [2.3] Right-click in columns to open another custom popup
            HelpMarker(
                "Demonstrate mixing table context menu (over header), item context button (over button) " +
                "and custom per-column context menu (over column body).");
            ImGuiTableFlags flags2 = ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable | ImGuiTableFlags.Borders;
            if (ImGui.BeginTable("table_context_menu_2", ColumnsCount, flags2))
            {
                ImGui.TableSetupColumn("One");
                ImGui.TableSetupColumn("Two");
                ImGui.TableSetupColumn("Three");

                // [2.1] Right-click on the TableHeadersRow() line to open the default table context menu.
                ImGui.TableHeadersRow();
                for (int row = 0; row < 4; row++)
                {
                    ImGui.TableNextRow();
                    for (int column = 0; column < ColumnsCount; column++)
                    {
                        // Submit dummy contents
                        ImGui.TableSetColumnIndex(column);
                        ImGui.Text("Cell %d,%d", column, row);
                        ImGui.SameLine();

                        // [2.2] Right-click on the "." to open a custom popup
                        ImGui.PushID(row * ColumnsCount + column);
                        ImGui.SmallButton("..");
                        if (ImGui.BeginPopupContextItem())
                        {
                            ImGui.Text("This is the popup for Button(\"..\") in Cell %d,%d", column, row);
                            if (ImGui.Button("Close"))
                                ImGui.CloseCurrentPopup();
                            ImGui.EndPopup();
                        }
                        ImGui.PopID();
                    }
                }

                // [2.3] Right-click anywhere in columns to open another custom popup
                // (instead of testing for !IsAnyItemHovered() the popup could also be opened with
                // ImGuiPopupFlags_NoOpenOverExistingPopup to manage popup priority, as the popup triggers
                // here, "are we hovering a column", are overlapping)
                int hoveredColumn = -1;
                for (int column = 0; column < ColumnsCount + 1; column++)
                {
                    ImGui.PushID(column);
                    if ((ImGui.TableGetColumnFlags(column) & ImGuiTableColumnFlags.IsHovered) != 0)
                        hoveredColumn = column;
                    if (hoveredColumn == column && !ImGui.IsAnyItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                        ImGui.OpenPopup("MyPopup");
                    if (ImGui.BeginPopup("MyPopup"))
                    {
                        if (column == ColumnsCount)
                            ImGui.Text("This is a custom popup for unused space after the last column.");
                        else
                            ImGui.Text("This is a custom popup for Column %d", column);
                        if (ImGui.Button("Close"))
                            ImGui.CloseCurrentPopup();
                        ImGui.EndPopup();
                    }
                    ImGui.PopID();
                }

                ImGui.EndTable();
                ImGui.Text("Hovered column: %d", hoveredColumn);
            }
            ImGui.TreePop();
        }

        // Demonstrate creating multiple tables with the same ID
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Synced instances"))
        {
            HelpMarker("Multiple tables with the same identifier will share their settings, width, visibility, order etc.");

            CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableSyncedFlags, ImGuiTableFlags.Resizable);
            CheckboxFlags("ImGuiTableFlags_ScrollY", ref s_tableSyncedFlags, ImGuiTableFlags.ScrollY);
            CheckboxFlags("ImGuiTableFlags_SizingFixedFit", ref s_tableSyncedFlags, ImGuiTableFlags.SizingFixedFit);
            CheckboxFlags("ImGuiTableFlags_HighlightHoveredColumn", ref s_tableSyncedFlags, ImGuiTableFlags.HighlightHoveredColumn);
            for (int n = 0; n < 3; n++)
            {
                string buf = $"Synced Table {n}";
                bool open = ImGui.CollapsingHeader(buf, ImGuiTreeNodeFlags.DefaultOpen);
                if (open && ImGui.BeginTable("Table", 3, s_tableSyncedFlags, new ImVec2(0.0f, ImGui.GetTextLineHeightWithSpacing() * 5)))
                {
                    ImGui.TableSetupColumn("One");
                    ImGui.TableSetupColumn("Two");
                    ImGui.TableSetupColumn("Three");
                    ImGui.TableHeadersRow();
                    int cellCount = (n == 1) ? 27 : 9; // Make second table have a scrollbar to verify that additional decoration is not affecting column positions.
                    for (int cell = 0; cell < cellCount; cell++)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text("this cell %d", cell);
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Sorting
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Sorting"))
        {
            // Create item list
            if (s_tableItems == null)
            {
                s_tableItems = new List<TableDemoItem>(50);
                for (int n = 0; n < 50; n++)
                {
                    s_tableItems.Add(new TableDemoItem
                    {
                        ID = n,
                        Name = s_templateNames[n % s_templateNames.Length],
                        Quantity = (n * n - n) % 20 // Assign default quantities
                    });
                }
            }

            PushStyleCompact();
            CheckboxFlags("ImGuiTableFlags_SortMulti", ref s_tableSortingFlags, ImGuiTableFlags.SortMulti);
            ImGui.SameLine(); HelpMarker("When sorting is enabled: hold shift when clicking headers to sort on multiple column. TableGetSortSpecs() may return specs where (SpecsCount > 1).");
            CheckboxFlags("ImGuiTableFlags_SortTristate", ref s_tableSortingFlags, ImGuiTableFlags.SortTristate);
            ImGui.SameLine(); HelpMarker("When sorting is enabled: allow no sorting, disable default sorting. TableGetSortSpecs() may return specs where (SpecsCount == 0).");
            PopStyleCompact();

            if (ImGui.BeginTable("table_sorting", 4, s_tableSortingFlags, new ImVec2(0.0f, textBaseHeight * 15), 0.0f))
            {
                // Declare columns
                // The "user_id" parameter of TableSetupColumn() stores an identifier in the sort specifications
                // so the sort function can identify a column given our own identifier; the column index could
                // also be used. Sort-related flags are mixed among the default sort, the no-sort family, and the
                // preferred-direction flags.
                ImGui.TableSetupColumn("ID",       ImGuiTableColumnFlags.DefaultSort          | ImGuiTableColumnFlags.WidthFixed,   0.0f, (uint)TableDemoColumnId.ID);
                ImGui.TableSetupColumn("Name",                                                  ImGuiTableColumnFlags.WidthFixed,   0.0f, (uint)TableDemoColumnId.Name);
                ImGui.TableSetupColumn("Action",   ImGuiTableColumnFlags.NoSort               | ImGuiTableColumnFlags.WidthFixed,   0.0f, (uint)TableDemoColumnId.Action);
                ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.PreferSortDescending | ImGuiTableColumnFlags.WidthStretch, 0.0f, (uint)TableDemoColumnId.Quantity);
                ImGui.TableSetupScrollFreeze(0, 1); // Make row always visible
                ImGui.TableHeadersRow();

                // Sort our data if sort specs have been changed!
                ImGuiTableSortSpecs? sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs != null && sortSpecs.SpecsDirty)
                {
                    SortWithSortSpecs(sortSpecs, s_tableItems);
                    sortSpecs.SpecsDirty = false;
                }

                // Demonstrate using clipper for large vertical lists
                ImGuiListClipper clipper = new();
                clipper.Begin(s_tableItems.Count);
                while (clipper.Step())
                    for (int rowN = clipper.DisplayStart; rowN < clipper.DisplayEnd; rowN++)
                    {
                        // Display a data item
                        TableDemoItem item = s_tableItems[rowN];
                        ImGui.PushID(item.ID);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("%04d", item.ID);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(item.Name);
                        ImGui.TableNextColumn();
                        ImGui.SmallButton("None");
                        ImGui.TableNextColumn();
                        ImGui.Text("%d", item.Quantity);
                        ImGui.PopID();
                    }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        // In this example we'll expose most table flags and settings.
        // For specific flags and settings refer to the corresponding section for more detailed explanation.
        // This section is mostly useful to experiment with combining certain flags or settings with each others.
        if (openAction != -1)
            ImGui.SetNextItemOpen(openAction != 0);
        if (ImGui.TreeNode("Advanced"))
        {
            // Options
            if (ImGui.TreeNode("Options"))
            {
                // Make the UI compact because there are so many fields
                PushStyleCompact();
                ImGui.PushItemWidth(textBaseWidth * 28.0f);

                if (ImGui.TreeNodeEx("Features:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    CheckboxFlags("ImGuiTableFlags_Resizable", ref s_tableAdvancedFlags, ImGuiTableFlags.Resizable);
                    CheckboxFlags("ImGuiTableFlags_Reorderable", ref s_tableAdvancedFlags, ImGuiTableFlags.Reorderable);
                    CheckboxFlags("ImGuiTableFlags_Hideable", ref s_tableAdvancedFlags, ImGuiTableFlags.Hideable);
                    CheckboxFlags("ImGuiTableFlags_Sortable", ref s_tableAdvancedFlags, ImGuiTableFlags.Sortable);
                    CheckboxFlags("ImGuiTableFlags_NoSavedSettings", ref s_tableAdvancedFlags, ImGuiTableFlags.NoSavedSettings);
                    CheckboxFlags("ImGuiTableFlags_ContextMenuInBody", ref s_tableAdvancedFlags, ImGuiTableFlags.ContextMenuInBody);
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Decorations:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    CheckboxFlags("ImGuiTableFlags_RowBg", ref s_tableAdvancedFlags, ImGuiTableFlags.RowBg);
                    CheckboxFlags("ImGuiTableFlags_BordersV", ref s_tableAdvancedFlags, ImGuiTableFlags.BordersV);
                    CheckboxFlags("ImGuiTableFlags_BordersOuterV", ref s_tableAdvancedFlags, ImGuiTableFlags.BordersOuterV);
                    CheckboxFlags("ImGuiTableFlags_BordersInnerV", ref s_tableAdvancedFlags, ImGuiTableFlags.BordersInnerV);
                    CheckboxFlags("ImGuiTableFlags_BordersH", ref s_tableAdvancedFlags, ImGuiTableFlags.BordersH);
                    CheckboxFlags("ImGuiTableFlags_BordersOuterH", ref s_tableAdvancedFlags, ImGuiTableFlags.BordersOuterH);
                    CheckboxFlags("ImGuiTableFlags_BordersInnerH", ref s_tableAdvancedFlags, ImGuiTableFlags.BordersInnerH);
                    CheckboxFlags("ImGuiTableFlags_NoBordersInBody", ref s_tableAdvancedFlags, ImGuiTableFlags.NoBordersInBody); ImGui.SameLine(); HelpMarker("Disable vertical borders in columns Body (borders will always appear in Headers)");
                    CheckboxFlags("ImGuiTableFlags_NoBordersInBodyUntilResize", ref s_tableAdvancedFlags, ImGuiTableFlags.NoBordersInBodyUntilResize); ImGui.SameLine(); HelpMarker("Disable vertical borders in columns Body until hovered for resize (borders will always appear in Headers)");
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Sizing:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    EditTableSizingFlags(ref s_tableAdvancedFlags);
                    ImGui.SameLine(); HelpMarker("In the Advanced demo we override the policy of each column so those table-wide settings have less effect that typical.");
                    CheckboxFlags("ImGuiTableFlags_NoHostExtendX", ref s_tableAdvancedFlags, ImGuiTableFlags.NoHostExtendX);
                    ImGui.SameLine(); HelpMarker("Make outer width auto-fit to columns, overriding outer_size.x value.\n\nOnly available when ScrollX/ScrollY are disabled and Stretch columns are not used.");
                    CheckboxFlags("ImGuiTableFlags_NoHostExtendY", ref s_tableAdvancedFlags, ImGuiTableFlags.NoHostExtendY);
                    ImGui.SameLine(); HelpMarker("Make outer height stop exactly at outer_size.y (prevent auto-extending table past the limit).\n\nOnly available when ScrollX/ScrollY are disabled. Data below the limit will be clipped and not visible.");
                    CheckboxFlags("ImGuiTableFlags_NoKeepColumnsVisible", ref s_tableAdvancedFlags, ImGuiTableFlags.NoKeepColumnsVisible);
                    ImGui.SameLine(); HelpMarker("Only available if ScrollX is disabled.");
                    CheckboxFlags("ImGuiTableFlags_PreciseWidths", ref s_tableAdvancedFlags, ImGuiTableFlags.PreciseWidths);
                    ImGui.SameLine(); HelpMarker("Disable distributing remainder width to stretched columns (width allocation on a 100-wide table with 3 columns: Without this flag: 33,33,34. With this flag: 33,33,33). With larger number of columns, resizing will appear to be less smooth.");
                    CheckboxFlags("ImGuiTableFlags_NoClip", ref s_tableAdvancedFlags, ImGuiTableFlags.NoClip);
                    ImGui.SameLine(); HelpMarker("Disable clipping rectangle for every individual columns (reduce draw command count, items will be able to overflow into other columns). Generally incompatible with ScrollFreeze options.");
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Padding:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    CheckboxFlags("ImGuiTableFlags_PadOuterX", ref s_tableAdvancedFlags, ImGuiTableFlags.PadOuterX);
                    CheckboxFlags("ImGuiTableFlags_NoPadOuterX", ref s_tableAdvancedFlags, ImGuiTableFlags.NoPadOuterX);
                    CheckboxFlags("ImGuiTableFlags_NoPadInnerX", ref s_tableAdvancedFlags, ImGuiTableFlags.NoPadInnerX);
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Scrolling:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    CheckboxFlags("ImGuiTableFlags_ScrollX", ref s_tableAdvancedFlags, ImGuiTableFlags.ScrollX);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.GetFrameHeight());
                    ImGui.DragInt("freeze_cols", ref s_tableAdvancedFreezeCols, 0.2f, 0, 9, null, ImGuiSliderFlags.NoInput);
                    CheckboxFlags("ImGuiTableFlags_ScrollY", ref s_tableAdvancedFlags, ImGuiTableFlags.ScrollY);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.GetFrameHeight());
                    ImGui.DragInt("freeze_rows", ref s_tableAdvancedFreezeRows, 0.2f, 0, 9, null, ImGuiSliderFlags.NoInput);
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Sorting:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    CheckboxFlags("ImGuiTableFlags_SortMulti", ref s_tableAdvancedFlags, ImGuiTableFlags.SortMulti);
                    ImGui.SameLine(); HelpMarker("When sorting is enabled: hold shift when clicking headers to sort on multiple column. TableGetSortSpecs() may return specs where (SpecsCount > 1).");
                    CheckboxFlags("ImGuiTableFlags_SortTristate", ref s_tableAdvancedFlags, ImGuiTableFlags.SortTristate);
                    ImGui.SameLine(); HelpMarker("When sorting is enabled: allow no sorting, disable default sorting. TableGetSortSpecs() may return specs where (SpecsCount == 0).");
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Headers:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Checkbox("show_headers", ref s_tableAdvancedShowHeaders);
                    CheckboxFlags("ImGuiTableFlags_HighlightHoveredColumn", ref s_tableAdvancedFlags, ImGuiTableFlags.HighlightHoveredColumn);
                    CheckboxFlags("ImGuiTableColumnFlags_AngledHeader", ref s_tableAdvancedColumnsBaseFlags, ImGuiTableColumnFlags.AngledHeader);
                    ImGui.SameLine(); HelpMarker("Enable AngledHeader on all columns. Best enabled on selected narrow columns (see \"Angled headers\" section of the demo).");
                    ImGui.TreePop();
                }

                if (ImGui.TreeNodeEx("Other:", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Checkbox("show_wrapped_text", ref s_tableAdvancedShowWrappedText);

                    ImGui.DragFloat2("##OuterSize", s_tableAdvancedOuterSize);
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                    ImGui.Checkbox("outer_size", ref s_tableAdvancedOuterSizeEnabled);
                    ImGui.SameLine();
                    HelpMarker("If scrolling is disabled (ScrollX and ScrollY not set):\n" +
                        "- The table is output directly in the parent window.\n" +
                        "- OuterSize.x < 0.0f will right-align the table.\n" +
                        "- OuterSize.x = 0.0f will narrow fit the table unless there are any Stretch columns.\n" +
                        "- OuterSize.y then becomes the minimum size for the table, which will extend vertically if there are more rows (unless NoHostExtendY is set).");

                    // From a user point of view 'inner_width' is used differently depending on whether the table
                    // is embedding scrolling. To facilitate toying with this demo 0.0f is passed to BeginTable()
                    // when ScrollX is disabled.
                    ImGui.DragFloat("inner_width (when ScrollX active)", ref s_tableAdvancedInnerWidth, 1.0f, 0.0f, float.MaxValue);

                    ImGui.DragFloat("row_min_height", ref s_tableAdvancedRowMinHeight, 1.0f, 0.0f, float.MaxValue);
                    ImGui.SameLine(); HelpMarker("Specify height of the Selectable item.");

                    ImGui.DragInt("items_count", ref s_tableAdvancedItemsCount, 0.1f, 0, 9999);
                    ImGui.Combo("items_type (first column)", ref s_tableAdvancedContentsType, s_tableAdvancedContentsTypeNames, s_tableAdvancedContentsTypeNames.Length);
                    ImGui.TreePop();
                }

                ImGui.PopItemWidth();
                PopStyleCompact();
                ImGui.Spacing();
                ImGui.TreePop();
            }

            // Update item list if we changed the number of items
            if (s_tableAdvancedItems == null || s_tableAdvancedItems.Count != s_tableAdvancedItemsCount)
            {
                s_tableAdvancedItems = new List<TableDemoItem>(s_tableAdvancedItemsCount);
                for (int n = 0; n < s_tableAdvancedItemsCount; n++)
                {
                    int templateN = n % s_templateNames.Length;
                    s_tableAdvancedItems.Add(new TableDemoItem
                    {
                        ID = n,
                        Name = s_templateNames[templateN],
                        Quantity = (templateN == 3) ? 10 : (templateN == 4) ? 20 : 0 // Assign default quantities
                    });
                }
            }

            ImDrawList parentDrawList = ImGui.GetWindowDrawList();
            int parentDrawListDrawCmdCount = parentDrawList.CmdBuffer.Count;
            ImVec2 tableScrollCur = default; // For debug display
            ImVec2 tableScrollMax = default; // "
            ImDrawList? tableDrawList = null; // "

            // Submit table
            float innerWidthToUse = (s_tableAdvancedFlags & ImGuiTableFlags.ScrollX) != 0 ? s_tableAdvancedInnerWidth : 0.0f;
            if (ImGui.BeginTable("table_advanced", 6, s_tableAdvancedFlags, s_tableAdvancedOuterSizeEnabled ? new ImVec2(s_tableAdvancedOuterSize[0], s_tableAdvancedOuterSize[1]) : new ImVec2(0, 0), innerWidthToUse))
            {
                // Declare columns
                // The "user_id" parameter of TableSetupColumn() stores an identifier in the sort specifications
                // so the sort function can identify a column given our own identifier; the column index could
                // also be used.
                ImGui.TableSetupColumn("ID",          s_tableAdvancedColumnsBaseFlags | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, 0.0f, (uint)TableDemoColumnId.ID);
                ImGui.TableSetupColumn("Name",        s_tableAdvancedColumnsBaseFlags | ImGuiTableColumnFlags.WidthFixed, 0.0f, (uint)TableDemoColumnId.Name);
                ImGui.TableSetupColumn("Action",      s_tableAdvancedColumnsBaseFlags | ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 0.0f, (uint)TableDemoColumnId.Action);
                ImGui.TableSetupColumn("Quantity",    s_tableAdvancedColumnsBaseFlags | ImGuiTableColumnFlags.PreferSortDescending, 0.0f, (uint)TableDemoColumnId.Quantity);
                ImGui.TableSetupColumn("Description", s_tableAdvancedColumnsBaseFlags | ((s_tableAdvancedFlags & ImGuiTableFlags.NoHostExtendX) != 0 ? ImGuiTableColumnFlags.None : ImGuiTableColumnFlags.WidthStretch), 0.0f, (uint)TableDemoColumnId.Description);
                ImGui.TableSetupColumn("Hidden",      s_tableAdvancedColumnsBaseFlags | ImGuiTableColumnFlags.DefaultHide | ImGuiTableColumnFlags.NoSort);
                ImGui.TableSetupScrollFreeze(s_tableAdvancedFreezeCols, s_tableAdvancedFreezeRows);

                // Sort our data if sort specs have been changed!
                ImGuiTableSortSpecs? sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs != null && sortSpecs.SpecsDirty)
                    s_tableAdvancedItemsNeedSort = true;
                if (sortSpecs != null && s_tableAdvancedItemsNeedSort && s_tableAdvancedItems.Count > 1)
                {
                    SortWithSortSpecs(sortSpecs, s_tableAdvancedItems);
                    sortSpecs.SpecsDirty = false;
                }
                s_tableAdvancedItemsNeedSort = false;

                // Take note of whether the table is currently sorting on the Quantity field; this is used to
                // trigger sorting when the data of that column has been modified.
                bool sortsSpecsUsingQuantity = (ImGui.TableGetColumnFlags(3) & ImGuiTableColumnFlags.IsSorted) != 0;

                // Show headers
                if (s_tableAdvancedShowHeaders && (s_tableAdvancedColumnsBaseFlags & ImGuiTableColumnFlags.AngledHeader) != 0)
                    ImGui.TableAngledHeadersRow();
                if (s_tableAdvancedShowHeaders)
                    ImGui.TableHeadersRow();

                // Show data using the clipper for large vertical lists
                ImGuiListClipper clipper = new();
                clipper.Begin(s_tableAdvancedItems.Count);
                while (clipper.Step())
                {
                    for (int rowN = clipper.DisplayStart; rowN < clipper.DisplayEnd; rowN++)
                    {
                        TableDemoItem item = s_tableAdvancedItems[rowN];

                        bool itemIsSelected = s_tableAdvancedSelection.Contains(item.ID);
                        ImGui.PushID(item.ID);
                        ImGui.TableNextRow(ImGuiTableRowFlags.None, s_tableAdvancedRowMinHeight);

                        // For the demo purpose the first column can select among different type of items
                        ImGui.TableSetColumnIndex(0);
                        string label = item.ID.ToString("D4");
                        if (s_tableAdvancedContentsType == TableAdvancedContentsText)
                            ImGui.TextUnformatted(label);
                        else if (s_tableAdvancedContentsType == TableAdvancedContentsButton)
                            ImGui.Button(label);
                        else if (s_tableAdvancedContentsType == TableAdvancedContentsSmallButton)
                            ImGui.SmallButton(label);
                        else if (s_tableAdvancedContentsType == TableAdvancedContentsFillButton)
                            ImGui.Button(label, new ImVec2(-float.Epsilon, 0.0f));
                        else if (s_tableAdvancedContentsType == TableAdvancedContentsSelectable || s_tableAdvancedContentsType == TableAdvancedContentsSelectableSpanRow)
                        {
                            ImGuiSelectableFlags selectableFlags = (s_tableAdvancedContentsType == TableAdvancedContentsSelectableSpanRow) ? ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap : ImGuiSelectableFlags.None;
                            if (ImGui.Selectable(label, itemIsSelected, selectableFlags, new ImVec2(0, s_tableAdvancedRowMinHeight)))
                            {
                                if (ImGui.GetIO().KeyCtrl)
                                {
                                    if (itemIsSelected)
                                        s_tableAdvancedSelection.Remove(item.ID);
                                    else
                                        s_tableAdvancedSelection.Add(item.ID);
                                }
                                else
                                {
                                    s_tableAdvancedSelection.Clear();
                                    s_tableAdvancedSelection.Add(item.ID);
                                }
                            }
                        }

                        if (ImGui.TableSetColumnIndex(1))
                            ImGui.TextUnformatted(item.Name);

                        // The data set is marked as needing to be sorted again if a quantity was modified and the
                        // table is currently sorted on the Quantity column. The sort is triggered only once the
                        // button has been released.
                        if (ImGui.TableSetColumnIndex(2))
                        {
                            if (ImGui.SmallButton("Chop")) { item.Quantity += 1; }
                            if (sortsSpecsUsingQuantity && ImGui.IsItemDeactivated()) { s_tableAdvancedItemsNeedSort = true; }
                            ImGui.SameLine();
                            if (ImGui.SmallButton("Eat")) { item.Quantity -= 1; }
                            if (sortsSpecsUsingQuantity && ImGui.IsItemDeactivated()) { s_tableAdvancedItemsNeedSort = true; }
                        }

                        if (ImGui.TableSetColumnIndex(3))
                            ImGui.Text("%d", item.Quantity);

                        ImGui.TableSetColumnIndex(4);
                        if (s_tableAdvancedShowWrappedText)
                            ImGui.TextWrapped("Lorem ipsum dolor sit amet");
                        else
                            ImGui.Text("Lorem ipsum dolor sit amet");

                        if (ImGui.TableSetColumnIndex(5))
                            ImGui.Text("1234");

                        ImGui.PopID();
                    }
                }

                // Store some info to display debug details below
                tableScrollCur = new ImVec2(ImGui.GetScrollX(), ImGui.GetScrollY());
                tableScrollMax = new ImVec2(ImGui.GetScrollMaxX(), ImGui.GetScrollMaxY());
                tableDrawList = ImGui.GetWindowDrawList();
                ImGui.EndTable();
            }
            ImGui.Checkbox("Debug details", ref s_tableAdvancedShowDebugDetails);
            if (s_tableAdvancedShowDebugDetails && tableDrawList != null)
            {
                ImGui.SameLine(0.0f, 0.0f);
                int tableDrawListDrawCmdCount = tableDrawList.CmdBuffer.Count;
                if (ReferenceEquals(tableDrawList, parentDrawList))
                    ImGui.Text(": DrawCmd: +%d (in same window)", tableDrawListDrawCmdCount - parentDrawListDrawCmdCount);
                else
                    ImGui.Text(": DrawCmd: +%d (in child window), Scroll: (%.f/%.f) (%.f/%.f)", tableDrawListDrawCmdCount - 1, tableScrollCur.X, tableScrollMax.X, tableScrollCur.Y, tableScrollMax.Y);
            }
            ImGui.TreePop();
        }

        ImGui.PopID();

        // Canonical calls the legacy Columns showcase at the end of the Tables & Columns section
        DemoWindowColumns();

        if (s_tableDisableIndent)
            ImGui.PopStyleVar();
    }

    #endregion

    #region Inputs & Focus (Lines 8084-8390)

    private static int s_inputCaptureOverrideMouse = -1;
    private static int s_inputCaptureOverrideKeyboard = -1;
    private static string s_tabBuf = "hello";
    private static string s_focusBuf = "click on a button to set focus";
    private static readonly float[] s_focusFloat3 = new float[3];

    /// <summary>
    /// Routing options toggled by the "Shortcuts" demonstration. Persists across frames because the
    /// canonical demonstration uses a function-local static.
    /// </summary>
    private static ImGuiInputFlags s_routeOptions = ImGuiInputFlags.Repeat;

    /// <summary>
    /// Selected shortcut routing policy for the "Shortcuts" demonstration. Persists across frames because
    /// the canonical demonstration uses a function-local static
    /// </summary>
    private static ImGuiInputFlags s_routeType = ImGuiInputFlags.RouteFocused;

    /// <summary>
    /// Factor value driven by the <c>Alt+F</c> shortcut example. Starts at the canonical
    /// <c>0.5f</c>.
    /// </summary>
    private static float s_shortcutFactor = 0.5f;

    /// <summary>
    /// Demonstrates mouse/keyboard input telemetry, capture override flags, focus navigation, and mouse dragging.
    /// </summary>
    public static void DemoWindowInputs()
    {
        if (!ImGui.CollapsingHeader("Inputs & Focus"))
            return;

        ImGuiIO io = ImGui.GetIO();

        // -------------------------------------------------------------------------
        // Inputs display
        // -------------------------------------------------------------------------
        ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        bool inputsOpened = ImGui.TreeNode("Inputs");
        ImGui.SameLine();
        HelpMarker(
            "This is a simplified view. See more detailed input state:\n" +
            "- in 'Tools->Metrics/Debugger->Inputs'.\n" +
            "- in 'Tools->Debug Log->IO'.");
        if (inputsOpened)
        {
            if (ImGui.IsMousePosValid())
                ImGui.Text($"Mouse pos: ({io.MousePos.X:F1}, {io.MousePos.Y:F1})");
            else
                ImGui.Text("Mouse pos: <INVALID>");

            ImGui.Text($"Mouse delta: ({io.MouseDelta.X:F1}, {io.MouseDelta.Y:F1})");
            ImGui.Text("Mouse down:");
            for (int i = 0; i < io.MouseDown.Length; i++)
            {
                if (ImGui.IsMouseDown((ImGuiMouseButton)i))
                {
                    ImGui.SameLine();
                    ImGui.Text($"b{i} ({io.MouseDownDuration[i]:F2}s)");
                }
            }

            ImGui.Text($"Mouse wheel: {io.MouseWheel:F1}");
            ImGui.Text("Mouse clicked count:");
            for (int i = 0; i < io.MouseDown.Length; i++)
            {
                if (io.MouseClickedCount[i] > 0)
                {
                    ImGui.SameLine();
                    ImGui.Text($"b{i}: {io.MouseClickedCount[i]}");
                }
            }

            // Only named keys are enumerated; user key indices are deliberately excluded.
            ImGui.Text("Keys down:");
            for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
            {
                if (!ImGui.IsKeyDown(key))
                    continue;
                ImGui.SameLine();
                ImGui.Text($"\"{ImGui.GetKeyName(key)}\" {(int)key}");
            }

            ImGui.Text($"Keys mods: {(io.KeyCtrl ? "CTRL " : "")}{(io.KeyShift ? "SHIFT " : "")}{(io.KeyAlt ? "ALT " : "")}{(io.KeySuper ? "SUPER " : "")}");
            ImGui.Text("Chars queue:");
            for (int i = 0; i < io.InputQueueCharacters.Count; i++)
            {
                uint c = io.InputQueueCharacters[i];
                ImGui.SameLine();
                ImGui.Text($"'{((c > ' ' && c <= 255) ? (char)c : '?')}' (0x{c:X4})");
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Outputs display
        // -------------------------------------------------------------------------
        ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        bool outputsOpened = ImGui.TreeNode("Outputs");
        ImGui.SameLine();
        HelpMarker(
            "The value of io.WantCaptureMouse and io.WantCaptureKeyboard are normally set by Dear ImGui " +
            "to instruct your application of how to route inputs. Typically, when a value is true, it means " +
            "Dear ImGui wants the corresponding inputs and we expect the underlying application to ignore them.\n\n" +
            "The most typical case is: when hovering a window, Dear ImGui set io.WantCaptureMouse to true, " +
            "and underlying application should ignore mouse inputs (in practice there are many and more subtle " +
            "rules leading to how those flags are set).");
        if (outputsOpened)
        {
            ImGui.Text($"io.WantCaptureMouse: {io.WantCaptureMouse}");
            ImGui.Text($"io.WantCaptureMouseUnlessPopupClose: {io.WantCaptureMouseUnlessPopupClose}");
            ImGui.Text($"io.WantCaptureKeyboard: {io.WantCaptureKeyboard}");
            ImGui.Text($"io.WantTextInput: {io.WantTextInput}");
            ImGui.Text($"io.WantSetMousePos: {io.WantSetMousePos}");
            ImGui.Text($"io.NavActive: {io.NavActive}, io.NavVisible: {io.NavVisible}");

            if (ImGui.TreeNode("WantCapture override"))
            {
                HelpMarker("Hovering the colored canvas overrides io.WantCaptureXXX fields.");
                string[] overrideDesc = { "None", "Set to false", "Set to true" };

                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 15);
                ImGui.SliderInt("SetNextFrameWantCaptureMouse() on hover", ref s_inputCaptureOverrideMouse, -1, 1, overrideDesc[s_inputCaptureOverrideMouse + 1], ImGuiSliderFlags.AlwaysClamp);

                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 15);
                ImGui.SliderInt("SetNextFrameWantCaptureKeyboard() on hover", ref s_inputCaptureOverrideKeyboard, -1, 1, overrideDesc[s_inputCaptureOverrideKeyboard + 1], ImGuiSliderFlags.AlwaysClamp);

                ImGui.ColorButton("##panel", new ImVec4(0.7f, 0.1f, 0.7f, 1.0f), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new ImVec2(128.0f, 96.0f));
                if (ImGui.IsItemHovered())
                {
                    if (s_inputCaptureOverrideMouse != -1)
                        ImGui.SetNextFrameWantCaptureMouse(s_inputCaptureOverrideMouse == 1);
                    if (s_inputCaptureOverrideKeyboard != -1)
                        ImGui.SetNextFrameWantCaptureKeyboard(s_inputCaptureOverrideKeyboard == 1);
                }

                ImGui.TreePop();
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Shortcuts
        // -------------------------------------------------------------------------
        // Demonstrate using Shortcut() and Routing Policies.
        // The general flow is:
        // - Code interested in a chord (e.g. "Ctrl+A") declares their intent.
        // - Multiple locations may be interested in same chord! Routing helps find a winner.
        // - Every frame, we resolve all claims and assign one owner if the modifiers are matching.
        // - The lower-level function is 'bool SetShortcutRouting()', returns true when caller got the route.
        // - Most of the times, SetShortcutRouting() is not called directly. User mostly calls Shortcut() with routing flags.
        // - If you call Shortcut() WITHOUT any routing option, it uses ImGuiInputFlags_RouteFocused.
        // TL;DR: Most uses will simply be:
        // - Shortcut(ImGuiMod_Ctrl | ImGuiKey_A); // Use ImGuiInputFlags_RouteFocused policy.
        if (ImGui.TreeNode("Shortcuts"))
        {
            CheckboxFlags("ImGuiInputFlags_Repeat", ref s_routeOptions, ImGuiInputFlags.Repeat);
            if (ImGui.RadioButton("ImGuiInputFlags_RouteActive", s_routeType == ImGuiInputFlags.RouteActive)) { s_routeType = ImGuiInputFlags.RouteActive; }
            if (ImGui.RadioButton("ImGuiInputFlags_RouteFocused (default)", s_routeType == ImGuiInputFlags.RouteFocused)) { s_routeType = ImGuiInputFlags.RouteFocused; }
            ImGui.Indent();
            ImGui.BeginDisabled(s_routeType != ImGuiInputFlags.RouteFocused);
            CheckboxFlags("ImGuiInputFlags_RouteOverActive##0", ref s_routeOptions, ImGuiInputFlags.RouteOverActive);
            ImGui.EndDisabled();
            ImGui.Unindent();
            if (ImGui.RadioButton("ImGuiInputFlags_RouteGlobal", s_routeType == ImGuiInputFlags.RouteGlobal)) { s_routeType = ImGuiInputFlags.RouteGlobal; }
            ImGui.Indent();
            ImGui.BeginDisabled(s_routeType != ImGuiInputFlags.RouteGlobal);
            CheckboxFlags("ImGuiInputFlags_RouteOverFocused", ref s_routeOptions, ImGuiInputFlags.RouteOverFocused);
            CheckboxFlags("ImGuiInputFlags_RouteOverActive", ref s_routeOptions, ImGuiInputFlags.RouteOverActive);
            CheckboxFlags("ImGuiInputFlags_RouteUnlessBgFocused", ref s_routeOptions, ImGuiInputFlags.RouteUnlessBgFocused);
            ImGui.EndDisabled();
            ImGui.Unindent();
            if (ImGui.RadioButton("ImGuiInputFlags_RouteAlways", s_routeType == ImGuiInputFlags.RouteAlways)) { s_routeType = ImGuiInputFlags.RouteAlways; }
            ImGuiInputFlags flags = s_routeType | s_routeOptions; // Merged flags.
            if (s_routeType != ImGuiInputFlags.RouteGlobal)
                flags &= ~(ImGuiInputFlags.RouteOverFocused | ImGuiInputFlags.RouteOverActive | ImGuiInputFlags.RouteUnlessBgFocused);

            ImGui.SeparatorText("Using SetNextItemShortcut()");
            ImGui.Text("Ctrl+S");
            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.S, flags | ImGuiInputFlags.Tooltip);
            ImGui.Button("Save");
            ImGui.Text("Alt+F");
            ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Alt | ImGuiKey.F, flags | ImGuiInputFlags.Tooltip);
            ImGui.SliderFloat("Factor", ref s_shortcutFactor, 0.0f, 1.0f);

            ImGui.SeparatorText("Using Shortcut()");
            float lineHeight = ImGui.GetTextLineHeightWithSpacing();
            ImGuiKey keyChord = ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.A;

            ImGui.Text("Ctrl+A");
            ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}, Shortcut: {(ImGui.Shortcut(keyChord, flags) ? "PRESSED" : "...")}");

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new ImVec4(1.0f, 0.0f, 1.0f, 0.1f));

            ImGui.BeginChild("WindowA", new ImVec2(-float.Epsilon, lineHeight * 14), ImGuiChildFlags.Borders);
            ImGui.Text("Press Ctrl+A and see who receives it!");
            ImGui.Separator();

            // 1: Window polling for Ctrl+A.
            ImGui.Text("(in WindowA)");
            ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}, Shortcut: {(ImGui.Shortcut(keyChord, flags) ? "PRESSED" : "...")}");

            // 2: InputText also polling for Ctrl+A: it always uses _RouteFocused internally (gets priority when active).
            // (Commented because the owner-aware version of Shortcut() is internal.)
            //string str = "Press Ctrl+A";
            //ImGui.Spacing();
            //ImGui.InputText("InputTextB", ref str, ImGuiInputTextFlags.ReadOnly);
            //uint itemId = ImGui.GetItemID();
            //ImGui.SameLine(); HelpMarker("Internal widgets always use _RouteFocused");
            //ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}, Shortcut: {(ImGui.Shortcut(keyChord, flags, itemId) ? "PRESSED" : ".")}");

            // 3: Dummy child is not claiming the route: focusing them shouldn't steal route away from WindowA.
            ImGui.BeginChild("ChildD", new ImVec2(-float.Epsilon, lineHeight * 4), ImGuiChildFlags.Borders);
            ImGui.Text("(in ChildD: not using same Shortcut)");
            ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}");
            ImGui.EndChild();

            // 4: Child window polling for Ctrl+A. It is deeper than WindowA and gets priority when focused.
            ImGui.BeginChild("ChildE", new ImVec2(-float.Epsilon, lineHeight * 4), ImGuiChildFlags.Borders);
            ImGui.Text("(in ChildE: using same Shortcut)");
            ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}, Shortcut: {(ImGui.Shortcut(keyChord, flags) ? "PRESSED" : "...")}");
            ImGui.EndChild();

            // 5: In a popup.
            if (ImGui.Button("Open Popup"))
                ImGui.OpenPopup("PopupF");
            if (ImGui.BeginPopup("PopupF"))
            {
                ImGui.Text("(in PopupF)");
                ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}, Shortcut: {(ImGui.Shortcut(keyChord, flags) ? "PRESSED" : "...")}");
                // (Commented because the owner-aware version of Shortcut() is internal.)
                //ImGui.InputText("InputTextG", ref str, ImGuiInputTextFlags.ReadOnly);
                //ImGui.Text($"IsWindowFocused: {ImGui.IsWindowFocused()}, Shortcut: {(ImGui.Shortcut(keyChord, flags, ImGui.GetItemID()) ? "PRESSED" : ".")}");
                ImGui.EndPopup();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Mouse Cursors
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Mouse Cursors"))
        {
            string[] cursorNames = { "Arrow", "TextInput", "ResizeAll", "ResizeNS", "ResizeEW", "ResizeNESW", "ResizeNWSE", "Hand", "Wait", "Progress", "NotAllowed" };
            ImGuiMouseCursor current = ImGui.GetMouseCursor();
            string curName = (int)current >= 0 && (int)current < cursorNames.Length ? cursorNames[(int)current] : "N/A";
            ImGui.Text($"Current mouse cursor = {(int)current}: {curName}");

            // The disabled checkbox mirrors the read-only backend capability flag and is shown for reference only.
            ImGui.BeginDisabled(true);
            ImGuiBackendFlags backendFlags = io.BackendFlags;
            CheckboxFlags("io.BackendFlags: HasMouseCursors", ref backendFlags, ImGuiBackendFlags.HasMouseCursors);
            ImGui.EndDisabled();
            io.BackendFlags = backendFlags;

            ImGui.Text("Hover to see mouse cursors:");
            ImGui.SameLine();
            HelpMarker(
                "Your application can render a different mouse cursor based on what ImGui.GetMouseCursor() returns. " +
                "If software cursor rendering (io.MouseDrawCursor) is set ImGui will draw the right cursor for you, " +
                "otherwise your backend needs to handle it.");

            for (int i = 0; i < cursorNames.Length; i++)
            {
                ImGui.Bullet();
                ImGui.Selectable($"Mouse cursor {i}: {cursorNames[i]}", false);
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor((ImGuiMouseCursor)i);
            }

            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Tabbing
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Tabbing"))
        {
            ImGui.Text("Use Tab/Shift+Tab to cycle through keyboard editable fields.");
            ImGui.InputText("1", ref s_tabBuf);
            ImGui.InputText("2", ref s_tabBuf);
            ImGui.InputText("3", ref s_tabBuf);
            ImGui.PushItemFlag(ImGuiItemFlags.NoTabStop, true);
            ImGui.InputText("4 (tab skip)", ref s_tabBuf);
            ImGui.SameLine(); HelpMarker("Item won't be cycled through when using TAB or Shift+Tab.");
            ImGui.PopItemFlag();
            ImGui.InputText("5", ref s_tabBuf);
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Focus from code
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Focus from code"))
        {
            bool focus1 = ImGui.Button("Focus on 1"); ImGui.SameLine();
            bool focus2 = ImGui.Button("Focus on 2"); ImGui.SameLine();
            bool focus3 = ImGui.Button("Focus on 3");

            int hasFocus = 0;
            if (focus1) ImGui.SetKeyboardFocusHere();
            ImGui.InputText("1", ref s_focusBuf);
            if (ImGui.IsItemActive()) hasFocus = 1;

            if (focus2) ImGui.SetKeyboardFocusHere();
            ImGui.InputText("2", ref s_focusBuf);
            if (ImGui.IsItemActive()) hasFocus = 2;

            ImGui.PushItemFlag(ImGuiItemFlags.NoTabStop, true);
            if (focus3) ImGui.SetKeyboardFocusHere();
            ImGui.InputText("3 (tab skip)", ref s_focusBuf);
            if (ImGui.IsItemActive()) hasFocus = 3;
            ImGui.SameLine(); HelpMarker("Item won't be cycled through when using TAB or Shift+Tab.");
            ImGui.PopItemFlag();

            ImGui.Text(hasFocus != 0 ? $"Item with focus: {hasFocus}" : "Item with focus: <none>");

            int focusAhead = -1;
            if (ImGui.Button("Focus on X")) { focusAhead = 0; } ImGui.SameLine();
            if (ImGui.Button("Focus on Y")) { focusAhead = 1; } ImGui.SameLine();
            if (ImGui.Button("Focus on Z")) { focusAhead = 2; }
            if (focusAhead != -1) ImGui.SetKeyboardFocusHere(focusAhead);

            ImGui.SliderFloat3("Float3", s_focusFloat3, 0.0f, 1.0f);

            ImGui.TextWrapped("NB: Cursor & selection are preserved when refocusing last used item in code.");
            ImGui.TreePop();
        }

        // -------------------------------------------------------------------------
        // Dragging
        // -------------------------------------------------------------------------
        if (ImGui.TreeNode("Dragging"))
        {
            ImGui.TextWrapped("You can use ImGui::GetMouseDragDelta(0) to query for the dragged amount on any widget.");
            for (int button = 0; button < 3; button++)
            {
                ImGui.Text($"IsMouseDragging({button}):");
                ImGui.Text($"  w/ default threshold: {ImGui.IsMouseDragging((ImGuiMouseButton)button)},");
                ImGui.Text($"  w/ zero threshold: {ImGui.IsMouseDragging((ImGuiMouseButton)button, 0.0f)},");
                ImGui.Text($"  w/ large threshold: {ImGui.IsMouseDragging((ImGuiMouseButton)button, 20.0f)},");
            }

            ImGui.Button("Drag Me");
            if (ImGui.IsItemActive())
            {
                ImGui.GetForegroundDrawList().AddLine(io.MouseClickedPos[0], io.MousePos, ImGui.GetColorU32(ImGuiCol.Button), 4.0f);
            }

            // Drag operations unlock once the mouse moves past io.MouseDragThreshold; a lower or higher
            // threshold can be requested through the second parameter of IsMouseDragging() and GetMouseDragDelta().
            ImVec2 valueRaw = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0.0f);
            ImVec2 valueWithLockThreshold = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            ImVec2 mouseDelta = io.MouseDelta;
            ImGui.Text("GetMouseDragDelta(0):");
            ImGui.Text($"  w/ default threshold: ({valueWithLockThreshold.X:F1}, {valueWithLockThreshold.Y:F1})");
            ImGui.Text($"  w/ zero threshold: ({valueRaw.X:F1}, {valueRaw.Y:F1})");
            ImGui.Text($"io.MouseDelta: ({mouseDelta.X:F1}, {mouseDelta.Y:F1})");
            ImGui.TreePop();
        }
    }

    #endregion

    #region Example Apps (Chunk 1-5 subset)

    // -----------------------------------------------------------------------------
    // Example App: Simple Layout
    // -----------------------------------------------------------------------------
    private static int s_appLayoutSelected = 0;

    /// <summary>
    /// Demonstrates master-detail two-pane layout using child windows.
    /// </summary>
    /// <param name="pOpen">Window open state reference.</param>
    public static void ShowExampleAppLayout(ref bool pOpen)
    {
        ImGui.SetNextWindowSize(new ImVec2(500, 440), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Example: Simple layout", ref pOpen, ImGuiWindowFlags.MenuBar))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Close", "Ctrl+W"))
                    pOpen = false;
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }

        // Left pane
        {
            ImGui.BeginChild("left pane", new ImVec2(150, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX);
            for (int i = 0; i < 100; i++)
            {
                if (ImGui.Selectable($"MyObject {i}", s_appLayoutSelected == i, ImGuiSelectableFlags.SelectOnNav))
                    s_appLayoutSelected = i;
            }
            ImGui.EndChild();
        }

        ImGui.SameLine();

        // Right pane
        {
            ImGui.BeginGroup();
            ImGui.BeginChild("item view", new ImVec2(0, -ImGui.GetFrameHeightWithSpacing()));
            ImGui.Text($"MyObject: {s_appLayoutSelected}");
            ImGui.Separator();
            ImGui.TextWrapped("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.");
            ImGui.Spacing();
            ImGui.Text($"ID: 0123456789-{s_appLayoutSelected:D4}");
            ImGui.EndChild();

            if (ImGui.Button("Revert")) { }
            ImGui.SameLine();
            if (ImGui.Button("Save")) { }
            ImGui.EndGroup();
        }

        ImGui.End();
    }

    // -----------------------------------------------------------------------------
    // Example App: Long Text
    // -----------------------------------------------------------------------------
    private static int s_longTextTestType = 0;
    private static readonly StringBuilder s_longTextLog = new();
    private static int s_longTextLines = 0;

    /// <summary>
    /// Demonstrates rendering large volumes of text and observes scrolling clipping behavior.
    /// </summary>
    /// <param name="pOpen">Window open state reference.</param>
    public static void ShowExampleAppLongText(ref bool pOpen)
    {
        ImGui.SetNextWindowSize(new ImVec2(520, 600), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Example: Long text display", ref pOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Printing unusually long amount of text.");
        string[] types = { "Single call to TextUnformatted()", "Multiple calls to Text(), clipped", "Multiple calls to Text(), not clipped (slow)" };
        ImGui.Combo("Test type", ref s_longTextTestType, types);
        ImGui.Text($"Buffer contents: {s_longTextLines} lines, {s_longTextLog.Length} bytes");

        if (ImGui.Button("Clear"))
        {
            s_longTextLog.Clear();
            s_longTextLines = 0;
        }
        ImGui.SameLine();
        if (ImGui.Button("Add 1000 lines"))
        {
            for (int i = 0; i < 1000; i++)
                s_longTextLog.Append($"{s_longTextLines + i} The quick brown fox jumps over the lazy dog\n");
            s_longTextLines += 1000;
        }

        ImGui.BeginChild("Log");
        switch (s_longTextTestType)
        {
            case 0:
                ImGui.TextUnformatted(s_longTextLog.ToString());
                break;
            case 1:
                // Multiple calls to Text(), manually coarsely clipped with ImGuiListClipper.
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(0, 0));
                ImGuiListClipper clipper = new();
                clipper.Begin(s_longTextLines);
                while (clipper.Step())
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        ImGui.Text($"{i} The quick brown fox jumps over the lazy dog");
                ImGui.PopStyleVar();
                break;
            case 2:
                // Multiple calls to Text(), not clipped (slow).
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(0, 0));
                for (int i = 0; i < s_longTextLines; i++)
                    ImGui.Text($"{i} The quick brown fox jumps over the lazy dog");
                ImGui.PopStyleVar();
                break;
        }
        ImGui.EndChild();

        ImGui.End();
    }

    // -----------------------------------------------------------------------------
    // Example App: Auto Resize
    // -----------------------------------------------------------------------------
    private static int s_autoResizeLines = 10;

    /// <summary>
    /// Demonstrates creating a window that dynamically resizes to fit its contents each frame.
    /// </summary>
    /// <param name="pOpen">Window open state reference.</param>
    public static void ShowExampleAppAutoResize(ref bool pOpen)
    {
        if (!ImGui.Begin("Example: Auto-resizing window", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted(
            "Window will resize every-frame to the size of its content.\n" +
            "Note that you probably don't want to query the window size to\n" +
            "output your content because that would create a feedback loop.");

        ImGui.SliderInt("Number of lines", ref s_autoResizeLines, 1, 20);
        for (int i = 0; i < s_autoResizeLines; i++)
            ImGui.Text($"{new string(' ', i * 4)}This is line {i}");

        ImGui.End();
    }

    // -----------------------------------------------------------------------------
    // Example App: Simple Overlay
    // -----------------------------------------------------------------------------
    private static int s_overlayLocation = 0;

    /// <summary>
    /// Demonstrates creating a static HUD overlay pinned to screen corners.
    /// </summary>
    /// <param name="pOpen">Window open state reference.</param>
    public static void ShowExampleAppSimpleOverlay(ref bool pOpen)
    {
        ImGuiIO io = ImGui.GetIO();
        ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        if (s_overlayLocation >= 0)
        {
            const float PAD = 10.0f;
            ImGuiViewport viewport = ImGui.GetMainViewport();
            ImVec2 workPos = viewport.WorkPos;
            ImVec2 workSize = viewport.WorkSize;
            ImVec2 windowPos = new(
                (s_overlayLocation & 1) != 0 ? (workPos.X + workSize.X - PAD) : (workPos.X + PAD),
                (s_overlayLocation & 2) != 0 ? (workPos.Y + workSize.Y - PAD) : (workPos.Y + PAD));
            ImVec2 windowPosPivot = new(
                (s_overlayLocation & 1) != 0 ? 1.0f : 0.0f,
                (s_overlayLocation & 2) != 0 ? 1.0f : 0.0f);

            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, windowPosPivot);
            ImGui.SetNextWindowViewport(viewport.ID);
            windowFlags |= ImGuiWindowFlags.NoMove;
        }
        else if (s_overlayLocation == -2)
        {
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new ImVec2(0.5f, 0.5f));
            windowFlags |= ImGuiWindowFlags.NoMove;
        }

        ImGui.SetNextWindowBgAlpha(0.35f);
        if (ImGui.Begin("Example: Simple overlay", ref pOpen, windowFlags))
        {
            ImGui.Text("Simple overlay\n(right-click to change position)");
            ImGui.Separator();
            if (ImGui.IsMousePosValid())
                ImGui.Text($"Mouse Position: ({io.MousePos.X:F1},{io.MousePos.Y:F1})");
            else
                ImGui.Text("Mouse Position: <invalid>");

            if (ImGui.BeginPopupContextWindow())
            {
                if (ImGui.MenuItem("Custom", null, s_overlayLocation == -1)) s_overlayLocation = -1;
                if (ImGui.MenuItem("Center", null, s_overlayLocation == -2)) s_overlayLocation = -2;
                if (ImGui.MenuItem("Top-left", null, s_overlayLocation == 0)) s_overlayLocation = 0;
                if (ImGui.MenuItem("Top-right", null, s_overlayLocation == 1)) s_overlayLocation = 1;
                if (ImGui.MenuItem("Bottom-left", null, s_overlayLocation == 2)) s_overlayLocation = 2;
                if (ImGui.MenuItem("Bottom-right", null, s_overlayLocation == 3)) s_overlayLocation = 3;
                if (ImGui.MenuItem("Close")) pOpen = false;
                ImGui.EndPopup();
            }
        }
        ImGui.End();
    }

    // -----------------------------------------------------------------------------
    // Example App: Custom Rendering using ImDrawList API
    // -----------------------------------------------------------------------------
    private static float s_customSz = 42.0f;
    private static float s_customThickness = 3.0f;
    private static int s_customNgonSides = 6;
    private static bool s_customCircleOverride = false;
    private static int s_customCircleOverrideV = 12;
    private static bool s_customCurveOverride = false;
    private static int s_customCurveOverrideV = 8;
    private static ImVec4 s_customColor = new(1.0f, 1.0f, 0.4f, 1.0f);
    private static ImDrawFlags s_customStrokeFlags = ImDrawFlags.None;
    private static ImDrawFlags s_customOtherFlags = ImDrawFlags.AALineEnds;
    private static ImDrawFlags s_customScopeFlags = ImDrawFlags.AALines | ImDrawFlags.AAFill;

    private static readonly List<ImVec2> s_canvasPoints = new();
    private static ImVec2 s_canvasScrolling = Vector2.Zero;
    private static bool s_canvasEnableGrid = true;
    private static bool s_canvasEnableContextMenu = true;
    private static bool s_canvasAddingLine = false;

    private static bool s_customDrawBg = true;
    private static bool s_customDrawFg = true;

    private static void PathConcaveShape(ImDrawList drawList, float x, float y, float sz)
    {
        ReadOnlySpan<ImVec2> posNorms = stackalloc ImVec2[]
        {
            new(0.0f, 0.0f), new(0.3f, 0.0f), new(0.3f, 0.7f), new(0.7f, 0.7f),
            new(0.7f, 0.0f), new(1.0f, 0.0f), new(1.0f, 1.0f), new(0.0f, 1.0f)
        };
        for (int i = 0; i < posNorms.Length; i++)
            drawList.PathLineTo(new ImVec2(x + 0.5f + (int)(sz * posNorms[i].X), y + 0.5f + (int)(sz * posNorms[i].Y)));
    }

    /// <summary>
    /// Demonstrates direct 2D vector drawing using <see cref="ImDrawList"/> primitives,
    /// multi-channel rendering, and an interactive panning/drawing canvas.
    /// </summary>
    /// <param name="pOpen">Window open state reference.</param>
    public static void ShowExampleAppCustomRendering(ref bool pOpen)
    {
        if (!ImGui.Begin("Example: Custom rendering", ref pOpen))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("##TabBar"))
        {
            if (ImGui.BeginTabItem("Primitives"))
            {
                RenderCustomPrimitivesTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Canvas"))
            {
                RenderCustomCanvasTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("BG/FG draw lists"))
            {
                RenderCustomBgFgTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Draw Channels"))
            {
                RenderCustomChannelsTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private static void RenderCustomPrimitivesTab()
    {
        ImDrawList drawList = ImGui.GetWindowDrawList();

        ImGui.Text("Gradients");
        ImVec2 gradientSize = new(ImGui.CalcItemWidth(), ImGui.GetFrameHeight());
        {
            ImVec2 p0 = ImGui.GetCursorScreenPos();
            ImVec2 p1 = new(p0.X + gradientSize.X, p0.Y + gradientSize.Y);
            drawList.AddRectFilledMultiColor(p0, p1, 0xFF000000, 0xFFFFFFFF, 0xFFFFFFFF, 0xFF000000);
            ImGui.InvisibleButton("##gradient1", gradientSize);
        }
        {
            ImVec2 p0 = ImGui.GetCursorScreenPos();
            ImVec2 p1 = new(p0.X + gradientSize.X, p0.Y + gradientSize.Y);
            drawList.AddRectFilledMultiColor(p0, p1, 0xFF00FF00, 0xFF0000FF, 0xFF0000FF, 0xFF00FF00);
            ImGui.InvisibleButton("##gradient2", gradientSize);
        }

        ImGui.Text("All primitives");
        ImGui.DragFloat("Size", ref s_customSz, 0.2f, 2.0f, 100.0f, "%.0f");
        ImGui.DragFloat("Thickness", ref s_customThickness, 0.05f, 0.0f, 32.0f, "%.02f");
        ImGui.SliderInt("N-gon sides", ref s_customNgonSides, 3, 12);
        ImGui.Checkbox("##circlesegoverride", ref s_customCircleOverride);
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        s_customCircleOverride |= ImGui.SliderInt("Circle segments override", ref s_customCircleOverrideV, 3, 40);
        ImGui.Checkbox("##curvesegoverride", ref s_customCurveOverride);
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        s_customCurveOverride |= ImGui.SliderInt("Curves segments override", ref s_customCurveOverrideV, 3, 40);
        ColorEdit4("Color", ref s_customColor);

        ImGui.SeparatorText("Per primitive flags (AddXXX functions)");
        int strokeFlags = (int)s_customStrokeFlags;
        if (ImGui.RadioButton("Default", ref strokeFlags, (int)ImDrawFlags.None)) s_customStrokeFlags = (ImDrawFlags)strokeFlags; ImGui.SameLine();
        if (ImGui.RadioButton("Inside", ref strokeFlags, (int)ImDrawFlags.StrokeInside)) s_customStrokeFlags = (ImDrawFlags)strokeFlags; ImGui.SameLine();
        if (ImGui.RadioButton("Center", ref strokeFlags, (int)ImDrawFlags.StrokeCenter)) s_customStrokeFlags = (ImDrawFlags)strokeFlags; ImGui.SameLine();
        if (ImGui.RadioButton("CenterBiased", ref strokeFlags, (int)ImDrawFlags.StrokeCenterBiased)) s_customStrokeFlags = (ImDrawFlags)strokeFlags; ImGui.SameLine();
        if (ImGui.RadioButton("Outside", ref strokeFlags, (int)ImDrawFlags.StrokeOutside)) s_customStrokeFlags = (ImDrawFlags)strokeFlags; ImGui.SameLine();
        if (ImGui.RadioButton("Legacy", ref strokeFlags, (int)ImDrawFlags.StrokeLegacy)) s_customStrokeFlags = (ImDrawFlags)strokeFlags;

        CheckboxFlags("AAFill", ref s_customOtherFlags, ImDrawFlags.AAFill); ImGui.SameLine();
        CheckboxFlags("AALines", ref s_customOtherFlags, ImDrawFlags.AALines); ImGui.SameLine();
        CheckboxFlags("AALineEnds", ref s_customOtherFlags, ImDrawFlags.AALineEnds); ImGui.SameLine();
        CheckboxFlags("JoinMiter", ref s_customOtherFlags, ImDrawFlags.JoinMiter); ImGui.SameLine();
        CheckboxFlags("CapSquare", ref s_customOtherFlags, ImDrawFlags.CapSquare);

        ImGui.SeparatorText("Current ImDrawList flags (e.g. PushDrawFlag function)");
        ImGui.PushID("ScopeFlags");
        CheckboxFlags("AAFill", ref s_customScopeFlags, ImDrawFlags.AAFill); ImGui.SameLine();
        CheckboxFlags("AALines", ref s_customScopeFlags, ImDrawFlags.AALines); ImGui.SameLine();
        CheckboxFlags("AALineEnds", ref s_customScopeFlags, ImDrawFlags.AALineEnds); ImGui.SameLine();
        CheckboxFlags("StrokeLegacy", ref s_customScopeFlags, ImDrawFlags.StrokeLegacy);
        ImGui.PopID();
        ImGui.Spacing();

        drawList.PushDrawFlag(ImDrawFlags.AllowInPushScope_, false);
        drawList.PushDrawFlag(s_customScopeFlags, true);
        ImDrawFlags flags = s_customStrokeFlags | s_customOtherFlags;

        ImVec2 p = ImGui.GetCursorScreenPos();
        uint col = ImGui.ColorConvertFloat4ToU32(s_customColor);
        const float spacing = 10.0f;
        float sz = s_customSz;
        float thickness = s_customThickness;
        int circleSegments = s_customCircleOverride ? s_customCircleOverrideV : 0;
        int curveSegments = s_customCurveOverride ? s_customCurveOverrideV : 0;
        float rounding = sz / 5.0f;
        ImDrawFlags cornersTlBr = ImDrawFlags.RoundCornersTopLeft | ImDrawFlags.RoundCornersBottomRight;

        ImVec2[] cp3 = { new(0.0f, sz * 0.6f), new(sz * 0.5f, -sz * 0.4f), new(sz, sz) };
        ImVec2[] cp4 = { new(0.0f, 0.0f), new(sz * 1.3f, sz * 0.3f), new(sz - sz * 1.3f, sz - sz * 0.3f), new(sz, sz) };

        float x = p.X + 4.0f;
        float y = p.Y + 4.0f;

        // Outlined shapes
        for (int n = 0; n < 2; n++)
        {
            float th = (n == 0) ? 1.0f : thickness;
            drawList.AddNgon(new ImVec2(x + sz * 0.5f, y + sz * 0.5f), sz * 0.5f, col, s_customNgonSides, th, flags); x += sz + spacing;
            drawList.AddCircle(new ImVec2(x + sz * 0.5f, y + sz * 0.5f), sz * 0.5f, col, circleSegments, th, flags); x += sz + spacing;
            drawList.AddEllipse(new ImVec2(x + sz * 0.5f, y + sz * 0.5f), new ImVec2(sz * 0.5f, sz * 0.3f), col, -0.3f, circleSegments, th, flags); x += sz + spacing;
            drawList.AddRect(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col, 0.0f, th, flags); x += sz + spacing;
            drawList.AddRect(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col, rounding, th, flags); x += sz + spacing;
            drawList.AddRect(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col, rounding, th, cornersTlBr | flags); x += sz + spacing;
            drawList.AddTriangle(new ImVec2(x + sz * 0.5f, y), new ImVec2(x + sz, y + sz - 0.5f), new ImVec2(x, y + sz - 0.5f), col, th, flags); x += sz + spacing;

            PathConcaveShape(drawList, x, y, sz);
            drawList.PathStroke(col, th, ImDrawFlags.Closed | flags);
            x += sz + spacing;

            drawList.AddLineH(x, x + sz, y, col, th, flags); x += sz + spacing;
            drawList.AddLineV(x, y, y + sz, col, th, flags); x += spacing;
            drawList.AddLine(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col, th, flags); x += sz + spacing;

            drawList.AddBezierQuadratic(new ImVec2(x + cp3[0].X, y + cp3[0].Y), new ImVec2(x + cp3[1].X, y + cp3[1].Y), new ImVec2(x + cp3[2].X, y + cp3[2].Y), col, th, curveSegments, flags);
            x += sz + spacing;
            drawList.AddBezierCubic(new ImVec2(x + cp4[0].X, y + cp4[0].Y), new ImVec2(x + cp4[1].X, y + cp4[1].Y), new ImVec2(x + cp4[2].X, y + cp4[2].Y), new ImVec2(x + cp4[3].X, y + cp4[3].Y), col, th, curveSegments, flags);

            x = p.X + 4.0f;
            y += sz + spacing;
        }

        // Filled shapes
        drawList.AddNgonFilled(new ImVec2(x + sz * 0.5f, y + sz * 0.5f), sz * 0.5f, col, s_customNgonSides); x += sz + spacing;
        drawList.AddCircleFilled(new ImVec2(x + sz * 0.5f, y + sz * 0.5f), sz * 0.5f, col, circleSegments); x += sz + spacing;
        drawList.AddEllipseFilled(new ImVec2(x + sz * 0.5f, y + sz * 0.5f), new ImVec2(sz * 0.5f, sz * 0.3f), col, -0.3f, circleSegments); x += sz + spacing;
        drawList.AddRectFilled(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col); x += sz + spacing;
        drawList.AddRectFilled(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col, 10.0f); x += sz + spacing;
        drawList.AddRectFilled(new ImVec2(x, y), new ImVec2(x + sz, y + sz), col, 10.0f, cornersTlBr); x += sz + spacing;
        drawList.AddTriangleFilled(new ImVec2(x + sz * 0.5f, y), new ImVec2(x + sz, y + sz - 0.5f), new ImVec2(x, y + sz - 0.5f), col); x += sz + spacing;

        PathConcaveShape(drawList, x, y, sz);
        drawList.PathFillConcave(col);
        x += sz + spacing;

        drawList.AddRectFilled(new ImVec2(x, y), new ImVec2(x + sz, y + thickness), col); x += sz + spacing;
        drawList.AddRectFilled(new ImVec2(x, y), new ImVec2(x + thickness, y + sz), col); x += spacing * 2.0f;
        drawList.AddRectFilled(new ImVec2(x, y), new ImVec2(x + 1, y + 1), col); x += sz;

        drawList.AddRectFilledMultiColor(new ImVec2(x, y), new ImVec2(x + sz, y + sz), 0xFF000000, 0xFF0000FF, 0xFF00FFFF, 0xFF00FF00);

        drawList.PopDrawFlag();
        drawList.PopDrawFlag();

        ImGui.Dummy(new ImVec2((sz + spacing) * 13.2f, (sz + spacing) * 3.0f));
    }

    private static void RenderCustomCanvasTab()
    {
        ImGuiIO io = ImGui.GetIO();
        ImDrawList drawList = ImGui.GetWindowDrawList();

        ImGui.Checkbox("Enable grid", ref s_canvasEnableGrid);
        ImGui.Checkbox("Enable context menu", ref s_canvasEnableContextMenu);
        ImGui.Text("Mouse Left: drag to add lines,\nMouse Right: drag to scroll, click for context menu.");

        ImVec2 canvasP0 = ImGui.GetCursorScreenPos();
        ImVec2 canvasSz = ImGui.GetContentRegionAvail();
        if (canvasSz.X < 50.0f) canvasSz.X = 50.0f;
        if (canvasSz.Y < 50.0f) canvasSz.Y = 50.0f;
        ImVec2 canvasP1 = new(canvasP0.X + canvasSz.X, canvasP0.Y + canvasSz.Y);

        drawList.AddRectFilled(canvasP0, canvasP1, 0xFF323232);
        drawList.AddRect(canvasP0, canvasP1, 0xFFFFFFFF);

        ImGui.InvisibleButton("canvas", canvasSz, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool isHovered = ImGui.IsItemHovered();
        bool isActive = ImGui.IsItemActive();
        ImVec2 origin = new(canvasP0.X + s_canvasScrolling.X, canvasP0.Y + s_canvasScrolling.Y);
        ImVec2 mousePosInCanvas = new(io.MousePos.X - origin.X, io.MousePos.Y - origin.Y);

        if (isHovered && !s_canvasAddingLine && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            s_canvasPoints.Add(mousePosInCanvas);
            s_canvasPoints.Add(mousePosInCanvas);
            s_canvasAddingLine = true;
        }
        if (s_canvasAddingLine)
        {
            s_canvasPoints[^1] = mousePosInCanvas;
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                s_canvasAddingLine = false;
        }

        float mouseThresholdForPan = s_canvasEnableContextMenu ? -1.0f : 0.0f;
        if (isActive && ImGui.IsMouseDragging(ImGuiMouseButton.Right, mouseThresholdForPan))
        {
            s_canvasScrolling.X += io.MouseDelta.X;
            s_canvasScrolling.Y += io.MouseDelta.Y;
        }

        ImVec2 dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right);
        if (s_canvasEnableContextMenu && dragDelta.X == 0.0f && dragDelta.Y == 0.0f)
            ImGui.OpenPopupOnItemClick("context", ImGuiPopupFlags.MouseButtonRight);

        if (ImGui.BeginPopup("context"))
        {
            if (s_canvasAddingLine && s_canvasPoints.Count >= 2)
                s_canvasPoints.RemoveRange(s_canvasPoints.Count - 2, 2);
            s_canvasAddingLine = false;

            if (ImGui.MenuItem("Remove one", null, false, s_canvasPoints.Count > 0))
            {
                if (s_canvasPoints.Count >= 2)
                    s_canvasPoints.RemoveRange(s_canvasPoints.Count - 2, 2);
            }
            if (ImGui.MenuItem("Remove all", null, false, s_canvasPoints.Count > 0))
                s_canvasPoints.Clear();

            ImGui.EndPopup();
        }

        drawList.PushClipRect(canvasP0, canvasP1, true);
        if (s_canvasEnableGrid)
        {
            const float GRID_STEP = 64.0f;
            for (float gx = s_canvasScrolling.X % GRID_STEP; gx < canvasSz.X; gx += GRID_STEP)
                drawList.AddLineV(canvasP0.X + gx, canvasP0.Y, canvasP1.Y, 0x28C8C8C8);
            for (float gy = s_canvasScrolling.Y % GRID_STEP; gy < canvasSz.Y; gy += GRID_STEP)
                drawList.AddLineH(canvasP0.X, canvasP1.X, canvasP0.Y + gy, 0x28C8C8C8);
        }

        for (int n = 0; n + 1 < s_canvasPoints.Count; n += 2)
        {
            drawList.AddLine(
                new ImVec2(origin.X + s_canvasPoints[n].X, origin.Y + s_canvasPoints[n].Y),
                new ImVec2(origin.X + s_canvasPoints[n + 1].X, origin.Y + s_canvasPoints[n + 1].Y),
                0xFF00FFFF, 2.0f);
        }
        drawList.PopClipRect();
    }

    private static void RenderCustomBgFgTab()
    {
        ImGui.Checkbox("Draw in Background draw list", ref s_customDrawBg);
        ImGui.SameLine(); HelpMarker("Renders below every ImGui window.");
        ImGui.Checkbox("Draw in Foreground draw list", ref s_customDrawFg);
        ImGui.SameLine(); HelpMarker("Renders over every ImGui window.");

        ImVec2 winPos = ImGui.GetWindowPos();
        ImVec2 winSize = ImGui.GetWindowSize();
        ImVec2 center = new(winPos.X + winSize.X * 0.5f, winPos.Y + winSize.Y * 0.5f);

        if (s_customDrawBg)
            ImGui.GetBackgroundDrawList().AddCircle(center, winSize.X * 0.6f, 0xC80000FF, 0, 14.0f);
        if (s_customDrawFg)
            ImGui.GetForegroundDrawList().AddCircle(center, winSize.Y * 0.6f, 0xC800FF00, 0, 10.0f);
    }

    private static void RenderCustomChannelsTab()
    {
        ImDrawList drawList = ImGui.GetWindowDrawList();

        ImGui.Text("Blue shape is drawn first: appears in back");
        ImGui.Text("Red shape is drawn after: appears in front");
        ImVec2 p0 = ImGui.GetCursorScreenPos();
        drawList.AddRectFilled(new ImVec2(p0.X, p0.Y), new ImVec2(p0.X + 50, p0.Y + 50), 0xFFFF0000); // Blue (ABGR)
        drawList.AddRectFilled(new ImVec2(p0.X + 25, p0.Y + 25), new ImVec2(p0.X + 75, p0.Y + 75), 0xFF0000FF); // Red
        ImGui.Dummy(new ImVec2(75, 75));

        ImGui.Separator();

        ImGui.Text("Blue shape is drawn first into channel 1: appears in front");
        ImGui.Text("Red shape is drawn after into channel 0: appears in back");
        ImVec2 p1 = ImGui.GetCursorScreenPos();

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        drawList.AddRectFilled(new ImVec2(p1.X, p1.Y), new ImVec2(p1.X + 50, p1.Y + 50), 0xFFFF0000);
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(new ImVec2(p1.X + 25, p1.Y + 25), new ImVec2(p1.X + 75, p1.Y + 75), 0xFF0000FF);
        drawList.ChannelsMerge();

        ImGui.Dummy(new ImVec2(75, 75));
        ImGui.Text("After reordering, contents of channel 0 appears below channel 1.");
    }

    #endregion
}
