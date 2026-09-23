// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>One part of a message, reasoning item, or function-call output.</summary>
/// <param name="Type">The part discriminator; see <see cref="ResponseContentTypes"/>.</param>
/// <param name="Text">The text of a text-shaped part.</param>
/// <param name="ImageUrl">The image of an <c>input_image</c> part, as a URL or a <c>data:</c> URI.</param>
/// <param name="Detail">The requested inspection detail of an <c>input_image</c> part.</param>
/// <remarks>
/// Modelled as a single record with optional members rather than a type hierarchy, matching
/// <see cref="Anthropic.ContentBlock"/> for the same reason: native AOT cannot generate the
/// polymorphic resolver a type hierarchy would need.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ResponseContentItem: {ToString(),nq}")]
public sealed record ResponseContentItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("image_url")] string? ImageUrl = null,
    [property: JsonPropertyName("detail")] string? Detail = null)
{
    /// <summary>Creates an <c>input_text</c> part.</summary>
    /// <param name="text">The part text.</param>
    /// <returns>The created part.</returns>
    public static ResponseContentItem ForInputText(string text) => new(ResponseContentTypes.InputText, Text: text);

    /// <summary>Creates an <c>output_text</c> part.</summary>
    /// <param name="text">The part text.</param>
    /// <returns>The created part.</returns>
    public static ResponseContentItem ForOutputText(string text) => new(ResponseContentTypes.OutputText, Text: text);

    /// <summary>Creates a <c>refusal</c> part.</summary>
    /// <param name="text">The declined-answer text.</param>
    /// <returns>The created part.</returns>
    public static ResponseContentItem ForRefusal(string text) => new(ResponseContentTypes.Refusal, Text: text);

    /// <summary>Creates a <c>summary_text</c> part.</summary>
    /// <param name="text">The summary text.</param>
    /// <returns>The created part.</returns>
    public static ResponseContentItem ForSummaryText(string text) => new(ResponseContentTypes.SummaryText, Text: text);

    /// <summary>Creates a <c>reasoning_text</c> part.</summary>
    /// <param name="text">The reasoning text.</param>
    /// <returns>The created part.</returns>
    public static ResponseContentItem ForReasoningText(string text) => new(ResponseContentTypes.ReasoningText, Text: text);
}
