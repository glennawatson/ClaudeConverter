// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>Reasoning controls carried on a Responses API request.</summary>
/// <param name="Effort">How much reasoning the caller asked for; <c>minimal</c>, <c>low</c>, <c>medium</c>, or <c>high</c>.</param>
/// <param name="Summary">How much of the reasoning trace the caller wants disclosed.</param>
/// <param name="Context">Which turns of the conversation reasoning should be retained across.</param>
[System.Diagnostics.DebuggerDisplay("ReasoningOptions: {ToString(),nq}")]
public sealed record ReasoningOptions(
    [property: JsonPropertyName("effort")] string? Effort = null,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("context")] string? Context = null);
