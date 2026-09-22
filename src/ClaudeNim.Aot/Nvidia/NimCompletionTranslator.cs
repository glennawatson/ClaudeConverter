// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Serialization;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Turns a completed NVIDIA NIM completion into an Anthropic message.</summary>
/// <remarks>
/// <para>
/// This is the non-streamed counterpart of <see cref="NimStreamTranslator"/> and applies the same
/// rules: reasoning becomes a <c>thinking</c> block rather than being dropped, inline
/// <c>&lt;think&gt;</c> tags are lifted out of the answer text, and tool calls become
/// <c>tool_use</c> blocks with parsed arguments.
/// </para>
/// <para>
/// Anthropic rejects a message with no content, so a turn that produced nothing observable still
/// yields a single empty text block.
/// </para>
/// </remarks>
public static class NimCompletionTranslator
{
    /// <summary>The content of a turn that produced nothing the client can render.</summary>
    private const string EmptyTurnText = " ";

    /// <summary>Translates a completed upstream completion.</summary>
    /// <param name="completion">The upstream completion, which may be absent.</param>
    /// <param name="messageId">The identifier to report for the Anthropic message.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="inputTokens">The prompt size to report when the upstream withheld usage.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <returns>The Anthropic message.</returns>
    public static MessagesResponse Translate(
        NimChatCompletion? completion,
        string messageId,
        string model,
        int inputTokens,
        bool thinkingEnabled,
        ILogger logger)
    {
        var choice = FirstChoice(completion);
        var blocks = ComposeBlocks(choice?.Message, thinkingEnabled, logger);
        var usage = completion?.Usage;

        return new(
            messageId,
            model,
            blocks,
            StopReasons.FromFinishReason(choice?.FinishReason),
            new TokenUsage(
                usage?.PromptTokens ?? inputTokens,
                usage?.CompletionTokens ?? EstimateOutputTokens(blocks)));
    }

    /// <summary>Composes the content blocks a completed upstream message translates into.</summary>
    /// <param name="message">The completed message, which may be absent.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <returns>The composed blocks, never empty.</returns>
    private static List<ContentBlock> ComposeBlocks(NimChatMessage? message, bool thinkingEnabled, ILogger logger)
    {
        var blocks = new List<ContentBlock>();

        AppendReasoning(blocks, message?.ReasoningContent, thinkingEnabled);
        AppendContent(blocks, message?.Content?.Text, thinkingEnabled, logger);
        AppendToolCalls(blocks, message?.ToolCalls);

        if (blocks.Count == 0)
        {
            blocks.Add(ContentBlock.ForText(EmptyTurnText));
        }

        return blocks;
    }

    /// <summary>Gets the first choice of a completion.</summary>
    /// <param name="completion">The upstream completion, which may be absent.</param>
    /// <returns>The first choice, or <see langword="null"/> when the completion carried none.</returns>
    private static NimChoice? FirstChoice(NimChatCompletion? completion) =>
        completion?.Choices is { Count: > 0 } choices ? choices[0] : null;

    /// <summary>Appends the reasoning the upstream reported on its own field.</summary>
    /// <param name="blocks">The blocks being composed.</param>
    /// <param name="reasoning">The reasoning, which may be absent.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    private static void AppendReasoning(List<ContentBlock> blocks, string? reasoning, bool thinkingEnabled)
    {
        if (thinkingEnabled && reasoning is { Length: > 0 } text)
        {
            blocks.Add(new(ContentBlockTypes.Thinking, Thinking: text));
        }
    }

    /// <summary>Appends the answer text, separating any reasoning wrapped inside it.</summary>
    /// <param name="blocks">The blocks being composed.</param>
    /// <param name="content">The answer text, which may be absent.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    private static void AppendContent(List<ContentBlock> blocks, string? content, bool thinkingEnabled, ILogger logger)
    {
        if (content is not { Length: > 0 } text)
        {
            return;
        }

        var parser = new ThinkTagParser();
        var segments = new List<ThinkTagSegment>();
        parser.Feed(text, segments);
        parser.Flush(segments);

        for (var i = 0; i < segments.Count; i++)
        {
            AppendSegment(blocks, segments[i], thinkingEnabled, logger);
        }
    }

    /// <summary>Appends a run of answer text, recovering any tool call written into it.</summary>
    /// <param name="blocks">The blocks being composed.</param>
    /// <param name="text">The answer text.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <remarks>
    /// Models that render a tool call as marker tokens inside their answer would otherwise have
    /// the whole call shown to the client as literal text and never executed.
    /// </remarks>
    private static void AppendText(List<ContentBlock> blocks, string text, ILogger logger)
    {
        var runs = new List<EmbeddedToolCall>();
        var parser = new EmbeddedToolCallParser();
        parser.Feed(text, runs);
        parser.Flush(runs);

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (!run.IsCall)
            {
                blocks.Add(ContentBlock.ForText(run.Payload));
                continue;
            }

            NvidiaLog.EmbeddedToolCallRecovered(logger, run.Name);

            blocks.Add(new(
                ContentBlockTypes.ToolUse,
                Id: $"toolu_{Guid.NewGuid():N}",
                Name: run.Name,
                Input: ToolParameterAliases.Restore(JsonElements.ParseOrEmpty(run.Payload))));
        }
    }

    /// <summary>Appends one classified run of output onto the block it belongs to.</summary>
    /// <param name="blocks">The blocks being composed.</param>
    /// <param name="segment">The run to append.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    private static void AppendSegment(
        List<ContentBlock> blocks,
        ThinkTagSegment segment,
        bool thinkingEnabled,
        ILogger logger)
    {
        if (!segment.IsThinking)
        {
            AppendText(blocks, segment.Text, logger);
            return;
        }

        if (thinkingEnabled)
        {
            blocks.Add(new(ContentBlockTypes.Thinking) { Thinking = segment.Text });
        }
    }

    /// <summary>Appends the tool calls the upstream issued.</summary>
    /// <param name="blocks">The blocks being composed.</param>
    /// <param name="toolCalls">The calls, which may be absent.</param>
    private static void AppendToolCalls(List<ContentBlock> blocks, List<NimToolCall>? toolCalls)
    {
        if (toolCalls is not { Count: > 0 })
        {
            return;
        }

        for (var i = 0; i < toolCalls.Count; i++)
        {
            var call = toolCalls[i];
            if (call.Function?.Name is not { Length: > 0 } name)
            {
                continue;
            }

            blocks.Add(new(
                ContentBlockTypes.ToolUse,
                Id: call.Id ?? $"toolu_{Guid.NewGuid():N}",
                Name: name,
                Input: ToolParameterAliases.Restore(JsonElements.ParseOrEmpty(call.Function.Arguments))));
        }
    }

    /// <summary>Estimates the completion size from the composed blocks.</summary>
    /// <param name="blocks">The blocks that were composed.</param>
    /// <returns>The estimated output token count.</returns>
    /// <remarks>Only reached when the upstream withheld usage despite being asked for it.</remarks>
    private static int EstimateOutputTokens(List<ContentBlock> blocks)
    {
        var characters = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            characters += block.Text?.Length ?? 0;
            characters += block.Thinking?.Length ?? 0;
            characters += block.Name?.Length ?? 0;
            characters += block.Input is { } input ? input.GetRawText().Length : 0;
        }

        return TokenEstimator.FromLength(characters);
    }
}
