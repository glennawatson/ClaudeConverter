// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The token accounting a Responses API turn reports.</summary>
/// <param name="InputTokens">Tokens consumed by the prompt.</param>
/// <param name="OutputTokens">Tokens produced by the model.</param>
/// <param name="TotalTokens">The sum of both counts.</param>
/// <param name="InputTokensDetails">A breakdown of the input count.</param>
/// <param name="OutputTokensDetails">A breakdown of the output count.</param>
[System.Diagnostics.DebuggerDisplay("ResponseUsage: {ToString(),nq}")]
public readonly record struct ResponseUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens,
    [property: JsonPropertyName("input_tokens_details")] ResponseTokenDetails? InputTokensDetails = null,
    [property: JsonPropertyName("output_tokens_details")] ResponseTokenDetails? OutputTokensDetails = null);
