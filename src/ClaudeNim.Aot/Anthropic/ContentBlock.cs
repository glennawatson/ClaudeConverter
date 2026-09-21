// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>One block of Anthropic message content.</summary>
/// <param name="Type">The block discriminator; see <see cref="ContentBlockTypes"/>.</param>
/// <param name="Text">The text of a <c>text</c> block.</param>
/// <param name="Thinking">The reasoning of a <c>thinking</c> block.</param>
/// <param name="Signature">The provider signature attached to a <c>thinking</c> block.</param>
/// <param name="Data">The opaque payload of a <c>redacted_thinking</c> block.</param>
/// <param name="Id">The identifier of a <c>tool_use</c> block.</param>
/// <param name="Name">The tool name of a <c>tool_use</c> block.</param>
/// <param name="Input">The arguments of a <c>tool_use</c> block.</param>
/// <param name="ToolUseId">The <c>tool_use</c> identifier a <c>tool_result</c> answers.</param>
/// <param name="Content">The payload of a <c>tool_result</c> block, which may be text or blocks.</param>
/// <param name="IsError">Whether a <c>tool_result</c> block reports a failure.</param>
/// <param name="Source">The image payload of an <c>image</c> block.</param>
/// <remarks>
/// Anthropic discriminates blocks on <c>type</c> and gives each variant its own fields. This is
/// modelled as a single record with optional members rather than a type hierarchy: the proxy
/// only ever reads a handful of fields, and a flat shape keeps serialization free of the
/// polymorphic resolver, which native AOT cannot generate.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ContentBlock: {ToString(),nq}")]
public sealed record ContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("thinking")] string? Thinking = null,
    [property: JsonPropertyName("signature")] string? Signature = null,
    [property: JsonPropertyName("data")] string? Data = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("input")] JsonElement? Input = null,
    [property: JsonPropertyName("tool_use_id")] string? ToolUseId = null,
    [property: JsonPropertyName("content")] JsonElement? Content = null,
    [property: JsonPropertyName("is_error")] bool? IsError = null,
    [property: JsonPropertyName("source")] ImageSource? Source = null)
{
    /// <summary>Creates a <c>text</c> block.</summary>
    /// <param name="text">The block text.</param>
    /// <returns>The created block.</returns>
    public static ContentBlock ForText(string text) => new(ContentBlockTypes.Text, Text: text);
}
