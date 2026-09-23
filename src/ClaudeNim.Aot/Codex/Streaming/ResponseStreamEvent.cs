// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex.Streaming;

/// <summary>One Responses API server-sent event.</summary>
/// <param name="Type">The event discriminator; see <see cref="ResponseStreamEventTypes"/>.</param>
/// <param name="SequenceNumber">The event's position in the stream, starting at zero.</param>
/// <param name="Response">The turn, on a lifecycle event.</param>
/// <param name="Item">The full item, on an <c>output_item</c> event.</param>
/// <param name="ItemId">The item an incremental event applies to.</param>
/// <param name="OutputIndex">The item's position in the turn's output array.</param>
/// <param name="ContentIndex">The content part an incremental event applies to.</param>
/// <param name="SummaryIndex">The reasoning summary part an incremental event applies to.</param>
/// <param name="Delta">The fragment carried by an incremental event.</param>
/// <param name="Text">The final text carried by a <c>done</c> event.</param>
/// <param name="Part">The full content part, on a <c>content_part</c> event.</param>
/// <remarks>
/// One flat type serves every event kind rather than the many small record types Anthropic's own
/// streaming events use: the Responses API's event surface is wide enough — twenty-odd kinds, most
/// sharing the same handful of addressing fields — that a type hierarchy would cost more files than
/// it saves, and native AOT cannot generate the polymorphic resolver one would need in any case.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ResponseStreamEvent: {Type}")]
public sealed record ResponseStreamEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("sequence_number")] int SequenceNumber = 0,
    [property: JsonPropertyName("response")] ResponsesResponse? Response = null,
    [property: JsonPropertyName("item")] ResponseInputItem? Item = null,
    [property: JsonPropertyName("item_id")] string? ItemId = null,
    [property: JsonPropertyName("output_index")] int? OutputIndex = null,
    [property: JsonPropertyName("content_index")] int? ContentIndex = null,
    [property: JsonPropertyName("summary_index")] int? SummaryIndex = null,
    [property: JsonPropertyName("delta")] string? Delta = null,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("part")] ResponseContentItem? Part = null);
