// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies platform interactivity features: clipboard getters/setters, default shell URL opener,
/// and cursor state transitions for hyperlinks and resizable window borders.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/>.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class InteractivityTests : ImGuiTestFixture
{
    /// <summary>
    /// Verifies that clipboard text stored via SetClipboardText forwards to Platform_SetClipboardTextFn
    /// and round-trips through Platform_GetClipboardTextFn.
    /// </summary>
    [Test]
    public void Clipboard_SetAndGetText_ForwardsToPlatformCallbacks()
    {
        string stored = string.Empty;
        _ctx.PlatformIO.Platform_SetClipboardTextFn = (_, text) => stored = text;
        _ctx.PlatformIO.Platform_GetClipboardTextFn = _ => stored;

        const string expected = "https://hikari.engine/docs/interactive-widgets";
        ImGui.SetClipboardText(expected);
        string actual = ImGui.GetClipboardText();

        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// Verifies that Platform_OpenInShellFn is initialized by default on context creation.
    /// </summary>
    [Test]
    public void Platform_OpenInShellFn_InitializedByDefault()
    {
        Assert.That(_ctx.PlatformIO.Platform_OpenInShellFn, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that TextLinkOpenURL invokes Platform_OpenInShellFn with the provided URL when clicked.
    /// </summary>
    [Test]
    public void TextLinkOpenURL_InvokesPlatformOpenInShellFn_WhenClicked()
    {
        string? openedUrl = null;
        _ctx.PlatformIO.Platform_OpenInShellFn = (_, url) =>
        {
            openedUrl = url;
            return true;
        };

        ImRect itemRect = default;
        // Frame 1: Measure stable widget rect
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f));
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 100.0f));
        if (ImGui.Begin("LinkTestWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.TextLinkOpenURL("Hikari Repo", "https://github.com/hikari/hikari");
            itemRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.End();
        }
        ImGui.Render();

        ImVec2 center = (itemRect.Min + itemRect.Max) * 0.5f;

        // Frame 2: Mouse down over item
        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        _ctx.IO.AddMouseButtonEvent(0, true);
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f));
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 100.0f));
        if (ImGui.Begin("LinkTestWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.TextLinkOpenURL("Hikari Repo", "https://github.com/hikari/hikari");
            ImGui.End();
        }
        ImGui.Render();

        // Frame 3: Mouse release over item triggers click
        _ctx.IO.AddMouseButtonEvent(0, false);
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f));
        ImGui.SetNextWindowSize(new ImVec2(300.0f, 100.0f));
        if (ImGui.Begin("LinkTestWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.TextLinkOpenURL("Hikari Repo", "https://github.com/hikari/hikari");
            ImGui.End();
        }
        ImGui.Render();

        Assert.That(openedUrl, Is.EqualTo("https://github.com/hikari/hikari"));
    }

    /// <summary>
    /// Verifies that hovering over a TextLink requests the Hand cursor shape.
    /// </summary>
    [Test]
    public void TextLink_SetsHandCursor_WhenHovered()
    {
        ImRect itemRect = default;
        // Frame 1: Measure widget rect
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f));
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 100.0f));
        if (ImGui.Begin("CursorTestWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.TextLink("Clickable Link");
            itemRect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.End();
        }
        ImGui.Render();

        ImVec2 center = (itemRect.Min + itemRect.Max) * 0.5f;

        // Frame 2: Hover over widget center
        _ctx.IO.AddMousePosEvent(center.X, center.Y);
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(0.0f, 0.0f));
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 100.0f));
        if (ImGui.Begin("CursorTestWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.TextLink("Clickable Link");
            ImGui.End();
        }

        Assert.That(ImGui.GetMouseCursor(), Is.EqualTo(ImGuiMouseCursor.Hand));
    }

    /// <summary>
    /// Verifies that hovering over a resizable window edge sets the corresponding resize cursor (ResizeEW or ResizeNS).
    /// </summary>
    [Test]
    public void Window_BorderHover_SetsResizeCursor()
    {
        _ctx.IO.ConfigWindowsResizeFromEdges = true;
        _ctx.IO.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        ImRect winRect = default;
        // Frame 1: Measure stable window position
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(50.0f, 50.0f));
        ImGui.SetNextWindowSize(new ImVec2(200.0f, 200.0f));
        if (ImGui.Begin("ResizeTestWindow"))
        {
            winRect = new ImRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
            ImGui.End();
        }
        ImGui.Render();

        // Place mouse directly on the right border
        float rightBorderX = winRect.Max.X;
        float centerY = (winRect.Min.Y + winRect.Max.Y) * 0.5f;

        // Hover for multiple frames to advance HoveredIdTimer past windowsResizeFromEdgesFeedbackTimer (0.04s)
        for (int i = 0; i < 5; i++)
        {
            _ctx.IO.AddMousePosEvent(rightBorderX, centerY);
            NewTestFrame();
            ImGui.SetNextWindowPos(new ImVec2(50.0f, 50.0f));
            ImGui.SetNextWindowSize(new ImVec2(200.0f, 200.0f));
            if (ImGui.Begin("ResizeTestWindow"))
            {
                ImGui.End();
            }
            ImGui.Render();
        }

        Assert.That(ImGui.GetMouseCursor(), Is.EqualTo(ImGuiMouseCursor.ResizeEW));
    }
}
