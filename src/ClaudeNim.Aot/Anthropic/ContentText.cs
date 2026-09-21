// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using System.Text.Json;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Flattens Anthropic content into the plain text the local fast paths match against.</summary>
public static class ContentText
{
    /// <summary>Extracts the readable text of a content value.</summary>
    /// <param name="content">The content to flatten.</param>
    /// <returns>The concatenated text, with an empty string when there is none.</returns>
    public static string Extract(MessageContent content)
    {
        if (content.Text is not null)
        {
            return content.Text;
        }

        var blocks = content.Blocks;
        if (blocks is null || blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            AppendBlock(builder, blocks[i]);
        }

        return builder.ToString();
    }

    /// <summary>Extracts the readable text of an optional content value.</summary>
    /// <param name="content">The content to flatten, which may be absent.</param>
    /// <returns>The concatenated text, with an empty string when there is none.</returns>
    public static string Extract(MessageContent? content) =>
        content.HasValue ? Extract(content.Value) : string.Empty;

    /// <summary>Renders the payload of a <c>tool_result</c> block as the text an upstream tool message carries.</summary>
    /// <param name="content">The raw <c>content</c> member of the block.</param>
    /// <returns>The rendered text.</returns>
    public static string FromToolResult(JsonElement? content) => content switch
    {
        { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        { ValueKind: JsonValueKind.Array } element => RenderToolResultArray(element),
        { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element => element.GetRawText(),
        _ => string.Empty,
    };

    /// <summary>Renders a tool result array as a plain text string.</summary>
    /// <param name="element">The array element to render.</param>
    /// <returns>The concatenated text representation of the array elements.</returns>
    private static string RenderToolResultArray(JsonElement element)
    {
        var builder = new StringBuilder();
        foreach (var item in element.EnumerateArray())
        {
            if (builder.Length > 0)
            {
                _ = builder.Append('\n');
            }

            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var type)
                && type.ValueEquals(ContentBlockTypes.Text)
                && item.TryGetProperty("text", out var text))
            {
                _ = builder.Append(text.GetString());
                continue;
            }

            _ = builder.Append(item.GetRawText());
        }

        return builder.ToString();
    }

    /// <summary>Appends the text of a content block to a string builder.</summary>
    /// <param name="builder">The builder to append to.</param>
    /// <param name="block">The block whose text is to be appended.</param>
    private static void AppendBlock(StringBuilder builder, ContentBlock block)
    {
        var text = block.Type switch
        {
            ContentBlockTypes.Text => block.Text,
            ContentBlockTypes.Thinking => block.Thinking,
            ContentBlockTypes.ToolResult => FromToolResult(block.Content),
            ContentBlockTypes.Image => "[Image]",
            _ => null,
        };

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (builder.Length > 0)
        {
            _ = builder.Append('\n');
        }

        _ = builder.Append(text);
    }
}
