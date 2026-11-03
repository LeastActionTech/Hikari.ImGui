// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System.Collections.Generic;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical context lifecycle, frame counters, input pipeline, key-query helpers,
/// context hooks, and error-recovery entry points implemented by the pure C# port.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="ImGuiContext"/> created by <see cref="ImGui.CreateContext"/>.
/// </remarks>
[TestFixture]
public class ContextTests : ImGuiTestFixture
{

    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
    }


    /// <summary>Starts and finishes one full frame so queued input is applied and retired.</summary>
    private static void Frame()
    {
        NewTestFrame();
        ImGui.Render();
    }

    #region Context lifecycle

    /// <summary>
    /// Verifies that a context created by <see cref="ImGui.CreateContext"/> is initialized and
    /// carries the canonical default <see cref="ImGuiIO"/> and <see cref="ImGuiStyle"/> values, and
    /// that the new context is installed when no context was current.
    /// </summary>
    [Test]
    public void Context_CreateContext_ReturnsCanonicalDefaults()
    {
        ImGuiContext ctx = ImGui.CreateContext();
        ctx.IO.IniFilename = null; // Tests must never read or write the developer's.ini file.

        try
        {
            Assert.That(ctx.Initialized, Is.True);
            Assert.That(ctx.WithinFrameScope, Is.False);
            Assert.That(ctx.FrameCount, Is.EqualTo(0));

            Assert.That(ctx.IO, Is.Not.Null);
            Assert.That(ctx.IO.Ctx, Is.SameAs(ctx));
            Assert.That(ctx.IO.DeltaTime, Is.EqualTo(1.0f / 60.0f));
            Assert.That(ctx.IO.DisplaySize, Is.EqualTo(new ImVec2(-1.0f, -1.0f)));
            Assert.That(ctx.IO.ConfigErrorRecovery, Is.True);
            Assert.That(ctx.IO.ConfigInputTrickleEventQueue, Is.True);
            Assert.That(ctx.IO.Fonts, Is.Not.Null);
            Assert.That(ctx.IO.KeysData.Length, Is.EqualTo((int)ImGuiKey.NamedKey_COUNT));

            // Canonical ImGuiStyle::ImGuiStyle() defaults.
            Assert.That(ctx.Style.WindowPadding, Is.EqualTo(new ImVec2(8.0f, 8.0f)));
            Assert.That(ctx.Style.WindowMinSize, Is.EqualTo(new ImVec2(32.0f, 32.0f)));
            Assert.That(ctx.Style.ItemSpacing, Is.EqualTo(new ImVec2(8.0f, 4.0f)));
            Assert.That(ctx.Style.FramePadding, Is.EqualTo(new ImVec2(4.0f, 3.0f)));
            Assert.That(ctx.Style.IndentSpacing, Is.EqualTo(21.0f));
            Assert.That(ctx.Style.Alpha, Is.EqualTo(1.0f));
            Assert.That(ctx.Style.DisabledAlpha, Is.EqualTo(0.60f));
        }
        finally
        {
            ImGui.DestroyContext(ctx);
        }
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.SetCurrentContext"/> installs and clears the ambient context
    /// and that <see cref="ImGui.DestroyContext"/> removes the destroyed context.
    /// </summary>
    [Test]
    public void Context_SetAndDestroyCurrentContext_UpdatesAmbientContext()
    {
        Assert.That(ImGui.GetCurrentContext(), Is.SameAs(_ctx));
        Assert.That(ImGui.GetIO(), Is.SameAs(_ctx.IO));

        ImGui.SetCurrentContext(null);
        Assert.That(ImGui.GetCurrentContext(), Is.Null);

        ImGui.SetCurrentContext(_ctx);
        Assert.That(ImGui.GetCurrentContext(), Is.SameAs(_ctx));

        ImGui.DestroyContext(_ctx);
        Assert.That(ImGui.GetCurrentContext(), Is.Null);
    }

    #endregion

    #region Frame lifecycle

    /// <summary>
    /// Verifies that <see cref="ImGui.GetFrameCount"/> advances exactly once per
    /// <see cref="ImGui.NewFrame"/> and that <see cref="ImGui.GetTime"/> accumulates the input
    /// delta time.
    /// </summary>
    [Test]
    public void Frame_NewFrame_AdvancesCountAndTime()
    {
        _ctx.IO.DeltaTime = 0.25f;

        Assert.That(ImGui.GetFrameCount(), Is.EqualTo(0));
        Assert.That(ImGui.GetTime(), Is.EqualTo(0.0));

        NewTestFrame();
        Assert.That(ImGui.GetFrameCount(), Is.EqualTo(1));
        Assert.That(ImGui.GetTime(), Is.EqualTo(0.25).Within(1e-9));
        ImGui.Render();

        NewTestFrame();
        Assert.That(ImGui.GetFrameCount(), Is.EqualTo(2));
        Assert.That(ImGui.GetTime(), Is.EqualTo(0.50).Within(1e-9));
        ImGui.Render();
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.Render"/> finalizes an open frame, exposes draw data, and is
    /// idempotent within the same frame.
    /// </summary>
    [Test]
    public void Frame_RenderAfterEndFrame_IsIdempotent()
    {
        NewTestFrame();
        ImGui.EndFrame();

        Assert.That(_ctx.FrameCountEnded, Is.EqualTo(_ctx.FrameCount));
        Assert.That(_ctx.FrameCountRendered, Is.EqualTo(-1));
        Assert.That(ImGui.GetDrawData(), Is.Null); // No render has happened yet.

        ImGui.Render();

        Assert.That(_ctx.FrameCountRendered, Is.EqualTo(_ctx.FrameCount));
        ImDrawData drawData = ImGui.GetDrawData();
        Assert.That(drawData, Is.Not.Null);
        Assert.That(drawData.Valid, Is.True);

        // A second Render() in the same frame returns before re-assembling draw data.
        ImGui.Render();
        Assert.That(ImGui.GetDrawData(), Is.SameAs(drawData));
    }

    #endregion

    #region Input pipeline

    /// <summary>
    /// Verifies that a queued mouse-position event is floored and applied to the live
    /// <see cref="ImGuiIO.MousePos"/> by the next frame.
    /// </summary>
    [Test]
    public void Input_MousePosEvent_UpdatesMousePos()
    {
        _ctx.IO.AddMousePosEvent(10.4f, 20.6f);

        NewTestFrame();

        Assert.That(_ctx.IO.MousePos, Is.EqualTo(new ImVec2(10.0f, 20.0f)));
        Assert.That(ImGui.GetMousePos(), Is.EqualTo(new ImVec2(10.0f, 20.0f)));
    }

    /// <summary>
    /// Verifies that a queued mouse-button event sets the canonical button state and that
    /// <see cref="ImGui.IsMouseDown"/> reports it.
    /// </summary>
    [Test]
    public void Input_MouseButtonEvent_SetsMouseDownState()
    {
        _ctx.IO.AddMousePosEvent(40.0f, 50.0f);
        _ctx.IO.AddMouseButtonEvent(0, true);

        NewTestFrame();

        Assert.That(_ctx.IO.MouseDown[0], Is.True);
        Assert.That(ImGui.IsMouseDown(ImGuiMouseButton.Left), Is.True);
        Assert.That(ImGui.IsMouseDown(ImGuiMouseButton.Right), Is.False);
        Assert.That(_ctx.IO.MouseDownDuration[0], Is.EqualTo(0.0f));
    }

    /// <summary>
    /// Verifies that <see cref="ImGuiIO.MouseDelta"/> is the difference between the current and
    /// previous frame mouse positions.
    /// </summary>
    [Test]
    public void Input_MousePosEvent_ProducesMouseDelta()
    {
        _ctx.IO.AddMousePosEvent(10.0f, 20.0f);
        Frame();

        _ctx.IO.AddMousePosEvent(15.0f, 18.0f);
        NewTestFrame();

        Assert.That(_ctx.IO.MouseDelta, Is.EqualTo(new ImVec2(5.0f, -2.0f)));
    }

    /// <summary>
    /// Verifies that a queued key event makes <see cref="ImGui.IsKeyDown"/> true and that
    /// <see cref="ImGui.IsKeyPressed"/> reports the press edge for exactly one frame.
    /// </summary>
    [Test]
    public void Input_KeyEvent_IsPressedOnlyOnThePressEdge()
    {
        _ctx.IO.AddKeyEvent(ImGuiKey.A, true);

        NewTestFrame();

        Assert.That(ImGui.IsKeyDown(ImGuiKey.A), Is.True);
        Assert.That(ImGui.IsKeyPressed(ImGuiKey.A), Is.True);
        Assert.That(ImGui.IsKeyPressed(ImGuiKey.A, repeat: false), Is.True);
        ImGui.Render();

        // The key stays held, but the press edge has elapsed.
        NewTestFrame();

        Assert.That(ImGui.IsKeyDown(ImGuiKey.A), Is.True);
        Assert.That(ImGui.IsKeyPressed(ImGuiKey.A), Is.False);
        ImGui.Render();

        _ctx.IO.AddKeyEvent(ImGuiKey.A, false);
        NewTestFrame();

        Assert.That(ImGui.IsKeyDown(ImGuiKey.A), Is.False);
        Assert.That(ImGui.IsKeyReleased(ImGuiKey.A), Is.True);
    }

    #endregion

    #region Key names and classification

    /// <summary>
    /// Verifies that <see cref="ImGui.GetKeyName"/> returns the canonical key and modifier names.
    /// </summary>
    [Test]
    public void Key_GetKeyName_ReturnsCanonicalNames()
    {
        Assert.That(ImGui.GetKeyName(ImGuiKey.None), Is.EqualTo("None"));
        Assert.That(ImGui.GetKeyName(ImGuiKey.A), Is.EqualTo("A"));
        Assert.That(ImGui.GetKeyName(ImGuiKey.Space), Is.EqualTo("Space"));
        Assert.That(ImGui.GetKeyName(ImGuiKey.F1), Is.EqualTo("F1"));
        Assert.That(ImGui.GetKeyName(ImGuiKey.LeftArrow), Is.EqualTo("LeftArrow"));
        Assert.That(ImGui.GetKeyName(ImGuiKey.ImGuiMod_Ctrl), Is.EqualTo("ModCtrl"));
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.GetKeyChordName"/> builds the canonical modifier-prefixed
    /// chord name.
    /// </summary>
    [Test]
    public void Key_GetKeyChordName_FormatsModifierPrefixes()
    {
        Assert.That(ImGui.GetKeyChordName(ImGuiKey.A), Is.EqualTo("A"));
        Assert.That(
            ImGui.GetKeyChordName(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.A),
            Is.EqualTo("Ctrl+A"));
        Assert.That(
            ImGui.GetKeyChordName(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift | ImGuiKey.S),
            Is.EqualTo("Ctrl+Shift+S"));
        // A left/right modifier folds its matching flag so the key is not repeated.
        Assert.That(ImGui.GetKeyChordName(ImGuiKey.LeftShift), Is.EqualTo("LeftShift"));
    }

    /// <summary>
    /// Verifies the canonical key-classification helpers.
    /// </summary>
    [Test]
    public void Key_ClassificationHelpers_SeparateKeyRanges()
    {
        Assert.That(ImGui.IsNamedKey(ImGuiKey.A), Is.True);
        Assert.That(ImGui.IsNamedKey(ImGuiKey.GamepadStart), Is.True);
        Assert.That(ImGui.IsNamedKey((ImGuiKey)100), Is.False);
        Assert.That(ImGui.IsNamedKey(ImGuiKey.ImGuiMod_Ctrl), Is.False);

        Assert.That(ImGui.IsNamedKeyOrMod(ImGuiKey.A), Is.True);
        Assert.That(ImGui.IsNamedKeyOrMod(ImGuiKey.ImGuiMod_Shift), Is.True);
        Assert.That(ImGui.IsNamedKeyOrMod((ImGuiKey)100), Is.False);

        Assert.That(ImGui.IsGamepadKey(ImGuiKey.GamepadStart), Is.True);
        Assert.That(ImGui.IsGamepadKey(ImGuiKey.GamepadRStickDown), Is.True);
        Assert.That(ImGui.IsGamepadKey(ImGuiKey.A), Is.False);

        Assert.That(ImGui.IsMouseKey(ImGuiKey.MouseLeft), Is.True);
        Assert.That(ImGui.IsMouseKey(ImGuiKey.MouseWheelY), Is.True);
        Assert.That(ImGui.IsMouseKey(ImGuiKey.A), Is.False);

        Assert.That(ImGui.IsLegacyKey((ImGuiKey)100), Is.True);
        Assert.That(ImGui.IsLegacyKey(ImGuiKey.A), Is.False);

        Assert.That(ImGui.IsAliasKey(ImGuiKey.MouseLeft), Is.True);
        Assert.That(ImGui.IsAliasKey(ImGuiKey.A), Is.False);
    }

    #endregion

    #region Context hooks

    /// <summary>
    /// Verifies that a hook registered for each lifecycle event fires exactly once on the
    /// corresponding <see cref="ImGui.NewFrame"/>, <see cref="ImGui.EndFrame"/>, and
    /// <see cref="ImGui.Render"/> transition.
    /// </summary>
    [Test]
    public void Hooks_RegisteredForLifecycle_FireOnMatchingTransition()
    {
        ImGuiContextHookType[] types =
        {
            ImGuiContextHookType.NewFramePre,
            ImGuiContextHookType.NewFramePost,
            ImGuiContextHookType.EndFramePre,
            ImGuiContextHookType.EndFramePost,
            ImGuiContextHookType.RenderPre,
            ImGuiContextHookType.RenderPost,
        };
        Dictionary<ImGuiContextHookType, int> counts = new();

        foreach (ImGuiContextHookType type in types)
        {
            ImGui.AddContextHook(_ctx, new ImGuiContextHook
            {
                Type = type,
                Callback = (_, hook) => counts[hook.Type] = counts.GetValueOrDefault(hook.Type) + 1,
            });
        }

        Frame();

        foreach (ImGuiContextHookType type in types)
            Assert.That(counts[type], Is.EqualTo(1), $"Hook {type} did not fire once.");

        Frame();

        foreach (ImGuiContextHookType type in types)
            Assert.That(counts[type], Is.EqualTo(2), $"Hook {type} did not fire twice.");
    }

    /// <summary>
    /// Verifies that <see cref="ImGui.RemoveContextHook"/> stops a hook from firing from the next
    /// frame onward.
    /// </summary>
    [Test]
    public void Hooks_RemoveContextHook_StopsCallback()
    {
        int count = 0;
        uint hookId = ImGui.AddContextHook(_ctx, new ImGuiContextHook
        {
            Type = ImGuiContextHookType.NewFramePre,
            Callback = (_, _) => count++,
        });

        NewTestFrame();
        ImGui.Render();
        Assert.That(count, Is.EqualTo(1));

        ImGui.RemoveContextHook(_ctx, hookId);

        // Deferred removal runs at the top of the next NewFrame, before hooks are dispatched.
        NewTestFrame();
        ImGui.Render();
        Assert.That(count, Is.EqualTo(1));
        Assert.That(_ctx.Hooks, Is.Empty);
    }

    #endregion

    #region Error recovery

    /// <summary>
    /// Verifies that <see cref="ImGui.ErrorLog"/> returns the assertion request derived from
    /// <see cref="ImGuiIO.ConfigErrorRecoveryEnableAssert"/> and forwards every message to the
    /// registered error callback.
    /// </summary>
    [Test]
    public void Error_ErrorLog_ReturnsAssertionRequestAndNotifiesCallback()
    {
        List<string> messages = new();
        _ctx.ErrorCallback = (_, _, message) => messages.Add(message);
        _ctx.IO.ConfigErrorRecovery = true;
        _ctx.IO.ConfigErrorRecoveryEnableAssert = true;

        Assert.That(ImGui.ErrorLog("first misuse"), Is.True);
        Assert.That(messages, Is.EqualTo(new[] { "first misuse" }));

        _ctx.IO.ConfigErrorRecoveryEnableAssert = false;
        Assert.That(ImGui.ErrorLog("second misuse"), Is.False);
        Assert.That(messages.Count, Is.EqualTo(2));
        Assert.That(messages[1], Is.EqualTo("second misuse"));
        Assert.That(_ctx.ErrorFirst, Is.False);
    }

    /// <summary>
    /// Verifies the canonical error-tooltip bookkeeping: each ErrorLog call increments
    /// <see cref="ImGuiContext.ErrorCountCurrentFrame"/> while inside a frame.
    /// </summary>
    /// <remarks>
    /// The canonical tooltip branch is now implemented, so a recoverable error submitted inside a frame
    /// both opens the <c>##Tooltip_Error</c> pseudo-tooltip and advances the per-frame counter.
    /// </remarks>
    [Test]
    public void Error_ErrorLog_IncrementsErrorCountCurrentFrame()
    {
        _ctx.IO.ConfigErrorRecovery = true;
        _ctx.IO.ConfigErrorRecoveryEnableTooltip = true;

        NewTestFrame();
        ImGui.ErrorLog("tooltip misuse");

        Assert.That(_ctx.ErrorCountCurrentFrame, Is.EqualTo(1));
    }

    #endregion
}
