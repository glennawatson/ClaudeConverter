// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The inner object of an Anthropic error body.</summary>
/// <param name="Type">The error class, such as <c>api_error</c> or <c>authentication_error</c>.</param>
/// <param name="Message">Prose describing the failure.</param>
[System.Diagnostics.DebuggerDisplay("ErrorDetail: {ToString(),nq}")]
public sealed record ErrorDetail(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] string Message);
