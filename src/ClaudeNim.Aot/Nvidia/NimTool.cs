// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One tool offered to the model.</summary>
/// <param name="Function">The function schema.</param>
/// <param name="Type">The tool kind, always <c>function</c>.</param>
[System.Diagnostics.DebuggerDisplay("NimTool: {ToString(),nq}")]
public sealed record NimTool(
    [property: JsonPropertyName("function")] NimFunctionDefinition Function,
    [property: JsonPropertyName("type")] string Type = "function");
