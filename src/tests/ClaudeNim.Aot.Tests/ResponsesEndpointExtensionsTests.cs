// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Codex.Streaming;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Endpoints.Codex;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers serving one Responses API turn.</summary>
public sealed class ResponsesEndpointExtensionsTests
{
    /// <summary>The upstream model every fixture resolves to.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The Codex model name used across the fixture requests.</summary>
    private const string CodexModel = "gpt-5.1-codex";

    /// <summary>The role every fixture message is authored as.</summary>
    private const string UserRole = "user";

    /// <summary>The role the fake upstream authors its answers as.</summary>
    private const string AssistantRole = "assistant";

    /// <summary>The user text shared by the real-turn fixtures.</summary>
    private const string OrdinaryUserText = "list the files in this directory";

    /// <summary>A request missing a model name is rejected before an upstream call is attempted.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task MissingModelIsRejected()
    {
        var request = new ResponsesRequest(string.Empty, [UserMessage("hi")]);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), Services(new()), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(((JsonHttpResult<CodexErrorResponse>)result!).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    /// <summary>A request carrying no input items is rejected before an upstream call is attempted.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyInputIsRejected()
    {
        var request = new ResponsesRequest(CodexModel, []);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), Services(new()), CancellationToken.None);

        await Assert.That(((JsonHttpResult<CodexErrorResponse>)result!).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    /// <summary>A non-streamed turn is translated from the upstream's completed JSON body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NonStreamedTurnIsTranslatedFromTheUpstreamBody()
    {
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("Sure, running `ls`.")))]);
        var client = new FakeNimClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), Services(client), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        var typed = (JsonHttpResult<ResponsesResponse>)result!;
        await Assert.That(typed.Value!.OutputText).IsEqualTo("Sure, running `ls`.");
        await Assert.That(typed.Value.Status).IsEqualTo(ResponsesResponse.StatusCompleted);
    }

    /// <summary>A structured tool call is translated into a <c>function_call</c> output item.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ToolCallIsTranslatedIntoAFunctionCallItem()
    {
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);
        var toolCall = new NimToolCall(0, "call_1", new NimFunctionCall("list_files", "{}"));
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, ToolCalls: [toolCall]), FinishReason: "tool_calls")]);
        var client = new FakeNimClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<ResponsesResponse>)result!;
        var call = typed.Value!.Output.Find(static item => item.Type == ResponseItemTypes.FunctionCall);
        await Assert.That(call).IsNotNull();
        await Assert.That(call!.Name).IsEqualTo("list_files");
        await Assert.That(call.CallId).IsEqualTo("call_1");
    }

    /// <summary>A failed upstream call is translated into a Responses API error body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FailedUpstreamCallBecomesACodexError()
    {
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") } };

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<CodexErrorResponse>)result!;
        await Assert.That(typed.StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
        await Assert.That(typed.Value!.Error.Message).Contains("boom");
    }

    /// <summary>A streamed turn writes Responses API events directly to the response body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedTurnWritesResponsesApiEventsToTheBody()
    {
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)], Stream: true);
        const string Sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Sure.\"}}]}\n\ndata: [DONE]\n\n";
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.OK) { Content = new StringContent(Sse) } };
        var context = Context();

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, context, Services(client), CancellationToken.None);

        await Assert.That(result).IsNull();
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        await Assert.That(body).Contains(ResponseStreamEventTypes.Created);
        await Assert.That(body).Contains(ResponseStreamEventTypes.OutputTextDelta);
        await Assert.That(body).Contains(ResponseStreamEventTypes.Completed);
        await Assert.That(body).Contains("Sure.");
    }

    /// <summary>Builds a <c>message</c> input item carrying one line of user text.</summary>
    /// <param name="text">The message text.</param>
    /// <returns>The item.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ResponseInputItem UserMessage(string text) =>
        ResponseInputItem.ForMessage(UserRole, [ResponseContentItem.ForInputText(text)]);

    /// <summary>Builds an HTTP context with a memory-backed response body.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext Context() => new() { Response = { Body = new MemoryStream() } };

    /// <summary>Builds the services a turn is served from, wired to a fake upstream client.</summary>
    /// <param name="client">The fake upstream client.</param>
    /// <returns>The services.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CodexServices Services(FakeNimClient client) =>
        new(
            new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true)),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            new RetryOptions(BaseDelayMilliseconds: 1, MaxDelayMilliseconds: 2, UseJitter: false),
            new ModelCatalogOptions(),
            new ResponsesCompletionTranslator(TimeProvider.System, NullLogger<ResponsesCompletionTranslator>.Instance),
            new ResponsesStreamTranslatorFactory(TimeProvider.System),
            new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System),
            new HttpTimeoutOptions(),
            TimeProvider.System,
            NullLogger<CodexServices>.Instance);

    /// <summary>Wraps a completion as a JSON upstream response.</summary>
    /// <param name="status">The status to return.</param>
    /// <param name="completion">The completion to serialize as the body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, NimChatCompletion completion) =>
        new(status) { Content = JsonContent.Create(completion, ProxyJsonContext.Default.NimChatCompletion) };
}
