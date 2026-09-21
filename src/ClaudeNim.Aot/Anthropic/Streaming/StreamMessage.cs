// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The message envelope carried by a <c>message_start</c> event.</summary>
/// <param name="Id">The message identifier.</param>
/// <param name="Model">The model the client asked for.</param>
/// <param name="Usage">Prompt usage known at the start of the turn.</param>
/// <param name="Content">Always empty; blocks arrive as later events.</param>
/// <param name="Type">The object discriminator, always <c>message</c>.</param>
/// <param name="Role">The author, always <c>assistant</c>.</param>
/// <param name="StopReason">Always absent at the start of a turn; it arrives on <c>message_delta</c>.</param>
/// <param name="StopSequence">Always absent here; a matched stop sequence is reported when the turn ends.</param>
[System.Diagnostics.DebuggerDisplay("StreamMessage: {ToString(),nq}")]
public sealed record StreamMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("usage")] TokenUsage Usage,
    [property: JsonPropertyName("content")] List<ContentBlock> Content,
    [property: JsonPropertyName("type")] string Type = "message",
    [property: JsonPropertyName("role")] string Role = AnthropicMessage.AssistantRole,
    [property: JsonPropertyName("stop_reason")] string? StopReason = null,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence = null);
