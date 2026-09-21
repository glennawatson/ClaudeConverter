// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The <c>content</c> of an Anthropic message, which the wire format allows to be either a bare string or an array of <see cref="ContentBlock"/> values.</summary>
/// <param name="Text">The text form, or <see langword="null"/> when blocks were supplied.</param>
/// <param name="Blocks">The block form, or <see langword="null"/> when text was supplied.</param>
[System.Diagnostics.DebuggerDisplay("MessageContent: {IsText}")]
[JsonConverter(typeof(MessageContentConverter))]
public readonly record struct MessageContent(string? Text, List<ContentBlock>? Blocks)
{
    /// <summary>Gets a value indicating whether this content is the bare-string form.</summary>
    public bool IsText => Text is not null;

    /// <summary>Creates content from a bare string.</summary>
    /// <param name="text">The message text.</param>
    /// <returns>The created content.</returns>
    public static MessageContent FromText(string text) => new(text, null);

    /// <summary>Creates content from a block list.</summary>
    /// <param name="blocks">The message blocks.</param>
    /// <returns>The created content.</returns>
    public static MessageContent FromBlocks(List<ContentBlock> blocks) => new(null, blocks);
}
