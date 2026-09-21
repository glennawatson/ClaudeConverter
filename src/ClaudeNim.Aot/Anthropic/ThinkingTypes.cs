// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Which <c>thinking.type</c> forms a listed model accepts.</summary>
/// <param name="Adaptive">Whether <c>adaptive</c> is accepted, where the model decides how much to think.</param>
/// <param name="Enabled">Whether <c>enabled</c> with an explicit <c>budget_tokens</c> ceiling is accepted.</param>
[System.Diagnostics.DebuggerDisplay("ThinkingTypes: {ToString(),nq}")]
public readonly record struct ThinkingTypes(
    [property: JsonPropertyName("adaptive")] CapabilitySupport Adaptive,
    [property: JsonPropertyName("enabled")] CapabilitySupport Enabled);
