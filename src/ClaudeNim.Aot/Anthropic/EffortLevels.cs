// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic;

/// <summary>The <c>output_config.effort</c> values Claude 5 clients send.</summary>
/// <remarks>
/// NVIDIA NIM exposes the same idea as the OpenAI-style <c>reasoning_effort</c> field, which
/// only spans <c>low</c>, <c>medium</c> and <c>high</c>. <see cref="ToReasoningEffort"/> folds
/// the two wider Claude levels onto <c>high</c>.
/// </remarks>
public static class EffortLevels
{
    /// <summary>Minimal reasoning, for subagents and simple tasks.</summary>
    internal const string Low = "low";

    /// <summary>Moderate reasoning.</summary>
    internal const string Medium = "medium";

    /// <summary>The default level.</summary>
    internal const string High = "high";

    /// <summary>Above <see cref="High"/>; the usual choice for coding and agentic work.</summary>
    internal const string ExtraHigh = "xhigh";

    /// <summary>The most thorough level.</summary>
    internal const string Max = "max";

    /// <summary>Maps a Claude effort level onto the OpenAI-style <c>reasoning_effort</c> NIM accepts.</summary>
    /// <param name="effort">The Claude effort level, which may be <see langword="null"/>.</param>
    /// <returns>The upstream effort value, or <see langword="null"/> when none applies.</returns>
    public static string? ToReasoningEffort(string? effort) => effort switch
    {
        Low or Medium => effort,
        High or ExtraHigh or Max => High,
        _ => null,
    };
}
