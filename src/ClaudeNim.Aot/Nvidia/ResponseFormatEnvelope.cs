// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The OpenAI-style <c>response_format</c> NIM accepts for a structured-output request.</summary>
/// <param name="Type">The format discriminator, always <c>json_schema</c>.</param>
/// <param name="JsonSchema">The nested schema body.</param>
/// <remarks>
/// NIM follows the OpenAI shape, which nests the schema under a <c>json_schema</c> member rather
/// than carrying it directly as Anthropic's <c>output_config.format</c> does.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ResponseFormatEnvelope: {Type}")]
public sealed record ResponseFormatEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")] ResponseFormatBody JsonSchema);
