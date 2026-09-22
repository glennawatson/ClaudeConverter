// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The <c>content</c> of a NIM message, which is either a string or a list of parts.</summary>
/// <param name="Text">The plain form, or <see langword="null"/> when parts were supplied.</param>
/// <param name="Parts">The multimodal form, or <see langword="null"/> when plain text was supplied.</param>
/// <remarks>
/// A text-only turn is sent as a bare string, which is what every model accepts. The list form is
/// used only when a turn actually carries an image, because a model without vision rejects the
/// list outright — so paying the more capable shape by default would break the majority case.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimContent: {IsText}")]
[JsonConverter(typeof(NimContentConverter))]
public readonly record struct NimContent(string? Text, List<NimContentPart>? Parts)
{
    /// <summary>Gets a value indicating whether this content is the bare-string form.</summary>
    public bool IsText => Parts is null;

    /// <summary>Creates content from a bare string.</summary>
    /// <param name="text">The message text.</param>
    /// <returns>The created content.</returns>
    public static NimContent FromText(string text) => new(text, null);

    /// <summary>Creates content from a list of parts.</summary>
    /// <param name="parts">The message parts.</param>
    /// <returns>The created content.</returns>
    public static NimContent FromParts(List<NimContentPart> parts) => new(null, parts);
}
