// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The function half of an OpenAI-style tool call.</summary>
/// <param name="Name">The tool name, which streams in fragments.</param>
/// <param name="Arguments">The JSON arguments, which stream in fragments.</param>
[System.Diagnostics.DebuggerDisplay("NimFunctionCall: {ToString(),nq}")]
public sealed record NimFunctionCall(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("arguments")] string? Arguments = null);
