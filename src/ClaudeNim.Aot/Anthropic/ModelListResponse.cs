// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The body returned from <c>GET /v1/models</c>.</summary>
/// <param name="Data">The models on this page, in listing order.</param>
/// <param name="HasMore">Whether more models follow this page.</param>
/// <param name="FirstId">The identifier of the first entry, or <see langword="null"/> when the page is empty.</param>
/// <param name="LastId">The identifier of the last entry, or <see langword="null"/> when the page is empty.</param>
/// <remarks>
/// <paramref name="HasMore"/>, <paramref name="FirstId"/> and <paramref name="LastId"/> are the
/// cursor a client pages with. They must describe the page actually returned: a listing that
/// hard-codes <c>has_more</c> to <see langword="false"/> strands every model past the first page,
/// and one that reports an empty string rather than <see langword="null"/> for an absent cursor
/// sends clients back for a page that does not exist.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ModelListResponse: {ToString(),nq}")]
public sealed record ModelListResponse(
    [property: JsonPropertyName("data")] List<ModelDescriptor> Data,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("first_id")] string? FirstId,
    [property: JsonPropertyName("last_id")] string? LastId);
