// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using Hikari.ImGui;

namespace Hikari.ImGui.Demo;

/// <summary>
/// Docking subsystem demonstration and example applications.
/// </summary>
public static partial class ImGuiDemo
{
    #region DockSpace Example Application State

    /// <summary>
    /// Configuration arguments for the advanced dockspace example.
    /// </summary>
    private sealed class ImGuiDemoDockspaceArgs
    {
        /// <summary>Whether the dockspace window fills the viewport fullscreen.</summary>
        public bool IsFullscreen = true;

        /// <summary>Whether to keep default window padding.</summary>
        public bool KeepWindowPadding = false;

        /// <summary>Flags configured on the dockspace node.</summary>
        public ImGuiDockNodeFlags DockSpaceFlags = ImGuiDockNodeFlags.None;
    }

    private static int s_dockDemoMode = 0;
    private static bool s_dockDemoModeChanged = false;
    private static readonly ImGuiDemoDockspaceArgs s_dockArgs = new();

    #endregion

    #region DockSpace Example Helper Functions

    private static void ShowDockingDisabledMessage()
    {
        ImGuiIO io = ImGui.GetIO();
        ImGui.Text("ERROR: Docking is not enabled! See Demo > Configuration.");
        ImGui.Text("Set io.ConfigFlags |= ImGuiConfigFlags.DockingEnable in your code, or ");
        ImGui.SameLine(0.0f, 0.0f);
        if (ImGui.SmallButton("click here"))
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
    }

    private static void ShowExampleAppDockSpaceBasic(ImGuiDockNodeFlags flags)
    {
        ImGui.DockSpaceOverViewport(0, null, flags);
    }

    private static void ShowExampleAppDockSpaceAdvanced(ImGuiDemoDockspaceArgs args, ref bool pOpen)
    {
        ImGuiDockNodeFlags dockspaceFlags = args.DockSpaceFlags;

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDocking;
        if (args.IsFullscreen)
        {
            ImGuiViewport viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
            windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
            windowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
            windowFlags |= ImGuiWindowFlags.NoBackground;
        }
        else
        {
            dockspaceFlags &= ~ImGuiDockNodeFlags.PassthruCentralNode;
        }

        if (!args.KeepWindowPadding)
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new ImVec2(0.0f, 0.0f));
        ImGui.Begin("Window with a DockSpace", ref pOpen, windowFlags);
        if (!args.KeepWindowPadding)
            ImGui.PopStyleVar();

        if (args.IsFullscreen)
            ImGui.PopStyleVar(2);

        uint dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, new ImVec2(0.0f, 0.0f), dockspaceFlags);

        ImGui.End();
    }

    #endregion

    #region DockSpace Example Application

    /// <summary>
    /// Demonstrates using <see cref="ImGui.DockSpace"/> and <see cref="ImGui.DockSpaceOverViewport"/>
    /// to provide a host docking area for other windows.
    /// </summary>
    /// <param name="pOpen">State controlling whether the example window is visible.</param>
    public static void ShowExampleAppDockSpace(ref bool pOpen)
    {
        if (s_dockDemoMode == 0)
            ShowExampleAppDockSpaceBasic(s_dockArgs.DockSpaceFlags);
        else
            ShowExampleAppDockSpaceAdvanced(s_dockArgs, ref pOpen);

        if (s_dockDemoModeChanged)
            ImGui.SetNextWindowFocus();

        ImGui.Begin("Examples: Dockspace", ref pOpen, ImGuiWindowFlags.MenuBar);
        s_dockDemoModeChanged = false;
        if (ImGui.RadioButton("Basic demo mode", s_dockDemoMode == 0))
        {
            s_dockDemoMode = 0;
            s_dockDemoModeChanged = true;
        }
        if (ImGui.RadioButton("Advanced demo mode", s_dockDemoMode == 1))
        {
            s_dockDemoMode = 1;
            s_dockDemoModeChanged = true;
        }

        ImGui.SeparatorText("Options");

        if ((ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
        {
            ShowDockingDisabledMessage();
        }
        else if (s_dockDemoMode == 0)
        {
            s_dockArgs.DockSpaceFlags &= ImGuiDockNodeFlags.PassthruCentralNode;
            CheckboxFlags("Flag: PassthruCentralNode", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.PassthruCentralNode);
        }
        else if (s_dockDemoMode == 1)
        {
            ImGui.Checkbox("Fullscreen", ref s_dockArgs.IsFullscreen);
            ImGui.Checkbox("Keep Window Padding", ref s_dockArgs.KeepWindowPadding);
            ImGui.SameLine();
            HelpMarker("This is mostly exposed to facilitate understanding that a DockSpace() is _inside_ a window.");
            ImGui.BeginDisabled(!s_dockArgs.IsFullscreen);
            CheckboxFlags("Flag: PassthruCentralNode", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.PassthruCentralNode);
            ImGui.EndDisabled();
            CheckboxFlags("Flag: NoDockingOverCentralNode", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.NoDockingOverCentralNode);
            CheckboxFlags("Flag: NoDockingSplit", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.NoDockingSplit);
            CheckboxFlags("Flag: NoUndocking", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.NoUndocking);
            CheckboxFlags("Flag: NoResize", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.NoResize);
            CheckboxFlags("Flag: AutoHideTabBar", ref s_dockArgs.DockSpaceFlags, ImGuiDockNodeFlags.AutoHideTabBar);
        }

        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("Help"))
            {
                ImGui.TextUnformatted(
                    "This demonstrates the use of ImGui::DockSpace() which allows you to manually\ncreate a docking node _within_ another window.\n" +
                    "The \"Basic\" version uses the ImGui::DockSpaceOverViewport() helper. Most applications can probably use this.");
                ImGui.Separator();
                ImGui.TextUnformatted("When docking is enabled, you can ALWAYS dock MOST window into another! Try it now!\n" +
                    "- Drag from window title bar or their tab to dock/undock.\n" +
                    "- Drag from window menu button (upper-left button) to undock an entire node (all windows).\n" +
                    "- Hold SHIFT to disable docking (if io.ConfigDockingWithShift == false, default)\n" +
                    "- Hold SHIFT to enable docking (if io.ConfigDockingWithShift == true)");
                ImGui.Separator();
                ImGui.TextUnformatted("More details:");
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextLinkOpenURL("Docking Wiki page", "https://github.com/ocornut/imgui/wiki/Docking");
                ImGui.BulletText("Read comments in ShowExampleAppDockSpace()");
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }

        ImGui.End();
    }

    #endregion
}
