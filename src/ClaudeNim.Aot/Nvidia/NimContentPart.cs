// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One part of a multimodal NIM message.</summary>
/// <param name="Type">The part discriminator, either <c>text</c> or <c>image_url</c>.</param>
/// <param name="Text">The text of a <c>text</c> part.</param>
/// <param name="ImageUrl">The image of an <c>image_url</c> part.</param>
[System.Diagnostics.DebuggerDisplay("NimContentPart: {Type}")]
public sealed record NimContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("image_url")] NimImageUrl? ImageUrl = null)
{
    /// <summary>The discriminator of a text part.</summary>
    internal const string TextType = "text";

    /// <summary>The discriminator of an image part.</summary>
    internal const string ImageType = "image_url";

    /// <summary>Creates a text part.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The created part.</returns>
    public static NimContentPart ForText(string text) => new(TextType, Text: text);

    /// <summary>Creates an image part.</summary>
    /// <param name="url">The image, as a <c>data:</c> URL or an ordinary one.</param>
    /// <returns>The created part.</returns>
    public static NimContentPart ForImage(string url) => new(ImageType, ImageUrl: new NimImageUrl(url));
}
