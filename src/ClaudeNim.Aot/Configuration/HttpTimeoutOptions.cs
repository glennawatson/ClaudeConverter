// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Timeouts applied to the upstream HTTP connection.</summary>
/// <param name="ReadSeconds">How long a non-streamed call may take from start to finish.</param>
/// <param name="ConnectSeconds">How long establishing the upstream connection may take.</param>
/// <param name="StreamIdleSeconds">How long a streamed response may produce nothing before it is abandoned.</param>
/// <remarks>
/// A streamed turn is bounded by silence, not by duration. A long answer is healthy and may run
/// well past any total timeout, so applying one to a stream cuts off exactly the turns most worth
/// waiting for; what actually signals a dead stream is a gap with no bytes in it.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("HttpTimeoutOptions: {ToString(),nq}")]
public sealed record HttpTimeoutOptions(
    int ReadSeconds = 120,
    int ConnectSeconds = 10,
    int StreamIdleSeconds = 120)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Timeouts";
}
