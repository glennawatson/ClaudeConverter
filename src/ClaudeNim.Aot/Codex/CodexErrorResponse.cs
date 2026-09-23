// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The error body a Responses API client expects on a failed call.</summary>
/// <param name="Error">The failure detail.</param>
[System.Diagnostics.DebuggerDisplay("CodexErrorResponse: {Error.Message}")]
public sealed record CodexErrorResponse([property: JsonPropertyName("error")] ResponseError Error)
{
    /// <summary>Creates an error body.</summary>
    /// <param name="type">The error class.</param>
    /// <param name="message">Prose describing the failure.</param>
    /// <returns>The created body.</returns>
    public static CodexErrorResponse Create(string type, string message) => new(new ResponseError(message, type));
}
