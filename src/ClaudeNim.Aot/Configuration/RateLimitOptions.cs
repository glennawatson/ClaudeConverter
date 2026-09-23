// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Bounds the request rate and in-flight concurrency the proxy applies to the upstream.</summary>
/// <param name="RequestsPerWindow">The number of upstream requests permitted per window.</param>
/// <param name="WindowSeconds">The length of the sliding window in seconds.</param>
/// <param name="MaxConcurrency">The maximum number of upstream requests in flight at once.</param>
/// <param name="MinGapMilliseconds">
/// The minimum spacing enforced between any two outbound NIM calls, or <c>0</c> to disable it.
/// </param>
/// <remarks>
/// A window and a concurrency ceiling both bound the total volume, but neither stops several calls
/// leaving at once: a fallback chain walk can ask three models within the same second and still sit
/// well inside both limits. NVIDIA's free endpoints appear to react to that burst itself, not just
/// the volume, so <see cref="MinGapMilliseconds"/> paces every physical call evenly instead — the
/// same shape of traffic a single well-behaved client produces.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("RateLimitOptions: {ToString(),nq}")]
public sealed record RateLimitOptions(
    int RequestsPerWindow = 40,
    int WindowSeconds = 60,
    int MaxConcurrency = 5,
    int MinGapMilliseconds = 2_000)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "RateLimits";
}
