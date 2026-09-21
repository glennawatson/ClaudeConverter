// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Bounds the request rate and in-flight concurrency the proxy applies to the upstream.</summary>
/// <param name="RequestsPerWindow">The number of upstream requests permitted per window.</param>
/// <param name="WindowSeconds">The length of the sliding window in seconds.</param>
/// <param name="MaxConcurrency">The maximum number of upstream requests in flight at once.</param>
[System.Diagnostics.DebuggerDisplay("RateLimitOptions: {ToString(),nq}")]
public sealed record RateLimitOptions(
    int RequestsPerWindow = 40,
    int WindowSeconds = 60,
    int MaxConcurrency = 5)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "RateLimits";
}
