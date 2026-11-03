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
/// ImGui Demo sections covering all canonical widget showcases.
/// Ports canonical Dear ImGui implementation.
/// </summary>
public static partial class ImGuiDemo
{
    #region [SECTION] Widgets State Holders

    // DemoWindowWidgetsBasic state.
    private static int s_basicClicked = 0;
    private static bool s_basicCheck = true;
    private static int s_basicRadioE = 0;
    private static int s_basicCounter = 0;
    private static string s_basicStr0 = "Hello, world!";
    private static string s_basicStr1 = "";
    private static int s_basicI0 = 123;
    private static float s_basicF0 = 0.001f;
    private static double s_basicD0 = 999999.00000001;
    private static float s_basicF1 = 1.0e10f;
    private static readonly float[] s_basicVec4a = new float[4] { 0.10f, 0.20f, 0.30f, 0.44f };
    private static int s_basicDragI1 = 50;
    private static int s_basicDragI2 = 42;
    private static int s_basicDragI3 = 128;
    private static float s_basicDragF1 = 1.00f;
    private static float s_basicDragF2 = 0.0067f;
    private static int s_basicSliderI1 = 0;
    private static float s_basicSliderF1 = 0.123f;
    private static float s_basicSliderF2 = 0.0f;
    private static float s_basicSliderAngle = 0.0f;
    private static int s_basicSliderElem = 0;
    private static readonly string[] s_basicElemNames = new[] { "Fire", "Earth", "Air", "Water" };
    private static readonly float[] s_basicColor1 = new float[3] { 1.0f, 0.0f, 0.2f };
    private static readonly float[] s_basicColor2 = new float[4] { 0.4f, 0.7f, 0.0f, 0.5f };
    private static int s_basicComboItemCurrent = 0;
    private static readonly string[] s_basicComboItems = new[] { "AAAA", "BBBB", "CCCC", "DDDD", "EEEE", "FFFF", "GGGG", "HHHH", "IIIIIII", "JJJJ", "KKKKKKK" };
    private static int s_basicListBoxItemCurrent = 1;
    private static readonly string[] s_basicListBoxItems = new[] { "Apple", "Banana", "Cherry", "Kiwi", "Mango", "Orange", "Pineapple", "Strawberry", "Watermelon" };

    // DemoWindowWidgetsCollapsingHeaders state.
    private static bool s_collapseClosableGroup = true;

    // DemoWindowWidgetsColorAndPickers state.
    private static readonly float[] s_colorPickerColor = new float[4] { 114.0f / 255.0f, 144.0f / 255.0f, 154.0f / 255.0f, 200.0f / 255.0f };
    private static ImGuiColorEditFlags s_colorPickerBaseFlags = ImGuiColorEditFlags.None;
    private static bool s_savedPaletteInit = true;
    private static readonly ImVec4[] s_savedPalette = new ImVec4[32];
    private static ImVec4 s_backupColor = new(0, 0, 0, 0);
    private static bool s_colorNoBorder = false;
    private static bool s_refColor = false;
    private static ImVec4 s_refColorV = new(1.0f, 0.0f, 1.0f, 0.5f);
    private static int s_pickerMode = 0;
    private static int s_displayMode = 0;
    private static ImGuiColorEditFlags s_colorPickerFlags = ImGuiColorEditFlags.AlphaBar;
    private static readonly float[] s_colorHsv = new float[4] { 0.23f, 1.0f, 1.0f, 1.0f };

    // DemoWindowWidgetsComboBoxes state.
    private static ImGuiComboFlags s_comboFlags = ImGuiComboFlags.None;
    private static int s_comboSelectedIdx = 0;
    private static string s_comboFilter = "";
    private static int s_comboItemCurrent2 = 0;
    private static int s_comboItemCurrent3 = -1;
    private static readonly string[] s_comboItems = new[] { "AAAA", "BBBB", "CCCC", "DDDD", "EEEE", "FFFF", "GGGG", "HHHH", "IIII", "JJJJ", "KKKK", "LLLLLLL", "MMMM", "OOOOOOO" };

    // DemoWindowWidgetsDragAndDrop state.
    // The item-name array of the canonical copy/swap demo is mutable because Move blanks the source
    // entry and Swap exchanges two entries.
    private static readonly float[] s_dragAndDropCol1 = new float[3] { 1.0f, 0.0f, 0.2f };
    private static readonly float[] s_dragAndDropCol2 = new float[4] { 0.4f, 0.7f, 0.0f, 0.5f };
    private static int s_dragAndDropMode = DragAndDropModeCopy;
    private static readonly string[] s_dragAndDropNames = new[]
    {
        "Bobby", "Beatrice", "Betty",
        "Brianna", "Barry", "Bernard",
        "Bibi", "Blaine", "Bryn"
    };
    private static readonly string[] s_dragAndDropItemNames = new[] { "Item One", "Item Two", "Item Three", "Item Four", "Item Five" };
    private static ImVec4 s_dragAndDropCol4 = new(1.0f, 0.0f, 0.2f, 1.0f);

    /// <summary>Copy mode for the drag-and-drop copy/swap demo (canonical <c>Mode_Copy</c>).</summary>
    private const int DragAndDropModeCopy = 0;

    /// <summary>Move mode for the drag-and-drop copy/swap demo (canonical <c>Mode_Move</c>).</summary>
    private const int DragAndDropModeMove = 1;

    /// <summary>Swap mode for the drag-and-drop copy/swap demo (canonical <c>Mode_Swap</c>).</summary>
    private const int DragAndDropModeSwap = 2;

    // DemoWindowWidgetsDragsAndSliders state.
    private static ImGuiSliderFlags s_dragSliderFlags = ImGuiSliderFlags.None;
    private static float s_dragF = 0.5f;
    private static readonly float[] s_dragF4 = new float[4];
    private static int s_dragI = 50;
    private static float s_sliderF = 0.5f;
    private static readonly float[] s_sliderF4 = new float[4];
    private static int s_sliderI = 50;

    // DemoWindowWidgetsMixedValues state.
    private static bool s_mixedLiveEdit = false;
    private static readonly float[] s_mixedItems = new float[3] { 12.0f, 0.0f, 0.0f };
    private static bool s_mixedCheckboxOn = true;
    private static bool s_mixedCheckboxOff = false;
    private static int s_mixedComboIdx = 0;
    private static readonly string[] s_mixedComboItems = new string[] { "One", "Two", "Three" };
    private static readonly float[] s_mixedColor = new float[4] { 0.5f, 0.5f, 0.5f, 0.5f };

    // DemoWindowWidgetsMultiComponents state.
    private static readonly float[] s_multiVec4f = new float[4] { 0.10f, 0.20f, 0.30f, 0.44f };
    private static readonly int[] s_multiVec4i = new int[4] { 1, 5, 100, 255 };
    private static ImGuiSliderFlags s_multiFlags = ImGuiSliderFlags.None;
    private static float s_multiRangeBeginF = 10.0f;
    private static float s_multiRangeEndF = 90.0f;
    private static int s_multiRangeBeginI = 100;
    private static int s_multiRangeEndI = 1000;

    // DemoWindowWidgetsPlotting state.
    private static bool s_plotAnimate = true;
    private static readonly float[] s_plotArr = new float[] { 0.6f, 0.1f, 1.0f, 0.5f, 0.92f, 0.1f, 0.2f };
    private static readonly float[] s_plotValues = new float[90];
    private static int s_plotValuesOffset = 0;
    private static double s_plotRefreshTime = 0.0;
    private static float s_plotPhase = 0.0f;
    private static int s_plotFuncType = 0;
    private static int s_plotDisplayCount = 70;

    // DemoWindowWidgetsProgressBars state.
    private static float s_progressAccum = 0.0f;
    private static float s_progressDir = 1.0f;

    // DemoWindowWidgetsQueryingStatuses state.
    private static int s_queryItemType = 4;
    private static bool s_queryItemDisabled = false;
    private static bool s_queryLiveEditFlagsOverride = false;
    private static ImGuiItemFlags s_queryLiveEditFlags = ImGuiItemFlags.None;
    private static bool s_queryB = false;
    private static readonly float[] s_queryCol4f = new float[4] { 1.0f, 0.5f, 0.0f, 1.0f };
    private static string s_queryStr = "";
    private static int s_queryComboCurrent = 1;
    private static int s_queryListBoxCurrent = 1;
    private static bool s_queryEmbedAll = false;
    private static bool s_queryTestWindow = false;
    private static string s_queryUnusedBuf = "";
    private static readonly string[] s_queryItemNames = new[]
    {
        "Text", "Button", "Button (w/ repeat)", "Checkbox", "SliderFloat", "InputText", "InputTextMultiline", "InputFloat",
        "InputFloat3", "ColorEdit4", "Selectable", "MenuItem", "TreeNode", "TreeNode (w/ double-click)", "Combo", "ListBox"
    };

    // DemoWindowWidgetsSelectables state.
    private static readonly bool[] s_selBasicSelection = new bool[5] { false, true, false, false, false };
    private static readonly bool[] s_selLineSelected = new bool[3];
    private static readonly bool[] s_selChecked = new bool[5];
    private static int s_selSelectedN = 0;
    private static readonly bool[] s_selTable1 = new bool[10];
    private static readonly bool[] s_selTable2 = new bool[10];
    private static readonly byte[,] s_selGrid = new byte[4, 4] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
    private static readonly bool[] s_selAlignSelected = new bool[9] { true, false, true, false, true, false, true, false, true };

    // DemoWindowWidgetsSelectionAndMultiSelect state.
    // The name list is shared by every list, table, checkbox, and dual-list-box subsection.
    private static readonly string[] s_selectionExampleNames = new[]
    {
        "Artichoke", "Arugula", "Asparagus", "Avocado", "Bamboo Shoots", "Bean Sprouts", "Beans", "Beet", "Belgian Endive", "Bell Pepper",
        "Bitter Gourd", "Bok Choy", "Broccoli", "Brussels Sprouts", "Burdock Root", "Cabbage", "Calabash", "Capers", "Carrot", "Cassava",
        "Cauliflower", "Celery", "Celery Root", "Celcuce", "Chayote", "Chinese Broccoli", "Corn", "Cucumber"
    };

    // Single-Select and manual multi-select state.
    private static int s_selectionSingleSelected = -1;
    private static readonly bool[] s_selectionManualSelected = new bool[5];

    // Multi-Select (basic) state.
    private static readonly ImGuiSelectionBasicStorage s_selectionBasic = new();

    // Multi-Select (with clipper) state.
    private static readonly ImGuiSelectionBasicStorage s_selectionClipper = new();

    // Multi-Select (with deletion) state. Item identifiers are persistent so focus survives deletion.
    private static readonly List<uint> s_selectionDeletionItems = new();
    private static uint s_selectionDeletionItemsNextId = 0;
    private static readonly ExampleSelectionWithDeletion s_selectionDeletion = new();

    // Multi-Select (dual list box) state.
    private static readonly ExampleDualListBox s_selectionDualListBox = new();

    // Multi-Select (in a table) state.
    private static readonly ImGuiSelectionBasicStorage s_selectionTable = new();

    // Multi-Select (checkboxes) state.
    private static readonly bool[] s_selectionCheckboxItems = new bool[20];
    private static ImGuiMultiSelectFlags s_selectionCheckboxFlags =
        ImGuiMultiSelectFlags.NoAutoSelect | ImGuiMultiSelectFlags.NoAutoClear | ImGuiMultiSelectFlags.ClearOnEscape;

    // Multi-Select (multiple scopes) state; one selection store per scope.
    private static readonly ImGuiSelectionBasicStorage[] s_selectionScopes = { new(), new(), new() };
    private static ImGuiMultiSelectFlags s_selectionScopesFlags =
        ImGuiMultiSelectFlags.ScopeRect | ImGuiMultiSelectFlags.ClearOnEscape;

    // Multi-Select (trees) state. The selection store is the persistent selection of the tree nodes.
    // Canonical caches the demo tree in demo_data->DemoTree; the port has no such field, so the root
    // is created once and cached in this static for the lifetime of the application.
    private static readonly ImGuiSelectionBasicStorage s_selectionTree = new();
    private static ExampleTreeNode? s_selectionTreeRoot;

    // Multi-Select (advanced) state.
    private static bool s_selectionAdvancedUseClipper = true;
    private static bool s_selectionAdvancedUseDeletion = true;
    private static bool s_selectionAdvancedUseDragDrop = true;
    private static bool s_selectionAdvancedShowInTable = false;
    private static bool s_selectionAdvancedShowColorButton = true;
    private static ImGuiMultiSelectFlags s_selectionAdvancedFlags =
        ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.BoxSelect1d;
    private static SelectionWidgetType s_selectionAdvancedWidgetType = SelectionWidgetType.Selectable;
    private static readonly List<int> s_selectionAdvancedItems = new();
    private static int s_selectionAdvancedItemsNextId = 0;
    private static readonly ExampleSelectionWithDeletion s_selectionAdvancedSelection = new();
    private static bool s_selectionAdvancedRequestDeletionFromMenu = false;

    // DemoWindowWidgetsText state.
    private static float s_textCustomSize = 16.0f;
    private static float s_textCustomScale = 1.0f;
    private static float s_textWrapWidth = 200.0f;
    private static string s_textUtf8Buf = "\u65E5\u672C\u8A9E";

    // DemoWindowWidgetsTextFilter state. The filter owns its editable
    // buffer, so a single instance is reused across frames rather than recreated.
    private static readonly ImGuiTextFilter s_textFilter = new();

    // DemoWindowWidgetsTextInput state.
    private static string s_inputMultiText =
        "/*\n" +
        " The Pentium F00F bug, shorthand for F0 0F C7 C8,\n" +
        " the hexadecimal encoding of one offending instruction,\n" +
        " more formally, the invalid operand with locked CMPXCHG8B\n" +
        " instruction bug, is a design flaw in the majority of\n" +
        " Intel Pentium, Pentium MMX, and Pentium OverDrive\n" +
        " processors (all in the P5 microarchitecture).\n" +
        "*/\n\n" +
        "label:\n" +
        "\tlock cmpxchg8b eax\n";
    private static ImGuiInputTextFlags s_inputMultiFlags = ImGuiInputTextFlags.AllowTabInput;
    private static string s_inputBuf1 = "";
    private static string s_inputBuf2 = "";
    private static string s_inputBuf3 = "";
    private static string s_inputBuf4 = "";
    private static string s_inputBuf5 = "";
    private static readonly byte[] s_inputCbBufCasing = new byte[32];
    private static readonly byte[] s_inputCbBufImGuiLetters = new byte[32];
    private static string s_inputPassword = "password123";
    private static readonly byte[] s_inputCbBuf1 = new byte[64];
    private static readonly byte[] s_inputCbBuf2 = new byte[64];
    private static readonly byte[] s_inputCbBuf3 = new byte[64];
    private static int s_inputCbEditCount = 0;
    private static string s_inputResizeStr = "";
    private static ImGuiInputTextFlags s_inputResizeFlags = ImGuiInputTextFlags.None;
    private static string s_inputElideBuf = "/path/to/some/folder/with/long/filename.cpp";
    private static ImGuiInputTextFlags s_inputElideFlags = ImGuiInputTextFlags.ElideLeft;
    private static string s_inputMiscBuf = "";
    private static ImGuiInputTextFlags s_inputMiscFlags = ImGuiInputTextFlags.EscapeClearsAll;

    // DemoWindowWidgetsTooltips state.
    private static int s_tooltipAlwaysOn = 0;
    private static readonly float[] s_tooltipCurveArr = new float[] { 0.6f, 0.1f, 1.0f, 0.5f, 0.92f, 0.1f, 0.2f };

    // DemoWindowWidgetsTreeNodes state.
    private static ImGuiTreeNodeFlags s_treeHierBaseFlags = ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DefaultOpen;
    private static int s_treeSelectionMask = 0x00;
    private static ImGuiTreeNodeFlags s_treeAdvBaseFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
    private static bool s_treeAdvAlignLabel = false;
    private static bool s_treeAdvUseDragDrop = false;

    // DemoWindowWidgetsVerticalSliders state.
    private static int s_vsliderIntValue = 0;
    private static readonly float[] s_vsliderValues = new float[7] { 0.0f, 0.60f, 0.35f, 0.9f, 0.70f, 0.20f, 0.0f };
    private static readonly float[] s_vsliderValues2 = new float[4] { 0.20f, 0.80f, 0.40f, 0.25f };

    // DemoWindowWidgetsDataTypes state.
    // One backing value per canonical data type; the width and signedness determine which typed
    // DragScalar/SliderScalar/InputScalar overload receives it.
    private static sbyte s_dataTypesS8V = 127;
    private static byte s_dataTypesU8V = 255;
    private static short s_dataTypesS16V = 32767;
    private static ushort s_dataTypesU16V = 65535;
    private static int s_dataTypesS32V = -1;
    private static uint s_dataTypesU32V = unchecked((uint)-1);
    private static long s_dataTypesS64V = -1;
    private static ulong s_dataTypesU64V = unchecked((ulong)-1);
    private static float s_dataTypesF32V = 0.123f;
    private static double s_dataTypesF64V = 90000.01234567890123456789;
    private static bool s_dataTypesDragClamp = false;
    private static bool s_dataTypesInputsStep = true;
    private static ImGuiInputTextFlags s_dataTypesInputFlags = ImGuiInputTextFlags.None;

    // DemoWindowWidgetsImages state.
    // The interactive viewer keeps its own pan/zoom state so the widget section and the standalone
    // image viewer example do not share a viewport. The pressed counter accumulates ImageButton hits.
    private static readonly ExampleImageViewerData s_imagesViewer = new();
    private static int s_imagesPressedCount = 0;

    // DemoWindowWidgetsListBoxes state.
    // Selection is stored as an index shared by both list boxes; the highlight index is per frame and
    // therefore stays local to the method.
    private static int s_listBoxesSelectedIdx = 0;
    private static bool s_listBoxesItemHighlight = false;

    // DemoWindowWidgetsLiveEdit state.
    // Backing variables edited while typing when the live-edit item flags are active.
    private static string s_liveEditStr = "";
    private static int s_liveEditInt = 0;
    private static float s_liveEditFloat = 0.0f;

    #endregion

    #region [SECTION] DemoWindowWidgetsBasic()

    /// <summary>
    /// Demonstrates foundational interactive widgets: buttons, checkboxes, radio buttons, repeating buttons, inputs, drags, sliders, and combos.
    /// </summary>
    public static void DemoWindowWidgetsBasic()
    {
        if (ImGui.TreeNode("Basic"))
        {
            ImGui.SeparatorText("General");

            if (ImGui.Button("Button"))
                s_basicClicked++;
            if ((s_basicClicked & 1) != 0)
            {
                ImGui.SameLine();
                ImGui.Text("Thanks for clicking me!");
            }

            ImGui.Checkbox("checkbox", ref s_basicCheck);

            ImGui.RadioButton("radio a", ref s_basicRadioE, 0); ImGui.SameLine();
            ImGui.RadioButton("radio b", ref s_basicRadioE, 1); ImGui.SameLine();
            ImGui.RadioButton("radio c", ref s_basicRadioE, 2);

            ImGui.AlignTextToFramePadding();
            ImGui.TextLinkOpenURL("Hyperlink", "https://github.com/ocornut/imgui/wiki/Error-Handling");

            // Color buttons, demonstrate using PushID() to add unique identifier in the ID stack, and changing style.
            for (int i = 0; i < 7; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                ImGui.PushID(i);
                ImGui.PushStyleColor(ImGuiCol.Button, ImColor.HSV(i / 7.0f, 0.6f, 0.6f).Value);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImColor.HSV(i / 7.0f, 0.7f, 0.7f).Value);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImColor.HSV(i / 7.0f, 0.8f, 0.8f).Value);
                ImGui.Button("Click");
                ImGui.PopStyleColor(3);
                ImGui.PopID();
            }

            // Use AlignTextToFramePadding() to align text baseline to the baseline of framed widgets elements
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Hold to repeat:");
            ImGui.SameLine();

            // Arrow buttons with Repeater
            float spacing = ImGui.GetStyle().ItemInnerSpacing.X;
            ImGui.PushItemFlag(ImGuiItemFlags.ButtonRepeat, true);
            if (ImGui.ArrowButton("##left", ImGuiDir.Left)) { s_basicCounter--; }
            ImGui.SameLine(0.0f, spacing);
            if (ImGui.ArrowButton("##right", ImGuiDir.Right)) { s_basicCounter++; }
            ImGui.PopItemFlag();
            ImGui.SameLine();
            ImGui.Text($"{s_basicCounter}");

            ImGui.Button("Tooltip");
            ImGui.SetItemTooltip("I am a tooltip");

            ImGui.LabelText("label", "Value");

            ImGui.SeparatorText("Inputs");

            {
                ImGui.InputText("input text", ref s_basicStr0);
                ImGui.SameLine(); HelpMarker(
                    "USER:\n" +
                    "Hold Shift or use mouse to select text.\n" +
                    "Ctrl+Left/Right to word jump.\n" +
                    "Ctrl+A or Double-Click to select all.\n" +
                    "Ctrl+X,Ctrl+C,Ctrl+V for clipboard.\n" +
                    "Ctrl+Z to undo, Ctrl+Y/Ctrl+Shift+Z to redo.\n" +
                    "Escape to revert.\n\n" +
                    "PROGRAMMER:\n" +
                    "Hikari supports ref string directly, as well as byte buffer overloads.");

                ImGui.InputTextWithHint("input text (w/ hint)", "enter text here", ref s_basicStr1);

                ImGui.InputInt("input int", ref s_basicI0);

                ImGui.InputFloat("input float", ref s_basicF0, 0.01f, 1.0f, "%.3f");

                ImGui.InputDouble("input double", ref s_basicD0, 0.01f, 1.0f, "%.8f");

                ImGui.InputFloat("input scientific", ref s_basicF1, 0.0f, 0.0f, "%e");
                ImGui.SameLine(); HelpMarker(
                    "You can input value using the scientific notation,\n" +
                    "  e.g. \"1e+8\" becomes \"100000000\".");

                ImGui.InputFloat3("input float3", s_basicVec4a);
            }

            ImGui.SeparatorText("Drags");

            {
                ImGui.DragInt("drag int", ref s_basicDragI1, 1);
                ImGui.SameLine(); HelpMarker(
                    "Click and drag to edit value.\n" +
                    "Hold Shift/Alt for faster/slower edit.\n" +
                    "Double-Click or Ctrl+Click to input value.");
                ImGui.DragInt("drag int 0..100", ref s_basicDragI2, 1, 0, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp);
                ImGui.DragInt("drag int wrap 100..200", ref s_basicDragI3, 1, 100, 200, "%d", ImGuiSliderFlags.WrapAround);

                ImGui.DragFloat("drag float", ref s_basicDragF1, 0.005f);
                ImGui.DragFloat("drag small float", ref s_basicDragF2, 0.0001f, 0.0f, 0.0f, "%.06f ns");
            }

            ImGui.SeparatorText("Sliders");

            {
                ImGui.SliderInt("slider int", ref s_basicSliderI1, -1, 3);
                ImGui.SameLine(); HelpMarker("Ctrl+Click to input value.");

                ImGui.SliderFloat("slider float", ref s_basicSliderF1, 0.0f, 1.0f, "ratio = %.3f");
                ImGui.SliderFloat("slider float (log)", ref s_basicSliderF2, -10.0f, 10.0f, "%.4f", ImGuiSliderFlags.Logarithmic);

                ImGui.SliderAngle("slider angle", ref s_basicSliderAngle);

                string elemName = (s_basicSliderElem >= 0 && s_basicSliderElem < s_basicElemNames.Length) ? s_basicElemNames[s_basicSliderElem] : "Unknown";
                ImGui.SliderInt("slider enum", ref s_basicSliderElem, 0, s_basicElemNames.Length - 1, elemName);
                ImGui.SameLine(); HelpMarker("Using the format string parameter to display a name instead of the underlying integer.");
            }

            ImGui.SeparatorText("Selectors/Pickers");

            {
                ImGui.ColorEdit3("color 1", s_basicColor1);
                ImGui.SameLine(); HelpMarker(
                    "Click on the color square to open a color picker.\n" +
                    "Click and hold to use drag and drop.\n" +
                    "Right-Click on the color square to show options.\n" +
                    "Ctrl+Click on individual component to input value.\n");

                ImGui.ColorEdit4("color 2", s_basicColor2);
            }

            {
                ImGui.Combo("combo", ref s_basicComboItemCurrent, s_basicComboItems);
                ImGui.SameLine(); HelpMarker(
                    "Using the simplified one-liner Combo API here.\n" +
                    "Refer to the \"Combo\" section below for an explanation of how to use the more flexible and general BeginCombo/EndCombo API.");
            }

            {
                ImGui.ListBox("listbox", ref s_basicListBoxItemCurrent, s_basicListBoxItems, 4);
                ImGui.SameLine(); HelpMarker(
                    "Using the simplified one-liner ListBox API here.\n" +
                    "Refer to the \"List boxes\" section below for an explanation of how to use the more flexible and general BeginListBox/EndListBox API.");
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsBullets()

    /// <summary>
    /// Demonstrates bullet markers and bulleted text across single and nested tree rows.
    /// </summary>
    public static void DemoWindowWidgetsBullets()
    {
        if (ImGui.TreeNode("Bullets"))
        {
            ImGui.BulletText("Bullet point 1");
            ImGui.BulletText("Bullet point 2\nOn multiple lines");
            if (ImGui.TreeNode("Tree node"))
            {
                ImGui.BulletText("Another bullet point");
                ImGui.TreePop();
            }
            ImGui.Bullet(); ImGui.Text("Bullet point 3 (two calls)");
            ImGui.Bullet(); ImGui.SmallButton("Button");
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsCollapsingHeaders()

    /// <summary>
    /// Demonstrates collapsing headers with toggleable and closable sections.
    /// </summary>
    public static void DemoWindowWidgetsCollapsingHeaders()
    {
        if (ImGui.TreeNode("Collapsing Headers"))
        {
            ImGui.Checkbox("Show 2nd header", ref s_collapseClosableGroup);
            if (ImGui.CollapsingHeader("Header", ImGuiTreeNodeFlags.None))
            {
                ImGui.Text($"IsItemHovered: {ImGui.IsItemHovered()}");
                for (int i = 0; i < 5; i++)
                    ImGui.Text($"Some content {i}");
            }
            if (ImGui.CollapsingHeader("Header with a close button", ref s_collapseClosableGroup))
            {
                ImGui.Text($"IsItemHovered: {ImGui.IsItemHovered()}");
                for (int i = 0; i < 5; i++)
                    ImGui.Text($"More content {i}");
            }
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsColorAndPickers()

    /// <summary>
    /// Demonstrates color editors, color pickers, palettes, HSV encodings, and mode overrides.
    /// </summary>
    public static void DemoWindowWidgetsColorAndPickers()
    {
        if (ImGui.TreeNode("Color/Picker Widgets"))
        {
            ImVec4 colorVec4 = new(s_colorPickerColor[0], s_colorPickerColor[1], s_colorPickerColor[2], s_colorPickerColor[3]);

            ImGui.SeparatorText("Options");
            CheckboxFlags("ImGuiColorEditFlags_NoAlpha", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.NoAlpha);
            CheckboxFlags("ImGuiColorEditFlags_AlphaOpaque", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.AlphaOpaque);
            CheckboxFlags("ImGuiColorEditFlags_AlphaNoBg", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.AlphaNoBg);
            CheckboxFlags("ImGuiColorEditFlags_AlphaPreviewHalf", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.AlphaPreviewHalf);
            CheckboxFlags("ImGuiColorEditFlags_NoOptions", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.NoOptions); ImGui.SameLine(); HelpMarker("Right-click on the individual color widget to show options.");
            CheckboxFlags("ImGuiColorEditFlags_NoDragDrop", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.NoDragDrop);
            CheckboxFlags("ImGuiColorEditFlags_NoColorMarkers", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.NoColorMarkers);
            CheckboxFlags("ImGuiColorEditFlags_HDR", ref s_colorPickerBaseFlags, ImGuiColorEditFlags.HDR); ImGui.SameLine(); HelpMarker("Currently all this does is to lift the 0..1 limits on dragging widgets.");

            ImGui.SeparatorText("Inline color editor");
            ImGui.Text("Color widget:");
            ImGui.SameLine(); HelpMarker(
                "Click on the color square to open a color picker.\n" +
                "Ctrl+Click on individual component to input value.\n");
            ImGui.ColorEdit3("MyColor##1", s_colorPickerColor, s_colorPickerBaseFlags);

            ImGui.Text("Color widget HSV with Alpha:");
            ImGui.ColorEdit4("MyColor##2", s_colorPickerColor, ImGuiColorEditFlags.DisplayHSV | s_colorPickerBaseFlags);

            ImGui.Text("Color widget with Float Display:");
            ImGui.ColorEdit4("MyColor##2f", s_colorPickerColor, ImGuiColorEditFlags.Float | s_colorPickerBaseFlags);

            ImGui.Text("Color button with Picker:");
            ImGui.SameLine(); HelpMarker(
                "With the ImGuiColorEditFlags_NoInputs flag you can hide all the slider/text inputs.\n" +
                "With the ImGuiColorEditFlags_NoLabel flag you can pass a non-empty label which will only " +
                "be used for the tooltip and picker popup.");
            ImGui.ColorEdit4("MyColor##3", s_colorPickerColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | s_colorPickerBaseFlags);

            ImGui.Text("Color button with Custom Picker Popup:");

            // Generate a default palette
            if (s_savedPaletteInit)
            {
                for (int n = 0; n < s_savedPalette.Length; n++)
                {
                    ImGui.ColorConvertHSVtoRGB(n / 31.0f, 0.8f, 0.8f, out float r, out float g, out float b);
                    s_savedPalette[n] = new ImVec4(r, g, b, 1.0f);
                }
                s_savedPaletteInit = false;
            }

            bool openPopup = ImGui.ColorButton("MyColor##3b", colorVec4, s_colorPickerBaseFlags);
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            openPopup |= ImGui.Button("Palette");
            if (openPopup)
            {
                ImGui.OpenPopup("mypicker");
                s_backupColor = colorVec4;
            }
            if (ImGui.BeginPopup("mypicker"))
            {
                ImGui.Text("MY CUSTOM COLOR PICKER WITH AN AMAZING PALETTE!");
                ImGui.Separator();
                ImGui.ColorPicker4("##picker", s_colorPickerColor, s_colorPickerBaseFlags | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview);
                ImGui.SameLine();

                ImGui.BeginGroup(); // Lock X position
                ImGui.Text("Current");
                // Canonical reads the live colour after ColorPicker4 has edited it., so the
                // swatch shows this frame's edit rather than the value captured before the picker ran.
                ImVec4 liveColor = new(s_colorPickerColor[0], s_colorPickerColor[1], s_colorPickerColor[2], s_colorPickerColor[3]);
                ImGui.ColorButton("##current", liveColor, ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.AlphaPreviewHalf, new ImVec2(60, 40));
                ImGui.Text("Previous");
                if (ImGui.ColorButton("##previous", s_backupColor, ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.AlphaPreviewHalf, new ImVec2(60, 40)))
                {
                    s_colorPickerColor[0] = s_backupColor.X;
                    s_colorPickerColor[1] = s_backupColor.Y;
                    s_colorPickerColor[2] = s_backupColor.Z;
                    s_colorPickerColor[3] = s_backupColor.W;
                }
                ImGui.Separator();
                ImGui.Text("Palette");
                for (int n = 0; n < s_savedPalette.Length; n++)
                {
                    ImGui.PushID(n);
                    if ((n % 8) != 0)
                        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.Y);

                    ImGuiColorEditFlags paletteButtonFlags = ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoTooltip;
                    if (ImGui.ColorButton("##palette", s_savedPalette[n], paletteButtonFlags, new ImVec2(20, 20)))
                    {
                        s_colorPickerColor[0] = s_savedPalette[n].X;
                        s_colorPickerColor[1] = s_savedPalette[n].Y;
                        s_colorPickerColor[2] = s_savedPalette[n].Z;
                        // Preserve alpha!
                    }

                    // Each palette entry accepts a colour dropped from another colour widget. ColorButton is itself a
                    // drag source unless NoDragDrop is set. The payload carries a managed
                    // float array: three components for IMGUI_PAYLOAD_TYPE_COLOR_3F, four for COLOR_4F.
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiPayload? payload3 = ImGui.AcceptDragDropPayload(ImGui.IMGUI_PAYLOAD_TYPE_COLOR_3F);
                        if (payload3 != null && payload3.ObjectData is float[] rgb3)
                            s_savedPalette[n] = new ImVec4(rgb3[0], rgb3[1], rgb3[2], s_savedPalette[n].W);
                        ImGuiPayload? payload4 = ImGui.AcceptDragDropPayload(ImGui.IMGUI_PAYLOAD_TYPE_COLOR_4F);
                        if (payload4 != null && payload4.ObjectData is float[] rgba4)
                            s_savedPalette[n] = new ImVec4(rgba4[0], rgba4[1], rgba4[2], rgba4[3]);
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.PopID();
                }
                ImGui.EndGroup();
                ImGui.EndPopup();
            }

            ImGui.Text("Color button only:");
            ImGui.Checkbox("ImGuiColorEditFlags_NoBorder", ref s_colorNoBorder);
            ImGui.ColorButton("MyColor##3c", colorVec4, s_colorPickerBaseFlags | (s_colorNoBorder ? ImGuiColorEditFlags.NoBorder : 0), new ImVec2(80, 80));

            ImGui.SeparatorText("Color picker");

            ImGui.PushID("Color picker");
            CheckboxFlags("ImGuiColorEditFlags_NoAlpha", ref s_colorPickerFlags, ImGuiColorEditFlags.NoAlpha);
            CheckboxFlags("ImGuiColorEditFlags_AlphaBar", ref s_colorPickerFlags, ImGuiColorEditFlags.AlphaBar);
            CheckboxFlags("ImGuiColorEditFlags_NoSidePreview", ref s_colorPickerFlags, ImGuiColorEditFlags.NoSidePreview);
            if ((s_colorPickerFlags & ImGuiColorEditFlags.NoSidePreview) != 0)
            {
                ImGui.SameLine();
                ImGui.Checkbox("With Ref Color", ref s_refColor);
                if (s_refColor)
                {
                    ImGui.SameLine();
                    ColorEdit4("##RefColor", ref s_refColorV, ImGuiColorEditFlags.NoInputs | s_colorPickerBaseFlags);
                }
            }
            CheckboxFlags("ImGuiColorEditFlags_PickerNoRotate", ref s_colorPickerFlags, ImGuiColorEditFlags.PickerNoRotate);

            string[] pickerModes = new[] { "Auto/Current", "ImGuiColorEditFlags_PickerHueBar", "ImGuiColorEditFlags_PickerHueWheel" };
            ImGui.Combo("Picker Mode", ref s_pickerMode, pickerModes);
            ImGui.SameLine(); HelpMarker("When not specified explicitly, user can right-click the picker to change mode.");

            string[] displayModes = new[] { "Auto/Current", "ImGuiColorEditFlags_NoInputs", "ImGuiColorEditFlags_DisplayRGB", "ImGuiColorEditFlags_DisplayHSV", "ImGuiColorEditFlags_DisplayHex" };
            ImGui.Combo("Display Mode", ref s_displayMode, displayModes);
            ImGui.SameLine(); HelpMarker(
                "ColorEdit defaults to displaying RGB inputs if you don't specify a display mode, " +
                "but the user can change it with a right-click on those inputs.\n\nColorPicker defaults to displaying RGB+HSV+Hex " +
                "if you don't specify a display mode.\n\nYou can change the defaults using io.ConfigColorEditFlags.");

            ImGuiColorEditFlags flags = s_colorPickerBaseFlags | s_colorPickerFlags;
            if (s_pickerMode == 1) flags |= ImGuiColorEditFlags.PickerHueBar;
            if (s_pickerMode == 2) flags |= ImGuiColorEditFlags.PickerHueWheel;
            if (s_displayMode == 1) flags |= ImGuiColorEditFlags.NoInputs;
            if (s_displayMode == 2) flags |= ImGuiColorEditFlags.DisplayRGB;
            if (s_displayMode == 3) flags |= ImGuiColorEditFlags.DisplayHSV;
            if (s_displayMode == 4) flags |= ImGuiColorEditFlags.DisplayHex;
            ImGui.ColorPicker4("MyColor##4", s_colorPickerColor, flags, s_refColor ? s_refColorV : (ImVec4?)null);

            ImGui.Text("Set defaults in code:");
            ImGui.SameLine(); HelpMarker(
                "io.ConfigColorEditFlags is designed to allow you to set boot-time default.\n" +
                "We don't have Push/Pop functions because you can force options on a per-widget basis if needed, " +
                "and the user can change non-forced ones with the options menu.\nWe don't have a getter to avoid " +
                "encouraging you to persistently save values that aren't forward-compatible.");
            if (ImGui.Button("Overwrite default: Uint8 + HSV + Hue Bar"))
                ImGui.GetIO().ConfigColorEditFlags = ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.DisplayHSV | ImGuiColorEditFlags.PickerHueBar;
            if (ImGui.Button("Overwrite default: Float + HDR + Hue Wheel"))
                ImGui.GetIO().ConfigColorEditFlags = ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.PickerHueWheel;

            // Always display a small version of both types of pickers
            ImGui.Text("Both types:");
            float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.Y) * 0.40f;
            ImGui.SetNextItemWidth(w);
            ImGui.ColorPicker3("##MyColor##5", s_colorPickerColor, ImGuiColorEditFlags.PickerHueBar | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w);
            ImGui.ColorPicker3("##MyColor##6", s_colorPickerColor, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha);
            ImGui.PopID();

            // HSV encoded support
            ImGui.Spacing();
            ImGui.Text("HSV encoded colors");
            ImGui.SameLine(); HelpMarker(
                "By default, colors are given to ColorEdit and ColorPicker in RGB, but ImGuiColorEditFlags_InputHSV " +
                "allows you to store colors as HSV and pass them to ColorEdit and ColorPicker as HSV. This comes with the " +
                "added benefit that you can manipulate hue values with the picker even when saturation or value are zero.");
            ImGui.Text("Color widget with InputHSV:");
            ImGui.ColorEdit4("HSV shown as RGB##1", s_colorHsv, ImGuiColorEditFlags.DisplayRGB | ImGuiColorEditFlags.InputHSV | ImGuiColorEditFlags.Float);
            ImGui.ColorEdit4("HSV shown as HSV##1", s_colorHsv, ImGuiColorEditFlags.DisplayHSV | ImGuiColorEditFlags.InputHSV | ImGuiColorEditFlags.Float);
            ImGui.DragFloat4("Raw HSV values", s_colorHsv, 0.01f, 0.0f, 1.0f);

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsComboBoxes()

    /// <summary>
    /// Demonstrates combo box variants: standard dropdowns, filtering combos, and one-liner helpers.
    /// </summary>
    public static void DemoWindowWidgetsComboBoxes()
    {
        if (ImGui.TreeNode("Combo"))
        {
            CheckboxFlags("ImGuiComboFlags_PopupAlignLeft", ref s_comboFlags, ImGuiComboFlags.PopupAlignLeft);
            ImGui.SameLine(); HelpMarker("Only makes a difference if the popup is larger than the combo");
            if (CheckboxFlags("ImGuiComboFlags_NoArrowButton", ref s_comboFlags, ImGuiComboFlags.NoArrowButton))
                s_comboFlags &= ~ImGuiComboFlags.NoPreview;
            if (CheckboxFlags("ImGuiComboFlags_NoPreview", ref s_comboFlags, ImGuiComboFlags.NoPreview))
                s_comboFlags &= ~(ImGuiComboFlags.NoArrowButton | ImGuiComboFlags.WidthFitPreview);
            if (CheckboxFlags("ImGuiComboFlags_WidthFitPreview", ref s_comboFlags, ImGuiComboFlags.WidthFitPreview))
                s_comboFlags &= ~ImGuiComboFlags.NoPreview;

            if (CheckboxFlags("ImGuiComboFlags_HeightSmall", ref s_comboFlags, ImGuiComboFlags.HeightSmall))
                s_comboFlags &= ~(ImGuiComboFlags.HeightMask_ & ~ImGuiComboFlags.HeightSmall);
            if (CheckboxFlags("ImGuiComboFlags_HeightRegular", ref s_comboFlags, ImGuiComboFlags.HeightRegular))
                s_comboFlags &= ~(ImGuiComboFlags.HeightMask_ & ~ImGuiComboFlags.HeightRegular);
            if (CheckboxFlags("ImGuiComboFlags_HeightLargest", ref s_comboFlags, ImGuiComboFlags.HeightLargest))
                s_comboFlags &= ~(ImGuiComboFlags.HeightMask_ & ~ImGuiComboFlags.HeightLargest);

            string comboPreviewValue = s_comboItems[s_comboSelectedIdx];
            if (ImGui.BeginCombo("combo 1", comboPreviewValue, s_comboFlags))
            {
                for (int n = 0; n < s_comboItems.Length; n++)
                {
                    bool isSelected = (s_comboSelectedIdx == n);
                    if (ImGui.Selectable(s_comboItems[n], isSelected))
                        s_comboSelectedIdx = n;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Combo 2 w/ filter
            if (ImGui.BeginCombo("combo 2 (w/ filter)", comboPreviewValue, s_comboFlags))
            {
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                    s_comboFilter = "";
                }
                ImGui.SetNextItemShortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.F);
                ImGui.InputText("##Filter", ref s_comboFilter);

                for (int n = 0; n < s_comboItems.Length; n++)
                {
                    bool isSelected = (s_comboSelectedIdx == n);
                    if (string.IsNullOrEmpty(s_comboFilter) || s_comboItems[n].Contains(s_comboFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ImGui.Selectable(s_comboItems[n], isSelected))
                            s_comboSelectedIdx = n;
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();
            ImGui.SeparatorText("One-liner variants");
            HelpMarker("The Combo() function is not greatly useful apart from cases were you want to embed all options in a single strings.\nFlags above don't apply to this section.");

            ImGui.Combo("combo 3 (one-liner)", ref s_comboItemCurrent2, "aaaa\0bbbb\0cccc\0dddd\0eeee\0\0");

            ImGui.Combo("combo 4 (array)", ref s_comboItemCurrent3, s_comboItems);

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsDataTypes()

    /// <summary>
    /// Demonstrates the generic scalar drag, slider, and input widgets across every numeric data type
    /// and width, including reverse-range sliders and the input-text flag combinations.
    /// </summary>
    /// <remarks>
    /// The port exposes typed <c>DragScalar</c>, <c>SliderScalar</c>, and <c>InputScalar</c> overloads,
    /// so the canonical generic calls that pass an <c>ImGuiDataType</c> and a <c>void*</c> are expressed
    /// with the matching element type. The 64-bit rows use <c>%d</c> and <c>%u</c> rather than the
    /// canonical <c>PRId64</c> and <c>PRIu64</c> macros because the port's printf formatter reads no C
    /// length modifiers; the emitted text is identical.
    /// </remarks>
    public static void DemoWindowWidgetsDataTypes()
    {
        if (ImGui.TreeNode("Data Types"))
        {
            // Setup limits as local constants so they can be passed as nullable bounds, exactly as the
            // canonical helper variables allow their address to be taken. SliderScalar() has a maximum
            // usable range of half the natural type maximum, hence the division by two.
            const sbyte s8Zero = 0, s8One = 1, s8Fifty = 50, s8Min = -128, s8Max = 127;
            const byte u8Zero = 0, u8One = 1, u8Fifty = 50, u8Min = 0, u8Max = 255;
            const short s16Zero = 0, s16One = 1, s16Fifty = 50, s16Min = -32768, s16Max = 32767;
            const ushort u16Zero = 0, u16One = 1, u16Fifty = 50, u16Min = 0, u16Max = 65535;
            const int s32Zero = 0, s32One = 1, s32Fifty = 50, s32Min = int.MinValue / 2, s32Max = int.MaxValue / 2, s32HiA = int.MaxValue / 2 - 100, s32HiB = int.MaxValue / 2;
            const uint u32Zero = 0, u32One = 1, u32Fifty = 50, u32Min = 0, u32Max = uint.MaxValue / 2, u32HiA = uint.MaxValue / 2 - 100, u32HiB = uint.MaxValue / 2;
            const long s64Zero = 0, s64One = 1, s64Fifty = 50, s64Min = long.MinValue / 2, s64Max = long.MaxValue / 2, s64HiA = long.MaxValue / 2 - 100, s64HiB = long.MaxValue / 2;
            const ulong u64Zero = 0, u64One = 1, u64Fifty = 50, u64Min = 0, u64Max = ulong.MaxValue / 2, u64HiA = ulong.MaxValue / 2 - 100, u64HiB = ulong.MaxValue / 2;
            const float f32Zero = 0.0f, f32One = 1.0f, f32LoA = -10000000000.0f, f32HiA = +10000000000.0f;
            const double f64Zero = 0.0, f64One = 1.0, f64LoA = -1000000000000000.0, f64HiA = +1000000000000000.0;
            const float dragSpeed = 0.2f;

            ImGui.SeparatorText("Drags");
            ImGui.Checkbox("Clamp integers to 0..50", ref s_dataTypesDragClamp);
            ImGui.SameLine(); HelpMarker(
                "As with every widget in dear imgui, we never modify values unless there is a user interaction.\n" +
                "You can override the clamping limits by using Ctrl+Click to input a value.");
            ImGui.DragScalar("drag s8", ImGuiDataType.S8, ref s_dataTypesS8V, dragSpeed, s_dataTypesDragClamp ? s8Zero : null, s_dataTypesDragClamp ? s8Fifty : null);
            ImGui.DragScalar("drag u8", ImGuiDataType.U8, ref s_dataTypesU8V, dragSpeed, s_dataTypesDragClamp ? u8Zero : null, s_dataTypesDragClamp ? u8Fifty : null, "%u ms");
            ImGui.DragScalar("drag s16", ImGuiDataType.S16, ref s_dataTypesS16V, dragSpeed, s_dataTypesDragClamp ? s16Zero : null, s_dataTypesDragClamp ? s16Fifty : null);
            ImGui.DragScalar("drag u16", ImGuiDataType.U16, ref s_dataTypesU16V, dragSpeed, s_dataTypesDragClamp ? u16Zero : null, s_dataTypesDragClamp ? u16Fifty : null, "%u ms");
            ImGui.DragScalar("drag s32", ImGuiDataType.S32, ref s_dataTypesS32V, dragSpeed, s_dataTypesDragClamp ? s32Zero : null, s_dataTypesDragClamp ? s32Fifty : null);
            ImGui.DragScalar("drag s32 hex", ImGuiDataType.S32, ref s_dataTypesS32V, dragSpeed, s_dataTypesDragClamp ? s32Zero : null, s_dataTypesDragClamp ? s32Fifty : null, "0x%08X");
            ImGui.DragScalar("drag u32", ImGuiDataType.U32, ref s_dataTypesU32V, dragSpeed, s_dataTypesDragClamp ? u32Zero : null, s_dataTypesDragClamp ? u32Fifty : null, "%u ms");
            ImGui.DragScalar("drag s64", ImGuiDataType.S64, ref s_dataTypesS64V, dragSpeed, s_dataTypesDragClamp ? s64Zero : null, s_dataTypesDragClamp ? s64Fifty : null);
            ImGui.DragScalar("drag u64", ImGuiDataType.U64, ref s_dataTypesU64V, dragSpeed, s_dataTypesDragClamp ? u64Zero : null, s_dataTypesDragClamp ? u64Fifty : null);
            ImGui.DragScalar("drag float", ImGuiDataType.Float, ref s_dataTypesF32V, 0.005f, f32Zero, f32One, "%f");
            ImGui.DragScalar("drag float log", ImGuiDataType.Float, ref s_dataTypesF32V, 0.005f, f32Zero, f32One, "%f", ImGuiSliderFlags.Logarithmic);
            ImGui.DragScalar("drag double", ImGuiDataType.Double, ref s_dataTypesF64V, 0.0005f, f64Zero, null, "%.10f grams");
            ImGui.DragScalar("drag double log", ImGuiDataType.Double, ref s_dataTypesF64V, 0.0005f, f64Zero, f64One, "0 < %.10f < 1", ImGuiSliderFlags.Logarithmic);

            ImGui.SeparatorText("Sliders");
            ImGui.SliderScalar("slider s8 full", ImGuiDataType.S8, ref s_dataTypesS8V, s8Min, s8Max, "%d");
            ImGui.SliderScalar("slider u8 full", ImGuiDataType.U8, ref s_dataTypesU8V, u8Min, u8Max, "%u");
            ImGui.SliderScalar("slider s16 full", ImGuiDataType.S16, ref s_dataTypesS16V, s16Min, s16Max, "%d");
            ImGui.SliderScalar("slider u16 full", ImGuiDataType.U16, ref s_dataTypesU16V, u16Min, u16Max, "%u");
            ImGui.SliderScalar("slider s32 low", ImGuiDataType.S32, ref s_dataTypesS32V, s32Zero, s32Fifty, "%d");
            ImGui.SliderScalar("slider s32 high", ImGuiDataType.S32, ref s_dataTypesS32V, s32HiA, s32HiB, "%d");
            ImGui.SliderScalar("slider s32 full", ImGuiDataType.S32, ref s_dataTypesS32V, s32Min, s32Max, "%d");
            ImGui.SliderScalar("slider s32 hex", ImGuiDataType.S32, ref s_dataTypesS32V, s32Zero, s32Fifty, "0x%04X");
            ImGui.SliderScalar("slider u32 low", ImGuiDataType.U32, ref s_dataTypesU32V, u32Zero, u32Fifty, "%u");
            ImGui.SliderScalar("slider u32 high", ImGuiDataType.U32, ref s_dataTypesU32V, u32HiA, u32HiB, "%u");
            ImGui.SliderScalar("slider u32 full", ImGuiDataType.U32, ref s_dataTypesU32V, u32Min, u32Max, "%u");
            ImGui.SliderScalar("slider s64 low", ImGuiDataType.S64, ref s_dataTypesS64V, s64Zero, s64Fifty, "%d");
            ImGui.SliderScalar("slider s64 high", ImGuiDataType.S64, ref s_dataTypesS64V, s64HiA, s64HiB, "%d");
            ImGui.SliderScalar("slider s64 full", ImGuiDataType.S64, ref s_dataTypesS64V, s64Min, s64Max, "%d");
            ImGui.SliderScalar("slider u64 low", ImGuiDataType.U64, ref s_dataTypesU64V, u64Zero, u64Fifty, "%u ms");
            ImGui.SliderScalar("slider u64 high", ImGuiDataType.U64, ref s_dataTypesU64V, u64HiA, u64HiB, "%u ms");
            ImGui.SliderScalar("slider u64 full", ImGuiDataType.U64, ref s_dataTypesU64V, u64Min, u64Max, "%u ms");
            ImGui.SliderScalar("slider float low", ImGuiDataType.Float, ref s_dataTypesF32V, f32Zero, f32One);
            ImGui.SliderScalar("slider float low log", ImGuiDataType.Float, ref s_dataTypesF32V, f32Zero, f32One, "%.10f", ImGuiSliderFlags.Logarithmic);
            ImGui.SliderScalar("slider float high", ImGuiDataType.Float, ref s_dataTypesF32V, f32LoA, f32HiA, "%e");
            ImGui.SliderScalar("slider double low", ImGuiDataType.Double, ref s_dataTypesF64V, f64Zero, f64One, "%.10f grams");
            ImGui.SliderScalar("slider double low log", ImGuiDataType.Double, ref s_dataTypesF64V, f64Zero, f64One, "%.10f", ImGuiSliderFlags.Logarithmic);
            ImGui.SliderScalar("slider double high", ImGuiDataType.Double, ref s_dataTypesF64V, f64LoA, f64HiA, "%e grams");

            ImGui.SeparatorText("Sliders (reverse)");
            ImGui.SliderScalar("slider s8 reverse", ImGuiDataType.S8, ref s_dataTypesS8V, s8Max, s8Min, "%d");
            ImGui.SliderScalar("slider u8 reverse", ImGuiDataType.U8, ref s_dataTypesU8V, u8Max, u8Min, "%u");
            ImGui.SliderScalar("slider s32 reverse", ImGuiDataType.S32, ref s_dataTypesS32V, s32Fifty, s32Zero, "%d");
            ImGui.SliderScalar("slider u32 reverse", ImGuiDataType.U32, ref s_dataTypesU32V, u32Fifty, u32Zero, "%u");
            ImGui.SliderScalar("slider s64 reverse", ImGuiDataType.S64, ref s_dataTypesS64V, s64Fifty, s64Zero, "%d");
            ImGui.SliderScalar("slider u64 reverse", ImGuiDataType.U64, ref s_dataTypesU64V, u64Fifty, u64Zero, "%u ms");

            ImGui.SeparatorText("Inputs");
            ImGui.Checkbox("Show step buttons", ref s_dataTypesInputsStep);
            CheckboxFlags("ImGuiInputTextFlags_ReadOnly", ref s_dataTypesInputFlags, ImGuiInputTextFlags.ReadOnly);
            CheckboxFlags("ImGuiInputTextFlags_ParseEmptyRefVal", ref s_dataTypesInputFlags, ImGuiInputTextFlags.ParseEmptyRefVal);
            CheckboxFlags("ImGuiInputTextFlags_DisplayEmptyRefVal", ref s_dataTypesInputFlags, ImGuiInputTextFlags.DisplayEmptyRefVal);
            ImGui.InputScalar("input s8", ImGuiDataType.S8, ref s_dataTypesS8V, s_dataTypesInputsStep ? s8One : null, null, "%d", s_dataTypesInputFlags);
            ImGui.InputScalar("input u8", ImGuiDataType.U8, ref s_dataTypesU8V, s_dataTypesInputsStep ? u8One : null, null, "%u", s_dataTypesInputFlags);
            ImGui.InputScalar("input s16", ImGuiDataType.S16, ref s_dataTypesS16V, s_dataTypesInputsStep ? s16One : null, null, "%d", s_dataTypesInputFlags);
            ImGui.InputScalar("input u16", ImGuiDataType.U16, ref s_dataTypesU16V, s_dataTypesInputsStep ? u16One : null, null, "%u", s_dataTypesInputFlags);
            ImGui.InputScalar("input s32", ImGuiDataType.S32, ref s_dataTypesS32V, s_dataTypesInputsStep ? s32One : null, null, "%d", s_dataTypesInputFlags);
            ImGui.InputScalar("input s32 hex", ImGuiDataType.S32, ref s_dataTypesS32V, s_dataTypesInputsStep ? s32One : null, null, "%04X", s_dataTypesInputFlags);
            ImGui.InputScalar("input u32", ImGuiDataType.U32, ref s_dataTypesU32V, s_dataTypesInputsStep ? u32One : null, null, "%u", s_dataTypesInputFlags);
            ImGui.InputScalar("input u32 hex", ImGuiDataType.U32, ref s_dataTypesU32V, s_dataTypesInputsStep ? u32One : null, null, "%08X", s_dataTypesInputFlags);
            ImGui.InputScalar("input s64", ImGuiDataType.S64, ref s_dataTypesS64V, s_dataTypesInputsStep ? s64One : null, null, null, s_dataTypesInputFlags);
            ImGui.InputScalar("input u64", ImGuiDataType.U64, ref s_dataTypesU64V, s_dataTypesInputsStep ? u64One : null, null, null, s_dataTypesInputFlags);
            ImGui.InputScalar("input float", ImGuiDataType.Float, ref s_dataTypesF32V, s_dataTypesInputsStep ? f32One : null, null, null, s_dataTypesInputFlags);
            ImGui.InputScalar("input double", ImGuiDataType.Double, ref s_dataTypesF64V, s_dataTypesInputsStep ? f64One : null, null, null, s_dataTypesInputFlags);

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsDragAndDrop()

    /// <summary>
    /// Demonstrates the drag-and-drop API: color widgets that act as sources and targets, a
    /// copy/move/swap board driven by a typed payload, a reorder list that works without the
    /// drag-and-drop API, and a target that vetoes the drop with a tooltip.
    /// </summary>
    /// <remarks>
    /// The copy/swap board submits <c>DND_DEMO_CELL</c> payloads through the port's managed
    /// <c>SetDragDropPayload(string, object)</c> overload, so <see cref="ImGuiPayload.ObjectData"/>
    /// carries the source index instead of a raw pointer.
    /// </remarks>
    public static void DemoWindowWidgetsDragAndDrop()
    {
        if (ImGui.TreeNode("Drag and Drop"))
        {
            if (ImGui.TreeNode("Drag and drop in standard widgets"))
            {
                // ColorEdit widgets are standardized drag sources and targets for the
                // IMGUI_PAYLOAD_TYPE_COLOR_3F/4F payloads, so user widgets can accept colors too.
                HelpMarker("You can drag from the color squares.");
                ImGui.ColorEdit3("color 1", s_dragAndDropCol1);
                ImGui.ColorEdit4("color 2", s_dragAndDropCol2);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Drag and drop to copy/swap items"))
            {
                if (ImGui.RadioButton("Copy", s_dragAndDropMode == DragAndDropModeCopy)) { s_dragAndDropMode = DragAndDropModeCopy; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Move", s_dragAndDropMode == DragAndDropModeMove)) { s_dragAndDropMode = DragAndDropModeMove; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Swap", s_dragAndDropMode == DragAndDropModeSwap)) { s_dragAndDropMode = DragAndDropModeSwap; }

                for (int n = 0; n < s_dragAndDropNames.Length; n++)
                {
                    ImGui.PushID(n);
                    if ((n % 3) != 0)
                        ImGui.SameLine();
                    ImGui.Button(s_dragAndDropNames[n], new ImVec2(60.0f, 60.0f));

                    // Each button is both a drag source and a drag target.
                    if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                    {
                        ImGui.SetDragDropPayload("DND_DEMO_CELL", n);

                        // The preview may show anything; here it names the item and the pending action.
                        if (s_dragAndDropMode == DragAndDropModeCopy) { ImGui.Text($"Copy {s_dragAndDropNames[n]}"); }
                        if (s_dragAndDropMode == DragAndDropModeMove) { ImGui.Text($"Move {s_dragAndDropNames[n]}"); }
                        if (s_dragAndDropMode == DragAndDropModeSwap) { ImGui.Text($"Swap {s_dragAndDropNames[n]}"); }
                        ImGui.EndDragDropSource();
                    }
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiPayload? payload = ImGui.AcceptDragDropPayload("DND_DEMO_CELL");
                        if (payload != null)
                        {
                            Debug.Assert(payload.ObjectData is int, "The DND_DEMO_CELL payload carries an item index.");
                            int payloadN = (int)payload.ObjectData!;
                            if (s_dragAndDropMode == DragAndDropModeCopy)
                            {
                                s_dragAndDropNames[n] = s_dragAndDropNames[payloadN];
                            }
                            if (s_dragAndDropMode == DragAndDropModeMove)
                            {
                                s_dragAndDropNames[n] = s_dragAndDropNames[payloadN];
                                s_dragAndDropNames[payloadN] = "";
                            }
                            if (s_dragAndDropMode == DragAndDropModeSwap)
                            {
                                (s_dragAndDropNames[n], s_dragAndDropNames[payloadN]) =
                                    (s_dragAndDropNames[payloadN], s_dragAndDropNames[n]);
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Drag to reorder items (simple)"))
            {
                // Reordering submits the same item twice in one frame while swapping, which trips the
                // duplicate-identifier check, so it is explicitly allowed for this section.
                ImGui.PushItemFlag(ImGuiItemFlags.AllowDuplicateId, true);

                // This section deliberately does not use the drag-and-drop API: it queries when an item
                // is held but no longer hovered and reorders accordingly.
                HelpMarker(
                    "We don't use the drag and drop api at all here! " +
                    "Instead we query when the item is held but not hovered, and order items accordingly.");
                for (int n = 0; n < s_dragAndDropItemNames.Length; n++)
                {
                    ImGui.Selectable(s_dragAndDropItemNames[n]);

                    if (ImGui.IsItemActive() && !ImGui.IsItemHovered())
                    {
                        int nNext = n + (ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y < 0.0f ? -1 : 1);
                        if (nNext >= 0 && nNext < s_dragAndDropItemNames.Length)
                        {
                            (s_dragAndDropItemNames[n], s_dragAndDropItemNames[nNext]) =
                                (s_dragAndDropItemNames[nNext], s_dragAndDropItemNames[n]);
                            ImGui.ResetMouseDragDelta();
                        }
                    }
                }

                ImGui.PopItemFlag();
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Tooltip at target location"))
            {
                for (int n = 0; n < 2; n++)
                {
                    // Drop targets.
                    ImGui.Button(n != 0 ? "drop here##1" : "drop here##0");
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiDragDropFlags dropTargetFlags =
                            ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoPreviewTooltip;
                        if (ImGui.AcceptDragDropPayload(ImGui.IMGUI_PAYLOAD_TYPE_COLOR_4F, dropTargetFlags) != null)
                        {
                            ImGui.SetMouseCursor(ImGuiMouseCursor.NotAllowed);
                            ImGui.SetTooltip("Cannot drop here!");
                        }
                        ImGui.EndDragDropTarget();
                    }

                    // Drop source.
                    if (n == 0)
                        ImGui.ColorButton("drag me", s_dragAndDropCol4);
                }
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsDragsAndSliders()

    /// <summary>
    /// Demonstrates advanced flags and formatting shared between DragXXX and SliderXXX controls.
    /// </summary>
    public static void DemoWindowWidgetsDragsAndSliders()
    {
        if (ImGui.TreeNode("Drag/Slider Flags"))
        {
            CheckboxFlags("ImGuiSliderFlags_AlwaysClamp", ref s_dragSliderFlags, ImGuiSliderFlags.AlwaysClamp);
            CheckboxFlags("ImGuiSliderFlags_ClampOnInput", ref s_dragSliderFlags, ImGuiSliderFlags.ClampOnInput);
            ImGui.SameLine(); HelpMarker("Clamp value to min/max bounds when input manually with Ctrl+Click. By default Ctrl+Click allows going out of bounds.");
            CheckboxFlags("ImGuiSliderFlags_ClampZeroRange", ref s_dragSliderFlags, ImGuiSliderFlags.ClampZeroRange);
            ImGui.SameLine(); HelpMarker("Clamp even if min==max==0.0f. Otherwise DragXXX functions don't clamp.");
            CheckboxFlags("ImGuiSliderFlags_Logarithmic", ref s_dragSliderFlags, ImGuiSliderFlags.Logarithmic);
            ImGui.SameLine(); HelpMarker("Enable logarithmic editing (more precision for small values).");
            CheckboxFlags("ImGuiSliderFlags_NoRoundToFormat", ref s_dragSliderFlags, ImGuiSliderFlags.NoRoundToFormat);
            ImGui.SameLine(); HelpMarker("Disable rounding underlying value to match precision of the format string (e.g. %.3f values are rounded to those 3 digits).");
            CheckboxFlags("ImGuiSliderFlags_NoInput", ref s_dragSliderFlags, ImGuiSliderFlags.NoInput);
            ImGui.SameLine(); HelpMarker("Disable Ctrl+Click or Enter key allowing to input text directly into the widget.");
            CheckboxFlags("ImGuiSliderFlags_NoSpeedTweaks", ref s_dragSliderFlags, ImGuiSliderFlags.NoSpeedTweaks);
            ImGui.SameLine(); HelpMarker("Disable keyboard modifiers altering tweak speed. Useful if you want to alter tweak speed yourself based on your own logic.");
            CheckboxFlags("ImGuiSliderFlags_WrapAround", ref s_dragSliderFlags, ImGuiSliderFlags.WrapAround);
            ImGui.SameLine(); HelpMarker("Enable wrapping around from max to min and from min to max (only supported by DragXXX() functions)");
            CheckboxFlags("ImGuiSliderFlags_ColorMarkers", ref s_dragSliderFlags, ImGuiSliderFlags.ColorMarkers);

            // Drags
            ImGui.Text($"Underlying float value: {s_dragF}");
            ImGui.DragFloat("DragFloat (0 -> 1)", ref s_dragF, 0.005f, 0.0f, 1.0f, "%.3f", s_dragSliderFlags);
            ImGui.DragFloat("DragFloat (0 -> +inf)", ref s_dragF, 0.005f, 0.0f, float.MaxValue, "%.3f", s_dragSliderFlags);
            ImGui.DragFloat("DragFloat (-inf -> 1)", ref s_dragF, 0.005f, -float.MaxValue, 1.0f, "%.3f", s_dragSliderFlags);
            ImGui.DragFloat("DragFloat (-inf -> +inf)", ref s_dragF, 0.005f, -float.MaxValue, float.MaxValue, "%.3f", s_dragSliderFlags);
            ImGui.DragInt("DragInt (0 -> 100)", ref s_dragI, 0.5f, 0, 100, "%d", s_dragSliderFlags);
            ImGui.DragFloat4("DragFloat4 (0 -> 1)", s_dragF4, 0.005f, 0.0f, 1.0f, "%.3f", s_dragSliderFlags);

            // Sliders
            ImGuiSliderFlags flagsForSliders = s_dragSliderFlags & ~ImGuiSliderFlags.WrapAround;
            ImGui.Text($"Underlying float value: {s_sliderF}");
            ImGui.SliderFloat("SliderFloat (0 -> 1)", ref s_sliderF, 0.0f, 1.0f, "%.3f", flagsForSliders);
            ImGui.SliderInt("SliderInt (0 -> 100)", ref s_sliderI, 0, 100, "%d", flagsForSliders);
            ImGui.SliderFloat4("SliderFloat4 (0 -> 1)", s_sliderF4, 0.0f, 1.0f, "%.3f", s_dragSliderFlags);

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsFonts()

    /// <summary>
    /// Exposes the font atlas showcase under the widget gallery's "Fonts" tree node. Canonical
    /// reference: , dispatched between the
    /// drag/slider and image sections.
    /// </summary>
    /// <remarks>
    /// The body delegates to <see cref="ImGui.ShowFontAtlas"/>, which is the library implementation of
    /// the canonical debug helper, so the demo and the metrics window render the same atlas report.
    /// </remarks>
    public static void DemoWindowWidgetsFonts()
    {
        if (ImGui.TreeNode("Fonts"))
        {
            ImGui.ShowFontAtlas(ImGui.GetIO().Fonts);
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsImages()

    /// <summary>
    /// Demonstrates the image drawing widgets using the font atlas as the only texture the demo owns:
    /// a plain image, an interactive pan/zoom viewer, and a row of textured buttons.
    /// </summary>
    /// <remarks>
    /// Canonical reads the texture identifier, width, and height from <c>io.Fonts-&gt;TexRef</c> and
    /// <c>io.Fonts-&gt;TexData</c>; the port reads the same members from <see cref="ImFontAtlas.TexData"/>
    /// because the demo displays the whole atlas texture. The interactive viewer uses this file's own
    /// <c>s_imagesViewer</c> instance so its pan and zoom state is independent of the standalone image
    /// viewer example.
    /// </remarks>
    public static void DemoWindowWidgetsImages()
    {
        if (ImGui.TreeNode("Images"))
        {
            ImGui.TextWrapped(
                "Below we are displaying the font texture (which is the only texture we have access to in this demo). " +
                "Use the 'ImTextureID' type as storage to pass pointers or identifier to your own texture data. " +
                "Hover the texture for a zoomed view!");

            // The font atlas is the only texture available to the demo. Real applications pass a texture
            // identifier they own to Image()/ImageButton(); the renderer backend decides what the
            // identifier stores. Width and height come from the atlas texture data because the whole
            // atlas is displayed here.
            ImFontAtlas atlas = ImGui.GetIO().Fonts;
            ImTextureData texData = atlas.TexData!;
            ImTextureRef myTexId = texData.GetTexRef();
            float myTexW = texData.Width;
            float myTexH = texData.Height;
            ImGui.Text("%.0fx%.0f", myTexW, myTexH);

            // Basic drawing.
            ImGui.SeparatorText("Image()/ImageWithBg() function");
            ImVec2 uvMin = new(0.0f, 0.0f); // Top-left.
            ImVec2 uvMax = new(1.0f, 1.0f); // Lower-right.
            ImGui.PushStyleVar(ImGuiStyleVar.ImageBorderSize, MathF.Max(1.0f, ImGui.GetStyle().ImageBorderSize));
            ImGui.ImageWithBg(myTexId, new ImVec2(myTexW, myTexH), uvMin, uvMax, new ImVec4(0.0f, 0.0f, 0.0f, 1.0f));
            ImGui.PopStyleVar();

            // Fancy widget: an interactive pan/zoom viewer over the same texture.
            ImGui.SeparatorText("Interactive Image Viewer");
            ImVec2 canvasSize = new(ImGui.GetContentRegionAvail().X, myTexH * 2.0f);
            ExampleImageViewerDrawOptions(s_imagesViewer);
            ExampleImageViewerDrawCanvas(s_imagesViewer, canvasSize, myTexId, (int)myTexW, (int)myTexH);

            ImGui.SeparatorText("Textured Buttons");
            ImGui.TextWrapped("And now some textured buttons..");
            for (int i = 0; i < 8; i++)
            {
                // UV coordinates are usually (0,0) and (1,1) to display a whole texture. This loop shows
                // only a 32x32 pixel area, so the lower-right coordinate scales by the atlas size.
                ImGui.PushID(i);
                if (i > 0)
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(i - 1.0f, i - 1.0f));
                ImVec2 size = new(32.0f, 32.0f);                              // Size of the image we want to make visible.
                ImVec2 uv0 = new(0.0f, 0.0f);                                 // UV coordinates for the lower-left corner.
                ImVec2 uv1 = new(32.0f / myTexW, 32.0f / myTexH);             // UV coordinates for (32,32) in the texture.
                ImVec4 bgCol = new(0.0f, 0.0f, 0.0f, 1.0f);                   // Black background.
                ImVec4 tintCol = new(1.0f, 1.0f, 1.0f, 1.0f);                 // No tint.
                if (ImGui.ImageButton("", myTexId, size, uv0, uv1, bgCol, tintCol))
                    s_imagesPressedCount += 1;
                if (i > 0)
                    ImGui.PopStyleVar();
                ImGui.PopID();
                ImGui.SameLine();
            }
            ImGui.NewLine();
            ImGui.Text("Pressed %d times.", s_imagesPressedCount);

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsListBoxes()

    /// <summary>
    /// Demonstrates the generic list box API with per-item selection and hover highlighting shared
    /// between a default-size list box and a full-width five-item list box.
    /// </summary>
    /// <remarks>
    /// The full-width list box passes a negative horizontal size, which the layout code resolves by
    /// adding the content region width; the port uses the same <c>-float.Epsilon</c> sentinel as the
    /// other full-width call sites in this demo. The highlighted index is recomputed every frame and
    /// therefore stays local to the method.
    /// </remarks>
    public static void DemoWindowWidgetsListBoxes()
    {
        if (ImGui.TreeNode("List Boxes"))
        {
            // Using the generic BeginListBox() API gives full control over how the combo contents are
            // displayed. The selection data could be an index, a pointer to the object, an identifier
            // for the object, or a flag stored intrusively in the object itself.
            string[] items = { "AAAA", "BBBB", "CCCC", "DDDD", "EEEE", "FFFF", "GGGG", "HHHH", "IIII", "JJJJ", "KKKK", "LLLLLLL", "MMMM", "OOOOOOO" };
            int itemHighlightedIdx = -1; // Here we store our highlighted data as an index.

            ImGui.Checkbox("Highlight hovered item in second listbox", ref s_listBoxesItemHighlight);

            if (ImGui.BeginListBox("listbox 1"))
            {
                for (int n = 0; n < items.Length; n++)
                {
                    bool isSelected = (s_listBoxesSelectedIdx == n);
                    if (ImGui.Selectable(items[n], isSelected))
                        s_listBoxesSelectedIdx = n;

                    if (s_listBoxesItemHighlight && ImGui.IsItemHovered())
                        itemHighlightedIdx = n;

                    // Set the initial focus when opening the combo (scrolling + keyboard navigation focus).
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndListBox();
            }
            ImGui.SameLine(); HelpMarker("Here we are sharing selection state between both boxes.");

            // Custom size: use all width, five items tall.
            ImGui.Text("Full-width:");
            if (ImGui.BeginListBox("##listbox 2", new ImVec2(-float.Epsilon, 5 * ImGui.GetTextLineHeightWithSpacing())))
            {
                for (int n = 0; n < items.Length; n++)
                {
                    bool isSelected = (s_listBoxesSelectedIdx == n);
                    ImGuiSelectableFlags flags = (itemHighlightedIdx == n) ? ImGuiSelectableFlags.Highlight : 0;
                    if (ImGui.Selectable(items[n], isSelected, flags))
                        s_listBoxesSelectedIdx = n;

                    // Set the initial focus when opening the combo (scrolling + keyboard navigation focus).
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndListBox();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsLiveEdit()

    /// <summary>
    /// Demonstrates the live-edit item flags, which apply keyboard edits to the backing variable while
    /// the user is still typing, for text and scalar inputs.
    /// </summary>
    /// <param name="demoData">The shared demo data holding the live-edit override flag and the flag word
    /// copied from the current item. A <see langword="null"/> value suppresses the flag controls while
    /// keeping the backing-value rows, because the override state has no storage to bind to.</param>
    /// <remarks>
    /// The port has no max-length argument on the <c>ref string</c> overload of <c>InputText</c>, so the
    /// canonical fixed character buffer is represented by the managed <c>s_liveEditStr</c> field.
    /// </remarks>
    public static void DemoWindowWidgetsLiveEdit(ImGuiDemoWindowData? demoData)
    {
        if (ImGui.TreeNode("Live Edit Flags"))
        {
            ImGui.TextWrapped("Select whether to apply keyboard edits to backing variables _while_ typing.");

            if (demoData != null)
            {
                ImGui.Checkbox("Override Live Edit Flags in Demo Window", ref demoData.LiveEditOverride);
                if (!demoData.LiveEditOverride)
                    demoData.LiveEditFlags = ImGui.GetItemFlags();

                ImGui.BeginDisabled(demoData.LiveEditOverride == false);
                ImGui.Indent();
                CheckboxFlags("ImGuiItemFlags_LiveEditOnInputText", ref demoData.LiveEditFlags, ImGuiItemFlags.LiveEditOnInputText);
                CheckboxFlags("ImGuiItemFlags_LiveEditOnInputScalar", ref demoData.LiveEditFlags, ImGuiItemFlags.LiveEditOnInputScalar);
                ImGui.Unindent();
                ImGui.EndDisabled();
            }

            ImGui.Text("Try typing '123' and seeing effect on backing value:");
            ImGui.InputText("str", ref s_liveEditStr);
            ImGui.Text("Backing value: \"%s\"", s_liveEditStr);
            ImGui.InputInt("int", ref s_liveEditInt, 0, 0);
            ImGui.Text("Backing value: %d", s_liveEditInt);
            ImGui.SliderFloat("float", ref s_liveEditFloat, 0.0f, 100.0f);
            ImGui.Text("Backing value: %f", s_liveEditFloat);

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsMixedValues()

    /// <summary>
    /// Demonstrates property editor patterns handling multi-selection mixed values.
    /// </summary>
    public static void DemoWindowWidgetsMixedValues()
    {
        if (ImGui.TreeNode("Mixed Values"))
        {
            HelpMarker("Using ImGuiItemFlags.MixedValue.");

            ImGui.Checkbox("ImGuiItemFlags_LiveEditOnInput", ref s_mixedLiveEdit);

            ImGui.SeparatorText("Scalar/Text Widgets");
            bool isMixed = s_mixedItems[0] != s_mixedItems[1] || s_mixedItems[0] != s_mixedItems[2];

            // Demonstrate Drags, Sliders, Inputs
            ImGui.PushItemFlag(ImGuiItemFlags.LiveEditOnInput, s_mixedLiveEdit);
            ImGui.PushItemFlag(ImGuiItemFlags.MixedValue, isMixed);
            bool edited = false;
            float itemRef = s_mixedItems[0];
            edited |= ImGui.DragFloat("DragFloat", ref itemRef);
            edited |= ImGui.SliderFloat("SliderFloat", ref itemRef, 0.0f, 100.0f);
            edited |= ImGui.InputFloat("InputFloat", ref itemRef, 1.0f);
            if (edited)
            {
                s_mixedItems[0] = itemRef;
                s_mixedItems[1] = itemRef;
                s_mixedItems[2] = itemRef;
            }
            ImGui.PopItemFlag();

            ImGui.Text("Underlying data:");
            ImGui.InputFloat("item 0 (ref)", ref s_mixedItems[0]);
            ImGui.InputFloat("item 1", ref s_mixedItems[1]);
            ImGui.InputFloat("item 2", ref s_mixedItems[2]);
            ImGui.PopItemFlag();

            // Demonstrate Checkbox(), RadioButton(), Combo(), ColorEdit4()
            ImGui.SeparatorText("Others Widgets");
            ImGui.Text("(note: edits are not applied in this demo)"); // <-- Would need more state tracking.
            ImGui.Checkbox("Checkbox On", ref s_mixedCheckboxOn);
            ImGui.Checkbox("Checkbox Off", ref s_mixedCheckboxOff);
            ImGui.PushItemFlag(ImGuiItemFlags.MixedValue, true);
            bool mixedDummy = false;
            ImGui.Checkbox("Checkbox Mixed", ref mixedDummy);
            ImGui.RadioButton("RadioButton Mixed", true);
            ImGui.SameLine();
            ImGui.RadioButton("RadioButton Mixed##2", true); // Showing 2 radio buttons makes the example more clear
            ImGui.Combo("Combo", ref s_mixedComboIdx, s_mixedComboItems);
            ImGui.ColorEdit4("ColorEdit4", s_mixedColor);
            ImGui.PopItemFlag();

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsMultiComponents()

    /// <summary>
    /// Demonstrates vector/multi-component numerical inputs, drags, sliders, and range selectors (2-wide, 3-wide, 4-wide).
    /// </summary>
    public static void DemoWindowWidgetsMultiComponents()
    {
        if (ImGui.TreeNode("Multi-component Widgets"))
        {
            CheckboxFlags("ImGuiSliderFlags_ColorMarkers", ref s_multiFlags, ImGuiSliderFlags.ColorMarkers);

            ImGui.SeparatorText("2-wide");
            ImGui.InputFloat2("input float2", s_multiVec4f);
            ImGui.InputInt2("input int2", s_multiVec4i);
            ImGui.DragFloat2("drag float2", s_multiVec4f, 0.01f, 0.0f, 1.0f, null, s_multiFlags);
            ImGui.DragInt2("drag int2", s_multiVec4i, 1.0f, 0, 255, null, s_multiFlags);
            ImGui.SliderFloat2("slider float2", s_multiVec4f, 0.0f, 1.0f, null, s_multiFlags);
            ImGui.SliderInt2("slider int2", s_multiVec4i, 0, 255, null, s_multiFlags);

            ImGui.SeparatorText("3-wide");
            ImGui.InputFloat3("input float3", s_multiVec4f);
            ImGui.InputInt3("input int3", s_multiVec4i);
            ImGui.DragFloat3("drag float3", s_multiVec4f, 0.01f, 0.0f, 1.0f, null, s_multiFlags);
            ImGui.DragInt3("drag int3", s_multiVec4i, 1.0f, 0, 255, null, s_multiFlags);
            ImGui.SliderFloat3("slider float3", s_multiVec4f, 0.0f, 1.0f, null, s_multiFlags);
            ImGui.SliderInt3("slider int3", s_multiVec4i, 0, 255, null, s_multiFlags);

            ImGui.SeparatorText("4-wide");
            ImGui.InputFloat4("input float4", s_multiVec4f);
            ImGui.InputInt4("input int4", s_multiVec4i);
            ImGui.DragFloat4("drag float4", s_multiVec4f, 0.01f, 0.0f, 1.0f, null, s_multiFlags);
            ImGui.DragInt4("drag int4", s_multiVec4i, 1.0f, 0, 255, null, s_multiFlags);
            ImGui.SliderFloat4("slider float4", s_multiVec4f, 0.0f, 1.0f, null, s_multiFlags);
            ImGui.SliderInt4("slider int4", s_multiVec4i, 0, 255, null, s_multiFlags);

            ImGui.SeparatorText("Ranges");
            ImGui.DragFloatRange2("range float", ref s_multiRangeBeginF, ref s_multiRangeEndF, 0.25f, 0.0f, 100.0f, "Min: %.1f %%", "Max: %.1f %%", ImGuiSliderFlags.AlwaysClamp);
            ImGui.DragIntRange2("range int", ref s_multiRangeBeginI, ref s_multiRangeEndI, 5.0f, 0, 1000, "Min: %d units", "Max: %d units");
            ImGui.DragIntRange2("range int (no bounds)", ref s_multiRangeBeginI, ref s_multiRangeEndI, 5.0f, 0, 0, "Min: %d units", "Max: %d units");

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsPlotting()

    /// <summary>
    /// Demonstrates basic line and histogram plotting with scrolling buffers and procedural sample callbacks.
    /// </summary>
    public static void DemoWindowWidgetsPlotting()
    {
        if (ImGui.TreeNode("Plotting"))
        {
            ImGui.Text("Need better plotting and graphing? Consider using ImPlot:");
            ImGui.TextLinkOpenURL("https://github.com/epezent/implot");
            ImGui.Separator();

            ImGui.Checkbox("Animate", ref s_plotAnimate);

            // Plot as lines and plot as histogram
            ImGui.PlotLines("Frame Times", s_plotArr, s_plotArr.Length);
            ImGui.PlotHistogram("Histogram", s_plotArr, s_plotArr.Length, 0, null, 0.0f, 1.0f, new ImVec2(0, 80.0f));

            if (!s_plotAnimate || s_plotRefreshTime == 0.0)
                s_plotRefreshTime = ImGui.GetTime();
            while (s_plotRefreshTime < ImGui.GetTime())
            {
                s_plotValues[s_plotValuesOffset] = MathF.Cos(s_plotPhase);
                s_plotValuesOffset = (s_plotValuesOffset + 1) % s_plotValues.Length;
                s_plotPhase += 0.10f * s_plotValuesOffset;
                s_plotRefreshTime += 1.0 / 60.0;
            }

            {
                float average = 0.0f;
                for (int n = 0; n < s_plotValues.Length; n++)
                    average += s_plotValues[n];
                average /= s_plotValues.Length;
                string overlay = $"avg {average:F6}";
                ImGui.PlotLines("Lines", s_plotValues, s_plotValues.Length, s_plotValuesOffset, overlay, -1.0f, 1.0f, new ImVec2(0, 80.0f));
            }

            ImGui.SeparatorText("Functions");
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            ImGui.Combo("func", ref s_plotFuncType, "Sin\0Saw\0");
            ImGui.SameLine();
            ImGui.SliderInt("Sample count", ref s_plotDisplayCount, 1, 400);
            Func<object?, int, float> func = (s_plotFuncType == 0)
                ? ((_, i) => MathF.Sin(i * 0.1f))
                : ((_, i) => (i & 1) != 0 ? 1.0f : -1.0f);
            ImGui.PlotLines("Lines##2", func, null, s_plotDisplayCount, 0, null, -1.0f, 1.0f, new ImVec2(0, 80));
            ImGui.PlotHistogram("Histogram##2", func, null, s_plotDisplayCount, 0, null, -1.0f, 1.0f, new ImVec2(0, 80));

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsProgressBars()

    /// <summary>
    /// Demonstrates deterministic and indeterminate progress bars with animated overlays.
    /// </summary>
    public static void DemoWindowWidgetsProgressBars()
    {
        if (ImGui.TreeNode("Progress Bars"))
        {
            s_progressAccum += s_progressDir * 0.4f * ImGui.GetIO().DeltaTime;
            if (s_progressAccum >= +1.1f) { s_progressAccum = +1.1f; s_progressDir *= -1.0f; }
            if (s_progressAccum <= -0.1f) { s_progressAccum = -0.1f; s_progressDir *= -1.0f; }

            float progress = Math.Clamp(s_progressAccum, 0.0f, 1.0f);

            ImGui.ProgressBar(progress, new ImVec2(0.0f, 0.0f));
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.Text("Progress Bar");

            string buf = $"{(int)(progress * 1753)}/1753";
            ImGui.ProgressBar(progress, new ImVec2(0.0f, 0.0f), buf);

            ImGui.ProgressBar(-1.0f * (float)ImGui.GetTime(), new ImVec2(0.0f, 0.0f), "Searching..");
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.Text("Indeterminate");

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsQueryingStatuses()

    /// <summary>
    /// Demonstrates live querying of item and window status flags (hovered, active, edited, activated, deactivated, etc.).
    /// </summary>
    public static void DemoWindowWidgetsQueryingStatuses()
    {
        if (ImGui.TreeNode("Querying Item Status (Edited/Active/Hovered etc.)"))
        {
            ImGui.Combo("Item Type", ref s_queryItemType, s_queryItemNames);
            ImGui.SameLine();
            HelpMarker("Testing how various types of items are interacting with the IsItemXXX functions. Note that the bool return value of most ImGui function is generally equivalent to calling ImGui::IsItemHovered().");
            ImGui.Checkbox("Item Disabled", ref s_queryItemDisabled);
            ImGui.Checkbox("Override LiveEdit:", ref s_queryLiveEditFlagsOverride);
            ImGui.SameLine();
            if (!s_queryLiveEditFlagsOverride)
                s_queryLiveEditFlags = ImGui.GetItemFlags();
            ImGui.BeginDisabled(!s_queryLiveEditFlagsOverride);
            CheckboxFlags("_LiveEditOnInput", ref s_queryLiveEditFlags, ImGuiItemFlags.LiveEditOnInput);
            ImGui.SameLine();
            CheckboxFlags("_LiveEditOnInputText", ref s_queryLiveEditFlags, ImGuiItemFlags.LiveEditOnInputText);
            ImGui.SameLine();
            CheckboxFlags("_LiveEditOnInputScalar", ref s_queryLiveEditFlags, ImGuiItemFlags.LiveEditOnInputScalar);
            ImGui.EndDisabled();
            if (s_queryLiveEditFlagsOverride)
            {
                ImGui.PushItemFlag(ImGuiItemFlags.LiveEditOnInputText, (s_queryLiveEditFlags & ImGuiItemFlags.LiveEditOnInputText) != 0);
                ImGui.PushItemFlag(ImGuiItemFlags.LiveEditOnInputScalar, (s_queryLiveEditFlags & ImGuiItemFlags.LiveEditOnInputScalar) != 0);
            }

            bool ret = false;
            if (s_queryItemDisabled)
                ImGui.BeginDisabled(true);
            if (s_queryItemType == 0) { ImGui.Text("ITEM: Text"); }
            if (s_queryItemType == 1) { ret = ImGui.Button("ITEM: Button"); }
            if (s_queryItemType == 2) { ImGui.PushItemFlag(ImGuiItemFlags.ButtonRepeat, true); ret = ImGui.Button("ITEM: Button"); ImGui.PopItemFlag(); }
            if (s_queryItemType == 3) { ret = ImGui.Checkbox("ITEM: Checkbox", ref s_queryB); }
            if (s_queryItemType == 4) { ret = ImGui.SliderFloat("ITEM: SliderFloat", ref s_queryCol4f[0], 0.0f, 1.0f); }
            if (s_queryItemType == 5) { ret = ImGui.InputText("ITEM: InputText", ref s_queryStr); }
            if (s_queryItemType == 6) { ret = ImGui.InputTextMultiline("ITEM: InputTextMultiline", ref s_queryStr); }
            if (s_queryItemType == 7) { ret = ImGui.InputFloat("ITEM: InputFloat", ref s_queryCol4f[0], 1.0f); }
            if (s_queryItemType == 8) { ret = ImGui.InputFloat3("ITEM: InputFloat3", s_queryCol4f); }
            if (s_queryItemType == 9) { ret = ImGui.ColorEdit4("ITEM: ColorEdit4", s_queryCol4f); }
            if (s_queryItemType == 10) { ret = ImGui.Selectable("ITEM: Selectable"); }
            if (s_queryItemType == 11) { ret = ImGui.MenuItem("ITEM: MenuItem"); }
            if (s_queryItemType == 12) { ret = ImGui.TreeNode("ITEM: TreeNode"); if (ret) ImGui.TreePop(); }
            if (s_queryItemType == 13) { ret = ImGui.TreeNodeEx("ITEM: TreeNode w/ ImGuiTreeNodeFlags_OpenOnDoubleClick", ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.NoTreePushOnOpen); }
            if (s_queryItemType == 14) { string[] items = new[] { "Apple", "Banana", "Cherry", "Kiwi" }; ret = ImGui.Combo("ITEM: Combo", ref s_queryComboCurrent, items); }
            if (s_queryItemType == 15) { string[] items = new[] { "Apple", "Banana", "Cherry", "Kiwi" }; ret = ImGui.ListBox("ITEM: ListBox", ref s_queryListBoxCurrent, items, items.Length); }

            bool hoveredDelayNone = ImGui.IsItemHovered();
            bool hoveredDelayStationary = ImGui.IsItemHovered(ImGuiHoveredFlags.Stationary);
            bool hoveredDelayShort = ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort);
            bool hoveredDelayNormal = ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal);
            bool hoveredDelayTooltip = ImGui.IsItemHovered(ImGuiHoveredFlags.ForTooltip);

            ImVec2 itemMin = ImGui.GetItemRectMin();
            ImVec2 itemMax = ImGui.GetItemRectMax();
            ImVec2 itemSize = ImGui.GetItemRectSize();

            ImGui.BulletText(
                $"Return value = {ret}\n" +
                $"IsItemFocused() = {ImGui.IsItemFocused()}\n" +
                $"IsItemHovered() = {ImGui.IsItemHovered()}\n" +
                $"IsItemHovered(_AllowWhenBlockedByPopup) = {ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)}\n" +
                $"IsItemHovered(_AllowWhenBlockedByActiveItem) = {ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)}\n" +
                $"IsItemHovered(_AllowWhenOverlappedByItem) = {ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlappedByItem)}\n" +
                $"IsItemHovered(_AllowWhenOverlappedByWindow) = {ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlappedByWindow)}\n" +
                $"IsItemHovered(_AllowWhenDisabled) = {ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)}\n" +
                $"IsItemHovered(_RectOnly) = {ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly)}\n" +
                $"IsItemActive() = {ImGui.IsItemActive()}\n" +
                $"IsItemEdited() = {ImGui.IsItemEdited()}\n" +
                $"IsItemActivated() = {ImGui.IsItemActivated()}\n" +
                $"IsItemDeactivated() = {ImGui.IsItemDeactivated()}\n" +
                $"IsItemDeactivatedAfterEdit() = {ImGui.IsItemDeactivatedAfterEdit()}\n" +
                $"IsItemVisible() = {ImGui.IsItemVisible()}\n" +
                $"IsItemClicked() = {ImGui.IsItemClicked()}\n" +
                $"IsItemToggledOpen() = {ImGui.IsItemToggledOpen()}\n" +
                $"GetItemRectMin() = ({itemMin.X:F1}, {itemMin.Y:F1})\n" +
                $"GetItemRectMax() = ({itemMax.X:F1}, {itemMax.Y:F1})\n" +
                $"GetItemRectSize() = ({itemSize.X:F1}, {itemSize.Y:F1})");

            ImGui.BulletText(
                "with Hovering Delay or Stationary test:\n" +
                $"IsItemHovered() = {hoveredDelayNone}\n" +
                $"IsItemHovered(_Stationary) = {hoveredDelayStationary}\n" +
                $"IsItemHovered(_DelayShort) = {hoveredDelayShort}\n" +
                $"IsItemHovered(_DelayNormal) = {hoveredDelayNormal}\n" +
                $"IsItemHovered(_Tooltip) = {hoveredDelayTooltip}");

            if (s_queryLiveEditFlagsOverride)
            {
                ImGui.PopItemFlag();
                ImGui.PopItemFlag();
            }
            if (s_queryItemDisabled)
                ImGui.EndDisabled();

            ImGui.InputText("unused", ref s_queryUnusedBuf, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            HelpMarker("This widget is only here to be able to tab-out of the widgets above and see e.g. Deactivated() status.");

            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Querying Window Status (Focused/Hovered etc.)"))
        {
            ImGui.Checkbox("Embed everything inside a child window for testing _RootWindow flag.", ref s_queryEmbedAll);
            if (s_queryEmbedAll)
                ImGui.BeginChild("outer_child", new ImVec2(0, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.Borders);

            ImGui.BulletText(
                $"IsWindowFocused() = {ImGui.IsWindowFocused()}\n" +
                $"IsWindowFocused(_ChildWindows) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows)}\n" +
                $"IsWindowFocused(_ChildWindows|_NoPopupHierarchy) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.NoPopupHierarchy)}\n" +
                $"IsWindowFocused(_ChildWindows|_DockHierarchy) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.DockHierarchy)}\n" +
                $"IsWindowFocused(_ChildWindows|_RootWindow) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow)}\n" +
                $"IsWindowFocused(_ChildWindows|_RootWindow|_NoPopupHierarchy) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow | ImGuiFocusedFlags.NoPopupHierarchy)}\n" +
                $"IsWindowFocused(_ChildWindows|_RootWindow|_DockHierarchy) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow | ImGuiFocusedFlags.DockHierarchy)}\n" +
                $"IsWindowFocused(_RootWindow) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.RootWindow)}\n" +
                $"IsWindowFocused(_RootWindow|_NoPopupHierarchy) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.RootWindow | ImGuiFocusedFlags.NoPopupHierarchy)}\n" +
                $"IsWindowFocused(_RootWindow|_DockHierarchy) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.RootWindow | ImGuiFocusedFlags.DockHierarchy)}\n" +
                $"IsWindowFocused(_AnyWindow) = {ImGui.IsWindowFocused(ImGuiFocusedFlags.AnyWindow)}");

            ImGui.BulletText(
                $"IsWindowHovered() = {ImGui.IsWindowHovered()}\n" +
                $"IsWindowHovered(_AllowWhenBlockedByPopup) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)}\n" +
                $"IsWindowHovered(_AllowWhenBlockedByActiveItem) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)}\n" +
                $"IsWindowHovered(_ChildWindows) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)}\n" +
                $"IsWindowHovered(_ChildWindows|_NoPopupHierarchy) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.NoPopupHierarchy)}\n" +
                $"IsWindowHovered(_ChildWindows|_DockHierarchy) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.DockHierarchy)}\n" +
                $"IsWindowHovered(_ChildWindows|_RootWindow) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RootWindow)}\n" +
                $"IsWindowHovered(_ChildWindows|_RootWindow|_NoPopupHierarchy) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RootWindow | ImGuiHoveredFlags.NoPopupHierarchy)}\n" +
                $"IsWindowHovered(_ChildWindows|_RootWindow|_DockHierarchy) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RootWindow | ImGuiHoveredFlags.DockHierarchy)}\n" +
                $"IsWindowHovered(_RootWindow) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.RootWindow)}\n" +
                $"IsWindowHovered(_RootWindow|_NoPopupHierarchy) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.RootWindow | ImGuiHoveredFlags.NoPopupHierarchy)}\n" +
                $"IsWindowHovered(_RootWindow|_DockHierarchy) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.RootWindow | ImGuiHoveredFlags.DockHierarchy)}\n" +
                $"IsWindowHovered(_ChildWindows|_AllowWhenBlockedByPopup) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup)}\n" +
                $"IsWindowHovered(_AnyWindow) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow)}\n" +
                $"IsWindowHovered(_Stationary) = {ImGui.IsWindowHovered(ImGuiHoveredFlags.Stationary)}");

            ImGui.BeginChild("child", new ImVec2(0, 50), ImGuiChildFlags.Borders);
            ImGui.Text("This is another child window for testing the _ChildWindows flag.");
            ImGui.EndChild();
            if (s_queryEmbedAll)
                ImGui.EndChild();

            ImGui.Checkbox("Hovered/Active tests after Begin() for title bar testing", ref s_queryTestWindow);
            if (s_queryTestWindow)
            {
                ImGui.Begin("Title bar Hovered/Active tests", ref s_queryTestWindow);
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem("Close")) { s_queryTestWindow = false; }
                    ImGui.EndPopup();
                }
                ImGui.Text(
                    $"IsItemHovered() after begin = {ImGui.IsItemHovered()} (== is title bar hovered)\n" +
                    $"IsItemActive() after begin = {ImGui.IsItemActive()} (== is window being clicked/moved)\n");
                ImGui.End();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsSelectables()

    /// <summary>
    /// Demonstrates selectable items: basic selections, inline combinations, tables, light-out grids, and alignment styling.
    /// </summary>
    public static void DemoWindowWidgetsSelectables()
    {
        if (ImGui.TreeNode("Selectables"))
        {
            if (ImGui.TreeNode("Basic"))
            {
                ImGui.Selectable("1. I am selectable", ref s_selBasicSelection[0]);
                ImGui.Selectable("2. I am selectable", ref s_selBasicSelection[1]);
                ImGui.Selectable("3. I am selectable", ref s_selBasicSelection[2]);
                if (ImGui.Selectable("4. I am double clickable", s_selBasicSelection[3], ImGuiSelectableFlags.AllowDoubleClick))
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        s_selBasicSelection[3] = !s_selBasicSelection[3];
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Multiple items on the same line"))
            {
                {
                    ImGui.SetNextItemAllowOverlap(); ImGui.Selectable("main.c", ref s_selLineSelected[0]); ImGui.SameLine(); ImGui.SmallButton("Link 1");
                    ImGui.SetNextItemAllowOverlap(); ImGui.Selectable("hello.cpp", ref s_selLineSelected[1]); ImGui.SameLine(); ImGui.SmallButton("Link 2");
                    ImGui.SetNextItemAllowOverlap(); ImGui.Selectable("hello.h", ref s_selLineSelected[2]); ImGui.SameLine(); ImGui.SmallButton("Link 3");
                }

                ImGui.Spacing();
                {
                    float colorMarkerW = ImGui.CalcTextSize("x").X;
                    for (int n = 0; n < 5; n++)
                    {
                        ImGui.PushID(n);
                        ImGui.AlignTextToFramePadding();
                        if (ImGui.Selectable("##selectable", s_selSelectedN == n, ImGuiSelectableFlags.AllowOverlap))
                            s_selSelectedN = n;
                        ImGui.SameLine(0, 0);
                        ImGui.Checkbox("##check", ref s_selChecked[n]);
                        ImGui.SameLine();
                        ImVec4 color = new((n & 1) != 0 ? 1.0f : 0.2f, (n & 2) != 0 ? 1.0f : 0.2f, 0.2f, 1.0f);
                        ImGui.ColorButton("##color", color, ImGuiColorEditFlags.NoTooltip, new ImVec2(colorMarkerW, 0));
                        ImGui.SameLine();
                        ImGui.Text("Some label");
                        ImGui.PopID();
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("In Tables"))
            {
                if (ImGui.BeginTable("split1", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.Borders))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        ImGui.TableNextColumn();
                        ImGui.Selectable($"Item {i}", ref s_selTable1[i]);
                    }
                    ImGui.EndTable();
                }
                ImGui.Spacing();
                if (ImGui.BeginTable("split2", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.Borders))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Selectable($"Item {i}", ref s_selTable2[i], ImGuiSelectableFlags.SpanAllColumns);
                        ImGui.TableNextColumn();
                        ImGui.Text("Some other contents");
                        ImGui.TableNextColumn();
                        ImGui.Text("123456");
                    }
                    ImGui.EndTable();
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Grid"))
            {
                float time = (float)ImGui.GetTime();
                bool winningState = true;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        if (s_selGrid[y, x] == 0) { winningState = false; break; }

                if (winningState)
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new ImVec2(0.5f + 0.5f * MathF.Cos(time * 2.0f), 0.5f + 0.5f * MathF.Sin(time * 3.0f)));

                float size = ImGui.CalcTextSize("Sailor").X;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        if (x > 0)
                            ImGui.SameLine();
                        ImGui.PushID(y * 4 + x);
                        if (ImGui.Selectable("Sailor", s_selGrid[y, x] != 0, 0, new ImVec2(size, size)))
                        {
                            s_selGrid[y, x] ^= 1;
                            if (x > 0) { s_selGrid[y, x - 1] ^= 1; }
                            if (x < 3) { s_selGrid[y, x + 1] ^= 1; }
                            if (y > 0) { s_selGrid[y - 1, x] ^= 1; }
                            if (y < 3) { s_selGrid[y + 1, x] ^= 1; }
                        }
                        ImGui.PopID();
                    }

                if (winningState)
                    ImGui.PopStyleVar();
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Alignment"))
            {
                HelpMarker(
                    "By default, Selectables uses style.SelectableTextAlign but it can be overridden on a per-item " +
                    "basis using PushStyleVar(). You'll probably want to always keep your default situation to " +
                    "left-align otherwise it becomes difficult to layout multiple items on a same line");

                float size = ImGui.CalcTextSize("(1.0,1.0)").X;
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        ImVec2 alignment = new((float)x / 2.0f, (float)y / 2.0f);
                        string name = $"({alignment.X:F1},{alignment.Y:F1})";
                        if (x > 0) ImGui.SameLine();
                        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, alignment);
                        ImGui.Selectable(name, ref s_selAlignSelected[3 * y + x], ImGuiSelectableFlags.None, new ImVec2(size, size));
                        ImGui.PopStyleVar();
                    }
                }
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsSelectionAndMultiSelect()

    /// <summary>
    /// Demonstrates selection-state ownership and the multi-selection API: hand-managed single and
    /// multiple selection, <c>BeginMultiSelect()</c>/<c>EndMultiSelect()</c> with the basic and
    /// external storage helpers, deletion, a dual list box, checkbox rows, multiple selection scopes,
    /// and the advanced option matrix.
    /// </summary>
    /// <param name="demoData">Demo configuration; the assets-browser subsection toggles its flag.</param>
    /// <remarks>
    /// <para>
    /// The canonical delete shortcut is expressed with <c>IsKeyPressed(ImGuiKey.Delete, true)</c>
    /// because <c>Shortcut()</c> is owned by Chunk 10, and post-deletion refocus is skipped for the
    /// same reason (<c>SetKeyboardFocusHere()</c>).
    /// </para>
    /// </remarks>
    public static void DemoWindowWidgetsSelectionAndMultiSelect(ImGuiDemoWindowData? demoData)
    {
        if (ImGui.TreeNode("Selection State & Multi-Select"))
        {
            HelpMarker("Selections can be built using Selectable(), TreeNode() or other widgets. Selection state is owned by application code/data.");

            ImGui.BulletText("Wiki page:");
            ImGui.SameLine();
            ImGui.TextLinkOpenURL("imgui/wiki/Multi-Select", "https://github.com/ocornut/imgui/wiki/Multi-Select");

            // Without any fancy API: manage single-selection by hand.
            if (ImGui.TreeNode("Single-Select"))
            {
                for (int n = 0; n < 5; n++)
                {
                    string label = $"Object {n}";
                    if (ImGui.Selectable(label, s_selectionSingleSelected == n))
                        s_selectionSingleSelected = n;
                }
                ImGui.TreePop();
            }

            // A most-basic manual multi-selection; it cannot support the Shift modifier, which needs
            // BeginMultiSelect().
            if (ImGui.TreeNode("Multi-Select (manual/simplified, without BeginMultiSelect)"))
            {
                HelpMarker("Hold Ctrl and Click to select multiple items.");
                for (int n = 0; n < 5; n++)
                {
                    string label = $"Object {n}";
                    if (ImGui.Selectable(label, s_selectionManualSelected[n]))
                    {
                        if (!ImGui.GetIO().KeyCtrl)
                            Array.Clear(s_selectionManualSelected, 0, s_selectionManualSelected.Length);
                        s_selectionManualSelected[n] = !s_selectionManualSelected[n];
                    }
                }
                ImGui.TreePop();
            }

            // Proper multi-selection through BeginMultiSelect()/EndMultiSelect() with the
            // ImGuiSelectionBasicStorage helper, which applications may reimplement.
            if (ImGui.TreeNode("Multi-Select"))
            {
                ImGui.Text("Supported features:");
                ImGui.BulletText("Keyboard navigation (arrows, page up/down, home/end, space).");
                ImGui.BulletText("Ctrl modifier to preserve and toggle selection.");
                ImGui.BulletText("Shift modifier for range selection.");
                ImGui.BulletText("Ctrl+A to select all.");
                ImGui.BulletText("Escape to clear selection.");
                ImGui.BulletText("Click and drag to box-select.");
                ImGui.Text("Tip: Use 'Demo->Tools->Debug Log->Selection' to see selection requests as they happen.");

                // The default adapter passes the index to SetNextItemSelectionUserData() and stores it.
                const int ItemsCount = 50;
                ImGui.Text($"Selection: {s_selectionBasic.Size}/{ItemsCount}");

                // BeginChild() only offers a scrolling region; it has no role in selection logic.
                if (ImGui.BeginChild("##Basket", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
                {
                    ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.BoxSelect1d;
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(flags, s_selectionBasic.Size, ItemsCount);
                    s_selectionBasic.ApplyRequests(msIo);

                    for (int n = 0; n < ItemsCount; n++)
                    {
                        string label = $"Object {n:D5}: {s_selectionExampleNames[n % s_selectionExampleNames.Length]}";
                        bool itemIsSelected = s_selectionBasic.Contains((uint)n);
                        ImGui.SetNextItemSelectionUserData(n);
                        ImGui.Selectable(label, itemIsSelected);
                    }

                    msIo = ImGui.EndMultiSelect();
                    s_selectionBasic.ApplyRequests(msIo);
                }
                ImGui.EndChild();
                ImGui.TreePop();
            }

            // Demonstrate using the clipper with BeginMultiSelect()/EndMultiSelect().
            if (ImGui.TreeNode("Multi-Select (with clipper)"))
            {
                // Use the default selection adapter: pass the index to
                // SetNextItemSelectionUserData() and store the index in the selection.
                ImGui.Text("Added features:");
                ImGui.BulletText("Using ImGuiListClipper.");

                const int ItemsCount = 10000;
                ImGui.Text($"Selection: {s_selectionClipper.Size}/{ItemsCount}");
                if (ImGui.BeginChild("##Basket", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
                {
                    ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.BoxSelect1d;
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(flags, s_selectionClipper.Size, ItemsCount);
                    s_selectionClipper.ApplyRequests(msIo);

                    ImGuiListClipper clipper = new();
                    clipper.Begin(ItemsCount);
                    if (msIo.RangeSrcItem != -1)
                        clipper.IncludeItemByIndex((int)msIo.RangeSrcItem); // Ensure RangeSrc item is not clipped.
                    while (clipper.Step())
                    {
                        for (int n = clipper.DisplayStart; n < clipper.DisplayEnd; n++)
                        {
                            string label = $"Object {n:D5}: {s_selectionExampleNames[n % s_selectionExampleNames.Length]}";
                            bool itemIsSelected = s_selectionClipper.Contains((uint)n);
                            ImGui.SetNextItemSelectionUserData(n);
                            ImGui.Selectable(label, itemIsSelected);
                        }
                    }

                    msIo = ImGui.EndMultiSelect();
                    s_selectionClipper.ApplyRequests(msIo);
                }
                ImGui.EndChild();
                ImGui.TreePop();
            }

            // Dynamic item list plus deletion support through the multi-select API.
            if (ImGui.TreeNode("Multi-Select (with deletion)"))
            {
                // Items and selection are stored separately; the adapter maps index to persistent id.
                s_selectionDeletion.UserData = s_selectionDeletionItems;
                s_selectionDeletion.AdapterIndexToStorageId = static (self, idx) => ((List<uint>)self.UserData!)[idx];

                ImGui.Text("Added features:");
                ImGui.BulletText("Dynamic list with Delete key support.");
                ImGui.Text($"Selection size: {s_selectionDeletion.Size}/{s_selectionDeletionItems.Count}");

                // Seed the default list and expose add/remove controls.
                if (s_selectionDeletionItemsNextId == 0)
                    for (uint n = 0; n < 50; n++)
                        s_selectionDeletionItems.Add(s_selectionDeletionItemsNextId++);
                if (ImGui.SmallButton("Add 20 items"))
                    for (int n = 0; n < 20; n++)
                        s_selectionDeletionItems.Add(s_selectionDeletionItemsNextId++);
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove 20 items"))
                {
                    for (int n = Math.Min(20, s_selectionDeletionItems.Count); n > 0; n--)
                    {
                        s_selectionDeletion.SetItemSelected(s_selectionDeletionItems[^1], false);
                        s_selectionDeletionItems.RemoveAt(s_selectionDeletionItems.Count - 1);
                    }
                }

                // Submitting the scrolling range ahead of time avoids one-frame readjustment on deletion.
                float itemsHeight = ImGui.GetTextLineHeightWithSpacing();
                ImGui.SetNextWindowContentSize(new ImVec2(0.0f, s_selectionDeletionItems.Count * itemsHeight));

                if (ImGui.BeginChild("##Basket", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
                {
                    ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.BoxSelect1d;
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(flags, s_selectionDeletion.Size, s_selectionDeletionItems.Count);
                    s_selectionDeletion.ApplyRequests(msIo);

                    // Shortcut() is owned by Chunk 10; IsKeyPressed with repeat matches its behavior.
                    bool wantDelete = ImGui.IsKeyPressed(ImGuiKey.Delete, repeat: true) && s_selectionDeletion.Size > 0;
                    int itemCurrIdxToFocus = wantDelete ? s_selectionDeletion.ApplyDeletionPreLoop(msIo, s_selectionDeletionItems.Count) : -1;

                    for (int n = 0; n < s_selectionDeletionItems.Count; n++)
                    {
                        uint itemId = s_selectionDeletionItems[n];
                        string label = $"Object {itemId:D5}: {s_selectionExampleNames[(int)(itemId % (uint)s_selectionExampleNames.Length)]}";

                        bool itemIsSelected = s_selectionDeletion.Contains(itemId);
                        ImGui.SetNextItemSelectionUserData(n);
                        ImGui.Selectable(label, itemIsSelected);
                        // Refocus after deletion would call SetKeyboardFocusHere(-1), owned by Chunk 10.
                    }

                    msIo = ImGui.EndMultiSelect();
                    s_selectionDeletion.ApplyRequests(msIo);
                    if (wantDelete)
                        s_selectionDeletion.ApplyDeletionPostLoop(msIo, s_selectionDeletionItems, itemCurrIdxToFocus);
                }
                ImGui.EndChild();
                ImGui.TreePop();
            }

            // Dual list box: two selection stores that move persistent item identifiers between sides.
            if (ImGui.TreeNode("Multi-Select (dual list box)"))
            {
                if (s_selectionDualListBox.Items[0].Count == 0 && s_selectionDualListBox.Items[1].Count == 0)
                    for (int itemId = 0; itemId < s_selectionExampleNames.Length; itemId++)
                        s_selectionDualListBox.Items[0].Add((uint)itemId);

                s_selectionDualListBox.Show();
                ImGui.TreePop();
            }

            // Demonstrate using the clipper with BeginMultiSelect()/EndMultiSelect() inside a table.
            if (ImGui.TreeNode("Multi-Select (in a table)"))
            {
                const int ItemsCount = 10000;
                ImGui.Text($"Selection: {s_selectionTable.Size}/{ItemsCount}");
                if (ImGui.BeginTable("##Basket", 2, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter, new ImVec2(0.0f, ImGui.GetFontSize() * 20.0f)))
                {
                    ImGui.TableSetupColumn("Object");
                    ImGui.TableSetupColumn("Action");
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableHeadersRow();

                    ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.BoxSelect1d;
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(flags, s_selectionTable.Size, ItemsCount);
                    s_selectionTable.ApplyRequests(msIo);

                    ImGuiListClipper clipper = new();
                    clipper.Begin(ItemsCount);
                    if (msIo.RangeSrcItem != -1)
                        clipper.IncludeItemByIndex((int)msIo.RangeSrcItem); // Ensure RangeSrc item is not clipped.
                    while (clipper.Step())
                    {
                        for (int n = clipper.DisplayStart; n < clipper.DisplayEnd; n++)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.PushID(n);
                            string label = $"Object {n:D5}: {s_selectionExampleNames[n % s_selectionExampleNames.Length]}";
                            bool itemIsSelected = s_selectionTable.Contains((uint)n);
                            ImGui.SetNextItemSelectionUserData(n);
                            ImGui.Selectable(label, itemIsSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap);
                            ImGui.TableNextColumn();
                            ImGui.SmallButton("hello");
                            ImGui.PopID();
                        }
                    }

                    msIo = ImGui.EndMultiSelect();
                    s_selectionTable.ApplyRequests(msIo);
                    ImGui.EndTable();
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Multi-Select (checkboxes)"))
            {
                ImGui.Text("In a list of checkboxes (not selectable):");
                ImGui.BulletText("Using _NoAutoSelect + _NoAutoClear flags.");
                ImGui.BulletText("Shift+Click to check multiple boxes.");
                ImGui.BulletText("Shift+Keyboard to copy current value to other boxes.");

                // Checkbox rows use NoAutoSelect + NoAutoClear and the external storage helper.
                CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoAutoSelect", ref s_selectionCheckboxFlags, ImGuiMultiSelectFlags.NoAutoSelect);
                CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoAutoClear", ref s_selectionCheckboxFlags, ImGuiMultiSelectFlags.NoAutoClear);
                CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_BoxSelect2d", ref s_selectionCheckboxFlags, ImGuiMultiSelectFlags.BoxSelect2d);

                if (ImGui.BeginChild("##Basket", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeY))
                {
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(s_selectionCheckboxFlags, -1, s_selectionCheckboxItems.Length);
                    ImGuiSelectionExternalStorage storageWrapper = new();
                    storageWrapper.UserData = s_selectionCheckboxItems;
                    storageWrapper.AdapterSetItemSelected = static (self, n, selected) => ((bool[])self.UserData!)[n] = selected;
                    storageWrapper.ApplyRequests(msIo);
                    for (int n = 0; n < s_selectionCheckboxItems.Length; n++)
                    {
                        string label = $"Item {n}";
                        ImGui.SetNextItemSelectionUserData(n);
                        ImGui.Checkbox(label, ref s_selectionCheckboxItems[n]);
                    }
                    msIo = ImGui.EndMultiSelect();
                    storageWrapper.ApplyRequests(msIo);
                }
                ImGui.EndChild();
                ImGui.TreePop();
            }

            // Several independent selection scopes in the same window.
            if (ImGui.TreeNode("Multi-Select (multiple scopes)"))
            {
                const int ScopesCount = 3;
                const int ItemsCount = 8; // Per scope.

                // ScopeRect keeps each selection scope independent from the others in the same window.
                if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ScopeWindow", ref s_selectionScopesFlags, ImGuiMultiSelectFlags.ScopeWindow)
                    && (s_selectionScopesFlags & ImGuiMultiSelectFlags.ScopeWindow) != 0)
                    s_selectionScopesFlags &= ~ImGuiMultiSelectFlags.ScopeRect;
                if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ScopeRect", ref s_selectionScopesFlags, ImGuiMultiSelectFlags.ScopeRect)
                    && (s_selectionScopesFlags & ImGuiMultiSelectFlags.ScopeRect) != 0)
                    s_selectionScopesFlags &= ~ImGuiMultiSelectFlags.ScopeWindow;
                CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ClearOnClickVoid", ref s_selectionScopesFlags, ImGuiMultiSelectFlags.ClearOnClickVoid);
                CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_BoxSelect1d", ref s_selectionScopesFlags, ImGuiMultiSelectFlags.BoxSelect1d);

                for (int selectionScopeN = 0; selectionScopeN < ScopesCount; selectionScopeN++)
                {
                    ImGui.PushID(selectionScopeN);
                    ImGuiSelectionBasicStorage selection = s_selectionScopes[selectionScopeN];
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(s_selectionScopesFlags, selection.Size, ItemsCount);
                    selection.ApplyRequests(msIo);

                    ImGui.SeparatorText("Selection scope");
                    ImGui.Text($"Selection size: {selection.Size}/{ItemsCount}");

                    for (int n = 0; n < ItemsCount; n++)
                    {
                        string label = $"Object {n:D5}: {s_selectionExampleNames[n % s_selectionExampleNames.Length]}";
                        bool itemIsSelected = selection.Contains((uint)n);
                        ImGui.SetNextItemSelectionUserData(n);
                        ImGui.Selectable(label, itemIsSelected);
                    }

                    msIo = ImGui.EndMultiSelect();
                    selection.ApplyRequests(msIo);
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }

            // The assets-browser example app owns its own multi-selection demonstration.
            if (ImGui.TreeNode("Multi-Select (tiled assets browser)"))
            {
                bool showAssetsBrowser = demoData?.ShowAppAssetsBrowser ?? false;
                if (ImGui.Checkbox("Assets Browser", ref showAssetsBrowser) && demoData != null)
                    demoData.ShowAppAssetsBrowser = showAssetsBrowser;
                ImGui.Text("(also access from 'Examples->Assets Browser' in menu)");
                ImGui.TreePop();
            }

            // Demonstrate supporting multiple selection in a tree. Selection user data carries the node
            // reference directly rather than a linear index, and range requests interpolate in
            // user-visible order over open nodes only.
            if (ImGui.TreeNode("Multi-Select (trees)"))
            {
                HelpMarker(
                    "This is rather advanced and experimental. If you are getting started with multi-select, " +
                    "please don't start by looking at how to use it for a tree!\n\n" +
                    "Future versions will try to simplify and formalize some of this.");

                s_selectionTreeRoot ??= ExampleTreeCreateDemoTree();
                ImGui.Text($"Selection size: {s_selectionTree.Size}");

                if (ImGui.BeginChild("##Tree", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
                {
                    ExampleTreeNode tree = s_selectionTreeRoot!;
                    ImGuiMultiSelectFlags msFlags = ImGuiMultiSelectFlags.ClearOnEscape | ImGuiMultiSelectFlags.BoxSelect2d;
                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(msFlags, s_selectionTree.Size, -1);
                    ExampleTreeFuncs.ApplySelectionRequests(msIo, tree, s_selectionTree);
                    foreach (ExampleTreeNode node in tree.Children)
                        ExampleTreeFuncs.DrawNode(node, s_selectionTree);
                    msIo = ImGui.EndMultiSelect();
                    ExampleTreeFuncs.ApplySelectionRequests(msIo, tree, s_selectionTree);
                }
                ImGui.EndChild();

                ImGui.TreePop();
            }

            // Advanced demonstration: deletion, drag and drop, tree nodes, and table placement.
            if (ImGui.TreeNode("Multi-Select (advanced)"))
            {
                if (ImGui.TreeNode("Options"))
                {
                    if (ImGui.RadioButton("Selectables", s_selectionAdvancedWidgetType == SelectionWidgetType.Selectable)) { s_selectionAdvancedWidgetType = SelectionWidgetType.Selectable; }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Tree nodes", s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode)) { s_selectionAdvancedWidgetType = SelectionWidgetType.TreeNode; }
                    ImGui.SameLine();
                    HelpMarker("TreeNode() is technically supported but... using this correctly is more complicated (you need some sort of linear/random access to your tree, which is suited to advanced trees setups already implementing filters and clipper. We will work toward simplifying and demoing this.\n\nFor now the tree demo is actually a little bit meaningless because it is an empty tree with only root nodes.");
                    ImGui.Checkbox("Enable clipper", ref s_selectionAdvancedUseClipper);
                    ImGui.Checkbox("Enable deletion", ref s_selectionAdvancedUseDeletion);
                    ImGui.Checkbox("Enable drag & drop", ref s_selectionAdvancedUseDragDrop);
                    ImGui.Checkbox("Show in a table", ref s_selectionAdvancedShowInTable);
                    ImGui.Checkbox("Show color button", ref s_selectionAdvancedShowColorButton);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_SingleSelect", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.SingleSelect);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoSelectAll", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.NoSelectAll);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoRangeSelect", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.NoRangeSelect);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoAutoSelect", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.NoAutoSelect);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoAutoClear", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.NoAutoClear);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoAutoClearOnReselect", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.NoAutoClearOnReselect);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_NoSelectOnRightClick", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.NoSelectOnRightClick);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_BoxSelect1d", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.BoxSelect1d);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_BoxSelect2d", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.BoxSelect2d);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_BoxSelectNoScroll", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.BoxSelectNoScroll);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ClearOnEscape", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.ClearOnEscape);
                    CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ClearOnClickVoid", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.ClearOnClickVoid);
                    if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ScopeWindow", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.ScopeWindow)
                        && (s_selectionAdvancedFlags & ImGuiMultiSelectFlags.ScopeWindow) != 0)
                        s_selectionAdvancedFlags &= ~ImGuiMultiSelectFlags.ScopeRect;
                    if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_ScopeRect", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.ScopeRect)
                        && (s_selectionAdvancedFlags & ImGuiMultiSelectFlags.ScopeRect) != 0)
                        s_selectionAdvancedFlags &= ~ImGuiMultiSelectFlags.ScopeWindow;
                    if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_SelectOnAuto", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.SelectOnAuto))
                        s_selectionAdvancedFlags &= ~(ImGuiMultiSelectFlags.SelectOnMask_ ^ ImGuiMultiSelectFlags.SelectOnAuto);
                    ImGui.SameLine();
                    HelpMarker("Apply selection on mouse down when clicking on unselected item, on mouse up when clicking on selected item. (Default)");
                    if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_SelectOnClickAlways", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.SelectOnClickAlways))
                        s_selectionAdvancedFlags &= ~(ImGuiMultiSelectFlags.SelectOnMask_ ^ ImGuiMultiSelectFlags.SelectOnClickAlways);
                    ImGui.SameLine();
                    HelpMarker("Prevents Drag and Drop from being used on multi-selection, but allows e.g. BoxSelect to always reselect even when clicking inside an existing selection. (Excel style behavior)");
                    if (CheckboxFlagsMultiSelect("ImGuiMultiSelectFlags_SelectOnClickRelease", ref s_selectionAdvancedFlags, ImGuiMultiSelectFlags.SelectOnClickRelease))
                        s_selectionAdvancedFlags &= ~(ImGuiMultiSelectFlags.SelectOnMask_ ^ ImGuiMultiSelectFlags.SelectOnClickRelease);
                    ImGui.SameLine();
                    HelpMarker("Allow dragging an unselected item without altering selection.");
                    ImGui.TreePop();
                }

                // Seed the default list with 1000 items keyed by a persistent identifier.
                if (s_selectionAdvancedItemsNextId == 0)
                    for (int n = 0; n < 1000; n++)
                        s_selectionAdvancedItems.Add(s_selectionAdvancedItemsNextId++);

                ImGui.Text($"Selection size: {s_selectionAdvancedSelection.Size}/{s_selectionAdvancedItems.Count}");

                float itemsHeight = s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode
                    ? ImGui.GetFontSize()
                    : ImGui.GetTextLineHeightWithSpacing();
                ImGui.SetNextWindowContentSize(new ImVec2(0.0f, s_selectionAdvancedItems.Count * itemsHeight));
                if (ImGui.BeginChild("##Basket", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
                {
                    ImVec2 colorButtonSize = new(ImGui.GetFontSize(), ImGui.GetFontSize());
                    if (s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode)
                        ImGui.PushStyleVarY(ImGuiStyleVar.ItemSpacing, 0.0f);

                    ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(s_selectionAdvancedFlags, s_selectionAdvancedSelection.Size, s_selectionAdvancedItems.Count);
                    s_selectionAdvancedSelection.ApplyRequests(msIo);

                    bool wantDelete = (ImGui.IsKeyPressed(ImGuiKey.Delete, repeat: true) && s_selectionAdvancedSelection.Size > 0)
                        || s_selectionAdvancedRequestDeletionFromMenu;
                    int itemCurrIdxToFocus = wantDelete ? s_selectionAdvancedSelection.ApplyDeletionPreLoop(msIo, s_selectionAdvancedItems.Count) : -1;
                    s_selectionAdvancedRequestDeletionFromMenu = false;

                    if (s_selectionAdvancedShowInTable)
                    {
                        if (s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode)
                            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new ImVec2(0.0f, 0.0f));
                        ImGui.BeginTable("##Split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX);
                        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch, 0.70f);
                        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch, 0.30f);
                    }

                    ImGuiListClipper clipper = new();
                    if (s_selectionAdvancedUseClipper)
                    {
                        clipper.Begin(s_selectionAdvancedItems.Count);
                        if (itemCurrIdxToFocus != -1)
                            clipper.IncludeItemByIndex(itemCurrIdxToFocus); // Ensure focused item is not clipped.
                        if (msIo.RangeSrcItem != -1)
                            clipper.IncludeItemByIndex((int)msIo.RangeSrcItem); // Ensure RangeSrc item is not clipped.
                    }

                    while (!s_selectionAdvancedUseClipper || clipper.Step())
                    {
                        int itemBegin = s_selectionAdvancedUseClipper ? clipper.DisplayStart : 0;
                        int itemEnd = s_selectionAdvancedUseClipper ? clipper.DisplayEnd : s_selectionAdvancedItems.Count;
                        for (int n = itemBegin; n < itemEnd; n++)
                        {
                            if (s_selectionAdvancedShowInTable)
                                ImGui.TableNextColumn();

                            int itemId = s_selectionAdvancedItems[n];
                            string itemCategory = s_selectionExampleNames[itemId % s_selectionExampleNames.Length];
                            string label = $"Object {itemId:D5}: {itemCategory}";

                            // The identifier is derived from the persistent item id, not the index, so focus
                            // is restored correctly after deletion.
                            ImGui.PushID(itemId);

                            // The color button tests that keyboard range selection landing on a non-scope item
                            // does not alter the selection.
                            if (s_selectionAdvancedShowColorButton)
                            {
                                uint dummyCol = ((uint)n * 0xC250B74Bu) | 0xFF000000u; // IM_COL32_A_MASK.
                                ImGui.ColorButton("##", ImGui.ColorConvertU32ToFloat4(dummyCol), ImGuiColorEditFlags.NoTooltip, colorButtonSize);
                                ImGui.SameLine();
                            }

                            bool itemIsSelected = s_selectionAdvancedSelection.Contains((uint)n);
                            bool itemIsOpen = false;
                            ImGui.SetNextItemSelectionUserData(n);
                            if (s_selectionAdvancedWidgetType == SelectionWidgetType.Selectable)
                            {
                                ImGui.Selectable(label, itemIsSelected);
                            }
                            else if (s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode)
                            {
                                ImGuiTreeNodeFlags treeNodeFlags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
                                if (itemIsSelected)
                                    treeNodeFlags |= ImGuiTreeNodeFlags.Selected;
                                itemIsOpen = ImGui.TreeNodeEx(label, treeNodeFlags);
                            }

                            // Refocus after deletion would call SetKeyboardFocusHere(-1), owned by Chunk 10.

                            if (s_selectionAdvancedUseDragDrop && ImGui.BeginDragDropSource())
                            {
                                // The payload carries the full selection, or the single unselected item that
                                // is only possible with the SelectOnClickRelease flag.
                                if (ImGui.GetDragDropPayload() == null)
                                {
                                    List<int> payloadItems = new();
                                    if (!itemIsSelected)
                                    {
                                        payloadItems.Add(itemId);
                                    }
                                    else
                                    {
                                        int opaqueIt = -1;
                                        while (s_selectionAdvancedSelection.GetNextSelectedItem(ref opaqueIt, out uint selectedId))
                                            payloadItems.Add((int)selectedId);
                                    }
                                    ImGui.SetDragDropPayload("MULTISELECT_DEMO_ITEMS", payloadItems.ToArray());
                                }

                                ImGuiPayload? payload = ImGui.GetDragDropPayload();
                                if (payload?.ObjectData is int[] payloadItemIds)
                                {
                                    if (payloadItemIds.Length == 1)
                                        ImGui.Text($"Object {payloadItemIds[0]:D5}: {s_selectionExampleNames[payloadItemIds[0] % s_selectionExampleNames.Length]}");
                                    else
                                        ImGui.Text($"Dragging {payloadItemIds.Length} objects");
                                }
                                ImGui.EndDragDropSource();
                            }

                            if (s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode && itemIsOpen)
                                ImGui.TreePop();

                            if (ImGui.BeginPopupContextItem())
                            {
                                ImGui.BeginDisabled(!s_selectionAdvancedUseDeletion || s_selectionAdvancedSelection.Size == 0);
                                if (ImGui.Selectable($"Delete {s_selectionAdvancedSelection.Size} item(s)###DeleteSelected"))
                                    s_selectionAdvancedRequestDeletionFromMenu = true;
                                ImGui.EndDisabled();
                                ImGui.Selectable("Close");
                                ImGui.EndPopup();
                            }

                            if (s_selectionAdvancedShowInTable)
                            {
                                ImGui.TableNextColumn();
                                ImGui.SetNextItemWidth(-float.Epsilon);
                                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new ImVec2(0.0f, 0.0f));
                                string categoryCopy = itemCategory;
                                ImGui.InputText("##NoLabel", ref categoryCopy, ImGuiInputTextFlags.ReadOnly);
                                ImGui.PopStyleVar();
                            }

                            ImGui.PopID();
                        }
                        if (!s_selectionAdvancedUseClipper)
                            break;
                    }

                    if (s_selectionAdvancedShowInTable)
                    {
                        ImGui.EndTable();
                        if (s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode)
                            ImGui.PopStyleVar();
                    }

                    msIo = ImGui.EndMultiSelect();
                    s_selectionAdvancedSelection.ApplyRequests(msIo);
                    if (wantDelete)
                        s_selectionAdvancedSelection.ApplyDeletionPostLoop(msIo, s_selectionAdvancedItems, itemCurrIdxToFocus);

                    if (s_selectionAdvancedWidgetType == SelectionWidgetType.TreeNode)
                        ImGui.PopStyleVar();
                }
                ImGui.EndChild();
                ImGui.TreePop();
            }
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsTabs()

    /// <summary>Persistent flags used by the "Advanced &amp; Close Button" tab demo.</summary>
    private static ImGuiTabBarFlags s_tabsAdvancedFlags = ImGuiTabBarFlags.Reorderable;

    /// <summary>Persistent open state of the four tabs in the "Advanced &amp; Close Button" demo.</summary>
    private static readonly bool[] s_tabsOpened = { true, true, true, true };

    /// <summary>Identifiers of the dynamically added tabs in the "TabItemButton" demo.</summary>
    private static readonly List<int> s_tabsActiveTabs = new();

    /// <summary>Monotonic identifier source for tabs added through the trailing "+" button.</summary>
    private static int s_tabsNextTabId;

    /// <summary>Whether the leading "?" TabItemButton is submitted.</summary>
    private static bool s_tabsShowLeadingButton = true;

    /// <summary>Whether the trailing "+" TabItemButton is submitted.</summary>
    private static bool s_tabsShowTrailingButton = true;

    /// <summary>Flags used by the "TabItemButton &amp; Leading/Trailing flags" tab demo.</summary>
    private static ImGuiTabBarFlags s_tabsItemButtonFlags =
        ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.FittingPolicyShrink;

    /// <summary>
    /// Exposes the mutually exclusive tab-bar fitting policy flags as checkboxes, ensuring one policy is
    /// always selected.
    /// </summary>
    /// <param name="pFlags">Tab bar flags updated in place.</param>
    private static void EditTabBarFittingPolicyFlags(ref ImGuiTabBarFlags pFlags)
    {
        if ((pFlags & ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMask_) == 0)
            pFlags |= ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyDefault_;
        if (CheckboxFlags("ImGuiTabBarFlags_FittingPolicyMixed", ref pFlags, ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMixed))
            pFlags &= ~(ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMask_ ^ ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMixed);
        if (CheckboxFlags("ImGuiTabBarFlags_FittingPolicyShrink", ref pFlags, ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyShrink))
            pFlags &= ~(ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMask_ ^ ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyShrink);
        if (CheckboxFlags("ImGuiTabBarFlags_FittingPolicyScroll", ref pFlags, ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyScroll))
            pFlags &= ~(ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMask_ ^ ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyScroll);
    }

    /// <summary>
    /// Demonstrates basic tab bars, advanced tab-bar flags with per-tab close buttons, and
    /// <c>TabItemButton</c> with leading/trailing placement.
    /// </summary>
    public static void DemoWindowWidgetsTabs()
    {
        if (ImGui.TreeNode("Tabs"))
        {
            if (ImGui.TreeNode("Basic"))
            {
                ImGuiTabBarFlags tabBarFlags = ImGuiTabBarFlags.None;
                if (ImGui.BeginTabBar("MyTabBar", tabBarFlags))
                {
                    if (ImGui.BeginTabItem("Avocado"))
                    {
                        ImGui.Text("This is the Avocado tab!\nblah blah blah blah blah");
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Broccoli"))
                    {
                        ImGui.Text("This is the Broccoli tab!\nblah blah blah blah blah");
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Cucumber"))
                    {
                        ImGui.Text("This is the Cucumber tab!\nblah blah blah blah blah");
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                ImGui.Separator();
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Advanced & Close Button"))
            {
                CheckboxFlags("ImGuiTabBarFlags_Reorderable", ref s_tabsAdvancedFlags, ImGuiTabBarFlags.Reorderable);
                CheckboxFlags("ImGuiTabBarFlags_AutoSelectNewTabs", ref s_tabsAdvancedFlags, ImGuiTabBarFlags.AutoSelectNewTabs);
                CheckboxFlags("ImGuiTabBarFlags_TabListPopupButton", ref s_tabsAdvancedFlags, ImGuiTabBarFlags.TabListPopupButton);
                CheckboxFlags("ImGuiTabBarFlags_NoCloseWithMiddleMouseButton", ref s_tabsAdvancedFlags, ImGuiTabBarFlags.NoCloseWithMiddleMouseButton);
                CheckboxFlags("ImGuiTabBarFlags_DrawSelectedOverline", ref s_tabsAdvancedFlags, ImGuiTabBarFlags.DrawSelectedOverline);
                EditTabBarFittingPolicyFlags(ref s_tabsAdvancedFlags);

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Opened:");
                string[] names = { "Artichoke", "Beetroot", "Celery", "Daikon" };
                for (int n = 0; n < names.Length; n++)
                {
                    ImGui.SameLine();
                    ImGui.Checkbox(names[n], ref s_tabsOpened[n]);
                }

                if (ImGui.BeginTabBar("MyTabBar", s_tabsAdvancedFlags))
                {
                    for (int n = 0; n < names.Length; n++)
                    {
                        if (s_tabsOpened[n] && ImGui.BeginTabItem(names[n], ref s_tabsOpened[n], ImGuiTabItemFlags.None))
                        {
                            ImGui.Text($"This is the {names[n]} tab!");
                            if ((n & 1) != 0)
                                ImGui.Text("I am an odd tab.");
                            ImGui.EndTabItem();
                        }
                    }
                    ImGui.EndTabBar();
                }
                ImGui.Separator();
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("TabItemButton & Leading/Trailing flags"))
            {
                if (s_tabsNextTabId == 0)
                    for (int i = 0; i < 3; i++)
                        s_tabsActiveTabs.Add(s_tabsNextTabId++);

                ImGui.Checkbox("Show Leading TabItemButton()", ref s_tabsShowLeadingButton);
                ImGui.Checkbox("Show Trailing TabItemButton()", ref s_tabsShowTrailingButton);
                EditTabBarFittingPolicyFlags(ref s_tabsItemButtonFlags);

                if (ImGui.BeginTabBar("MyTabBar", s_tabsItemButtonFlags))
                {
                    if (s_tabsShowLeadingButton)
                        if (ImGui.TabItemButton("?", ImGuiTabItemFlags.Leading | ImGuiTabItemFlags.NoTooltip))
                            ImGui.OpenPopup("MyHelpMenu");
                    if (ImGui.BeginPopup("MyHelpMenu"))
                    {
                        ImGui.Selectable("Hello!");
                        ImGui.EndPopup();
                    }

                    if (s_tabsShowTrailingButton)
                        if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing | ImGuiTabItemFlags.NoTooltip))
                            s_tabsActiveTabs.Add(s_tabsNextTabId++);

                    for (int n = 0; n < s_tabsActiveTabs.Count;)
                    {
                        bool open = true;
                        string name = $"{s_tabsActiveTabs[n]:D4}";
                        if (ImGui.BeginTabItem(name, ref open, ImGuiTabItemFlags.None))
                        {
                            ImGui.Text($"This is the {name} tab!");
                            ImGui.EndTabItem();
                        }

                        if (!open)
                            s_tabsActiveTabs.RemoveAt(n);
                        else
                            n++;
                    }
                    ImGui.EndTabBar();
                }
                ImGui.Separator();
                ImGui.TreePop();
            }
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsText()

    /// <summary>
    /// Demonstrates colored text, dynamic font scales and sizes, word wrapping boundaries, and UTF-8 multi-byte glyphs.
    /// </summary>
    public static void DemoWindowWidgetsText()
    {
        if (ImGui.TreeNode("Text"))
        {
            if (ImGui.TreeNode("Colorful Text"))
            {
                ImGui.TextColored(new ImVec4(1.0f, 0.0f, 1.0f, 1.0f), "Pink");
                ImGui.TextColored(new ImVec4(1.0f, 1.0f, 0.0f, 1.0f), "Yellow");
                ImGui.TextDisabled("Disabled");
                ImGui.SameLine(); HelpMarker("The TextDisabled color is stored in ImGuiStyle.");
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Font Size"))
            {
                ImGuiStyle style = ImGui.GetStyle();
                float globalScale = style.FontScaleMain * style.FontScaleDpi;
                ImGui.Text($"style.FontScaleMain = {style.FontScaleMain:0.00}");
                ImGui.Text($"style.FontScaleDpi = {style.FontScaleDpi:0.00}");
                ImGui.Text($"global_scale = ~{globalScale:0.00}");
                ImGui.Text($"FontSize = {ImGui.GetFontSize():0.00}");

                ImGui.SeparatorText("");
                ImGui.SliderFloat("custom_size", ref s_textCustomSize, 10.0f, 100.0f, "%.0f");
                ImGui.Text("ImGui::PushFont(nullptr, custom_size);");
                ImGui.PushFont(null, s_textCustomSize);
                ImGui.Text($"FontSize = {ImGui.GetFontSize():F2} (== {s_textCustomSize:F2} * global_scale)");
                ImGui.PopFont();

                ImGui.SeparatorText("");
                ImGui.SliderFloat("custom_scale", ref s_textCustomScale, 0.5f, 4.0f, "%.2f");
                ImGui.Text("ImGui::PushFont(nullptr, style.FontSizeBase * custom_scale);");
                ImGui.PushFont(null, style.FontSizeBase * s_textCustomScale);
                ImGui.Text($"FontSize = {ImGui.GetFontSize():F2} (== style.FontSizeBase * {s_textCustomScale:F2} * global_scale)");
                ImGui.PopFont();

                ImGui.SeparatorText("");
                for (float scaling = 0.5f; scaling <= 4.0f; scaling += 0.5f)
                {
                    ImGui.PushFont(null, style.FontSizeBase * scaling);
                    ImGui.Text($"FontSize = {ImGui.GetFontSize():F2} (== style.FontSizeBase * {scaling:F2} * global_scale)");
                    ImGui.PopFont();
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Word Wrapping"))
            {
                ImGui.TextWrapped(
                    "This text should automatically wrap on the edge of the window. The current implementation " +
                    "for text wrapping follows simple rules suitable for English and possibly other languages.");
                ImGui.Spacing();

                ImGui.SliderFloat("Wrap width", ref s_textWrapWidth, -20, 600, "%.0f");

                ImDrawList drawList = ImGui.GetWindowDrawList();
                for (int n = 0; n < 2; n++)
                {
                    ImGui.Text($"Test paragraph {n}:");
                    ImVec2 pos = ImGui.GetCursorScreenPos();
                    ImVec2 markerMin = new(pos.X + s_textWrapWidth, pos.Y);
                    ImVec2 markerMax = new(pos.X + s_textWrapWidth + 10, pos.Y + ImGui.GetTextLineHeight());
                    ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + s_textWrapWidth);
                    if (n == 0)
                        ImGui.Text($"The lazy dog is a good dog. This paragraph should fit within {s_textWrapWidth:F0} pixels. Testing a 1 character word. The quick brown fox jumps over the lazy dog.");
                    else
                        ImGui.Text("aaaaaaaa bbbbbbbb, c cccccccc,dddddddd. d eeeeeeee   ffffffff. gggggggg!hhhhhhhh");

                    drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new ImVec4(1.0f, 1.0f, 0.0f, 1.0f)));
                    drawList.AddRectFilled(markerMin, markerMax, ImGui.GetColorU32(new ImVec4(1.0f, 0.0f, 1.0f, 1.0f)));
                    ImGui.PopTextWrapPos();
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("UTF-8 Text"))
            {
                ImGui.TextWrapped(
                    "CJK text will only appear if the font was loaded with the appropriate CJK character ranges. " +
                    "Call io.Fonts->AddFontFromFileTTF() manually to load extra character ranges. " +
                    "Read docs/FONTS.md for details.");
                ImGui.Text("Hiragana: \u304B\u304D\u304F\u3051\u3053 (kakikukeko)");
                ImGui.Text("Kanjis: \u65E5\u672C\u8A9E (nihongo)");
                ImGui.InputText("UTF-8 input", ref s_textUtf8Buf);
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsTextFilter()

    /// <summary>
    /// Demonstrates <c>ImGuiTextFilter</c>, a helper that performs simple include/exclude filtering on
    /// text strings, and the bullet list of file names it filters.
    /// </summary>
    public static void DemoWindowWidgetsTextFilter()
    {
        if (ImGui.TreeNode("Text Filter"))
        {
            // Helper class to easy setup a text filter.
            // You may want to implement a more feature-full filtering scheme in your own application.
            HelpMarker("Not a widget per-se, but ImGuiTextFilter is a helper to perform simple filtering on text strings.");
            ImGui.Text("Filter usage:\n"
                + "  \"\"         display all lines\n"
                + "  \"xxx\"      display lines containing \"xxx\"\n"
                + "  \"xxx,yyy\"  display lines containing \"xxx\" or \"yyy\"\n"
                + "  \"-xxx\"     hide lines containing \"xxx\"");
            s_textFilter.Draw();
            string[] lines = { "aaa1.c", "bbb1.c", "ccc1.c", "aaa2.cpp", "bbb2.cpp", "ccc2.cpp", "abc.h", "hello, world" };
            for (int i = 0; i < lines.Length; i++)
                if (s_textFilter.PassFilter(lines[i]))
                    ImGui.BulletText(lines[i]);
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsTextInput()

    /// <summary>
    /// Demonstrates single-line, multi-line, password, filtered, and callback-driven text inputs.
    /// </summary>
    public static void DemoWindowWidgetsTextInput()
    {
        if (ImGui.TreeNode("Text Input"))
        {
            if (ImGui.TreeNode("Multi-line Text Input"))
            {
                HelpMarker("You can use the ImGuiInputTextFlags_CallbackResize facility if you need to wire InputTextMultiline() to a dynamic string type. Hikari supports ref string natively.");
                CheckboxFlags("ImGuiInputTextFlags_ReadOnly", ref s_inputMultiFlags, ImGuiInputTextFlags.ReadOnly);
                CheckboxFlags("ImGuiInputTextFlags_WordWrap", ref s_inputMultiFlags, ImGuiInputTextFlags.WordWrap);
                ImGui.SameLine(); HelpMarker("Feature is currently in Beta. Please read comments in imgui.h");
                CheckboxFlags("ImGuiInputTextFlags_AllowTabInput", ref s_inputMultiFlags, ImGuiInputTextFlags.AllowTabInput);
                ImGui.SameLine(); HelpMarker("When _AllowTabInput is set, passing through the widget with Tabbing doesn't automatically activate it, in order to also cycling through subsequent widgets.");
                CheckboxFlags("ImGuiInputTextFlags_CtrlEnterForNewLine", ref s_inputMultiFlags, ImGuiInputTextFlags.CtrlEnterForNewLine);
                ImGui.InputTextMultiline("##source", ref s_inputMultiText, new ImVec2(-float.Epsilon, ImGui.GetTextLineHeight() * 16), s_inputMultiFlags);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Filtered Text Input"))
            {
                ImGui.InputText("default", ref s_inputBuf1);
                ImGui.InputText("decimal", ref s_inputBuf2, ImGuiInputTextFlags.CharsDecimal);
                ImGui.InputText("hexadecimal", ref s_inputBuf3, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.CharsUppercase);
                ImGui.InputText("uppercase", ref s_inputBuf4, ImGuiInputTextFlags.CharsUppercase);
                ImGui.InputText("no blank", ref s_inputBuf5, ImGuiInputTextFlags.CharsNoBlank);

                ImGui.InputText("casing swap", s_inputCbBufCasing, s_inputCbBufCasing.Length, ImGuiInputTextFlags.CallbackCharFilter, data =>
                {
                    if (data.EventChar >= 'a' && data.EventChar <= 'z') data.EventChar -= 'a' - 'A';
                    else if (data.EventChar >= 'A' && data.EventChar <= 'Z') data.EventChar += 'a' - 'A';
                    return 0;
                });

                ImGui.InputText("\"imgui\"", s_inputCbBufImGuiLetters, s_inputCbBufImGuiLetters.Length, ImGuiInputTextFlags.CallbackCharFilter, data =>
                {
                    if (data.EventChar < 256 && "imgui".IndexOf((char)data.EventChar) >= 0)
                        return 0;
                    return 1;
                });

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Password Input"))
            {
                ImGui.InputText("password", ref s_inputPassword, ImGuiInputTextFlags.Password);
                ImGui.SameLine(); HelpMarker("Display all characters as '*'.\nDisable clipboard cut and copy.\nDisable logging.\n");
                ImGui.InputTextWithHint("password (w/ hint)", "<password>", ref s_inputPassword, ImGuiInputTextFlags.Password);
                ImGui.InputText("password (clear)", ref s_inputPassword);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Completion, History, Edit Callbacks"))
            {
                ImGuiInputTextCallback callback = data =>
                {
                    if (data.EventFlag == ImGuiInputTextFlags.CallbackCompletion)
                    {
                        if (data.Buf != null && data.CursorPos + 2 < data.BufSize)
                        {
                            data.Buf[data.CursorPos] = (byte)'.';
                            data.Buf[data.CursorPos + 1] = (byte)'.';
                            data.BufTextLen = Math.Max(data.BufTextLen, data.CursorPos + 2);
                            data.CursorPos += 2;
                            data.Buf[data.BufTextLen] = 0;
                            data.BufDirty = true;
                        }
                    }
                    else if (data.EventFlag == ImGuiInputTextFlags.CallbackHistory)
                    {
                        if (data.Buf != null)
                        {
                            byte[] text = (data.EventKey == ImGuiKey.UpArrow)
                                ? Encoding.UTF8.GetBytes("Pressed Up!")
                                : Encoding.UTF8.GetBytes("Pressed Down!");
                            int copyLen = Math.Min(text.Length, data.BufSize - 1);
                            Array.Copy(text, data.Buf, copyLen);
                            data.BufTextLen = copyLen;
                            data.Buf[copyLen] = 0;
                            data.SelectAll();
                            data.BufDirty = true;
                        }
                    }
                    else if (data.EventFlag == ImGuiInputTextFlags.CallbackEdit)
                    {
                        if (data.Buf != null && data.BufTextLen > 0)
                        {
                            byte c = data.Buf[0];
                            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                            {
                                data.Buf[0] ^= 32;
                                data.BufDirty = true;
                            }
                        }
                        s_inputCbEditCount++;
                    }
                    return 0;
                };

                ImGui.InputText("Completion", s_inputCbBuf1, s_inputCbBuf1.Length, ImGuiInputTextFlags.CallbackCompletion, callback);
                ImGui.SameLine(); HelpMarker(
                    "Here we append \"..\" each time Tab is pressed. " +
                    "See 'Examples>Console' for a more meaningful demonstration of using this callback.");

                ImGui.InputText("History", s_inputCbBuf2, s_inputCbBuf2.Length, ImGuiInputTextFlags.CallbackHistory, callback);
                ImGui.SameLine(); HelpMarker(
                    "Here we replace and select text each time Up/Down are pressed. " +
                    "See 'Examples>Console' for a more meaningful demonstration of using this callback.");

                ImGui.InputText("Edit", s_inputCbBuf3, s_inputCbBuf3.Length, ImGuiInputTextFlags.CallbackEdit, callback);
                ImGui.SameLine(); HelpMarker(
                    "Here we toggle the casing of the first character on every edit + count edits.");
                ImGui.SameLine(); ImGui.Text($"({s_inputCbEditCount})");

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Resize Callback"))
            {
                HelpMarker(
                    "In Hikari.ImGui, ref string parameters dynamically grow and shrink automatically.\n" +
                    "You do not need manual ImVector buffer management.");
                CheckboxFlags("ImGuiInputTextFlags_WordWrap", ref s_inputResizeFlags, ImGuiInputTextFlags.WordWrap);
                ImGui.InputTextMultiline("##MyStr", ref s_inputResizeStr, new ImVec2(-float.Epsilon, ImGui.GetTextLineHeight() * 16), s_inputResizeFlags);
                ImGui.Text($"Length: {s_inputResizeStr.Length}");
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Eliding, Alignment"))
            {
                CheckboxFlags("ImGuiInputTextFlags_ElideLeft", ref s_inputElideFlags, ImGuiInputTextFlags.ElideLeft);
                ImGui.InputText("Path", ref s_inputElideBuf, s_inputElideFlags);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Miscellaneous"))
            {
                CheckboxFlags("ImGuiInputTextFlags_EscapeClearsAll", ref s_inputMiscFlags, ImGuiInputTextFlags.EscapeClearsAll);
                CheckboxFlags("ImGuiInputTextFlags_ReadOnly", ref s_inputMiscFlags, ImGuiInputTextFlags.ReadOnly);
                CheckboxFlags("ImGuiInputTextFlags_NoUndoRedo", ref s_inputMiscFlags, ImGuiInputTextFlags.NoUndoRedo);
                ImGui.InputText("Hello", ref s_inputMiscBuf, s_inputMiscFlags);
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsTooltips()

    /// <summary>
    /// Demonstrates basic, custom, delayed, stationary, and persistent tooltip windows.
    /// </summary>
    public static void DemoWindowWidgetsTooltips()
    {
        if (ImGui.TreeNode("Tooltips"))
        {
            ImGui.SeparatorText("General");

            HelpMarker(
                "Tooltip are typically created by using a IsItemHovered() + SetTooltip() sequence.\n\n" +
                "We provide a helper SetItemTooltip() function to perform the two with standards flags.");

            ImVec2 sz = new(-float.Epsilon, 0.0f);

            ImGui.Button("Basic", sz);
            ImGui.SetItemTooltip("I am a tooltip");

            ImGui.Button("Fancy", sz);
            if (ImGui.BeginItemTooltip())
            {
                ImGui.Text("I am a fancy tooltip");
                ImGui.PlotLines("Curve", s_tooltipCurveArr, s_tooltipCurveArr.Length);
                ImGui.Text($"Sin(time) = {MathF.Sin((float)ImGui.GetTime())}");
                ImGui.EndTooltip();
            }

            ImGui.SeparatorText("Always On");

            ImGui.RadioButton("Off", ref s_tooltipAlwaysOn, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Always On (Simple)", ref s_tooltipAlwaysOn, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Always On (Advanced)", ref s_tooltipAlwaysOn, 2);
            if (s_tooltipAlwaysOn == 1)
            {
                ImGui.SetTooltip("I am following you around.");
            }
            else if (s_tooltipAlwaysOn == 2 && ImGui.BeginTooltip())
            {
                ImGui.ProgressBar(MathF.Sin((float)ImGui.GetTime()) * 0.5f + 0.5f, new ImVec2(ImGui.GetFontSize() * 25, 0.0f));
                ImGui.EndTooltip();
            }

            ImGui.SeparatorText("Custom");

            HelpMarker(
                "Passing ImGuiHoveredFlags_ForTooltip to IsItemHovered() is the preferred way to standardize " +
                "tooltip activation details across your application. You may however decide to use custom " +
                "flags for a specific tooltip instance.");

            ImGui.Button("Manual", sz);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.ForTooltip))
                ImGui.SetTooltip("I am a manually emitted tooltip.");

            ImGui.Button("DelayNone", sz);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNone))
                ImGui.SetTooltip("I am a tooltip with no delay.");

            ImGui.Button("DelayShort", sz);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.NoSharedDelay))
                ImGui.SetTooltip($"I am a tooltip with a short delay ({ImGui.GetStyle().HoverDelayShort:0.00} sec).");

            ImGui.Button("DelayLong", sz);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal | ImGuiHoveredFlags.NoSharedDelay))
                ImGui.SetTooltip($"I am a tooltip with a long delay ({ImGui.GetStyle().HoverDelayNormal:0.00} sec).");

            ImGui.Button("Stationary", sz);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.Stationary))
                ImGui.SetTooltip("I am a tooltip requiring mouse to be stationary before activating.");

            ImGui.BeginDisabled();
            ImGui.Button("Disabled item", sz);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.ForTooltip))
                ImGui.SetTooltip("I am a tooltip for a disabled item.");
            ImGui.EndDisabled();

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsTreeNodes()

    /// <summary>
    /// Demonstrates hierarchical tree nodes, hierarchy lines, selectable node trees, leaves, and advanced styling flags.
    /// </summary>
    public static void DemoWindowWidgetsTreeNodes()
    {
        if (ImGui.TreeNode("Tree Nodes"))
        {
            if (ImGui.TreeNode("Basic Trees"))
            {
                for (int i = 0; i < 5; i++)
                {
                    if (i == 0)
                        ImGui.SetNextItemOpen(true, ImGuiCond.Once);

                    ImGui.PushID(i);
                    if (ImGui.TreeNode("", $"Child {i}"))
                    {
                        ImGui.Text("blah blah");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("button")) { }
                        ImGui.TreePop();
                    }
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Hierarchy Lines"))
            {
                HelpMarker("Default option for DrawLinesXXX is stored in style.TreeLinesFlags");
                CheckboxFlags("ImGuiTreeNodeFlags_DrawLinesNone", ref s_treeHierBaseFlags, ImGuiTreeNodeFlags.DrawLinesNone);
                CheckboxFlags("ImGuiTreeNodeFlags_DrawLinesFull", ref s_treeHierBaseFlags, ImGuiTreeNodeFlags.DrawLinesFull);
                CheckboxFlags("ImGuiTreeNodeFlags_DrawLinesToNodes", ref s_treeHierBaseFlags, ImGuiTreeNodeFlags.DrawLinesToNodes);

                if (ImGui.TreeNodeEx("Parent", s_treeHierBaseFlags))
                {
                    if (ImGui.TreeNodeEx("Child 1", s_treeHierBaseFlags))
                    {
                        ImGui.Button("Button for Child 1");
                        ImGui.TreePop();
                    }
                    if (ImGui.TreeNodeEx("Child 2", s_treeHierBaseFlags))
                    {
                        ImGui.Button("Button for Child 2");
                        ImGui.TreePop();
                    }
                    ImGui.Text("Remaining contents");
                    ImGui.Text("Remaining contents");
                    ImGui.TreePop();
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Clipping Large Trees"))
            {
                ImGui.TextWrapped(
                    "- Using ImGuiListClipper with trees is a less easy than on arrays or grids.\n" +
                    "- Refer to 'Demo->Examples->Property Editor' for an example of how to do that.\n" +
                    "- Discuss in #3823");
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Selectable Nodes"))
            {
                HelpMarker(
                    "Manually implemented selectable nodes.\n" +
                    "Click to select, Ctrl+Click to toggle, click on arrows or double-click to open.\n\n" +
                    "You may also use the multi-select API (see 'Demo->Widgets->Selection State & Multi-Select') for more advanced multi-selection features.");

                int nodeClickedIdx = -1;
                for (int nodeN = 0; nodeN < 6; nodeN++)
                {
                    ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
                    if ((s_treeSelectionMask & (1 << nodeN)) != 0)
                        flags |= ImGuiTreeNodeFlags.Selected;

                    bool isOpen = ImGui.TreeNodeEx((object)nodeN, flags, $"Selectable Node {nodeN}");
                    if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                        nodeClickedIdx = nodeN;
                    if (isOpen)
                    {
                        ImGui.BulletText("<Node contents here>");
                        ImGui.TreePop();
                    }
                }
                if (nodeClickedIdx != -1)
                {
                    if (ImGui.GetIO().KeyCtrl)
                        s_treeSelectionMask ^= (1 << nodeClickedIdx);
                    else
                        s_treeSelectionMask = (1 << nodeClickedIdx);
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Advanced"))
            {
                CheckboxFlags("ImGuiTreeNodeFlags_OpenOnArrow", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.OpenOnArrow);
                CheckboxFlags("ImGuiTreeNodeFlags_OpenOnDoubleClick", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.OpenOnDoubleClick);
                CheckboxFlags("ImGuiTreeNodeFlags_SpanAvailWidth", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.SpanAvailWidth); ImGui.SameLine(); HelpMarker("Extend hit area to all available width instead of allowing more items to be laid out after the node.");
                CheckboxFlags("ImGuiTreeNodeFlags_SpanFullWidth", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.SpanFullWidth);
                CheckboxFlags("ImGuiTreeNodeFlags_SpanLabelWidth", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.SpanLabelWidth); ImGui.SameLine(); HelpMarker("Reduce hit area to the text label and a bit of margin.");
                CheckboxFlags("ImGuiTreeNodeFlags_SpanAllColumns", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.SpanAllColumns); ImGui.SameLine(); HelpMarker("For use in Tables only.");
                CheckboxFlags("ImGuiTreeNodeFlags_AllowOverlap", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.AllowOverlap);
                CheckboxFlags("ImGuiTreeNodeFlags_Framed", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.Framed); ImGui.SameLine(); HelpMarker("Draw frame with background (e.g. for CollapsingHeader)");
                CheckboxFlags("ImGuiTreeNodeFlags_FramePadding", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.FramePadding);
                CheckboxFlags("ImGuiTreeNodeFlags_NavLeftJumpsToParent", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.NavLeftJumpsToParent);

                HelpMarker("Default option for DrawLinesXXX is stored in style.TreeLinesFlags");
                CheckboxFlags("ImGuiTreeNodeFlags_DrawLinesNone", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.DrawLinesNone);
                CheckboxFlags("ImGuiTreeNodeFlags_DrawLinesFull", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.DrawLinesFull);
                CheckboxFlags("ImGuiTreeNodeFlags_DrawLinesToNodes", ref s_treeAdvBaseFlags, ImGuiTreeNodeFlags.DrawLinesToNodes);

                ImGui.Checkbox("Align label with current X position", ref s_treeAdvAlignLabel);
                ImGui.Checkbox("Make Tree Nodes as drag & drop sources", ref s_treeAdvUseDragDrop);
                if (s_treeAdvAlignLabel)
                    ImGui.Unindent(ImGui.GetTreeNodeToLabelSpacing());

                for (int nodeN = 0; nodeN < 6; nodeN++)
                {
                    ImGuiTreeNodeFlags nodeFlags = s_treeAdvBaseFlags;
                    if (nodeN < 3)
                    {
                        bool isOpen = ImGui.TreeNodeEx((object)nodeN, nodeFlags, $"Selectable Node {nodeN}");
                        if (nodeN == 2 && (s_treeAdvBaseFlags & ImGuiTreeNodeFlags.SpanLabelWidth) != 0)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton("button")) { }
                        }
                        if (isOpen)
                        {
                            ImGui.BulletText("Blah blah\nBlah Blah");
                            ImGui.SameLine();
                            ImGui.SmallButton("Button");
                            ImGui.TreePop();
                        }
                    }
                    else
                    {
                        nodeFlags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
                        ImGui.TreeNodeEx((object)nodeN, nodeFlags, $"Selectable Leaf {nodeN}");
                    }
                }
                if (s_treeAdvAlignLabel)
                    ImGui.Indent(ImGui.GetTreeNodeToLabelSpacing());
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsVerticalSliders()

    /// <summary>
    /// Demonstrates vertical slider layouts across single sliders, colorful banks, and multi-row compact grids.
    /// </summary>
    public static void DemoWindowWidgetsVerticalSliders()
    {
        if (ImGui.TreeNode("Vertical Sliders"))
        {
            const float spacing = 4.0f;
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(spacing, spacing));

            ImGui.VSliderInt("##int", new ImVec2(18, 160), ref s_vsliderIntValue, 0, 5);
            ImGui.SameLine();

            ImGui.PushID("set1");
            for (int i = 0; i < 7; i++)
            {
                if (i > 0) ImGui.SameLine();
                ImGui.PushID(i);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, ImColor.HSV(i / 7.0f, 0.5f, 0.5f).Value);
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ImColor.HSV(i / 7.0f, 0.6f, 0.5f).Value);
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ImColor.HSV(i / 7.0f, 0.7f, 0.5f).Value);
                ImGui.PushStyleColor(ImGuiCol.SliderGrab, ImColor.HSV(i / 7.0f, 0.9f, 0.9f).Value);
                ImGui.VSliderFloat("##v", new ImVec2(18, 160), ref s_vsliderValues[i], 0.0f, 1.0f, "");
                if (ImGui.IsItemActive() || ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{s_vsliderValues[i]:F3}");
                ImGui.PopStyleColor(4);
                ImGui.PopID();
            }
            ImGui.PopID();

            ImGui.SameLine();
            ImGui.PushID("set2");
            const int rows = 3;
            ImVec2 smallSliderSize = new(18, (float)(int)((160.0f - (rows - 1) * spacing) / rows));
            for (int nx = 0; nx < 4; nx++)
            {
                if (nx > 0) ImGui.SameLine();
                ImGui.BeginGroup();
                for (int ny = 0; ny < rows; ny++)
                {
                    ImGui.PushID(nx * rows + ny);
                    ImGui.VSliderFloat("##v", smallSliderSize, ref s_vsliderValues2[nx], 0.0f, 1.0f, "");
                    if (ImGui.IsItemActive() || ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{s_vsliderValues2[nx]:F3}");
                    ImGui.PopID();
                }
                ImGui.EndGroup();
            }
            ImGui.PopID();

            ImGui.SameLine();
            ImGui.PushID("set3");
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) ImGui.SameLine();
                ImGui.PushID(i);
                ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 40.0f);
                ImGui.VSliderFloat("##v", new ImVec2(40, 160), ref s_vsliderValues[i], 0.0f, 1.0f, "%.2f\nsec");
                ImGui.PopStyleVar();
                ImGui.PopID();
            }
            ImGui.PopID();
            ImGui.PopStyleVar();
            ImGui.TreePop();
        }
    }

    #endregion

    #region [SECTION] DemoWindowWidgetsDisableBlocks()

    /// <summary>
    /// Demonstrates toggling the disabled state of every widget section above through a single checkbox.
    /// </summary>
    /// <param name="demoData">The shared demo data whose <see cref="ImGuiDemoWindowData.DisableSections"/>
    /// flag drives the global disabled scope in <see cref="DemoWindowWidgets(ImGuiDemoWindowData?)"/>.
    /// A <see langword="null"/> value suppresses the bound checkbox while keeping the explanatory
    /// tree node, because the flag has no storage to bind to.</param>
    /// <remarks>
    /// The checkbox is submitted outside the global disabled scope so that it stays interactive while the
    /// sections it controls are disabled; <see cref="DemoWindowWidgets(ImGuiDemoWindowData?)"/> temporarily
    /// ends and re-enters that scope around this submission.
    /// </remarks>
    public static void DemoWindowWidgetsDisableBlocks(ImGuiDemoWindowData? demoData)
    {
        if (!ImGui.TreeNode("Disable Blocks"))
            return;

        if (demoData != null)
            ImGui.Checkbox("Disable entire section above", ref demoData.DisableSections);
        ImGui.SameLine();
        HelpMarker("Demonstrate using BeginDisabled()/EndDisabled() across other sections.");

        ImGui.TreePop();
    }

    #endregion

    #region [SECTION] DemoWindowWidgets()

    /// <summary>
    /// Demonstrates all canonical Dear ImGui widget sections under the main <c>"Widgets"</c> collapsing header.
    /// </summary>
    public static void DemoWindowWidgets()
    {
        DemoWindowWidgets(s_demoData);
    }

    /// <summary>
    /// Demonstrates all canonical Dear ImGui widget sections under the main <c>"Widgets"</c> collapsing header with specific demo data.
    /// </summary>
    /// <param name="demoData">Demo configuration flags and settings.</param>
    public static void DemoWindowWidgets(ImGuiDemoWindowData? demoData)
    {
        if (!ImGui.CollapsingHeader("Widgets"))
            return;

        bool disableAll = demoData?.DisableSections ?? false;
        bool overrideLiveEdit = demoData?.LiveEditOverride ?? false;
        if (disableAll)
            ImGui.BeginDisabled();
        if (overrideLiveEdit && demoData != null)
        {
            ImGui.PushItemFlag(ImGuiItemFlags.LiveEditOnInputText, (demoData.LiveEditFlags & ImGuiItemFlags.LiveEditOnInputText) != 0);
            ImGui.PushItemFlag(ImGuiItemFlags.LiveEditOnInputScalar, (demoData.LiveEditFlags & ImGuiItemFlags.LiveEditOnInputScalar) != 0);
        }

        DemoWindowWidgetsBasic();
        DemoWindowWidgetsBullets();
        DemoWindowWidgetsCollapsingHeaders();
        DemoWindowWidgetsComboBoxes();
        DemoWindowWidgetsColorAndPickers();
        DemoWindowWidgetsDataTypes();

        // The "Disable Blocks" section carries the checkbox that controls the surrounding disabled scope,
        // so it must be submitted outside that scope.
        if (disableAll)
            ImGui.EndDisabled();
        DemoWindowWidgetsDisableBlocks(demoData);
        if (disableAll)
            ImGui.BeginDisabled();

        DemoWindowWidgetsDragAndDrop();
        DemoWindowWidgetsDragsAndSliders();
        DemoWindowWidgetsFonts();
        DemoWindowWidgetsImages();
        DemoWindowWidgetsListBoxes();
        DemoWindowWidgetsLiveEdit(demoData);
        DemoWindowWidgetsMixedValues();
        DemoWindowWidgetsMultiComponents();
        DemoWindowWidgetsPlotting();
        DemoWindowWidgetsProgressBars();
        DemoWindowWidgetsQueryingStatuses();
        DemoWindowWidgetsSelectables();
        DemoWindowWidgetsSelectionAndMultiSelect(demoData);
        DemoWindowWidgetsTabs();
        DemoWindowWidgetsText();
        DemoWindowWidgetsTextFilter();
        DemoWindowWidgetsTextInput();
        DemoWindowWidgetsTooltips();
        DemoWindowWidgetsTreeNodes();
        DemoWindowWidgetsVerticalSliders();

        if (overrideLiveEdit)
        {
            ImGui.PopItemFlag();
            ImGui.PopItemFlag();
        }
        if (disableAll)
            ImGui.EndDisabled();
    }

    #endregion

    #region [SECTION] Selection Demo Helpers

    /// <summary>
    /// Identifies which widget kind the advanced multi-select demonstration submits for each item.
    /// Ports the canonical <c>WidgetType</c> enum declared inside
    /// <c>DemoWindowWidgetsSelectionAndMultiSelect</c>.
    /// </summary>
    private enum SelectionWidgetType
    {
        /// <summary>Submits each item as a <c>Selectable()</c>.</summary>
        Selectable,

        /// <summary>Submits each item as a <c>TreeNodeEx()</c>.</summary>
        TreeNode,
    }

    /// <summary>
    /// Extends <see cref="ImGuiSelectionBasicStorage"/> with the deletion bookkeeping implemented by
    /// the canonical demo's <c>ExampleSelectionWithDeletion</c>
    /// </summary>
    /// <remarks>
    /// The helper cannot live in core because it needs the application's item list to choose the next
    /// focus target and to rewrite that list after deletion.
    /// </remarks>
    private sealed class ExampleSelectionWithDeletion : ImGuiSelectionBasicStorage
    {
        /// <summary>
        /// Chooses the item index that should receive keyboard focus after a pending deletion.
        /// </summary>
        /// <param name="msIo">The requests produced by <c>BeginMultiSelect()</c>.</param>
        /// <param name="itemsCount">The item count before deletion.</param>
        /// <returns>The index to focus, or <c>-1</c> when no index should be focused.</returns>
        public int ApplyDeletionPreLoop(ImGuiMultiSelectIO msIo, int itemsCount)
        {
            if (Size == 0)
                return -1;

            // If the focused item is not selected, recover the range source from the navigation id.
            int focusedIdx = (int)msIo.NavIdItem;
            if (!msIo.NavIdSelected)
            {
                msIo.RangeSrcReset = true;
                return focusedIdx;
            }

            // Otherwise land on the first unselected item after the focused item.
            for (int idx = focusedIdx + 1; idx < itemsCount; idx++)
                if (!Contains(GetStorageIdFromIndex(idx)))
                    return idx;

            // Failing that, land on the last unselected item before it.
            for (int idx = Math.Min(focusedIdx, itemsCount) - 1; idx >= 0; idx--)
                if (!Contains(GetStorageIdFromIndex(idx)))
                    return idx;

            return -1;
        }

        /// <summary>
        /// Deletes the selected items from the caller's list and moves the selection to the requested
        /// post-deletion index.
        /// </summary>
        /// <typeparam name="T">The caller's item element type.</typeparam>
        /// <param name="msIo">The requests produced by <c>EndMultiSelect()</c>.</param>
        /// <param name="items">The caller's mutable item list, rewritten in place.</param>
        /// <param name="itemCurrIdxToSelect">The pre-deletion index returned by <see cref="ApplyDeletionPreLoop"/>.</param>
        public void ApplyDeletionPostLoop<T>(ImGuiMultiSelectIO msIo, List<T> items, int itemCurrIdxToSelect)
        {
            // Rebuild the list without the selected entries, tracking where the focus target lands.
            List<T> newItems = new(items.Count - Size);
            int itemNextIdxToSelect = -1;
            for (int idx = 0; idx < items.Count; idx++)
            {
                if (!Contains(GetStorageIdFromIndex(idx)))
                    newItems.Add(items[idx]);
                if (itemCurrIdxToSelect == idx)
                    itemNextIdxToSelect = newItems.Count - 1;
            }
            items.Clear();
            items.AddRange(newItems);

            Clear();
            if (itemNextIdxToSelect != -1 && msIo.NavIdSelected)
                SetItemSelected(GetStorageIdFromIndex(itemNextIdxToSelect), true);
        }
    }

    /// <summary>
    /// A dual list box that moves persistent item identifiers between an "Available" and a "Basket"
    /// list, each with its own selection store. Ports canonical <c>ExampleDualListBox</c>
    /// </summary>
    private sealed class ExampleDualListBox
    {
        /// <summary>Gets the two ordered item lists; index 0 is "Available" and index 1 is "Basket".</summary>
        public List<uint>[] Items { get; } = new List<uint>[] { new(), new() };

        /// <summary>Gets the two selection stores, one per list.</summary>
        public ImGuiSelectionBasicStorage[] Selections { get; } = new ImGuiSelectionBasicStorage[] { new(), new() };

        /// <summary>
        /// Moves every item from one side to the other and transfers the selection accordingly.
        /// </summary>
        /// <param name="src">The source side index, 0 or 1.</param>
        /// <param name="dst">The destination side index, 0 or 1.</param>
        public void MoveAll(int src, int dst)
        {
            Items[dst].AddRange(Items[src]);
            Items[src].Clear();
            SortItems(dst);
            Selections[src].Swap(Selections[dst]);
            Selections[src].Clear();
        }

        /// <summary>
        /// Moves the selected items from one side to the other, preserving identifier order.
        /// </summary>
        /// <param name="src">The source side index, 0 or 1.</param>
        /// <param name="dst">The destination side index, 0 or 1.</param>
        public void MoveSelected(int src, int dst)
        {
            for (int srcN = 0; srcN < Items[src].Count; srcN++)
            {
                uint itemId = Items[src][srcN];
                if (!Selections[src].Contains(itemId))
                    continue;
                Items[src].RemoveAt(srcN);
                Items[dst].Add(itemId);
                srcN--;
            }
            SortItems(dst);
            Selections[src].Swap(Selections[dst]);
            Selections[src].Clear();
        }

        /// <summary>
        /// Applies the multi-select requests for one side, using that side's item identifiers as the
        /// adapter's storage identifiers.
        /// </summary>
        /// <param name="msIo">The requests produced by the multi-select API.</param>
        /// <param name="side">The side index whose store receives the requests.</param>
        public void ApplySelectionRequests(ImGuiMultiSelectIO msIo, int side)
        {
            Selections[side].UserData = Items[side];
            Selections[side].AdapterIndexToStorageId = static (self, idx) => ((List<uint>)self.UserData!)[idx];
            Selections[side].ApplyRequests(msIo);
        }

        /// <summary>
        /// Sorts one side's item identifiers in ascending order. The canonical <c>OptKeepSorted</c>
        /// toggle is left enabled by the demo, so this always runs.
        /// </summary>
        /// <param name="n">The side index to sort.</param>
        public void SortItems(int n)
        {
            Items[n].Sort();
        }

        /// <summary>
        /// Submits the three-column table: the available list, the transfer buttons, and the basket.
        /// </summary>
        public void Show()
        {
            if (ImGui.BeginTable("split", 3, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch); // Left side.
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);   // Buttons.
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch); // Right side.
                ImGui.TableNextRow();

                int requestMoveSelected = -1;
                int requestMoveAll = -1;
                float childHeight0 = 0.0f;
                for (int side = 0; side < 2; side++)
                {
                    List<uint> items = Items[side];
                    ImGuiSelectionBasicStorage selection = Selections[side];

                    ImGui.TableSetColumnIndex(side == 0 ? 0 : 2);
                    ImGui.Text($"{(side == 0 ? "Available" : "Basket")} ({items.Count})");

                    // Submitting the scrolling range avoids glitches while items move.
                    float itemsHeight = ImGui.GetTextLineHeightWithSpacing();
                    ImGui.SetNextWindowContentSize(new ImVec2(0.0f, items.Count * itemsHeight));

                    bool childVisible;
                    if (side == 0)
                    {
                        // The left child is resizable.
                        ImGui.SetNextWindowSizeConstraints(
                            new ImVec2(0.0f, ImGui.GetFrameHeightWithSpacing() * 4.0f),
                            new ImVec2(float.MaxValue, float.MaxValue));
                        childVisible = ImGui.BeginChild("0", new ImVec2(-float.Epsilon, ImGui.GetFontSize() * 20.0f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY);
                        childHeight0 = ImGui.GetWindowSize().Y;
                    }
                    else
                    {
                        // The right child reuses the left child's height.
                        childVisible = ImGui.BeginChild("1", new ImVec2(-float.Epsilon, childHeight0), ImGuiChildFlags.FrameStyle);
                    }
                    if (childVisible)
                    {
                        ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.BoxSelect1d;
                        ImGuiMultiSelectIO msIo = ImGui.BeginMultiSelect(flags, selection.Size, items.Count);
                        ApplySelectionRequests(msIo, side);

                        for (int itemN = 0; itemN < items.Count; itemN++)
                        {
                            uint itemId = items[itemN];
                            bool itemIsSelected = selection.Contains(itemId);
                            ImGui.SetNextItemSelectionUserData(itemN);
                            ImGui.Selectable(s_selectionExampleNames[itemId], itemIsSelected, ImGuiSelectableFlags.AllowDoubleClick);
                            if (ImGui.IsItemFocused())
                            {
                                if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
                                    requestMoveSelected = side;
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                                    requestMoveSelected = side;
                            }
                        }

                        msIo = ImGui.EndMultiSelect();
                        ApplySelectionRequests(msIo, side);
                    }
                    ImGui.EndChild();
                }

                // Buttons column.
                ImGui.TableSetColumnIndex(1);
                ImGui.NewLine();
                ImVec2 buttonSize = new(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
                if (ImGui.Button(">>", buttonSize))
                    requestMoveAll = 0;
                if (ImGui.Button(">", buttonSize))
                    requestMoveSelected = 0;
                if (ImGui.Button("<", buttonSize))
                    requestMoveSelected = 1;
                if (ImGui.Button("<<", buttonSize))
                    requestMoveAll = 1;

                if (requestMoveAll != -1)
                    MoveAll(requestMoveAll, requestMoveAll ^ 1);
                if (requestMoveSelected != -1)
                    MoveSelected(requestMoveSelected, requestMoveSelected ^ 1);

                ImGui.EndTable();
            }
        }
    }

    /// <summary>
    /// Toggles one multi-select flag. Canonical <c>ImGui::CheckboxFlags()</c> is a template that
    /// accepts any flag enum, while the port provides only <see cref="int"/> and <see cref="uint"/>
    /// overloads; this helper bridges the two so the demo keeps the strongly typed flag value.
    /// </summary>
    /// <param name="label">Checkbox label and identifier.</param>
    /// <param name="flags">The flag set to toggle in place.</param>
    /// <param name="flagsValue">The single flag to toggle.</param>
    /// <returns><see langword="true"/> when the checkbox changed the flag set.</returns>
    private static bool CheckboxFlagsMultiSelect(string label, ref ImGuiMultiSelectFlags flags, ImGuiMultiSelectFlags flagsValue)
    {
        int value = (int)flags;
        bool changed = ImGui.CheckboxFlags(label, ref value, (int)flagsValue);
        flags = (ImGuiMultiSelectFlags)value;
        return changed;
    }

    #endregion
}
