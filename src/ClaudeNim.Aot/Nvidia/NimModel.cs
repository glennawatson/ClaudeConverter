// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One entry of the NIM <c>GET /v1/models</c> listing.</summary>
/// <param name="Id">The model identifier, such as <c>nvidia/nemotron-3-ultra-550b-a55b</c>.</param>
/// <param name="OwnedBy">The publisher segment of the identifier.</param>
/// <param name="Created">Publication time, as seconds since the Unix epoch.</param>
/// <param name="Object">The object discriminator, always <c>model</c>.</param>
[System.Diagnostics.DebuggerDisplay("NimModel: {ToString(),nq}")]
public sealed record NimModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("owned_by")] string? OwnedBy = null,
    [property: JsonPropertyName("created")] long Created = 0,
    [property: JsonPropertyName("object")] string? Object = null);
