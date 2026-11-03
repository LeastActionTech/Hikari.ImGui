// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Hikari.ImGui;

/// <summary>
/// Single interception point for the port's canonical Dear ImGui assertion guards.
/// </summary>
/// <remarks>
/// <para>
/// Canonical Dear ImGui expresses programmer-error guards with <c>IM_ASSERT</c>
///., which the port previously wrote directly as
/// <see cref="Debug.Assert(bool)"/>. Routing every guard through this type gives the port one
/// auditable assertion seam, keeps the canonical "assert only reports programmer error, never
/// substitutes for validation" contract, and lets a host or the test harness record guard failures
/// through <see cref="FailureHandler"/> instead of aborting the process.
/// </para>
/// <para>
/// The type is internal to <c>Hikari.ImGui</c> and is reachable from the test assembly through the
/// project's <c>InternalsVisibleTo</c> entry.
/// </para>
/// </remarks>
internal static class ImAssert
{
    /// <summary>
    /// Optional recorder that receives the message of a failed guard instead of the debugger
    /// assertion being raised. A <see langword="null"/> value selects the default behavior.
    /// </summary>
    /// <remarks>
    /// The test harness installs a recorder for the duration of a fixture so that a canonical guard
    /// failure becomes an observable, declarable event. Production code leaves this unset and falls
    /// back to <see cref="Debug.Assert(bool, string)"/>.
    /// </remarks>
    internal static Action<string>? FailureHandler { get; set; }

    /// <summary>
    /// Reports whether guard calls are compiled into the running build.
    /// </summary>
    /// <remarks>
    /// Mirrors the compile-time nature of canonical <c>IM_ASSERT</c>. Callers that declare an
    /// expected guard failure, such as the test harness, consult this so their expectations remain
    /// valid when the calls are compiled out of a Release build.
    /// </remarks>
    internal static bool Enabled
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Checks a canonical programmer-error guard and reports the failure when it does not hold.
    /// </summary>
    /// <param name="condition">Condition that must hold; a <see langword="false"/> value is a
    /// programmer error.</param>
    /// <param name="message">Human-readable text describing the violated contract. When the caller
    /// omits it, the compiler supplies the source text of <paramref name="condition"/>, matching the
    /// diagnostic quality of <see cref="Debug.Assert(bool)"/>.</param>
    /// <remarks>
    /// When <see cref="FailureHandler"/> is installed, the failure is handed to it and the debugger
    /// assertion is skipped; otherwise the canonical behavior of
    /// <see cref="Debug.Assert(bool, string)"/> is used. Guard calls are removed from Release builds
    /// by <see cref="ConditionalAttribute"/> on this method.
    /// </remarks>
    [Conditional("DEBUG")]
    internal static void Check(
        bool condition,
        [CallerArgumentExpression(nameof(condition))] string? message = null)
    {
        if (condition)
            return;

        if (FailureHandler != null)
        {
            FailureHandler(message ?? "Assertion failed.");
            return;
        }

        Debug.Assert(condition, message);
    }
}