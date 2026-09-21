// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>What the proxy knows about a NVIDIA NIM model beyond its identifier.</summary>
/// <param name="Id">The NIM model identifier.</param>
/// <param name="DisplayName">The name shown in a model picker.</param>
/// <param name="SupportsTools">Whether the model can call client-supplied tools.</param>
/// <param name="SupportsVision">Whether the model accepts image input.</param>
/// <param name="SupportsThinking">Whether the model returns a reasoning trace.</param>
/// <param name="MaxInputTokens">The context window, or <see langword="null"/> when unknown.</param>
/// <param name="MaxOutputTokens">The output ceiling, or <see langword="null"/> when unknown.</param>
/// <remarks>
/// The NIM listing endpoint returns identifiers and nothing else, so sizing and capability
/// have to come from somewhere. Anything not stated here falls back to the configured
/// defaults rather than being invented, so a model the proxy has never heard of is still
/// listed and still routable.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimModelProfile: {ToString(),nq}")]
public sealed record NimModelProfile(
    string Id,
    string DisplayName,
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsThinking,
    int? MaxInputTokens = null,
    int? MaxOutputTokens = null);
