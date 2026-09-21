// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The payload of an <c>image</c> content block.</summary>
/// <param name="Type">How the image is carried; Anthropic clients send <c>base64</c>.</param>
/// <param name="MediaType">The image MIME type, such as <c>image/png</c>.</param>
/// <param name="Data">The base64-encoded image bytes.</param>
[System.Diagnostics.DebuggerDisplay("ImageSource: {ToString(),nq}")]
public sealed record ImageSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("media_type")] string? MediaType = null,
    [property: JsonPropertyName("data")] string? Data = null);
