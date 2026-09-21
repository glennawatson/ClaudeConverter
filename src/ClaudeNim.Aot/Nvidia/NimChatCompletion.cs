// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>A non-streamed NIM <c>chat.completion</c> response.</summary>
/// <param name="Id">The completion identifier.</param>
/// <param name="Model">The model that produced the completion.</param>
/// <param name="Choices">The completion choices.</param>
/// <param name="Usage">Token accounting for the call.</param>
[System.Diagnostics.DebuggerDisplay("NimChatCompletion: {ToString(),nq}")]
public sealed record NimChatCompletion(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("choices")] List<NimChoice>? Choices = null,
    [property: JsonPropertyName("usage")] NimUsage? Usage = null);
