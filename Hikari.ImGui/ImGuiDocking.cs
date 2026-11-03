// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hikari.ImGui;

/// <summary>
/// Provides immediate-mode GUI operations, context and frame lifecycle management, windowing,
/// layout, input routing, styling, widgets, tables, and docking facilities.
/// </summary>
public static unsafe partial class ImGui
{
    #region [SECTION] DOCKING: Constants & Shared State

    internal const int DOCKING_HOST_DRAW_CHANNEL_BG = 0;
    internal const int DOCKING_HOST_DRAW_CHANNEL_FG = 1;
    internal const string IMGUI_PAYLOAD_TYPE_WINDOW = "_IMWINDOW";

    private static readonly ImGuiCol[] GWindowDockStyleColors =
    [
        ImGuiCol.Text,
        ImGuiCol.TabHovered,
        ImGuiCol.TabSelected,
        ImGuiCol.TabSelected,
        ImGuiCol.TabSelectedOverline,
        ImGuiCol.TabDimmed,
        ImGuiCol.TabDimmedSelected,
        ImGuiCol.TabDimmedSelectedOverline,
        ImGuiCol.UnsavedMarker
    ];

    #endregion

    #region [SECTION] DOCKING: Context Lifecycle & Frame Updates

    /// <summary>
    /// Initializes the docking subsystem context and registers the settings handler for docking.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    public static void DockContextInitialize(ImGuiContext ctx)
    {
        ctx.DockNodeWindowMenuHandler = DockNodeWindowMenuHandler_Default;
        DockSettingsHandlerRegister(ctx);
    }

    /// <summary>
    /// Registers the `Docking` settings handler with the context.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <remarks>
    /// The handler is registered with <c>ClearAllFn</c> and <c>ReadInitFn</c> pointing at the same clear
    /// routine, so a `.ini` read starts from an empty docking state rather than merging into whatever the
    /// previous file produced.
    /// </remarks>
    internal static void DockSettingsHandlerRegister(ImGuiContext ctx)
    {
        ImGuiSettingsHandler handler = new()
        {
            TypeName = "Docking",
            TypeHash = ImHashStr("Docking"),
            ClearAllFn = DockSettingsHandler_ClearAllHandler,
            ReadInitFn = DockSettingsHandler_ClearAllHandler, // Also clear on read.
            ReadOpenFn = DockSettingsHandler_ReadOpen,
            ReadLineFn = DockSettingsHandler_ReadLine,
            ApplyAllFn = DockSettingsHandler_ApplyAllHandler,
            WriteAllFn = DockSettingsHandler_WriteAll,
        };
        AddSettingsHandler(handler);
    }

    /// <summary>
    /// Adapts the canonical two-argument clear callback to the port's context-only helper.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="handler">The settings handler, unused.</param>
    private static void DockSettingsHandler_ClearAllHandler(ImGuiContext ctx, ImGuiSettingsHandler handler)
    {
        _ = handler;
        DockSettingsHandler_ClearAll(ctx);
    }

    /// <summary>
    /// Adapts the canonical two-argument apply callback to the port's context-only helper.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="handler">The settings handler, unused.</param>
    private static void DockSettingsHandler_ApplyAllHandler(ImGuiContext ctx, ImGuiSettingsHandler handler)
    {
        _ = handler;
        DockSettingsHandler_ApplyAll(ctx);
    }

    /// <summary>
    /// Clears all docking node settings in the specified context.
    /// </summary>
    /// <param name="ctx">The context whose docking settings will be cleared.</param>
    internal static void DockSettingsHandler_ClearAll(ImGuiContext ctx)
    {
        ctx.DockContext.NodesSettings.Clear();
        DockContextClearNodes(ctx, 0, true);
    }

    /// <summary>
    /// Recreates dock nodes from the loaded settings.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <remarks>
    /// Canonical prunes stale settings nodes only at boot time, when no window exists yet; a later apply
    /// must keep every node referenced by the already-running windows.
    /// </remarks>
    internal static void DockSettingsHandler_ApplyAll(ImGuiContext ctx)
    {
        if (ctx.Windows.Count == 0)
            DockContextPruneUnusedSettingsNodes(ctx);
        DockContextBuildNodesFromSettings(ctx, ctx.DockContext.NodesSettings);
        DockContextBuildAddWindowsToNodes(ctx, 0);
    }

    /// <summary>
    /// Opens the single `[Docking][Data]` section for reading.
    /// </summary>
    /// <param name="ctx">Active ImGui context, unused.</param>
    /// <param name="handler">The settings handler, unused.</param>
    /// <param name="name">The section entry name.</param>
    /// <returns>A non-null sentinel when the entry is named <c>Data</c>; otherwise
    /// <see langword="null"/>, which makes the parser ignore the section's lines.</returns>
    private static object? DockSettingsHandler_ReadOpen(ImGuiContext ctx, ImGuiSettingsHandler handler, string name)
    {
        _ = ctx;
        _ = handler;
        if (name != "Data")
            return null;
        return true;
    }

    /// <summary>
    /// Finds the docking settings record for a node identifier.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="id">Node identifier to look up.</param>
    /// <returns>The matching settings record, or <see langword="null"/> when absent.</returns>
    internal static ImGuiDockNodeSettings? DockSettingsFindNodeSettings(ImGuiContext ctx, uint id)
    {
        List<ImGuiDockNodeSettings> settings = ctx.DockContext.NodesSettings;
        for (int n = 0; n < settings.Count; n++)
        {
            if (settings[n].ID == id)
                return settings[n];
        }
        return null;
    }

    /// <summary>
    /// Reads one depth-indented dock node line.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="handler">The settings handler, unused.</param>
    /// <param name="entry">The read-open sentinel, unused.</param>
    /// <param name="line">The line to parse, for example
    /// <c>" DockNode ID=0x00000001 Pos=383,193 Size=201,322 Split=Y,0.506"</c>.</param>
    /// <remarks>
    /// A malformed line or one missing a mandatory field is ignored, matching canonical's zero-tolerance
    /// parser. Depth is derived from the parent record, which must therefore appear earlier in the file;
    /// the canonical writer emits records in depth-first order to guarantee that.
    /// </remarks>
    internal static void DockSettingsHandler_ReadLine(ImGuiContext ctx, ImGuiSettingsHandler handler, object entry, string line)
    {
        _ = handler;
        _ = entry;

        string text = line.TrimStart();
        ImGuiDockNodeSettings node = new();
        if (text.StartsWith("DockNode", StringComparison.Ordinal))
        {
            text = text[8..].TrimStart();
        }
        else if (text.StartsWith("DockSpace", StringComparison.Ordinal))
        {
            text = text[9..].TrimStart();
            node.Flags |= ImGuiDockNodeFlags.DockSpace;
        }
        else
        {
            return;
        }

        bool haveId = false;
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = token.IndexOf('=');
            if (equals <= 0)
                continue;
            string key = token[..equals];
            ReadOnlySpan<char> value = token.AsSpan(equals + 1);
            switch (key)
            {
                case "ID":
                    if (!TryParseDockHex(value, out uint id))
                        return;
                    node.ID = id;
                    haveId = true;
                    break;
                case "Parent":
                    if (!TryParseDockHex(value, out uint parentId) || parentId == 0)
                        return;
                    node.ParentNodeId = parentId;
                    break;
                case "Window":
                    if (!TryParseDockHex(value, out uint windowId) || windowId == 0)
                        return;
                    node.ParentWindowId = windowId;
                    break;
                case "Pos":
                    if (node.ParentNodeId == 0 && TryParseDockIntPair(value, out int x, out int y))
                        node.Pos = new ImVec2ih((short)x, (short)y);
                    break;
                case "Size":
                    if (node.ParentNodeId == 0 && TryParseDockIntPair(value, out x, out y))
                        node.Size = new ImVec2ih((short)x, (short)y);
                    break;
                case "SizeRef":
                    if (node.ParentNodeId != 0 && TryParseDockIntPair(value, out x, out y))
                        node.SizeRef = new ImVec2ih((short)x, (short)y);
                    break;
                case "Split":
                    if (value.Length >= 1)
                        node.SplitAxis = value[0] == 'X' ? (sbyte)ImGuiAxis.X : (sbyte)ImGuiAxis.Y;
                    break;
                case "NoResize":
                    if (ParseDockFlag(value))
                        node.Flags |= ImGuiDockNodeFlags.NoResize;
                    break;
                case "CentralNode":
                    if (ParseDockFlag(value))
                        node.Flags |= ImGuiDockNodeFlags.CentralNode;
                    break;
                case "NoTabBar":
                    if (ParseDockFlag(value))
                        node.Flags |= ImGuiDockNodeFlags.NoTabBar;
                    break;
                case "HiddenTabBar":
                    if (ParseDockFlag(value))
                        node.Flags |= ImGuiDockNodeFlags.HiddenTabBar;
                    break;
                case "NoWindowMenuButton":
                    if (ParseDockFlag(value))
                        node.Flags |= ImGuiDockNodeFlags.NoWindowMenuButton;
                    break;
                case "NoCloseButton":
                    if (ParseDockFlag(value))
                        node.Flags |= ImGuiDockNodeFlags.NoCloseButton;
                    break;
                case "Selected":
                    if (TryParseDockHex(value, out uint selected))
                        node.SelectedTabId = selected;
                    break;
            }
        }

        if (!haveId)
            return;
        if (node.ParentNodeId != 0)
        {
            ImGuiDockNodeSettings? parent = DockSettingsFindNodeSettings(ctx, node.ParentNodeId);
            if (parent != null)
                node.Depth = (sbyte)(parent.Depth + 1);
        }
        ctx.DockContext.NodesSettings.Add(node);
    }

    /// <summary>
    /// Converts a dock node and its descendants into settings records, depth-first.
    /// </summary>
    /// <param name="ctx">Active ImGui context; the records are appended to its docking context.</param>
    /// <param name="node">The node to convert.</param>
    /// <param name="depth">The node's depth below its root, which the writer uses for indentation.</param>
    /// <remarks>
    /// Only the saved flag mask is preserved, so transient flags such as <c>NoResizeX</c>/<c>NoResizeY</c>
    /// that are recomputed at runtime never reach the file. The parent host window is recorded only for a
    /// dock-space node, because that is the window whose identity must be restored to reattach the node.
    /// </remarks>
    internal static void DockSettingsHandler_DockNodeToSettings(ImGuiDockContext ctx, ImGuiDockNode node, int depth)
    {
        ImAssert.Check(depth < 256, "Dock node depth must fit the packed settings field.");
        ctx.NodesSettings.Add(new ImGuiDockNodeSettings
        {
            ID = node.ID,
            ParentNodeId = node.ParentNode?.ID ?? 0,
            ParentWindowId = node.IsDockSpace() && node.HostWindow?.ParentWindow != null ? node.HostWindow.ParentWindow.ID : 0,
            SelectedTabId = node.SelectedTabId,
            SplitAxis = node.IsSplitNode() ? (sbyte)node.SplitAxis : (sbyte)-1,
            Depth = (sbyte)depth,
            Flags = node.LocalFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.SavedFlagsMask_,
            Pos = new ImVec2ih((short)node.Pos.X, (short)node.Pos.Y),
            Size = new ImVec2ih((short)node.Size.X, (short)node.Size.Y),
            SizeRef = new ImVec2ih((short)node.SizeRef.X, (short)node.SizeRef.Y),
        });

        if (node.ChildNodes[0] != null)
            DockSettingsHandler_DockNodeToSettings(ctx, node.ChildNodes[0]!, depth + 1);
        if (node.ChildNodes[1] != null)
            DockSettingsHandler_DockNodeToSettings(ctx, node.ChildNodes[1]!, depth + 1);
    }

    /// <summary>
    /// Converts one dock node into a settings record without its descendants.
    /// </summary>
    /// <param name="node">The node to convert.</param>
    /// <returns>The settings record for the node at depth zero.</returns>
    /// <remarks>
    /// This single-node form exists for callers that already walk the hierarchy themselves; the recursive
    /// overload is the one the writer uses. It records the parent host window and the saved flags, which
    /// the earlier docking port omitted (bug 6-B8).
    /// </remarks>
    internal static ImGuiDockNodeSettings DockSettingsHandler_DockNodeToSettings(ImGuiDockNode node)
    {
        return new ImGuiDockNodeSettings
        {
            ID = node.ID,
            ParentNodeId = node.ParentNode?.ID ?? 0,
            ParentWindowId = node.IsDockSpace() && node.HostWindow?.ParentWindow != null ? node.HostWindow.ParentWindow.ID : 0,
            SelectedTabId = node.SelectedTabId,
            SplitAxis = node.IsSplitNode() ? (sbyte)node.SplitAxis : (sbyte)-1,
            Depth = 0,
            Flags = node.LocalFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.SavedFlagsMask_,
            Pos = new ImVec2ih((short)node.Pos.X, (short)node.Pos.Y),
            Size = new ImVec2ih((short)node.Size.X, (short)node.Size.Y),
            SizeRef = new ImVec2ih((short)node.SizeRef.X, (short)node.SizeRef.Y),
        };
    }

    /// <summary>
    /// Rewrites the current dock node hierarchy into the context's settings list.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <remarks>
    /// Unlike the window settings, docking does a full rewrite each time because the node list is rebuilt
    /// from the settings on load. The walk starts only from root nodes so each node is emitted once.
    /// </remarks>
    internal static void DockSettingsSave(ImGuiContext ctx)
    {
        ctx.DockContext.NodesSettings.Clear();
        foreach (ImGuiDockNode node in ctx.DockContext.Nodes.Values)
        {
            if (node.IsRootNode())
                DockSettingsHandler_DockNodeToSettings(ctx.DockContext, node, 0);
        }
    }

    /// <summary>
    /// Removes references to invalidated nodes from the window settings entries.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="nodeIds">The invalidated node identifiers.</param>
    private static void DockSettingsRemoveNodeReferences(ImGuiContext ctx, uint[] nodeIds)
    {
        List<ImGuiWindowSettings> settingsWindows = ctx.SettingsWindows;
        for (int windowN = 0; windowN < settingsWindows.Count; windowN++)
        {
            ImGuiWindowSettings settings = settingsWindows[windowN];
            for (int nodeN = 0; nodeN < nodeIds.Length; nodeN++)
            {
                if (settings.DockId == nodeIds[nodeN])
                {
                    settings.DockId = 0;
                    settings.DockOrder = -1;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Discards settings nodes that no longer reference a live window or dock space.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <remarks>
    /// This runs only when no window exists yet, which is what lets the reference counts be computed purely
    /// from the settings data. A root node is removed when it carries at most one window and no children;
    /// any node whose root ends up with no child windows is removed; and a node left as an only child is
    /// merged into its parent.
    /// </remarks>
    private static void DockContextPruneUnusedSettingsNodes(ImGuiContext ctx)
    {
        ImGuiDockContext dc = ctx.DockContext;
        ImAssert.Check(ctx.Windows.Count == 0);

        ImPool<ImGuiDockContextPruneNodeData> pool = new();

        // Count child nodes and resolve each node's root.
        for (int settingsN = 0; settingsN < dc.NodesSettings.Count; settingsN++)
        {
            ImGuiDockNodeSettings settings = dc.NodesSettings[settingsN];
            if (pool.GetByKey(settings.ID) != null)
            {
                settings.ID = 0; // Duplicate entry.
                continue;
            }
            ImGuiDockContextPruneNodeData? parentData = settings.ParentNodeId != 0 ? pool.GetByKey(settings.ParentNodeId) : null;
            pool.GetOrAddByKey(settings.ID).RootId = parentData != null ? parentData.RootId : settings.ID;
            if (settings.ParentNodeId != 0)
                pool.GetOrAddByKey(settings.ParentNodeId).CountChildNodes++;
        }

        // A dock space counts as a reference to the dock node it will build.
        for (int settingsN = 0; settingsN < dc.NodesSettings.Count; settingsN++)
        {
            ImGuiDockNodeSettings settings = dc.NodesSettings[settingsN];
            if (settings.ParentWindowId == 0)
                continue;
            ImGuiWindowSettings? windowSettings = FindWindowSettingsByID(settings.ParentWindowId);
            if (windowSettings == null || windowSettings.DockId == 0)
                continue;
            ImGuiDockContextPruneNodeData? data = pool.GetByKey(windowSettings.DockId);
            if (data != null)
                data.CountChildNodes++;
        }

        // Window settings reference their dock node, and that reference propagates to the node's root.
        for (int windowN = 0; windowN < ctx.SettingsWindows.Count; windowN++)
        {
            ImGuiWindowSettings windowSettings = ctx.SettingsWindows[windowN];
            if (windowSettings.DockId == 0)
                continue;
            ImGuiDockContextPruneNodeData? data = pool.GetByKey(windowSettings.DockId);
            if (data == null)
                continue;
            data.CountWindows++;
            ImGuiDockContextPruneNodeData? dataRoot = data.RootId == windowSettings.DockId ? data : pool.GetByKey(data.RootId);
            if (dataRoot != null)
                dataRoot.CountChildWindows++;
        }

        for (int settingsN = 0; settingsN < dc.NodesSettings.Count; settingsN++)
        {
            ImGuiDockNodeSettings settings = dc.NodesSettings[settingsN];
            ImGuiDockContextPruneNodeData? data = pool.GetByKey(settings.ID);
            if (data == null || data.CountWindows > 1)
                continue;
            ImGuiDockContextPruneNodeData? dataRoot = settings.ID == data.RootId ? data : pool.GetByKey(data.RootId);
            ImGuiDockContextPruneNodeData? dataParent = settings.ParentNodeId != 0 ? pool.GetByKey(settings.ParentNodeId) : null;

            bool remove = false;
            remove |= data.CountWindows == 1 && settings.ParentNodeId == 0 && data.CountChildNodes == 0
                && (settings.Flags & ImGuiDockNodeFlags.CentralNode) == 0;
            remove |= data.CountWindows == 0 && settings.ParentNodeId == 0 && data.CountChildNodes == 0;
            remove |= dataRoot == null || dataRoot.CountChildWindows == 0;
            if (remove)
            {
                DockSettingsRemoveNodeReferences(ctx, new[] { settings.ID });
                settings.ID = 0;
            }
            else if (dataParent != null && dataParent.CountChildNodes == 1)
            {
                DockSettingsRenameNodeReferences(settings.ID, settings.ParentNodeId);
                settings.ID = 0;
            }
        }
    }

    /// <summary>
    /// Writes the docking section to the output buffer.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="handler">The docking settings handler supplying the section type name.</param>
    /// <param name="buf">The output buffer receiving the `[Docking][Data]` section.</param>
    /// <remarks>
    /// Nodes are indented by twice their depth and padded on the right so their fields line up in the
    /// file, which makes hand-inspection practical. The whole list is rewritten on every save.
    /// </remarks>
    internal static void DockSettingsHandler_WriteAll(ImGuiContext ctx, ImGuiSettingsHandler handler, ImGuiTextBuffer buf)
    {
        ImGuiDockContext dc = ctx.DockContext;
        if ((ctx.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
            return;

        DockSettingsSave(ctx);
        int maxDepth = 0;
        for (int n = 0; n < dc.NodesSettings.Count; n++)
            maxDepth = Math.Max(dc.NodesSettings[n].Depth, maxDepth);

        buf.AppendF("[{0}][Data]\n", handler.TypeName);
        for (int n = 0; n < dc.NodesSettings.Count; n++)
        {
            ImGuiDockNodeSettings settings = dc.NodesSettings[n];
            buf.AppendF("{0}{1}{2}", new string(' ', settings.Depth * 2),
                (settings.Flags & ImGuiDockNodeFlags.DockSpace) != 0 ? "DockSpace" : "DockNode ",
                new string(' ', (maxDepth - settings.Depth) * 2));
            buf.AppendF(" ID=0x{0:X8}", settings.ID);
            if (settings.ParentNodeId != 0)
            {
                buf.AppendF(" Parent=0x{0:X8} SizeRef={1},{2}", settings.ParentNodeId, settings.SizeRef.X, settings.SizeRef.Y);
            }
            else
            {
                if (settings.ParentWindowId != 0)
                    buf.AppendF(" Window=0x{0:X8}", settings.ParentWindowId);
                buf.AppendF(" Pos={0},{1} Size={2},{3}", settings.Pos.X, settings.Pos.Y, settings.Size.X, settings.Size.Y);
            }
            if (settings.SplitAxis != (sbyte)-1)
                buf.AppendF(" Split={0}", settings.SplitAxis == (sbyte)ImGuiAxis.X ? 'X' : 'Y');
            if ((settings.Flags & ImGuiDockNodeFlags.NoResize) != 0)
                buf.AppendF(" NoResize=1");
            if ((settings.Flags & ImGuiDockNodeFlags.CentralNode) != 0)
                buf.AppendF(" CentralNode=1");
            if ((settings.Flags & ImGuiDockNodeFlags.NoTabBar) != 0)
                buf.AppendF(" NoTabBar=1");
            if ((settings.Flags & ImGuiDockNodeFlags.HiddenTabBar) != 0)
                buf.AppendF(" HiddenTabBar=1");
            if ((settings.Flags & ImGuiDockNodeFlags.NoWindowMenuButton) != 0)
                buf.AppendF(" NoWindowMenuButton=1");
            if ((settings.Flags & ImGuiDockNodeFlags.NoCloseButton) != 0)
                buf.AppendF(" NoCloseButton=1");
            if (settings.SelectedTabId != 0)
                buf.AppendF(" Selected=0x{0:X8}", settings.SelectedTabId);
            buf.Append("\n");
        }
        buf.Append("\n");
    }

    /// <summary>
    /// Parses a hexadecimal dock identifier written with an optional `0x` prefix.
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <param name="value">The parsed identifier.</param>
    /// <returns><see langword="true"/> when the text was a valid hexadecimal identifier.</returns>
    private static bool TryParseDockHex(ReadOnlySpan<char> span, out uint value)
    {
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            span = span[2..];
        return uint.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Parses a comma-separated decimal pair such as a position or size.
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <param name="first">The parsed first value.</param>
    /// <param name="second">The parsed second value.</param>
    /// <returns><see langword="true"/> when both values parsed.</returns>
    private static bool TryParseDockIntPair(ReadOnlySpan<char> span, out int first, out int second)
    {
        first = 0;
        second = 0;
        int comma = span.IndexOf(',');
        if (comma < 0)
            return false;
        return int.TryParse(span[..comma], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out first)
            && int.TryParse(span[(comma + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out second);
    }

    /// <summary>
    /// Interprets a non-zero integer as an enabled docking flag.
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <returns><see langword="true"/> when the value parsed and is non-zero.</returns>
    private static bool ParseDockFlag(ReadOnlySpan<char> span)
    {
        return int.TryParse(span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) && value != 0;
    }

    /// <summary>
    /// Shuts down the docking subsystem context and deletes all active nodes.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    public static void DockContextShutdown(ImGuiContext ctx)
    {
        ImGuiDockContext dc = ctx.DockContext;
        List<ImGuiDockNode> nodesList = new(dc.Nodes.Values);
        for (int n = 0; n < nodesList.Count; n++)
            DockContextDeleteNode(ctx, nodesList[n]);
        dc.Nodes.Clear();
    }

    /// <summary>
    /// Clears dock nodes belonging to the given root hierarchy.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="root_id">Root node identifier, or 0 to clear all nodes.</param>
    /// <param name="clear_settings_refs">Whether to clear references in window/settings data.</param>
    public static void DockContextClearNodes(ImGuiContext ctx, uint root_id, bool clear_settings_refs)
    {
        DockBuilderRemoveNodeDockedWindows(root_id, clear_settings_refs);
        DockBuilderRemoveNodeChildNodes(root_id);
    }

    /// <summary>
    /// Rebuilds dock nodes from settings data.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    public static void DockContextRebuildNodes(ImGuiContext ctx)
    {
        uint rootId = 0;
        DockContextClearNodes(ctx, rootId, false);
        DockContextBuildNodesFromSettings(ctx, ctx.DockContext.NodesSettings);
        DockContextBuildAddWindowsToNodes(ctx, rootId);
    }

    /// <summary>
    /// Updates queued undocking requests at the beginning of the frame before input processing.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    public static void DockContextNewFrameUpdateUndocking(ImGuiContext ctx)
    {
        ImGuiContext g = ctx;
        ImGuiDockContext dc = ctx.DockContext;
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
        {
            if (dc.Nodes.Count > 0 || dc.Requests.Count > 0)
                DockContextClearNodes(ctx, 0, true);
            return;
        }

        // Setting NoSplit at runtime merges all nodes
        if (g.IO.ConfigDockingNoSplit)
        {
            List<ImGuiDockNode> nodes = new(dc.Nodes.Values);
            for (int n = 0; n < nodes.Count; n++)
            {
                ImGuiDockNode node = nodes[n];
                if (node.IsRootNode() && node.IsSplitNode())
                    DockBuilderRemoveNodeChildNodes(node.ID);
            }
        }

        if (dc.WantFullRebuild)
        {
            DockContextRebuildNodes(ctx);
            dc.WantFullRebuild = false;
        }

        // Process undocking requests before mouse movement resolution
        for (int i = 0; i < dc.Requests.Count; i++)
        {
            ImGuiDockRequest req = dc.Requests[i];
            if (req.Type == ImGuiDockRequestType.Undock && req.UndockTargetWindow != null)
                DockContextProcessUndockWindow(ctx, req.UndockTargetWindow);
            else if (req.Type == ImGuiDockRequestType.Undock && req.UndockTargetNode != null)
                DockContextProcessUndockNode(ctx, req.UndockTargetNode);
        }
    }

    /// <summary>
    /// Processes queued docking requests and creates host windows for floating root nodes.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    public static void DockContextNewFrameUpdateDocking(ImGuiContext ctx)
    {
        ImGuiContext g = ctx;
        ImGuiDockContext dc = ctx.DockContext;
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
            return;

        // Debug hovered dock node tracking
        g.DebugHoveredDockNode = null;
        if (g.HoveredWindowUnderMovingWindow is { } hoveredWindow)
        {
            if (hoveredWindow.DockNodeAsHost != null)
                g.DebugHoveredDockNode = DockNodeTreeFindVisibleNodeByPos(hoveredWindow.DockNodeAsHost, g.IO.MousePos);
            else if (hoveredWindow.RootWindow?.DockNode != null)
                g.DebugHoveredDockNode = hoveredWindow.RootWindow.DockNode;
        }

        // Process docking requests
        for (int i = 0; i < dc.Requests.Count; i++)
        {
            ImGuiDockRequest req = dc.Requests[i];
            if (req.Type == ImGuiDockRequestType.Dock)
                DockContextProcessDock(ctx, req);
        }
        dc.Requests.Clear();

        // Create windows for each floating root dock node
        List<ImGuiDockNode> nodes = new(dc.Nodes.Values);
        for (int n = 0; n < nodes.Count; n++)
        {
            ImGuiDockNode node = nodes[n];
            if (node.IsFloatingNode())
                DockNodeUpdate(node);
        }
    }

    /// <summary>
    /// Completes the docking frame lifecycle by drawing backgrounds of empty leaf nodes.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    public static void DockContextEndFrame(ImGuiContext ctx)
    {
        ImGuiContext g = ctx;
        ImGuiDockContext dc = g.DockContext;
        foreach (ImGuiDockNode node in dc.Nodes.Values)
        {
            if (node.LastFrameActive == g.FrameCount && node.IsVisible && node.HostWindow != null && node.IsLeafNode() && !node.IsBgDrawnThisFrame)
            {
                ImRect bgRect = new(node.Pos + new ImVec2(0.0f, GetFrameHeight()), node.Pos + node.Size);
                ImDrawFlags bgRoundingFlags = CalcRoundingFlagsForRectInRect(bgRect, node.HostWindow.Rect(), g.Style.DockingSeparatorSize);
                node.HostWindow.DrawList.ChannelsSetCurrent(DOCKING_HOST_DRAW_CHANNEL_BG);
                node.HostWindow.DrawList.AddRectFilled(bgRect.Min, bgRect.Max, node.LastBgColor, node.HostWindow.WindowRounding, bgRoundingFlags);
            }
        }
    }

    /// <summary>
    /// Generates a new unique dock node identifier.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <returns>A new non-colliding dock node ID.</returns>
    public static uint DockContextGenNodeID(ImGuiContext ctx)
    {
        uint id = 0x0001;
        while (DockContextFindNodeByID(ctx, id) != null)
            id++;
        return id;
    }

    /// <summary>
    /// Finds a dock node by its identifier.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="id">Node identifier.</param>
    /// <returns>The dock node, or <c>null</c> if not found.</returns>
    public static ImGuiDockNode? DockContextFindNodeByID(ImGuiContext ctx, uint id)
    {
        if (id == 0)
            return null;
        ctx.DockContext.Nodes.TryGetValue(id, out ImGuiDockNode? node);
        return node;
    }

    #endregion

    #region [SECTION] DOCKING: Queued Docking & Undocking Operations

    /// <summary>
    /// Enqueues a dock request to be executed during the frame update.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="target">Target window, if any.</param>
    /// <param name="target_node">Target dock node, if any.</param>
    /// <param name="payload">Payload window being docked.</param>
    /// <param name="split_dir">Split direction.</param>
    /// <param name="split_ratio">Split ratio.</param>
    /// <param name="split_outer">Whether the split applies to the outer perimeter.</param>
    public static void DockContextQueueDock(ImGuiContext ctx, ImGuiWindow? target, ImGuiDockNode? target_node, ImGuiWindow? payload, ImGuiDir split_dir, float split_ratio, bool split_outer)
    {
        ImAssert.Check(target != payload);
        ImGuiDockRequest req = new()
        {
            Type = ImGuiDockRequestType.Dock,
            DockTargetWindow = target,
            DockTargetNode = target_node,
            DockPayload = payload,
            DockSplitDir = split_dir,
            DockSplitRatio = split_ratio,
            DockSplitOuter = split_outer
        };
        ctx.DockContext.Requests.Add(req);
    }

    /// <summary>
    /// Enqueues an undock request for a single window.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="window">Window to undock.</param>
    public static void DockContextQueueUndockWindow(ImGuiContext ctx, ImGuiWindow window)
    {
        ImGuiDockRequest req = new()
        {
            Type = ImGuiDockRequestType.Undock,
            UndockTargetWindow = window
        };
        ctx.DockContext.Requests.Add(req);
    }

    /// <summary>
    /// Enqueues an undock request for an entire dock node hierarchy.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="node">Dock node to undock.</param>
    public static void DockContextQueueUndockNode(ImGuiContext ctx, ImGuiDockNode node)
    {
        ImGuiDockRequest req = new()
        {
            Type = ImGuiDockRequestType.Undock,
            UndockTargetNode = node
        };
        ctx.DockContext.Requests.Add(req);
    }

    /// <summary>
    /// Cancels queued requests targeting a removed node.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="node">Node being removed.</param>
    internal static void DockContextQueueNotifyRemovedNode(ImGuiContext ctx, ImGuiDockNode node)
    {
        List<ImGuiDockRequest> requests = ctx.DockContext.Requests;
        for (int i = 0; i < requests.Count; i++)
        {
            if (requests[i].DockTargetNode == node)
                requests[i].Type = ImGuiDockRequestType.None;
        }
    }

    /// <summary>
    /// Processes a single window undocking operation immediately.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="window">Window to undock.</param>
    /// <param name="clear_persistent_docking_ref">Whether to clear the window's persistent dock ID reference.</param>
    public static void DockContextProcessUndockWindow(ImGuiContext ctx, ImGuiWindow window, bool clear_persistent_docking_ref = true)
    {
        if (window.DockNode != null)
            DockNodeRemoveWindow(window.DockNode, window, clear_persistent_docking_ref ? 0 : window.DockId);
        else
            window.DockId = 0;

        window.Collapsed = false;
        window.DockIsActive = false;
        window.DockNodeIsVisible = false;
        window.DockTabIsVisible = false;
        window.Size = window.SizeFull = FixLargeWindowsWhenUndocking(window.SizeFull, window.Viewport);

        MarkIniSettingsDirty();
    }

    /// <summary>
    /// Processes a dock node undocking operation immediately.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="node">Dock node to undock.</param>
    public static void DockContextProcessUndockNode(ImGuiContext ctx, ImGuiDockNode node)
    {
        ImAssert.Check(node.IsLeafNode());
        ImAssert.Check(node.Windows.Count >= 1);

        if (node.IsRootNode() || node.IsCentralNode())
        {
            ImGuiDockNode newNode = DockContextAddNode(ctx, 0);
            newNode.Pos = node.Pos;
            newNode.Size = node.Size;
            newNode.SizeRef = node.SizeRef;
            DockNodeMoveWindows(newNode, node);
            DockSettingsRenameNodeReferences(node.ID, newNode.ID);
            node = newNode;
        }
        else if (node.ParentNode is ImGuiDockNode parentNode)
        {
            ImAssert.Check(parentNode.ChildNodes[0] == node || parentNode.ChildNodes[1] == node);
            int indexInParent = parentNode.ChildNodes[0] == node ? 0 : 1;
            parentNode.ChildNodes[indexInParent] = null;
            DockNodeTreeMerge(ctx, parentNode, parentNode.ChildNodes[indexInParent ^ 1]!);
            parentNode.AuthorityForViewport = (byte)ImGuiDataAuthority.Window;
            node.ParentNode = null;
        }

        for (int i = 0; i < node.Windows.Count; i++)
        {
            ImGuiWindow window = node.Windows[i];
            window.Flags &= ~ImGuiWindowFlags.ChildWindow;
            if (window.ParentWindow != null)
                window.ParentWindow.DC.ChildWindows.Remove(window);
            UpdateWindowParentAndRootLinks(window, window.Flags, null);
        }

        node.AuthorityForPos = (byte)ImGuiDataAuthority.DockNode;
        node.AuthorityForSize = (byte)ImGuiDataAuthority.DockNode;
        node.Size = FixLargeWindowsWhenUndocking(node.Size, node.Windows[0].Viewport);
        node.WantMouseMove = true;
        MarkIniSettingsDirty();
    }

    /// <summary>
    /// Calculates the screen position for a docking drop split.
    /// </summary>
    /// <param name="target">Target window.</param>
    /// <param name="target_node">Target node.</param>
    /// <param name="payload_window">Window being docked.</param>
    /// <param name="payload_node">Node being docked.</param>
    /// <param name="split_dir">Split direction.</param>
    /// <param name="split_outer">Whether the split is on the outer boundary.</param>
    /// <param name="out_pos">Resulting drop position.</param>
    /// <returns><c>true</c> if a valid drop position was computed; otherwise <c>false</c>.</returns>
    public static bool DockContextCalcDropPosForDocking(ImGuiWindow? target, ImGuiDockNode? target_node, ImGuiWindow? payload_window, ImGuiDockNode? payload_node, ImGuiDir split_dir, bool split_outer, ImVec2* out_pos)
    {
        bool ret = DockContextCalcDropPosForDocking(target, target_node, payload_window, payload_node, split_dir, split_outer, out ImVec2 pos);
        if (out_pos != null)
            *out_pos = pos;
        return ret;
    }

    /// <summary>
    /// Calculates the screen position for a docking drop split.
    /// </summary>
    /// <param name="target">Target window.</param>
    /// <param name="target_node">Target node.</param>
    /// <param name="payload_window">Window being docked.</param>
    /// <param name="payload_node">Node being docked.</param>
    /// <param name="split_dir">Split direction.</param>
    /// <param name="split_outer">Whether the split is on the outer boundary.</param>
    /// <param name="outPos">Resulting drop position.</param>
    /// <returns><c>true</c> if a valid drop position was computed; otherwise <c>false</c>.</returns>
    public static bool DockContextCalcDropPosForDocking(ImGuiWindow? target, ImGuiDockNode? target_node, ImGuiWindow? payload_window, ImGuiDockNode? payload_node, ImGuiDir split_dir, bool split_outer, out ImVec2 outPos)
    {
        outPos = default;
        if (target != null && target_node == null)
            target_node = target.DockNode;

        if (target_node != null && target_node.ParentNode == null && target_node.IsCentralNode() && split_dir != ImGuiDir.None)
            split_outer = true;

        ImGuiDockPreviewData splitData = new();
        DockNodePreviewDockSetup(target, target_node, payload_window, payload_node, splitData, false, split_outer);
        int dirIndex = (int)split_dir + 1;
        if (dirIndex < 0 || dirIndex >= splitData.DropRectsDraw.Length || splitData.DropRectsDraw[dirIndex].IsInverted())
            return false;

        outPos = splitData.DropRectsDraw[dirIndex].GetCenter();
        return true;
    }

    #endregion

    #region [SECTION] DOCKING: Public APIs & Window Docking State

    /// <summary>
    /// Sets the dock target node identifier for the next window to be submitted.
    /// </summary>
    /// <param name="dock_id">Dock node identifier.</param>
    /// <param name="cond">Condition flag determining when the dock ID is applied.</param>
    public static void SetNextWindowDockID(uint dock_id, ImGuiCond cond = 0)
    {
        ImGuiContext g = GImGui!;
        g.NextWindowData.SetDockId(dock_id, cond);
    }

    /// <summary>
    /// Gets the dock node identifier of the current window.
    /// </summary>
    /// <returns>The dock ID of the current window, or 0 if undocked.</returns>
    public static uint GetWindowDockID()
    {
        ImGuiContext g = GImGui!;
        return g.CurrentWindow?.DockId ?? 0;
    }

    /// <summary>
    /// Returns whether the current window is docked into a dock node.
    /// </summary>
    /// <returns><c>true</c> if the current window is docked; otherwise <c>false</c>.</returns>
    public static bool IsWindowDocked()
    {
        ImGuiContext g = GImGui!;
        return (g.CurrentWindow?.DockIsActive ?? false) && (g.CurrentWindow?.DockNode != null);
    }

    /// <summary>
    /// Docks a window into a specific dock node.
    /// </summary>
    /// <param name="window">Window to dock.</param>
    /// <param name="dock_id">Destination dock node identifier.</param>
    /// <param name="cond">Condition flags.</param>
    public static void SetWindowDock(ImGuiWindow window, uint dock_id, ImGuiCond cond)
    {
        if (cond != ImGuiCond.None && (window.SetWindowDockAllowFlags & cond) == 0)
            return;
        window.SetWindowDockAllowFlags &= ~(ImGuiCond.Once | ImGuiCond.FirstUseEver | ImGuiCond.Appearing);

        if (window.DockId == dock_id)
            return;

        ImGuiContext g = GImGui!;
        if (DockContextFindNodeByID(g, dock_id) is { } newNode)
        {
            if (newNode.IsSplitNode())
            {
                newNode = DockNodeGetRootNode(newNode);
                if (newNode.CentralNode != null)
                {
                    ImAssert.Check(newNode.CentralNode.IsCentralNode());
                    dock_id = newNode.CentralNode.ID;
                }
                else
                {
                    dock_id = newNode.LastFocusedNodeId;
                }
            }
        }

        if (window.DockId == dock_id)
            return;

        if (window.DockNode != null)
            DockNodeRemoveWindow(window.DockNode, window, 0);
        window.DockId = dock_id;
    }

    /// <summary>
    /// Creates an explicit dockspace node within an existing window.
    /// </summary>
    /// <param name="dockspace_id">Unique dockspace identifier.</param>
    /// <param name="size">Explicit dockspace size, or (0,0) to fill remaining window space.</param>
    /// <param name="flags">Dock node behavior flags.</param>
    /// <param name="window_class">Window class constraints for docking into this node.</param>
    /// <returns>The dockspace node identifier.</returns>
    public static uint DockSpace(uint dockspace_id, ImVec2 size = default, ImGuiDockNodeFlags flags = 0, ImGuiWindowClass? window_class = null)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow? window = GetCurrentWindowRead();
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
            return 0;

        if (window != null && window.SkipItems)
            flags |= ImGuiDockNodeFlags.KeepAliveOnly;
        if ((flags & ImGuiDockNodeFlags.KeepAliveOnly) == 0)
            window = GetCurrentWindow();

        ImAssert.Check(dockspace_id != 0, "DockSpace ID cannot be 0");
        bool isKeepAlive = (flags & ImGuiDockNodeFlags.KeepAliveOnly) != 0;

        ImGuiDockNode? node = DockContextFindNodeByID(g, dockspace_id);
        if (node == null)
        {
            node = DockContextAddNode(g, dockspace_id);
            node.SetLocalFlags(ImGuiDockNodeFlags.CentralNode);
        }
        else
        {
            ImAssert.Check(node.IsRootNode());
            if (node.HostWindow != null && node.HostWindow != window && node.LastFrameActive >= g.FrameCount)
            {
                ImAssert.Check(false, "DockSpace ID collision or submitted twice in the same frame");
                return 0;
            }
        }

        node.SharedFlags = (ImGuiDockNodeFlags)((uint)flags & (uint)ImGuiDockNodeFlagsPrivate.SharedFlagsInheritMask_);
        node.WindowClass = window_class ?? new ImGuiWindowClass();
        node.SetLocalFlags(node.LocalFlags | ImGuiDockNodeFlags.DockSpace);

        if (isKeepAlive)
        {
            node.LastFrameAlive = g.FrameCount;
            return dockspace_id;
        }

        ImAssert.Check(window != null);
        ImVec2 contentAvail = GetContentRegionAvail();
        if (size.X <= 0.0f)
            size.X = MathF.Max(contentAvail.X + size.X, 4.0f);
        if (size.Y <= 0.0f)
            size.Y = MathF.Max(contentAvail.Y + size.Y, 4.0f);
        ImAssert.Check(size.X > 0.0f && size.Y > 0.0f);

        node.Pos = window!.DC.CursorPos;
        node.Size = node.SizeRef = size;
        SetNextWindowPos(node.Pos);
        SetNextWindowSize(node.Size);

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.DockNodeHost | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar;
        windowFlags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground;

        string title = $"{window.Name}/DockSpace_{dockspace_id:X08}";
        PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0.0f);
        Begin(title, windowFlags);
        PopStyleVar();

        ImGuiWindow hostWindow = g.CurrentWindow!;
        DockNodeSetupHostWindow(node, hostWindow);
        hostWindow.ChildId = window.GetID(title);
        node.OnlyNodeWithWindows = null;

        ImAssert.Check(node.IsRootNode());

        if (node.IsLeafNode() && !node.IsCentralNode())
            node.SetLocalFlags(node.LocalFlags | (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode);

        DockNodeUpdate(node);
        End();

        ImRect bb = new(node.Pos, node.Pos + size);
        ItemSize(size);
        ItemAdd(bb, dockspace_id, null, ImGuiItemFlags.NoNav);
        if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HoveredRect) != 0 && g.HoveredWindow != null && IsWindowChildOf(g.HoveredWindow, hostWindow, false, true))
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredWindow;

        return dockspace_id;
    }

    /// <summary>
    /// Submits a fullscreen dockspace window spanning the given viewport.
    /// </summary>
    /// <param name="dockspace_id">Unique dockspace ID, or 0 to generate a stable ID.</param>
    /// <param name="viewport">Viewport to cover, or <c>null</c> for main viewport.</param>
    /// <param name="dockspace_flags">Dock node flags.</param>
    /// <param name="window_class">Window class constraints.</param>
    /// <returns>The dockspace ID.</returns>
    public static uint DockSpaceOverViewport(uint dockspace_id = 0, ImGuiViewport? viewport = null, ImGuiDockNodeFlags dockspace_flags = 0, ImGuiWindowClass? window_class = null)
    {
        viewport ??= GetMainViewport();

        SetNextWindowPos(viewport.WorkPos);
        SetNextWindowSize(viewport.WorkSize);
        SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags hostWindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
        hostWindowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
        if ((dockspace_flags & ImGuiDockNodeFlags.PassthruCentralNode) != 0)
            hostWindowFlags |= ImGuiWindowFlags.NoBackground;

        if ((dockspace_flags & ImGuiDockNodeFlags.KeepAliveOnly) != 0)
            hostWindowFlags |= ImGuiWindowFlags.NoMouseInputs;

        string label = $"WindowOverViewport_{viewport.ID:X08}";

        PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        PushStyleVar(ImGuiStyleVar.WindowPadding, new ImVec2(0.0f, 0.0f));
        Begin(label, hostWindowFlags);
        PopStyleVar(3);

        if (dockspace_id == 0)
            dockspace_id = GetID("DockSpace");
        DockSpace(dockspace_id, new ImVec2(0.0f, 0.0f), dockspace_flags, window_class);

        End();
        return dockspace_id;
    }

    #endregion

    #region [SECTION] DOCKING: TabBar Amending & Dock Node Menus

    /// <summary>
    /// Begins appending user-submitted controls into an active dock node's tab bar.
    /// </summary>
    /// <param name="node">Target dock node.</param>
    /// <returns><c>true</c> if amending succeeded; otherwise <c>false</c>.</returns>
    public static bool DockNodeBeginAmendTabBar(ImGuiDockNode node)
    {
        if (node.TabBar == null || node.HostWindow == null)
            return false;
        if ((node.MergedFlags & ImGuiDockNodeFlags.KeepAliveOnly) != 0)
            return false;
        if (node.TabBar.ID == 0)
            return false;

        Begin(node.HostWindow.Name);
        PushOverrideID(node.ID);
        bool ret = BeginTabBarEx(node.TabBar, node.TabBar.BarRect, node.TabBar.Flags);
        ImAssert.Check(ret);
        return true;
    }

    /// <summary>
    /// Finishes appending controls into a dock node's tab bar.
    /// </summary>
    public static void DockNodeEndAmendTabBar()
    {
        EndTabBar();
        PopID();
        End();
    }

    /// <summary>
    /// Default handler for a dock node window menu popup.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="node">Target dock node.</param>
    /// <param name="tab_bar">Node tab bar.</param>
    public static void DockNodeWindowMenuHandler_Default(ImGuiContext ctx, ImGuiDockNode node, ImGuiTabBar tab_bar)
    {
        for (int i = 0; i < tab_bar.Tabs.Count; i++)
        {
            ImGuiTabItem tab = tab_bar.Tabs[i];
            if (tab.Window == null)
                continue;
            bool selected = (tab.ID == tab_bar.SelectedTabId);
            if (MenuItem(tab.Window.Name, null, selected))
                TabBarQueueFocus(tab_bar, tab);
        }
    }

    #endregion

    #region [SECTION] DOCKING: Drag & Drop Hooks & BeginDocked

    /// <summary>
    /// Performs docking-specific initialization for a docked window during Begin().
    /// </summary>
    /// <param name="window">Window being processed.</param>
    /// <param name="p_open">Optional open toggle flag.</param>
    public static void BeginDocked(ImGuiWindow window, bool* p_open)
    {
        bool dummy = true;
        if (p_open != null)
        {
            bool openVal = *p_open;
            BeginDocked(window, ref openVal, true);
            *p_open = openVal;
        }
        else
        {
            BeginDocked(window, ref dummy, false);
        }
    }

    /// <summary>
    /// Performs docking-specific initialization for a docked window during Begin().
    /// </summary>
    /// <param name="window">Window being processed.</param>
    /// <param name="pOpen">Reference to open boolean.</param>
    /// <param name="hasPOpen">Whether pOpen is passed.</param>
    internal static void BeginDocked(ImGuiWindow window, ref bool pOpen, bool hasPOpen = true)
    {
        ImGuiContext g = GImGui!;

        if (window.IsFallbackWindow && !window.WasActive)
        {
            DockNodeHideWindowDuringHostWindowCreation(window);
            return;
        }

        bool autoDockNode = GetWindowAlwaysWantOwnTabBar(window);
        if (autoDockNode)
        {
            if (window.DockId == 0)
            {
                ImAssert.Check(window.DockNode == null);
                window.DockId = DockContextGenNodeID(g);
            }
        }
        else
        {
            bool wantUndock = false;
            wantUndock |= (window.Flags & ImGuiWindowFlags.NoDocking) != 0;
            wantUndock |= (g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasPos) != 0 && (window.SetWindowPosAllowFlags & g.NextWindowData.PosCond) != 0 && g.NextWindowData.PosUndock;
            if (wantUndock)
            {
                DockContextProcessUndockWindow(g, window);
                return;
            }
        }

        ImGuiDockNode? node = window.DockNode;
        if (node != null)
            ImAssert.Check(window.DockId == node.ID);
        if (window.DockId != 0 && node == null)
        {
            node = DockContextBindNodeToWindow(g, window);
            if (node == null)
                return;
        }

        if (node == null)
            return;

        if (node.LastFrameAlive < g.FrameCount)
        {
            ImGuiDockNode rootNode = DockNodeGetRootNode(node);
            if (rootNode.LastFrameAlive < g.FrameCount)
                DockContextProcessUndockWindow(g, window);
            else
                window.DockIsActive = true;
            return;
        }

        StoreDockStyleForWindow(window);

        if (node.HostWindow == null)
        {
            if (node.State == ImGuiDockNodeState.HostWindowHiddenBecauseWindowsAreResizing)
                window.DockIsActive = true;
            if (node.Windows.Count > 1 && window.Appearing)
                DockNodeHideWindowDuringHostWindowCreation(window);
            return;
        }

        ImAssert.Check(node.IsLeafNode());
        ImAssert.Check(node.Size.X >= 0.0f && node.Size.Y >= 0.0f);
        node.State = ImGuiDockNodeState.HostWindowVisible;

        if ((node.MergedFlags & ImGuiDockNodeFlags.KeepAliveOnly) == 0 && window.BeginOrderWithinContext < node.HostWindow.BeginOrderWithinContext)
        {
            DockContextProcessUndockWindow(g, window);
            return;
        }

        SetNextWindowPos(node.Pos);
        SetNextWindowSize(node.Size);
        g.NextWindowData.PosUndock = false;
        window.DockIsActive = true;
        window.DockNodeIsVisible = true;
        window.DockTabIsVisible = false;
        if ((node.MergedFlags & ImGuiDockNodeFlags.KeepAliveOnly) != 0)
            return;

        if (node.VisibleWindow == window)
            window.DockTabIsVisible = true;

        ImAssert.Check((window.Flags & ImGuiWindowFlags.ChildWindow) == 0);
        window.Flags |= ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoResize;
        window.ChildFlags |= ImGuiChildFlags.AlwaysUseWindowPadding;
        if (node.IsHiddenTabBar() || node.IsNoTabBar())
            window.Flags |= ImGuiWindowFlags.NoTitleBar;
        else
            window.Flags &= ~ImGuiWindowFlags.NoTitleBar;

        if (node.TabBar != null && window.WasActive)
            window.DockOrder = (short)DockNodeGetTabOrder(window);

        if ((node.WantCloseAll || node.WantCloseTabId == window.TabId) && hasPOpen)
            pOpen = false;

        ImGuiWindow parentWindow = window.DockNode!.HostWindow!;
        window.ChildId = parentWindow.GetID(window.Name);
    }

    /// <summary>
    /// Initiates a drag and drop payload representing a movable dockable window.
    /// </summary>
    /// <param name="window">Window being dragged.</param>
    public static void BeginDockableDragDropSource(ImGuiWindow window)
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check(g.ActiveId == window.MoveId);
        ImAssert.Check(g.MovingWindow == window);

        if (g.IO.ConfigDockingWithShift != g.IO.KeyShift)
            return;

        g.LastItemData.ID = window.MoveId;
        window = window.RootWindowDockTree!;
        ImAssert.Check((window.Flags & ImGuiWindowFlags.NoDocking) == 0);

        bool isDragDocking = g.IO.ConfigDockingWithShift || new ImRect(0, 0, window.SizeFull.X, GetFrameHeight()).Contains(g.ActiveIdClickOffset);
        ImGuiDragDropFlags flags = ImGuiDragDropFlags.SourceNoPreviewTooltip | ImGuiDragDropFlags.SourceNoHoldToOpenOthers | ImGuiDragDropFlags.PayloadAutoExpire;
        if (isDragDocking && BeginDragDropSource(flags))
        {
            SetDragDropPayload(IMGUI_PAYLOAD_TYPE_WINDOW, window);
            EndDragDropSource();
            StoreDockStyleForWindow(window);
        }
    }

    /// <summary>
    /// Handles drag and drop hover and target preview for dockable windows.
    /// </summary>
    /// <param name="window">Target window.</param>
    public static void BeginDockableDragDropTarget(ImGuiWindow window)
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check((window.Flags & ImGuiWindowFlags.NoDocking) == 0);
        if (!g.DragDropActive)
            return;

        if (!BeginDragDropTargetCustom(window.Rect(), window.ID))
            return;

        ImGuiPayload payload = g.DragDropPayload;
        if (!payload.IsDataType(IMGUI_PAYLOAD_TYPE_WINDOW) || payload.ObjectData is not ImGuiWindow payloadWindow || !DockNodeIsDropAllowed(window, payloadWindow))
        {
            EndDragDropTarget();
            return;
        }

        if (AcceptDragDropPayload(IMGUI_PAYLOAD_TYPE_WINDOW, ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect) != null)
        {
            // Select target node
            // (Important: we cannot use g.HoveredDockNode here! Because each of our target node have filters based on payload, each candidate drop target will do its own evaluation)
            bool dockIntoFloatingWindow = false;
            ImGuiDockNode? node = null;
            if (window.DockNodeAsHost != null)
            {
                // Cannot assume that node will != null even though we passed the rectangle test: it depends on padding/spacing handled by DockNodeTreeFindVisibleNodeByPos().
                node = DockNodeTreeFindVisibleNodeByPos(window.DockNodeAsHost, g.IO.MousePos);

                // There is an edge case when docking into a dockspace which only has _inactive_ nodes (because none of the windows are active)
                // In this case we need to fallback into any leaf mode, possibly the central node.
                if (node != null && node.IsDockSpace() && node.IsRootNode())
                    node = (node.CentralNode != null && node.IsLeafNode()) ? node.CentralNode : DockNodeTreeFindFallbackLeafNode(node);
            }
            else
            {
                if (window.DockNode != null)
                    node = window.DockNode;
                else
                    dockIntoFloatingWindow = true; // Dock into a regular window
            }

            ImRect explicitTargetRect = (node != null && node.TabBar != null && !node.IsHiddenTabBar() && !node.IsNoTabBar())
                ? node.TabBar.BarRect
                : new ImRect(window.Pos, window.Pos + new ImVec2(window.Size.X, GetFrameHeight()));
            bool isExplicitTarget = g.IO.ConfigDockingWithShift || IsMouseHoveringRect(explicitTargetRect.Min, explicitTargetRect.Max);

            // Preview docking request and find out split direction/ratio
            bool doPreview = payload.Preview || payload.Delivery;
            if (doPreview && (node != null || dockIntoFloatingWindow))
            {
                // If we have a non-leaf node it means we are hovering the border of a parent node, in which case only outer markers will appear.
                ImGuiDockPreviewData splitInner = new();
                ImGuiDockPreviewData splitOuter = new();
                ImGuiDockPreviewData splitData = splitInner;
                if (node != null && (node.ParentNode != null || node.IsCentralNode() || !node.IsLeafNode()))
                {
                    if (DockNodeGetRootNode(node) is { } rootNode)
                    {
                        DockNodePreviewDockSetup(window, rootNode, payloadWindow, null, splitOuter, isExplicitTarget, true);
                        if (splitOuter.IsSplitDirExplicit)
                            splitData = splitOuter;
                    }
                }
                if (node == null || node.IsLeafNode())
                    DockNodePreviewDockSetup(window, node, payloadWindow, null, splitInner, isExplicitTarget, false);
                if (ReferenceEquals(splitData, splitOuter))
                    splitInner.IsDropAllowed = false;

                // Draw inner then outer, so that previewed tab (in inner data) will be behind the outer drop boxes
                DockNodePreviewDockRender(window, node, payloadWindow, splitInner);
                DockNodePreviewDockRender(window, node, payloadWindow, splitOuter);

                // Queue docking request
                if (splitData.IsDropAllowed && payload.Delivery)
                {
                    DockContextQueueDock(g, window, splitData.SplitNode, payloadWindow, splitData.SplitDir, splitData.SplitRatio, ReferenceEquals(splitData, splitOuter));
                }
            }
        }
        EndDragDropTarget();
    }

    #endregion

    #region [SECTION] DOCKING: DockBuilder API Family

    /// <summary>
    /// Assigns a window name to a dock node ID in advance.
    /// </summary>
    /// <param name="window_name">Window name.</param>
    /// <param name="node_id">Node identifier.</param>
    public static void DockBuilderDockWindow(string window_name, uint node_id)
    {
        ImGuiContext g = GImGui!;
        uint windowId = ImHashStr(window_name);
        if (FindWindowByID(windowId) is { } window)
        {
            uint prevNodeId = window.DockId;
            SetWindowDock(window, node_id, ImGuiCond.Always);
            if (window.DockId != prevNodeId)
                window.DockOrder = -1;
        }
        else
        {
            ImGuiWindowSettings? settings = FindWindowSettingsByID(windowId);
            if (settings == null)
                settings = CreateNewWindowSettings(window_name);
            if (settings.DockId != node_id)
                settings.DockOrder = -1;
            settings.DockId = node_id;
        }
    }

    /// <summary>
    /// Looks up a dock node by its identifier.
    /// (, )
    /// </summary>
    /// <param name="node_id">Node identifier.</param>
    /// <returns>The dock node, or <see langword="null"/> if not found.</returns>
    public static ImGuiDockNode? DockBuilderGetNode(uint node_id)
    {
        ImGuiContext g = GImGui!;
        return DockContextFindNodeByID(g, node_id);
    }

    /// <summary>
    /// Looks up the central dock node in the hierarchy containing the specified node.
    /// </summary>
    /// <param name="node_id">Node identifier.</param>
    /// <returns>The central dock node, or <see langword="null"/> if not found.</returns>
    public static ImGuiDockNode? DockBuilderGetCentralNode(uint node_id)
    {
        ImGuiDockNode? node = DockBuilderGetNode(node_id);
        if (node == null)
            return null;
        return DockNodeGetRootNode(node).CentralNode;
    }

    /// <summary>
    /// Adds a dock node by ID or creates a new ID.
    /// </summary>
    /// <param name="node_id">Node ID, or 0 to auto-generate.</param>
    /// <param name="flags">Dock node flags.</param>
    /// <returns>The allocated dock node ID.</returns>
    public static uint DockBuilderAddNode(uint node_id = 0, ImGuiDockNodeFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        if (node_id != 0)
            DockBuilderRemoveNode(node_id);

        ImGuiDockNode? node;
        if ((flags & ImGuiDockNodeFlags.DockSpace) != 0)
        {
            DockSpace(node_id, new ImVec2(0, 0), (flags & ~ImGuiDockNodeFlags.DockSpace) | ImGuiDockNodeFlags.KeepAliveOnly);
            node = DockContextFindNodeByID(g, node_id);
        }
        else
        {
            node = DockContextAddNode(g, node_id);
            node.SetLocalFlags(flags);
        }

        ImAssert.Check(node != null);
        node!.LastFrameAlive = g.FrameCount;
        return node.ID;
    }

    /// <summary>
    /// Removes a dock node and its children.
    /// </summary>
    /// <param name="node_id">Node identifier.</param>
    public static void DockBuilderRemoveNode(uint node_id)
    {
        ImGuiContext g = GImGui!;
        ImGuiDockNode? node = DockContextFindNodeByID(g, node_id);
        if (node == null)
            return;

        DockBuilderRemoveNodeDockedWindows(node_id, true);
        DockBuilderRemoveNodeChildNodes(node_id);

        node = DockContextFindNodeByID(g, node_id);
        if (node == null)
            return;

        if (node.IsCentralNode() && node.ParentNode != null)
            node.ParentNode.SetLocalFlags(node.ParentNode.LocalFlags | (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode);

        DockContextRemoveNode(g, node, true);
    }

    /// <summary>
    /// Removes child nodes of a dock node.
    /// </summary>
    /// <param name="node_id">Root node identifier, or 0 for all nodes.</param>
    public static void DockBuilderRemoveNodeChildNodes(uint node_id)
    {
        ImGuiContext g = GImGui!;
        ImGuiDockContext dc = g.DockContext;

        ImGuiDockNode? rootNode = node_id != 0 ? DockContextFindNodeByID(g, node_id) : null;
        if (node_id != 0 && rootNode == null)
            return;

        bool hasCentralNode = false;
        ImGuiDataAuthority backupPosAuth = rootNode != null ? (ImGuiDataAuthority)rootNode.AuthorityForPos : ImGuiDataAuthority.Auto;
        ImGuiDataAuthority backupSizeAuth = rootNode != null ? (ImGuiDataAuthority)rootNode.AuthorityForSize : ImGuiDataAuthority.Auto;

        List<ImGuiDockNode> nodesToRemove = new();
        foreach (ImGuiDockNode node in dc.Nodes.Values)
        {
            bool wantRemoval = (node_id == 0) || (node.ID != node_id && DockNodeGetRootNode(node).ID == node_id);
            if (wantRemoval)
            {
                if (node.IsCentralNode())
                    hasCentralNode = true;
                if (node_id != 0)
                    DockContextQueueNotifyRemovedNode(g, node);
                if (rootNode != null)
                {
                    DockNodeMoveWindows(rootNode, node);
                    DockSettingsRenameNodeReferences(node.ID, rootNode.ID);
                }
                nodesToRemove.Add(node);
            }
        }

        if (rootNode != null)
        {
            rootNode.AuthorityForPos = (byte)backupPosAuth;
            rootNode.AuthorityForSize = (byte)backupSizeAuth;
        }

        // Point any window settings that referenced a removed node at the placeholder root, mirroring
        // canonical. When the whole hierarchy is cleared
        // (node_id == 0) no root node survives, so those settings follow it to zero.
        for (int n = 0; n < g.SettingsWindows.Count; n++)
        {
            ImGuiWindowSettings settings = g.SettingsWindows[n];
            if (settings.DockId == 0)
                continue;
            for (int m = 0; m < nodesToRemove.Count; m++)
            {
                if (nodesToRemove[m].ID == settings.DockId)
                {
                    settings.DockId = node_id;
                    break;
                }
            }
        }

        // Remove the deepest nodes first so a parent is never destroyed while it still owns children;
        // DockContextRemoveNode requires a leaf.
        if (nodesToRemove.Count > 1)
            nodesToRemove.Sort(static (left, right) => DockNodeGetDepth(right) - DockNodeGetDepth(left));

        for (int n = 0; n < nodesToRemove.Count; n++)
            DockContextRemoveNode(g, nodesToRemove[n], false);

        if (node_id == 0)
        {
            dc.Nodes.Clear();
            dc.Requests.Clear();
        }
        else if (hasCentralNode && rootNode != null)
        {
            rootNode.CentralNode = rootNode;
            rootNode.SetLocalFlags(rootNode.LocalFlags | (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode);
        }
    }

    /// <summary>
    /// Undocks all windows docked into a node or its children.
    /// </summary>
    /// <param name="node_id">Root node identifier, or 0 for all windows.</param>
    /// <param name="clear_settings_refs">Whether to clear persistent dock settings references.</param>
    public static void DockBuilderRemoveNodeDockedWindows(uint node_id, bool clear_settings_refs = true)
    {
        ImGuiContext g = GImGui!;
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            bool wantRemoval = (node_id == 0)
                || (window.DockNode != null && DockNodeGetRootNode(window.DockNode).ID == node_id)
                || (window.DockNodeAsHost != null && window.DockNodeAsHost.ID == node_id);
            if (wantRemoval)
                DockContextProcessUndockWindow(g, window, clear_settings_refs);
        }
    }

    /// <summary>
    /// Sets a dock node's position.
    /// </summary>
    /// <param name="node_id">Node identifier.</param>
    /// <param name="pos">Screen position.</param>
    public static void DockBuilderSetNodePos(uint node_id, ImVec2 pos)
    {
        ImGuiContext g = GImGui!;
        if (DockContextFindNodeByID(g, node_id) is { } node)
        {
            node.Pos = pos;
            node.AuthorityForPos = (byte)ImGuiDataAuthority.DockNode;
        }
    }

    /// <summary>
    /// Sets a dock node's size.
    /// </summary>
    /// <param name="node_id">Node identifier.</param>
    /// <param name="size">Node size.</param>
    public static void DockBuilderSetNodeSize(uint node_id, ImVec2 size)
    {
        ImGuiContext g = GImGui!;
        if (DockContextFindNodeByID(g, node_id) is { } node)
        {
            ImAssert.Check(size.X > 0.0f && size.Y > 0.0f);
            node.Size = node.SizeRef = size;
            node.AuthorityForSize = (byte)ImGuiDataAuthority.DockNode;
        }
    }

    /// <summary>
    /// Splits an existing dock node into two adjacent nodes along a direction.
    /// </summary>
    /// <param name="node_id">Node to split.</param>
    /// <param name="split_dir">Split direction.</param>
    /// <param name="size_ratio_for_node_at_dir">Size ratio allocated to the directed node (0.1).</param>
    /// <param name="out_id_at_dir">Output node ID at the direction.</param>
    /// <param name="out_id_at_opposite_dir">Output node ID opposite to the direction.</param>
    /// <returns>Node ID at the direction.</returns>
    public static uint DockBuilderSplitNode(uint node_id, ImGuiDir split_dir, float size_ratio_for_node_at_dir, uint* out_id_at_dir, uint* out_id_at_opposite_dir)
    {
        uint atDir = 0, oppositeDir = 0;
        uint ret = DockBuilderSplitNode(node_id, split_dir, size_ratio_for_node_at_dir, out atDir, out oppositeDir);
        if (out_id_at_dir != null)
            *out_id_at_dir = atDir;
        if (out_id_at_opposite_dir != null)
            *out_id_at_opposite_dir = oppositeDir;
        return ret;
    }

    /// <summary>
    /// Splits an existing dock node into two adjacent nodes along a direction.
    /// </summary>
    /// <param name="node_id">Node to split.</param>
    /// <param name="split_dir">Split direction.</param>
    /// <param name="size_ratio_for_node_at_dir">Size ratio allocated to the directed node (0.1).</param>
    /// <param name="outIdAtDir">Output node ID at the direction.</param>
    /// <param name="outIdAtOppositeDir">Output node ID opposite to the direction.</param>
    /// <returns>Node ID at the direction.</returns>
    public static uint DockBuilderSplitNode(uint node_id, ImGuiDir split_dir, float size_ratio_for_node_at_dir, out uint outIdAtDir, out uint outIdAtOppositeDir)
    {
        outIdAtDir = 0;
        outIdAtOppositeDir = 0;
        ImGuiContext g = GImGui!;
        ImAssert.Check(split_dir != ImGuiDir.None);

        ImGuiDockNode? node = DockContextFindNodeByID(g, node_id);
        if (node == null)
            return 0;

        ImGuiDockRequest req = new()
        {
            Type = ImGuiDockRequestType.Split,
            DockTargetNode = node,
            DockSplitDir = split_dir,
            DockSplitRatio = Math.Clamp((split_dir == ImGuiDir.Left || split_dir == ImGuiDir.Up) ? size_ratio_for_node_at_dir : 1.0f - size_ratio_for_node_at_dir, 0.0f, 1.0f),
            DockSplitOuter = false
        };
        DockContextProcessDock(g, req);

        int atDirIdx = (split_dir == ImGuiDir.Left || split_dir == ImGuiDir.Up) ? 0 : 1;
        outIdAtDir = node.ChildNodes[atDirIdx]!.ID;
        outIdAtOppositeDir = node.ChildNodes[atDirIdx ^ 1]!.ID;
        return outIdAtDir;
    }

    /// <summary>
    /// Copies a dock node hierarchy from a source node to a destination node.
    /// </summary>
    /// <param name="src_node_id">Source node ID.</param>
    /// <param name="dst_node_id">Destination node ID.</param>
    /// <param name="out_node_remap_pairs">Pairs mapping source IDs to cloned IDs.</param>
    public static void DockBuilderCopyNode(uint src_node_id, uint dst_node_id, void* out_node_remap_pairs)
    {
        DockBuilderCopyNode(src_node_id, dst_node_id, (List<uint>?)null);
    }

    /// <summary>
    /// Copies a dock node hierarchy from a source node to a destination node.
    /// </summary>
    /// <param name="src_node_id">Source node ID.</param>
    /// <param name="dst_node_id">Destination node ID.</param>
    /// <param name="out_node_remap_pairs">Optional list receiving mapping pairs (src, dst).</param>
    public static void DockBuilderCopyNode(uint src_node_id, uint dst_node_id, List<uint>? out_node_remap_pairs = null)
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check(src_node_id != 0 && dst_node_id != 0);

        DockBuilderRemoveNode(dst_node_id);
        ImGuiDockNode? srcNode = DockContextFindNodeByID(g, src_node_id);
        if (srcNode == null)
            return;

        out_node_remap_pairs ??= new List<uint>();
        out_node_remap_pairs.Clear();
        DockBuilderCopyNodeRec(srcNode, dst_node_id, out_node_remap_pairs);
    }

    /// <summary>
    /// Recursively copies a dock node hierarchy from a source node to a destination node, recording remapped node identifiers.
    /// </summary>
    /// <param name="srcNode">The source node hierarchy to clone.</param>
    /// <param name="dstNodeIdIfKnown">The explicit destination node identifier, or zero to generate a new identifier.</param>
    /// <param name="outRemapPairs">List receiving pairs of old and new node identifiers.</param>
    /// <returns>The root of the newly created dock node hierarchy.</returns>
    private static ImGuiDockNode DockBuilderCopyNodeRec(ImGuiDockNode srcNode, uint dstNodeIdIfKnown, List<uint> outRemapPairs)
    {
        ImGuiContext g = GImGui!;
        ImGuiDockNode dstNode = DockContextAddNode(g, dstNodeIdIfKnown);
        dstNode.SharedFlags = srcNode.SharedFlags;
        dstNode.LocalFlags = srcNode.LocalFlags;
        dstNode.LocalFlagsInWindows = ImGuiDockNodeFlags.None;
        dstNode.Pos = srcNode.Pos;
        dstNode.Size = srcNode.Size;
        dstNode.SizeRef = srcNode.SizeRef;
        dstNode.SplitAxis = srcNode.SplitAxis;
        dstNode.UpdateMergedFlags();

        outRemapPairs.Add(srcNode.ID);
        outRemapPairs.Add(dstNode.ID);

        for (int childN = 0; childN < 2; childN++)
        {
            if (srcNode.ChildNodes[childN] is { } childSrc)
            {
                dstNode.ChildNodes[childN] = DockBuilderCopyNodeRec(childSrc, 0, outRemapPairs);
                dstNode.ChildNodes[childN]!.ParentNode = dstNode;
            }
        }
        return dstNode;
    }

    /// <summary>
    /// Copies window position and size settings from one window name to another.
    /// </summary>
    /// <param name="src_name">Source window name.</param>
    /// <param name="dst_name">Destination window name.</param>
    public static void DockBuilderCopyWindowSettings(string src_name, string dst_name)
    {
        if (FindWindowByName(src_name) is { } srcWindow)
        {
            if (FindWindowByName(dst_name) is { } dstWindow)
            {
                dstWindow.Pos = srcWindow.Pos;
                dstWindow.Size = srcWindow.Size;
                dstWindow.SizeFull = srcWindow.SizeFull;
                dstWindow.Collapsed = srcWindow.Collapsed;
            }
            else
            {
                ImGuiWindowSettings? dstSettings = FindWindowSettingsByID(ImHashStr(dst_name));
                dstSettings ??= CreateNewWindowSettings(dst_name);
                dstSettings.Pos = new ImVec2ih((short)srcWindow.Pos.X, (short)srcWindow.Pos.Y);
                dstSettings.Size = new ImVec2ih((short)srcWindow.SizeFull.X, (short)srcWindow.SizeFull.Y);
                dstSettings.Collapsed = srcWindow.Collapsed;
            }
        }
    }

    /// <summary>
    /// Duplicates an entire dockspace hierarchy and remaps docked window names.
    /// </summary>
    /// <param name="src_dockspace_id">Source dockspace ID.</param>
    /// <param name="dst_dockspace_id">Destination dockspace ID.</param>
    /// <param name="in_window_remap_pairs">Array of (src_name, dst_name) pairs.</param>
    public static void DockBuilderCopyDockSpace(uint src_dockspace_id, uint dst_dockspace_id, void* in_window_remap_pairs)
    {
        DockBuilderCopyDockSpace(src_dockspace_id, dst_dockspace_id, (List<string>?)null);
    }

    /// <summary>
    /// Duplicates an entire dockspace hierarchy and remaps docked window names.
    /// </summary>
    /// <param name="src_dockspace_id">Source dockspace ID.</param>
    /// <param name="dst_dockspace_id">Destination dockspace ID.</param>
    /// <param name="in_window_remap_pairs">List of (src_name, dst_name) pairs.</param>
    public static void DockBuilderCopyDockSpace(uint src_dockspace_id, uint dst_dockspace_id, List<string>? in_window_remap_pairs = null)
    {
        List<uint> nodeRemapPairs = new();
        DockBuilderCopyNode(src_dockspace_id, dst_dockspace_id, nodeRemapPairs);

        if (in_window_remap_pairs != null)
        {
            for (int i = 0; i < in_window_remap_pairs.Count - 1; i += 2)
            {
                string srcName = in_window_remap_pairs[i];
                string dstName = in_window_remap_pairs[i + 1];
                uint srcWinId = ImHashStr(srcName);
                uint srcDockId = FindWindowByID(srcWinId)?.DockId ?? FindWindowSettingsByID(srcWinId)?.DockId ?? 0;
                uint dstDockId = 0;

                for (int n = 0; n < nodeRemapPairs.Count - 1; n += 2)
                {
                    if (nodeRemapPairs[n] == srcDockId)
                    {
                        dstDockId = nodeRemapPairs[n + 1];
                        break;
                    }
                }

                if (dstDockId != 0)
                    DockBuilderDockWindow(dstName, dstDockId);
                else
                    DockBuilderCopyWindowSettings(srcName, dstName);
            }
        }
    }

    /// <summary>
    /// Finishes building a dock node hierarchy.
    /// </summary>
    /// <param name="node_id">Root node identifier.</param>
    public static void DockBuilderFinish(uint node_id)
    {
        DockContextBuildAddWindowsToNodes(GImGui!, node_id);
    }

    #endregion

    #region [SECTION] DOCKING: Visual Utilities & Debug

    /// <summary>
    /// Renders the small downward triangle arrow used for dock menu buttons.
    /// </summary>
    /// <param name="draw_list">Draw list to render into.</param>
    /// <param name="p_min">Upper-left corner.</param>
    /// <param name="sz">Button font size.</param>
    /// <param name="col">Color.</param>
    public static void RenderArrowDockMenu(ImDrawList draw_list, ImVec2 p_min, float sz, uint col)
    {
        draw_list.AddRectFilled(p_min + new ImVec2(sz * 0.20f, sz * 0.15f), p_min + new ImVec2(sz * 0.80f, sz * 0.30f), col);
        RenderArrowPointingAt(draw_list, p_min + new ImVec2(sz * 0.50f, sz * 0.85f), new ImVec2(sz * 0.30f, sz * 0.40f), ImGuiDir.Down, col);
    }

    /// <summary>
    /// Renders debug tree inspection for a dock node.
    /// </summary>
    /// <param name="node">Node to inspect.</param>
    /// <param name="label">Display label.</param>
    public static void DebugNodeDockNode(ImGuiDockNode node, string label)
    {
        ImGuiContext g = GImGui!;
        bool isAlive = (g.FrameCount - node.LastFrameAlive < 2);
        bool isActive = (g.FrameCount - node.LastFrameActive < 2);
        if (!isAlive) PushStyleColor(ImGuiCol.Text, GetStyleColorVec4(ImGuiCol.TextDisabled));

        ImGuiTreeNodeFlags flags = node.IsFocused ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None;
        bool open;
        if (node.Windows.Count > 0)
            open = TreeNodeEx(label, flags, $"{label} 0x{node.ID:X04}{(node.IsVisible ? "" : " (hidden)")}: {node.Windows.Count} windows");
        else
            open = TreeNodeEx(label, flags, $"{label} 0x{node.ID:X04}{(node.IsVisible ? "" : " (hidden)")}: {(node.SplitAxis == ImGuiAxis.X ? "horizontal split" : node.SplitAxis == ImGuiAxis.Y ? "vertical split" : "empty")}");
        if (!isAlive) PopStyleColor();

        if (open)
        {
            BulletText($"Pos ({node.Pos.X:F0},{node.Pos.Y:F0}), Size ({node.Size.X:F0}, {node.Size.Y:F0})");
            BulletText($"SelectedTabID: 0x{node.SelectedTabId:X08}, LastFocusedNodeID: 0x{node.LastFocusedNodeId:X08}");
            if (node.ParentNode != null)
                DebugNodeDockNode(node.ParentNode, "ParentNode");
            if (node.ChildNodes[0] != null)
                DebugNodeDockNode(node.ChildNodes[0]!, "Child[0]");
            if (node.ChildNodes[1] != null)
                DebugNodeDockNode(node.ChildNodes[1]!, "Child[1]");
            TreePop();
        }
    }

    #endregion

    #region [SECTION] DOCKING: Supporting Widget & Window Moving Helpers

    /// <summary>
    /// Submits the window collapse button, which also doubles as the dock menu button.
    /// </summary>
    /// <param name="id">Item identifier.</param>
    /// <param name="pos">Screen position.</param>
    /// <param name="dockNode">Associated dock node, if any.</param>
    /// <returns><c>true</c> if pressed; otherwise <c>false</c>.</returns>
    public static bool CollapseButton(uint id, ImVec2 pos, ImGuiDockNode? dockNode = null)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;

        ImRect bb = new(pos, pos + new ImVec2(g.FontSize, g.FontSize));
        bool isClipped = !ItemAdd(bb, id);
        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held, ImGuiButtonFlags.None);
        if (isClipped)
            return pressed;

        uint bgCol = GetColorU32((held && hovered) ? ImGuiCol.ButtonActive : hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
        uint textCol = GetColorU32(ImGuiCol.Text);
        if (hovered || held)
            window.DrawList.AddRectFilled(bb.Min, bb.Max, bgCol);
        RenderNavCursor(bb, id, ImGuiNavRenderCursorFlags.Compact);

        if (dockNode != null)
            RenderArrowDockMenu(window.DrawList, bb.Min, g.FontSize, textCol);
        else
            RenderArrow(window.DrawList, bb.Min, textCol, window.Collapsed ? ImGuiDir.Right : ImGuiDir.Down, 1.0f);

        if (IsItemActive() && IsMouseDragging(ImGuiMouseButton.Left))
            StartMouseMovingWindowOrNode(window, dockNode, true);

        return pressed;
    }

    /// <summary>
    /// Initiates mouse movement for a window or an entire dock node hierarchy.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="node">Target dock node.</param>
    /// <param name="undock">Whether dragging initiates undocking.</param>
    internal static void StartMouseMovingWindowOrNode(ImGuiWindow window, ImGuiDockNode? node, bool undock)
    {
        ImGuiContext g = GImGui!;
        bool canUndockNode = false;
        if (undock && node != null && node.VisibleWindow != null && (node.VisibleWindow.Flags & ImGuiWindowFlags.NoMove) == 0 && (node.MergedFlags & ImGuiDockNodeFlags.NoUndocking) == 0)
        {
            ImGuiDockNode rootNode = DockNodeGetRootNode(node);
            if (rootNode.OnlyNodeWithWindows != node || rootNode.CentralNode != null)
                canUndockNode = true;
        }

        bool clicked = IsMouseClicked(ImGuiMouseButton.Left);
        bool dragging = IsMouseDragging(ImGuiMouseButton.Left);
        if (canUndockNode && dragging)
            DockContextQueueUndockNode(g, node!);
        else if (!canUndockNode && (clicked || dragging) && g.MovingWindow != window)
            StartMouseMovingWindow(window);
    }

    /// <summary>
    /// Evaluates whether a window always wants its own tab bar even when single and floating.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <returns><c>true</c> if the window requires its own tab bar; otherwise <c>false</c>.</returns>
    internal static bool GetWindowAlwaysWantOwnTabBar(ImGuiWindow window)
    {
        ImGuiContext g = GImGui!;
        if (g.IO.ConfigDockingAlwaysTabBar || window.WindowClass.DockingAlwaysTabBar)
            if ((window.Flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDocking)) == 0)
                if (!window.IsFallbackWindow)
                    return true;
        return false;
    }

    /// <summary>
    /// Implements interactive splitter widget behavior for resizing two adjacent panels along an axis.
    /// </summary>
    /// <param name="bb">Bounding box of the splitter bar.</param>
    /// <param name="id">Widget identifier for the splitter.</param>
    /// <param name="axis">Split axis.</param>
    /// <param name="size1">Size of the first pane, updated on drag.</param>
    /// <param name="size2">Size of the second pane, updated on drag.</param>
    /// <param name="minSize1">Minimum allowed size of the first pane.</param>
    /// <param name="minSize2">Minimum allowed size of the second pane.</param>
    /// <param name="hoverExtend">Amount to expand interaction rectangle.</param>
    /// <param name="hoverVisibilityDelay">Delay before displaying hovered highlight.</param>
    /// <param name="bgCol">Optional background color fill.</param>
    /// <returns><c>true</c> if active and being dragged; otherwise <c>false</c>.</returns>
    public static bool SplitterBehavior(ImRect bb, uint id, ImGuiAxis axis, ref float size1, ref float size2, float minSize1, float minSize2, float hoverExtend = 0.0f, float hoverVisibilityDelay = 0.0f, uint bgCol = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;

        if (!ItemAdd(bb, id, null, ImGuiItemFlags.NoNav))
            return false;

        ImGuiButtonFlags buttonFlags = ImGuiButtonFlags.AllowOverlap;
        ImRect bbInteract = bb;
        bbInteract.Expand(axis == ImGuiAxis.Y ? new ImVec2(0.0f, hoverExtend) : new ImVec2(hoverExtend, 0.0f));
        ButtonBehavior(bbInteract, id, out bool hovered, out bool held, buttonFlags);
        if (hovered)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredRect;

        if (held || (hovered && g.HoveredIdPreviousFrame == id && g.HoveredIdTimer >= hoverVisibilityDelay))
            SetMouseCursor(axis == ImGuiAxis.Y ? ImGuiMouseCursor.ResizeNS : ImGuiMouseCursor.ResizeEW);

        ImRect bbRender = bb;
        if (held)
        {
            float mouseDelta = (g.IO.MousePos - g.ActiveIdClickOffset - bbInteract.Min)[axis];
            float size1MaxDelta = MathF.Max(0.0f, size1 - minSize1);
            float size2MaxDelta = MathF.Max(0.0f, size2 - minSize2);
            if (mouseDelta < -size1MaxDelta) mouseDelta = -size1MaxDelta;
            if (mouseDelta > size2MaxDelta) mouseDelta = size2MaxDelta;

            if (mouseDelta != 0.0f)
            {
                size1 = MathF.Max(size1 + mouseDelta, minSize1);
                size2 = MathF.Max(size2 - mouseDelta, minSize2);
                bbRender.Translate(axis == ImGuiAxis.X ? new ImVec2(mouseDelta, 0.0f) : new ImVec2(0.0f, mouseDelta));
                MarkItemEdited(id);
            }
        }

        if ((bgCol & 0xFF000000) != 0)
            window.DrawList.AddRectFilled(bbRender.Min, bbRender.Max, bgCol, 0.0f);
        uint col = GetColorU32(held ? ImGuiCol.SeparatorActive : (hovered && g.HoveredIdTimer >= hoverVisibilityDelay) ? ImGuiCol.SeparatorHovered : ImGuiCol.Separator);
        window.DrawList.AddRectFilled(bbRender.Min, bbRender.Max, col, 0.0f);

        return held;
    }

    #endregion

    #region [SECTION] DOCKING: Internal Helpers & Tree Management

    /// <summary>
    /// Traverses upwards through the dock node hierarchy to find the root node.
    /// </summary>
    /// <param name="node">The dock node whose root to locate.</param>
    /// <returns>The topmost root <see cref="ImGuiDockNode"/> in the hierarchy.</returns>
    internal static ImGuiDockNode DockNodeGetRootNode(ImGuiDockNode node)
    {
        while (node.ParentNode != null)
            node = node.ParentNode;
        return node;
    }

    /// <summary>
    /// Determines whether a dock node is a descendant of the specified ancestor node.
    /// </summary>
    /// <param name="node">The node to evaluate.</param>
    /// <param name="ancestor">The prospective ancestor node.</param>
    /// <returns><see langword="true"/> if <paramref name="node"/> is within the hierarchy of <paramref name="ancestor"/>; otherwise, <see langword="false"/>.</returns>
    internal static bool DockNodeIsInHierarchyOf(ImGuiDockNode node, ImGuiDockNode ancestor)
    {
        while (node != null)
        {
            if (node == ancestor)
                return true;
            node = node.ParentNode!;
        }
        return false;
    }

    /// <summary>
    /// Calculates the hierarchy depth of a dock node relative to the root.
    /// </summary>
    /// <param name="node">The dock node to inspect.</param>
    /// <returns>The zero-based depth of the node within its dock tree.</returns>
    internal static int DockNodeGetDepth(ImGuiDockNode node)
    {
        int depth = 0;
        while (node.ParentNode != null)
        {
            depth++;
            node = node.ParentNode;
        }
        return depth;
    }

    /// <summary>
    /// Generates a unique UI element identifier for the window menu button within a dock node.
    /// </summary>
    /// <param name="node">The dock node.</param>
    /// <returns>The 32-bit hashed identifier for the window menu button.</returns>
    internal static uint DockNodeGetWindowMenuButtonId(ImGuiDockNode node)
    {
        return ImHashStr("#COLLAPSE", 0, node.ID);
    }

    /// <summary>
    /// Allocates and registers a new dock node with the specified identifier in the context.
    /// </summary>
    /// <param name="ctx">The ImGui context owning the dock manager.</param>
    /// <param name="id">The unique identifier to assign to the new node, or zero to generate one.</param>
    /// <returns>The newly created <see cref="ImGuiDockNode"/>.</returns>
    internal static ImGuiDockNode DockContextAddNode(ImGuiContext ctx, uint id)
    {
        if (id == 0)
            id = DockContextGenNodeID(ctx);
        else
            ImAssert.Check(DockContextFindNodeByID(ctx, id) == null);

        ImGuiDockNode node = new(id);
        ctx.DockContext.Nodes[node.ID] = node;
        return node;
    }

    /// <summary>
    /// Removes a dock node from the context, optionally collapsing its sibling into the parent node.
    /// </summary>
    /// <param name="ctx">The ImGui context owning the dock manager.</param>
    /// <param name="node">The dock node to remove.</param>
    /// <param name="mergeSiblingIntoParent"><see langword="true"/> to merge remaining sibling hierarchy into parent; otherwise, <see langword="false"/>.</param>
    internal static void DockContextRemoveNode(ImGuiContext ctx, ImGuiDockNode node, bool mergeSiblingIntoParent)
    {
        ImAssert.Check(DockContextFindNodeByID(ctx, node.ID) == node);
        ImAssert.Check(node.ChildNodes[0] == null && node.ChildNodes[1] == null);
        ImAssert.Check(node.Windows.Count == 0);

        if (node.HostWindow != null)
            node.HostWindow.DockNodeAsHost = null;

        ImGuiDockNode? parentNode = node.ParentNode;
        bool merge = mergeSiblingIntoParent && parentNode != null;
        if (merge)
        {
            ImAssert.Check(parentNode!.ChildNodes[0] == node || parentNode.ChildNodes[1] == node);
            ImGuiDockNode siblingNode = (parentNode.ChildNodes[0] == node ? parentNode.ChildNodes[1] : parentNode.ChildNodes[0])!;
            DockNodeTreeMerge(ctx, parentNode, siblingNode);
        }
        else
        {
            if (parentNode != null)
            {
                for (int n = 0; n < parentNode.ChildNodes.Length; n++)
                    if (parentNode.ChildNodes[n] == node)
                        parentNode.ChildNodes[n] = null;
            }
            DockContextDeleteNode(ctx, node);
        }
    }

    /// <summary>
    /// Completely removes and frees a dock node and its children from the context.
    /// </summary>
    /// <param name="ctx">The ImGui context owning the dock manager.</param>
    /// <param name="node">The dock node to delete.</param>
    internal static void DockContextDeleteNode(ImGuiContext ctx, ImGuiDockNode node)
    {
        node.TabBar = null;
        ctx.DockContext.Nodes.Remove(node.ID);
    }

    /// <summary>
    /// Processes a pending docking or undocking request queued on the context.
    /// </summary>
    /// <param name="ctx">The ImGui context owning the docking subsystem.</param>
    /// <param name="req">The docking request to execute.</param>
    internal static void DockContextProcessDock(ImGuiContext ctx, ImGuiDockRequest req)
    {
        ImGuiWindow? payloadWindow = req.DockPayload;
        ImGuiWindow? targetWindow = req.DockTargetWindow;
        ImGuiDockNode? node = req.DockTargetNode;

        uint nextSelectedId = 0;
        ImGuiDockNode? payloadNode = null;
        if (payloadWindow != null)
        {
            payloadNode = payloadWindow.DockNodeAsHost;
            payloadWindow.DockNodeAsHost = null;
            if (payloadNode != null && payloadNode.IsLeafNode() && payloadNode.TabBar != null)
                nextSelectedId = payloadNode.TabBar.NextSelectedTabId != 0 ? payloadNode.TabBar.NextSelectedTabId : payloadNode.TabBar.SelectedTabId;
            if (payloadNode == null)
                nextSelectedId = payloadWindow.TabId;
        }

        if (node == null && targetWindow != null)
        {
            node = DockContextAddNode(ctx, 0);
            node.Pos = targetWindow.Pos;
            node.Size = targetWindow.Size;
            if (targetWindow.DockNodeAsHost == null)
            {
                DockNodeAddWindow(node, targetWindow, true);
                if (node.TabBar != null && node.TabBar.Tabs.Count > 0)
                    node.TabBar.Tabs[0].Flags &= ~ImGuiTabItemFlags.Unsorted;
                targetWindow.DockIsActive = true;
            }
        }

        if (node == null)
            return;

        ImGuiDir splitDir = req.DockSplitDir;
        if (splitDir != ImGuiDir.None)
        {
            ImGuiAxis splitAxis = (splitDir == ImGuiDir.Left || splitDir == ImGuiDir.Right) ? ImGuiAxis.X : ImGuiAxis.Y;
            int splitInheritorChildIdx = (splitDir == ImGuiDir.Left || splitDir == ImGuiDir.Up) ? 1 : 0;
            float splitRatio = req.DockSplitRatio;
            DockNodeTreeSplit(ctx, node, splitAxis, splitInheritorChildIdx, splitRatio, payloadNode);
            ImGuiDockNode newNode = node.ChildNodes[splitInheritorChildIdx ^ 1]!;
            newNode.HostWindow = node.HostWindow;
            node = newNode;
        }
        node.SetLocalFlags(node.LocalFlags & ~(ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_HiddenTabBar);

        if (node != payloadNode)
        {
            if (node.Windows.Count > 0 && node.TabBar == null)
            {
                DockNodeAddTabBar(node);
                for (int n = 0; n < node.Windows.Count; n++)
                    TabBarAddTab(node.TabBar!, ImGuiTabItemFlags.None, node.Windows[n]);
            }

            if (payloadNode != null)
            {
                if (payloadNode.IsSplitNode())
                {
                    if (node.Windows.Count > 0)
                    {
                        ImGuiDockNode visibleNode = payloadNode.OnlyNodeWithWindows!;
                        DockNodeMoveWindows(node, visibleNode);
                        DockNodeMoveWindows(visibleNode, node);
                        DockSettingsRenameNodeReferences(node.ID, visibleNode.ID);
                    }
                    if (node.IsCentralNode())
                    {
                        ImGuiDockNode? lastFocusedNode = DockContextFindNodeByID(ctx, payloadNode.LastFocusedNodeId);
                        if (lastFocusedNode != null)
                        {
                            ImGuiDockNode lastFocusedRoot = DockNodeGetRootNode(lastFocusedNode);
                            lastFocusedNode.SetLocalFlags(lastFocusedNode.LocalFlags | (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode);
                            node.SetLocalFlags(node.LocalFlags & ~(ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode);
                            lastFocusedRoot.CentralNode = lastFocusedNode;
                        }
                    }
                    DockNodeMoveChildNodes(node, payloadNode);
                }
                else
                {
                    uint payloadDockId = payloadNode.ID;
                    DockNodeMoveWindows(node, payloadNode);
                    DockSettingsRenameNodeReferences(payloadDockId, node.ID);
                }
                DockContextRemoveNode(ctx, payloadNode, true);
            }
            else if (payloadWindow != null)
            {
                uint payloadDockId = payloadWindow.DockId;
                node.VisibleWindow = payloadWindow;
                DockNodeAddWindow(node, payloadWindow, true);
                if (payloadDockId != 0)
                    DockSettingsRenameNodeReferences(payloadDockId, node.ID);
            }
        }
        else
        {
            node.WantHiddenTabBarUpdate = true;
        }

        if (node.TabBar != null)
            node.TabBar.NextSelectedTabId = nextSelectedId;
        MarkIniSettingsDirty();
    }

    /// <summary>
    /// Binds a window to its corresponding dock node based on window dock state or settings.
    /// </summary>
    /// <param name="ctx">The ImGui context.</param>
    /// <param name="window">The window to associate with a dock node.</param>
    /// <returns>The bound <see cref="ImGuiDockNode"/>, or <see langword="null"/> if unassigned.</returns>
    internal static ImGuiDockNode? DockContextBindNodeToWindow(ImGuiContext ctx, ImGuiWindow window)
    {
        ImGuiDockNode? node = DockContextFindNodeByID(ctx, window.DockId);
        ImAssert.Check(window.DockNode == null);

        if (node != null && node.IsSplitNode())
        {
            DockContextProcessUndockWindow(ctx, window);
            return null;
        }

        if (node == null)
        {
            node = DockContextAddNode(ctx, window.DockId);
            node.AuthorityForPos = node.AuthorityForSize = node.AuthorityForViewport = (byte)ImGuiDataAuthority.Window;
            node.LastFrameAlive = ctx.FrameCount;
        }

        if (!node.IsVisible)
        {
            ImGuiDockNode? ancestorNode = node;
            while (!ancestorNode.IsVisible && ancestorNode.ParentNode != null)
                ancestorNode = ancestorNode.ParentNode;
            if (ancestorNode.Size.X > 0.0f && ancestorNode.Size.Y > 0.0f)
            {
                DockNodeUpdateHasCentralNodeChild(DockNodeGetRootNode(ancestorNode));
                DockNodeTreeUpdatePosSize(ancestorNode, ancestorNode.Pos, ancestorNode.Size, node);
            }
        }

        bool nodeWasVisible = node.IsVisible;
        DockNodeAddWindow(node, window, true);
        node.IsVisible = nodeWasVisible;
        return node;
    }

    /// <summary>
    /// Adds a window to a dock node, optionally appending it to the node's tab bar.
    /// </summary>
    /// <param name="node">The target dock node.</param>
    /// <param name="window">The window to add.</param>
    /// <param name="addToTabBar"><see langword="true"/> to register the window in the node tab bar; otherwise, <see langword="false"/>.</param>
    internal static void DockNodeAddWindow(ImGuiDockNode node, ImGuiWindow window, bool addToTabBar)
    {
        ImAssert.Check(window.DockNode == null);
        window.DockNode = node;
        window.DockId = node.ID;
        node.Windows.Add(window);

        if (addToTabBar)
        {
            if (node.TabBar == null)
                DockNodeAddTabBar(node);
            if (TabBarFindTabByID(node.TabBar!, window.TabId) == null)
                TabBarAddTab(node.TabBar!, ImGuiTabItemFlags.Unsorted, window);
        }

        if (node.Windows.Count == 1)
        {
            node.VisibleWindow = window;
            if (node.AuthorityForPos == (byte)ImGuiDataAuthority.Auto)
                node.AuthorityForPos = (byte)ImGuiDataAuthority.DockNode;
            if (node.AuthorityForSize == (byte)ImGuiDataAuthority.Auto)
                node.AuthorityForSize = (byte)ImGuiDataAuthority.DockNode;
            if (node.AuthorityForViewport == (byte)ImGuiDataAuthority.Auto)
                node.AuthorityForViewport = (byte)ImGuiDataAuthority.DockNode;
        }

        ImGuiDockNode rootNode = DockNodeGetRootNode(node);
        if (node.Windows.Count == 2)
        {
            rootNode.LastFocusedNodeId = node.ID;
        }
        DockNodeUpdateFlagsAndCollapse(rootNode);
    }

    /// <summary>
    /// Removes a window from a dock node and updates active tab state.
    /// </summary>
    /// <param name="node">The source dock node.</param>
    /// <param name="window">The window to remove.</param>
    /// <param name="saveDockId">The identifier to record on the window as its last dock ID.</param>
    internal static void DockNodeRemoveWindow(ImGuiDockNode node, ImGuiWindow window, uint saveDockId)
    {
        ImAssert.Check(window.DockNode == node);
        window.DockNode = null;
        window.DockIsActive = false;
        window.DockNodeIsVisible = false;
        window.DockTabIsVisible = false;
        window.DockId = saveDockId;
        window.DockOrder = -1;

        node.Windows.Remove(window);
        if (node.TabBar != null)
            TabBarRemoveTab(node.TabBar, window.TabId);

        if (node.VisibleWindow == window)
            node.VisibleWindow = node.Windows.Count > 0 ? node.Windows[0] : null;

        if (node.Windows.Count == 0 && node.TabBar != null)
            DockNodeRemoveTabBar(node);

        ImGuiDockNode rootNode = DockNodeGetRootNode(node);
        DockNodeUpdateFlagsAndCollapse(rootNode);
    }

    /// <summary>
    /// Transfers all windows hosted in a source dock node to a destination dock node.
    /// </summary>
    /// <param name="dstNode">The destination node receiving windows.</param>
    /// <param name="srcNode">The source node relinquishing windows.</param>
    internal static void DockNodeMoveWindows(ImGuiDockNode dstNode, ImGuiDockNode srcNode)
    {
        for (int i = 0; i < srcNode.Windows.Count; i++)
        {
            ImGuiWindow window = srcNode.Windows[i];
            window.DockNode = dstNode;
            window.DockId = dstNode.ID;
            dstNode.Windows.Add(window);
        }
        srcNode.Windows.Clear();

        if (srcNode.TabBar != null)
        {
            if (dstNode.TabBar == null)
                DockNodeAddTabBar(dstNode);
            for (int i = 0; i < srcNode.TabBar.Tabs.Count; i++)
                dstNode.TabBar!.Tabs.Add(srcNode.TabBar.Tabs[i]);
            DockNodeRemoveTabBar(srcNode);
        }

        if (dstNode.VisibleWindow == null)
            dstNode.VisibleWindow = srcNode.VisibleWindow;
        srcNode.VisibleWindow = null;
    }

    /// <summary>
    /// Transfers child split nodes from a source node to a destination node.
    /// </summary>
    /// <param name="dstNode">The destination node.</param>
    /// <param name="srcNode">The source node.</param>
    internal static void DockNodeMoveChildNodes(ImGuiDockNode dstNode, ImGuiDockNode srcNode)
    {
        ImAssert.Check(dstNode.ChildNodes[0] == null && dstNode.ChildNodes[1] == null);
        dstNode.ChildNodes[0] = srcNode.ChildNodes[0];
        dstNode.ChildNodes[1] = srcNode.ChildNodes[1];
        srcNode.ChildNodes[0] = null;
        srcNode.ChildNodes[1] = null;
        if (dstNode.ChildNodes[0] != null) dstNode.ChildNodes[0]!.ParentNode = dstNode;
        if (dstNode.ChildNodes[1] != null) dstNode.ChildNodes[1]!.ParentNode = dstNode;
        dstNode.SplitAxis = srcNode.SplitAxis;
        srcNode.SplitAxis = ImGuiAxis.None;
    }

    /// <summary>
    /// Propagates the dock node's position and size to all member windows.
    /// </summary>
    /// <param name="node">The dock node whose dimensions to apply.</param>
    internal static void DockNodeApplyPosSizeToWindows(ImGuiDockNode node)
    {
        for (int i = 0; i < node.Windows.Count; i++)
        {
            ImGuiWindow window = node.Windows[i];
            SetWindowPos(window, node.Pos, ImGuiCond.Always);
            SetWindowSize(window, node.Size, ImGuiCond.Always);
        }
    }

    /// <summary>
    /// Hides the host container window representing the specified dock node.
    /// </summary>
    /// <param name="node">The dock node whose host window will be hidden.</param>
    internal static void DockNodeHideHostWindow(ImGuiDockNode node)
    {
        if (node.HostWindow != null)
        {
            node.HostWindow.Hidden = true;
            node.State = ImGuiDockNodeState.HostWindowHiddenBecauseWindowsAreResizing;
        }
    }

    /// <summary>
    /// Temporarily hides a window while its host docking container is being constructed.
    /// </summary>
    /// <param name="window">The window to hide during host creation.</param>
    internal static void DockNodeHideWindowDuringHostWindowCreation(ImGuiWindow window)
    {
        window.Hidden = true;
        window.HiddenFramesCannotSkipItems = 1;
    }

    /// <summary>
    /// Splits an existing dock node into two child branches along the specified axis.
    /// </summary>
    /// <param name="ctx">The ImGui context.</param>
    /// <param name="parentNode">The node to divide into children.</param>
    /// <param name="splitAxis">The axis along which to divide the space.</param>
    /// <param name="splitInheritorChildIdx">Child index inheriting existing windows (0 or 1).</param>
    /// <param name="splitRatio">The proportional size ratio allocated to the split.</param>
    /// <param name="newNode">An optional pre-allocated node to install as the sibling child.</param>
    internal static void DockNodeTreeSplit(ImGuiContext ctx, ImGuiDockNode parentNode, ImGuiAxis splitAxis, int splitInheritorChildIdx, float splitRatio, ImGuiDockNode? newNode)
    {
        ImGuiContext g = ctx;
        ImAssert.Check(splitAxis != ImGuiAxis.None);

        ImGuiDockNode child0 = (newNode != null && splitInheritorChildIdx != 0) ? newNode : DockContextAddNode(ctx, 0);
        child0.ParentNode = parentNode;

        ImGuiDockNode child1 = (newNode != null && splitInheritorChildIdx != 1) ? newNode : DockContextAddNode(ctx, 0);
        child1.ParentNode = parentNode;

        ImGuiDockNode childInheritor = (splitInheritorChildIdx == 0) ? child0 : child1;
        DockNodeMoveChildNodes(childInheritor, parentNode);
        parentNode.ChildNodes[0] = child0;
        parentNode.ChildNodes[1] = child1;
        parentNode.ChildNodes[splitInheritorChildIdx]!.VisibleWindow = parentNode.VisibleWindow;
        parentNode.SplitAxis = splitAxis;
        parentNode.VisibleWindow = null;
        parentNode.AuthorityForPos = parentNode.AuthorityForSize = (byte)ImGuiDataAuthority.DockNode;

        float sizeAvail = parentNode.Size[splitAxis] - g.Style.DockingSeparatorSize;
        sizeAvail = MathF.Max(sizeAvail, g.Style.WindowMinSize[splitAxis] * 2.0f);
        ImAssert.Check(sizeAvail > 0.0f);
        child0.SizeRef = child1.SizeRef = parentNode.Size;
        child0.SizeRef[splitAxis] = MathF.Truncate(sizeAvail * splitRatio);
        child1.SizeRef[splitAxis] = MathF.Truncate(sizeAvail - child0.SizeRef[splitAxis]);

        DockNodeMoveWindows(parentNode.ChildNodes[splitInheritorChildIdx]!, parentNode);
        DockSettingsRenameNodeReferences(parentNode.ID, parentNode.ChildNodes[splitInheritorChildIdx]!.ID);
        DockNodeUpdateHasCentralNodeChild(DockNodeGetRootNode(parentNode));
        DockNodeTreeUpdatePosSize(parentNode, parentNode.Pos, parentNode.Size);

        child0.SharedFlags = (ImGuiDockNodeFlags)((uint)parentNode.SharedFlags & (uint)ImGuiDockNodeFlagsPrivate.SharedFlagsInheritMask_);
        child1.SharedFlags = (ImGuiDockNodeFlags)((uint)parentNode.SharedFlags & (uint)ImGuiDockNodeFlagsPrivate.SharedFlagsInheritMask_);
        childInheritor.LocalFlags = (ImGuiDockNodeFlags)((uint)parentNode.LocalFlags & (uint)ImGuiDockNodeFlagsPrivate.LocalFlagsTransferMask_);
        parentNode.LocalFlags = (ImGuiDockNodeFlags)((uint)parentNode.LocalFlags & ~(uint)ImGuiDockNodeFlagsPrivate.LocalFlagsTransferMask_);
        child0.UpdateMergedFlags();
        child1.UpdateMergedFlags();
        parentNode.UpdateMergedFlags();
        if (childInheritor.IsCentralNode())
            DockNodeGetRootNode(parentNode).CentralNode = childInheritor;
    }

    /// <summary>
    /// Merges child split nodes back into their parent dock node.
    /// </summary>
    /// <param name="ctx">The ImGui context.</param>
    /// <param name="parentNode">The parent node being collapsed.</param>
    /// <param name="mergeLeadChild">The remaining child node whose windows and state are promoted.</param>
    internal static void DockNodeTreeMerge(ImGuiContext ctx, ImGuiDockNode parentNode, ImGuiDockNode mergeLeadChild)
    {
        ImGuiDockNode? child0 = parentNode.ChildNodes[0];
        ImGuiDockNode? child1 = parentNode.ChildNodes[1];
        ImAssert.Check(child0 != null || child1 != null);

        ImVec2 backupSize = parentNode.SizeRef;
        DockNodeMoveChildNodes(parentNode, mergeLeadChild);
        if (child0 != null)
        {
            DockNodeMoveWindows(parentNode, child0);
            DockSettingsRenameNodeReferences(child0.ID, parentNode.ID);
        }
        if (child1 != null)
        {
            DockNodeMoveWindows(parentNode, child1);
            DockSettingsRenameNodeReferences(child1.ID, parentNode.ID);
        }
        DockNodeApplyPosSizeToWindows(parentNode);
        parentNode.AuthorityForPos = parentNode.AuthorityForSize = parentNode.AuthorityForViewport = (byte)ImGuiDataAuthority.Auto;
        parentNode.VisibleWindow = mergeLeadChild.VisibleWindow;
        parentNode.SizeRef = backupSize;

        parentNode.LocalFlags &= ~(ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.LocalFlagsTransferMask_;
        if (child0 != null) parentNode.LocalFlags |= child0.LocalFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.LocalFlagsTransferMask_;
        if (child1 != null) parentNode.LocalFlags |= child1.LocalFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.LocalFlagsTransferMask_;
        parentNode.UpdateMergedFlags();

        if (child0 != null) DockContextDeleteNode(ctx, child0);
        if (child1 != null) DockContextDeleteNode(ctx, child1);
    }

    /// <summary>
    /// Recursively updates the positions and dimensions of a dock node tree based on split ratios.
    /// </summary>
    /// <param name="node">The root or branch node to update.</param>
    /// <param name="pos">The new top-left coordinates.</param>
    /// <param name="size">The new dimensions.</param>
    /// <param name="onlyWriteToSingleNode">Optional constraint to update only the specified node.</param>
    internal static void DockNodeTreeUpdatePosSize(ImGuiDockNode node, ImVec2 pos, ImVec2 size, ImGuiDockNode? onlyWriteToSingleNode = null)
    {
        ImGuiContext g = GImGui!;
        bool writeToNode = onlyWriteToSingleNode == null || onlyWriteToSingleNode == node;
        if (writeToNode)
        {
            node.Pos = pos;
            node.Size = size;
        }

        if (node.IsLeafNode())
            return;

        ImGuiDockNode child0 = node.ChildNodes[0]!;
        ImGuiDockNode child1 = node.ChildNodes[1]!;
        ImVec2 child0Pos = pos, child1Pos = pos;
        ImVec2 child0Size = size, child1Size = size;

        bool child0Toward = (onlyWriteToSingleNode != null && DockNodeIsInHierarchyOf(onlyWriteToSingleNode, child0));
        bool child1Toward = (onlyWriteToSingleNode != null && DockNodeIsInHierarchyOf(onlyWriteToSingleNode, child1));
        bool child0Visible = child0.IsVisible || child0Toward;
        bool child1Visible = child1.IsVisible || child1Toward;

        if (child0Visible && child1Visible)
        {
            float spacing = g.Style.DockingSeparatorSize;
            ImGuiAxis axis = node.SplitAxis;
            float sizeAvail = MathF.Max(size[axis] - spacing, 0.0f);
            float sizeMinEach = MathF.Truncate(MathF.Min(sizeAvail, g.Style.WindowMinSize[axis] * 2.0f) * 0.5f);

            if (child0.WantLockSizeOnce && !child1.WantLockSizeOnce)
            {
                child0Size[axis] = child0.SizeRef[axis] = MathF.Min(sizeAvail - 1.0f, child0.Size[axis]);
                child1Size[axis] = child1.SizeRef[axis] = sizeAvail - child0Size[axis];
            }
            else if (child1.WantLockSizeOnce && !child0.WantLockSizeOnce)
            {
                child1Size[axis] = child1.SizeRef[axis] = MathF.Min(sizeAvail - 1.0f, child1.Size[axis]);
                child0Size[axis] = child0.SizeRef[axis] = sizeAvail - child1Size[axis];
            }
            else if (child0.WantLockSizeOnce && child1.WantLockSizeOnce)
            {
                float splitRatio = child0Size[axis] / (child0Size[axis] + child1Size[axis]);
                child0Size[axis] = child0.SizeRef[axis] = MathF.Truncate(sizeAvail * splitRatio);
                child1Size[axis] = child1.SizeRef[axis] = sizeAvail - child0Size[axis];
                ImAssert.Check(child0.SizeRef[axis] > 0.0f && child1.SizeRef[axis] > 0.0f);
            }
            else if (child0.SizeRef[axis] != 0.0f && child1.HasCentralNodeChild)
            {
                child0Size[axis] = MathF.Min(sizeAvail - sizeMinEach, child0.SizeRef[axis]);
                child1Size[axis] = sizeAvail - child0Size[axis];
            }
            else if (child1.SizeRef[axis] != 0.0f && child0.HasCentralNodeChild)
            {
                child1Size[axis] = MathF.Min(sizeAvail - sizeMinEach, child1.SizeRef[axis]);
                child0Size[axis] = sizeAvail - child1Size[axis];
            }
            else
            {
                float totalRef = child0.SizeRef[axis] + child1.SizeRef[axis];
                float splitRatio = totalRef > 0.0f ? child0.SizeRef[axis] / totalRef : 0.5f;
                child0Size[axis] = MathF.Max(sizeMinEach, MathF.Truncate(sizeAvail * splitRatio + 0.5f));
                child1Size[axis] = sizeAvail - child0Size[axis];
            }

            child1Pos[axis] += spacing + child0Size[axis];
        }

        if (onlyWriteToSingleNode == null)
            child0.WantLockSizeOnce = child1.WantLockSizeOnce = false;

        bool child0Recurse = onlyWriteToSingleNode != null ? child0Toward : child0.IsVisible;
        bool child1Recurse = onlyWriteToSingleNode != null ? child1Toward : child1.IsVisible;
        if (child0Recurse)
            DockNodeTreeUpdatePosSize(child0, child0Pos, child0Size, onlyWriteToSingleNode);
        if (child1Recurse)
            DockNodeTreeUpdatePosSize(child1, child1Pos, child1Size, onlyWriteToSingleNode);
    }

    /// <summary>
    /// Locates all leaf dock nodes that share a boundary line with the specified splitter axis.
    /// </summary>
    /// <param name="node">The subtree root to search.</param>
    /// <param name="axis">The orientation axis of the splitter.</param>
    /// <param name="side">The side index (0 or 1) of the boundary.</param>
    /// <param name="touchingNodes">The collection receiving touching leaf nodes.</param>
    internal static void DockNodeTreeUpdateSplitterFindTouchingNode(ImGuiDockNode node, ImGuiAxis axis, int side, List<ImGuiDockNode> touchingNodes)
    {
        if (node.IsLeafNode())
        {
            touchingNodes.Add(node);
            return;
        }
        if (node.ChildNodes[0] != null && node.ChildNodes[0]!.IsVisible)
            if (node.SplitAxis != axis || side == 0 || node.ChildNodes[1] == null || !node.ChildNodes[1]!.IsVisible)
                DockNodeTreeUpdateSplitterFindTouchingNode(node.ChildNodes[0]!, axis, side, touchingNodes);
        if (node.ChildNodes[1] != null && node.ChildNodes[1]!.IsVisible)
            if (node.SplitAxis != axis || side == 1 || node.ChildNodes[0] == null || !node.ChildNodes[0]!.IsVisible)
                DockNodeTreeUpdateSplitterFindTouchingNode(node.ChildNodes[1]!, axis, side, touchingNodes);
    }

    /// <summary>
    /// Updates and interacts with resize splitter grips between child dock nodes.
    /// </summary>
    /// <param name="node">The branch node containing children divided by a splitter.</param>
    internal static void DockNodeTreeUpdateSplitter(ImGuiDockNode node)
    {
        if (node.IsLeafNode())
            return;

        ImGuiContext g = GImGui!;
        ImGuiDockNode child0 = node.ChildNodes[0]!;
        ImGuiDockNode child1 = node.ChildNodes[1]!;
        if (child0.IsVisible && child1.IsVisible)
        {
            ImGuiAxis axis = node.SplitAxis;
            ImAssert.Check(axis != ImGuiAxis.None);
            ImRect bb = new(child0.Pos, child1.Pos);
            bb.Min[axis] += child0.Size[axis];
            bb.Max[axis ^ (ImGuiAxis)1] += child1.Size[axis ^ (ImGuiAxis)1];

            ImGuiDockNodeFlags mergedFlags = child0.MergedFlags | child1.MergedFlags;
            ImGuiDockNodeFlags noResizeAxisFlag = (axis == ImGuiAxis.X) ? ImGuiDockNodeFlags.NoResizeX : ImGuiDockNodeFlags.NoResizeY;
            if ((mergedFlags & ImGuiDockNodeFlags.NoResize) != 0 || (mergedFlags & noResizeAxisFlag) != 0)
            {
                ImGuiWindow window = g.CurrentWindow!;
                window.DrawList.AddRectFilled(bb.Min, bb.Max, GetColorU32(ImGuiCol.Separator), g.Style.FrameRounding);
            }
            else
            {
                PushOverrideID(node.ID);
                uint splitterId = GetID("##Splitter");

                List<ImGuiDockNode>[] touchingNodes = [new(), new()];
                float minSize = g.Style.WindowMinSize[axis];
                float[] resizeLimits =
                [
                    child0.Pos[axis] + minSize,
                    child1.Pos[axis] + child1.Size[axis] - minSize
                ];

                if (g.ActiveId == splitterId)
                {
                    DockNodeTreeUpdateSplitterFindTouchingNode(child0, axis, 1, touchingNodes[0]);
                    DockNodeTreeUpdateSplitterFindTouchingNode(child1, axis, 0, touchingNodes[1]);
                    for (int n = 0; n < touchingNodes[0].Count; n++)
                        resizeLimits[0] = MathF.Max(resizeLimits[0], touchingNodes[0][n].Rect().Min[axis] + minSize);
                    for (int n = 0; n < touchingNodes[1].Count; n++)
                        resizeLimits[1] = MathF.Min(resizeLimits[1], touchingNodes[1][n].Rect().Max[axis] - minSize);
                }

                float curSize0 = child0.Size[axis];
                float curSize1 = child1.Size[axis];
                float minSize0 = resizeLimits[0] - child0.Pos[axis];
                float minSize1 = child1.Pos[axis] + child1.Size[axis] - resizeLimits[1];
                uint bgCol = GetColorU32(ImGuiCol.WindowBg);
                if (SplitterBehavior(bb, splitterId, axis, ref curSize0, ref curSize1, minSize0, minSize1, g.WindowsBorderHoverPadding, 0.04f, bgCol))
                {
                    if (touchingNodes[0].Count > 0 && touchingNodes[1].Count > 0)
                    {
                        child0.Size[axis] = child0.SizeRef[axis] = curSize0;
                        child1.Pos[axis] -= curSize1 - child1.Size[axis];
                        child1.Size[axis] = child1.SizeRef[axis] = curSize1;

                        for (int sideN = 0; sideN < 2; sideN++)
                        {
                            for (int touchingNodeN = 0; touchingNodeN < touchingNodes[sideN].Count; touchingNodeN++)
                            {
                                ImGuiDockNode touchingNode = touchingNodes[sideN][touchingNodeN];
                                while (touchingNode.ParentNode != node && touchingNode.ParentNode != null)
                                {
                                    if (touchingNode.ParentNode.SplitAxis == axis)
                                    {
                                        ImGuiDockNode? nodeToPreserve = touchingNode.ParentNode.ChildNodes[sideN];
                                        if (nodeToPreserve != null)
                                            nodeToPreserve.WantLockSizeOnce = true;
                                    }
                                    touchingNode = touchingNode.ParentNode;
                                }
                            }
                        }

                        DockNodeTreeUpdatePosSize(child0, child0.Pos, child0.Size);
                        DockNodeTreeUpdatePosSize(child1, child1.Pos, child1.Size);
                        MarkIniSettingsDirty();
                    }
                }
                PopID();
            }
        }

        if (child0.IsVisible) DockNodeTreeUpdateSplitter(child0);
        if (child1.IsVisible) DockNodeTreeUpdateSplitter(child1);
    }

    /// <summary>
    /// Locates the topmost visible dock node enclosing the specified screen-space coordinate.
    /// </summary>
    /// <param name="node">The root node of the dock tree to query.</param>
    /// <param name="pos">The screen-space coordinate to test.</param>
    /// <returns>The visible <see cref="ImGuiDockNode"/> containing <paramref name="pos"/>, or <see langword="null"/> if outside.</returns>
    internal static ImGuiDockNode? DockNodeTreeFindVisibleNodeByPos(ImGuiDockNode node, ImVec2 pos)
    {
        if (!node.IsVisible)
            return null;
        if (!node.Rect().Contains(pos))
            return null;

        if (node.IsLeafNode())
            return node;

        if (node.ChildNodes[0] != null && node.ChildNodes[0]!.IsVisible && node.ChildNodes[0]!.Rect().Contains(pos))
            return DockNodeTreeFindVisibleNodeByPos(node.ChildNodes[0]!, pos);
        if (node.ChildNodes[1] != null && node.ChildNodes[1]!.IsVisible && node.ChildNodes[1]!.Rect().Contains(pos))
            return DockNodeTreeFindVisibleNodeByPos(node.ChildNodes[1]!, pos);

        return null;
    }

    /// <summary>
    /// Finds a default fallback leaf node in a dock tree hierarchy suitable for hosting windows.
    /// </summary>
    /// <param name="node">The dock tree node to search.</param>
    /// <returns>A leaf <see cref="ImGuiDockNode"/> within the hierarchy.</returns>
    internal static ImGuiDockNode DockNodeTreeFindFallbackLeafNode(ImGuiDockNode node)
    {
        if (node.IsLeafNode())
            return node;
        if (node.ChildNodes[0] != null)
            return DockNodeTreeFindFallbackLeafNode(node.ChildNodes[0]!);
        if (node.ChildNodes[1] != null)
            return DockNodeTreeFindFallbackLeafNode(node.ChildNodes[1]!);
        return node;
    }

    /// <summary>
    /// Updates the cached flag indicating whether any descendant in the subtree is a central node.
    /// </summary>
    /// <param name="node">The root or branch node to evaluate.</param>
    internal static void DockNodeUpdateHasCentralNodeChild(ImGuiDockNode node)
    {
        node.HasCentralNodeChild = false;
        if (node.IsCentralNode())
            node.HasCentralNodeChild = true;
        if (node.ChildNodes[0] != null)
        {
            DockNodeUpdateHasCentralNodeChild(node.ChildNodes[0]!);
            node.HasCentralNodeChild |= node.ChildNodes[0]!.HasCentralNodeChild;
        }
        if (node.ChildNodes[1] != null)
        {
            DockNodeUpdateHasCentralNodeChild(node.ChildNodes[1]!);
            node.HasCentralNodeChild |= node.ChildNodes[1]!.HasCentralNodeChild;
        }
    }

    /// <summary>
    /// Recalculates combined node flags and manages automatic collapse behavior.
    /// </summary>
    /// <param name="node">The dock node to update.</param>
    internal static void DockNodeUpdateFlagsAndCollapse(ImGuiDockNode node)
    {
        node.UpdateMergedFlags();
        if (node.ChildNodes[0] != null) DockNodeUpdateFlagsAndCollapse(node.ChildNodes[0]!);
        if (node.ChildNodes[1] != null) DockNodeUpdateFlagsAndCollapse(node.ChildNodes[1]!);
    }

    /// <summary>
    /// Recalculates the visibility status flag for a dock node based on its child hierarchy.
    /// </summary>
    /// <param name="node">The dock node whose visibility to evaluate.</param>
    internal static void DockNodeUpdateVisibleFlag(ImGuiDockNode node)
    {
        bool isVisible = false;
        if (node.IsLeafNode())
        {
            isVisible = (node.Windows.Count > 0) || node.IsCentralNode();
        }
        else
        {
            if (node.ChildNodes[0] != null) { DockNodeUpdateVisibleFlag(node.ChildNodes[0]!); isVisible |= node.ChildNodes[0]!.IsVisible; }
            if (node.ChildNodes[1] != null) { DockNodeUpdateVisibleFlag(node.ChildNodes[1]!); isVisible |= node.ChildNodes[1]!.IsVisible; }
        }
        node.IsVisible = isVisible;
    }

    /// <summary>
    /// Configures host window properties, styling, and geometry to align with a dock node.
    /// </summary>
    /// <param name="node">The dock node being hosted.</param>
    /// <param name="hostWindow">The window acting as the host surface.</param>
    internal static void DockNodeSetupHostWindow(ImGuiDockNode node, ImGuiWindow hostWindow)
    {
        node.HostWindow = hostWindow;
        hostWindow.DockNodeAsHost = node;
    }

    /// <summary>
    /// Performs per-frame lifecycle updates, tab bar maintenance, and rendering for a dock node.
    /// </summary>
    /// <param name="node">The dock node to process.</param>
    internal static void DockNodeUpdate(ImGuiDockNode node)
    {
        ImGuiContext g = GImGui!;
        node.LastFrameAlive = g.FrameCount;
        node.IsBgDrawnThisFrame = false;

        if (node.IsRootNode())
        {
            DockNodeUpdateVisibleFlag(node);
            DockNodeUpdateHasCentralNodeChild(node);
        }

        if (!node.IsVisible)
            return;

        ImGuiWindow? hostWindow = null;
        bool beginnedIntoHostWindow = false;

        if (node.IsDockSpace())
        {
            hostWindow = node.HostWindow;
        }
        else
        {
            if (node.IsRootNode() && node.IsVisible)
            {
                string hostTitle = DockNodeGetHostWindowTitle(node);
                SetNextWindowPos(node.Pos);
                SetNextWindowSize(node.Size);
                ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.DockNodeHost;
                flags |= ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing;

                PushStyleVar(ImGuiStyleVar.WindowPadding, ImVec2.Zero);
                Begin(hostTitle, flags);
                PopStyleVar();
                beginnedIntoHostWindow = true;

                hostWindow = g.CurrentWindow!;
                DockNodeSetupHostWindow(node, hostWindow);
                hostWindow.DC.CursorPos = hostWindow.Pos;
                node.Pos = hostWindow.Pos;
                node.Size = hostWindow.Size;

                if (node.HostWindow!.Appearing)
                    BringWindowToDisplayFront(node.HostWindow!);

                node.AuthorityForPos = node.AuthorityForSize = node.AuthorityForViewport = (byte)ImGuiDataAuthority.Auto;
            }
            else if (node.ParentNode != null)
            {
                node.HostWindow = hostWindow = node.ParentNode.HostWindow;
                node.AuthorityForPos = node.AuthorityForSize = node.AuthorityForViewport = (byte)ImGuiDataAuthority.Auto;
            }
        }

        if (node.IsRootNode())
        {
            DockNodeTreeUpdatePosSize(node, node.Pos, node.Size);
            DockNodeTreeUpdateSplitter(node);
        }

        // Draw empty node background on BG channel
        if (hostWindow != null && node.IsEmpty() && node.IsVisible)
        {
            hostWindow.DrawList.ChannelsSetCurrent(DOCKING_HOST_DRAW_CHANNEL_BG);
            node.LastBgColor = ((node.MergedFlags & ImGuiDockNodeFlags.PassthruCentralNode) != 0) ? 0 : GetColorU32(ImGuiCol.DockingEmptyBg);
            if (node.LastBgColor != 0)
                hostWindow.DrawList.AddRectFilled(node.Pos, node.Pos + node.Size, node.LastBgColor);
            node.IsBgDrawnThisFrame = true;
        }

        // Whole dockspace background if PassthruCentralNode is set
        bool renderDockspaceBg = node.IsRootNode() && hostWindow != null && ((node.MergedFlags & ImGuiDockNodeFlags.PassthruCentralNode) != 0);
        if (renderDockspaceBg && node.IsVisible)
        {
            hostWindow!.DrawList.ChannelsSetCurrent(DOCKING_HOST_DRAW_CHANNEL_BG);
            hostWindow.DrawList.AddRectFilled(node.Pos, node.Pos + node.Size, GetColorU32(ImGuiCol.WindowBg), 0.0f);
        }

        // Draw and populate Tab Bar on FG channel
        if (hostWindow != null)
            hostWindow.DrawList.ChannelsSetCurrent(DOCKING_HOST_DRAW_CHANNEL_FG);

        if (hostWindow != null && node.Windows.Count > 0)
        {
            DockNodeUpdateTabBar(node, hostWindow);
        }
        else
        {
            node.WantCloseAll = false;
            node.WantCloseTabId = 0;
            node.IsFocused = false;
        }

        if (node.TabBar != null && node.TabBar.SelectedTabId != 0)
            node.SelectedTabId = node.TabBar.SelectedTabId;
        else if (node.Windows.Count > 0)
            node.SelectedTabId = node.Windows[0].TabId;

        // Draw payload drop target.
        if (hostWindow != null && node.IsVisible)
            if (node.IsRootNode() && (g.MovingWindow == null || g.MovingWindow.RootWindowDockTree != hostWindow))
                BeginDockableDragDropTarget(hostWindow);

        node.LastFrameActive = g.FrameCount;

        // Recurse into children
        if (hostWindow != null)
        {
            if (node.ChildNodes[0] != null) DockNodeUpdate(node.ChildNodes[0]!);
            if (node.ChildNodes[1] != null) DockNodeUpdate(node.ChildNodes[1]!);
        }

        if (beginnedIntoHostWindow)
            End();
    }

    /// <summary>
    /// Creates and attaches a tab bar to a leaf dock node.
    /// </summary>
    /// <param name="node">The leaf dock node receiving the tab bar.</param>
    internal static void DockNodeAddTabBar(ImGuiDockNode node)
    {
        ImAssert.Check(node.TabBar == null);
        node.TabBar = new ImGuiTabBar { ID = node.ID };
    }

    /// <summary>
    /// Detaches and destroys the tab bar from a dock node.
    /// </summary>
    /// <param name="node">The dock node whose tab bar to remove.</param>
    internal static void DockNodeRemoveTabBar(ImGuiDockNode node)
    {
        node.TabBar = null;
    }

    /// <summary>
    /// Computes geometric layout coordinates for the tab bar, buttons, and title area within a dock node.
    /// </summary>
    /// <param name="node">The dock node to measure.</param>
    /// <param name="outTitleRect">Receives the screen-space bounding rectangle for the title area.</param>
    /// <param name="outTabBarRect">Receives the screen-space bounding rectangle for the tab bar.</param>
    /// <param name="outWindowMenuButtonPos">Receives the top-left position for the window menu button.</param>
    /// <param name="outCloseButtonPos">Receives the top-left position for the close button.</param>
    internal static void DockNodeCalcTabBarLayout(ImGuiDockNode node, out ImRect outTitleRect, out ImRect outTabBarRect, out ImVec2 outWindowMenuButtonPos, out ImVec2 outCloseButtonPos)
    {
        ImGuiContext g = GImGui!;
        ImGuiStyle style = g.Style;
        float frameHeight = GetFrameHeight();
        ImRect titleBarRect = new(node.Pos, node.Pos + new ImVec2(node.Size.X, frameHeight));
        ImRect tabBarRect = titleBarRect;

        outTitleRect = titleBarRect;
        outTabBarRect = tabBarRect;
        outWindowMenuButtonPos = titleBarRect.Min + style.FramePadding;
        outCloseButtonPos = new ImVec2(titleBarRect.Max.X - frameHeight, titleBarRect.Min.Y + style.FramePadding.Y);
    }

    /// <summary>
    /// Submits and processes tab bar UI interactions for all windows docked in a node.
    /// </summary>
    /// <param name="node">The dock node containing the tab bar.</param>
    /// <param name="hostWindow">The window hosting the dock node rendering.</param>
    internal static void DockNodeUpdateTabBar(ImGuiDockNode node, ImGuiWindow hostWindow)
    {
        ImGuiContext g = GImGui!;
        if (node.IsHiddenTabBar() || node.IsNoTabBar())
        {
            if (node.TabBar != null && node.VisibleWindow != null)
                node.TabBar.VisibleTabId = node.VisibleWindow.TabId;
            return;
        }

        if (node.TabBar == null)
            DockNodeAddTabBar(node);

        ImGuiTabBar tabBar = node.TabBar!;
        DockNodeCalcTabBarLayout(node, out _, out ImRect tabBarRect, out ImVec2 windowMenuButtonPos, out ImVec2 closeButtonPos);

        for (int i = 0; i < node.Windows.Count; i++)
        {
            ImGuiWindow w = node.Windows[i];
            if (TabBarFindTabByID(tabBar, w.TabId) == null)
                TabBarAddTab(tabBar, ImGuiTabItemFlags.Unsorted, w);
        }

        ImGuiTabBarFlags tabBarFlags = ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs;
        tabBarFlags |= (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_SaveSettings
            | (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode
            | ImGuiTabBarFlags.FittingPolicyMixed
            | ImGuiTabBarFlags.DrawSelectedOverline;
        if (!hostWindow.Collapsed && node.IsFocused)
            tabBarFlags |= (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_IsFocused;
        tabBar.ID = node.ID;

        ImGuiWindow? prevWindow = g.CurrentWindow;
        if (g.CurrentWindow != hostWindow)
            SetCurrentWindow(hostWindow);

        PushOverrideID(node.ID);
        if (BeginTabBarEx(tabBar, tabBarRect, tabBarFlags))
        {
            for (int n = 0; n < node.Windows.Count; n++)
            {
                ImGuiWindow w = node.Windows[n];
                bool open = true;
                TabItemEx(tabBar, w.Name, ref open, ImGuiTabItemFlags.None, w);
                if (!open && w.HasCloseButton)
                    node.WantCloseTabId = w.TabId;
            }
            EndTabBar();
        }
        PopID();

        if (prevWindow != null && prevWindow != hostWindow)
            SetCurrentWindow(prevWindow);

        if (tabBar.SelectedTabId != 0)
        {
            for (int n = 0; n < node.Windows.Count; n++)
            {
                if (node.Windows[n].TabId == tabBar.SelectedTabId)
                {
                    node.VisibleWindow = node.Windows[n];
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Determines the zero-based tab ordering position of a window inside its parent dock node.
    /// </summary>
    /// <param name="window">The window to locate.</param>
    /// <returns>The zero-based tab index, or <c>-1</c> if not docked in a tab bar.</returns>
    internal static int DockNodeGetTabOrder(ImGuiWindow window)
    {
        if (window.DockNode?.TabBar is { } tabBar)
        {
            if (TabBarFindTabByID(tabBar, window.TabId) is { } tab)
                return TabBarGetTabOrder(tabBar, tab);
        }
        return -1;
    }

    /// <summary>
    /// Evaluates whether dragging a payload window can drop into any part of the host window.
    /// </summary>
    /// <param name="hostWindow">The prospective drop target window.</param>
    /// <param name="rootPayload">The root window of the dragging payload.</param>
    /// <returns><see langword="true"/> if dropping is permitted; otherwise, <see langword="false"/>.</returns>
    internal static bool DockNodeIsDropAllowed(ImGuiWindow hostWindow, ImGuiWindow rootPayload)
    {
        if (rootPayload.DockNodeAsHost != null && rootPayload.DockNodeAsHost.IsSplitNode())
            return true;

        int payloadCount = rootPayload.DockNodeAsHost != null ? rootPayload.DockNodeAsHost.Windows.Count : 1;
        for (int n = 0; n < payloadCount; n++)
        {
            ImGuiWindow payload = rootPayload.DockNodeAsHost != null ? rootPayload.DockNodeAsHost.Windows[n] : rootPayload;
            if (DockNodeIsDropAllowedOne(payload, hostWindow))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks compatibility between an individual payload window and a potential dock host window.
    /// </summary>
    /// <param name="payload">The payload window to test.</param>
    /// <param name="hostWindow">The prospective host window.</param>
    /// <returns><see langword="true"/> if the payload may dock into the host; otherwise, <see langword="false"/>.</returns>
    internal static bool DockNodeIsDropAllowedOne(ImGuiWindow payload, ImGuiWindow hostWindow)
    {
        if ((payload.Flags & ImGuiWindowFlags.NoDocking) != 0)
            return false;
        if (hostWindow.DockNodeAsHost != null && hostWindow.DockNodeAsHost.IsDockSpace() && payload.BeginOrderWithinContext < hostWindow.BeginOrderWithinContext)
            return false;

        ImGuiWindowClass hostClass = hostWindow.DockNodeAsHost?.WindowClass ?? hostWindow.WindowClass;
        ImGuiWindowClass payloadClass = payload.WindowClass;
        if (hostClass.ClassId != payloadClass.ClassId)
        {
            bool pass = false;
            if (hostClass.ClassId != 0 && hostClass.DockingAllowUnclassed && payloadClass.ClassId == 0) pass = true;
            if (payloadClass.ClassId != 0 && payloadClass.DockingAllowUnclassed && hostClass.ClassId == 0) pass = true;
            if (!pass) return false;
        }
        return true;
    }

    /// <summary>
    /// Configures geometry, drop rectangles, and directional slot targets for previewing a dock drop.
    /// </summary>
    /// <param name="hostWindow">The window being hovered as a target.</param>
    /// <param name="hostNode">The dock node target, if present.</param>
    /// <param name="payloadWindow">The window being dragged.</param>
    /// <param name="payloadNode">The source dock node of the payload, if present.</param>
    /// <param name="data">The preview data state record to populate.</param>
    /// <param name="isExplicitTarget">A value indicating whether a specific target was explicitly requested.</param>
    /// <param name="isOuterDocking">A value indicating whether outer edge dock targets are being evaluated.</param>
    internal static void DockNodePreviewDockSetup(ImGuiWindow? hostWindow, ImGuiDockNode? hostNode, ImGuiWindow? payloadWindow, ImGuiDockNode? payloadNode, ImGuiDockPreviewData data, bool isExplicitTarget, bool isOuterDocking)
    {
        ImGuiContext g = GImGui!;

        if (payloadNode == null && payloadWindow != null)
            payloadNode = payloadWindow.DockNodeAsHost;
        ImGuiDockNode? refNodeForRect = (hostNode != null && !hostNode.IsVisible) ? DockNodeGetRootNode(hostNode) : hostNode;
        if (refNodeForRect != null)
            ImAssert.Check(refNodeForRect.IsVisible);

        // Filter, figure out where we are allowed to dock
        ImGuiDockNodeFlags srcNodeFlags = payloadNode != null ? payloadNode.MergedFlags : (payloadWindow != null ? payloadWindow.WindowClass.DockNodeFlagsOverrideSet : 0);
        ImGuiDockNodeFlags dstNodeFlags = hostNode != null ? hostNode.MergedFlags : (hostWindow != null ? hostWindow.WindowClass.DockNodeFlagsOverrideSet : 0);
        data.IsCenterAvailable = true;
        if (isOuterDocking)
            data.IsCenterAvailable = false;
        else if (g.IO.ConfigDockingNoDockingOver)
            data.IsCenterAvailable = false;
        else if ((dstNodeFlags & ImGuiDockNodeFlags.NoDockingOverMe) != 0)
            data.IsCenterAvailable = false;
        else if (hostNode != null && (dstNodeFlags & ImGuiDockNodeFlags.NoDockingOverCentralNode) != 0 && hostNode.IsCentralNode())
            data.IsCenterAvailable = false;
        else if ((hostNode == null || !hostNode.IsEmpty()) && payloadNode != null && payloadNode.IsSplitNode() && payloadNode.OnlyNodeWithWindows == null)
            data.IsCenterAvailable = false;
        else if ((srcNodeFlags & ImGuiDockNodeFlags.NoDockingOverOther) != 0 && (hostNode == null || !hostNode.IsEmpty()))
            data.IsCenterAvailable = false;
        else if ((srcNodeFlags & ImGuiDockNodeFlags.NoDockingOverEmpty) != 0 && hostNode != null && hostNode.IsEmpty())
            data.IsCenterAvailable = false;

        data.IsSidesAvailable = true;
        if ((dstNodeFlags & ImGuiDockNodeFlags.NoDockingSplit) != 0 || g.IO.ConfigDockingNoSplit)
            data.IsSidesAvailable = false;
        else if (!isOuterDocking && hostNode != null && hostNode.ParentNode == null && hostNode.IsCentralNode())
            data.IsSidesAvailable = false;
        else if ((srcNodeFlags & ImGuiDockNodeFlags.NoDockingSplitOther) != 0)
            data.IsSidesAvailable = false;

        // Build a tentative future node
        data.FutureNode.HasCloseButton = (hostNode != null ? hostNode.HasCloseButton : (hostWindow != null ? hostWindow.HasCloseButton : false)) || (payloadWindow != null && payloadWindow.HasCloseButton);
        data.FutureNode.HasWindowMenuButton = hostNode != null || (hostWindow != null ? (hostWindow.Flags & ImGuiWindowFlags.NoCollapse) == 0 : true);
        data.FutureNode.Pos = refNodeForRect != null ? refNodeForRect.Pos : (hostWindow != null ? hostWindow.Pos : ImVec2.Zero);
        data.FutureNode.Size = refNodeForRect != null ? refNodeForRect.Size : (hostWindow != null ? hostWindow.Size : ImVec2.Zero);

        data.SplitNode = hostNode;
        data.SplitDir = ImGuiDir.None;
        data.IsSplitDirExplicit = false;
        if (hostWindow == null || !hostWindow.Collapsed)
        {
            for (int dir = (int)ImGuiDir.None; dir < (int)ImGuiDir.COUNT; dir++)
            {
                if (dir == (int)ImGuiDir.None && !data.IsCenterAvailable)
                    continue;
                if (dir != (int)ImGuiDir.None && !data.IsSidesAvailable)
                    continue;

                if (DockNodeCalcDropRectsAndTestMousePos(data.FutureNode.Rect(), (ImGuiDir)dir, out ImRect drawRect, isOuterDocking, g.IO.MousePos))
                {
                    data.SplitDir = (ImGuiDir)dir;
                    data.IsSplitDirExplicit = true;
                }
                data.DropRectsDraw[dir + 1] = drawRect;
            }
        }

        data.IsDropAllowed = (data.SplitDir != ImGuiDir.None) || data.IsCenterAvailable;
        if (!isExplicitTarget && !data.IsSplitDirExplicit && !g.IO.ConfigDockingWithShift)
            data.IsDropAllowed = false;

        data.SplitRatio = 0.0f;
        if (data.SplitDir != ImGuiDir.None)
        {
            ImGuiDir splitDir = data.SplitDir;
            ImGuiAxis splitAxis = (splitDir == ImGuiDir.Left || splitDir == ImGuiDir.Right) ? ImGuiAxis.X : ImGuiAxis.Y;
            ImVec2 posOld = data.FutureNode.Pos;
            ImVec2 sizeOld = data.FutureNode.Size;
            DockNodeCalcSplitRects(posOld, sizeOld, out ImVec2 posNew, out ImVec2 sizeNew, splitDir, payloadWindow != null ? payloadWindow.Size : ImVec2.Zero);

            float splitRatio = Math.Clamp(sizeNew[splitAxis] / data.FutureNode.Size[splitAxis], 0.0f, 1.0f);
            data.FutureNode.Pos = posNew;
            data.FutureNode.Size = sizeNew;
            data.SplitRatio = (splitDir == ImGuiDir.Right || splitDir == ImGuiDir.Down) ? (1.0f - splitRatio) : splitRatio;
        }
    }

    /// <summary>
    /// Calculates directional drop target rectangles and determines whether the mouse cursor hovers them.
    /// </summary>
    /// <param name="parent">The parent boundary rectangle.</param>
    /// <param name="dir">The directional dock slot to calculate.</param>
    /// <param name="outDraw">Receives the visual preview highlight rectangle.</param>
    /// <param name="outerDocking">A value indicating whether outer perimeter docking geometry is evaluated.</param>
    /// <param name="testMousePos">The mouse position to test, or <see langword="null"/> for current mouse position.</param>
    /// <returns><see langword="true"/> if the position hovers the drop rectangle; otherwise, <see langword="false"/>.</returns>
    internal static bool DockNodeCalcDropRectsAndTestMousePos(ImRect parent, ImGuiDir dir, out ImRect outDraw, bool outerDocking, ImVec2? testMousePos)
    {
        ImGuiContext g = GImGui!;

        float parentSmallerAxis = MathF.Min(parent.GetWidth(), parent.GetHeight());
        float hsForCentralNodes = MathF.Min(g.FontSize * 1.5f, MathF.Max(g.FontSize * 0.5f, parentSmallerAxis / 8.0f));
        float hsW;
        float hsH;
        ImVec2 off;
        if (outerDocking)
        {
            hsW = MathF.Truncate(hsForCentralNodes * 1.50f);
            hsH = MathF.Truncate(hsForCentralNodes * 0.80f);
            off = new ImVec2(MathF.Truncate(parent.GetWidth() * 0.5f - hsH), MathF.Truncate(parent.GetHeight() * 0.5f - hsH));
        }
        else
        {
            hsW = MathF.Truncate(hsForCentralNodes);
            hsH = MathF.Truncate(hsForCentralNodes * 0.90f);
            off = new ImVec2(MathF.Truncate(hsW * 2.40f), MathF.Truncate(hsW * 2.40f));
        }

        ImVec2 c = new(MathF.Truncate(parent.GetCenter().X), MathF.Truncate(parent.GetCenter().Y));
        if (dir == ImGuiDir.None)
            outDraw = new ImRect(c.X - hsW, c.Y - hsW, c.X + hsW, c.Y + hsW);
        else if (dir == ImGuiDir.Up)
            outDraw = new ImRect(c.X - hsW, c.Y - off.Y - hsH, c.X + hsW, c.Y - off.Y + hsH);
        else if (dir == ImGuiDir.Down)
            outDraw = new ImRect(c.X - hsW, c.Y + off.Y - hsH, c.X + hsW, c.Y + off.Y + hsH);
        else if (dir == ImGuiDir.Left)
            outDraw = new ImRect(c.X - off.X - hsH, c.Y - hsW, c.X - off.X + hsH, c.Y + hsW);
        else if (dir == ImGuiDir.Right)
            outDraw = new ImRect(c.X + off.X - hsH, c.Y - hsW, c.X + off.X + hsH, c.Y + hsW);
        else
            outDraw = parent;

        if (testMousePos == null)
            return false;

        ImRect hitR = outDraw;
        if (!outerDocking)
        {
            // Custom hit testing for the 5-way selection, designed to reduce flickering when moving diagonally between sides
            hitR.Expand(MathF.Truncate(hsW * 0.30f));
            ImVec2 mouseDelta = testMousePos.Value - c;
            float mouseDeltaLen2 = (mouseDelta.X * mouseDelta.X) + (mouseDelta.Y * mouseDelta.Y);
            float rThresholdCenter = hsW * 1.4f;
            float rThresholdSides = hsW * (1.4f + 1.2f);
            if (mouseDeltaLen2 < rThresholdCenter * rThresholdCenter)
                return dir == ImGuiDir.None;
            if (mouseDeltaLen2 < rThresholdSides * rThresholdSides)
                return dir == ImGetDirQuadrantFromDelta(mouseDelta.X, mouseDelta.Y);
        }

        return hitR.Contains(testMousePos.Value);
    }

    /// <summary>
    /// Calculates new position and size bounds when splitting an area along a directional vector.
    /// </summary>
    /// <param name="posOld">The initial position of the area.</param>
    /// <param name="sizeOld">The initial dimensions of the area.</param>
    /// <param name="posNew">Receives the computed new top-left position.</param>
    /// <param name="sizeNew">Receives the computed new size dimensions.</param>
    /// <param name="dir">The split direction.</param>
    /// <param name="desiredSize">The requested size for the new split region.</param>
    internal static void DockNodeCalcSplitRects(ImVec2 posOld, ImVec2 sizeOld, out ImVec2 posNew, out ImVec2 sizeNew, ImGuiDir dir, ImVec2 desiredSize)
    {
        posNew = posOld;
        sizeNew = sizeOld;
        switch (dir)
        {
            case ImGuiDir.Left:
                sizeNew.X = MathF.Min(sizeOld.X * 0.5f, desiredSize.X);
                break;
            case ImGuiDir.Right:
                sizeNew.X = MathF.Min(sizeOld.X * 0.5f, desiredSize.X);
                posNew.X = posOld.X + sizeOld.X - sizeNew.X;
                break;
            case ImGuiDir.Up:
                sizeNew.Y = MathF.Min(sizeOld.Y * 0.5f, desiredSize.Y);
                break;
            case ImGuiDir.Down:
                sizeNew.Y = MathF.Min(sizeOld.Y * 0.5f, desiredSize.Y);
                posNew.Y = posOld.Y + sizeOld.Y - sizeNew.Y;
                break;
        }
    }

    /// <summary>
    /// Renders preview drop overlays, highlight rectangles, and directional split indicators.
    /// </summary>
    /// <param name="hostWindow">The host window being hovered.</param>
    /// <param name="hostNode">The active dock node, if any.</param>
    /// <param name="rootPayload">The payload window being dragged.</param>
    /// <param name="data">The preview geometry data to render.</param>
    internal static void DockNodePreviewDockRender(ImGuiWindow hostWindow, ImGuiDockNode? hostNode, ImGuiWindow rootPayload, ImGuiDockPreviewData data)
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check(g.CurrentWindow == hostWindow);

        bool isTransparentPayload = g.IO.ConfigDockingTransparentPayload;

        int overlayDrawListsCount = 0;
        ImDrawList[] overlayDrawLists = new ImDrawList[2];
        overlayDrawLists[overlayDrawListsCount++] = GetForegroundDrawList(hostWindow.Viewport ?? GetMainViewport());
        if (hostWindow.Viewport != rootPayload.Viewport && !isTransparentPayload && rootPayload.Viewport != null)
            overlayDrawLists[overlayDrawListsCount++] = GetForegroundDrawList(rootPayload.Viewport);

        uint overlayColMain = GetColorU32(ImGuiCol.DockingPreview, isTransparentPayload ? 0.60f : 0.40f);
        uint overlayColDrop = GetColorU32(ImGuiCol.DockingPreview, isTransparentPayload ? 0.90f : 0.70f);
        uint overlayColDropHovered = GetColorU32(ImGuiCol.DockingPreview, isTransparentPayload ? 1.20f : 1.00f);
        uint overlayColLines = GetColorU32(ImGuiCol.NavWindowingHighlight, isTransparentPayload ? 0.80f : 0.60f);

        bool canPreviewTabs = rootPayload.DockNodeAsHost == null || rootPayload.DockNodeAsHost.Windows.Count > 0;
        if (data.IsDropAllowed)
        {
            ImRect overlayRect = data.FutureNode.Rect();
            if (data.SplitDir == ImGuiDir.None && canPreviewTabs)
                overlayRect.Min.Y += GetFrameHeight();
            if (data.SplitDir != ImGuiDir.None || data.IsCenterAvailable)
            {
                ImDrawFlags roundingFlags = CalcRoundingFlagsForRectInRect(overlayRect, hostWindow.Rect(), g.Style.DockingSeparatorSize);
                for (int overlayN = 0; overlayN < overlayDrawListsCount; overlayN++)
                    overlayDrawLists[overlayN].AddRectFilled(overlayRect.Min, overlayRect.Max, overlayColMain, hostWindow.WindowRounding, roundingFlags);
            }
        }

        // Display tab shape/label preview unless we are splitting node
        if (data.IsDropAllowed && canPreviewTabs && data.SplitDir == ImGuiDir.None && data.IsCenterAvailable)
        {
            DockNodeCalcTabBarLayout(data.FutureNode, out _, out ImRect tabBarRect, out _, out _);
            ImVec2 tabPos = tabBarRect.Min;
            if (hostNode != null && hostNode.TabBar != null)
            {
                if (!hostNode.IsHiddenTabBar() && !hostNode.IsNoTabBar())
                    tabPos.X += hostNode.TabBar.WidthAllTabs + g.Style.ItemInnerSpacing.X;
                else if (hostNode.Windows.Count > 0)
                    tabPos.X += g.Style.ItemInnerSpacing.X + TabItemCalcSize(hostNode.Windows[0]).X;
            }
            else if ((hostWindow.Flags & ImGuiWindowFlags.DockNodeHost) == 0)
            {
                tabPos.X += g.Style.ItemInnerSpacing.X + TabItemCalcSize(hostWindow).X;
            }

            ImGuiTabBar? tabBarWithPayload = rootPayload.DockNodeAsHost != null ? rootPayload.DockNodeAsHost.TabBar : null;
            int payloadCount = tabBarWithPayload != null ? tabBarWithPayload.Tabs.Count : 1;
            for (int payloadN = 0; payloadN < payloadCount; payloadN++)
            {
                ImGuiWindow? payloadWindow = tabBarWithPayload != null ? tabBarWithPayload.Tabs[payloadN].Window : rootPayload;
                if (tabBarWithPayload != null && payloadWindow == null)
                    continue;
                if (!DockNodeIsDropAllowedOne(payloadWindow!, hostWindow))
                    continue;

                ImVec2 tabSize = TabItemCalcSize(payloadWindow!);
                ImRect tabBb = new(tabPos.X, tabPos.Y, tabPos.X + tabSize.X, tabPos.Y + tabSize.Y);
                tabPos.X += tabSize.X + g.Style.ItemInnerSpacing.X;
                uint overlayColText = GetColorU32(payloadWindow!.DockStyle.Colors[(int)ImGuiWindowDockStyleCol.Text]);
                uint overlayColTabs = GetColorU32(payloadWindow.DockStyle.Colors[(int)ImGuiWindowDockStyleCol.TabSelected]);
                uint overlayColUnsavedMarker = GetColorU32(payloadWindow.DockStyle.Colors[(int)ImGuiWindowDockStyleCol.UnsavedMarker]);
                PushStyleColor(ImGuiCol.Text, overlayColText);
                PushStyleColor(ImGuiCol.UnsavedMarker, overlayColUnsavedMarker);
                for (int overlayN = 0; overlayN < overlayDrawListsCount; overlayN++)
                {
                    ImGuiTabItemFlags tabFlags = (payloadWindow.Flags & ImGuiWindowFlags.UnsavedDocument) != 0 ? ImGuiTabItemFlags.UnsavedDocument : 0;
                    if (!tabBarRect.Contains(tabBb))
                        overlayDrawLists[overlayN].PushClipRect(tabBarRect.Min, tabBarRect.Max);
                    TabItemBackground(overlayDrawLists[overlayN], tabBb, tabFlags, overlayColTabs);
                    TabItemLabelAndCloseButton(overlayDrawLists[overlayN], tabBb, tabFlags, g.Style.FramePadding, payloadWindow.Name, 0, 0, false, out _, out _);
                    if (!tabBarRect.Contains(tabBb))
                        overlayDrawLists[overlayN].PopClipRect();
                }
                PopStyleColor(2);
            }
        }

        // Display drop boxes
        float scale = g.Style._MainScale;
        float overlayRounding = MathF.Max((int)(3.0f * scale), g.Style.FrameRounding);
        for (int dir = (int)ImGuiDir.None; dir < (int)ImGuiDir.COUNT; dir++)
        {
            if (!data.DropRectsDraw[dir + 1].IsInverted())
            {
                ImRect drawR = data.DropRectsDraw[dir + 1];
                ImRect drawRIn = drawR;
                drawRIn.Expand((int)(-2.0f * scale));
                uint overlayCol = (data.SplitDir == (ImGuiDir)dir && data.IsSplitDirExplicit) ? overlayColDropHovered : overlayColDrop;
                float thickness = (int)(1.0f * scale);
                for (int overlayN = 0; overlayN < overlayDrawListsCount; overlayN++)
                {
                    ImVec2 center = new(MathF.Floor(drawRIn.GetCenter().X), MathF.Floor(drawRIn.GetCenter().Y));
                    overlayDrawLists[overlayN].AddRectFilled(drawR.Min, drawR.Max, overlayCol, overlayRounding);
                    overlayDrawLists[overlayN].AddRect(drawRIn.Min, drawRIn.Max, overlayColLines, overlayRounding, thickness: thickness);
                    if (dir == (int)ImGuiDir.Left || dir == (int)ImGuiDir.Right)
                        overlayDrawLists[overlayN].AddLine(new ImVec2(center.X, drawRIn.Min.Y), new ImVec2(center.X, drawRIn.Max.Y), overlayColLines, thickness);
                    if (dir == (int)ImGuiDir.Up || dir == (int)ImGuiDir.Down)
                        overlayDrawLists[overlayN].AddLine(new ImVec2(drawRIn.Min.X, center.Y), new ImVec2(drawRIn.Max.X, center.Y), overlayColLines, thickness);
                }
            }

            // Stop after ImGuiDir_None
            if ((hostNode != null && (hostNode.MergedFlags & ImGuiDockNodeFlags.NoDockingSplit) != 0) || g.IO.ConfigDockingNoSplit)
                return;
        }
    }

    /// <summary>
    /// Clamps oversized window dimensions when undocking so the window fits comfortably on screen.
    /// </summary>
    /// <param name="size">The current window dimensions.</param>
    /// <param name="refViewport">The reference viewport providing boundary metrics.</param>
    /// <returns>Clamped dimensions suitable for a floating window.</returns>
    private static ImVec2 FixLargeWindowsWhenUndocking(ImVec2 size, ImGuiViewport? refViewport)
    {
        if (refViewport == null)
            return size;
        ImVec2 maxSize = refViewport.WorkSize * 0.90f;
        return new ImVec2(MathF.Min(size.X, maxSize.X), MathF.Min(size.Y, maxSize.Y));
    }

    /// <summary>
    /// Preserves current window styling attributes in the window's dock style cache.
    /// </summary>
    /// <param name="window">The window whose dock style to capture.</param>
    private static void StoreDockStyleForWindow(ImGuiWindow window)
    {
        ImGuiContext g = GImGui!;
        for (int i = 0; i < (int)ImGuiWindowDockStyleCol.COUNT; i++)
            window.DockStyle.Colors[i] = ColorConvertFloat4ToU32(g.Style.Colors[(int)GWindowDockStyleColors[i]]);
    }

    /// <summary>
    /// Updates persistent settings records by replacing references to an old node ID with a new node ID.
    /// </summary>
    /// <param name="oldNodeId">The original node identifier.</param>
    /// <param name="newNodeId">The replacement node identifier.</param>
    private static void DockSettingsRenameNodeReferences(uint oldNodeId, uint newNodeId)
    {
        ImGuiContext g = GImGui!;
        for (int i = 0; i < g.SettingsWindows.Count; i++)
            if (g.SettingsWindows[i].DockId == oldNodeId)
                g.SettingsWindows[i].DockId = newNodeId;
    }

    /// <summary>
    /// Rebuilds the dock node hierarchy from loaded settings.
    /// </summary>
    /// <param name="ctx">Active ImGui context.</param>
    /// <param name="settings">The settings records to materialize, in parent-before-child order.</param>
    /// <remarks>
    /// The parent link is resolved through the identifier, and the node is attached to the first free child
    /// slot of that parent. Binding the host window immediately lets the title-bar highlight and the dock
    /// style resolve during the same frame the nodes are rebuilt.
    /// </remarks>
    private static void DockContextBuildNodesFromSettings(ImGuiContext ctx, List<ImGuiDockNodeSettings> settings)
    {
        for (int i = 0; i < settings.Count; i++)
        {
            ImGuiDockNodeSettings s = settings[i];
            if (s.ID == 0)
                continue;
            if (DockContextFindNodeByID(ctx, s.ID) != null)
                continue; // Skip a duplicate identifier.

            ImGuiDockNode node = DockContextAddNode(ctx, s.ID);
            node.ParentNode = s.ParentNodeId != 0 ? DockContextFindNodeByID(ctx, s.ParentNodeId) : null;
            node.Pos = new ImVec2(s.Pos.X, s.Pos.Y);
            node.Size = new ImVec2(s.Size.X, s.Size.Y);
            node.SizeRef = new ImVec2(s.SizeRef.X, s.SizeRef.Y);
            node.AuthorityForPos = node.AuthorityForSize = node.AuthorityForViewport = (byte)ImGuiDataAuthority.DockNode;
            if (node.ParentNode != null && node.ParentNode.ChildNodes[0] == null)
                node.ParentNode.ChildNodes[0] = node;
            else if (node.ParentNode != null && node.ParentNode.ChildNodes[1] == null)
                node.ParentNode.ChildNodes[1] = node;
            node.SelectedTabId = s.SelectedTabId;
            node.SplitAxis = (ImGuiAxis)s.SplitAxis;
            node.SetLocalFlags(s.Flags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.SavedFlagsMask_);

            // Bind the host window immediately when it already exists (a rebuild can happen mid-session);
            // the title-bar highlight depends on node.HostWindow being set.
            ImGuiDockNode rootNode = DockNodeGetRootNode(node);
            node.HostWindow = FindWindowByName(DockNodeGetHostWindowTitle(rootNode));
        }
    }

    /// <summary>
    /// Returns the stable internal title of a dock node's host window.
    /// </summary>
    /// <param name="node">The node whose host window title is required.</param>
    /// <returns>The `##DockNode_XX` title, which is hidden by the `##` marker but identifies the window.</returns>
    private static string DockNodeGetHostWindowTitle(ImGuiDockNode node)
    {
        return $"##DockNode_{node.ID:X2}";
    }

    /// <summary>
    /// Reconnects persisted windows to reconstructed dock nodes following layout deserialization.
    /// </summary>
    /// <param name="ctx">The ImGui context.</param>
    /// <param name="rootId">The root dock node identifier.</param>
    private static void DockContextBuildAddWindowsToNodes(ImGuiContext ctx, uint rootId)
    {
        for (int i = 0; i < ctx.Windows.Count; i++)
        {
            ImGuiWindow window = ctx.Windows[i];
            if (window.DockId != 0 && (rootId == 0 || window.DockId == rootId))
            {
                if (DockContextFindNodeByID(ctx, window.DockId) is { } node)
                    DockNodeAddWindow(node, window, true);
            }
        }
    }

    #endregion

    #region [SECTION] TABS: Canonical Dear ImGui Tab Bar & Tab Item Subsystem

    /// <summary>
    /// Temporary layout metrics for a section (leading, normal, trailing) of a tab bar.
    /// </summary>
    private sealed class ImGuiTabBarSection
    {
        /// <summary>Gets or sets the number of tabs in this section.</summary>
        public int TabCount;

        /// <summary>Gets or sets the total width of all tabs in this section.</summary>
        public float Width;

        /// <summary>Gets or sets the section width after shrinking minimum width tabs.</summary>
        public float WidthAfterShrinkMinWidth;

        /// <summary>Gets or sets the spacing between tabs in this section.</summary>
        public float Spacing;
    }

    /// <summary>
    /// Determines the section layout index (leading, normal, or trailing) of a tab item.
    /// </summary>
    /// <param name="tab">The tab item to inspect.</param>
    /// <returns>The section index (<c>0</c> for leading, <c>1</c> for standard, <c>2</c> for trailing).</returns>
    private static int TabItemGetSectionIdx(ImGuiTabItem tab)
    {
        return (tab.Flags & ImGuiTabItemFlags.Leading) != 0 ? 0 : (tab.Flags & ImGuiTabItemFlags.Trailing) != 0 ? 2 : 1;
    }

    /// <summary>
    /// Compares two tab items based on their layout section grouping.
    /// </summary>
    /// <param name="a">The first tab item.</param>
    /// <param name="b">The second tab item.</param>
    /// <returns>A negative integer if <paramref name="a"/> precedes <paramref name="b"/>; zero if equal; positive if greater.</returns>
    private static int TabItemComparerBySection(ImGuiTabItem a, ImGuiTabItem b)
    {
        int aSection = TabItemGetSectionIdx(a);
        int bSection = TabItemGetSectionIdx(b);
        if (aSection != bSection)
            return aSection - bSection;
        return a.IndexDuringLayout - b.IndexDuringLayout;
    }

    /// <summary>
    /// Compares two tab items based on the order in which they were submitted within the frame.
    /// </summary>
    /// <param name="a">The first tab item.</param>
    /// <param name="b">The second tab item.</param>
    /// <returns>A value indicating relative submission ordering.</returns>
    private static int TabItemComparerByBeginOrder(ImGuiTabItem a, ImGuiTabItem b)
    {
        return a.BeginOrder - b.BeginOrder;
    }

    /// <summary>
    /// Begins a manual/standalone TabBar layout container.
    /// </summary>
    /// <param name="str_id">Identifier string for the tab bar.</param>
    /// <param name="flags">Behavior and style flags.</param>
    /// <returns><c>true</c> if visible and can receive tab items; otherwise <c>false</c>.</returns>
    public static bool BeginTabBar(string str_id, ImGuiTabBarFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;

        uint id = window.GetID(str_id);
        ImGuiTabBar tabBar = g.TabBars.GetOrAddByKey(id);
        ImRect tabBarBb = new(window.DC.CursorPos.X, window.DC.CursorPos.Y, window.WorkRect.Max.X, window.DC.CursorPos.Y + g.FontSize + g.Style.FramePadding.Y * 2.0f);
        tabBar.ID = id;
        tabBar.SeparatorMinX = tabBarBb.Min.X - MathF.Truncate(window.WindowPadding.X * 0.5f);
        tabBar.SeparatorMaxX = tabBarBb.Max.X + MathF.Truncate(window.WindowPadding.X * 0.5f);
        flags |= (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_IsFocused;
        return BeginTabBarEx(tabBar, tabBarBb, flags);
    }

    /// <summary>
    /// Begins internal submission of an ImGuiTabBar.
    /// </summary>
    /// <param name="tab_bar">Tab bar to begin.</param>
    /// <param name="tab_bar_bb">Bounding rectangle for the tab bar.</param>
    /// <param name="flags">Tab bar behavior flags.</param>
    /// <returns><c>true</c> if visible; otherwise <c>false</c>.</returns>
    public static bool BeginTabBarEx(ImGuiTabBar tab_bar, ImRect tab_bar_bb, ImGuiTabBarFlags flags)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;

        ImAssert.Check(tab_bar.ID != 0);
        if ((flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) == 0)
            PushOverrideID(tab_bar.ID);

        g.CurrentTabBarStack.Add(tab_bar);
        g.CurrentTabBar = tab_bar;
        tab_bar.Window = window;

        tab_bar.BackupCursorPos = window.DC.CursorPos;
        if (tab_bar.CurrFrameVisible == g.FrameCount)
        {
            window.DC.CursorPos = new ImVec2(tab_bar.BarRect.Min.X, tab_bar.BarRect.Max.Y + tab_bar.ItemSpacingY);
            tab_bar.BeginCount++;
            return true;
        }

        if ((flags & ImGuiTabBarFlags.Reorderable) != (tab_bar.Flags & ImGuiTabBarFlags.Reorderable) || (tab_bar.TabsAddedNew && (flags & ImGuiTabBarFlags.Reorderable) == 0))
        {
            if ((flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) == 0)
                tab_bar.Tabs.Sort(TabItemComparerByBeginOrder);
        }
        tab_bar.TabsAddedNew = false;

        if ((flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMask_) == 0)
            flags |= (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyDefault_;

        tab_bar.Flags = flags;
        tab_bar.BarRect = tab_bar_bb;
        tab_bar.WantLayout = true;
        tab_bar.PrevFrameVisible = tab_bar.CurrFrameVisible;
        tab_bar.CurrFrameVisible = g.FrameCount;
        tab_bar.PrevTabsContentsHeight = tab_bar.CurrTabsContentsHeight;
        tab_bar.CurrTabsContentsHeight = 0.0f;
        tab_bar.ItemSpacingY = g.Style.ItemSpacing.Y;
        tab_bar.FramePadding = g.Style.FramePadding;
        tab_bar.TabsActiveCount = 0;
        tab_bar.LastTabItemIdx = -1;
        tab_bar.BeginCount = 1;

        window.DC.CursorPos = new ImVec2(tab_bar.BarRect.Min.X, tab_bar.BarRect.Max.Y + tab_bar.ItemSpacingY);

        uint col = GetColorU32((flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_IsFocused) != 0 ? ImGuiCol.TabSelected : ImGuiCol.TabDimmedSelected);
        if (g.Style.TabBarBorderSize > 0.0f)
        {
            float y = tab_bar.BarRect.Max.Y;
            window.DrawList.AddRectFilled(new ImVec2(tab_bar.SeparatorMinX, y - g.Style.TabBarBorderSize), new ImVec2(tab_bar.SeparatorMaxX, y), col);
        }
        return true;
    }

    /// <summary>
    /// Ends an ImGuiTabBar submission.
    /// </summary>
    public static void EndTabBar()
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        ImGuiTabBar? tab_bar = g.CurrentTabBar;
        AssertUserError(tab_bar != null, "Mismatched BeginTabBar()/EndTabBar()!");
        if (tab_bar == null)
            return;

        if (tab_bar.WantLayout)
            TabBarLayout(tab_bar);

        bool tabBarAppearing = tab_bar.PrevFrameVisible + 1 < g.FrameCount;
        if (tab_bar.VisibleTabWasSubmitted || tab_bar.VisibleTabId == 0 || tabBarAppearing)
        {
            tab_bar.CurrTabsContentsHeight = MathF.Max(window.DC.CursorPos.Y - tab_bar.BarRect.Max.Y, tab_bar.CurrTabsContentsHeight);
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, tab_bar.BarRect.Max.Y + tab_bar.CurrTabsContentsHeight);
        }
        else
        {
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X, tab_bar.BarRect.Max.Y + tab_bar.PrevTabsContentsHeight);
        }
        if (tab_bar.BeginCount > 1)
            window.DC.CursorPos = tab_bar.BackupCursorPos;

        tab_bar.LastTabItemIdx = -1;
        if ((tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) == 0)
            PopID();

        if (g.CurrentTabBarStack.Count > 0)
            g.CurrentTabBarStack.RemoveAt(g.CurrentTabBarStack.Count - 1);
        g.CurrentTabBar = g.CurrentTabBarStack.Count == 0 ? null : g.CurrentTabBarStack[^1];
    }

    /// <summary>
    /// Begins a tab item within the active tab bar with an optional close button.
    /// </summary>
    /// <param name="label">Tab display label.</param>
    /// <param name="p_open">Reference tracking whether the tab is open.</param>
    /// <param name="flags">Tab item behavior flags.</param>
    /// <returns><c>true</c> if the tab is selected and its content scope is open; otherwise <c>false</c>.</returns>
    public static bool BeginTabItem(string label, ref bool p_open, ImGuiTabItemFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;

        ImGuiTabBar? tabBar = g.CurrentTabBar;
        AssertUserError(tabBar != null, "Needs to be called between BeginTabBar() and EndTabBar()!");
        if (tabBar == null)
            return false;
        ImAssert.Check((flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) == 0);

        bool ret = TabItemEx(tabBar, label, ref p_open, flags, null);
        if (ret && (flags & ImGuiTabItemFlags.NoPushId) == 0)
        {
            ImGuiTabItem tab = tabBar.Tabs[tabBar.LastTabItemIdx];
            PushOverrideID(tab.ID);
        }
        return ret;
    }

    /// <summary>
    /// Begins a tab item within the active tab bar without a close button.
    /// </summary>
    /// <param name="label">Tab display label.</param>
    /// <param name="flags">Tab item behavior flags.</param>
    /// <returns><c>true</c> if the tab is selected and its content scope is open; otherwise <c>false</c>.</returns>
    public static bool BeginTabItem(string label, ImGuiTabItemFlags flags = 0)
    {
        bool dummy = true;
        return BeginTabItem(label, ref dummy, flags | (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_NoCloseButton);
    }

    /// <summary>
    /// Ends a tab item submission, popping its ID from the stack.
    /// </summary>
    public static void EndTabItem()
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        ImGuiTabBar? tabBar = g.CurrentTabBar;
        AssertUserError(tabBar != null, "Needs to be called between BeginTabBar() and EndTabBar()!");
        if (tabBar == null)
            return;
        ImAssert.Check(tabBar.LastTabItemIdx >= 0);
        ImGuiTabItem tab = tabBar.Tabs[tabBar.LastTabItemIdx];
        if ((tab.Flags & ImGuiTabItemFlags.NoPushId) == 0)
            PopID();
    }

    /// <summary>
    /// Creates a tab item styled as a button that triggers an action without switching tabs.
    /// </summary>
    /// <param name="label">Button label.</param>
    /// <param name="flags">Tab item flags.</param>
    /// <returns><c>true</c> if clicked; otherwise <c>false</c>.</returns>
    public static bool TabItemButton(string label, ImGuiTabItemFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;

        ImGuiTabBar? tabBar = g.CurrentTabBar;
        AssertUserError(tabBar != null, "Needs to be called between BeginTabBar() and EndTabBar()!");
        if (tabBar == null)
            return false;

        return TabItemEx(tabBar, label, flags | (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button | ImGuiTabItemFlags.NoReorder, null);
    }

    /// <summary>
    /// Requests a tab item to be marked as closed by its label.
    /// </summary>
    /// <param name="tab_or_docked_window_label">Label of the tab to close.</param>
    public static void SetTabItemClosed(string tab_or_docked_window_label)
    {
        ImGuiContext g = GImGui!;
        bool isWithinManualTabBar = g.CurrentTabBar != null && (g.CurrentTabBar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) == 0;
        if (isWithinManualTabBar)
        {
            ImGuiTabBar tabBar = g.CurrentTabBar!;
            uint tabId = TabBarCalcTabID(tabBar, tab_or_docked_window_label, null);
            ImGuiTabItem? tab = TabBarFindTabByID(tabBar, tabId);
            if (tab != null)
                tab.WantClose = true;
        }
        else
        {
            ImGuiWindow? window = FindWindowByName(tab_or_docked_window_label);
            if (window != null && window.DockIsActive && window.DockNode != null)
            {
                ImGuiDockNode node = window.DockNode;
                if (node.TabBar != null)
                {
                    uint tabId = TabBarCalcTabID(node.TabBar, tab_or_docked_window_label, window);
                    TabBarRemoveTab(node.TabBar, tabId);
                    window.DockTabWantClose = true;
                }
            }
        }
    }

    /// <summary>
    /// Reserves horizontal spacing within a tab bar.
    /// </summary>
    /// <param name="str_id">Identifier for spacing.</param>
    /// <param name="flags">Flags.</param>
    /// <param name="width">Width in pixels.</param>
    public static void TabItemSpacing(string str_id, ImGuiTabItemFlags flags, float width)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        ImGuiTabBar? tabBar = g.CurrentTabBar;
        AssertUserError(tabBar != null, "Needs to be called between BeginTabBar() and EndTabBar()!");
        if (tabBar == null)
            return;

        SetNextItemWidth(width);
        TabItemEx(tabBar, str_id, flags | (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button | ImGuiTabItemFlags.NoReorder | (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Invisible, null);
    }

    /// <summary>
    /// Computes the available horizontal pixel width for scrollable tabs within a tab bar.
    /// </summary>
    /// <param name="tabBar">The tab bar being measured.</param>
    /// <param name="sections">The section array containing tab items.</param>
    /// <returns>The available scrollable width in pixels.</returns>
    private static float TabBarCalcScrollableWidth(ImGuiTabBar tabBar, ImGuiTabBarSection[] sections)
    {
        return tabBar.BarRect.GetWidth() - sections[0].Width - sections[2].Width - sections[1].Spacing;
    }

    /// <summary>
    /// Calculates layout positions, tab dimensions, clipping, and scroll bounds for a tab bar.
    /// </summary>
    /// <param name="tab_bar">The tab bar to arrange.</param>
    private static void TabBarLayout(ImGuiTabBar tab_bar)
    {
        ImGuiContext g = GImGui!;
        tab_bar.WantLayout = false;

        bool scrollToSelectedTab = tab_bar.BarRectPrevWidth > tab_bar.BarRect.GetWidth();
        tab_bar.BarRectPrevWidth = tab_bar.BarRect.GetWidth();

        int tabDstN = 0;
        bool needSortBySection = false;
        ImGuiTabBarSection[] sections = new ImGuiTabBarSection[3];
        for (int i = 0; i < 3; i++) sections[i] = new ImGuiTabBarSection();

        for (int tabSrcN = 0; tabSrcN < tab_bar.Tabs.Count; tabSrcN++)
        {
            ImGuiTabItem tab = tab_bar.Tabs[tabSrcN];
            if (tab.LastFrameVisible < tab_bar.PrevFrameVisible || tab.WantClose)
            {
                if (tab_bar.VisibleTabId == tab.ID) tab_bar.VisibleTabId = 0;
                if (tab_bar.SelectedTabId == tab.ID) tab_bar.SelectedTabId = 0;
                if (tab_bar.NextSelectedTabId == tab.ID) tab_bar.NextSelectedTabId = 0;
                continue;
            }
            if (tabDstN != tabSrcN)
                tab_bar.Tabs[tabDstN] = tab_bar.Tabs[tabSrcN];

            tab = tab_bar.Tabs[tabDstN];
            tab.IndexDuringLayout = (short)tabDstN;

            int currSectionN = TabItemGetSectionIdx(tab);
            if (tabDstN > 0)
            {
                ImGuiTabItem prevTab = tab_bar.Tabs[tabDstN - 1];
                int prevSectionN = TabItemGetSectionIdx(prevTab);
                if (currSectionN == 0 && prevSectionN != 0)
                    needSortBySection = true;
                if (prevSectionN == 2 && currSectionN != 2)
                    needSortBySection = true;
            }
            sections[currSectionN].TabCount++;
            tabDstN++;
        }
        if (tab_bar.Tabs.Count > tabDstN)
            tab_bar.Tabs.RemoveRange(tabDstN, tab_bar.Tabs.Count - tabDstN);

        if (needSortBySection)
            tab_bar.Tabs.Sort(TabItemComparerBySection);

        float tabSpacing = g.Style.ItemInnerSpacing.X;
        sections[0].Spacing = sections[0].TabCount > 0 && (sections[1].TabCount + sections[2].TabCount) > 0 ? tabSpacing : 0.0f;
        sections[1].Spacing = sections[1].TabCount > 0 && sections[2].TabCount > 0 ? tabSpacing : 0.0f;

        uint scrollToTabId = 0;
        if (tab_bar.NextScrollToTabId != 0)
        {
            scrollToTabId = tab_bar.NextScrollToTabId;
            tab_bar.NextScrollToTabId = 0;
        }
        if (tab_bar.NextSelectedTabId != 0)
        {
            tab_bar.SelectedTabId = tab_bar.NextSelectedTabId;
            tab_bar.NextSelectedTabId = 0;
            scrollToTabId = tab_bar.SelectedTabId;
        }
        if (tab_bar.ReorderRequestTabId != 0)
        {
            if (TabBarProcessReorder(tab_bar))
                if (tab_bar.ReorderRequestTabId == tab_bar.SelectedTabId)
                    scrollToTabId = tab_bar.ReorderRequestTabId;
            tab_bar.ReorderRequestTabId = 0;
        }
        if ((tab_bar.Flags & ImGuiTabBarFlags.TabListPopupButton) != 0)
        {
            ImGuiTabItem? tabToSelect = TabBarTabListPopupButton(tab_bar);
            if (tabToSelect != null)
                scrollToTabId = tab_bar.SelectedTabId = tabToSelect.ID;
        }

        int[] shrinkBufferIndexes = new int[3] { 0, sections[0].TabCount + sections[2].TabCount, sections[0].TabCount };
        while (g.ShrinkWidthBuffer.Count < tab_bar.Tabs.Count)
            g.ShrinkWidthBuffer.Add(new ImGuiShrinkWidthItem());
        if (g.ShrinkWidthBuffer.Count > tab_bar.Tabs.Count)
            g.ShrinkWidthBuffer.RemoveRange(tab_bar.Tabs.Count, g.ShrinkWidthBuffer.Count - tab_bar.Tabs.Count);

        float shrinkMinWidth = (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMixed) != 0 ? g.Style.TabMinWidthShrink : 1.0f;

        ImGuiTabItem? mostRecentlySelectedTab = null;
        int currSection = -1;
        bool foundSelectedTabId = false;

        for (int tabN = 0; tabN < tab_bar.Tabs.Count; tabN++)
        {
            ImGuiTabItem tab = tab_bar.Tabs[tabN];
            ImAssert.Check(tab.LastFrameVisible >= tab_bar.PrevFrameVisible);

            if ((mostRecentlySelectedTab == null || mostRecentlySelectedTab.LastFrameSelected < tab.LastFrameSelected) && (tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) == 0)
                mostRecentlySelectedTab = tab;
            if (tab.ID == tab_bar.SelectedTabId)
                foundSelectedTabId = true;
            if (scrollToTabId == 0 && g.NavJustMovedToId == tab.ID)
                scrollToTabId = tab.ID;

            string tabName = TabBarGetTabName(tab_bar, tab);
            bool hasCloseButtonOrUnsavedMarker = (tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_NoCloseButton) == 0 || (tab.Flags & ImGuiTabItemFlags.UnsavedDocument) != 0;
            tab.ContentWidth = tab.RequestedWidth >= 0.0f ? tab.RequestedWidth : TabItemCalcSize(tabName, hasCloseButtonOrUnsavedMarker).X;
            if ((tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) == 0)
                tab.ContentWidth = MathF.Max(tab.ContentWidth, g.Style.TabMinWidthBase);

            int sectionN = TabItemGetSectionIdx(tab);
            ImGuiTabBarSection section = sections[sectionN];
            section.Width += tab.ContentWidth + (sectionN == currSection ? tabSpacing : 0.0f);
            section.WidthAfterShrinkMinWidth += MathF.Min(tab.ContentWidth, shrinkMinWidth) + (sectionN == currSection ? tabSpacing : 0.0f);
            currSection = sectionN;

            ImGuiShrinkWidthItem shrinkWidthItem = g.ShrinkWidthBuffer[shrinkBufferIndexes[sectionN]++];
            shrinkWidthItem.Index = tabN;
            shrinkWidthItem.Width = shrinkWidthItem.InitialWidth = tab.ContentWidth;
            tab.Width = MathF.Max(tab.ContentWidth, 1.0f);
        }

        float widthAllTabsAfterMinWidthShrink = 0.0f;
        tab_bar.WidthAllTabsIdeal = 0.0f;
        for (int sectionN = 0; sectionN < 3; sectionN++)
        {
            tab_bar.WidthAllTabsIdeal += sections[sectionN].Width + sections[sectionN].Spacing;
            widthAllTabsAfterMinWidthShrink += sections[sectionN].WidthAfterShrinkMinWidth + sections[sectionN].Spacing;
        }

        bool canScroll = (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyScroll) != 0 || (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMixed) != 0;
        float widthAllTabsToUseForScroll = (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyScroll) != 0 ? tab_bar.WidthAllTabs : widthAllTabsAfterMinWidthShrink;
        tab_bar.ScrollButtonEnabled = (widthAllTabsToUseForScroll > tab_bar.BarRect.GetWidth() && tab_bar.Tabs.Count > 1) && (tab_bar.Flags & ImGuiTabBarFlags.NoTabListScrollingButtons) == 0 && canScroll;
        if (tab_bar.ScrollButtonEnabled)
        {
            ImGuiTabItem? scrollAndSelectTab = TabBarScrollingButtons(tab_bar);
            if (scrollAndSelectTab != null)
            {
                scrollToTabId = scrollAndSelectTab.ID;
                if ((scrollAndSelectTab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) == 0)
                    tab_bar.SelectedTabId = scrollToTabId;
            }
        }
        if (scrollToTabId == 0 && scrollToSelectedTab)
            scrollToTabId = tab_bar.SelectedTabId;

        float section0W = sections[0].Width + sections[0].Spacing;
        float section1W = sections[1].Width + sections[1].Spacing;
        float section2W = sections[2].Width + sections[2].Spacing;
        bool centralSectionIsVisible = (section0W + section2W) < tab_bar.BarRect.GetWidth();
        float widthExcess = centralSectionIsVisible
            ? MathF.Max(section1W - (tab_bar.BarRect.GetWidth() - section0W - section2W), 0.0f)
            : (section0W + section2W) - tab_bar.BarRect.GetWidth();

        bool canShrink = (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyShrink) != 0 || (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlags.ImGuiTabBarFlags_FittingPolicyMixed) != 0;
        if (widthExcess >= 1.0f && (canShrink || !centralSectionIsVisible))
        {
            int shrinkDataCount = centralSectionIsVisible ? sections[1].TabCount : sections[0].TabCount + sections[2].TabCount;
            int shrinkDataOffset = centralSectionIsVisible ? sections[0].TabCount + sections[2].TabCount : 0;
            ShrinkWidths(g.ShrinkWidthBuffer, shrinkDataOffset, shrinkDataCount, widthExcess, shrinkMinWidth);

            for (int tabN = shrinkDataOffset; tabN < shrinkDataOffset + shrinkDataCount; tabN++)
            {
                ImGuiTabItem tab = tab_bar.Tabs[g.ShrinkWidthBuffer[tabN].Index];
                float shrinkedWidth = MathF.Truncate(g.ShrinkWidthBuffer[tabN].Width);
                if (shrinkedWidth < 0.0f)
                    continue;

                shrinkedWidth = MathF.Max(1.0f, shrinkedWidth);
                int sN = TabItemGetSectionIdx(tab);
                sections[sN].Width -= (tab.Width - shrinkedWidth);
                tab.Width = shrinkedWidth;
            }
        }

        int sectionTabIndex = 0;
        float tabOffset = 0.0f;
        tab_bar.WidthAllTabs = 0.0f;
        for (int sectionN = 0; sectionN < 3; sectionN++)
        {
            ImGuiTabBarSection section = sections[sectionN];
            if (sectionN == 2)
                tabOffset = MathF.Min(MathF.Max(0.0f, tab_bar.BarRect.GetWidth() - section.Width), tabOffset);

            for (int tabN = 0; tabN < section.TabCount; tabN++)
            {
                ImGuiTabItem tab = tab_bar.Tabs[sectionTabIndex + tabN];
                tab.Offset = tabOffset;
                tab.NameOffset = -1;
                tabOffset += tab.Width + (tabN < section.TabCount - 1 ? g.Style.ItemInnerSpacing.X : 0.0f);
            }
            tab_bar.WidthAllTabs += MathF.Max(section.Width + section.Spacing, 0.0f);
            tabOffset += section.Spacing;
            sectionTabIndex += section.TabCount;
        }

        tab_bar.TabsNames.Clear();

        bool tabBarAppearing = tab_bar.PrevFrameVisible + 1 < g.FrameCount;
        if (!foundSelectedTabId && !tabBarAppearing)
            tab_bar.SelectedTabId = 0;
        if (tab_bar.SelectedTabId == 0 && tab_bar.NextSelectedTabId == 0 && mostRecentlySelectedTab != null)
            scrollToTabId = tab_bar.SelectedTabId = mostRecentlySelectedTab.ID;

        tab_bar.VisibleTabId = tab_bar.SelectedTabId;
        tab_bar.VisibleTabWasSubmitted = false;

        if (g.NavWindowingTarget != null && g.NavWindowingTarget.DockNode != null && g.NavWindowingTarget.DockNode.TabBar == tab_bar)
            tab_bar.VisibleTabId = scrollToTabId = g.NavWindowingTarget.TabId;

        if (scrollToTabId != 0)
            TabBarScrollToTab(tab_bar, scrollToTabId, sections);
        else if (tab_bar.ScrollButtonEnabled && IsMouseHoveringRect(tab_bar.BarRect.Min, tab_bar.BarRect.Max, true) && IsWindowContentHoverable(g.CurrentWindow!))
        {
            float wheel = g.IO.MouseWheelRequestAxisSwap ? g.IO.MouseWheel : g.IO.MouseWheelH;
            ImGuiKey wheelKey = g.IO.MouseWheelRequestAxisSwap ? ImGuiKey.MouseWheelY : ImGuiKey.MouseWheelX;
            if (TestKeyOwner(wheelKey, tab_bar.ID) && wheel != 0.0f)
            {
                float scrollStep = wheel * TabBarCalcScrollableWidth(tab_bar, sections) / 3.0f;
                tab_bar.ScrollingTargetDistToVisibility = 0.0f;
                tab_bar.ScrollingTarget = TabBarScrollClamp(tab_bar, tab_bar.ScrollingTarget - scrollStep);
            }
            SetKeyOwner(wheelKey, tab_bar.ID);
        }

        tab_bar.ScrollingAnim = TabBarScrollClamp(tab_bar, tab_bar.ScrollingAnim);
        tab_bar.ScrollingTarget = TabBarScrollClamp(tab_bar, tab_bar.ScrollingTarget);
        if (tab_bar.ScrollingAnim != tab_bar.ScrollingTarget)
        {
            tab_bar.ScrollingSpeed = MathF.Max(tab_bar.ScrollingSpeed, 70.0f * g.FontSize);
            tab_bar.ScrollingSpeed = MathF.Max(tab_bar.ScrollingSpeed, MathF.Abs(tab_bar.ScrollingTarget - tab_bar.ScrollingAnim) / 0.3f);
            bool teleport = (tab_bar.PrevFrameVisible + 1 < g.FrameCount) || (tab_bar.ScrollingTargetDistToVisibility > 10.0f * g.FontSize);
            tab_bar.ScrollingAnim = teleport ? tab_bar.ScrollingTarget : ImLinearSweep(tab_bar.ScrollingAnim, tab_bar.ScrollingTarget, g.IO.DeltaTime * tab_bar.ScrollingSpeed);
        }
        else
        {
            tab_bar.ScrollingSpeed = 0.0f;
        }
        tab_bar.ScrollingRectMinX = tab_bar.BarRect.Min.X + sections[0].Width + sections[0].Spacing;
        tab_bar.ScrollingRectMaxX = tab_bar.BarRect.Max.X - sections[2].Width - sections[1].Spacing;

        ImGuiWindow window = g.CurrentWindow!;
        window.DC.CursorPos = tab_bar.BarRect.Min;
        ItemSize(new ImVec2(tab_bar.WidthAllTabs, tab_bar.BarRect.GetHeight()), tab_bar.FramePadding.Y);
        window.DC.IdealMaxPos = new ImVec2(MathF.Max(window.DC.IdealMaxPos.X, tab_bar.BarRect.Min.X + tab_bar.WidthAllTabsIdeal), window.DC.IdealMaxPos.Y);
    }

    /// <summary>
    /// Clamps a tab-bar horizontal scroll position to the valid range between the left edge and the
    /// rightmost scrollable extent.
    /// </summary>
    /// <param name="tab_bar">Tab bar whose scroll position is being clamped.</param>
    /// <param name="scrolling">Candidate scroll offset.</param>
    /// <returns>The clamped scroll offset, never below zero.</returns>
    private static float TabBarScrollClamp(ImGuiTabBar tab_bar, float scrolling)
    {
        scrolling = MathF.Min(scrolling, tab_bar.WidthAllTabs - tab_bar.BarRect.GetWidth());
        return MathF.Max(scrolling, 0.0f);
    }

    /// <summary>
    /// Adjusts tab bar scrolling targets to bring a specific tab into view.
    /// </summary>
    /// <param name="tabBar">The tab bar.</param>
    /// <param name="tabId">The identifier of the tab to scroll to.</param>
    /// <param name="sections">The tab bar sections containing tabs.</param>
    private static void TabBarScrollToTab(ImGuiTabBar tabBar, uint tabId, ImGuiTabBarSection[] sections)
    {
        ImGuiTabItem? tab = TabBarFindTabByID(tabBar, tabId);
        if (tab == null)
            return;
        if ((tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_) != 0)
            return;

        ImGuiContext g = GImGui!;
        float margin = g.FontSize * 1.0f;
        int order = TabBarGetTabOrder(tabBar, tab);

        float scrollableWidth = TabBarCalcScrollableWidth(tabBar, sections);
        float tabX1 = tab.Offset - sections[0].Width + (order > sections[0].TabCount - 1 ? -margin : 0.0f);
        float tabX2 = tab.Offset - sections[0].Width + tab.Width + (order + 1 < tabBar.Tabs.Count - sections[2].TabCount ? margin : 1.0f);
        tabBar.ScrollingTargetDistToVisibility = 0.0f;
        if (tabBar.ScrollingTarget > tabX1 || (tabX2 - tabX1 >= scrollableWidth))
        {
            tabBar.ScrollingTargetDistToVisibility = MathF.Max(tabBar.ScrollingAnim - tabX2, 0.0f);
            tabBar.ScrollingTarget = tabX1;
        }
        else if (tabBar.ScrollingTarget < tabX2 - scrollableWidth)
        {
            tabBar.ScrollingTargetDistToVisibility = MathF.Max((tabX1 - scrollableWidth) - tabBar.ScrollingAnim, 0.0f);
            tabBar.ScrollingTarget = tabX2 - scrollableWidth;
        }
    }

    /// <summary>
    /// Renders and processes left/right navigation scroll buttons for overflowing tab bars.
    /// </summary>
    /// <param name="tabBar">The tab bar.</param>
    /// <returns>The tab item activated by scroll navigation, if any.</returns>
    private static ImGuiTabItem? TabBarScrollingButtons(ImGuiTabBar tabBar)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;

        ImVec2 arrowButtonSize = new(g.FontSize - 2.0f, g.FontSize + g.Style.FramePadding.Y * 2.0f);
        float scrollingButtonsWidth = arrowButtonSize.X * 2.0f;
        ImVec2 backupCursorPos = window.DC.CursorPos;

        int selectDir = 0;
        ImVec4 arrowCol = g.Style.Colors[(int)ImGuiCol.Text];
        arrowCol.W *= 0.5f;

        PushStyleColor(ImGuiCol.Text, arrowCol);
        PushStyleColor(ImGuiCol.Button, new ImVec4(0, 0, 0, 0));
        PushItemFlag(ImGuiItemFlags.ButtonRepeat | ImGuiItemFlags.NoNav, true);
        float backupRepeatDelay = g.IO.KeyRepeatDelay;
        float backupRepeatRate = g.IO.KeyRepeatRate;
        g.IO.KeyRepeatDelay = 0.250f;
        g.IO.KeyRepeatRate = 0.200f;

        float x = MathF.Max(tabBar.BarRect.Min.X, tabBar.BarRect.Max.X - scrollingButtonsWidth);
        window.DC.CursorPos = new ImVec2(x, tabBar.BarRect.Min.Y);
        if (ArrowButtonEx("##<", ImGuiDir.Left, arrowButtonSize, (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick))
            selectDir = -1;
        window.DC.CursorPos = new ImVec2(x + arrowButtonSize.X, tabBar.BarRect.Min.Y);
        if (ArrowButtonEx("##>", ImGuiDir.Right, arrowButtonSize, (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick))
            selectDir = +1;

        PopItemFlag();
        PopStyleColor(2);
        g.IO.KeyRepeatRate = backupRepeatRate;
        g.IO.KeyRepeatDelay = backupRepeatDelay;

        ImGuiTabItem? tabToScrollTo = null;
        if (selectDir != 0)
        {
            ImGuiTabItem? tab = TabBarFindTabByID(tabBar, tabBar.SelectedTabId);
            if (tab != null)
            {
                int selectedOrder = TabBarGetTabOrder(tabBar, tab);
                int targetOrder = selectedOrder + selectDir;

                while (tabToScrollTo == null)
                {
                    tabToScrollTo = tabBar.Tabs[(targetOrder >= 0 && targetOrder < tabBar.Tabs.Count) ? targetOrder : selectedOrder];
                    if ((tabToScrollTo.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) != 0)
                    {
                        targetOrder += selectDir;
                        selectedOrder += selectDir;
                        tabToScrollTo = (targetOrder < 0 || targetOrder >= tabBar.Tabs.Count) ? tabToScrollTo : null;
                    }
                }
            }
        }

        window.DC.CursorPos = backupCursorPos;
        tabBar.BarRect = new ImRect(tabBar.BarRect.Min, new ImVec2(tabBar.BarRect.Max.X - (scrollingButtonsWidth + 1.0f), tabBar.BarRect.Max.Y));
        return tabToScrollTo;
    }

    /// <summary>
    /// Renders and processes the dropdown tab list popup button when tabs overflow.
    /// </summary>
    /// <param name="tabBar">The tab bar.</param>
    /// <returns>The tab item selected from the dropdown popup, if any.</returns>
    private static ImGuiTabItem? TabBarTabListPopupButton(ImGuiTabBar tabBar)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow window = g.CurrentWindow!;

        float tabListPopupButtonWidth = g.FontSize + g.Style.FramePadding.Y;
        ImVec2 backupCursorPos = window.DC.CursorPos;
        window.DC.CursorPos = new ImVec2(tabBar.BarRect.Min.X - g.Style.FramePadding.Y, tabBar.BarRect.Min.Y);
        tabBar.BarRect = new ImRect(new ImVec2(tabBar.BarRect.Min.X + tabListPopupButtonWidth, tabBar.BarRect.Min.Y), tabBar.BarRect.Max);

        ImVec4 arrowCol = g.Style.Colors[(int)ImGuiCol.Text];
        arrowCol.W *= 0.5f;
        PushStyleColor(ImGuiCol.Text, arrowCol);
        PushStyleColor(ImGuiCol.Button, new ImVec4(0, 0, 0, 0));
        bool open = BeginCombo("##v", null, ImGuiComboFlags.NoPreview | ImGuiComboFlags.HeightLargest);
        PopStyleColor(2);

        ImGuiTabItem? tabToSelect = null;
        if (open)
        {
            for (int tabN = 0; tabN < tabBar.Tabs.Count; tabN++)
            {
                ImGuiTabItem tab = tabBar.Tabs[tabN];
                if ((tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) != 0)
                    continue;

                string tabName = TabBarGetTabName(tabBar, tab);
                if (Selectable(tabName, tabBar.SelectedTabId == tab.ID))
                    tabToSelect = tab;
            }
            EndCombo();
        }

        window.DC.CursorPos = backupCursorPos;
        return tabToSelect;
    }

    /// <summary>
    /// Calculates the ID of a tab item based on string label and dock host window identity.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="label">Tab display label.</param>
    /// <param name="docked_window">Window associated with the tab, if any.</param>
    /// <returns>Unique 32-bit identifier.</returns>
    public static uint TabBarCalcTabID(ImGuiTabBar tab_bar, string label, ImGuiWindow? docked_window)
    {
        if (docked_window != null)
        {
            ImAssert.Check((tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) != 0);
            uint id = docked_window.TabId;
            KeepAliveID(id);
            return id;
        }
        else
        {
            ImGuiWindow window = GImGui!.CurrentWindow!;
            return window.GetID(label);
        }
    }

    /// <summary>
    /// Calculates the maximum width permitted for any individual tab.
    /// </summary>
    /// <returns>Maximum tab width in pixels.</returns>
    public static float TabBarCalcMaxTabWidth()
    {
        ImGuiContext g = GImGui!;
        return g.FontSize * 20.0f;
    }

    /// <summary>
    /// Finds a tab bar by its identifier from the context pool.
    /// </summary>
    /// <param name="id">Tab bar identifier.</param>
    /// <returns>Matching tab bar or <c>null</c>.</returns>
    public static ImGuiTabBar? TabBarFindByID(uint id)
    {
        ImGuiContext g = GImGui!;
        return g.TabBars.GetByKey(id);
    }

    /// <summary>
    /// Removes a tab bar from the context pool.
    /// </summary>
    /// <param name="tab_bar">Tab bar to remove.</param>
    public static void TabBarRemove(ImGuiTabBar tab_bar)
    {
        ImGuiContext g = GImGui!;
        g.TabBars.Remove(tab_bar.ID, tab_bar);
    }

    /// <summary>
    /// Submits a single tab item within the current tab bar, with a close button whose open state is
    /// tracked by <paramref name="p_open"/>.
    /// </summary>
    /// <param name="tab_bar">Tab bar containing the item.</param>
    /// <param name="label">Tab display label.</param>
    /// <param name="p_open">Close state reference; set to <c>false</c> when the close button is pressed.</param>
    /// <param name="flags">Tab item flags.</param>
    /// <param name="docked_window">Window associated with this tab item, if any.</param>
    /// <returns><c>true</c> if selected; otherwise <c>false</c>.</returns>
    public static bool TabItemEx(ImGuiTabBar tab_bar, string label, ref bool p_open, ImGuiTabItemFlags flags, ImGuiWindow? docked_window = null)
        => TabItemExCore(tab_bar, label, ref p_open, hasPOpen: true, flags, docked_window);

    /// <summary>
    /// Submits a single tab item within the current tab bar without a close button. This is the managed
    /// equivalent of canonical's <c>p_open == NULL</c> call form.
    /// </summary>
    /// <param name="tab_bar">Tab bar containing the item.</param>
    /// <param name="label">Tab display label.</param>
    /// <param name="flags">Tab item flags.</param>
    /// <param name="docked_window">Window associated with this tab item, if any.</param>
    /// <returns><c>true</c> if selected; otherwise <c>false</c>.</returns>
    public static bool TabItemEx(ImGuiTabBar tab_bar, string label, ImGuiTabItemFlags flags = 0, ImGuiWindow? docked_window = null)
    {
        bool pOpen = false; // Never observed: the core skips every p_open read and write when hasPOpen is false.
        return TabItemExCore(tab_bar, label, ref pOpen, hasPOpen: false, flags, docked_window);
    }

    /// <summary>
    /// Shared implementation of the two <see cref="TabItemEx(ImGuiTabBar, string, ref bool, ImGuiTabItemFlags, ImGuiWindow?)"/>
    /// call forms. Canonical expresses the distinction with a nullable <c>bool* p_open</c>; C# cannot
    /// pass a null <c>ref</c>, so <paramref name="hasPOpen"/> carries the sentinel.
    /// </summary>
    /// <param name="tab_bar">Tab bar containing the item.</param>
    /// <param name="label">Tab display label.</param>
    /// <param name="p_open">Close state reference, read and written only when <paramref name="hasPOpen"/> is true.</param>
    /// <param name="hasPOpen">Whether a caller-supplied close state is present.</param>
    /// <param name="flags">Tab item flags.</param>
    /// <param name="docked_window">Window associated with this tab item, if any.</param>
    /// <returns><c>true</c> if selected; otherwise <c>false</c>.</returns>
    private static bool TabItemExCore(ImGuiTabBar tab_bar, string label, ref bool p_open, bool hasPOpen, ImGuiTabItemFlags flags, ImGuiWindow? docked_window)
    {
        ImGuiContext g = GImGui!;
        if (tab_bar.WantLayout)
        {
            ImGuiNextItemData backupNextItemData = g.NextItemData;
            TabBarLayout(tab_bar);
            g.NextItemData = backupNextItemData;
        }
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;

        ImGuiStyle style = g.Style;
        uint id = TabBarCalcTabID(tab_bar, label, docked_window);

        bool pOpen = hasPOpen;
        if (pOpen && !p_open)
        {
            ItemAdd(new ImRect(), id, null, ImGuiItemFlags.NoNav);
            return false;
        }

        ImAssert.Check(!pOpen || (flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) == 0);
        ImAssert.Check((flags & (ImGuiTabItemFlags.Leading | ImGuiTabItemFlags.Trailing)) != (ImGuiTabItemFlags.Leading | ImGuiTabItemFlags.Trailing));

        // Normalize p_open and the NoCloseButton flag onto each other so later code has one source of truth.
        if ((flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_NoCloseButton) != 0)
            pOpen = false;
        else if (!pOpen)
            flags |= (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_NoCloseButton;

        ImGuiTabItem? tab = TabBarFindTabByID(tab_bar, id);
        bool tabIsNew = false;
        if (tab == null)
        {
            tab = new ImGuiTabItem { ID = id };
            tab_bar.Tabs.Add(tab);
            tab_bar.TabsAddedNew = tabIsNew = true;
        }
        tab_bar.LastTabItemIdx = (short)tab_bar.Tabs.IndexOf(tab);

        ImVec2 size = TabItemCalcSize(label, pOpen || (flags & ImGuiTabItemFlags.UnsavedDocument) != 0);
        tab.RequestedWidth = -1.0f;
        if ((g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasWidth) != 0)
            size.X = tab.RequestedWidth = g.NextItemData.Width;
        if (tabIsNew)
            tab.Width = MathF.Max(1.0f, size.X);
        tab.ContentWidth = size.X;
        tab.BeginOrder = tab_bar.TabsActiveCount++;

        bool tabBarAppearing = (tab_bar.PrevFrameVisible + 1 < g.FrameCount);
        bool tabBarFocused = (tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_IsFocused) != 0;
        bool tabAppearing = (tab.LastFrameVisible + 1 < g.FrameCount);
        bool tabJustUnsaved = (flags & ImGuiTabItemFlags.UnsavedDocument) != 0 && (tab.Flags & ImGuiTabItemFlags.UnsavedDocument) == 0;
        bool isTabButton = (flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) != 0;
        tab.LastFrameVisible = g.FrameCount;
        tab.Flags = flags;
        tab.Window = docked_window;

        if (docked_window != null)
        {
            ImAssert.Check((tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) != 0);
            tab.NameOffset = -1;
        }
        else
        {
            tab.NameOffset = tab_bar.TabsNames.Count;
            tab_bar.TabsNames.Add(label);
        }

        if (!isTabButton)
        {
            if (tabAppearing && (tab_bar.Flags & ImGuiTabBarFlags.AutoSelectNewTabs) != 0 && tab_bar.NextSelectedTabId == 0)
                if (!tabBarAppearing || tab_bar.SelectedTabId == 0)
                    TabBarQueueFocus(tab_bar, tab);
            if ((flags & ImGuiTabItemFlags.SetSelected) != 0 && tab_bar.SelectedTabId != id)
                TabBarQueueFocus(tab_bar, tab);
        }

        bool tabContentsVisible = tab_bar.VisibleTabId == id;
        if (tabContentsVisible)
            tab_bar.VisibleTabWasSubmitted = true;

        if (!tabContentsVisible && tab_bar.SelectedTabId == 0 && tabBarAppearing && docked_window == null)
            if (tab_bar.Tabs.Count == 1 && (tab_bar.Flags & ImGuiTabBarFlags.AutoSelectNewTabs) == 0)
                tabContentsVisible = true;

        if (tabAppearing && (!tabBarAppearing || tabIsNew))
        {
            ItemAdd(new ImRect(), id, null, ImGuiItemFlags.NoNav);
            if (isTabButton)
                return false;
            return tabContentsVisible;
        }

        if (tab_bar.SelectedTabId == id)
            tab.LastFrameSelected = g.FrameCount;

        ImVec2 backupMainCursorPos = window.DC.CursorPos;
        bool isCentralSection = (tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_) == 0;
        size.X = tab.Width;
        window.DC.CursorPos = TabBarGetTabPos(tab_bar, tab);
        ImVec2 pos = window.DC.CursorPos;
        ImRect bb = new(pos, pos + size);

        bool wantClipRect = isCentralSection && (bb.Min.X < tab_bar.ScrollingRectMinX || bb.Max.X > tab_bar.ScrollingRectMaxX);
        if (wantClipRect)
            PushClipRect(new ImVec2(Math.Clamp(bb.Min.X, tab_bar.ScrollingRectMinX, tab_bar.ScrollingRectMaxX), bb.Min.Y - 1.0f), new ImVec2(tab_bar.ScrollingRectMaxX, bb.Max.Y), true);

        ImVec2 backupCursorMaxPos = window.DC.CursorMaxPos;
        ItemSize(bb.GetSize(), style.FramePadding.Y);
        window.DC.CursorMaxPos = backupCursorMaxPos;

        if (!ItemAdd(bb, id))
        {
            if (wantClipRect)
                PopClipRect();
            window.DC.CursorPos = backupMainCursorPos;
            return tabContentsVisible;
        }

        ImGuiButtonFlags buttonFlags = (isTabButton ? (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease : (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick) | ImGuiButtonFlags.AllowOverlap;
        if (g.DragDropActive && !g.DragDropPayload.IsDataType(IMGUI_PAYLOAD_TYPE_WINDOW))
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDragDropHold;
        bool hovered = false, held = false, pressed = false;
        if ((flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Invisible) != 0)
            hovered = held = pressed = false;
        else
            pressed = ButtonBehavior(bb, id, out hovered, out held, buttonFlags);

        if (pressed && !isTabButton)
            TabBarQueueFocus(tab_bar, tab);

        if (held && docked_window != null && g.ActiveId == id && g.ActiveIdIsJustActivated)
            g.ActiveIdWindow = docked_window;

        ImGuiDockNode? node = docked_window?.DockNode;
        bool singleFloatingWindowNode = node != null && node.IsFloatingNode() && node.Windows.Count == 1;
        if (held && singleFloatingWindowNode && IsMouseDragging(ImGuiMouseButton.Left, 0.0f))
        {
            StartMouseMovingWindow(docked_window!);
        }
        else if (held && !tabAppearing && IsMouseDragging(ImGuiMouseButton.Left))
        {
            int dragDir = 0;
            float dragDistanceFromEdgeX = 0.0f;
            if (!g.DragDropActive && ((tab_bar.Flags & ImGuiTabBarFlags.Reorderable) != 0 || docked_window != null))
            {
                if (g.IO.MouseDelta.X < 0.0f && g.IO.MousePos.X < bb.Min.X)
                {
                    dragDir = -1;
                    dragDistanceFromEdgeX = bb.Min.X - g.IO.MousePos.X;
                    TabBarQueueReorderFromMousePos(tab_bar, tab, g.IO.MousePos);
                }
                else if (g.IO.MouseDelta.X > 0.0f && g.IO.MousePos.X > bb.Max.X)
                {
                    dragDir = +1;
                    dragDistanceFromEdgeX = g.IO.MousePos.X - bb.Max.X;
                    TabBarQueueReorderFromMousePos(tab_bar, tab, g.IO.MousePos);
                }
            }

            bool canUndock = docked_window != null && (docked_window.Flags & ImGuiWindowFlags.NoMove) == 0 && (node!.MergedFlags & ImGuiDockNodeFlags.NoUndocking) == 0;
            if (canUndock)
            {
                bool undockingTab = (g.DragDropActive && g.DragDropPayload.SourceId == id);
                if (!undockingTab)
                {
                    float thresholdBase = g.FontSize;
                    float thresholdX = thresholdBase * 2.2f;
                    float thresholdY = (thresholdBase * 1.5f) + Math.Clamp((MathF.Abs(g.IO.MouseDragMaxDistanceAbs[0].X) - thresholdBase * 2.0f) * 0.20f, 0.0f, thresholdBase * 4.0f);
                    float distanceFromEdgeY = MathF.Max(bb.Min.Y - g.IO.MousePos.Y, g.IO.MousePos.Y - bb.Max.Y);
                    if (distanceFromEdgeY >= thresholdY)
                        undockingTab = true;
                    if (dragDistanceFromEdgeX > thresholdX)
                        if ((dragDir < 0 && TabBarGetTabOrder(tab_bar, tab) == 0) || (dragDir > 0 && TabBarGetTabOrder(tab_bar, tab) == tab_bar.Tabs.Count - 1))
                            undockingTab = true;
                }

                if (undockingTab)
                {
                    DockContextQueueUndockWindow(g, docked_window!);
                    g.MovingWindow = docked_window;
                    SetActiveID(g.MovingWindow!.MoveId, g.MovingWindow);
                    g.ActiveIdClickOffset -= g.MovingWindow.Pos - bb.Min;
                    g.ActiveIdNoClearOnFocusLoss = true;
                    SetActiveIdUsingAllKeyboardKeys();
                }
            }
        }

        bool isVisible = (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Visible) != 0 && (flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Invisible) == 0;
        if (isVisible)
        {
            ImDrawList displayDrawList = window.DrawList;
            uint tabCol = GetColorU32((held || hovered) ? ImGuiCol.TabHovered : tabContentsVisible ? (tabBarFocused ? ImGuiCol.TabSelected : ImGuiCol.TabDimmedSelected) : (tabBarFocused ? ImGuiCol.Tab : ImGuiCol.TabDimmed));
            TabItemBackground(displayDrawList, bb, flags, tabCol);

            if (tabContentsVisible && (tab_bar.Flags & ImGuiTabBarFlags.DrawSelectedOverline) != 0 && style.TabBarOverlineSize > 0.0f)
            {
                ImVec2 tl = bb.GetTL() + new ImVec2(0.0f, 1.0f * g.CurrentDpiScale);
                ImVec2 tr = bb.GetTR() + new ImVec2(0.0f, 1.0f * g.CurrentDpiScale);
                uint overlineCol = GetColorU32(tabBarFocused ? ImGuiCol.TabSelectedOverline : ImGuiCol.TabDimmedSelectedOverline);
                if (style.TabRounding > 0.0f)
                {
                    float rounding = style.TabRounding;
                    displayDrawList.PathArcToFast(tl + new ImVec2(+rounding, +rounding), rounding, 7, 9);
                    displayDrawList.PathArcToFast(tr + new ImVec2(-rounding, +rounding), rounding, 9, 11);
                    displayDrawList.PathStroke(overlineCol, ImDrawFlags.None, style.TabBarOverlineSize);
                }
                else
                {
                    displayDrawList.AddLine(tl - new ImVec2(0.5f, 0.5f), tr - new ImVec2(0.5f, 0.5f), overlineCol, style.TabBarOverlineSize);
                }
            }
            RenderNavCursor(bb, id);

            bool hoveredUnblocked = IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup);
            if (tab_bar.SelectedTabId != tab.ID && hoveredUnblocked && (IsMouseClicked(ImGuiMouseButton.Right) || IsMouseReleased(ImGuiMouseButton.Right)) && !isTabButton)
                TabBarQueueFocus(tab_bar, tab);

            if ((tab_bar.Flags & ImGuiTabBarFlags.NoCloseWithMiddleMouseButton) != 0)
                flags |= ImGuiTabItemFlags.NoCloseWithMiddleMouseButton;

            uint closeButtonId = pOpen ? GetIDWithSeed("#CLOSE", null, docked_window != null ? docked_window.ID : id) : 0;
            bool justClosed = false;
            bool textClipped = false;
            TabItemLabelAndCloseButton(displayDrawList, bb, tabJustUnsaved ? (flags & ~ImGuiTabItemFlags.UnsavedDocument) : flags, tab_bar.FramePadding, label, id, closeButtonId, tabContentsVisible, &justClosed, &textClipped);
            if (justClosed && pOpen)
            {
                p_open = false;
                TabBarCloseTab(tab_bar, tab);
            }

            if (docked_window != null && (hovered || g.HoveredId == closeButtonId))
                g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredWindow;

            if (textClipped && g.HoveredId == id && !held)
                if ((tab_bar.Flags & ImGuiTabBarFlags.NoTooltip) == 0 && (tab.Flags & ImGuiTabItemFlags.NoTooltip) == 0)
                    SetItemTooltip("%s", FindRenderedTextEnd(label));
        }

        if (wantClipRect)
            PopClipRect();

        window.DC.CursorPos = backupMainCursorPos;
        if (isTabButton)
            return pressed;
        return tabContentsVisible;
    }

    /// <summary>
    /// Finds a tab item by identifier.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab_id">Tab ID.</param>
    /// <returns>Tab item or <c>null</c>.</returns>
    public static ImGuiTabItem? TabBarFindTabByID(ImGuiTabBar tab_bar, uint tab_id)
    {
        if (tab_id == 0)
            return null;
        for (int i = 0; i < tab_bar.Tabs.Count; i++)
            if (tab_bar.Tabs[i].ID == tab_id)
                return tab_bar.Tabs[i];
        return null;
    }

    /// <summary>
    /// Finds a tab item by visible order.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="order">Visible order.</param>
    /// <returns>Tab item or <c>null</c>.</returns>
    public static ImGuiTabItem? TabBarFindTabByOrder(ImGuiTabBar tab_bar, int order)
    {
        if (order < 0 || order >= tab_bar.Tabs.Count)
            return null;
        return tab_bar.Tabs[order];
    }

    /// <summary>
    /// Gets the tab currently being processed by BeginTabItem / EndTabItem.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <returns>Active tab item or <c>null</c>.</returns>
    public static ImGuiTabItem? TabBarGetCurrentTab(ImGuiTabBar tab_bar)
    {
        if (tab_bar.LastTabItemIdx < 0 || tab_bar.LastTabItemIdx >= tab_bar.Tabs.Count)
            return null;
        return tab_bar.Tabs[tab_bar.LastTabItemIdx];
    }

    /// <summary>
    /// Queues focus for a tab item specified by name.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab_name">Tab name.</param>
    public static void TabBarQueueFocus(ImGuiTabBar tab_bar, string tab_name)
    {
        ImAssert.Check((tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_DockNode) == 0);
        uint tabId = TabBarCalcTabID(tab_bar, tab_name, null);
        tab_bar.NextSelectedTabId = tabId;
    }

    /// <summary>
    /// Queues a tab item reorder operation.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab">Tab item to move.</param>
    /// <param name="offset">Relative offset.</param>
    public static void TabBarQueueReorder(ImGuiTabBar tab_bar, ImGuiTabItem tab, short offset)
    {
        ImAssert.Check(offset != 0);
        ImAssert.Check(tab_bar.ReorderRequestTabId == 0);
        tab_bar.ReorderRequestTabId = tab.ID;
        tab_bar.ReorderRequestOffset = offset;
    }

    /// <summary>
    /// Queues reorder request from mouse position during drag-and-drop.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="src_tab">Dragged tab.</param>
    /// <param name="mouse_pos">Cursor position.</param>
    public static void TabBarQueueReorderFromMousePos(ImGuiTabBar tab_bar, ImGuiTabItem src_tab, ImVec2 mouse_pos)
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check(tab_bar.ReorderRequestTabId == 0);
        if ((tab_bar.Flags & ImGuiTabBarFlags.Reorderable) == 0)
            return;

        float tabSpacing = g.Style.ItemInnerSpacing.X;
        bool isCentralSection = (src_tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_) == 0;
        float barOffset = tab_bar.BarRect.Min.X - (isCentralSection ? tab_bar.ScrollingTarget : 0.0f);

        int dir = (barOffset + src_tab.Offset) > mouse_pos.X ? -1 : +1;
        int srcIdx = tab_bar.Tabs.IndexOf(src_tab);
        int dstIdx = srcIdx;
        for (int i = srcIdx; i >= 0 && i < tab_bar.Tabs.Count; i += dir)
        {
            ImGuiTabItem dstTab = tab_bar.Tabs[i];
            if ((dstTab.Flags & ImGuiTabItemFlags.NoReorder) != 0)
                break;
            if ((dstTab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_) != (src_tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_))
                break;
            dstIdx = i;

            float x1 = barOffset + dstTab.Offset - tabSpacing;
            float x2 = barOffset + dstTab.Offset + dstTab.Width + tabSpacing;
            if ((dir < 0 && mouse_pos.X > x1) || (dir > 0 && mouse_pos.X < x2))
                break;
        }

        if (dstIdx != srcIdx)
            TabBarQueueReorder(tab_bar, src_tab, (short)(dstIdx - srcIdx));
    }

    /// <summary>
    /// Processes queued reorder requests for a tab bar.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <returns><c>true</c> if reordered; otherwise <c>false</c>.</returns>
    public static bool TabBarProcessReorder(ImGuiTabBar tab_bar)
    {
        ImGuiTabItem? tab1 = TabBarFindTabByID(tab_bar, tab_bar.ReorderRequestTabId);
        if (tab1 == null || (tab1.Flags & ImGuiTabItemFlags.NoReorder) != 0)
            return false;

        int tab2Order = TabBarGetTabOrder(tab_bar, tab1) + tab_bar.ReorderRequestOffset;
        if (tab2Order < 0 || tab2Order >= tab_bar.Tabs.Count)
            return false;

        ImGuiTabItem tab2 = tab_bar.Tabs[tab2Order];
        if ((tab2.Flags & ImGuiTabItemFlags.NoReorder) != 0)
            return false;
        if ((tab1.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_) != (tab2.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_))
            return false;

        int srcIdx = tab_bar.Tabs.IndexOf(tab1);
        tab_bar.Tabs.RemoveAt(srcIdx);
        tab_bar.Tabs.Insert(tab2Order, tab1);

        if ((tab_bar.Flags & (ImGuiTabBarFlags)ImGuiTabBarFlagsPrivate.ImGuiTabBarFlags_SaveSettings) != 0)
            MarkIniSettingsDirty();
        return true;
    }

    /// <summary>
    /// Adds a tab item to a tab bar.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab_flags">Tab item flags.</param>
    /// <param name="window">Window associated with the tab.</param>
    public static void TabBarAddTab(ImGuiTabBar tab_bar, ImGuiTabItemFlags tab_flags, ImGuiWindow window)
    {
        if (TabBarFindTabByID(tab_bar, window.TabId) != null)
            return;

        ImGuiContext g = GImGui!;
        if (!window.HasCloseButton)
            tab_flags |= (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_NoCloseButton; // Used for first-frame width calculation.

        int lastFrameVisible = tab_bar.CurrFrameVisible;
        if (lastFrameVisible == -1)
            lastFrameVisible = g.FrameCount - 1;
        ImGuiTabItem tab = new()
        {
            ID = window.TabId,
            Flags = tab_flags,
            LastFrameVisible = lastFrameVisible, // Required so BeginTabBar() does not discard the tab.
            Window = window                        // Required so TabBarLayout() can measure the tab before submission.
        };
        tab_bar.Tabs.Add(tab);
    }

    /// <summary>
    /// Removes a tab item by identifier from a tab bar.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab_id">Tab ID to remove.</param>
    public static void TabBarRemoveTab(ImGuiTabBar tab_bar, uint tab_id)
    {
        for (int i = 0; i < tab_bar.Tabs.Count; i++)
        {
            if (tab_bar.Tabs[i].ID == tab_id)
            {
                tab_bar.Tabs.RemoveAt(i);
                break;
            }
        }
        if (tab_bar.SelectedTabId == tab_id) tab_bar.SelectedTabId = 0;
        if (tab_bar.NextSelectedTabId == tab_id) tab_bar.NextSelectedTabId = 0;
        if (tab_bar.VisibleTabId == tab_id) tab_bar.VisibleTabId = 0;
    }

    /// <summary>
    /// Marks a tab item for closure.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab">Tab item to close.</param>
    public static void TabBarCloseTab(ImGuiTabBar tab_bar, ImGuiTabItem tab)
    {
        if ((tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) != 0)
            return; // A button appended with TabItemButton().

        if ((tab.Flags & (ImGuiTabItemFlags.UnsavedDocument | ImGuiTabItemFlags.NoAssumedClosure)) == 0)
        {
            // Removes a frame of lag when selecting another tab on closure. Not run for an unsaved document,
            // so the user gets a chance to fully undo the closure.
            tab.WantClose = true;
            if (tab_bar.VisibleTabId == tab.ID)
            {
                tab.LastFrameVisible = -1;
                tab_bar.SelectedTabId = 0;
                tab_bar.NextSelectedTabId = 0;
            }
        }
        else
        {
            // An unsaved-document tab is expected to handle the closure itself, so select it first.
            if (tab_bar.VisibleTabId != tab.ID)
                TabBarQueueFocus(tab_bar, tab);
        }
    }

    /// <summary>
    /// Gets the visual index order of a tab within its parent tab bar.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab">Tab item.</param>
    /// <returns>Index of the tab, or -1.</returns>
    public static int TabBarGetTabOrder(ImGuiTabBar tab_bar, ImGuiTabItem tab)
    {
        return tab_bar.Tabs.IndexOf(tab);
    }

    /// <summary>
    /// Finds the most recently selected tab whose window is still active, which the navigation system
    /// uses to restore focus into a dock node host.
    /// </summary>
    /// <param name="tab_bar">Tab bar to search.</param>
    /// <returns>The most recently selected active tab, or <c>null</c>.</returns>
    public static ImGuiTabItem? TabBarFindMostRecentlySelectedTabForActiveWindow(ImGuiTabBar tab_bar)
    {
        ImGuiTabItem? mostRecentlySelectedTab = null;
        for (int tabN = 0; tabN < tab_bar.Tabs.Count; tabN++)
        {
            ImGuiTabItem tab = tab_bar.Tabs[tabN];
            if (mostRecentlySelectedTab == null || mostRecentlySelectedTab.LastFrameSelected < tab.LastFrameSelected)
                if (tab.Window != null && tab.Window.WasActive)
                    mostRecentlySelectedTab = tab;
        }
        return mostRecentlySelectedTab;
    }

    /// <summary>
    /// Gets the screen-space position for a tab item within its tab bar.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab">Tab item.</param>
    /// <returns>Screen-space position.</returns>
    public static ImVec2 TabBarGetTabPos(ImGuiTabBar tab_bar, ImGuiTabItem tab)
    {
        if ((tab.Flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_SectionMask_) == 0)
            return tab_bar.BarRect.Min + new ImVec2(MathF.Truncate(tab.Offset - tab_bar.ScrollingAnim), 0.0f);
        else
            return tab_bar.BarRect.Min + new ImVec2(tab.Offset, 0.0f);
    }

    /// <summary>
    /// Gets the display name of a tab item.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab">Tab item.</param>
    /// <returns>Tab display name.</returns>
    public static string TabBarGetTabName(ImGuiTabBar tab_bar, ImGuiTabItem tab)
    {
        if (tab.Window != null)
            return tab.Window.Name;
        if (tab.NameOffset == -1)
            return "N/A";
        ImAssert.Check(tab.NameOffset < tab_bar.TabsNames.Count);
        return tab_bar.TabsNames[tab.NameOffset];
    }

    /// <summary>
    /// Queues focus for a tab item.
    /// </summary>
    /// <param name="tab_bar">Parent tab bar.</param>
    /// <param name="tab">Tab item.</param>
    public static void TabBarQueueFocus(ImGuiTabBar tab_bar, ImGuiTabItem tab)
    {
        tab_bar.NextSelectedTabId = tab.ID;
    }

    /// <summary>
    /// Calculates the size of a tab item.
    /// </summary>
    /// <param name="label">Tab label.</param>
    /// <param name="has_close_button_or_unsaved_marker">Whether the tab displays a close button.</param>
    /// <returns>Tab dimensions.</returns>
    public static ImVec2 TabItemCalcSize(string label, bool has_close_button_or_unsaved_marker = false)
    {
        ImGuiContext g = GImGui!;
        ImVec2 labelSize = CalcTextSize(label, true);
        ImVec2 size = new(labelSize.X + g.Style.FramePadding.X, labelSize.Y + g.Style.FramePadding.Y * 2.0f);
        if (has_close_button_or_unsaved_marker)
            size.X += g.Style.FramePadding.X + (g.Style.ItemInnerSpacing.X + g.FontSize); // Y is intentional: fits the close-button circle.
        else
            size.X += g.Style.FramePadding.X + 1.0f;
        return new ImVec2(MathF.Min(size.X, TabBarCalcMaxTabWidth()), size.Y);
    }

    /// <summary>
    /// Calculates the size of a tab item for a window.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <returns>Tab dimensions.</returns>
    public static ImVec2 TabItemCalcSize(ImGuiWindow window)
    {
        return TabItemCalcSize(window.Name, window.HasCloseButton || (window.Flags & ImGuiWindowFlags.UnsavedDocument) != 0);
    }

    /// <summary>
    /// Renders the background shape of a tab item.
    /// </summary>
    /// <param name="draw_list">Draw list to render into.</param>
    /// <param name="bb">Bounding box.</param>
    /// <param name="flags">Tab flags.</param>
    /// <param name="col">Background color.</param>
    public static void TabItemBackground(ImDrawList draw_list, ImRect bb, ImGuiTabItemFlags flags, uint col)
    {
        ImGuiContext g = GImGui!;
        // The top pixel is trimmed so tabs fit a regular frame height while looking detached from it.
        float width = bb.GetWidth();
        ImAssert.Check(width > 0.0f);
        float rounding = MathF.Max(0.0f, MathF.Min((flags & (ImGuiTabItemFlags)ImGuiTabItemFlagsPrivate.ImGuiTabItemFlags_Button) != 0 ? g.Style.FrameRounding : g.Style.TabRounding, width * 0.5f - 1.0f));
        float y1 = bb.Min.Y + 1.0f;
        float y2 = bb.Max.Y - g.Style.TabBarBorderSize;
        draw_list.AddRectFilled(bb.Min, new ImVec2(bb.Max.X, y2), col, rounding, ImDrawFlags.RoundCornersTop);
        if (g.Style.TabBorderSize > 0.0f)
        {
            draw_list.PathLineTo(new ImVec2(bb.Min.X + 0.5f, y2));
            draw_list.PathArcToFast(new ImVec2(bb.Min.X + rounding + 0.5f, y1 + rounding + 0.5f), rounding, 6, 9);
            draw_list.PathArcToFast(new ImVec2(bb.Max.X - rounding - 0.5f, y1 + rounding + 0.5f), rounding, 9, 12);
            draw_list.PathLineTo(new ImVec2(bb.Max.X - 0.5f, y2));
            draw_list.PathStroke(GetColorU32(ImGuiCol.Border), ImDrawFlags.None, g.Style.TabBorderSize);
        }
    }

    /// <summary>
    /// Renders the label text and optional close button for a tab item.
    /// </summary>
    /// <param name="draw_list">Draw list.</param>
    /// <param name="bb">Bounding box.</param>
    /// <param name="flags">Tab flags.</param>
    /// <param name="frame_padding">Frame padding.</param>
    /// <param name="label">Label string.</param>
    /// <param name="tab_id">Tab ID.</param>
    /// <param name="close_button_id">Close button ID.</param>
    /// <param name="is_contents_visible">Whether contents are visible.</param>
    /// <param name="out_just_closed">Output just closed boolean.</param>
    /// <param name="out_text_clipped">Output text clipped boolean.</param>
    public static void TabItemLabelAndCloseButton(ImDrawList draw_list, ImRect bb, ImGuiTabItemFlags flags, ImVec2 frame_padding, string label, uint tab_id, uint close_button_id, bool is_contents_visible, bool* out_just_closed, bool* out_text_clipped)
    {
        ImGuiContext g = GImGui!;
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false, -1.0f);

        if (out_just_closed != null)
            *out_just_closed = false;
        if (out_text_clipped != null)
            *out_text_clipped = false;

        if (bb.GetWidth() <= 1.0f)
            return;

        ImRect textEllipsisClipBb = new(bb.Min.X + frame_padding.X, bb.Min.Y + frame_padding.Y, bb.Max.X - frame_padding.X, bb.Max.Y);

        if (out_text_clipped != null)
            *out_text_clipped = (textEllipsisClipBb.Min.X + labelSize.X) > textEllipsisClipBb.Max.X;

        float buttonSz = g.FontSize;
        ImVec2 buttonPos = new(MathF.Max(bb.Min.X, bb.Max.X - frame_padding.X - buttonSz), bb.Min.Y + frame_padding.Y);

        bool closeButtonPressed = false;
        bool closeButtonVisible = false;
        bool isHovered = g.HoveredId == tab_id || g.HoveredId == close_button_id || g.ActiveId == tab_id || g.ActiveId == close_button_id;

        if (close_button_id != 0)
        {
            if (is_contents_visible)
                closeButtonVisible = (g.Style.TabCloseButtonMinWidthSelected < 0.0f) ? true : (isHovered && bb.GetWidth() >= MathF.Max(buttonSz, g.Style.TabCloseButtonMinWidthSelected));
            else
                closeButtonVisible = (g.Style.TabCloseButtonMinWidthUnselected < 0.0f) ? true : (isHovered && bb.GetWidth() >= MathF.Max(buttonSz, g.Style.TabCloseButtonMinWidthUnselected));
        }

        bool unsavedMarkerVisible = (flags & ImGuiTabItemFlags.UnsavedDocument) != 0 && (buttonPos.X + buttonSz <= bb.Max.X) && (!closeButtonVisible || !isHovered);
        if (unsavedMarkerVisible)
        {
            ImVec2 bulletPos = buttonPos + new ImVec2(buttonSz, buttonSz) * 0.5f;
            RenderBullet(draw_list, bulletPos, GetColorU32(ImGuiCol.UnsavedMarker));
        }
        else if (closeButtonVisible)
        {
            ImGuiLastItemData lastItemBackup = g.LastItemData;
            if (CloseButton(close_button_id, buttonPos))
                closeButtonPressed = true;
            g.LastItemData = lastItemBackup;

            if (isHovered && (flags & ImGuiTabItemFlags.NoCloseWithMiddleMouseButton) == 0 && IsMouseClicked(ImGuiMouseButton.Middle))
                closeButtonPressed = true;
        }

        float ellipsisMaxX = textEllipsisClipBb.Max.X;
        if (closeButtonVisible || unsavedMarkerVisible)
        {
            bool visibleWithoutHover = unsavedMarkerVisible || (is_contents_visible ? g.Style.TabCloseButtonMinWidthSelected : g.Style.TabCloseButtonMinWidthUnselected) < 0.0f;
            if (visibleWithoutHover)
            {
                textEllipsisClipBb.Max.X -= buttonSz * 0.90f;
                ellipsisMaxX -= buttonSz * 0.90f;
            }
            else
            {
                textEllipsisClipBb.Max.X -= buttonSz * 1.00f;
            }
        }

        // The label is captured between slashes so a tab label is distinguishable in a capture.
        LogSetNextTextDecoration("/", "\\");
        RenderTextEllipsis(draw_list, textEllipsisClipBb.Min, textEllipsisClipBb.Max, ellipsisMaxX, label, labelEnd, labelSize);

        if (out_just_closed != null)
            *out_just_closed = closeButtonPressed;
    }

    /// <summary>
    /// Safe overload for <see cref="TabItemLabelAndCloseButton(ImDrawList, ImRect, ImGuiTabItemFlags, ImVec2, string, uint, uint, bool, bool*, bool*)"/> using output parameters.
    /// </summary>
    /// <param name="draw_list">Target draw list.</param>
    /// <param name="bb">Bounding box rectangle.</param>
    /// <param name="flags">Tab item flags.</param>
    /// <param name="frame_padding">Frame padding dimensions.</param>
    /// <param name="label">Tab title label.</param>
    /// <param name="tab_id">Tab item identifier.</param>
    /// <param name="close_button_id">Close button identifier.</param>
    /// <param name="is_contents_visible">Whether tab contents are currently visible.</param>
    /// <param name="out_just_closed">Outputs true if the tab was just closed this frame.</param>
    /// <param name="out_text_clipped">Outputs true if the text label was clipped by layout boundaries.</param>
    public static void TabItemLabelAndCloseButton(ImDrawList draw_list, ImRect bb, ImGuiTabItemFlags flags, ImVec2 frame_padding, string label, uint tab_id, uint close_button_id, bool is_contents_visible, out bool out_just_closed, out bool out_text_clipped)
    {
        bool justClosed = false;
        bool textClipped = false;
        TabItemLabelAndCloseButton(draw_list, bb, flags, frame_padding, label, tab_id, close_button_id, is_contents_visible, &justClosed, &textClipped);
        out_just_closed = justClosed;
        out_text_clipped = textClipped;
    }

    #endregion

    #region [SECTION] DRAG & DROP

    /// <summary>
    /// Payload type tag for a three-component floating-point color payload. Ports canonical
    /// <c>IMGUI_PAYLOAD_TYPE_COLOR_3F</c>.
    /// </summary>
    /// <remarks>
    /// The payload object is a <see cref="float"/>[] of length three holding red, green, and blue.
    /// Color widgets publish and consume this tag so application code can accept colors from them.
    /// </remarks>
    public const string IMGUI_PAYLOAD_TYPE_COLOR_3F = "_COL3F";

    /// <summary>
    /// Payload type tag for a four-component floating-point color payload. Ports canonical
    /// <c>IMGUI_PAYLOAD_TYPE_COLOR_4F</c>.
    /// </summary>
    /// <remarks>
    /// The payload object is a <see cref="float"/>[] of length four holding red, green, blue, and
    /// alpha. Color widgets publish and consume this tag so application code can accept colors from
    /// them.
    /// </remarks>
    public const string IMGUI_PAYLOAD_TYPE_COLOR_4F = "_COL4F";

    /// <summary>
    /// Returns whether a drag operation is currently active, meaning a source has been started and the
    /// drag has not yet been cleared by delivery, cancellation, or source teardown.
    /// </summary>
    /// <returns><see langword="true"/> while <see cref="ImGuiContext.DragDropActive"/> is set.</returns>
    internal static bool IsDragDropActive()
    {
        return GImGui!.DragDropActive;
    }

    /// <summary>
    /// Returns whether the active drag has a payload that a target accepted on the previous frame.
    /// </summary>
    /// <returns><see langword="true"/> when a drag is active and a target identifier was recorded as
    /// previously accepted.</returns>
    /// <remarks>
    /// The check reads <see cref="ImGuiContext.DragDropAcceptIdPrev"/> rather than the current-frame
    /// identifier because it is used by subsystems such as window moving that must not steal the mouse
    /// viewport while a drop target is holding the payload.
    /// </remarks>
    internal static bool IsDragDropPayloadBeingAccepted()
    {
        ImGuiContext g = GImGui!;
        return g.DragDropActive && g.DragDropAcceptIdPrev != 0;
    }

    /// <summary>
    /// Clears every drag-and-drop state member, discarding the current payload and the target
    /// acceptance bookkeeping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source flags, source frame count, mouse button, target identifiers, accept flags and
    /// surface, and the hold-to-open identifier all return to their canonical inactive values so a
    /// later <c>BeginDragDropSource()</c> begins from a clean payload.
    /// </para>
    /// <para>
    /// The context keeps the owned payload buffers (<see cref="ImGuiContext.DragDropPayloadBufHeap"/>
    /// and <see cref="ImGuiContext.DragDropPayloadBufLocal"/>) for layout parity with canonical and
    /// releases them here. This port has no unmanaged payload store: raw payloads retain the pointer
    /// supplied by the caller, and managed payloads own their data through
    /// <see cref="ImGuiPayload.ObjectData"/>.
    /// </para>
    /// </remarks>
    internal static void ClearDragDrop()
    {
        ImGuiContext g = GImGui!;
        g.DragDropActive = false;
        g.DragDropPayload.Clear();
        g.DragDropAcceptFlagsCurr = ImGuiDragDropFlags.None;
        g.DragDropAcceptIdCurr = g.DragDropAcceptIdPrev = 0;
        g.DragDropAcceptIdCurrRectSurface = float.MaxValue;
        g.DragDropAcceptFrameCount = -1;

        g.DragDropPayloadBufHeap.Clear();
        Array.Clear(g.DragDropPayloadBufLocal, 0, g.DragDropPayloadBufLocal.Length);
    }

    /// <summary>
    /// Begins a drag-and-drop source for the last submitted item, or for an external source when
    /// <see cref="ImGuiDragDropFlags.SourceExtern"/> is set. While this returns <see langword="true"/>
    /// the caller must call <see cref="SetDragDropPayload(string, object, ImGuiCond)"/> exactly once, may render
    /// a preview, and must call <see cref="EndDragDropSource"/>.
    /// </summary>
    /// <param name="flags">Source behavior flags; see <see cref="ImGuiDragDropFlags"/>.</param>
    /// <returns><see langword="true"/> while the item (or external source) is being dragged.</returns>
    /// <remarks>
    /// <para>
    /// Items that own an identifier follow the common path: the item must already be the active item
    /// (typically through <c>ButtonBehavior()</c>), and the mouse button that activated it carries
    /// the drag. Items without an identifier require
    /// <see cref="ImGuiDragDropFlags.SourceAllowNullID"/> and receive a throwaway identifier derived
    /// from their window-relative rectangle, which does not survive repositioning or resizing.
    /// </para>
    /// <para>
    /// On the frame the drag activates, the source identifier, parent identifier, flags, and mouse
    /// button are latched and <c>ClearDragDrop()</c> resets any stale payload. The drag then keeps
    /// <see cref="ImGuiContext.DragDropSourceFrameCount"/> current so the end-of-frame elapse logic
    /// can detect a source that stopped being submitted.
    /// </para>
    /// <para>
    /// Unless <see cref="ImGuiDragDropFlags.SourceNoPreviewTooltip"/> is set, a preview tooltip is
    /// opened; the target may suppress it through
    /// <see cref="ImGuiDragDropFlags.AcceptNoPreviewTooltip"/>, in which case a hidden tooltip is
    /// opened so caller-emitted contents remain valid.
    /// </para>
    /// </remarks>
    public static bool BeginDragDropSource(ImGuiDragDropFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiWindow? window = g.CurrentWindow;

        // The mouse button carrying the drag. Items with an identifier publish theirs through
        // g.ActiveIdMouseButton; an external source without a live button uses -1.
        ImGuiMouseButton mouseButton = ImGuiMouseButton.Left;

        bool sourceDragActive = false;
        uint sourceId = 0;
        uint sourceParentId = 0;
        if ((flags & ImGuiDragDropFlags.SourceExtern) == 0)
        {
            sourceId = g.LastItemData.ID;
            if (sourceId != 0)
            {
                // Common path: an item with an identifier must itself be the active item.
                if (g.ActiveId != sourceId)
                    return false;
                if (g.ActiveIdMouseButton != -1)
                    mouseButton = (ImGuiMouseButton)g.ActiveIdMouseButton;
                if (!g.IO.MouseDown[(int)mouseButton] || window!.SkipItems)
                    return false;
                g.ActiveIdAllowOverlap = false;
            }
            else
            {
                // Uncommon path: items without an identifier, such as Text() or Image(). The hovered
                // rectangle or an active item in this window establishes the interaction candidate.
                if (!g.IO.MouseDown[(int)mouseButton] || window!.SkipItems)
                    return false;
                if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HoveredRect) == 0
                    && (g.ActiveId == 0 || g.ActiveIdWindow != window))
                    return false;

                if ((flags & ImGuiDragDropFlags.SourceAllowNullID) == 0)
                {
                    // Canonical asserts here; the port reports the misuse and declines the drag.
                    ImAssert.Check(false, "BeginDragDropSource() requires SourceAllowNullID for items without an identifier.");
                    return false;
                }

                // The throwaway identifier is hashed from the item rectangle relative to the window's
                // cursor origin, so it changes whenever the widget moves or the window resizes.
                sourceId = g.LastItemData.ID = GetIDFromRectangle(g.LastItemData.Rect);
                KeepAliveID(sourceId);
                bool isHovered = ItemHoverable(g.LastItemData.Rect, sourceId, g.LastItemData.ItemFlags);
                if (isHovered && g.IO.MouseClicked[(int)mouseButton])
                {
                    SetActiveID(sourceId, window);
                    FocusWindow(window);
                }

                // Let the underlying widget still report hovered on the release frame, avoiding a
                // one-frame flicker before the drag ends.
                if (g.ActiveId == sourceId)
                    g.ActiveIdAllowOverlap = isHovered;
            }

            if (g.ActiveId != sourceId)
                return false;
            sourceParentId = window!.IDStack[^1];
            sourceDragActive = IsMouseDragging(mouseButton);

            // Navigation and other keyboard activation are suppressed while a drag is in progress.
            SetActiveIdUsingAllKeyboardKeys();
        }
        else
        {
            // External source: no current item or window is read, the drag is unconditionally active,
            // and the fixed identifier ensures only one external source can exist at a time.
            window = null;
            sourceId = ImHashStr("#SourceExtern");
            sourceDragActive = true;
            mouseButton = g.IO.MouseDown[(int)ImGuiMouseButton.Left] ? ImGuiMouseButton.Left : (ImGuiMouseButton)(-1);
            KeepAliveID(sourceId);
            SetActiveID(sourceId, null);
        }

        ImAssert.Check(!g.DragDropWithinTarget, "Cannot nest BeginDragDropSource() and BeginDragDropTarget().");
        if (!sourceDragActive)
            return false;

        // Activate the drag and latch its immutable source metadata on its first frame.
        if (!g.DragDropActive)
        {
            ImAssert.Check(sourceId != 0, "A drag source must carry an identifier.");
            ClearDragDrop();
            ImGuiPayload payload = g.DragDropPayload;
            payload.SourceId = sourceId;
            payload.SourceParentId = sourceParentId;
            g.DragDropActive = true;
            g.DragDropSourceFlags = flags;
            g.DragDropMouseButton = (int)mouseButton;

            // A source that is also the active item must survive focus loss until the drag completes.
            if (payload.SourceId == g.ActiveId)
                g.ActiveIdNoClearOnFocusLoss = true;
        }
        g.DragDropSourceFrameCount = g.FrameCount;
        g.DragDropWithinSource = true;

        if ((flags & ImGuiDragDropFlags.SourceNoPreviewTooltip) == 0)
        {
            // The target may request that the source tooltip be hidden; the source still emits its
            // contents so the request stays explicit rather than silently suppressing user code.
            bool ret = (g.DragDropAcceptIdPrev != 0
                        && (g.DragDropAcceptFlagsPrev & ImGuiDragDropFlags.AcceptNoPreviewTooltip) != 0)
                ? BeginTooltipHidden()
                : BeginTooltip();
            ImAssert.Check(ret, "BeginDragDropSource() failed to open the preview tooltip window.");
        }

        // By default the source stops reporting itself as hovered so no later widget submits a
        // tooltip for it; SourceNoDisableHover opts out, and external sources have no item.
        if ((flags & ImGuiDragDropFlags.SourceNoDisableHover) == 0
            && (flags & ImGuiDragDropFlags.SourceExtern) == 0)
            g.LastItemData.StatusFlags &= ~ImGuiItemStatusFlags.HoveredRect;

        return true;
    }

    /// <summary>
    /// Ends a drag-and-drop source block, closing its preview tooltip and discarding the drag when no
    /// payload was submitted.
    /// </summary>
    /// <remarks>
    /// The payload is discarded when <see cref="ImGuiPayload.DataFrameCount"/> is still -1, which is
    /// the canonical signal that <see cref="SetDragDropPayload(string, object, ImGuiCond)"/> was never called
    /// between the matching <c>BeginDragDropSource()</c> and this call.
    /// </remarks>
    public static void EndDragDropSource()
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check(g.DragDropActive, "EndDragDropSource() requires an active drag.");
        ImAssert.Check(g.DragDropWithinSource, "EndDragDropSource() must follow a BeginDragDropSource() block.");

        if ((g.DragDropSourceFlags & ImGuiDragDropFlags.SourceNoPreviewTooltip) == 0)
            EndTooltip();

        // Discard the drag when the source never called SetDragDropPayload().
        if (g.DragDropPayload.DataFrameCount == -1)
            ClearDragDrop();
        g.DragDropWithinSource = false;
    }

    /// <summary>
    /// Sets the raw-pointer payload carried by the active drag source.
    /// </summary>
    /// <param name="type">The payload type tag, at most 32 characters.</param>
    /// <param name="data">The payload data pointer, or <see cref="IntPtr.Zero"/> when the size is zero.</param>
    /// <param name="data_size">The payload size in bytes.</param>
    /// <param name="cond">When to refresh the payload: <see cref="ImGuiCond.Always"/>, or
    /// <see cref="ImGuiCond.Once"/> to keep the first frame's data. A value of
    /// <see cref="ImGuiCond.None"/> is treated as <see cref="ImGuiCond.Always"/>, matching canonical.</param>
    /// <returns><see langword="true"/> when a target accepted this payload on the current or previous frame.</returns>
    /// <remarks>
    /// <para>
    /// The payload fields are refreshed only when <paramref name="cond"/> permits it, so a payload
    /// submitted with <see cref="ImGuiCond.Once"/> latches exactly once for the whole drag, while
    /// <see cref="ImGuiPayload.DataFrameCount"/> still advances every frame.
    /// </para>
    /// <para>
    /// This port stores the caller's <see cref="IntPtr"/> directly because it has no unmanaged payload
    /// allocator, so no byte copy happens and the canonical payload buffers
    /// (<see cref="ImGuiContext.DragDropPayloadBufHeap"/> and
    /// <see cref="ImGuiContext.DragDropPayloadBufLocal"/>) are retained only for field parity and
    /// released by <see cref="ClearDragDrop"/>. Callers must keep the pointed-to memory valid until
    /// the payload is cleared, or use <see cref="SetDragDropPayload(string, object, ImGuiCond)"/>, which retains
    /// the managed object reference and is the path used by docking.
    /// </para>
    /// </remarks>
    public static bool SetDragDropPayload(string type, IntPtr data, int data_size, ImGuiCond cond = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiPayload payload = g.DragDropPayload;
        if (cond == ImGuiCond.None)
            cond = ImGuiCond.Always;

        ImAssert.Check(type.Length <= 32, "Payload type can be at most 32 characters long.");
        ImAssert.Check((data != IntPtr.Zero && data_size > 0) || (data == IntPtr.Zero && data_size == 0),
            "Payload data and size must both be empty or both be provided.");
        ImAssert.Check(cond == ImGuiCond.Always || cond == ImGuiCond.Once,
            "Only ImGuiCond.Always and ImGuiCond.Once are supported for payload submission.");
        ImAssert.Check(payload.SourceId != 0,
            "SetDragDropPayload() must be called between BeginDragDropSource() and EndDragDropSource().");

        if (cond == ImGuiCond.Always || payload.DataFrameCount == -1)
        {
            payload.DataType = TruncatePayloadType(type);
            payload.ObjectData = null;
            payload.Data = data;
            payload.DataSize = data_size;
        }
        payload.DataFrameCount = g.FrameCount;

        // Report whether a target accepted the source on this or the previous frame.
        return g.DragDropAcceptFrameCount == g.FrameCount || g.DragDropAcceptFrameCount == g.FrameCount - 1;
    }

    /// <summary>
    /// Sets a managed-object payload carried by the active drag source.
    /// </summary>
    /// <param name="type">The payload type tag.</param>
    /// <param name="data">The managed payload object, whose lifetime the context retains for the drag.</param>
    /// <param name="cond">
    /// When to refresh the payload: <see cref="ImGuiCond.Always"/>, or <see cref="ImGuiCond.Once"/> to
    /// keep the first frame's object reference. A value of <see cref="ImGuiCond.None"/> is treated as
    /// <see cref="ImGuiCond.Always"/>, matching the raw-pointer overload and canonical.
    /// </param>
    /// <returns><see langword="true"/> when a target accepted this payload on the current or previous frame.</returns>
    /// <remarks>
    /// <para>
    /// This overload is the port's owned-data equivalent of the canonical byte copy: the object
    /// reference is stored in <see cref="ImGuiPayload.ObjectData"/> and the raw pointer and size are
    /// cleared so the two payload paths never mix. Docking submits
    /// <c>IMGUI_PAYLOAD_TYPE_WINDOW</c> through this overload, and the drag-and-drop demo and tests
    /// use it for application payloads.
    /// </para>
    /// <para>
    /// Without <see cref="ImGuiCond.Once"/> the type and object are refreshed on every call, matching
    /// <see cref="ImGuiCond.Always"/> semantics. With <see cref="ImGuiCond.Once"/> the fields latch on
    /// the first call of the drag, while <see cref="ImGuiPayload.DataFrameCount"/> still advances every
    /// frame so the payload stays visible to targets.
    /// </para>
    /// </remarks>
    public static bool SetDragDropPayload(string type, object data, ImGuiCond cond = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiPayload payload = g.DragDropPayload;
        if (cond == ImGuiCond.None)
            cond = ImGuiCond.Always;

        ImAssert.Check(type.Length <= 32, "Payload type can be at most 32 characters long.");
        ImAssert.Check(cond == ImGuiCond.Always || cond == ImGuiCond.Once,
            "Only ImGuiCond.Always and ImGuiCond.Once are supported for payload submission.");
        ImAssert.Check(payload.SourceId != 0,
            "SetDragDropPayload() must be called between BeginDragDropSource() and EndDragDropSource().");

        if (cond == ImGuiCond.Always || payload.DataFrameCount == -1)
        {
            payload.DataType = TruncatePayloadType(type);
            payload.Data = IntPtr.Zero;
            payload.DataSize = 0;
            payload.ObjectData = data;
        }
        payload.DataFrameCount = g.FrameCount;

        return g.DragDropAcceptFrameCount == g.FrameCount || g.DragDropAcceptFrameCount == g.FrameCount - 1;
    }

    /// <summary>
    /// Truncates a payload type tag to the canonical 32-character field width.
    /// </summary>
    /// <param name="type">The caller-supplied type tag; must be non-null.</param>
    /// <returns>
    /// <paramref name="type"/> when it fits the canonical <c>DataType[33]</c> buffer (at most 32
    /// characters plus the terminator), otherwise its first 32 characters. Canonical accepts and
    /// silently truncates longer tags through <c>ImStrncpy</c>.
    /// </returns>
    private static string TruncatePayloadType(string type)
    {
        return type.Length <= 32 ? type : type.Substring(0, 32);
    }

    /// <summary>
    /// Begins a drag-and-drop target for an explicit rectangle and identifier.
    /// </summary>
    /// <param name="bb">The target rectangle in screen space.</param>
    /// <param name="id">The target identifier; must be non-zero so overlapping targets can be ordered.</param>
    /// <returns><see langword="true"/> when the caller may submit <c>AcceptDragDropPayload()</c>.</returns>
    /// <remarks>
    /// The target is rejected when no drag is active, when the current window does not share a dock
    /// tree root with the window under the mouse, when the pointer is outside the rectangle, when the
    /// identifier belongs to the drag source, or when the window skips items.
    /// </remarks>
    public static bool BeginDragDropTargetCustom(ImRect bb, uint id)
    {
        ImGuiContext g = GImGui!;
        if (!g.DragDropActive)
            return false;

        ImGuiWindow window = g.CurrentWindow!;
        ImGuiWindow? hoveredWindow = g.HoveredWindowUnderMovingWindow;
        if (hoveredWindow == null || window.RootWindowDockTree != hoveredWindow.RootWindowDockTree)
            return false;
        ImAssert.Check(id != 0, "BeginDragDropTargetCustom() requires a non-zero identifier.");
        if (!IsMouseHoveringRect(bb.Min, bb.Max) || id == g.DragDropPayload.SourceId)
            return false;
        if (window.SkipItems)
            return false;

        ImAssert.Check(!g.DragDropWithinTarget && !g.DragDropWithinSource,
            "Cannot nest BeginDragDropSource() and BeginDragDropTarget().");
        g.DragDropTargetRect = bb;
        g.DragDropTargetClipRect = window.ClipRect;
        g.DragDropTargetId = id;
        g.DragDropTargetFullViewport = 0;
        g.DragDropWithinTarget = true;
        return true;
    }

    /// <summary>
    /// Begins a drag-and-drop target across a whole viewport, optionally overridden by an explicit
    /// rectangle. Typical use is <c>BeginDragDropTargetViewport(GetMainViewport(), null)</c> guarded
    /// by a caller-side hover test.
    /// </summary>
    /// <param name="viewport">The target viewport; <see langword="null"/> declines the target.</param>
    /// <param name="p_bb">An optional explicit rectangle; the viewport work rectangle when omitted.</param>
    /// <returns><see langword="true"/> when the caller may submit <c>AcceptDragDropPayload()</c>.</returns>
    /// <remarks>
    /// The viewport-wide target is recorded in
    /// <see cref="ImGuiContext.DragDropTargetFullViewport"/> so <c>AcceptDragDropPayload()</c> draws
    /// its default rectangle into the viewport's foreground draw list instead of the window list.
    /// </remarks>
    public static bool BeginDragDropTargetViewport(ImGuiViewport? viewport, ImRect? p_bb = null)
    {
        ImGuiContext g = GImGui!;
        if (!g.DragDropActive || viewport == null)
            return false;

        ImRect bb = p_bb ?? viewport.GetWorkRect();
        uint id = viewport.ID;
        if (g.MouseViewport != viewport || !IsMouseHoveringRect(bb.Min, bb.Max, false) || id == g.DragDropPayload.SourceId)
            return false;

        ImAssert.Check(!g.DragDropWithinTarget && !g.DragDropWithinSource,
            "Cannot nest BeginDragDropSource() and BeginDragDropTarget().");
        g.DragDropTargetRect = bb;
        g.DragDropTargetClipRect = bb;
        g.DragDropTargetId = id;
        g.DragDropTargetFullViewport = id;
        g.DragDropWithinTarget = true;
        return true;
    }

    /// <summary>
    /// Begins a drag-and-drop target for the last submitted item.
    /// </summary>
    /// <returns><see langword="true"/> when the caller may submit <c>AcceptDragDropPayload()</c>.</returns>
    /// <remarks>
    /// <para>
    /// This entry point does not reuse <see cref="BeginDragDropTargetCustom"/> because it relies on
    /// the last item's <see cref="ImGuiItemStatusFlags.HoveredRect"/> flag, which honors items that
    /// temporarily push a clip rectangle, and because it early-outs on the cheapest tests first.
    /// </para>
    /// <para>
    /// When the last item has no identifier, one is manufactured from its display rectangle and kept
    /// alive so the target can still be resolved. The target rectangle prefers
    /// <see cref="ImGuiLastItemData.DisplayRect"/> and the clip rectangle prefers
    /// <see cref="ImGuiLastItemData.ClipRect"/> when the item published them.
    /// </para>
    /// </remarks>
    public static bool BeginDragDropTarget()
    {
        ImGuiContext g = GImGui!;
        if (!g.DragDropActive)
            return false;

        ImGuiWindow window = g.CurrentWindow!;
        if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HoveredRect) == 0)
            return false;
        ImGuiWindow? hoveredWindow = g.HoveredWindowUnderMovingWindow;
        if (hoveredWindow == null || window.RootWindowDockTree != hoveredWindow.RootWindowDockTree || window.SkipItems)
            return false;

        ImRect displayRect = (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HasDisplayRect) != 0
            ? g.LastItemData.DisplayRect
            : g.LastItemData.Rect;
        uint id = g.LastItemData.ID;
        if (id == 0)
        {
            id = GetIDFromRectangle(displayRect);
            KeepAliveID(id);
        }
        if (g.DragDropPayload.SourceId == id)
            return false;

        ImAssert.Check(!g.DragDropWithinTarget && !g.DragDropWithinSource,
            "Cannot nest BeginDragDropSource() and BeginDragDropTarget().");
        g.DragDropTargetRect = displayRect;
        g.DragDropTargetClipRect = (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HasClipRect) != 0
            ? g.LastItemData.ClipRect
            : window.ClipRect;
        g.DragDropTargetId = id;
        g.DragDropWithinTarget = true;
        return true;
    }

    /// <summary>
    /// Accepts the active payload when its type matches the requested tag, or accepts any payload when
    /// the tag is <see langword="null"/>, returning the payload so the caller can read its data and
    /// preview/delivery state.
    /// </summary>
    /// <param name="type">
    /// The payload type tag to accept, or <see langword="null"/> to accept any payload type. A
    /// null tag is the canonical wildcard used by generic drop targets.
    /// </param>
    /// <param name="flags">Accept behavior flags; see <see cref="ImGuiDragDropFlags"/>.</param>
    /// <returns>The accepted payload, or <see langword="null"/> when the type does not match, the
    /// target is not the smallest overlapping candidate, or delivery is pending without
    /// <see cref="ImGuiDragDropFlags.AcceptBeforeDelivery"/>.</returns>
    /// <remarks>
    /// <para>
    /// A non-null tag must match <see cref="ImGuiPayload.DataType"/> exactly; a null tag skips the
    /// match and therefore accepts the first payload offered to the target.
    /// </para>
    /// <para>
    /// Overlapping targets resolve to the smallest rectangle surface, which allows targets to be
    /// nested without ordering constraints. A target is only marked as "previously accepted" when its
    /// identifier matched <see cref="ImGuiContext.DragDropAcceptIdPrev"/> on the previous frame, so
    /// <see cref="ImGuiPayload.Preview"/> stays false on the first hover frame.
    /// </para>
    /// <para>
    /// <see cref="ImGuiPayload.Delivery"/> becomes true once the source's mouse button is released
    /// (or, for an external source without a live button, once the source frame count is older than
    /// the current frame). Without <see cref="ImGuiDragDropFlags.AcceptBeforeDelivery"/> a
    /// not-yet-delivered payload is not returned.
    /// </para>
    /// <para>
    /// The default drop rectangle is drawn only for a previewed payload and is suppressed for
    /// <see cref="ImGuiDragDropFlags.AcceptNoDrawDefaultRect"/>, which the source may also request.
    /// </para>
    /// </remarks>
    public static ImGuiPayload? AcceptDragDropPayload(string? type, ImGuiDragDropFlags flags = 0)
    {
        ImGuiContext g = GImGui!;
        ImGuiPayload payload = g.DragDropPayload;
        ImAssert.Check(g.DragDropActive, "AcceptDragDropPayload() must be called between BeginDragDropTarget() and EndDragDropTarget().");
        ImAssert.Check(payload.DataFrameCount != -1,
            "AcceptDragDropPayload() requires a payload submitted between BeginDragDropSource() and EndDragDropSource().");
        if (type != null && !payload.IsDataType(type))
            return null;

        // Prefer the smallest overlapping target so nested targets need no specific submission order.
        bool wasAcceptedPreviously = g.DragDropAcceptIdPrev == g.DragDropTargetId;
        ImRect r = g.DragDropTargetRect;
        float rSurface = r.GetWidth() * r.GetHeight();
        if (rSurface > g.DragDropAcceptIdCurrRectSurface)
            return null;

        g.DragDropAcceptFlagsCurr = flags;
        g.DragDropAcceptIdCurr = g.DragDropTargetId;
        g.DragDropAcceptIdCurrRectSurface = rSurface;

        // Render the default drop visuals; the source can also inhibit them for short-lived sources.
        payload.Preview = wasAcceptedPreviously;
        flags |= g.DragDropSourceFlags & ImGuiDragDropFlags.AcceptNoDrawDefaultRect;
        bool drawTargetRect = payload.Preview && (flags & ImGuiDragDropFlags.AcceptNoDrawDefaultRect) == 0;
        if (drawTargetRect && g.DragDropTargetFullViewport != 0)
        {
            ImGuiViewport? viewport = FindViewportById(g.DragDropTargetFullViewport);
            ImAssert.Check(viewport != null, "A viewport-wide drag target requires its viewport to exist.");
            if (viewport != null)
            {
                // The viewport rectangle is expanded inward so the highlight sits inside the screen edge.
                ImRect bb = g.DragDropTargetRect;
                bb.Expand(-3.5f);
                RenderDragDropTargetRectEx(GetForegroundDrawList(viewport), bb, g.Style.DragDropTargetRounding);
            }
        }
        else if (drawTargetRect)
        {
            RenderDragDropTargetRectForItem(r);
        }

        g.DragDropAcceptFrameCount = g.FrameCount;

        // External sources that outlive the OS mouse button use the source frame count instead of a
        // live button test; every other source delivers on mouse release.
        if ((g.DragDropSourceFlags & ImGuiDragDropFlags.SourceExtern) != 0 && g.DragDropMouseButton == -1)
            payload.Delivery = wasAcceptedPreviously && g.DragDropSourceFrameCount < g.FrameCount;
        else
            payload.Delivery = wasAcceptedPreviously && !IsMouseDown((ImGuiMouseButton)g.DragDropMouseButton);

        if (!payload.Delivery && (flags & ImGuiDragDropFlags.AcceptBeforeDelivery) == 0)
            return null;

        return payload;
    }

    /// <summary>
    /// Finds a viewport by its identifier, used to resolve the viewport-wide drop highlight.
    /// </summary>
    /// <param name="id">The viewport identifier to search for.</param>
    /// <returns>The matching viewport, or <see langword="null"/> when none exists.</returns>
    /// <remarks>
    /// The item-target highlight is drawn by <c>RenderDragDropTargetRectForItem()</c> and its
    /// viewport variant by <c>RenderDragDropTargetRectEx()</c>, both declared in
    /// <c>ImGuiWidgets.cs</c>.
    /// </remarks>
    private static ImGuiViewport? FindViewportById(uint id)
    {
        ImGuiContext g = GImGui!;
        for (int n = 0; n < g.Viewports.Count; n++)
            if (g.Viewports[n].ID == id)
                return g.Viewports[n];
        return null;
    }

    /// <summary>
    /// Gets the payload currently being dragged, or <see langword="null"/> when no payload has been
    /// submitted for the active drag.
    /// </summary>
    /// <returns>The active payload, or <see langword="null"/>.</returns>
    public static ImGuiPayload? GetDragDropPayload()
    {
        ImGuiContext g = GImGui!;
        return g.DragDropActive && g.DragDropPayload.DataFrameCount != -1 ? g.DragDropPayload : null;
    }

    /// <summary>
    /// Ends a drag-and-drop target block and clears the drag immediately after a payload delivery.
    /// </summary>
    /// <remarks>
    /// Clearing on delivery gives the target one frame with the delivered payload; the source's
    /// subsequent frames observe an inactive drag and stop submitting.
    /// </remarks>
    public static void EndDragDropTarget()
    {
        ImGuiContext g = GImGui!;
        ImAssert.Check(g.DragDropActive, "EndDragDropTarget() requires an active drag.");
        ImAssert.Check(g.DragDropWithinTarget, "EndDragDropTarget() must follow a BeginDragDropTarget() block.");
        g.DragDropWithinTarget = false;

        // Clear the drag and drop state payload right after delivery.
        if (g.DragDropPayload.Delivery)
            ClearDragDrop();
    }

    #endregion
}
