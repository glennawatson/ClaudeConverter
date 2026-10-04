// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers subscription tool calls and terminal events.</summary>
public sealed class ChatGptResponseTranslatorTests
{
    /// <summary>Tool arguments retain their slot when other output items precede them.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task ChunkPreservesToolCallSlots()
    {
        const int outputIndex = 2;
        var slots = new Dictionary<int, int>();
        var item = ResponseInputItem.ForFunctionCall("call-1", "read", string.Empty);
        var added = ChatGptResponseTranslator.Chunk(new("response.output_item.added", Item: item, OutputIndex: outputIndex), slots);
        var arguments = ChatGptResponseTranslator.Chunk(new("response.function_call_arguments.delta", OutputIndex: outputIndex, Delta: "{}"), slots);

        await Assert.That(added!.Choices![0].Delta!.ToolCalls![0].Id).IsEqualTo("call-1");
        await Assert.That(added.Choices[0].Delta!.ToolCalls![0].Function!.Name).IsEqualTo("read");
        await Assert.That(arguments!.Choices![0].Delta!.ToolCalls![0].Index).IsEqualTo(0);
        await Assert.That(arguments.Choices[0].Delta!.ToolCalls![0].Function!.Arguments).IsEqualTo("{}");
    }

    /// <summary>An incomplete upstream response preserves its finish reason and usage.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task CompletePreservesIncompleteStatusAndUsage()
    {
        const int totalTokens = 2;
        var output = ResponseInputItem.ForMessage("assistant", [ResponseContentItem.ForOutputText("partial")]);
        var response = new ResponsesResponse("response-1", "gpt-test", "incomplete", [output], Usage: new(1, 1, totalTokens));
        var completion = ChatGptResponseTranslator.Complete(response);

        await Assert.That(completion.Choices![0].FinishReason).IsEqualTo("length");
        await Assert.That(completion.Choices[0].Message!.Content!.Value.Text).IsEqualTo("partial");
        await Assert.That(completion.Usage!.Value.TotalTokens).IsEqualTo(response.Usage!.Value.TotalTokens);
    }

    /// <summary>A failed event remains a stream error.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task ChunkPreservesFailure()
    {
        var response = new ResponsesResponse("response-1", "gpt-test", "failed", [], Error: new("declined", "failure"));
        var chunk = ChatGptResponseTranslator.Chunk(new("response.failed", Response: response), []);

        await Assert.That(chunk!.Error!.Message).IsEqualTo("declined");
    }
}
