// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The body returned from the NIM <c>GET /v1/models</c> endpoint.</summary>
/// <param name="Data">The models the key can reach.</param>
/// <param name="Object">The object discriminator, always <c>list</c>.</param>
[System.Diagnostics.DebuggerDisplay("NimModelList: {ToString(),nq}")]
public sealed record NimModelList(
    [property: JsonPropertyName("data")] List<NimModel>? Data = null,
    [property: JsonPropertyName("object")] string? Object = null);
