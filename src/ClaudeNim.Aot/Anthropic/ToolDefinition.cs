// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>A client-supplied tool the model may call.</summary>
/// <param name="Name">The tool name the model uses to invoke it.</param>
/// <param name="Description">Prose telling the model when the tool applies.</param>
/// <param name="InputSchema">The JSON Schema describing the tool's arguments.</param>
[System.Diagnostics.DebuggerDisplay("ToolDefinition: {ToString(),nq}")]
public sealed record ToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("input_schema")] JsonElement? InputSchema = null);
