// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The body of a <c>POST /v1/messages</c> call.</summary>
/// <param name="Model">The Claude model the client asked for.</param>
/// <param name="Messages">The conversation so far.</param>
/// <param name="MaxTokens">The output ceiling.</param>
/// <param name="System">The system prompt, as text or as blocks.</param>
/// <param name="Stream">Whether the response is streamed as server-sent events.</param>
/// <param name="Temperature">The sampling temperature.</param>
/// <param name="TopP">The nucleus sampling cutoff.</param>
/// <param name="TopK">The top-k sampling cutoff.</param>
/// <param name="StopSequences">Sequences that end generation.</param>
/// <param name="Tools">The tools the model may call.</param>
/// <param name="ToolChoice">How the model should pick a tool.</param>
/// <param name="Thinking">The extended-thinking request.</param>
/// <param name="OutputConfig">The output shaping request, including effort.</param>
/// <param name="Metadata">Opaque caller metadata, which the proxy does not forward.</param>
[System.Diagnostics.DebuggerDisplay("MessagesRequest: {IsStreaming}")]
public sealed record MessagesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<AnthropicMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] MessageContent? System = null,
    [property: JsonPropertyName("stream")] bool? Stream = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("top_p")] double? TopP = null,
    [property: JsonPropertyName("top_k")] int? TopK = null,
    [property: JsonPropertyName("stop_sequences")] List<string>? StopSequences = null,
    [property: JsonPropertyName("tools")] List<ToolDefinition>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("thinking")] ThinkingConfig? Thinking = null,
    [property: JsonPropertyName("output_config")] OutputConfig? OutputConfig = null,
    [property: JsonPropertyName("metadata")] JsonElement? Metadata = null)
{
    /// <summary>Gets a value indicating whether the client asked for a streamed response.</summary>
    public bool IsStreaming => Stream ?? false;
}
