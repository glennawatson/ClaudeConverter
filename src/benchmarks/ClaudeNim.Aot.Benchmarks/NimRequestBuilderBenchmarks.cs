// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Benchmarks;

/// <summary>Measures the allocation of translating one Anthropic request into a NIM request.</summary>
/// <remarks>
/// <see cref="NimRequestBuilder.Build"/> runs once per call to <c>POST /v1/messages</c>, so the
/// fixture is a multi-turn conversation carrying a tool result, a tool definition and an image —
/// the shapes that exercise every branch of message and tool translation at once.
/// </remarks>
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class NimRequestBuilderBenchmarks
{
    /// <summary>How many times the request is built inside one invocation.</summary>
    private const int Repetitions = 500;

    /// <summary>The output-token ceiling attached to the request fixture.</summary>
    private const int MaxTokens = 4096;

    /// <summary>The length of the fixture's base64 image payload.</summary>
    private const int ImageDataLength = 4096;

    /// <summary>A vision-capable model, so the image branch of message translation is exercised.</summary>
    private const string VisionModel = "z-ai/glm-5.3-flash";

    /// <summary>The configured NIM defaults used to build every request.</summary>
    private static readonly NvidiaNimOptions Options = new();

    /// <summary>The tool definition attached to every request.</summary>
    private static readonly ToolDefinition Tool = new(
        "read_file",
        "Reads a range of lines from a file.",
        JsonElement.Parse(
            """
            {"type":"object","properties":{"path":{"type":"string"},"start_line":{"type":"integer"},"type":{"type":"string"}},"required":["path"]}
            """));

    /// <summary>The request built for every benchmark invocation.</summary>
    private readonly MessagesRequest _request = BuildRequest();

    /// <summary>Builds the request fixture <see cref="Repetitions"/> times.</summary>
    /// <returns>The message count of the last built request, so the call cannot be optimised away.</returns>
    [Benchmark]
    public int Build()
    {
        var count = 0;

        for (var i = 0; i < Repetitions; i++)
        {
            count = NimRequestBuilder.Build(_request, VisionModel, thinkingEnabled: true, Options).Messages.Count;
        }

        return count;
    }

    /// <summary>Builds the multi-turn request fixture, mirroring a real coding-session turn.</summary>
    /// <returns>The request fixture.</returns>
    private static MessagesRequest BuildRequest()
    {
        var image = new ImageSource("base64", "image/png", new string('A', ImageDataLength));

        List<AnthropicMessage> messages =
        [
            new(AnthropicMessage.UserRole, MessageContent.FromText("Can you look at this screenshot and read the file it shows?")),
            new(
                AnthropicMessage.UserRole,
                MessageContent.FromBlocks(
                [
                    ContentBlock.ForText("Here is the screenshot."),
                    new(ContentBlockTypes.Image, Source: image),
                ])),
            new(
                AnthropicMessage.AssistantRole,
                MessageContent.FromBlocks(
                [
                    new(ContentBlockTypes.Thinking, Thinking: "The screenshot shows a stack trace pointing at Program.cs."),
                    ContentBlock.ForText("Let me read that file."),
                    new(
                        ContentBlockTypes.ToolUse,
                        Id: "toolu_1",
                        Name: "read_file",
                        Input: JsonElement.Parse("""{"path":"src/Program.cs","start_line":1}""")),
                ])),
            new(
                AnthropicMessage.UserRole,
                MessageContent.FromBlocks(
                [
                    new(
                        ContentBlockTypes.ToolResult,
                        ToolUseId: "toolu_1",
                        Content: JsonElement.Parse(""" "public static class Program { }" """)),
                ])),
            new(AnthropicMessage.AssistantRole, MessageContent.FromText("The file defines the entry point.")),
            new(AnthropicMessage.UserRole, MessageContent.FromText("Thanks, can you double-check the retry policy too?")),
        ];

        return new(
            VisionModel,
            messages,
            MaxTokens,
            System: MessageContent.FromText("You are a careful coding assistant."),
            Stream: true,
            Tools: [Tool]);
    }
}
