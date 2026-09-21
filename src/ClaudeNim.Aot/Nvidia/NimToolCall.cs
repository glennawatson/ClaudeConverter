// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One tool call in a NIM response or request transcript.</summary>
/// <param name="Index">The call's slot, used to reassemble fragments across streamed chunks.</param>
/// <param name="Id">The call identifier echoed back on the matching tool message.</param>
/// <param name="Function">The function being called.</param>
/// <param name="Type">The call kind, always <c>function</c>.</param>
[System.Diagnostics.DebuggerDisplay("NimToolCall: {ToString(),nq}")]
public sealed record NimToolCall(
    [property: JsonPropertyName("index")] int Index = 0,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("function")] NimFunctionCall? Function = null,
    [property: JsonPropertyName("type")] string Type = "function");
