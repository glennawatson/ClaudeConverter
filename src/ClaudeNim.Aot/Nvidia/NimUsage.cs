// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The token accounting a NIM completion reports.</summary>
/// <param name="PromptTokens">Tokens consumed by the prompt.</param>
/// <param name="CompletionTokens">Tokens produced by the model.</param>
/// <param name="TotalTokens">The sum of both counts.</param>
[System.Diagnostics.DebuggerDisplay("NimUsage: {ToString(),nq}")]
public readonly record struct NimUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);
