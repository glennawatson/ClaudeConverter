// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Text;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Serialization;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Accumulates a completed upstream message into the Responses API items it translates into.</summary>
/// <remarks>
/// A NIM completion carries reasoning, answer text, and tool calls as separate fields of one
/// message; the Responses API wants each as its own sibling item in the turn's output array. This
/// type owns folding the three together, including tool calls a model wrote as marker tokens
/// inside its answer text rather than returning structurally.
/// </remarks>
internal sealed class OutputComposition
{
    /// <summary>The content of a turn that produced nothing the client can render.</summary>
    private const string EmptyTurnText = " ";

    /// <summary>The text shown in place of a declined answer with nothing else to say.</summary>
    private const string DeclinedText = "The turn was declined.";

    /// <summary>The reasoning text accumulated so far.</summary>
    private readonly StringBuilder _reasoning = new();

    /// <summary>The answer text accumulated so far.</summary>
    private readonly StringBuilder _text = new();

    /// <summary>The tool calls accumulated so far.</summary>
    private readonly List<(string? Id, string Name, string Arguments)> _calls = [];

    /// <summary>Folds in the reasoning the upstream reported on its own field.</summary>
    /// <param name="reasoning">The reasoning, which may be absent.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    internal void AddReasoning(string? reasoning, bool thinkingEnabled)
    {
        if (thinkingEnabled && reasoning is { Length: > 0 } text)
        {
            AppendReasoning(text);
        }
    }

    /// <summary>Folds in the answer text, separating any reasoning wrapped inside it.</summary>
    /// <param name="content">The answer text, which may be absent.</param>
    /// <param name="nimModel">The NIM model that produced the completion.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    internal void AddContent(string? content, string nimModel, bool thinkingEnabled, ILogger logger)
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
            AppendSegment(segments[i], nimModel, thinkingEnabled, logger);
        }
    }

    /// <summary>Folds in the tool calls the upstream issued structurally.</summary>
    /// <param name="toolCalls">The calls, which may be absent.</param>
    internal void AddToolCalls(List<NimToolCall>? toolCalls)
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

            _calls.Add((call.Id, name, call.Function.Arguments ?? "{}"));
        }
    }

    /// <summary>Builds the composed items.</summary>
    /// <param name="refused">Whether the turn was declined by a safety filter.</param>
    /// <returns>The composed items, never empty.</returns>
    internal List<ResponseInputItem> Build(bool refused)
    {
        List<ResponseInputItem> output = [];

        if (_reasoning.Length > 0)
        {
            output.Add(ResponseInputItem.ForReasoning([ResponseContentItem.ForSummaryText(_reasoning.ToString())])
                with
                { Id = $"rs_{Guid.NewGuid():N}" });
        }

        AppendMessage(output, refused);

        for (var i = 0; i < _calls.Count; i++)
        {
            var call = _calls[i];
            output.Add(ResponseInputItem.ForFunctionCall(call.Id ?? $"call_{Guid.NewGuid():N}", call.Name, call.Arguments)
                with
                { Id = $"fc_{Guid.NewGuid():N}", Status = ResponsesResponse.StatusCompleted });
        }

        if (output.Count == 0)
        {
            output.Add(EmptyMessage());
        }

        return output;
    }

    /// <summary>Builds an assistant message item carrying one content part.</summary>
    /// <param name="part">The part the message carries.</param>
    /// <returns>The message item.</returns>
    private static ResponseInputItem MessageItem(ResponseContentItem part) =>
        ResponseInputItem.ForMessage(ResponseItemTypes.AssistantRole, [part])
            with
            { Id = $"msg_{Guid.NewGuid():N}", Status = ResponsesResponse.StatusCompleted };

    /// <summary>Builds the message item a turn that produced nothing observable still carries.</summary>
    /// <returns>The message item.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ResponseInputItem EmptyMessage() => MessageItem(ResponseContentItem.ForOutputText(EmptyTurnText));

    /// <summary>Appends the answer or refusal message, when there is one.</summary>
    /// <param name="output">The items being composed.</param>
    /// <param name="refused">Whether the turn was declined by a safety filter.</param>
    private void AppendMessage(List<ResponseInputItem> output, bool refused)
    {
        if (refused)
        {
            var declined = _text.Length > 0 ? _text.ToString() : DeclinedText;
            output.Add(MessageItem(ResponseContentItem.ForRefusal(declined)));
            return;
        }

        if (_text.Length > 0)
        {
            output.Add(MessageItem(ResponseContentItem.ForOutputText(_text.ToString())));
        }
    }

    /// <summary>Appends a run of reasoning, separating runs with a blank line.</summary>
    /// <param name="text">The reasoning to append.</param>
    private void AppendReasoning(string text)
    {
        if (_reasoning.Length > 0)
        {
            _ = _reasoning.Append("\n\n");
        }

        _ = _reasoning.Append(text);
    }

    /// <summary>Appends one classified run of output onto the field it belongs to.</summary>
    /// <param name="segment">The run to append.</param>
    /// <param name="nimModel">The NIM model that produced the completion.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <param name="logger">The diagnostic log.</param>
    private void AppendSegment(ThinkTagSegment segment, string nimModel, bool thinkingEnabled, ILogger logger)
    {
        if (!segment.IsThinking)
        {
            AppendTextWithEmbeddedCalls(segment.Text, nimModel, logger);
            return;
        }

        if (thinkingEnabled)
        {
            AppendReasoning(segment.Text);
        }
    }

    /// <summary>Appends a run of answer text, recovering any tool call written into it.</summary>
    /// <param name="text">The answer text.</param>
    /// <param name="nimModel">The NIM model that wrote the call into its answer.</param>
    /// <param name="logger">The diagnostic log.</param>
    private void AppendTextWithEmbeddedCalls(string text, string nimModel, ILogger logger)
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
                _ = _text.Append(run.Payload);
                continue;
            }

            NvidiaLog.EmbeddedToolCallRecovered(logger, nimModel, run.Name);
            var restored = ToolParameterAliases.Restore(JsonElements.ParseOrEmpty(run.Payload));
            _calls.Add((null, run.Name, restored.GetRawText()));
        }
    }
}
