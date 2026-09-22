// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The schema an <c>output_config.format</c> asks the answer to conform to.</summary>
/// <param name="Type">The format discriminator, always <c>json_schema</c>.</param>
/// <param name="Schema">The JSON schema of the required output shape.</param>
/// <remarks>
/// This is the Anthropic form of a structured-output request. NVIDIA NIM accepts the same idea
/// through the OpenAI-style <c>response_format</c>, whose shape nests the schema one level deeper;
/// see <see cref="Nvidia.NimRequestBuilder"/> for the translation.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("OutputFormat: {Type}")]
public sealed record OutputFormat(
    [property: JsonPropertyName("type")] string Type = OutputFormat.JsonSchema,
    [property: JsonPropertyName("schema")] JsonElement? Schema = null)
{
    /// <summary>The only documented format discriminator.</summary>
    internal const string JsonSchema = "json_schema";
}
