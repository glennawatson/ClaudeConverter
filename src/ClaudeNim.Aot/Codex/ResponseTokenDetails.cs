// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>A breakdown of one side of a Responses API turn's token accounting.</summary>
/// <param name="CachedTokens">Prompt tokens served from cache.</param>
/// <param name="ReasoningTokens">Output tokens spent on reasoning rather than the visible answer.</param>
[System.Diagnostics.DebuggerDisplay("ResponseTokenDetails: {ToString(),nq}")]
public readonly record struct ResponseTokenDetails(
    [property: JsonPropertyName("cached_tokens")] int? CachedTokens = null,
    [property: JsonPropertyName("reasoning_tokens")] int? ReasoningTokens = null);
