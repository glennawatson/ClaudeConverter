// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Tests;

/// <summary>
/// Covers the flat record and record-struct payloads that carry no behavior beyond their own
/// construction, so the members System.Text.Json's generated (de)serializers read are exercised at
/// least once.
/// </summary>
public sealed class DataTransferObjectTests
{
    /// <summary>The Claude model name shared by the DTO fixtures.</summary>
    private const string ClaudeModel = "claude-sonnet-5";

    /// <summary>The message identifier shared by the DTO fixtures.</summary>
    private const string MessageId = "msg_1";

    /// <summary>The stop reason a declined turn carries.</summary>
    private const string RefusalStopReason = "refusal";

    /// <summary>The error class shared by the error-body fixtures.</summary>
    private const string ApiErrorType = "api_error";

    /// <summary>The prompt size shared by the DTO fixtures.</summary>
    private const int SamplePromptTokens = 1;

    /// <summary>The completion size shared by the DTO fixtures.</summary>
    private const int SampleCompletionTokens = 2;

    /// <summary>The status code the mid-stream error fixture carries.</summary>
    private const int SampleErrorCode = 500;

    /// <summary>The token count the count-response fixture reports.</summary>
    private const int SampleTokenCount = 42;

    /// <summary>The stop reason shared by the streaming-envelope and finish-reason fixtures.</summary>
    private const string EndTurnStopReason = "end_turn";

    /// <summary>Every streaming-envelope record round-trips its constructor arguments onto its properties.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamingEnvelopesRoundTripTheirConstructorArguments()
    {
        var message = new StreamMessage(MessageId, ClaudeModel, new TokenUsage(SamplePromptTokens, SampleCompletionTokens), []);
        var start = new StreamMessageStart(message);
        var blockStart = new StreamContentBlockStart(0, ContentBlock.ForText(string.Empty));
        var delta = new StreamContentBlockDelta(0, StreamDelta.ForText("hi"));
        var stop = new StreamContentBlockStop(0);
        var stopDetail = new StreamStopDetail(EndTurnStopReason, "STOP");
        var messageDelta = new StreamMessageDelta(stopDetail, new TokenUsage(SamplePromptTokens, SampleCompletionTokens));

        await Assert.That(start.Message.Id).IsEqualTo(MessageId);
        await Assert.That(start.Type).IsEqualTo("message_start");
        await Assert.That(blockStart.Type).IsEqualTo("content_block_start");
        await Assert.That(delta.Delta.Text).IsEqualTo("hi");
        await Assert.That(stop.Index).IsEqualTo(0);
        await Assert.That(messageDelta.Delta.StopSequence).IsEqualTo("STOP");
        await Assert.That(StreamMessageStop.Instance.Type).IsEqualTo("message_stop");
    }

    /// <summary>The Nvidia chat DTOs round-trip their constructor arguments onto their properties.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NvidiaChatDtosRoundTripTheirConstructorArguments()
    {
        var function = new NimFunctionDefinition("search", "Searches", ClaudeNim.Aot.Serialization.JsonElements.EmptyObjectSchema);
        var tool = new NimTool(function);
        var delta = new NimDelta("assistant", "hi", "why", [new NimToolCall()]);
        var usage = new NimUsage(SamplePromptTokens, SampleCompletionTokens, SamplePromptTokens + SampleCompletionTokens);
        var completion = new NimChatCompletion("id", "model", [new NimChoice()], usage);
        var streamError = new NimStreamError("boom", "type", SampleErrorCode);
        var chunk = new NimChatCompletionChunk("id", "model", [new NimStreamChoice()], usage, streamError);

        await Assert.That(tool.Type).IsEqualTo("function");
        await Assert.That(tool.Function.Name).IsEqualTo("search");
        await Assert.That(delta.Role).IsEqualTo("assistant");
        await Assert.That(completion.Id).IsEqualTo("id");
        await Assert.That(chunk.Error?.Code).IsEqualTo(SampleErrorCode);
    }

    /// <summary>The Anthropic error and configuration DTOs round-trip their constructor arguments.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ErrorAndConfigurationDtosRoundTripTheirConstructorArguments()
    {
        var detail = new ErrorDetail(ApiErrorType, "boom");
        var error = ErrorResponse.Create(ApiErrorType, "boom");
        var listing = new ModelListResponse([], false, null, null);
        var output = new OutputConfig("high");
        var count = new TokenCountRequest(ClaudeModel, []);
        var countResponse = new TokenCountResponse(SampleTokenCount);
        var auth = new ProxyAuthenticationOptions("secret");

        await Assert.That(detail.Message).IsEqualTo("boom");
        await Assert.That(error.Error.Type).IsEqualTo(ApiErrorType);
        await Assert.That(error.Type).IsEqualTo("error");
        await Assert.That(listing.HasMore).IsFalse();
        await Assert.That(output.Effort).IsEqualTo("high");
        await Assert.That(count.Model).IsEqualTo(ClaudeModel);
        await Assert.That(countResponse.InputTokens).IsEqualTo(SampleTokenCount);
        await Assert.That(auth.AuthToken).IsEqualTo("secret");
    }

    /// <summary>Every OpenAI-style finish reason maps onto its documented Anthropic stop reason.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FinishReasonsMapOntoDocumentedStopReasons()
    {
        await Assert.That(StopReasons.FromFinishReason("tool_calls")).IsEqualTo("tool_use");
        await Assert.That(StopReasons.FromFinishReason("length")).IsEqualTo("max_tokens");
        await Assert.That(StopReasons.FromFinishReason("stop")).IsEqualTo(EndTurnStopReason);
        await Assert.That(StopReasons.FromFinishReason(null)).IsEqualTo(EndTurnStopReason);
    }

    /// <summary>A filtered turn is reported as a refusal rather than as a finished one.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A Claude 5.5-era client checks the stop reason before reading the content, and a declined
    /// turn carries no content to read. Reported as <c>end_turn</c> it was indistinguishable from
    /// a model with nothing to say, which for a coding client ends the work.
    /// </remarks>
    [Test]
    public async Task FilteredTurnIsReportedAsARefusal()
    {
        await Assert.That(StopReasons.FromFinishReason("content_filter")).IsEqualTo(RefusalStopReason);
        await Assert.That(StopReasons.DetailFor(RefusalStopReason)!.Type).IsEqualTo(RefusalStopReason);
        await Assert.That(StopReasons.DetailFor(RefusalStopReason)!.Explanation).IsNotNull();
    }

    /// <summary>The detail is attached to a refusal alone, as Anthropic documents it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StopDetailIsAbsentForEveryOtherStopReason()
    {
        await Assert.That(StopReasons.DetailFor(EndTurnStopReason)).IsNull();
        await Assert.That(StopReasons.DetailFor("max_tokens")).IsNull();
        await Assert.That(StopReasons.DetailFor(null)).IsNull();
    }

    /// <summary>A refusal serializes through the generated context, which is what native AOT needs.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Reflection-based serialization is off, so a payload missing from the context fails at
    /// runtime rather than at build time. Serializing one here is what catches that.
    /// </remarks>
    [Test]
    public async Task RefusalSerializesThroughTheGeneratedContext()
    {
        var message = new MessagesResponse(
            MessageId,
            ClaudeModel,
            [ContentBlock.ForText(string.Empty)],
            RefusalStopReason,
            new TokenUsage(1, 0),
            StopDetails: StopReasons.DetailFor(RefusalStopReason));

        var json = JsonSerializer.Serialize(message, ProxyJsonContext.Default.MessagesResponse);

        await Assert.That(json).Contains("\"stop_reason\":\"refusal\"");
        await Assert.That(json).Contains("\"stop_details\":{");
        await Assert.That(json).Contains("\"type\":\"refusal\"");
    }
}
