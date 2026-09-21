// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The token accounting Anthropic returns alongside a message.</summary>
/// <param name="InputTokens">Tokens consumed by the prompt.</param>
/// <param name="OutputTokens">Tokens produced by the model.</param>
[System.Diagnostics.DebuggerDisplay("TokenUsage: {ToString(),nq}")]
public readonly record struct TokenUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);
