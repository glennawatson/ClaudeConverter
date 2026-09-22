// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Timeouts applied to the upstream HTTP connection.</summary>
/// <param name="ReadSeconds">How long a streamed call may wait for its headers, and a non-streamed body for the rest of itself once they have arrived.</param>
/// <param name="ConnectSeconds">How long establishing the upstream connection may take.</param>
/// <param name="StreamIdleSeconds">How long a streamed response may produce nothing before it is abandoned.</param>
/// <param name="CompletionSeconds">How long a non-streamed call may take from start to finish.</param>
/// <remarks>
/// <para>
/// A streamed turn is bounded by silence, not by duration. A long answer is healthy and may run
/// well past any total timeout, so applying one to a stream cuts off exactly the turns most worth
/// waiting for; what actually signals a dead stream is a gap with no bytes in it. Its headers are
/// a different matter: they arrive in about a second on a healthy endpoint, so a short bound on
/// that phase is what catches a connection that will never answer.
/// </para>
/// <para>
/// A non-streamed call has no such phase to bound. NIM sends nothing at all — not even a status
/// line — until the whole answer has been generated, so the wait for its headers <em>is</em> the
/// generation, and a bound sized for a header wait cuts off every answer that takes longer than
/// one. That is why it has a bound of its own, and why that bound is minutes rather than seconds:
/// a reasoning model working through a long prompt routinely spends more than two minutes before
/// the first byte of its answer exists.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("HttpTimeoutOptions: {ToString(),nq}")]
public sealed record HttpTimeoutOptions(
    int ReadSeconds = 120,
    int ConnectSeconds = 10,
    int StreamIdleSeconds = 120,
    int CompletionSeconds = 600)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Timeouts";
}
