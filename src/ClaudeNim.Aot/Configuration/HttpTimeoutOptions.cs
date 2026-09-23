// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Configuration;

/// <summary>Timeouts applied to the upstream HTTP connection.</summary>
/// <param name="ReadSeconds">How long a streamed call may wait for its headers, and a non-streamed body for the rest of itself once they have arrived.</param>
/// <param name="ConnectSeconds">How long establishing the upstream connection may take.</param>
/// <param name="StreamIdleSeconds">How long a streamed response may produce nothing before it is abandoned.</param>
/// <param name="CompletionSeconds">How long a non-streamed call may take from start to finish.</param>
/// <param name="OpusReadSeconds">The header-wait override for a streamed Opus-tier turn, or unset to use <paramref name="ReadSeconds"/>.</param>
/// <param name="SonnetReadSeconds">The header-wait override for a streamed Sonnet-tier turn, or unset to use <paramref name="ReadSeconds"/>.</param>
/// <param name="HaikuReadSeconds">The header-wait override for a streamed Haiku-tier turn, or unset to use <paramref name="ReadSeconds"/>.</param>
/// <remarks>
/// <para>
/// A streamed turn is bounded by silence, not by duration. A long answer is healthy and may run
/// well past any total timeout, so applying one to a stream cuts off exactly the turns most worth
/// waiting for; what actually signals a dead stream is a gap with no bytes in it. Its headers are
/// a different matter: they arrive in about a second on a healthy endpoint, so a short bound on
/// that phase is what catches a connection that will never answer.
/// </para>
/// <para>
/// That assumption does not hold for every model. The largest one in the catalogue can
/// legitimately spend well over a minute reasoning before it flushes a single byte, and it is
/// usually the one a heavier tier routes to — so the same short bound that correctly kills a dead
/// connection to a small model also kills a live one to a large model doing exactly what it was
/// asked, and the turn is quietly downgraded to a weaker fallback instead. The per-tier overrides
/// exist for this: a tier whose model is known to be slow to start gets a longer header-wait
/// budget than one whose model answers in about a second.
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
    int CompletionSeconds = 600,
    int? OpusReadSeconds = null,
    int? SonnetReadSeconds = null,
    int? HaikuReadSeconds = null)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Timeouts";

    /// <summary>Resolves the header-wait budget for a streamed turn of the given tier.</summary>
    /// <param name="tier">The tier the turn was routed to.</param>
    /// <returns>The tier's override, or <see cref="ReadSeconds"/> when it has none.</returns>
    public int ReadSecondsFor(ModelTier tier) => tier switch
    {
        ModelTier.Opus => OpusReadSeconds ?? ReadSeconds,
        ModelTier.Sonnet => SonnetReadSeconds ?? ReadSeconds,
        ModelTier.Haiku => HaikuReadSeconds ?? ReadSeconds,
        _ => ReadSeconds,
    };
}
