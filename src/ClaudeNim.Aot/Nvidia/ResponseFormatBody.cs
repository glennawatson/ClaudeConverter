// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The nested schema body of a <c>response_format</c>.</summary>
/// <param name="Schema">The JSON schema of the required output shape.</param>
/// <param name="Name">The label the schema is registered under.</param>
/// <remarks>
/// <c>name</c> is not optional upstream. Anthropic's <c>output_config.format</c> carries no such
/// label, so a proxy that translates the schema alone sends a body the OpenAI shape considers
/// incomplete, and NIM rejects the whole turn with <c>missing field `name`</c> before the model is
/// ever reached. A constant satisfies it: the label names the schema, not the request, and nothing
/// downstream reads it back.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ResponseFormatBody: {Name}")]
public sealed record ResponseFormatBody(
    [property: JsonPropertyName("schema")] JsonElement Schema,
    [property: JsonPropertyName("name")] string Name = ResponseFormatBody.DefaultName)
{
    /// <summary>The label used when the caller supplies no name of its own.</summary>
    /// <remarks>
    /// Upstream constrains this to letters, digits, underscores and dashes.
    /// </remarks>
    internal const string DefaultName = "response";
}
