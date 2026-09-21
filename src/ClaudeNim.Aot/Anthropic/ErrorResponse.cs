// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The error body Anthropic clients expect on a failed call.</summary>
/// <param name="Error">The failure detail.</param>
/// <param name="Type">The object discriminator, always <c>error</c>.</param>
[System.Diagnostics.DebuggerDisplay("ErrorResponse: {ToString(),nq}")]
public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] ErrorDetail Error,
    [property: JsonPropertyName("type")] string Type = "error")
{
    /// <summary>Creates an error body.</summary>
    /// <param name="type">The error class.</param>
    /// <param name="message">Prose describing the failure.</param>
    /// <returns>The created body.</returns>
    public static ErrorResponse Create(string type, string message) => new(new ErrorDetail(type, message));
}
