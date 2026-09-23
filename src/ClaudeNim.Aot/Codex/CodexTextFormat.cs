// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The requested output shape of a Responses API turn's text.</summary>
/// <param name="Type">The format discriminator; the proxy only translates <see cref="JsonSchema"/>.</param>
/// <param name="Name">The schema's name.</param>
/// <param name="Schema">The JSON Schema the output must conform to.</param>
/// <param name="Strict">Whether the model must conform its output to the schema exactly.</param>
[System.Diagnostics.DebuggerDisplay("CodexTextFormat: {ToString(),nq}")]
public sealed record CodexTextFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("schema")] JsonElement? Schema = null,
    [property: JsonPropertyName("strict")] bool? Strict = null)
{
    /// <summary>The discriminator that carries a structured-output schema.</summary>
    internal const string JsonSchema = "json_schema";
}
