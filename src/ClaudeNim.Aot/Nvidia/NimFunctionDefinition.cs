// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The function schema of a tool offered to the model.</summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">Prose telling the model when the tool applies.</param>
/// <param name="Parameters">The JSON Schema describing the arguments.</param>
[System.Diagnostics.DebuggerDisplay("NimFunctionDefinition: {ToString(),nq}")]
public sealed record NimFunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters);
