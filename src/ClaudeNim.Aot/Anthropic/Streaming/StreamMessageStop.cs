// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The payload of a <c>message_stop</c> event.</summary>
/// <param name="Type">The event discriminator.</param>
[System.Diagnostics.DebuggerDisplay("StreamMessageStop: {ToString(),nq}")]
public readonly record struct StreamMessageStop(
    [property: JsonPropertyName("type")] string Type)
{
    /// <summary>Gets the only value this payload ever takes.</summary>
    public static StreamMessageStop Instance => new(StreamEventNames.MessageStop);
}
