// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the translation of a Responses API request into the NIM request that serves it.</summary>
public sealed class CodexRequestBuilderTests
{
    /// <summary>The output ceiling assumed for a model the catalogue does not size.</summary>
    private const int CatalogDefault = 65_536;

    /// <summary>The model identifier passed through to the upstream request.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The user text used by the minimal fixture requests.</summary>
    private const string UserText = "hello";

    /// <summary>The call identifier shared by the tool-call fixtures.</summary>
    private const string CallId = "call_1";

    /// <summary>The tool name shared by the tool-call fixtures.</summary>
    private const string ToolName = "read_file";

    /// <summary>The reasoning summary text shared by the reasoning fixtures.</summary>
    private const string ReasoningSummary = "thinking it through";

    /// <summary>The assistant answer text shared by the sibling-item fixtures.</summary>
    private const string AssistantAnswer = "On it.";

    /// <summary>The settings a request is built against.</summary>
    private static readonly NvidiaNimOptions Options = new();

    /// <summary>A streamed turn asks for usage, so token counts do not have to be guessed.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedTurnRequestsUsage()
    {
        var built = CodexRequestBuilder.Build(Request(UserMessage(UserText), stream: true), UpstreamModel, false, Options, CatalogDefault);

        await Assert.That(built.Stream).IsTrue();
        await Assert.That(built.StreamOptions?.IncludeUsage).IsTrue();
    }

    /// <summary>The system-level instruction becomes the first message.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task InstructionsBecomeTheSystemMessage()
    {
        const string Instructions = "be terse";
        var request = Request(UserMessage(UserText), instructions: Instructions);
        var built = CodexRequestBuilder.Build(request, UpstreamModel, false, Options, CatalogDefault);

        await Assert.That(built.Messages[0].Role).IsEqualTo(NimChatMessage.SystemRole);
        await Assert.That(built.Messages[0].Content?.Text).IsEqualTo(Instructions);
    }

    /// <summary>An assistant message, a reasoning item, and a function call sharing one turn fold into one upstream message.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task SiblingAssistantItemsFoldIntoOneMessage()
    {
        List<ResponseInputItem> input =
        [
            UserMessage(UserText),
            ResponseInputItem.ForReasoning([ResponseContentItem.ForSummaryText(ReasoningSummary)]),
            ResponseInputItem.ForMessage(ResponseItemTypes.AssistantRole, [ResponseContentItem.ForOutputText(AssistantAnswer)]),
            ResponseInputItem.ForFunctionCall(CallId, ToolName, "{\"path\":\"a.txt\"}"),
        ];

        var built = CodexRequestBuilder.Build(Request(input), UpstreamModel, true, Options, CatalogDefault);

        // System-less request: index 0 is the user turn, index 1 is the folded assistant turn.
        var assistant = built.Messages[1];

        await Assert.That(assistant.Role).IsEqualTo(NimChatMessage.AssistantRole);
        await Assert.That(assistant.Content?.Text).IsEqualTo(AssistantAnswer);
        await Assert.That(assistant.ReasoningContent).IsEqualTo(ReasoningSummary);
        await Assert.That(assistant.ToolCalls?[0].Function?.Name).IsEqualTo(ToolName);
        await Assert.That(assistant.ToolCalls?[0].Id).IsEqualTo(CallId);
    }

    /// <summary>Reasoning is folded only when the tier has reasoning enabled.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReasoningIsDroppedWhenTheTierHasItDisabled()
    {
        List<ResponseInputItem> input =
        [
            UserMessage(UserText),
            ResponseInputItem.ForReasoning([ResponseContentItem.ForSummaryText(ReasoningSummary)]),
            ResponseInputItem.ForMessage(ResponseItemTypes.AssistantRole, [ResponseContentItem.ForOutputText(AssistantAnswer)]),
        ];

        var built = CodexRequestBuilder.Build(Request(input), UpstreamModel, false, Options, CatalogDefault);

        await Assert.That(built.Messages[1].ReasoningContent).IsNull();
    }

    /// <summary>A <c>function_call_output</c> item with a bare string becomes a <c>tool</c> message.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FunctionCallOutputTextBecomesAToolMessage()
    {
        const string FileContents = "file contents";
        List<ResponseInputItem> input =
        [
            UserMessage(UserText),
            ResponseInputItem.ForFunctionCall(CallId, ToolName, "{}"),
            new ResponseInputItem(ResponseItemTypes.FunctionCallOutput, CallId: CallId, Output: FunctionCallOutput.FromText(FileContents)),
        ];

        var built = CodexRequestBuilder.Build(Request(input), UpstreamModel, false, Options, CatalogDefault);
        var toolMessage = built.Messages.Find(static m => string.Equals(m.Role, NimChatMessage.ToolRole, StringComparison.Ordinal));

        await Assert.That(toolMessage).IsNotNull();
        await Assert.That(toolMessage!.ToolCallId).IsEqualTo(CallId);
        await Assert.That(toolMessage.Content?.Text).IsEqualTo(FileContents);
    }

    /// <summary>A <c>function_call_output</c> item with an array of parts joins their text.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task FunctionCallOutputPartsAreJoined()
    {
        List<ResponseInputItem> input =
        [
            UserMessage(UserText),
            ResponseInputItem.ForFunctionCall(CallId, ToolName, "{}"),
            new ResponseInputItem(
                ResponseItemTypes.FunctionCallOutput,
                CallId: CallId,
                Output: FunctionCallOutput.FromItems([ResponseContentItem.ForOutputText("line one"), ResponseContentItem.ForOutputText("line two")])),
        ];

        var built = CodexRequestBuilder.Build(Request(input), UpstreamModel, false, Options, CatalogDefault);
        var toolMessage = built.Messages.Find(static m => string.Equals(m.Role, NimChatMessage.ToolRole, StringComparison.Ordinal));

        await Assert.That(toolMessage!.Content?.Text).IsEqualTo("line one\nline two");
    }

    /// <summary>Tool definitions translate their JSON Schema parameters through unchanged.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ToolDefinitionsCarryTheirSchema()
    {
        var schema = JsonElement.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""");
        List<CodexTool> tools = [new(CodexTool.FunctionType, ToolName, "Reads a file", schema)];

        var built = CodexRequestBuilder.Build(Request(UserMessage(UserText), tools: tools), UpstreamModel, false, Options, CatalogDefault);

        await Assert.That(built.Tools?[0].Function.Name).IsEqualTo(ToolName);
        await Assert.That(built.Tools?[0].Function.Parameters.GetProperty("properties").TryGetProperty("path", out _)).IsTrue();
    }

    /// <summary>A named tool choice is rebuilt into the nested shape NIM's chat-completions surface expects.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NamedToolChoiceIsNested()
    {
        var schema = JsonElement.Parse("{}");
        List<CodexTool> tools = [new(CodexTool.FunctionType, ToolName, Parameters: schema)];
        var choice = JsonElement.Parse("""{"type":"function","name":"read_file"}""");

        var built = CodexRequestBuilder.Build(Request(UserMessage(UserText), tools: tools, toolChoice: choice), UpstreamModel, false, Options, CatalogDefault);

        await Assert.That(built.ToolChoice?.GetProperty("function").GetProperty("name").GetString()).IsEqualTo(ToolName);
    }

    /// <summary>A bare string tool choice passes through unchanged.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task StringToolChoicePassesThrough()
    {
        var schema = JsonElement.Parse("{}");
        List<CodexTool> tools = [new(CodexTool.FunctionType, ToolName, Parameters: schema)];
        var choice = JsonElement.Parse("\"required\"");

        var built = CodexRequestBuilder.Build(Request(UserMessage(UserText), tools: tools, toolChoice: choice), UpstreamModel, false, Options, CatalogDefault);

        await Assert.That(built.ToolChoice?.GetString()).IsEqualTo("required");
    }

    /// <summary>A "minimal" reasoning effort folds onto NIM's lowest supported level.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task MinimalEffortFoldsToLow()
    {
        var built = CodexRequestBuilder.Build(
            Request(UserMessage(UserText), reasoning: new(Effort: "minimal")),
            UpstreamModel,
            true,
            Options,
            CatalogDefault);

        await Assert.That(built.ReasoningEffort).IsEqualTo("low");
    }

    /// <summary>Builds a <c>message</c> item carrying one line of user text.</summary>
    /// <param name="text">The message text.</param>
    /// <returns>The item.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ResponseInputItem UserMessage(string text) =>
        ResponseInputItem.ForMessage(ResponseItemTypes.UserRole, [ResponseContentItem.ForInputText(text)]);

    /// <summary>Builds a fixture request carrying one input item.</summary>
    /// <param name="item">The item the request carries.</param>
    /// <param name="stream">Whether the request asks to be streamed.</param>
    /// <param name="instructions">The system-level instruction, if any.</param>
    /// <param name="tools">The tools offered, if any.</param>
    /// <param name="toolChoice">The requested tool choice, if any.</param>
    /// <param name="reasoning">The reasoning controls, if any.</param>
    /// <returns>The request.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ResponsesRequest Request(
        ResponseInputItem item,
        bool stream = false,
        string? instructions = null,
        List<CodexTool>? tools = null,
        JsonElement? toolChoice = null,
        ReasoningOptions? reasoning = null) =>
        Request([item], stream, instructions, tools, toolChoice, reasoning);

    /// <summary>Builds a fixture request.</summary>
    /// <param name="input">The items the request carries.</param>
    /// <param name="stream">Whether the request asks to be streamed.</param>
    /// <param name="instructions">The system-level instruction, if any.</param>
    /// <param name="tools">The tools offered, if any.</param>
    /// <param name="toolChoice">The requested tool choice, if any.</param>
    /// <param name="reasoning">The reasoning controls, if any.</param>
    /// <returns>The request.</returns>
    private static ResponsesRequest Request(
        List<ResponseInputItem> input,
        bool stream = false,
        string? instructions = null,
        List<CodexTool>? tools = null,
        JsonElement? toolChoice = null,
        ReasoningOptions? reasoning = null) =>
        new(
            "gpt-5.1-codex",
            input,
            instructions,
            tools,
            toolChoice,
            Reasoning: reasoning,
            Stream: stream);
}
