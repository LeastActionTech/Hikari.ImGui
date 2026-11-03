// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Shared lifecycle for every test that drives a real <see cref="ImGuiContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The fixture exists so that the context setup, the per-fixture configuration hook, the
/// failure-proof teardown, the frame helper, and the canonical-guard recorder are defined once
/// rather than repeated by every fixture. Derived fixtures declare only their own configuration
/// through <see cref="ConfigureContext"/>.
/// </para>
/// <para>
/// Two lifecycle guarantees matter for correctness and are the reason this type exists:
/// </para>
/// <list type="number">
/// <item><description>
/// <see cref="ImGui.CreateContext(ImFontAtlas?)"/> restores a previously current context when one
/// exists, mirroring canonical <c>ImGui::CreateContext</c>. <see cref="SetUp"/> therefore installs
/// the new context with <see cref="ImGui.SetCurrentContext(ImGuiContext?)"/> explicitly, so a
/// context leaked by an earlier fixture can never become the ambient context of a later test.
/// </description></item>
/// <item><description>
/// Teardown destroys the context in a <see langword="finally"/> block, so cleanup runs even when a
/// test body or an end-of-frame guard raises. Without that guarantee a single failure leaked the
/// thread's current context and produced misleading failures in every following fixture.
/// </description></item>
/// </list>
/// <para>
/// <see cref="ImAssert.FailureHandler"/> is installed for the duration of each test so that a
/// canonical guard failure is recorded rather than thrown by the test host. A test that
/// intentionally trips a guard declares it with <see cref="ExpectAssert"/>; any guard failure that
/// was not declared fails the test during teardown. This keeps the canonical guards observable
/// instead of either aborting the run or being silently ignored.
/// </para>
/// </remarks>
public abstract class ImGuiTestFixture
{
    #region State fields

    /// <summary>Context created by <see cref="SetUp"/> and destroyed by <see cref="TearDown"/>.</summary>
    protected ImGuiContext _ctx = null!;

    /// <summary>Declared guard messages that have not fired yet.</summary>
    private readonly List<string> _pendingExpectedAsserts = new();

    /// <summary>Declared guard messages that never fired during the test.</summary>
    private readonly List<string> _unfiredExpectedAsserts = new();

    /// <summary>Guard failures observed during the test that no expectation declared.</summary>
    private readonly List<string> _unexpectedAsserts = new();

    #endregion

    #region Lifecycle

    /// <summary>
    /// Creates the context, installs it as the current context, applies the defaults shared by every
    /// fixture, and then applies the derived fixture's own configuration.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(_ctx);
        _ctx.IO.IniFilename = null; // Tests must never read or write the developer's.ini file.
        _ctx.IO.DisplaySize = new ImVec2(1280.0f, 720.0f);
        _ctx.IO.DeltaTime = 1.0f / 60.0f;
        _ctx.IO.Fonts.AddFontDefault();

        _pendingExpectedAsserts.Clear();
        _unfiredExpectedAsserts.Clear();
        _unexpectedAsserts.Clear();
        ImAssert.FailureHandler = RecordAssertFailure;

        ConfigureContext();
    }

    /// <summary>
    /// Applies the input-processing and windowing options that a derived fixture needs before its
    /// tests run.
    /// </summary>
    /// <remarks>
    /// The base setup deliberately leaves input trickling and config flags at their canonical
    /// defaults, because those choices differ per fixture: docking tests enable docking, navigation
    /// tests enable keyboard navigation, and the input tests disable trickling so that one queued
    /// batch produces one deterministic frame. Overriding this method keeps those decisions local to
    /// the fixture that depends on them.
    /// </remarks>
    protected virtual void ConfigureContext()
    {
    }

    /// <summary>
    /// Destroys the context and verifies that every canonical guard failure observed during the test
    /// was declared with <see cref="ExpectAssert"/>.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        try
        {
            if (ImGui.GetCurrentContext() == _ctx && _ctx.WithinFrameScope)
                ImGui.EndFrame();
        }
        finally
        {
            try
            {
                ImGui.DestroyContext(_ctx);
            }
            finally
            {
                ImGui.SetCurrentContext(null);
                ImAssert.FailureHandler = null;

                if (ImAssert.Enabled)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(_unfiredExpectedAsserts, Is.Empty,
                            "Declared canonical guard failures that never fired.");
                        Assert.That(_unexpectedAsserts, Is.Empty,
                            "Unexpected canonical guard failures; declare the ones a test intends to trip.");
                    });
                }
            }
        }
    }

    #endregion

    #region Frame helpers

    /// <summary>
    /// Starts a frame, first completing a frame that an earlier step left open.
    /// </summary>
    /// <remarks>
    /// Canonical <c>NewFrame()</c> asserts when the previous frame was never rendered or ended. Tests
    /// that submit several frames in one body therefore use this helper, which ends the open frame
    /// before starting the next one. A test that needs explicit draw-data submission still calls
    /// <see cref="ImGui.Render"/> itself.
    /// </remarks>
    protected static void NewTestFrame()
    {
        ImGuiContext? current = ImGui.GetCurrentContext();
        if (current != null && current.WithinFrameScope)
            ImGui.EndFrame();

        ImGui.NewFrame();
    }

    #endregion

    #region Expectation recording

    /// <summary>
    /// Declares that the enclosed code is expected to trip a canonical guard whose message contains
    /// <paramref name="message"/>.
    /// </summary>
    /// <param name="message">Distinctive fragment of the expected guard message.</param>
    /// <returns>A scope that ends the expectation when disposed.</returns>
    /// <remarks>
    /// The expectation is inert when guard calls are compiled out of the running build, so a test
    /// that declares one still passes against a Release build. An expectation that never fires fails
    /// the test during teardown.
    /// </remarks>
    protected IDisposable ExpectAssert(string message)
    {
        if (!ImAssert.Enabled)
            return NoOpScope.Instance;

        _pendingExpectedAsserts.Add(message);
        return new ExpectedAssertScope(this, message);
    }

    /// <summary>
    /// Records a canonical guard failure, matching it against the expectations declared by the test.
    /// </summary>
    /// <param name="message">Guard message supplied by <see cref="ImAssert"/>.</param>
    private void RecordAssertFailure(string message)
    {
        int declared = _pendingExpectedAsserts.FindIndex(
            expected => message.Contains(expected, StringComparison.Ordinal));
        if (declared >= 0)
            _pendingExpectedAsserts.RemoveAt(declared);
        else
            _unexpectedAsserts.Add(message);
    }

    /// <summary>
    /// Removes a declared expectation when its scope closes and records it as unfired when the guard
    /// did not run.
    /// </summary>
    /// <param name="message">Fragment supplied by <see cref="ExpectAssert"/>.</param>
    private void EndExpectedAssert(string message)
    {
        if (_pendingExpectedAsserts.Remove(message))
            _unfiredExpectedAsserts.Add(message);
    }

    #endregion

    #region Expectation scopes

    /// <summary>
    /// Scope returned by <see cref="ExpectAssert"/> that ends the declaration when disposed.
    /// </summary>
    private sealed class ExpectedAssertScope : IDisposable
    {
        /// <summary>Fixture that owns the declaration.</summary>
        private readonly ImGuiTestFixture _fixture;

        /// <summary>Declared message fragment.</summary>
        private readonly string _message;

        /// <summary>Whether the scope has already been disposed.</summary>
        private bool _disposed;

        /// <summary>Creates a scope for one declared guard expectation.</summary>
        /// <param name="fixture">Fixture that owns the declaration.</param>
        /// <param name="message">Declared message fragment.</param>
        internal ExpectedAssertScope(ImGuiTestFixture fixture, string message)
        {
            _fixture = fixture;
            _message = message;
        }

        /// <summary>Ends the declaration.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _fixture.EndExpectedAssert(_message);
        }
    }

    /// <summary>
    /// Shared inert scope used when guard calls are compiled out of the running build.
    /// </summary>
    private sealed class NoOpScope : IDisposable
    {
        /// <summary>Single shared instance.</summary>
        internal static readonly NoOpScope Instance = new();

        /// <summary>Prevents external construction.</summary>
        private NoOpScope()
        {
        }

        /// <summary>Does nothing.</summary>
        public void Dispose()
        {
        }
    }

    #endregion
}