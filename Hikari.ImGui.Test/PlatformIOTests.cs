// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the platform interface and the single-viewport half of the viewport lifecycle with
/// concrete state assertions: the identity of <c>GetPlatformIO()</c>, the symmetry of the handler
/// clear operations, viewport lookup, monitor association and fallback, the platform session date,
/// the automatic clearing of multi-viewport support when the backend lacks the flags, and the
/// main-viewport geometry.
/// </summary>
/// <remarks>
/// <para>
/// Every test runs on the shared <see cref="ImGuiTestFixture"/> lifecycle. Canonical guard failures are
/// recorded by the fixture instead of aborting the run; a test that intentionally trips one declares it
/// with <c>ExpectAssert</c>, and any undeclared guard failure fails the test during teardown.
/// </para>
/// </remarks>
[TestFixture]
public class PlatformIOTests : ImGuiTestFixture
{

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
    /// Runs one complete frame with an empty body so per-frame viewport bookkeeping runs before the
    /// measured frame.
    /// </summary>
    private void WarmupFrame()
    {
        NewTestFrame();
        ImGui.Render();
    }

    #endregion

    #region Accessors and handler management

    /// <summary>
    /// Verifies that both <c>GetPlatformIO()</c> overloads return the live platform interface owned by
    /// the context, not a copy.
    /// </summary>
    [Test]
    public void GetPlatformIO_ReturnsTheContextInstance()
    {
        ImGuiPlatformIO viaCurrent = ImGui.GetPlatformIO();
        ImGuiPlatformIO viaContext = ImGui.GetPlatformIO(_ctx);

        Assert.That(viaCurrent, Is.SameAs(_ctx.PlatformIO));
        Assert.That(viaContext, Is.SameAs(_ctx.PlatformIO));
        Assert.That(viaCurrent, Is.SameAs(viaContext));
    }

    /// <summary>
    /// Verifies that <c>ClearPlatformHandlers()</c> clears every platform handler and its user data
    /// while leaving the renderer handlers and resource limits untouched
    /// </summary>
    [Test]
    public void ClearPlatformHandlers_LeavesRendererHandlers()
    {
        ImGuiPlatformIO platformIo = _ctx.PlatformIO;
        platformIo.Platform_CreateWindow = _ => { };
        platformIo.Platform_SetWindowAlpha = (_, _) => { };
        platformIo.Platform_ClipboardUserData = (IntPtr)0x1234;
        platformIo.Renderer_CreateWindow = _ => { };
        platformIo.Renderer_SetWindowSize = (_, _) => { };
        platformIo.Renderer_TextureMaxWidth = 4096;
        platformIo.Renderer_TextureMaxHeight = 2048;
        platformIo.DrawCallback_SetSamplerLinear = (_, _) => { };

        platformIo.ClearPlatformHandlers();

        Assert.That(platformIo.Platform_CreateWindow, Is.Null);
        Assert.That(platformIo.Platform_SetWindowAlpha, Is.Null);
        Assert.That(platformIo.Platform_GetClipboardTextFn, Is.Null);
        Assert.That(platformIo.Platform_ClipboardUserData, Is.EqualTo(IntPtr.Zero));
        Assert.That(platformIo.Platform_ImeUserData, Is.EqualTo(IntPtr.Zero));
        Assert.That(platformIo.Renderer_CreateWindow, Is.Not.Null, "Renderer handlers must survive a platform clear.");
        Assert.That(platformIo.Renderer_SetWindowSize, Is.Not.Null);
        Assert.That(platformIo.Renderer_TextureMaxWidth, Is.EqualTo(4096));
        Assert.That(platformIo.Renderer_TextureMaxHeight, Is.EqualTo(2048));
        Assert.That(platformIo.DrawCallback_SetSamplerLinear, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that <c>ClearRendererHandlers()</c> clears every renderer handler and resource limit
    /// while leaving the platform handlers untouched.
    /// </summary>
    [Test]
    public void ClearRendererHandlers_LeavesPlatformHandlers()
    {
        ImGuiPlatformIO platformIo = _ctx.PlatformIO;
        platformIo.Platform_CreateWindow = _ => { };
        platformIo.Platform_GetWindowPos = _ => new ImVec2(4.0f, 5.0f);
        platformIo.Renderer_CreateWindow = _ => { };
        platformIo.Renderer_RenderState = (IntPtr)0xBEEF;
        platformIo.Renderer_TextureMaxWidth = 4096;
        platformIo.DrawCallback_ResetRenderState = (_, _) => { };
        platformIo.DrawCallback_SetSamplerNearest = (_, _) => { };

        platformIo.ClearRendererHandlers();

        Assert.That(platformIo.Renderer_CreateWindow, Is.Null);
        Assert.That(platformIo.Renderer_RenderState, Is.EqualTo(IntPtr.Zero));
        Assert.That(platformIo.Renderer_TextureMaxWidth, Is.EqualTo(0));
        Assert.That(platformIo.Renderer_TextureMaxHeight, Is.EqualTo(0));
        Assert.That(platformIo.DrawCallback_ResetRenderState, Is.Null);
        Assert.That(platformIo.DrawCallback_SetSamplerNearest, Is.Null);
        Assert.That(platformIo.Platform_CreateWindow, Is.Not.Null, "Platform handlers must survive a renderer clear.");
        Assert.That(platformIo.Platform_GetWindowPos, Is.Not.Null);
    }

    #endregion

    #region Viewport lookup and monitors

    /// <summary>
    /// Verifies that <c>FindViewportByID()</c> resolves the main viewport and returns null for an
    /// unknown identifier.
    /// </summary>
    [Test]
    public void FindViewportByID_ResolvesMainViewportAndRejectsUnknown()
    {
        ImGuiViewport mainViewport = ImGui.GetMainViewport();

        Assert.That(mainViewport.ID, Is.EqualTo(ImGuiInternalConstants.ViewportDefaultId));
        Assert.That(ImGui.FindViewportByID(mainViewport.ID), Is.SameAs(mainViewport));
        Assert.That(ImGui.FindViewportByID(0xDEADBEEFu), Is.Null);
        Assert.That(ImGui.FindViewportByID(0u), Is.Null);
    }

    /// <summary>
    /// Verifies that <c>FindViewportByPlatformHandle()</c> resolves a viewport by its stored handle and
    /// returns null for an unknown handle. Canonical compares the handle directly, so the default zero
    /// handle of the main viewport matches a zero query
    /// </summary>
    [Test]
    public void FindViewportByPlatformHandle_ResolvesStoredHandleOnly()
    {
        ImGuiViewport mainViewport = ImGui.GetMainViewport();
        Assert.That(ImGui.FindViewportByPlatformHandle(IntPtr.Zero), Is.SameAs(mainViewport),
            "The main viewport's default zero handle must match a zero query, as canonical compares handles directly.");

        IntPtr handle = (IntPtr)0x1234;
        mainViewport.PlatformHandle = handle;
        try
        {
            Assert.That(ImGui.FindViewportByPlatformHandle(handle), Is.SameAs(mainViewport));
            Assert.That(ImGui.FindViewportByPlatformHandle((IntPtr)0x9999), Is.Null);
            Assert.That(ImGui.FindViewportByPlatformHandle(IntPtr.Zero), Is.Null,
                "After the only viewport's handle is assigned, no viewport carries the zero handle.");
        }
        finally
        {
            // The handle is borrowed state; teardown asserts that the backend released it.
            mainViewport.PlatformHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Verifies that a viewport is associated with the monitor that contains its rectangle and that
    /// <c>GetViewportPlatformMonitor()</c> returns that monitor, falling back to the context's fallback
    /// monitor when the registered list is empty
    /// </summary>
    [Test]
    public void GetViewportPlatformMonitor_SelectsContainingMonitorAndFallsBack()
    {
        _ctx.PlatformIO.Monitors.Add(new ImGuiPlatformMonitor
        {
            MainPos = new ImVec2(0.0f, 0.0f),
            MainSize = new ImVec2(1920.0f, 1080.0f),
            WorkPos = new ImVec2(0.0f, 40.0f),
            WorkSize = new ImVec2(1920.0f, 1040.0f),
            DpiScale = 1.5f,
        });

        NewTestFrame();
        Assert.That(_ctx.Viewports[0].PlatformMonitor, Is.EqualTo(0),
            "The main viewport must be associated with the containing monitor.");
        Assert.That(ImGui.GetViewportPlatformMonitor(ImGui.GetMainViewport()),
            Is.SameAs(_ctx.PlatformIO.Monitors[0]));
        ImGui.EndFrame();
        ImGui.Render();

        _ctx.PlatformIO.Monitors.Clear();
        Assert.That(ImGui.GetViewportPlatformMonitor(ImGui.GetMainViewport()),
            Is.SameAs(_ctx.FallbackMonitor),
            "A stale monitor index must fall back to the fallback monitor when no monitors are registered.");
    }

    #endregion

    #region Session date

    /// <summary>
    /// Verifies that <c>NewFrame()</c> derives the context session date from
    /// <c>PlatformIO.Platform_SessionDate</c> and that a change is observed on the next frame.
    /// </summary>
    [Test]
    public void Platform_SessionDate_PropagatesToContextSessionDate()
    {
        _ctx.PlatformIO.Platform_SessionDate = 20261231;
        NewTestFrame();
        Assert.That(_ctx.SessionDate.Year, Is.EqualTo((ushort)26));
        Assert.That(_ctx.SessionDate.Month, Is.EqualTo((ushort)12));
        Assert.That(_ctx.SessionDate.Day, Is.EqualTo((ushort)31));
        Assert.That(_ctx.SessionDate.IsValid(), Is.True);
        ImGui.EndFrame();
        ImGui.Render();

        _ctx.PlatformIO.Platform_SessionDate = 20250102;
        NewTestFrame();
        Assert.That(_ctx.SessionDate.Year, Is.EqualTo((ushort)25));
        Assert.That(_ctx.SessionDate.Month, Is.EqualTo((ushort)1));
        Assert.That(_ctx.SessionDate.Day, Is.EqualTo((ushort)2));
        ImGui.EndFrame();
        ImGui.Render();
    }

    #endregion

    #region Multi-viewport gating and main-viewport geometry

    /// <summary>
    /// Verifies that multi-viewport support is cleared when the backend does not advertise both
    /// viewport backend flags.
    /// </summary>
    [Test]
    public void ViewportsEnable_IsClearedWithoutBackendFlags()
    {
        // ViewportsEnable is set before the first frame, which is what canonical requires: enabling it
        // exactly at frame 1 would warn that the.ini settings are lost. Without the backend viewport
        // flags, NewFrame must clear the feature again.
        _ctx.IO.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        _ctx.IO.BackendFlags &= ~(ImGuiBackendFlags.PlatformHasViewports | ImGuiBackendFlags.RendererHasViewports);

        NewTestFrame();

        Assert.That((_ctx.IO.ConfigFlags & ImGuiConfigFlags.ViewportsEnable), Is.EqualTo(ImGuiConfigFlags.None));
        Assert.That((_ctx.ConfigFlagsCurrFrame & ImGuiConfigFlags.ViewportsEnable), Is.EqualTo(ImGuiConfigFlags.None));
        ImGui.EndFrame();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies the main-viewport geometry for a single-viewport frame: the rectangle follows
    /// <c>io.DisplaySize</c>, the work rectangle is inset-free, the DPI scale is 1.0, the canonical
    /// flags are set, and the user-facing viewport list contains exactly the main viewport.
    /// </summary>
    [Test]
    public void MainViewportGeometry_MatchesDisplaySizeAndFlags()
    {
        _ctx.IO.DisplaySize = new ImVec2(1024.0f, 768.0f);

        // The user-facing list is rebuilt during EndFrame, so one warmup frame publishes the main
        // viewport before the measured frame reads it.
        WarmupFrame();
        NewTestFrame();
        ImGuiViewport mainViewport = ImGui.GetMainViewport();

        Assert.That(mainViewport.Pos, Is.EqualTo(ImVec2.Zero));
        Assert.That(mainViewport.Size, Is.EqualTo(new ImVec2(1024.0f, 768.0f)));
        Assert.That(mainViewport.WorkPos, Is.EqualTo(ImVec2.Zero));
        Assert.That(mainViewport.WorkSize, Is.EqualTo(new ImVec2(1024.0f, 768.0f)));
        Assert.That(mainViewport.DpiScale, Is.EqualTo(1.0f));
        Assert.That((mainViewport.Flags & ImGuiViewportFlags.OwnedByApp), Is.Not.EqualTo(ImGuiViewportFlags.None));
        Assert.That((mainViewport.Flags & ImGuiViewportFlags.CanHostOtherWindows), Is.Not.EqualTo(ImGuiViewportFlags.None));
        Assert.That((mainViewport.Flags & ImGuiViewportFlags.IsPlatformWindow), Is.Not.EqualTo(ImGuiViewportFlags.None));
        Assert.That(_ctx.PlatformIO.Viewports.Count, Is.EqualTo(1));
        Assert.That(_ctx.PlatformIO.Viewports[0], Is.SameAs(mainViewport));
        ImGui.EndFrame();
        ImGui.Render();
    }

    /// <summary>
    /// Verifies the multi-viewport callback path when a backend advertises both viewport flags: a
    /// secondary viewport is published, <c>UpdatePlatformWindows()</c> creates and positions its platform
    /// window, and <c>RenderPlatformWindowsDefault()</c> renders then presents it, skipping the main
    /// viewport.
    /// </summary>
    [Test]
    public void UpdatePlatformWindows_CreatesAndRendersSecondaryViewport()
    {
        WarmupFrame();
        // The canonical main loop calls UpdatePlatformWindows() after EndFrame(); doing it here keeps the
        // platform frame counter aligned before the viewport feature is enabled below.
        ImGui.UpdatePlatformWindows();
        _ctx.IO.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports | ImGuiBackendFlags.RendererHasViewports;
        _ctx.IO.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        _ctx.Viewports[0].PlatformHandle = (IntPtr)0x1;
        _ctx.PlatformIO.Monitors.Add(new ImGuiPlatformMonitor
        {
            MainPos = new ImVec2(0.0f, 0.0f),
            MainSize = new ImVec2(1920.0f, 1080.0f),
            WorkPos = new ImVec2(0.0f, 0.0f),
            WorkSize = new ImVec2(1920.0f, 1080.0f),
            DpiScale = 1.0f,
        });

        int createCount = 0;
        int showCount = 0;
        int setPosCount = 0;
        int renderCount = 0;
        int swapCount = 0;
        _ctx.PlatformIO.Platform_GetWindowPos = _ => new ImVec2(0.0f, 0.0f);
        _ctx.PlatformIO.Platform_GetWindowSize = _ => new ImVec2(800.0f, 600.0f);
        _ctx.PlatformIO.Platform_CreateWindow = _ => createCount++;
        _ctx.PlatformIO.Platform_ShowWindow = _ => showCount++;
        _ctx.PlatformIO.Platform_SetWindowPos = (_, _) => setPosCount++;
        _ctx.PlatformIO.Platform_SetWindowSize = (_, _) => { };
        _ctx.PlatformIO.Platform_DestroyWindow = _ => { };
        _ctx.PlatformIO.Renderer_CreateWindow = _ => { };
        _ctx.PlatformIO.Renderer_DestroyWindow = _ => { };
        _ctx.PlatformIO.Platform_RenderWindow = (_, _) => renderCount++;
        _ctx.PlatformIO.Renderer_RenderWindow = (_, _) => { };
        _ctx.PlatformIO.Platform_SwapBuffers = (_, _) => swapCount++;
        _ctx.PlatformIO.Renderer_SwapBuffers = (_, _) => { };

        // Enabling ViewportsEnable after frame 0 is itself the canonical warning this test declares.
        using (ExpectAssert("Please set ViewportsEnable before the first call to NewFrame()!"))
            NewTestFrame();
        // Simulate the mid-frame creation of a secondary viewport whose platform window is not yet created.
        ImGuiViewportP secondary = new ImGuiViewportP
        {
            ID = 0x22222222u,
            Idx = 1,
            Pos = new ImVec2(100.0f, 100.0f),
            LastPos = new ImVec2(100.0f, 100.0f),
            Size = new ImVec2(800.0f, 600.0f),
            LastSize = new ImVec2(800.0f, 600.0f),
            LastFrameActive = _ctx.FrameCount,
            PlatformWindowCreated = false,
        };
        _ctx.Viewports.Add(secondary);
        // The simulated viewport has no owning window yet; canonical reports that while publishing the
        // viewport list at the end of the frame, and the test declares the partial state it exercises.
        using (ExpectAssert("Every secondary viewport must be owned by a window."))
            ImGui.EndFrame();
        ImGui.Render();

        ImGui.UpdatePlatformWindows();

        Assert.That(createCount, Is.EqualTo(1));
        Assert.That(showCount, Is.EqualTo(1));
        Assert.That(setPosCount, Is.EqualTo(1), "A newly created window must receive its queued position.");
        Assert.That(secondary.PlatformWindowCreated, Is.True);
        Assert.That(_ctx.PlatformIO.Viewports.Count, Is.EqualTo(2));
        Assert.That(_ctx.PlatformIO.Viewports[1], Is.SameAs(secondary));

        ImGui.RenderPlatformWindowsDefault();

        Assert.That(renderCount, Is.EqualTo(1), "Only the secondary viewport is rendered by the helper.");
        Assert.That(swapCount, Is.EqualTo(1), "Only the secondary viewport is presented by the helper.");

        // Release the platform windows this test created. The handle staged on the main viewport is
        // cleared first so the destroy pass does not report a retained backend handle.
        _ctx.Viewports[0].PlatformHandle = IntPtr.Zero;
        ImGui.DestroyPlatformWindows();
    }

    #endregion
}
