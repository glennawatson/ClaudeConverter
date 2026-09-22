// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The image carried by an OpenAI-style content part.</summary>
/// <param name="Url">The image, as a <c>data:</c> URL or an ordinary one.</param>
[System.Diagnostics.DebuggerDisplay("NimImageUrl: {ToString(),nq}")]
public sealed record NimImageUrl(
    [property: JsonPropertyName("url")] string Url);
