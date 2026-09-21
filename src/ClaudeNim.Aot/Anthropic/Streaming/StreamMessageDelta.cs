// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The payload of a <c>message_delta</c> event.</summary>
/// <param name="Delta">Why generation ended.</param>
/// <param name="Usage">The final token accounting for the turn.</param>
/// <param name="Type">The event discriminator.</param>
[System.Diagnostics.DebuggerDisplay("StreamMessageDelta: {ToString(),nq}")]
public sealed record StreamMessageDelta(
    [property: JsonPropertyName("delta")] StreamStopDetail Delta,
    [property: JsonPropertyName("usage")] TokenUsage Usage,
    [property: JsonPropertyName("type")] string Type = StreamEventNames.MessageDelta);
