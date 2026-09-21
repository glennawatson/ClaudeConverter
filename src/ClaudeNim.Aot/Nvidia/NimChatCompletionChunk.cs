// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One <c>chat.completion.chunk</c> from a streamed NIM response.</summary>
/// <param name="Id">The completion identifier.</param>
/// <param name="Model">The model that produced the chunk.</param>
/// <param name="Choices">The choices carried by this chunk.</param>
/// <param name="Usage">Token accounting, present on the final chunk when usage was requested.</param>
/// <param name="Error">A failure reported mid-stream, after the status code was already sent.</param>
[System.Diagnostics.DebuggerDisplay("NimChatCompletionChunk: {ToString(),nq}")]
public sealed record NimChatCompletionChunk(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("choices")] List<NimStreamChoice>? Choices = null,
    [property: JsonPropertyName("usage")] NimUsage? Usage = null,
    [property: JsonPropertyName("error")] NimStreamError? Error = null);
