// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic;

/// <summary>Estimates the prompt size of an Anthropic request.</summary>
/// <remarks>
/// <para>
/// The count is an approximation. An exact figure needs the model's own byte-pair vocabulary,
/// and neither Anthropic's tokenizer nor the vocabulary of whichever NIM model is serving the
/// request is available to the proxy; shipping one would also cost a multi-megabyte table that
/// native AOT would have to carry.
/// </para>
/// <para>
/// The approximation is four characters per token plus the per-block overheads Anthropic
/// charges for structure. Clients use this figure for progress display and context-budget
/// warnings rather than billing, so a few percent of drift is tolerable. When the upstream
/// reports real prompt tokens they are preferred over this estimate.
/// </para>
/// </remarks>
public static class TokenEstimator
{
    /// <summary>The assumed average number of characters a single token covers.</summary>
    private const int CharactersPerToken = 4;

    /// <summary>The token overhead for each message in the conversation.</summary>
    private const int PerMessageOverhead = 4;

    /// <summary>The token overhead for each tool definition.</summary>
    private const int PerToolOverhead = 5;

    /// <summary>The token overhead for each tool use block.</summary>
    private const int ToolUseOverhead = 15;

    /// <summary>The token overhead for each tool result block.</summary>
    private const int ToolResultOverhead = 8;

    /// <summary>The token overhead for the system prompt.</summary>
    private const int SystemPromptOverhead = 4;

    /// <summary>The default number of tokens an image consumes.</summary>
    private const int ImageTokens = 765;

    /// <summary>The minimum number of tokens an image may consume.</summary>
    private const int MinimumImageTokens = 85;

    /// <summary>The assumed number of bytes in an image per token.</summary>
    private const int ImageBytesPerToken = 3000;

    /// <summary>Estimates how many input tokens a request consumes.</summary>
    /// <param name="messages">The conversation to measure.</param>
    /// <param name="system">The system prompt to measure.</param>
    /// <param name="tools">The tool definitions to measure.</param>
    /// <returns>The estimated token count, never below one.</returns>
    public static int Estimate(
        List<AnthropicMessage> messages,
        MessageContent? system,
        List<ToolDefinition>? tools)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var total = 0;

        if (system.HasValue)
        {
            total += FromText(ContentText.Extract(system.Value)) + SystemPromptOverhead;
        }

        for (var i = 0; i < messages.Count; i++)
        {
            total += EstimateMessage(messages[i]) + PerMessageOverhead;
        }

        if (tools is not null)
        {
            for (var i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                total += FromText(tool.Name);
                total += FromText(tool.Description);
                total += tool.InputSchema is { } schema ? FromText(schema.GetRawText()) : 0;
                total += PerToolOverhead;
            }
        }

        return total < 1 ? 1 : total;
    }

    /// <summary>Estimates how many tokens a run of text occupies.</summary>
    /// <param name="text">The text to measure, which may be absent.</param>
    /// <returns>The estimated token count.</returns>
    public static int FromText(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : FromLength(text.Length);

    /// <summary>Estimates how many tokens a given number of characters occupies.</summary>
    /// <param name="characterCount">The number of characters.</param>
    /// <returns>The estimated token count.</returns>
    public static int FromLength(int characterCount) =>
        characterCount <= 0 ? 0 : (characterCount + CharactersPerToken - 1) / CharactersPerToken;

    /// <summary>Estimates the tokens a single message occupies.</summary>
    /// <param name="message">The message to measure.</param>
    /// <returns>The estimated token count.</returns>
    private static int EstimateMessage(AnthropicMessage message)
    {
        var content = message.Content;
        if (content.Text is not null)
        {
            return FromText(content.Text);
        }

        var blocks = content.Blocks;
        if (blocks is null)
        {
            return 0;
        }

        var total = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            total += EstimateBlock(blocks[i]);
        }

        return total;
    }

    /// <summary>Estimates the tokens a single content block occupies.</summary>
    /// <param name="block">The block to measure.</param>
    /// <returns>The estimated token count.</returns>
    private static int EstimateBlock(ContentBlock block) => block.Type switch
    {
        ContentBlockTypes.Text => FromText(block.Text),
        ContentBlockTypes.Thinking => FromText(block.Thinking),
        ContentBlockTypes.RedactedThinking => FromText(block.Data),
        ContentBlockTypes.ToolUse => EstimateToolUse(block),
        ContentBlockTypes.ToolResult => EstimateToolResult(block),
        ContentBlockTypes.Image => EstimateImage(block.Source),
        _ => 0,
    };

    /// <summary>Estimates the tokens a tool use block occupies.</summary>
    /// <param name="block">The block to measure.</param>
    /// <returns>The estimated token count.</returns>
    private static int EstimateToolUse(ContentBlock block) =>
        FromText(block.Name)
        + FromText(block.Id)
        + (block.Input is { } input ? FromText(input.GetRawText()) : 0)
        + ToolUseOverhead;

    /// <summary>Estimates the tokens a tool result block occupies.</summary>
    /// <param name="block">The block to measure.</param>
    /// <returns>The estimated token count.</returns>
    private static int EstimateToolResult(ContentBlock block) =>
        FromText(ContentText.FromToolResult(block.Content))
        + FromText(block.ToolUseId)
        + ToolResultOverhead;

    /// <summary>Estimates the tokens an image occupies.</summary>
    /// <param name="source">The image source to measure.</param>
    /// <returns>The estimated token count.</returns>
    private static int EstimateImage(ImageSource? source)
    {
        if (source?.Data is not { Length: > 0 } data)
        {
            return ImageTokens;
        }

        var scaled = data.Length / ImageBytesPerToken;
        return scaled < MinimumImageTokens ? MinimumImageTokens : scaled;
    }
}
