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
/// <remarks>
/// <see cref="EnableSonnetThinking"/> defaults to off, unlike the other tiers. Claude Code's Auto
/// Mode classifier always evaluates actions against a Sonnet-tier model, regardless of which model
/// the user is actually working with, and it sends a large system prompt (~150,000 characters
/// observed) on every single tool call. With reasoning enabled that turn cost 4-5 real seconds per
/// call on NVIDIA NIM — added latency in front of every Bash/tool dispatch, not a one-off cost —
/// because the classifier only needs a terse verdict, not a worked chain of thought. A real
/// Sonnet-tier coding request loses the reasoning step too, which is the trade-off: faster and
/// less verbose everywhere the Sonnet tier is used, in exchange for Auto Mode staying responsive.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ModelRoutingOptions: {ToString(),nq}")]
public sealed record ModelRoutingOptions(
    string Default = ModelRoutingOptions.FallbackModel,
    string Opus = "",
    string Sonnet = "",
    string Haiku = "",
    bool EnableThinking = true,
    bool? EnableOpusThinking = null,
    bool? EnableSonnetThinking = false,
    bool? EnableHaikuThinking = null)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "ModelRouting";

    /// <summary>The NIM model used when configuration names none.</summary>
    internal const string FallbackModel = "nvidia/nemotron-3-super-120b-a12b";
}
