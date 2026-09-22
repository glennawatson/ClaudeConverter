// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The nested schema body of a <c>response_format</c>.</summary>
/// <param name="Schema">The JSON schema of the required output shape.</param>
[System.Diagnostics.DebuggerDisplay("ResponseFormatBody: {ToString(),nq}")]
public sealed record ResponseFormatBody(
    [property: JsonPropertyName("schema")] JsonElement Schema);
