// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The payload of a <c>message_start</c> event.</summary>
/// <param name="Message">The message envelope.</param>
/// <param name="Type">The event discriminator.</param>
[System.Diagnostics.DebuggerDisplay("StreamMessageStart: {ToString(),nq}")]
public sealed record StreamMessageStart(
    [property: JsonPropertyName("message")] StreamMessage Message,
    [property: JsonPropertyName("type")] string Type = StreamEventNames.MessageStart);
