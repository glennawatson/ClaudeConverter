// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The OpenAI Responses API request a Codex-family client sends.</summary>
/// <param name="Model">The model identifier the client asked for.</param>
/// <param name="Input">The conversation, as a typed item array.</param>
/// <param name="Instructions">The system-level instruction for the turn.</param>
/// <param name="Tools">The tools offered to the model.</param>
/// <param name="ToolChoice">How the model should choose among the offered tools.</param>
/// <param name="ParallelToolCalls">Whether the model may return more than one tool call at once.</param>
/// <param name="Reasoning">The reasoning controls for the turn.</param>
/// <param name="Store">
/// Whether the caller asked the turn to be retrievable later. The proxy keeps no state, so this is
/// accepted and ignored.
/// </param>
/// <param name="Stream">Whether the turn should be streamed.</param>
/// <param name="Text">Controls over the turn's text output.</param>
/// <param name="Temperature">The sampling temperature.</param>
/// <param name="TopP">The nucleus-sampling cutoff.</param>
/// <param name="MaxOutputTokens">The output ceiling.</param>
/// <param name="PreviousResponseId">
/// A prior turn to continue from. The proxy keeps no state, so this is accepted and ignored — the
/// caller is expected to replay the full conversation in <see cref="Input"/>, as Codex CLI itself
/// does.
/// </param>
[System.Diagnostics.DebuggerDisplay("ResponsesRequest: {Model}")]
public sealed record ResponsesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] List<ResponseInputItem> Input,
    [property: JsonPropertyName("instructions")] string? Instructions = null,
    [property: JsonPropertyName("tools")] List<CodexTool>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls = null,
    [property: JsonPropertyName("reasoning")] ReasoningOptions? Reasoning = null,
    [property: JsonPropertyName("store")] bool? Store = null,
    [property: JsonPropertyName("stream")] bool? Stream = null,
    [property: JsonPropertyName("text")] TextControls? Text = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("top_p")] double? TopP = null,
    [property: JsonPropertyName("max_output_tokens")] int? MaxOutputTokens = null,
    [property: JsonPropertyName("previous_response_id")] string? PreviousResponseId = null)
{
    /// <summary>Gets a value indicating whether the caller asked for a streamed turn.</summary>
    public bool IsStreaming => Stream == true;
}
