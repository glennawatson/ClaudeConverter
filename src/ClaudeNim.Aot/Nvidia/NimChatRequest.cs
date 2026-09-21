// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The body of a NVIDIA NIM <c>POST /v1/chat/completions</c> call.</summary>
/// <param name="Model">The NIM model identifier.</param>
/// <param name="Messages">The conversation transcript.</param>
/// <param name="MaxTokens">The output ceiling.</param>
/// <param name="Stream">Whether the response streams.</param>
/// <param name="StreamOptions">Options applied to a streamed response.</param>
/// <param name="Temperature">The sampling temperature.</param>
/// <param name="TopP">The nucleus sampling cutoff.</param>
/// <param name="TopK">The top-k sampling cutoff.</param>
/// <param name="MinP">The minimum token probability.</param>
/// <param name="RepetitionPenalty">The repetition penalty.</param>
/// <param name="PresencePenalty">The presence penalty.</param>
/// <param name="FrequencyPenalty">The frequency penalty.</param>
/// <param name="MinTokens">The minimum number of tokens to generate.</param>
/// <param name="Seed">The deterministic sampling seed.</param>
/// <param name="Stop">Sequences that end generation.</param>
/// <param name="IgnoreEos">Whether the end-of-sequence token is ignored.</param>
/// <param name="Tools">The tools offered to the model.</param>
/// <param name="ToolChoice">How the model should pick a tool.</param>
/// <param name="ParallelToolCalls">Whether the model may emit parallel tool calls.</param>
/// <param name="ChatTemplateKwargs">Reasoning control passed to the model's chat template.</param>
[System.Diagnostics.DebuggerDisplay("NimChatRequest: {ToString(),nq}")]
public sealed record NimChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<NimChatMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("stream_options")] NimStreamOptions? StreamOptions = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("top_p")] double? TopP = null,
    [property: JsonPropertyName("top_k")] int? TopK = null,
    [property: JsonPropertyName("min_p")] double? MinP = null,
    [property: JsonPropertyName("repetition_penalty")] double? RepetitionPenalty = null,
    [property: JsonPropertyName("presence_penalty")] double? PresencePenalty = null,
    [property: JsonPropertyName("frequency_penalty")] double? FrequencyPenalty = null,
    [property: JsonPropertyName("min_tokens")] int? MinTokens = null,
    [property: JsonPropertyName("seed")] int? Seed = null,
    [property: JsonPropertyName("stop")] List<string>? Stop = null,
    [property: JsonPropertyName("ignore_eos")] bool? IgnoreEos = null,
    [property: JsonPropertyName("tools")] List<NimTool>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls = null,
    [property: JsonPropertyName("chat_template_kwargs")] NimChatTemplateKwargs? ChatTemplateKwargs = null);
