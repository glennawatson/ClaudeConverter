// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The body returned from a non-streaming <c>POST /v1/messages</c> call.</summary>
/// <param name="Id">The message identifier.</param>
/// <param name="Model">The model the client asked for, echoed back.</param>
/// <param name="Content">The assistant's content blocks.</param>
/// <param name="StopReason">Why generation ended; see <see cref="StopReasons"/>.</param>
/// <param name="Usage">The token accounting for the call.</param>
/// <param name="StopSequence">The stop sequence that ended generation, when one did.</param>
/// <param name="Type">The object discriminator, always <c>message</c>.</param>
/// <param name="Role">The author of the message, always <c>assistant</c>.</param>
[System.Diagnostics.DebuggerDisplay("MessagesResponse: {ToString(),nq}")]
public sealed record MessagesResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("content")] List<ContentBlock> Content,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("usage")] TokenUsage Usage,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence = null,
    [property: JsonPropertyName("type")] string Type = "message",
    [property: JsonPropertyName("role")] string Role = AnthropicMessage.AssistantRole);
