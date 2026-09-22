// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers serving one Messages API turn, from housekeeping answers through to translated upstream replies.</summary>
public sealed class MessagesEndpointExtensionsTests
{
    /// <summary>The upstream model every fixture resolves to.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The Claude model name used across the fixture requests.</summary>
    private const string ClaudeModel = "claude-sonnet-5";

    /// <summary>The output ceiling used across ordinary (non-housekeeping) fixture requests.</summary>
    private const int OrdinaryMaxTokens = 1024;

    /// <summary>The role the fake upstream authors its answers as.</summary>
    private const string AssistantRole = "assistant";

    /// <summary>The user text shared by the real-turn fixtures.</summary>
    private const string OrdinaryUserText = "hello, how are you?";

    /// <summary>The number of upstream calls one retried streamed turn takes.</summary>
    private const int CallsAfterOneStreamRetry = 2;

    /// <summary>A request missing a model name is rejected before an upstream call is attempted.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task MissingModelIsRejected()
    {
        var request = new MessagesRequest(string.Empty, [new(AnthropicMessage.UserRole, MessageContent.FromText("hi"))], OrdinaryMaxTokens);

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(new()), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(((JsonHttpResult<ErrorResponse>)result!).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    /// <summary>A request with no messages is rejected before an upstream call is attempted.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyMessagesAreRejected()
    {
        var request = new MessagesRequest(ClaudeModel, [], OrdinaryMaxTokens);

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(new()), CancellationToken.None);

        await Assert.That(((JsonHttpResult<ErrorResponse>)result!).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    /// <summary>A housekeeping probe is answered locally, without an upstream call.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task HousekeepingProbeIsAnsweredLocallyWithoutAnUpstreamCall()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText("quota"))];
        var request = new MessagesRequest(ClaudeModel, messages, 1);
        var client = new FakeNimClient();

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(client.Requests.Count).IsEqualTo(0);
    }

    /// <summary>A streamed housekeeping probe writes Anthropic events directly to the response body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedHousekeepingProbeWritesEventsToTheBody()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText("quota"))];
        var request = new MessagesRequest(ClaudeModel, messages, 1, Stream: true);
        var context = Context();

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, context, Services(new()), CancellationToken.None);

        await Assert.That(result).IsNull();
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        await Assert.That(body).Contains("message_start");
        await Assert.That(body).Contains("message_stop");
    }

    /// <summary>A non-streamed turn is translated from the upstream's completed JSON body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NonStreamedTurnIsTranslatedFromTheUpstreamBody()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("I'm well.")))]);
        var client = new FakeNimClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        var typed = (JsonHttpResult<MessagesResponse>)result!;
        await Assert.That(typed.Value!.Content[0].Text).IsEqualTo("I'm well.");
    }

    /// <summary>A failed upstream call is translated into an Anthropic error body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FailedUpstreamCallBecomesAnAnthropicError()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") } };

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<ErrorResponse>)result!;
        await Assert.That(typed.StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
    }

    /// <summary>A streamed turn that failed before producing anything is asked for again.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// NVIDIA reports saturation inside an already-committed 200, which no status-code retry can
    /// catch. Nothing has reached the client at that point though, so the whole turn can be asked
    /// for again and the client never learns it happened — which is the difference between a
    /// session that carries on and one that stops on the upstream being busy.
    /// </remarks>
    [Test]
    public async Task StreamedTurnFailingBeforeOutputIsRetried()
    {
        const string Overloaded = """
            data: {"error":{"message":"Service temporarily overloaded","code":503}}

            data: [DONE]

            """;

        const string Answer = """
            data: {"choices":[{"delta":{"content":"recovered"}}]}

            data: [DONE]

            """;

        // The fake records each request before invoking this, so the first call sees a count of one.
        var client = new FakeNimClient();
        client.OnSendChat = _ => SaturatedResponse(client.Requests.Count == 1 ? Overloaded : Answer);

        var context = Context();
        var request = new MessagesRequest(
            ClaudeModel,
            [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))],
            OrdinaryMaxTokens,
            Stream: true);

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, context, Services(client), CancellationToken.None);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneStreamRetry);
        await Assert.That(body).Contains("recovered");
        await Assert.That(body).DoesNotContain("overloaded");
    }

    /// <summary>A streamed turn that never produces anything reports the failure in the end.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedTurnFailingEveryAttemptReportsTheFailure()
    {
        const string Overloaded = """
            data: {"error":{"message":"Service temporarily overloaded","code":503}}

            data: [DONE]

            """;

        var client = new FakeNimClient { OnSendChat = static _ => SaturatedResponse(Overloaded) };

        var context = Context();
        var request = new MessagesRequest(
            ClaudeModel,
            [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))],
            OrdinaryMaxTokens,
            Stream: true);

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, context, Services(client), CancellationToken.None);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

        await Assert.That(client.Requests.Count).IsGreaterThan(1);
        await Assert.That(body).Contains("overloaded_error");
    }

    /// <summary>A structured-output turn asks for no reasoning trace.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Nothing reads the trace in front of a JSON object, and on a model that reasons inline it is
    /// generated before the first character of the answer, over a prompt that carries the whole
    /// conversation being judged. A client's evaluator times out waiting, which reads as the
    /// assistant giving up rather than as a turn still coming.
    /// </remarks>
    [Test]
    public async Task StructuredOutputTurnAsksForNoReasoning()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var format = new OutputFormat(Schema: JsonElement.Parse("""{"type":"object"}"""));
        var request = new MessagesRequest(
            ClaudeModel,
            messages,
            OrdinaryMaxTokens,
            OutputConfig: new OutputConfig(Format: format));

        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("{}")))]);

        var client = new FakeNimClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        await Assert.That(client.Requests.Count).IsEqualTo(1);

        var sent = client.Requests[0];
        await Assert.That(sent.ReasoningEffort).IsNull();
        await Assert.That(sent.ResponseFormat).IsNotNull();
    }

    /// <summary>An upstream credential failure is not reported as the caller's own.</summary>
    /// <param name="upstreamStatus">The status NIM rejected the proxy's key with.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect. The key NIM rejected is the gateway's, but the
    /// status was forwarded verbatim, so a coding client read a 403 on its own key and stopped to
    /// ask the user to log in again — over an operator's expired NVIDIA key that logging in cannot
    /// touch.
    /// </remarks>
    [Test]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task UpstreamCredentialFailureIsReportedAsAGatewayFailure(HttpStatusCode upstreamStatus)
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var client = new FakeNimClient { OnSendChat = _ => new(upstreamStatus) { Content = new StringContent("Authorization failed") } };

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<ErrorResponse>)result!;
        await Assert.That(typed.StatusCode).IsEqualTo(StatusCodes.Status502BadGateway);
        await Assert.That(typed.Value!.Error.Type).IsEqualTo("api_error");
        await Assert.That(typed.Value.Error.Message).Contains("proxy's own API key");
    }

    /// <summary>A transport failure reaching the upstream at all becomes a clean Anthropic error.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect: a dropped connection or the internal
    /// header-wait timeout firing reached Kestrel as an unhandled exception, which a real client
    /// saw as the connection dying rather than a turn it could act on.
    /// </remarks>
    [Test]
    public async Task TransportFailureReachingTheUpstreamBecomesAnAnthropicError()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var client = new FakeNimClient { OnSendChat = static _ => throw new IOException("Simulated transport failure.") };

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<ErrorResponse>)result!;
        await Assert.That(typed.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>A transport failure reading a non-streamed body becomes a clean Anthropic error.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TransportFailureReadingTheNonStreamedBodyBecomesAnAnthropicError()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.OK) { Content = new ThrowingHttpContent() } };

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<ErrorResponse>)result!;
        await Assert.That(typed.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>A transport failure reading the body of a streamed turn writes a clean error event.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task TransportFailureReadingTheStreamedBodyWritesAnErrorEvent()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens, Stream: true);
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.OK) { Content = new ThrowingHttpContent() } };
        var context = Context();

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, context, Services(client), CancellationToken.None);

        await Assert.That(result).IsNull();
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        await Assert.That(body).Contains("event: error");
        await Assert.That(body).Contains("overloaded_error");
    }

    /// <summary>A transport failure reading a failed upstream's error body still reports the real status.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TransportFailureReadingAFailedUpstreamBodyFallsBackToTheStatus()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.InternalServerError) { Content = new ThrowingHttpContent() } };

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client), CancellationToken.None);

        var typed = (JsonHttpResult<ErrorResponse>)result!;
        await Assert.That(typed.StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
    }

    /// <summary>A streamed turn forwards the upstream's server-sent events through the translator.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedTurnForwardsTranslatedUpstreamEvents()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens, Stream: true);
        const string Sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\ndata: [DONE]\n\n";
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.OK) { Content = new StringContent(Sse, Encoding.UTF8) } };
        var context = Context();

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, context, Services(client), CancellationToken.None);

        await Assert.That(result).IsNull();
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        await Assert.That(body).Contains("content_block_delta");
    }

    /// <summary>A real turn logs which upstream model it was routed to.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// This is a regression test for a real gap: nothing distinguished a request that never
    /// reached the proxy from one that reached it and was rejected -- both looked like silence
    /// in the log. This pins that a turn is recorded before its outcome is known.
    /// </remarks>
    [Test]
    public async Task RealTurnLogsWhereItWasRouted()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText("hi")))]);
        var client = new FakeNimClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };
        var logger = new CapturingLogger<MessageServices>();

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client, logger), CancellationToken.None);

        await Assert.That(logger.Messages.Exists(static m =>
                m.Contains(ClaudeModel, StringComparison.Ordinal) && m.Contains(UpstreamModel, StringComparison.Ordinal)))
            .IsTrue();
    }

    /// <summary>A rejected upstream call logs the status it was rejected with.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RejectedUpstreamCallLogsTheStatus()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var client = new FakeNimClient { OnSendChat = static _ => new(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") } };
        var logger = new CapturingLogger<MessageServices>();

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client, logger), CancellationToken.None);

        await Assert.That(logger.Messages.Exists(static m => m.Contains("500", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>Builds an HTTP context with a memory-backed response body.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext Context() => new() { Response = { Body = new MemoryStream() } };

    /// <summary>Builds the services a turn is served from, wired to a fake upstream client.</summary>
    /// <param name="client">The fake upstream client.</param>
    /// <returns>The services.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MessageServices Services(FakeNimClient client) =>
        new(
            new FakeModelRouter(new(ClaudeModel, UpstreamModel, ModelTier.Sonnet, true)),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            new RetryOptions(),
            new ModelCatalogOptions(),
            new OptimizationOptions(),
            new HttpTimeoutOptions(),
            NullLogger<MessageServices>.Instance);

    /// <summary>Builds the services a turn is served from, wired to a fake upstream client and a capturing logger.</summary>
    /// <param name="client">The fake upstream client.</param>
    /// <param name="logger">The logger every message is captured into.</param>
    /// <returns>The services.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MessageServices Services(FakeNimClient client, CapturingLogger<MessageServices> logger) =>
        new(
            new FakeModelRouter(new(ClaudeModel, UpstreamModel, ModelTier.Sonnet, true)),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            new RetryOptions(),
            new ModelCatalogOptions(),
            new OptimizationOptions(),
            new HttpTimeoutOptions(),
            logger);

    /// <summary>Builds a JSON-bodied response for a completed upstream call.</summary>
    /// <summary>Wraps a raw server-sent event body as a successful upstream response.</summary>
    /// <param name="sse">The event body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage SaturatedResponse(string sse) =>
        new(HttpStatusCode.OK) { Content = new StringContent(sse) };

    /// <summary>Wraps a completion as a JSON upstream response.</summary>
    /// <param name="status">The status to return.</param>
    /// <param name="completion">The completion to serialize as the body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, NimChatCompletion completion) =>
        new(status) { Content = JsonContent.Create(completion, ProxyJsonContext.Default.NimChatCompletion) };
}
