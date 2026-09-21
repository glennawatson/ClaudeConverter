// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>A failure NVIDIA NIM reports inside an already-successful streamed response.</summary>
/// <param name="Message">Prose describing the failure.</param>
/// <param name="Type">The upstream's own classification of the failure.</param>
/// <param name="Code">The status the failure would have carried had it arrived as one.</param>
/// <remarks>
/// A streamed turn's status code is committed before generation begins, so a failure that happens
/// afterwards arrives as an <c>error</c> payload on the event stream of a 200 response. A proxy
/// that only inspects the status code sees success and forwards an empty turn, which a client
/// reports as the model having nothing to say. Reading this member is what turns that into an
/// error the client can act on.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimStreamError: {ToString(),nq}")]
public sealed record NimStreamError(
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("code")] int? Code = null);
