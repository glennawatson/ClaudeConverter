// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The payload of a <c>content_block_delta</c> event.</summary>
/// <param name="Index">The block index being appended to.</param>
/// <param name="Delta">The increment.</param>
/// <param name="Type">The event discriminator.</param>
[System.Diagnostics.DebuggerDisplay("StreamContentBlockDelta: {ToString(),nq}")]
public sealed record StreamContentBlockDelta(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] StreamDelta Delta,
    [property: JsonPropertyName("type")] string Type = StreamEventNames.ContentBlockDelta);
