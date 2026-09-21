// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Options that change what a streamed NIM response carries.</summary>
/// <param name="IncludeUsage">Whether a final chunk carrying token counts is emitted.</param>
/// <remarks>
/// Without <paramref name="IncludeUsage"/> a streamed completion never reports token counts, and
/// a proxy is left guessing them from response length. Asking for usage up front is what lets the
/// Anthropic <c>message_delta</c> event carry the real figure.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimStreamOptions: {ToString(),nq}")]
public readonly record struct NimStreamOptions(
    [property: JsonPropertyName("include_usage")] bool IncludeUsage);
