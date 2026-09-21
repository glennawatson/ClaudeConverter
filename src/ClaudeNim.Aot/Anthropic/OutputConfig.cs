// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The caller's output shaping request, carrying the effort level introduced with Claude 4.7.</summary>
/// <param name="Effort">How hard the model should work; see <see cref="EffortLevels"/>.</param>
[System.Diagnostics.DebuggerDisplay("OutputConfig: {ToString(),nq}")]
public sealed record OutputConfig(
    [property: JsonPropertyName("effort")] string? Effort = null);
