// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>Controls over a Responses API turn's text output.</summary>
/// <param name="Verbosity">How long the answer should be; <c>low</c>, <c>medium</c>, or <c>high</c>.</param>
/// <param name="Format">The requested output shape.</param>
[System.Diagnostics.DebuggerDisplay("TextControls: {ToString(),nq}")]
public sealed record TextControls(
    [property: JsonPropertyName("verbosity")] string? Verbosity = null,
    [property: JsonPropertyName("format")] CodexTextFormat? Format = null);
