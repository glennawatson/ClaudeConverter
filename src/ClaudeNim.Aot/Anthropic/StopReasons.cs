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

    /// <summary>Maps an OpenAI-style <c>finish_reason</c> onto its Anthropic equivalent.</summary>
    /// <param name="finishReason">The upstream finish reason, which may be <see langword="null"/>.</param>
    /// <returns>The matching Anthropic stop reason.</returns>
    public static string FromFinishReason(string? finishReason) => finishReason switch
    {
        "tool_calls" => ToolUse,
        "length" => MaxTokens,
        _ => EndTurn,
    };
}
