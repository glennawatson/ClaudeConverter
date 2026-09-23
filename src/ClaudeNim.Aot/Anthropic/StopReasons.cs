// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic;

/// <summary>The <c>stop_reason</c> values Anthropic clients expect on a completed message.</summary>
public static class StopReasons
{
    /// <summary>The model finished its turn.</summary>
    internal const string EndTurn = "end_turn";

    /// <summary>Generation stopped at the output ceiling.</summary>
    internal const string MaxTokens = "max_tokens";

    /// <summary>The model is waiting for tool results.</summary>
    internal const string ToolUse = "tool_use";

    /// <summary>Generation stopped at a caller-supplied stop sequence.</summary>
    internal const string StopSequence = "stop_sequence";

    /// <summary>The turn was declined by a safety classifier rather than answered.</summary>
    /// <remarks>
    /// Claude 5.5-era clients check for this before reading <c>content</c>, and a turn carrying it
    /// is worth re-asking elsewhere rather than treating as an answer. NVIDIA's own filters stop a
    /// turn the same way, so the two are reported the same way.
    /// </remarks>
    internal const string Refusal = "refusal";

    /// <summary>The upstream finish reason meaning a filter stopped the turn.</summary>
    private const string ContentFiltered = "content_filter";

    /// <summary>Maps an OpenAI-style <c>finish_reason</c> onto its Anthropic equivalent.</summary>
    /// <param name="finishReason">The upstream finish reason, which may be <see langword="null"/>.</param>
    /// <returns>The matching Anthropic stop reason.</returns>
    public static string FromFinishReason(string? finishReason) => finishReason switch
    {
        "tool_calls" => ToolUse,
        "length" => MaxTokens,
        ContentFiltered => Refusal,
        _ => EndTurn,
    };

    /// <summary>Builds the detail that accompanies a declined turn.</summary>
    /// <param name="stopReason">The stop reason the turn ended with.</param>
    /// <returns>The detail, or <see langword="null"/> for every reason but a refusal.</returns>
    /// <remarks>
    /// Null for anything else on purpose: a client reads this field behind the stop reason, and
    /// Anthropic populates it on a refusal alone.
    /// </remarks>
    public static StopDetail? DetailFor(string? stopReason) =>
        string.Equals(stopReason, Refusal, StringComparison.Ordinal) ? StopDetail.Filtered : null;
}
