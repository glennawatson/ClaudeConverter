// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Codex;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Turns a completed NVIDIA NIM completion into a Responses API turn.</summary>
/// <remarks>
/// This is the Responses API counterpart of <see cref="NimCompletionTranslator"/> and applies the
/// same rules for recovering reasoning and embedded tool calls; it differs only in the shape the
/// result is assembled into. Where Anthropic nests everything inside one message's content blocks,
/// the Responses API wants a reasoning item, a message item, and one <c>function_call</c> item per
/// tool call as siblings in the turn's <c>output</c> array.
/// </remarks>
public static class CodexCompletionTranslator
{
    /// <summary>The upstream finish reason meaning a filter stopped the turn.</summary>
    private const string ContentFilteredFinishReason = "content_filter";

    /// <summary>The upstream finish reason meaning generation stopped at the output ceiling.</summary>
    private const string LengthFinishReason = "length";

    /// <summary>Translates a completed upstream completion.</summary>
    /// <param name="completion">The upstream completion, which may be absent.</param>
    /// <param name="responseId">The identifier to report for the turn.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="nimModel">The NIM model that produced the completion, which the log names.</param>
    /// <param name="context">Everything else the translation needs.</param>
    /// <returns>The Responses API turn.</returns>
    public static ResponsesResponse Translate(
        NimChatCompletion? completion,
        string responseId,
        string model,
        string nimModel,
        CodexCompletionContext context)
    {
        var choice = FirstChoice(completion);
        var output = ComposeOutput(choice?.Message, choice?.FinishReason, nimModel, context.ThinkingEnabled, context.Logger);

        return new(
            responseId,
            model,
            StatusFor(choice?.FinishReason),
            output,
            CreatedAt: context.Time.GetUtcNow().ToUnixTimeSeconds(),
            OutputText: OutputTextOf(output),
            Usage: BuildUsage(completion?.Usage, context.InputTokens, output),
            IncompleteDetails: IncompleteDetailsFor(choice?.FinishReason));
    }

    /// <summary>Builds the turn's token accounting, estimating whatever the upstream withheld.</summary>
    /// <param name="usage">The upstream's own accounting, which may be absent.</param>
    /// <param name="inputTokens">The prompt size to report when the upstream withheld usage.</param>
    /// <param name="output">The composed output items, used to estimate a withheld output count.</param>
    /// <returns>The turn's token accounting.</returns>
    private static ResponseUsage BuildUsage(NimUsage? usage, int inputTokens, List<ResponseInputItem> output)
    {
        var input = usage?.PromptTokens ?? inputTokens;
        var completion = usage?.CompletionTokens ?? EstimateOutputTokens(output);

        return new(input, completion, input + completion);
    }

    /// <summary>Gets the first choice of a completion.</summary>
    /// <param name="completion">The upstream completion, which may be absent.</param>
    /// <returns>The first choice, or <see langword="null"/> when the completion carried none.</returns>
    private static NimChoice? FirstChoice(NimChatCompletion? completion) =>
        completion?.Choices is { Count: > 0 } choices ? choices[0] : null;

    /// <summary>Maps an upstream finish reason onto the turn's status.</summary>
    /// <param name="finishReason">The upstream finish reason, which may be <see langword="null"/>.</param>
    /// <returns>The status.</returns>
    private static string StatusFor(string? finishReason) =>
        string.Equals(finishReason, LengthFinishReason, StringComparison.Ordinal)
            ? ResponsesResponse.StatusIncomplete
            : ResponsesResponse.StatusCompleted;

    /// <summary>Builds the detail that accompanies an incomplete turn.</summary>
    /// <param name="finishReason">The upstream finish reason, which may be <see langword="null"/>.</param>
    /// <returns>The detail, or <see langword="null"/> for any reason but the output ceiling.</returns>
    private static IncompleteDetails? IncompleteDetailsFor(string? finishReason) =>
        string.Equals(finishReason, LengthFinishReason, StringComparison.Ordinal)
            ? new IncompleteDetails("max_output_tokens")
            : null;

    /// <summary>Concatenates every <c>output_text</c> part across the turn's output items.</summary>
    /// <param name="output">The composed output items.</param>
    /// <returns>The concatenated text, or <see langword="null"/> when the turn produced none.</returns>
    private static string? OutputTextOf(List<ResponseInputItem> output)
    {
        string? text = null;

        for (var i = 0; i < output.Count; i++)
        {
            if (output[i].Content is not { Count: > 0 } parts)
            {
                continue;
            }

            for (var j = 0; j < parts.Count; j++)
            {
                if (!string.Equals(parts[j].Type, ResponseContentTypes.OutputText, StringComparison.Ordinal))
                {
                    continue;
                }

                text = text is null ? parts[j].Text : text + parts[j].Text;
            }
        }

        return text;
    }

    /// <summary>Composes the output items a completed upstream message translates into.</summary>
    /// <param name="message">The completed message, which may be absent.</param>
    /// <param name="finishReason">The upstream finish reason, which may be <see langword="null"/>.</param>
    /// <param name="nimModel">The NIM model that produced the completion.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <returns>The composed items, never empty.</returns>
    private static List<ResponseInputItem> ComposeOutput(
        NimChatMessage? message,
        string? finishReason,
        string nimModel,
        bool thinkingEnabled,
        ILogger logger)
    {
        var composition = new OutputComposition();

        composition.AddReasoning(message?.ReasoningContent, thinkingEnabled);
        composition.AddContent(message?.Content?.Text, nimModel, thinkingEnabled, logger);
        composition.AddToolCalls(message?.ToolCalls);

        var refused = string.Equals(finishReason, ContentFilteredFinishReason, StringComparison.Ordinal);

        return composition.Build(refused);
    }

    /// <summary>Estimates the completion size from the composed output items.</summary>
    /// <param name="output">The items that were composed.</param>
    /// <returns>The estimated output token count.</returns>
    /// <remarks>Only reached when the upstream withheld usage despite being asked for it.</remarks>
    private static int EstimateOutputTokens(List<ResponseInputItem> output)
    {
        var characters = 0;

        for (var i = 0; i < output.Count; i++)
        {
            var item = output[i];
            characters += ContentLength(item.Content);
            characters += ContentLength(item.Summary);
            characters += item.Name?.Length ?? 0;
            characters += item.Arguments?.Length ?? 0;
        }

        return Anthropic.TokenEstimator.FromLength(characters);
    }

    /// <summary>Sums the text length of a list of content parts.</summary>
    /// <param name="parts">The parts to measure, which may be absent.</param>
    /// <returns>The summed length.</returns>
    private static int ContentLength(List<ResponseContentItem>? parts)
    {
        if (parts is not { Count: > 0 })
        {
            return 0;
        }

        var total = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            total += parts[i].Text?.Length ?? 0;
        }

        return total;
    }
}
