// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The increment carried by a <c>content_block_delta</c> event.</summary>
/// <param name="Type">Which member is populated; see <see cref="StreamDeltaTypes"/>.</param>
/// <param name="Text">The text appended to a text block.</param>
/// <param name="Thinking">The reasoning appended to a thinking block.</param>
/// <param name="PartialJson">The JSON fragment appended to a tool call's arguments.</param>
[System.Diagnostics.DebuggerDisplay("StreamDelta: {ToString(),nq}")]
public sealed record StreamDelta(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("thinking")] string? Thinking = null,
    [property: JsonPropertyName("partial_json")] string? PartialJson = null)
{
    /// <summary>Creates a text increment.</summary>
    /// <param name="text">The appended text.</param>
    /// <returns>The created increment.</returns>
    public static StreamDelta ForText(string text) => new(StreamDeltaTypes.Text, Text: text);

    /// <summary>Creates a reasoning increment.</summary>
    /// <param name="thinking">The appended reasoning.</param>
    /// <returns>The created increment.</returns>
    public static StreamDelta ForThinking(string thinking) => new(StreamDeltaTypes.Thinking, Thinking: thinking);

    /// <summary>Creates a tool-argument increment.</summary>
    /// <param name="partialJson">The appended JSON fragment.</param>
    /// <returns>The created increment.</returns>
    public static StreamDelta ForToolArguments(string partialJson) =>
        new(StreamDeltaTypes.InputJson, PartialJson: partialJson);
}
