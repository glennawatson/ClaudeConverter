// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The error carried by a failed Responses API turn.</summary>
/// <param name="Message">Prose describing the failure.</param>
/// <param name="Type">The error class.</param>
/// <param name="Code">A machine-readable failure code.</param>
[System.Diagnostics.DebuggerDisplay("ResponseError: {Message}")]
public sealed record ResponseError(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("code")] string? Code = null);
