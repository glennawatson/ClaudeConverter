// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The body of a <c>POST /v1/messages/count_tokens</c> call.</summary>
/// <param name="Model">The Claude model the count is for.</param>
/// <param name="Messages">The conversation to measure.</param>
/// <param name="System">The system prompt to measure.</param>
/// <param name="Tools">The tool definitions to measure.</param>
/// <param name="ToolChoice">How the model would pick a tool; measured but not otherwise used.</param>
[System.Diagnostics.DebuggerDisplay("TokenCountRequest: {ToString(),nq}")]
public sealed record TokenCountRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<AnthropicMessage> Messages,
    [property: JsonPropertyName("system")] MessageContent? System = null,
    [property: JsonPropertyName("tools")] List<ToolDefinition>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null);
