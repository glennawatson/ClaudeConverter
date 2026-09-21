// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The payload of a <c>content_block_start</c> event.</summary>
/// <param name="Index">The block index being opened.</param>
/// <param name="ContentBlock">The empty block that later deltas append to.</param>
/// <param name="Type">The event discriminator.</param>
[System.Diagnostics.DebuggerDisplay("StreamContentBlockStart: {ToString(),nq}")]
public sealed record StreamContentBlockStart(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("content_block")] ContentBlock ContentBlock,
    [property: JsonPropertyName("type")] string Type = StreamEventNames.ContentBlockStart);
