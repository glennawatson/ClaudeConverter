// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>A client-supplied tool the model may call.</summary>
/// <param name="Type">The tool kind; the proxy only ever sees <see cref="FunctionType"/>.</param>
/// <param name="Name">The tool name the model uses to invoke it.</param>
/// <param name="Description">Prose telling the model when the tool applies.</param>
/// <param name="Parameters">The JSON Schema describing the tool's arguments.</param>
/// <param name="Strict">Whether the model must conform its call to the schema exactly.</param>
[System.Diagnostics.DebuggerDisplay("CodexTool: {ToString(),nq}")]
public sealed record CodexTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters = null,
    [property: JsonPropertyName("strict")] bool? Strict = null)
{
    /// <summary>The only tool kind the Responses API function-calling surface defines.</summary>
    internal const string FunctionType = "function";
}
