// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the subscription request contract.</summary>
public sealed class ChatGptRequestBuilderTests
{
    /// <summary>Subscription requests stream without server storage.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task BuildForcesStreamingWithoutStorage()
    {
        var request = new NimChatRequest("gpt-test", [], 1, Stream: false);
        var body = ChatGptRequestBuilder.Build(request);

        await Assert.That(body.GetProperty("stream").GetBoolean()).IsTrue();
        await Assert.That(body.GetProperty("store").GetBoolean()).IsFalse();
        await Assert.That(body.TryGetProperty("max_tokens", out _)).IsFalse();
    }

    /// <summary>The subscription request preserves tool history and groups offered tools.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task BuildPreservesToolHistoryAndNamespace()
    {
        const string callId = "call-1";
        const string toolsField = "tools";
        using var schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
        var messages = new List<NimChatMessage>
        {
            new("system", NimContent.FromText("Follow instructions.")),
            new("assistant", ToolCalls: [new(Id: callId, Function: new("read", "{}"))]),
            new("tool", NimContent.FromText("file contents"), ToolCallId: callId),
        };
        var request = new NimChatRequest("gpt-test", messages, 1, Stream: false, Tools: [new(new("read", "Read a file.", schema.RootElement))]);
        var body = ChatGptRequestBuilder.Build(request);
        var input = body.GetProperty("input");

        await Assert.That(input[0].GetProperty("role").GetString()).IsEqualTo("developer");
        await Assert.That(input[1].GetProperty("type").GetString()).IsEqualTo("function_call");
        await Assert.That(input[1].GetProperty("call_id").GetString()).IsEqualTo(callId);
        await Assert.That(input[2].GetProperty("type").GetString()).IsEqualTo("function_call_output");
        await Assert.That(input[2].GetProperty("output").GetString()).IsEqualTo("file contents");
        await Assert.That(body.GetProperty(toolsField)[0].GetProperty("type").GetString()).IsEqualTo("namespace");
        await Assert.That(body.GetProperty(toolsField)[0].GetProperty(toolsField)[0].GetProperty("name").GetString()).IsEqualTo("read");
    }
}
