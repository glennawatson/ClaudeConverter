// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Codex.Streaming;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Converts subscription responses to the shared upstream format.</summary>
internal static class ChatGptResponseTranslator
{
    /// <summary>Converts a terminal response.</summary>
    /// <param name="response">The completed response.</param>
    /// <returns>The shared completion.</returns>
    internal static NimChatCompletion Complete(ResponsesResponse response)
    {
        var text = new StringBuilder();
        var calls = new List<NimToolCall>();
        foreach (var item in response.Output)
        {
            if (item.Type == "function_call")
            {
                calls.Add(new(calls.Count, item.CallId, new(item.Name, item.Arguments)));
            }
            else if (item.Type == "message" && item.Content is { } parts)
            {
                foreach (var part in parts)
                {
                    _ = text.Append(part.Text);
                }
            }
        }

        var reason = FinishReason(response.Status, calls.Count > 0);
        return new(response.Id, response.Model, [new(Message: new("assistant", NimContent.FromText(text.ToString()), calls.Count > 0 ? calls : null), FinishReason: reason)], Usage(response.Usage));
    }

    /// <summary>Converts one streamed response event.</summary>
    /// <param name="eventData">The upstream event.</param>
    /// <param name="toolSlots">Tool slots indexed by output position.</param>
    /// <returns>A shared chunk, or null for an event with no visible output.</returns>
    internal static NimChatCompletionChunk? Chunk(ResponseStreamEvent eventData, Dictionary<int, int> toolSlots)
    {
        switch (eventData.Type)
        {
            case "response.output_text.delta":
                return new(Choices: [new(Delta: new(Content: eventData.Delta))]);
            case "response.reasoning_summary_text.delta":
                return new(Choices: [new(Delta: new(ReasoningContent: eventData.Delta))]);
            case "response.output_item.added" when eventData.Item?.Type == "function_call":
            {
                var slot = toolSlots.Count;
                toolSlots.Add(eventData.OutputIndex ?? slot, slot);
                return new(Choices: [new(Delta: new(ToolCalls: [new(slot, eventData.Item.CallId, new(eventData.Item.Name, string.Empty))]))]);
            }

            case "response.function_call_arguments.delta":
            {
                var callSlot = toolSlots[eventData.OutputIndex ?? 0];
                return new(Choices: [new(Delta: new(ToolCalls: [new(callSlot, Function: new(Arguments: eventData.Delta))]))]);
            }

            case "response.completed" or "response.incomplete" when eventData.Response is { } terminal:
            {
                var finish = FinishReason(terminal.Status, toolSlots.Count > 0);
                return new(terminal.Id, terminal.Model, [new(FinishReason: finish)], Usage(terminal.Usage));
            }

            case "response.failed" or "error":
                return new(Error: new(eventData.Response?.Error?.Message ?? "The subscription request failed."));
            default:
                return null;
        }
    }

    /// <summary>Resolves the shared finish reason.</summary>
    /// <param name="status">The upstream response status.</param>
    /// <param name="hasTools">Whether the response produced tools.</param>
    /// <returns>The shared finish reason.</returns>
    private static string FinishReason(string status, bool hasTools)
    {
        if (status == "incomplete")
        {
            return "length";
        }

        return hasTools ? "tool_calls" : "stop";
    }

    /// <summary>Converts token accounting.</summary>
    /// <param name="usage">The upstream token counts.</param>
    /// <returns>The shared token counts.</returns>
    private static NimUsage? Usage(ResponseUsage? usage) => usage is { } counts
        ? new(counts.InputTokens, counts.OutputTokens, counts.TotalTokens)
        : null;
}
