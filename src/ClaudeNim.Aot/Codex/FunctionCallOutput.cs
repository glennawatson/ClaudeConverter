// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The <c>output</c> of a <c>function_call_output</c> item, sent as a bare string or a bare array.</summary>
/// <param name="Text">The text form, or <see langword="null"/> when parts were supplied.</param>
/// <param name="Items">The part form, or <see langword="null"/> when text was supplied.</param>
/// <remarks>
/// Codex encodes this with no wrapper object around either shape, matching how
/// <see cref="Anthropic.MessageContent"/> encodes a dual-shape field — a string when the caller
/// sent plain text, an array when it sent structured parts.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("FunctionCallOutput: {IsText}")]
[JsonConverter(typeof(FunctionCallOutputConverter))]
public readonly record struct FunctionCallOutput(string? Text, List<ResponseContentItem>? Items)
{
    /// <summary>Gets a value indicating whether this output is the bare-string form.</summary>
    public bool IsText => Items is null;

    /// <summary>Creates output from a bare string.</summary>
    /// <param name="text">The tool result text.</param>
    /// <returns>The created output.</returns>
    public static FunctionCallOutput FromText(string text) => new(text, null);

    /// <summary>Creates output from a part list.</summary>
    /// <param name="items">The tool result parts.</param>
    /// <returns>The created output.</returns>
    public static FunctionCallOutput FromItems(List<ResponseContentItem> items) => new(null, items);
}
