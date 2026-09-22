// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The caller's output shaping request.</summary>
/// <param name="Effort">How hard the model should work; see <see cref="EffortLevels"/>.</param>
/// <param name="Format">The structured-output schema the answer must conform to, when one was asked for.</param>
[System.Diagnostics.DebuggerDisplay("OutputConfig: {ToString(),nq}")]
public sealed record OutputConfig(
    [property: JsonPropertyName("effort")] string? Effort = null,
    [property: JsonPropertyName("format")] OutputFormat? Format = null);
