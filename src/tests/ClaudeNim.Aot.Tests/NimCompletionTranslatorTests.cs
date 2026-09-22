// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers translating a completed NVIDIA NIM completion into an Anthropic message.</summary>
public sealed class NimCompletionTranslatorTests
{
    /// <summary>The message identifier used across the fixtures.</summary>
    private const string MessageId = "msg_1";

    /// <summary>The model name echoed back across the fixtures.</summary>
    private const string EchoedModel = "claude-sonnet-5";

    /// <summary>The prompt size fed to every translation.</summary>
    private const int InputTokens = 5;

    /// <summary>The role every fixture message is authored as.</summary>
    private const string AssistantRole = "assistant";

    /// <summary>The reasoning text shared by the reasoning-field fixtures.</summary>
    private const string ReasoningText = "let me think";

    /// <summary>The discriminator of a thinking content block.</summary>
    private const string ThinkingBlockType = "thinking";

    /// <summary>The tool name shared by the tool-call fixtures.</summary>
    private const string ToolName = "search";

    /// <summary>The reported prompt tokens used by the reported-usage fixture.</summary>
    private const int ReportedPromptTokens = 11;

    /// <summary>The reported completion tokens used by the reported-usage fixture.</summary>
    private const int ReportedCompletionTokens = 22;

    /// <summary>The reported total tokens used by the reported-usage fixture.</summary>
    private const int ReportedTotalTokens = 33;

    /// <summary>The character length of the long-answer fixture text.</summary>
    private const int LongAnswerLength = 400;

    /// <summary>The stop-reason and content-block discriminator shared by the tool-call fixtures.</summary>
    private const string ToolUseType = "tool_use";

    /// <summary>The NIM model every fixture translates a completion from, which its log lines name.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>An absent completion still yields a single placeholder text block.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task AbsentCompletionYieldsAPlaceholderBlock()
    {
        var message = NimCompletionTranslator.Translate(null, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Content.Count).IsEqualTo(1);
        await Assert.That(message.Content[0].Text).IsEqualTo(" ");
        await Assert.That(message.StopReason).IsEqualTo("end_turn");
    }

    /// <summary>Plain answer text becomes a single text block.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PlainAnswerTextBecomesATextBlock()
    {
        var completion = Completion(new(AssistantRole, NimContent.FromText("hello there")));

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Content.Count).IsEqualTo(1);
        await Assert.That(message.Content[0].Type).IsEqualTo("text");
        await Assert.That(message.Content[0].Text).IsEqualTo("hello there");
    }

    /// <summary>Reasoning on its own field becomes a thinking block when reasoning is enabled.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReasoningFieldBecomesAThinkingBlockWhenEnabled()
    {
        var choice = new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("done"), ReasoningContent: ReasoningText));
        var completion = new NimChatCompletion(Choices: [choice]);

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Content.Exists(static b => b.Type == ThinkingBlockType && b.Thinking == ReasoningText)).IsTrue();
    }

    /// <summary>Reasoning is suppressed entirely when reasoning is disabled.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReasoningIsSuppressedWhenDisabled()
    {
        var choice = new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("done"), ReasoningContent: ReasoningText));
        var completion = new NimChatCompletion(Choices: [choice]);

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: false, NullLogger.Instance);

        await Assert.That(message.Content.Exists(static b => b.Type == ThinkingBlockType)).IsFalse();
    }

    /// <summary>An inline <c>&lt;think&gt;</c> tag in the answer is split into a thinking block.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task InlineThinkTagSplitsIntoAThinkingBlock()
    {
        var completion = Completion(new(AssistantRole, NimContent.FromText("<think>reasoning</think>answer")));

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Content.Exists(static b => b.Type == ThinkingBlockType && b.Thinking == "reasoning")).IsTrue();
        await Assert.That(message.Content.Exists(static b => b.Type == "text" && b.Text == "answer")).IsTrue();
    }

    /// <summary>A structured tool call becomes a tool-use block with parsed arguments.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StructuredToolCallBecomesAToolUseBlock()
    {
        var call = new NimToolCall(Id: "call_1", Function: new NimFunctionCall(ToolName, """{"q":"weather"}"""));
        var message0 = new NimChatMessage(AssistantRole, null, ToolCalls: [call]);
        var completion = new NimChatCompletion(Choices: [new NimChoice(Message: message0, FinishReason: "tool_calls")]);

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        var block = message.Content.Find(static b => b.Type == ToolUseType);
        await Assert.That(block).IsNotNull();
        await Assert.That(block!.Name).IsEqualTo(ToolName);
        await Assert.That(block.Id).IsEqualTo("call_1");
        await Assert.That(message.StopReason).IsEqualTo(ToolUseType);
    }

    /// <summary>An embedded tool call written into the answer text is recovered as a tool-use block.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmbeddedToolCallIsRecoveredFromAnswerText()
    {
        const string Text = "<|tool_call_begin|>search<|tool_call_argument_begin|>{}<|tool_call_end|>";
        var completion = Completion(new(AssistantRole, NimContent.FromText(Text)));

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Content.Exists(static b => b.Type == ToolUseType && b.Name == ToolName)).IsTrue();
    }

    /// <summary>Usage reported by the upstream is used verbatim, rather than the estimate.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReportedUsageOverridesTheEstimate()
    {
        var choice = new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("hi")));
        var completion = new NimChatCompletion(
            Choices: [choice],
            Usage: new NimUsage(ReportedPromptTokens, ReportedCompletionTokens, ReportedTotalTokens));

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Usage.InputTokens).IsEqualTo(ReportedPromptTokens);
        await Assert.That(message.Usage.OutputTokens).IsEqualTo(ReportedCompletionTokens);
    }

    /// <summary>Absent usage falls back to an estimate derived from the composed blocks.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AbsentUsageFallsBackToAnEstimate()
    {
        var completion = Completion(new(AssistantRole, NimContent.FromText(new('a', LongAnswerLength))));

        var message = NimCompletionTranslator.Translate(completion, MessageId, EchoedModel, UpstreamModel, InputTokens, thinkingEnabled: true, NullLogger.Instance);

        await Assert.That(message.Usage.OutputTokens).IsGreaterThan(0);
    }

    /// <summary>A call lifted out of answer text says so in the log.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The two ways a call reaches a client are indistinguishable once it gets there, and they
    /// fail differently: one carries the model's own arguments, the other carries arguments this
    /// proxy assembled from marker tokens. Without this line, a call that arrives with arguments
    /// making no sense cannot be attributed to either from the log.
    /// </remarks>
    [Test]
    public async Task RecoveredCallIsReported()
    {
        const string Answer = "<tool_call><function=search><parameter=query>rain</parameter></function></tool_call>";

        var logger = new CapturingLogger<NimCompletionTranslatorTests>();
        var completion = Completion(new(AssistantRole, NimContent.FromText(Answer)));

        var message = NimCompletionTranslator.Translate(
            completion,
            MessageId,
            EchoedModel,
            UpstreamModel,
            InputTokens,
            thinkingEnabled: true,
            logger);

        await Assert.That(message.Content.Exists(static block => block.Type == ToolUseType)).IsTrue();

        var recovered = logger.Entries.FindAll(static entry =>
            entry.Level >= LogLevel.Warning && entry.Message.Contains("Recovered a search call", StringComparison.Ordinal));

        await Assert.That(recovered.Count).IsEqualTo(1);
        await Assert.That(recovered[0].Message).Contains(UpstreamModel);
    }

    /// <summary>Wraps one message as a single-choice completion.</summary>
    /// <param name="message">The completed message.</param>
    /// <returns>The completion.</returns>
    private static NimChatCompletion Completion(NimChatMessage message) => new(Choices: [new(Message: message)]);
}
