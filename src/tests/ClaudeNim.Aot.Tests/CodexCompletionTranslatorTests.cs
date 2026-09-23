// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers translating a completed NVIDIA NIM completion into a Responses API turn.</summary>
public sealed class CodexCompletionTranslatorTests
{
    /// <summary>The turn identifier used across the fixtures.</summary>
    private const string ResponseId = "resp_1";

    /// <summary>The model name echoed back across the fixtures.</summary>
    private const string EchoedModel = "gpt-5.1-codex";

    /// <summary>The prompt size fed to every translation.</summary>
    private const int InputTokens = 5;

    /// <summary>The role every fixture message is authored as.</summary>
    private const string AssistantRole = "assistant";

    /// <summary>The NIM model every fixture translates a completion from, which its log lines name.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The answer text shared by the plain-text fixtures.</summary>
    private const string AnswerText = "the answer";

    /// <summary>The reasoning text shared by the reasoning-field fixtures.</summary>
    private const string ReasoningText = "let me think";

    /// <summary>The call identifier shared by the tool-call fixtures.</summary>
    private const string CallId = "call_1";

    /// <summary>The reported prompt tokens used by the reported-usage fixture.</summary>
    private const int ReportedPromptTokens = 11;

    /// <summary>The reported completion tokens used by the reported-usage fixture.</summary>
    private const int ReportedCompletionTokens = 22;

    /// <summary>The reported total tokens used by the reported-usage fixture.</summary>
    private const int ReportedTotalTokens = 33;

    /// <summary>An absent completion still yields a single placeholder message item.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task AbsentCompletionYieldsAPlaceholderMessage()
    {
        var response = Translate(null, thinkingEnabled: true);

        await Assert.That(response.Output.Count).IsEqualTo(1);
        await Assert.That(response.Output[0].Content?[0].Text).IsEqualTo(" ");
        await Assert.That(response.Status).IsEqualTo(ResponsesResponse.StatusCompleted);
    }

    /// <summary>Plain answer text becomes an assistant message item.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PlainAnswerTextBecomesAMessageItem()
    {
        var completion = Completion(Message(AnswerText));

        var response = Translate(completion, thinkingEnabled: false);

        await Assert.That(response.Output[0].Type).IsEqualTo(ResponseItemTypes.Message);
        await Assert.That(response.Output[0].Content?[0].Type).IsEqualTo(ResponseContentTypes.OutputText);
        await Assert.That(response.Output[0].Content?[0].Text).IsEqualTo(AnswerText);
        await Assert.That(response.OutputText).IsEqualTo(AnswerText);
    }

    /// <summary>Reasoning reported on its own field becomes a reasoning item ahead of the message.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReasoningContentBecomesALeadingReasoningItem()
    {
        var completion = Completion(Message(AnswerText, reasoning: ReasoningText));

        var response = Translate(completion, thinkingEnabled: true);

        await Assert.That(response.Output[0].Type).IsEqualTo(ResponseItemTypes.Reasoning);
        await Assert.That(response.Output[0].Summary?[0].Text).IsEqualTo(ReasoningText);
        await Assert.That(response.Output[1].Type).IsEqualTo(ResponseItemTypes.Message);
    }

    /// <summary>Reasoning is dropped when the tier has it disabled.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReasoningIsDroppedWhenDisabled()
    {
        var completion = Completion(Message(AnswerText, reasoning: ReasoningText));

        var response = Translate(completion, thinkingEnabled: false);

        await Assert.That(response.Output.Exists(static item => item.Type == ResponseItemTypes.Reasoning)).IsFalse();
    }

    /// <summary>A structured tool call becomes its own <c>function_call</c> item.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StructuredToolCallBecomesAFunctionCallItem()
    {
        var toolCall = new NimToolCall(0, CallId, new NimFunctionCall("search", "{\"q\":\"x\"}"));
        var completion = Completion(Message(null, toolCalls: [toolCall]), finishReason: "tool_calls");

        var response = Translate(completion, thinkingEnabled: false);

        var call = response.Output.Find(static item => item.Type == ResponseItemTypes.FunctionCall);
        await Assert.That(call).IsNotNull();
        await Assert.That(call!.CallId).IsEqualTo(CallId);
        await Assert.That(call.Name).IsEqualTo("search");
        await Assert.That(call.Arguments).IsEqualTo("{\"q\":\"x\"}");
    }

    /// <summary>A finish reason of <c>length</c> marks the turn incomplete.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task LengthFinishReasonMarksTheTurnIncomplete()
    {
        var completion = Completion(Message("cut off"), finishReason: "length");

        var response = Translate(completion, thinkingEnabled: false);

        await Assert.That(response.Status).IsEqualTo(ResponsesResponse.StatusIncomplete);
        await Assert.That(response.IncompleteDetails?.Reason).IsEqualTo("max_output_tokens");
    }

    /// <summary>A content-filter finish reason yields a refusal part rather than an answer.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ContentFilterFinishReasonYieldsARefusal()
    {
        var completion = Completion(Message(null), finishReason: "content_filter");

        var response = Translate(completion, thinkingEnabled: false);

        await Assert.That(response.Output[0].Content?[0].Type).IsEqualTo(ResponseContentTypes.Refusal);
    }

    /// <summary>Reported usage is preferred over the estimate.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReportedUsageIsPreferredOverTheEstimate()
    {
        var completion = Completion(Message(AnswerText)) with { Usage = new NimUsage(ReportedPromptTokens, ReportedCompletionTokens, ReportedTotalTokens) };

        var response = Translate(completion, thinkingEnabled: false);

        await Assert.That(response.Usage?.InputTokens).IsEqualTo(ReportedPromptTokens);
        await Assert.That(response.Usage?.OutputTokens).IsEqualTo(ReportedCompletionTokens);
    }

    /// <summary>Translates a completion with the fixture identifiers.</summary>
    /// <param name="completion">The completion to translate.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded.</param>
    /// <returns>The translated turn.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ResponsesResponse Translate(NimChatCompletion? completion, bool thinkingEnabled) =>
        CodexCompletionTranslator.Translate(
            completion,
            ResponseId,
            EchoedModel,
            UpstreamModel,
            new(InputTokens, thinkingEnabled, new FakeTimeProvider(), NullLogger.Instance));

    /// <summary>Builds a completion carrying one choice.</summary>
    /// <param name="message">The choice's message.</param>
    /// <param name="finishReason">The choice's finish reason.</param>
    /// <returns>The completion.</returns>
    private static NimChatCompletion Completion(NimChatMessage message, string? finishReason = null) =>
        new(Choices: [new NimChoice(Message: message, FinishReason: finishReason)]);

    /// <summary>Builds a fixture assistant message.</summary>
    /// <param name="text">The answer text, if any.</param>
    /// <param name="reasoning">The reasoning text, if any.</param>
    /// <param name="toolCalls">The tool calls, if any.</param>
    /// <returns>The message.</returns>
    private static NimChatMessage Message(string? text, string? reasoning = null, List<NimToolCall>? toolCalls = null) =>
        new(
            AssistantRole,
            text is null ? null : NimContent.FromText(text),
            toolCalls,
            ReasoningContent: reasoning);
}
