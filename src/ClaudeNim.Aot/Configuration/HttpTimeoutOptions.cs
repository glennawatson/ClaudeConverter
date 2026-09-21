// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Timeouts applied to the upstream HTTP connection.</summary>
/// <param name="ReadSeconds">How long a streamed response may stay idle before it is abandoned.</param>
/// <param name="ConnectSeconds">How long establishing the upstream connection may take.</param>
[System.Diagnostics.DebuggerDisplay("HttpTimeoutOptions: {ToString(),nq}")]
public sealed record HttpTimeoutOptions(
    int ReadSeconds = 120,
    int ConnectSeconds = 10)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Timeouts";
}
