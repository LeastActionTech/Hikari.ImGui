// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using Hikari.ImGui;

namespace Hikari.ImGui.Demo;

/// <summary>
/// Data shared across different functions of the demo window.
/// </summary>
/// <summary>
/// State data configuring the visibility and options of the Dear ImGui demo window.
/// </summary>
public sealed class ImGuiDemoWindowData
{
    // Examples Apps (accessible from the "Examples" menu)
    /// <summary>Whether to show the main menu bar demo.</summary>
    public bool ShowMainMenuBar;
    /// <summary>Whether to show the assets browser example app.</summary>
    public bool ShowAppAssetsBrowser;
    /// <summary>Whether to show the console example app.</summary>
    public bool ShowAppConsole;
    /// <summary>Whether to show the custom rendering example app.</summary>
    public bool ShowAppCustomRendering;
    /// <summary>Whether to show the documents example app.</summary>
    public bool ShowAppDocuments;
    /// <summary>Whether to show the dock space example app.</summary>
    public bool ShowAppDockSpace;
    /// <summary>Whether to show the image viewer example app.</summary>
    public bool ShowAppImageViewer;
    /// <summary>Whether to show the log example app.</summary>
    public bool ShowAppLog;
    /// <summary>Whether to show the layout example app.</summary>
    public bool ShowAppLayout;
    /// <summary>Whether to show the property editor example app.</summary>
    public bool ShowAppPropertyEditor;
    /// <summary>Whether to show the simple overlay example app.</summary>
    public bool ShowAppSimpleOverlay;
    /// <summary>Whether to show the auto-resize example app.</summary>
    public bool ShowAppAutoResize;
    /// <summary>Whether to show the constrained resize example app.</summary>
    public bool ShowAppConstrainedResize;
    /// <summary>Whether to show the fullscreen example app.</summary>
    public bool ShowAppFullscreen;
    /// <summary>Whether to show the long text example app.</summary>
    public bool ShowAppLongText;
    /// <summary>Whether to show the window titles example app.</summary>
    public bool ShowAppWindowTitles;

    // Dear ImGui Tools (accessible from the "Tools" menu)
    /// <summary>Whether to show the metrics window.</summary>
    public bool ShowMetrics;
    /// <summary>Whether to show the debug log window.</summary>
    public bool ShowDebugLog;
    /// <summary>Whether to show the ID stack tool window.</summary>
    public bool ShowIDStackTool;
    /// <summary>Whether to show the style editor window.</summary>
    public bool ShowStyleEditor;
    /// <summary>Whether to show the about window.</summary>
    public bool ShowAbout;

    // Other data
    /// <summary>Whether to disable all interactive demo sections.</summary>
    public bool DisableSections;
    /// <summary>Whether live edit override is enabled.</summary>
    public bool LiveEditOverride;
    /// <summary>Flags governing live edit behavior.</summary>
    public ImGuiItemFlags LiveEditFlags = ImGuiItemFlags.LiveEditOnInputText;
}

/// <summary>
/// Showcase and demonstration of Hikari.ImGui features.
/// Ports canonical <c>ShowDemoWindow()</c> and supporting routines.
/// </summary>
public static partial class ImGuiDemo
{
    #region Static Window State

    // Stored demo data shared across sub-windows.
    private static readonly ImGuiDemoWindowData s_demoData = new();

    // Window options flags.
    private static bool s_noTitlebar = false;
    private static bool s_noScrollbar = false;
    private static bool s_noMenu = false;
    private static bool s_noMove = false;
    private static bool s_noResize = false;
    private static bool s_noCollapse = false;
    private static bool s_noClose = false;
    private static bool s_noNav = false;
    private static bool s_noBackground = false;
    private static bool s_noBringToFront = false;
    private static bool s_noDocking = false;
    private static bool s_unsavedDocument = false;

    #endregion

    #region Helper Widgets

    /// <summary>
    /// Helper to submit a checkbox toggling a specific <see cref="ImGuiConfigFlags"/> bit in <see cref="ImGuiIO.ConfigFlags"/>.
    /// </summary>
    private static void CheckboxConfigFlags(string label, ImGuiIO io, ImGuiConfigFlags flag)
    {
        int flags = (int)io.ConfigFlags;
        if (ImGui.CheckboxFlags(label, ref flags, (int)flag))
        {
            io.ConfigFlags = (ImGuiConfigFlags)flags;
        }
    }

    #endregion

    #region Menu Bar

    /// <summary>
    /// Renders the demo window menu bar.
    /// </summary>
    /// <param name="demoData">The shared demo window state.</param>
    public static void DemoWindowMenuBar(ImGuiDemoWindowData demoData)
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("Menu"))
            {
                ShowExampleMenuFile();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Examples"))
            {
                ImGui.MenuItem("Main menu bar", null, ref demoData.ShowMainMenuBar);

                ImGui.SeparatorText("Mini apps");
                ImGui.MenuItem("Assets Browser", null, ref demoData.ShowAppAssetsBrowser);
                ImGui.MenuItem("Console", null, ref demoData.ShowAppConsole);
                ImGui.MenuItem("Custom rendering", null, ref demoData.ShowAppCustomRendering);
                ImGui.MenuItem("Documents", null, ref demoData.ShowAppDocuments);
                ImGui.MenuItem("Dockspace", null, ref demoData.ShowAppDockSpace);
                ImGui.MenuItem("Image Viewer", null, ref demoData.ShowAppImageViewer);
                ImGui.MenuItem("Log", null, ref demoData.ShowAppLog);
                ImGui.MenuItem("Property editor", null, ref demoData.ShowAppPropertyEditor);
                ImGui.MenuItem("Simple layout", null, ref demoData.ShowAppLayout);
                ImGui.MenuItem("Simple overlay", null, ref demoData.ShowAppSimpleOverlay);

                ImGui.SeparatorText("Concepts");
                ImGui.MenuItem("Auto-resizing window", null, ref demoData.ShowAppAutoResize);
                ImGui.MenuItem("Constrained-resizing window", null, ref demoData.ShowAppConstrainedResize);
                ImGui.MenuItem("Fullscreen window", null, ref demoData.ShowAppFullscreen);
                ImGui.MenuItem("Long text display", null, ref demoData.ShowAppLongText);
                ImGui.MenuItem("Manipulating window titles", null, ref demoData.ShowAppWindowTitles);

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Tools"))
            {
                ImGuiIO io = ImGui.GetIO();
                const bool hasDebugTools = true;

                ImGui.MenuItem("Metrics/Debugger", null, ref demoData.ShowMetrics, hasDebugTools);
                if (ImGui.BeginMenu("Debug Options"))
                {
                    ImGui.BeginDisabled(!hasDebugTools);
                    bool highlightIdConflicts = io.ConfigDebugHighlightIdConflicts;
                    if (ImGui.Checkbox("Highlight ID Conflicts", ref highlightIdConflicts))
                        io.ConfigDebugHighlightIdConflicts = highlightIdConflicts;
                    ImGui.EndDisabled();

                    bool assertOnError = io.ConfigErrorRecoveryEnableAssert;
                    if (ImGui.Checkbox("Assert on error recovery", ref assertOnError))
                        io.ConfigErrorRecoveryEnableAssert = assertOnError;

                    ImGui.TextDisabled("(see Demo->Configuration for more)");
                    ImGui.EndMenu();
                }
                ImGui.MenuItem("Debug Log", null, ref demoData.ShowDebugLog, hasDebugTools);
                ImGui.MenuItem("ID Stack Tool", null, ref demoData.ShowIDStackTool, hasDebugTools);

                bool isDebuggerPresent = io.ConfigDebugIsDebuggerPresent;
                if (ImGui.MenuItem("Item Picker", null, false, hasDebugTools))
                {
                    ImGui.DebugStartItemPicker();
                }
                if (!isDebuggerPresent)
                {
                    ImGui.SetItemTooltip("Requires io.ConfigDebugIsDebuggerPresent=true to be set.\n\nWe otherwise disable some extra features to avoid casual users crashing the application.");
                }

                ImGui.MenuItem("Style Editor", null, ref demoData.ShowStyleEditor);
                ImGui.MenuItem("About Dear ImGui", null, ref demoData.ShowAbout);

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    #endregion

    #region Main Demo Window Entry Point

    /// <summary>
    /// Demonstrates most Hikari.ImGui features with a close button controlling <paramref name="pOpen"/>.
    /// </summary>
    /// <param name="pOpen">State indicating whether the window is open.</param>
    public static void ShowDemoWindow(ref bool pOpen)
    {
        ShowDemoWindowInternal(ref pOpen, hasOpen: true);
    }

    /// <summary>
    /// Demonstrates most Hikari.ImGui features unconditionally without an external close variable.
    /// </summary>
    public static void ShowDemoWindow()
    {
        bool dummy = true;
        ShowDemoWindowInternal(ref dummy, hasOpen: false);
    }

    private static void ShowDemoWindowInternal(ref bool pOpen, bool hasOpen)
    {
        // Examples Apps (accessible from the "Examples" menu, )
        // Canonical submits the docking and document apps first because their explicit DockSpace() nodes
        // must be created before the other example windows are submitted.
        if (s_demoData.ShowMainMenuBar)             ShowExampleAppMainMenuBar();
        if (s_demoData.ShowAppDockSpace)            ShowExampleAppDockSpace(ref s_demoData.ShowAppDockSpace);
        if (s_demoData.ShowAppDocuments)            ShowExampleAppDocuments(ref s_demoData.ShowAppDocuments);
        if (s_demoData.ShowAppAssetsBrowser)        ShowExampleAppAssetsBrowser(ref s_demoData.ShowAppAssetsBrowser);
        if (s_demoData.ShowAppConsole)              ShowExampleAppConsole(ref s_demoData.ShowAppConsole);
        if (s_demoData.ShowAppCustomRendering)      ShowExampleAppCustomRendering(ref s_demoData.ShowAppCustomRendering);
        if (s_demoData.ShowAppImageViewer)          ShowExampleAppImageViewer(ref s_demoData.ShowAppImageViewer);
        if (s_demoData.ShowAppLog)                  ShowExampleAppLog(ref s_demoData.ShowAppLog);
        if (s_demoData.ShowAppLayout)               ShowExampleAppLayout(ref s_demoData.ShowAppLayout);
        if (s_demoData.ShowAppPropertyEditor)       ShowExampleAppPropertyEditor(ref s_demoData.ShowAppPropertyEditor);
        if (s_demoData.ShowAppSimpleOverlay)        ShowExampleAppSimpleOverlay(ref s_demoData.ShowAppSimpleOverlay);
        if (s_demoData.ShowAppAutoResize)           ShowExampleAppAutoResize(ref s_demoData.ShowAppAutoResize);
        if (s_demoData.ShowAppConstrainedResize)    ShowExampleAppConstrainedResize(ref s_demoData.ShowAppConstrainedResize);
        if (s_demoData.ShowAppFullscreen)           ShowExampleAppFullscreen(ref s_demoData.ShowAppFullscreen);
        if (s_demoData.ShowAppLongText)             ShowExampleAppLongText(ref s_demoData.ShowAppLongText);
        if (s_demoData.ShowAppWindowTitles)         ShowExampleAppWindowTitles(ref s_demoData.ShowAppWindowTitles);

        // Dear ImGui Tools (accessible from the "Tools" menu, )
        if (s_demoData.ShowMetrics)
        {
            ImGui.ShowMetricsWindow(ref s_demoData.ShowMetrics);
        }
        if (s_demoData.ShowDebugLog)
        {
            ImGui.ShowDebugLogWindow(ref s_demoData.ShowDebugLog);
        }
        if (s_demoData.ShowIDStackTool)
        {
            ImGui.ShowIDStackToolWindow(ref s_demoData.ShowIDStackTool);
        }
        if (s_demoData.ShowStyleEditor)
        {
            ImGui.Begin("Dear ImGui Style Editor", ref s_demoData.ShowStyleEditor);
            ShowStyleEditor();
            ImGui.End();
        }
        if (s_demoData.ShowAbout)
        {
            ShowAboutWindow(ref s_demoData.ShowAbout);
        }

        // Demonstrate the various window flags.
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None;
        if (s_noTitlebar)       windowFlags |= ImGuiWindowFlags.NoTitleBar;
        if (s_noScrollbar)      windowFlags |= ImGuiWindowFlags.NoScrollbar;
        if (!s_noMenu)          windowFlags |= ImGuiWindowFlags.MenuBar;
        if (s_noMove)           windowFlags |= ImGuiWindowFlags.NoMove;
        if (s_noResize)         windowFlags |= ImGuiWindowFlags.NoResize;
        if (s_noCollapse)       windowFlags |= ImGuiWindowFlags.NoCollapse;
        if (s_noNav)            windowFlags |= ImGuiWindowFlags.NoNav;
        if (s_noBackground)     windowFlags |= ImGuiWindowFlags.NoBackground;
        if (s_noBringToFront)   windowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (s_noDocking)        windowFlags |= ImGuiWindowFlags.NoDocking;
        if (s_unsavedDocument)  windowFlags |= ImGuiWindowFlags.UnsavedDocument;

        bool passOpen = hasOpen && !s_noClose;

        // Default window position and size if no.ini data exists.
        ImGuiViewport mainViewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new ImVec2(mainViewport.WorkPos.X + 650, mainViewport.WorkPos.Y + 20), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new ImVec2(550, 680), ImGuiCond.FirstUseEver);

        // Main body of the Demo window starts here.
        bool windowOpen;
        if (passOpen)
        {
            windowOpen = ImGui.Begin("Dear ImGui Demo", ref pOpen, windowFlags);
        }
        else
        {
            windowOpen = ImGui.Begin("Dear ImGui Demo", windowFlags);
        }

        if (!windowOpen)
        {
            // Early out if window is collapsed as an optimization
            ImGui.End();
            return;
        }

        // Shared item width configuration.
        float labelWidthBase = ImGui.GetFontSize() * 12.0f;
        float labelWidthMax = ImGui.GetContentRegionAvail().X * 0.40f;
        float labelWidth = MathF.Min(labelWidthBase, labelWidthMax);
        ImGui.PushItemWidth(-labelWidth);

        // Menu Bar.
        DemoWindowMenuBar(s_demoData);

        ImGui.Text($"dear imgui says hello! ({ImGui.GetVersion()}) ({ImGui.ImGuiVersionNum})");
        ImGui.Spacing();

        // [SECTION] Help.
        if (ImGui.CollapsingHeader("Help"))
        {
            ImGui.SeparatorText("ABOUT THIS DEMO:");
            ImGui.BulletText("Sections below are demonstrating many aspects of the library.");
            ImGui.BulletText("The \"Examples\" menu above leads to more demo contents.");
            ImGui.BulletText("The \"Tools\" menu above gives access to: About Box, Style Editor,\nand Metrics/Debugger (general purpose Dear ImGui debugging tool).");
            ImGui.BulletText("Web demo (w/ source code browser): ");
            ImGui.SameLine(0, 0);
            ImGui.TextLinkOpenURL("https://pthom.github.io/imgui_explorer");

            ImGui.SeparatorText("PROGRAMMER GUIDE:");
            ImGui.BulletText("See the ShowDemoWindow() code in imgui_demo.cpp. <- you are here!");
            ImGui.BulletText("See comments in imgui.cpp.");
            ImGui.BulletText("See example applications in the examples/ folder.");
            ImGui.BulletText("Read the FAQ at ");
            ImGui.SameLine(0, 0);
            ImGui.TextLinkOpenURL("https://www.dearimgui.com/faq/");
            ImGui.BulletText("Set 'io.ConfigFlags |= NavEnableKeyboard' for keyboard controls.");
            ImGui.BulletText("Set 'io.ConfigFlags |= NavEnableGamepad' for gamepad controls.");

            ImGui.SeparatorText("USER GUIDE:");
            ShowUserGuide();
        }

        // [SECTION] Configuration.
        if (ImGui.CollapsingHeader("Configuration"))
        {
            ImGuiIO io = ImGui.GetIO();

            if (ImGui.TreeNode("Configuration##2"))
            {
                ImGui.SeparatorText("General");
                CheckboxConfigFlags("io.ConfigFlags: NavEnableKeyboard", io, ImGuiConfigFlags.NavEnableKeyboard);
                ImGui.SameLine(); HelpMarker("Enable keyboard controls.");

                CheckboxConfigFlags("io.ConfigFlags: NavEnableGamepad", io, ImGuiConfigFlags.NavEnableGamepad);
                ImGui.SameLine(); HelpMarker("Enable gamepad controls. Require backend to set io.BackendFlags |= ImGuiBackendFlags_HasGamepad.\n\nRead instructions in imgui.cpp for details.");

                CheckboxConfigFlags("io.ConfigFlags: NoMouse", io, ImGuiConfigFlags.NoMouse);
                ImGui.SameLine(); HelpMarker("Instruct dear imgui to disable mouse inputs and interactions.");

                // The "NoMouse" option can get us stuck with a disabled mouse! Let's provide an alternative way to fix it:
                if ((io.ConfigFlags & ImGuiConfigFlags.NoMouse) != 0)
                {
                    if ((float)(ImGui.GetTime() % 0.40) < 0.20f)
                    {
                        ImGui.SameLine();
                        ImGui.Text("<<PRESS SPACE TO DISABLE>>");
                    }
                    if (ImGui.IsKeyPressed(ImGuiKey.Space) || (io.ConfigFlags & ImGuiConfigFlags.NoKeyboard) != 0)
                    {
                        io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
                    }
                }

                CheckboxConfigFlags("io.ConfigFlags: NoMouseCursorChange", io, ImGuiConfigFlags.NoMouseCursorChange);
                ImGui.SameLine(); HelpMarker("Instruct backend to not alter mouse cursor shape and visibility.");

                CheckboxConfigFlags("io.ConfigFlags: NoKeyboard", io, ImGuiConfigFlags.NoKeyboard);
                ImGui.SameLine(); HelpMarker("Instruct dear imgui to disable keyboard inputs and interactions.");

                bool trickle = io.ConfigInputTrickleEventQueue;
                if (ImGui.Checkbox("io.ConfigInputTrickleEventQueue", ref trickle))
                    io.ConfigInputTrickleEventQueue = trickle;
                ImGui.SameLine(); HelpMarker("Enable input queue trickling: some types of events submitted during the same frame (e.g. button down + up) will be spread over multiple frames, improving interactions with low framerates.");

                bool mouseDrawCursor = io.MouseDrawCursor;
                if (ImGui.Checkbox("io.MouseDrawCursor", ref mouseDrawCursor))
                    io.MouseDrawCursor = mouseDrawCursor;
                ImGui.SameLine(); HelpMarker("Instruct Dear ImGui to render a mouse cursor itself. Note that a mouse cursor rendered via your application GPU rendering path will feel more laggy than hardware cursor, but will be more in sync with your other visuals.\n\nSome desktop applications may use both kinds of cursors (e.g. enable software cursor only when resizing/dragging something).");

                ImGui.SeparatorText("Keyboard/Gamepad Navigation");
                bool swapGamepad = io.ConfigNavSwapGamepadButtons;
                if (ImGui.Checkbox("io.ConfigNavSwapGamepadButtons", ref swapGamepad))
                    io.ConfigNavSwapGamepadButtons = swapGamepad;

                bool moveSetMouse = io.ConfigNavMoveSetMousePos;
                if (ImGui.Checkbox("io.ConfigNavMoveSetMousePos", ref moveSetMouse))
                    io.ConfigNavMoveSetMousePos = moveSetMouse;
                ImGui.SameLine(); HelpMarker("Directional/tabbing navigation teleports the mouse cursor. May be useful on TV/console systems where moving a virtual mouse is difficult");

                bool captureKeyboard = io.ConfigNavCaptureKeyboard;
                if (ImGui.Checkbox("io.ConfigNavCaptureKeyboard", ref captureKeyboard))
                    io.ConfigNavCaptureKeyboard = captureKeyboard;

                bool escapeClearItem = io.ConfigNavEscapeClearFocusItem;
                if (ImGui.Checkbox("io.ConfigNavEscapeClearFocusItem", ref escapeClearItem))
                    io.ConfigNavEscapeClearFocusItem = escapeClearItem;
                ImGui.SameLine(); HelpMarker("Pressing Escape clears focused item.");

                bool escapeClearWindow = io.ConfigNavEscapeClearFocusWindow;
                if (ImGui.Checkbox("io.ConfigNavEscapeClearFocusWindow", ref escapeClearWindow))
                    io.ConfigNavEscapeClearFocusWindow = escapeClearWindow;
                ImGui.SameLine(); HelpMarker("Pressing Escape clears focused window.");

                bool cursorVisibleAuto = io.ConfigNavCursorVisibleAuto;
                if (ImGui.Checkbox("io.ConfigNavCursorVisibleAuto", ref cursorVisibleAuto))
                    io.ConfigNavCursorVisibleAuto = cursorVisibleAuto;
                ImGui.SameLine(); HelpMarker("Using directional navigation key makes the cursor visible. Mouse click hides the cursor.");

                bool cursorVisibleAlways = io.ConfigNavCursorVisibleAlways;
                if (ImGui.Checkbox("io.ConfigNavCursorVisibleAlways", ref cursorVisibleAlways))
                    io.ConfigNavCursorVisibleAlways = cursorVisibleAlways;
                ImGui.SameLine(); HelpMarker("Navigation cursor is always visible.");

                ImGui.SeparatorText("Docking");
                CheckboxConfigFlags("io.ConfigFlags: DockingEnable", io, ImGuiConfigFlags.DockingEnable);
                ImGui.SameLine();
                if (io.ConfigDockingWithShift)
                    HelpMarker("Drag from window title bar or their tab to dock/undock. Hold SHIFT to enable docking.\n\nDrag from window menu button (upper-left button) to undock an entire node (all windows).");
                else
                    HelpMarker("Drag from window title bar or their tab to dock/undock. Hold SHIFT to disable docking.\n\nDrag from window menu button (upper-left button) to undock an entire node (all windows).");

                if ((io.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0)
                {
                    ImGui.Indent();
                    bool dockingNoSplit = io.ConfigDockingNoSplit;
                    if (ImGui.Checkbox("io.ConfigDockingNoSplit", ref dockingNoSplit))
                        io.ConfigDockingNoSplit = dockingNoSplit;
                    ImGui.SameLine(); HelpMarker("Simplified docking mode: disable window splitting, so docking is limited to merging multiple windows together into tab-bars.");

                    bool dockingNoOver = io.ConfigDockingNoDockingOver;
                    if (ImGui.Checkbox("io.ConfigDockingNoDockingOver", ref dockingNoOver))
                        io.ConfigDockingNoDockingOver = dockingNoOver;
                    ImGui.SameLine(); HelpMarker("Simplified docking mode: disable window merging into a same tab-bar, so docking is limited to splitting windows.");

                    bool dockingWithShift = io.ConfigDockingWithShift;
                    if (ImGui.Checkbox("io.ConfigDockingWithShift", ref dockingWithShift))
                        io.ConfigDockingWithShift = dockingWithShift;
                    ImGui.SameLine(); HelpMarker("Enable docking when holding Shift only (allow to drop in wider space, reduce visual noise)");

                    bool dockingTabBar = io.ConfigDockingAlwaysTabBar;
                    if (ImGui.Checkbox("io.ConfigDockingAlwaysTabBar", ref dockingTabBar))
                        io.ConfigDockingAlwaysTabBar = dockingTabBar;
                    ImGui.SameLine(); HelpMarker("Create a docking node and tab-bar on single floating windows.");

                    bool dockingPayload = io.ConfigDockingTransparentPayload;
                    if (ImGui.Checkbox("io.ConfigDockingTransparentPayload", ref dockingPayload))
                        io.ConfigDockingTransparentPayload = dockingPayload;
                    ImGui.SameLine(); HelpMarker("Make window or viewport transparent when docking and only display docking boxes on the target viewport. Useful if rendering of multiple viewport cannot be synced. Best used with ConfigViewportsNoAutoMerge.");
                    ImGui.Unindent();
                }

                ImGui.SeparatorText("Multi-viewports");
                CheckboxConfigFlags("io.ConfigFlags: ViewportsEnable", io, ImGuiConfigFlags.ViewportsEnable);
                ImGui.SameLine(); HelpMarker("[beta] Enable beta multi-viewports support. See ImGuiPlatformIO for details.");

                if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
                {
                    ImGui.Indent();
                    bool vpNoAutoMerge = io.ConfigViewportsNoAutoMerge;
                    if (ImGui.Checkbox("io.ConfigViewportsNoAutoMerge", ref vpNoAutoMerge))
                        io.ConfigViewportsNoAutoMerge = vpNoAutoMerge;
                    ImGui.SameLine(); HelpMarker("Set to make all floating imgui windows always create their own viewport. Otherwise, they are merged into the main host viewports when overlapping it.");

                    bool vpNoTaskBar = io.ConfigViewportsNoTaskBarIcon;
                    if (ImGui.Checkbox("io.ConfigViewportsNoTaskBarIcon", ref vpNoTaskBar))
                        io.ConfigViewportsNoTaskBarIcon = vpNoTaskBar;
                    ImGui.SameLine(); HelpMarker("(note: some platform backends may not reflect a change of this value for existing viewports, and may need the viewport to be recreated)");

                    bool vpNoDeco = io.ConfigViewportsNoDecoration;
                    if (ImGui.Checkbox("io.ConfigViewportsNoDecoration", ref vpNoDeco))
                        io.ConfigViewportsNoDecoration = vpNoDeco;
                    ImGui.SameLine(); HelpMarker("(note: some platform backends may not reflect a change of this value for existing viewports, and may need the viewport to be recreated)");

                    bool vpNoDefaultParent = io.ConfigViewportsNoDefaultParent;
                    if (ImGui.Checkbox("io.ConfigViewportsNoDefaultParent", ref vpNoDefaultParent))
                        io.ConfigViewportsNoDefaultParent = vpNoDefaultParent;
                    ImGui.SameLine(); HelpMarker("(note: some platform backends may not reflect a change of this value for existing viewports, and may need the viewport to be recreated)");

                    bool vpPlatformFocus = io.ConfigViewportsPlatformFocusSetsImGuiFocus;
                    if (ImGui.Checkbox("io.ConfigViewportsPlatformFocusSetsImGuiFocus", ref vpPlatformFocus))
                        io.ConfigViewportsPlatformFocusSetsImGuiFocus = vpPlatformFocus;
                    ImGui.SameLine(); HelpMarker("When a platform window is focused (e.g. using Alt+Tab, clicking Platform Title Bar), apply corresponding focus on imgui windows (may clear focus/active id from imgui windows location in other platform windows). In principle this is better enabled but we provide an opt-out, because some Linux window managers tend to eagerly focus windows (e.g. on mouse hover, or even a simple window pos/size change).");
                    ImGui.Unindent();
                }

                // Disabled in canonical 
                //ImGui.SeparatorText("DPI/Scaling");
                //bool dpiScaleFonts = io.ConfigDpiScaleFonts;
                //ImGui.Checkbox("io.ConfigDpiScaleFonts", ref dpiScaleFonts);
                //ImGui.SameLine(); HelpMarker("Experimental: Automatically update style.FontScaleDpi when Monitor DPI changes. This will scale fonts but NOT style sizes/padding for now.");
                //bool dpiScaleViewports = io.ConfigDpiScaleViewports;
                //ImGui.Checkbox("io.ConfigDpiScaleViewports", ref dpiScaleViewports);
                //ImGui.SameLine(); HelpMarker("Experimental: Scale Dear ImGui and Platform Windows when Monitor DPI changes.");

                ImGui.SeparatorText("Windows");
                bool winResizeEdges = io.ConfigWindowsResizeFromEdges;
                if (ImGui.Checkbox("io.ConfigWindowsResizeFromEdges", ref winResizeEdges))
                    io.ConfigWindowsResizeFromEdges = winResizeEdges;
                ImGui.SameLine(); HelpMarker("Enable resizing of windows from their edges and from the lower-left corner.\nThis requires ImGuiBackendFlags_HasMouseCursors for better mouse cursor feedback.");

                bool winMoveTitle = io.ConfigWindowsMoveFromTitleBarOnly;
                if (ImGui.Checkbox("io.ConfigWindowsMoveFromTitleBarOnly", ref winMoveTitle))
                    io.ConfigWindowsMoveFromTitleBarOnly = winMoveTitle;

                bool winCopyCtrlC = io.ConfigWindowsCopyContentsWithCtrlC;
                if (ImGui.Checkbox("io.ConfigWindowsCopyContentsWithCtrlC", ref winCopyCtrlC)) // [EXPERIMENTAL]
                    io.ConfigWindowsCopyContentsWithCtrlC = winCopyCtrlC;
                ImGui.SameLine(); HelpMarker("*EXPERIMENTAL* Ctrl+C copy the contents of focused window into the clipboard.\n\nExperimental because:\n- (1) has known issues with nested Begin/End pairs.\n- (2) text output quality varies.\n- (3) text output is in submission order rather than spatial order.");

                bool scrollByPage = io.ConfigScrollbarScrollByPage;
                if (ImGui.Checkbox("io.ConfigScrollbarScrollByPage", ref scrollByPage))
                    io.ConfigScrollbarScrollByPage = scrollByPage;
                ImGui.SameLine(); HelpMarker("Enable scrolling page by page when clicking outside the scrollbar grab.\nWhen disabled, always scroll to clicked location.\nWhen enabled, Shift+Click scrolls to clicked location.");

                ImGui.SeparatorText("Widgets");
                bool cursorBlink = io.ConfigInputTextCursorBlink;
                if (ImGui.Checkbox("io.ConfigInputTextCursorBlink", ref cursorBlink))
                    io.ConfigInputTextCursorBlink = cursorBlink;
                ImGui.SameLine(); HelpMarker("Enable blinking cursor (optional as some users consider it to be distracting).");

                bool enterKeepActive = io.ConfigInputTextEnterKeepActive;
                if (ImGui.Checkbox("io.ConfigInputTextEnterKeepActive", ref enterKeepActive))
                    io.ConfigInputTextEnterKeepActive = enterKeepActive;
                ImGui.SameLine(); HelpMarker("Pressing Enter will reactivate item and select all text (single-line only).");

                bool dragClick = io.ConfigDragClickToInputText;
                if (ImGui.Checkbox("io.ConfigDragClickToInputText", ref dragClick))
                    io.ConfigDragClickToInputText = dragClick;
                ImGui.SameLine(); HelpMarker("Enable turning DragXXX widgets into text input with a simple mouse click-release (without moving).");

                bool macOsx = io.ConfigMacOSXBehaviors;
                if (ImGui.Checkbox("io.ConfigMacOSXBehaviors", ref macOsx))
                    io.ConfigMacOSXBehaviors = macOsx;
                ImGui.SameLine(); HelpMarker("Swap Cmd<>Ctrl keys, enable various MacOS style behaviors.");

                ImGui.Text("Also see Style->Rendering for rendering options.");

                ImGui.SeparatorText("Settings");
                bool saveLastUsed = io.ConfigIniSettingsSaveLastUsedDate;
                if (ImGui.Checkbox("io.ConfigIniSettingsSaveLastUsedDate", ref saveLastUsed))
                    io.ConfigIniSettingsSaveLastUsedDate = saveLastUsed;

                // Also read: https://github.com/ocornut/imgui/wiki/Error-Handling
                ImGui.SeparatorText("Error Handling");
                bool errorRecovery = io.ConfigErrorRecovery;
                if (ImGui.Checkbox("io.ConfigErrorRecovery", ref errorRecovery))
                    io.ConfigErrorRecovery = errorRecovery;
                ImGui.SameLine(); HelpMarker(
                    "Options to configure how we handle recoverable errors.\n" +
                    "- Error recovery is not perfect nor guaranteed! It is a feature to ease development.\n" +
                    "- You not are not supposed to rely on it in the course of a normal application run.\n" +
                    "- Possible usage: facilitate recovery from errors triggered from a scripting language or after specific exceptions handlers.\n" +
                    "- Always ensure that on programmers seat you have at minimum Asserts or Tooltips enabled when making direct imgui API call! " +
                    "Otherwise it would severely hinder your ability to catch and correct mistakes!");

                bool assertOnErr = io.ConfigErrorRecoveryEnableAssert;
                if (ImGui.Checkbox("io.ConfigErrorRecoveryEnableAssert", ref assertOnErr))
                    io.ConfigErrorRecoveryEnableAssert = assertOnErr;

                bool debugLogOnErr = io.ConfigErrorRecoveryEnableDebugLog;
                if (ImGui.Checkbox("io.ConfigErrorRecoveryEnableDebugLog", ref debugLogOnErr))
                    io.ConfigErrorRecoveryEnableDebugLog = debugLogOnErr;

                bool tooltipOnErr = io.ConfigErrorRecoveryEnableTooltip;
                if (ImGui.Checkbox("io.ConfigErrorRecoveryEnableTooltip", ref tooltipOnErr))
                    io.ConfigErrorRecoveryEnableTooltip = tooltipOnErr;

                if (!io.ConfigErrorRecoveryEnableAssert && !io.ConfigErrorRecoveryEnableDebugLog && !io.ConfigErrorRecoveryEnableTooltip)
                    io.ConfigErrorRecoveryEnableAssert = io.ConfigErrorRecoveryEnableDebugLog = io.ConfigErrorRecoveryEnableTooltip = true;

                // Also read: https://github.com/ocornut/imgui/wiki/Debug-Tools
                ImGui.SeparatorText("Debug");
                bool isDebuggerPresent = io.ConfigDebugIsDebuggerPresent;
                if (ImGui.Checkbox("io.ConfigDebugIsDebuggerPresent", ref isDebuggerPresent))
                    io.ConfigDebugIsDebuggerPresent = isDebuggerPresent;
                ImGui.SameLine(); HelpMarker("Enable various tools calling IM_DEBUG_BREAK().\n\nRequires a debugger being attached, otherwise IM_DEBUG_BREAK() options will appear to crash your application.");

                bool idConflicts = io.ConfigDebugHighlightIdConflicts;
                if (ImGui.Checkbox("io.ConfigDebugHighlightIdConflicts", ref idConflicts))
                    io.ConfigDebugHighlightIdConflicts = idConflicts;
                ImGui.SameLine(); HelpMarker("Highlight and show an error message when multiple items have conflicting identifiers.");

                ImGui.BeginDisabled();
                bool retValOnce = io.ConfigDebugBeginReturnValueOnce;
                ImGui.Checkbox("io.ConfigDebugBeginReturnValueOnce", ref retValOnce);
                ImGui.EndDisabled();
                ImGui.SameLine(); HelpMarker("First calls to Begin()/BeginChild() will return false.\n\nTHIS OPTION IS DISABLED because it needs to be set at application boot-time to make sense. Showing the disabled option is a way to make this feature easier to discover.");

                bool retValLoop = io.ConfigDebugBeginReturnValueLoop;
                if (ImGui.Checkbox("io.ConfigDebugBeginReturnValueLoop", ref retValLoop))
                    io.ConfigDebugBeginReturnValueLoop = retValLoop;
                ImGui.SameLine(); HelpMarker("Some calls to Begin()/BeginChild() will return false.\n\nWill cycle through window depths then repeat. Windows should be flickering while running.");

                bool ignoreFocusLoss = io.ConfigDebugIgnoreFocusLoss;
                if (ImGui.Checkbox("io.ConfigDebugIgnoreFocusLoss", ref ignoreFocusLoss))
                    io.ConfigDebugIgnoreFocusLoss = ignoreFocusLoss;
                ImGui.SameLine(); HelpMarker("Option to deactivate io.AddFocusEvent(false) handling. May facilitate interactions with a debugger when focus loss leads to clearing inputs data.");

                bool debugIni = io.ConfigDebugIniSettings;
                if (ImGui.Checkbox("io.ConfigDebugIniSettings", ref debugIni))
                    io.ConfigDebugIniSettings = debugIni;
                ImGui.SameLine(); HelpMarker("Option to save .ini data with extra comments (particularly helpful for Docking, but makes saving slower).");

                ImGui.TreePop();
                ImGui.Spacing();
            }

            if (ImGui.TreeNode("Backend Flags"))
            {
                HelpMarker(
                    "Those flags are set by the backends (imgui_impl_xxx files) to specify their capabilities.\n" +
                    "Here we expose them as read-only fields to avoid breaking interactions with your backend.");

                // Make a local copy to avoid modifying actual backend flags.
                ImGui.BeginDisabled();
                int backendFlags = (int)io.BackendFlags;
                ImGui.CheckboxFlags("io.BackendFlags: HasGamepad", ref backendFlags, (int)ImGuiBackendFlags.HasGamepad);
                ImGui.CheckboxFlags("io.BackendFlags: HasMouseCursors", ref backendFlags, (int)ImGuiBackendFlags.HasMouseCursors);
                ImGui.CheckboxFlags("io.BackendFlags: HasSetMousePos", ref backendFlags, (int)ImGuiBackendFlags.HasSetMousePos);
                ImGui.CheckboxFlags("io.BackendFlags: PlatformHasViewports", ref backendFlags, (int)ImGuiBackendFlags.PlatformHasViewports);
                ImGui.CheckboxFlags("io.BackendFlags: HasMouseHoveredViewport", ref backendFlags, (int)ImGuiBackendFlags.HasMouseHoveredViewport);
                ImGui.CheckboxFlags("io.BackendFlags: HasParentViewport", ref backendFlags, (int)ImGuiBackendFlags.HasParentViewport);
                ImGui.CheckboxFlags("io.BackendFlags: RendererHasVtxOffset", ref backendFlags, (int)ImGuiBackendFlags.RendererHasVtxOffset);
                ImGui.CheckboxFlags("io.BackendFlags: RendererHasTextures", ref backendFlags, (int)ImGuiBackendFlags.RendererHasTextures);
                ImGui.CheckboxFlags("io.BackendFlags: RendererHasViewports", ref backendFlags, (int)ImGuiBackendFlags.RendererHasViewports);
                ImGui.EndDisabled();

                ImGui.TreePop();
                ImGui.Spacing();
            }

            if (ImGui.TreeNode("Style, Fonts"))
            {
                ImGui.Checkbox("Style Editor", ref s_demoData.ShowStyleEditor);
                ImGui.SameLine();
                HelpMarker("The same contents can be accessed in 'Tools->Style Editor' or by calling the ShowStyleEditor() function.");
                ImGui.TreePop();
                ImGui.Spacing();
            }

            if (ImGui.TreeNode("Capture/Logging"))
            {
                HelpMarker(
                    "The logging API redirects all text output so you can easily capture the content of " +
                    "a window or a block. Tree nodes can be automatically expanded.\n" +
                    "Try opening any of the contents below in this window and then click one of the \"Log To\" button.");
                ImGui.LogButtons();

                HelpMarker("You can also call ImGui::LogText() to output directly to the log without a visual output.");
                if (ImGui.Button("Copy \"Hello, world!\" to clipboard"))
                {
                    ImGui.LogToClipboard();
                    ImGui.LogText("Hello, world!");
                    ImGui.LogFinish();
                }
                ImGui.TreePop();
            }
        }

        // [SECTION] Window options.
        if (ImGui.CollapsingHeader("Window options"))
        {
            if (ImGui.BeginTable("split", 3))
            {
                ImGui.TableNextColumn(); ImGui.Checkbox("No titlebar", ref s_noTitlebar);
                ImGui.TableNextColumn(); ImGui.Checkbox("No scrollbar", ref s_noScrollbar);
                ImGui.TableNextColumn(); ImGui.Checkbox("No menu", ref s_noMenu);
                ImGui.TableNextColumn(); ImGui.Checkbox("No move", ref s_noMove);
                ImGui.TableNextColumn(); ImGui.Checkbox("No resize", ref s_noResize);
                ImGui.TableNextColumn(); ImGui.Checkbox("No collapse", ref s_noCollapse);
                ImGui.TableNextColumn(); ImGui.Checkbox("No close", ref s_noClose);
                ImGui.TableNextColumn(); ImGui.Checkbox("No nav", ref s_noNav);
                ImGui.TableNextColumn(); ImGui.Checkbox("No background", ref s_noBackground);
                ImGui.TableNextColumn(); ImGui.Checkbox("No bring to front", ref s_noBringToFront);
                ImGui.TableNextColumn(); ImGui.Checkbox("No docking", ref s_noDocking);
                ImGui.TableNextColumn(); ImGui.Checkbox("Unsaved document", ref s_unsavedDocument);
                ImGui.EndTable();
            }
        }

        // Demo sections (Widgets, Layout, Popups, Tables, Inputs).
        DemoWindowWidgets(s_demoData);
        DemoWindowLayout();
        DemoWindowPopups();
        DemoWindowTables();
        DemoWindowInputs();

        // End of ShowDemoWindow().
        ImGui.PopItemWidth();
        ImGui.End();
    }

    #endregion

    // Repository invariant compliance: demo helpers that adapt an engine shape to the demo's own
    // preferred spelling. The former text-height and frame-height re-derivations were removed when the
    // library exposed the canonical public accessors; see ImGui.GetTextLineHeightWithSpacing and
    // ImGui.GetFrameHeight.
    internal static bool ColorEdit4(string label, ref ImVec4 col, ImGuiColorEditFlags flags = 0)
    {
        float[] arr = [col.X, col.Y, col.Z, col.W];
        bool changed = ImGui.ColorEdit4(label, arr, flags);
        if (changed) col = new ImVec4(arr[0], arr[1], arr[2], arr[3]);
        return changed;
    }

    internal static bool DragFloat2(string label, ref ImVec2 v, float vSpeed = 1.0f, float vMin = 0.0f, float vMax = 0.0f, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        float[] arr = [v.X, v.Y];
        bool changed = ImGui.DragFloat2(label, arr, vSpeed, vMin, vMax, format, flags);
        if (changed) v = new ImVec2(arr[0], arr[1]);
        return changed;
    }

}
