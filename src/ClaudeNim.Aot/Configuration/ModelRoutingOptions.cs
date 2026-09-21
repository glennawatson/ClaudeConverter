// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Maps the Claude model tiers a client asks for onto concrete NVIDIA NIM model names.</summary>
/// <param name="Default">The NIM model used when no tier-specific override matches.</param>
/// <param name="Opus">The NIM model serving Opus-tier requests, or empty to use <paramref name="Default"/>.</param>
/// <param name="Sonnet">The NIM model serving Sonnet-tier requests, or empty to use <paramref name="Default"/>.</param>
/// <param name="Haiku">The NIM model serving Haiku-tier requests, or empty to use <paramref name="Default"/>.</param>
/// <param name="EnableThinking">Whether reasoning is requested when a tier has no override.</param>
/// <param name="EnableOpusThinking">The reasoning override for Opus-tier requests.</param>
/// <param name="EnableSonnetThinking">The reasoning override for Sonnet-tier requests.</param>
/// <param name="EnableHaikuThinking">The reasoning override for Haiku-tier requests.</param>
[System.Diagnostics.DebuggerDisplay("ModelRoutingOptions: {ToString(),nq}")]
public sealed record ModelRoutingOptions(
    string Default = ModelRoutingOptions.FallbackModel,
    string Opus = "",
    string Sonnet = "",
    string Haiku = "",
    bool EnableThinking = true,
    bool? EnableOpusThinking = null,
    bool? EnableSonnetThinking = null,
    bool? EnableHaikuThinking = null)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "ModelRouting";

    /// <summary>The NIM model used when configuration names none.</summary>
    internal const string FallbackModel = "nvidia/nemotron-3-super-120b-a12b";
}
