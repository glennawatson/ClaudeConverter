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

    /// <summary>The text returned by a successful backup.</summary>
    private const string ServedResponseText = "served";

    /// <summary>The primary attempt followed by its backup.</summary>
    private const int CallsAfterOneFallback = 2;

    /// <summary>The model configured behind the primary.</summary>
    private const string BackupModel = "nvidia/nemotron-3.5-lightning-30b-a3b";

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

    /// <summary>Backend credentials, exhausted quota and model-specific token limits can use the configured backup.</summary>
    /// <param name="status">The unavailable backend's status.</param>
    /// <param name="body">The backend's rejection detail.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(HttpStatusCode.Unauthorized, "Invalid backend key")]
    [Arguments(HttpStatusCode.Forbidden, "Backend credential expired")]
    [Arguments(HttpStatusCode.PaymentRequired, "Insufficient credits")]
    [Arguments(HttpStatusCode.TooManyRequests, """{"error":{"code":"insufficient_quota"}}""")]
    [Arguments(HttpStatusCode.BadRequest, """{"error":{"code":"context_length_exceeded","message":"Too many tokens"}}""")]
    [Arguments(HttpStatusCode.RequestEntityTooLarge, """{"error":{"message":"This model's maximum context length is 8192 tokens."}}""")]
    [Arguments(HttpStatusCode.UnprocessableEntity, """{"error":{"message":"The prompt is too long."}}""")]
    public async Task BackendTokenOrCredentialFailureUsesTheBackup(HttpStatusCode status, string body, CancellationToken cancellationToken)
    {
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        using var rejection = new HttpResponseMessage(status) { Content = new StringContent(body) };
        var client = new FakeNimClient { OnSendChat = request => request.Model == UpstreamModel ? rejection : JsonResponse(HttpStatusCode.OK, completion) };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), services, cancellationToken);

        var answered = await Assert.That(result).IsTypeOf<JsonHttpResult<ResponsesResponse>>();
        await Assert.That(answered!.Value!.OutputText).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneFallback);
        await Assert.That(client.Requests[1].Model).IsEqualTo(BackupModel);
    }

    /// <summary>A dropped error body cannot prevent switching away from a failed backend.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task UnreadableRejectionBodyUsesTheBackup(CancellationToken cancellationToken)
    {
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        var client = new FakeNimClient
        {
            OnSendChat = request => request.Model == UpstreamModel
                ? new(HttpStatusCode.ServiceUnavailable) { Content = new ThrowingHttpContent() }
                : JsonResponse(HttpStatusCode.OK, completion),
        };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), services, cancellationToken);

        var answered = await Assert.That(result).IsTypeOf<JsonHttpResult<ResponsesResponse>>();
        await Assert.That(answered!.Value!.OutputText).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneFallback);
    }

    /// <summary>A dropped completion body can use a backup before any output has reached the caller.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task UnreadableCompletionBodyUsesTheBackup(CancellationToken cancellationToken)
    {
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        var client = new FakeNimClient
        {
            OnSendChat = request => request.Model == UpstreamModel
                ? new(HttpStatusCode.OK) { Content = new ThrowingHttpContent() }
                : JsonResponse(HttpStatusCode.OK, completion),
        };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), services, cancellationToken);

        var answered = await Assert.That(result).IsTypeOf<JsonHttpResult<ResponsesResponse>>();
        await Assert.That(answered!.Value!.OutputText).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneFallback);
    }

    /// <summary>A completion-body failure exhausts each remaining model once before reporting failure.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task UnreadableCompletionBodiesExhaustTheRemainingChain(CancellationToken cancellationToken)
    {
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.OK) { Content = new ThrowingHttpContent() } };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), services, cancellationToken);

        var rejected = await Assert.That(result).IsTypeOf<JsonHttpResult<CodexErrorResponse>>();
        await Assert.That(rejected!.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneFallback);
    }

    /// <summary>A stream that has emitted answer text cannot be replaced by another model's answer.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task StreamFailureAfterVisibleOutputDoesNotUseTheBackup(CancellationToken cancellationToken)
    {
        const string Events = """
            data: {"choices":[{"delta":{"content":"</think>visible"}}]}

            data: {"error":{"message":"Unavailable","code":503}}

            """;
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.OK) { Content = new StringContent(Events) } };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)], Stream: true);
        var context = Context();

        _ = await ResponsesEndpointExtensions.SendResponseAsync(request, context, services, cancellationToken);

        await Assert.That(Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())).Contains("visible");
        await Assert.That(client.Requests.Count).IsEqualTo(1);
    }

    /// <summary>Malformed input and content filtering remain terminal even when a backup is configured.</summary>
    /// <param name="body">The backend's rejection detail.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("Malformed tool schema")]
    [Arguments("""{"error":{"code":"content_filter","message":"Blocked prompt: maximum context length"}}""")]
    public async Task RejectedRequestIsNotWalkedDownTheChain(string body, CancellationToken cancellationToken)
    {
        var client = new FakeNimClient { OnSendChat = _ => new(HttpStatusCode.BadRequest) { Content = new StringContent(body) } };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };

        var result = await ResponsesEndpointExtensions.SendResponseAsync(new(CodexModel, [UserMessage(OrdinaryUserText)]), Context(), services, cancellationToken);

        var rejected = await Assert.That(result).IsTypeOf<JsonHttpResult<CodexErrorResponse>>();
        await Assert.That(rejected!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(client.Requests.Count).IsEqualTo(1);
    }

    /// <summary>A backend deadline is recoverable while the caller still wants the response.</summary>
    /// <param name="timedOut">Whether the backend deadline or a connection failure is simulated.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task UpstreamTransportFailureUsesTheBackup(bool timedOut, CancellationToken cancellationToken)
    {
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        var backendFailure = timedOut ? (Exception)new OperationCanceledException("Backend deadline") : new HttpRequestException("Backend disconnected");
        var client = new FakeNimClient { OnSendChat = request => request.Model == UpstreamModel ? throw backendFailure : JsonResponse(HttpStatusCode.OK, completion) };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };

        var result = await ResponsesEndpointExtensions.SendResponseAsync(new(CodexModel, [UserMessage(OrdinaryUserText)]), Context(), services, cancellationToken);

        var answered = await Assert.That(result).IsTypeOf<JsonHttpResult<ResponsesResponse>>();
        await Assert.That(answered!.Value!.OutputText).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneFallback);
    }

    /// <summary>A caller's cancelled turn does not make another paid backend call.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ClientCancellationNeverUsesTheBackup(CancellationToken cancellationToken)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new FakeNimClient { OnSendChat = Abandon };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, UpstreamModel, ModelTier.Sonnet, true, [BackupModel])),
        };

        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);
        await Assert.That(async () => await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), services, source.Token)).Throws<OperationCanceledException>();

        await Assert.That(client.Requests.Count).IsEqualTo(1);

        HttpResponseMessage Abandon(NimChatRequest _)
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        }
    }

    /// <summary>Azure quota exhaustion crosses providers to the configured NVIDIA backup.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AzureQuotaExhaustionUsesTheNvidiaBackup(CancellationToken cancellationToken)
    {
        var azure = new FakeOpenAiCompatibleClient { OnSendChat = static _ => new(HttpStatusCode.TooManyRequests) };
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        var client = new FakeNimClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };
        var services = Services(client) with
        {
            Router = new FakeModelRouter(new(CodexModel, "azure:gpt-6-astra", ModelTier.Sonnet, true, [UpstreamModel])),
            AzureClient = azure,
        };
        var request = new ResponsesRequest(CodexModel, [UserMessage(OrdinaryUserText)]);

        var result = await ResponsesEndpointExtensions.SendResponseAsync(request, Context(), services, cancellationToken);

        var answered = await Assert.That(result).IsTypeOf<JsonHttpResult<ResponsesResponse>>();
        await Assert.That(answered!.Value!.OutputText).IsEqualTo(ServedResponseText);
        await Assert.That(azure.Requests.Count).IsEqualTo(1);
        await Assert.That(azure.Requests[0].Model).IsEqualTo("gpt-6-astra");
        await Assert.That(client.Requests.Count).IsEqualTo(1);
        await Assert.That(client.Requests[0].Model).IsEqualTo(UpstreamModel);
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
            new FakeOpenAiCompatibleClient(),
            new FakeOpenAiCompatibleClient(),
            TimeProvider.System,
            NullLogger<CodexServices>.Instance);

    /// <summary>Wraps a completion as a JSON upstream response.</summary>
    /// <param name="status">The status to return.</param>
    /// <param name="completion">The completion to serialize as the body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, NimChatCompletion completion) =>
        new(status) { Content = JsonContent.Create(completion, ProxyJsonContext.Default.NimChatCompletion) };
}
