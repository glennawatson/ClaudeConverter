// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    /// <summary>The user text shared by the real-turn fixtures.</summary>
    private const string OrdinaryUserText = "hello, how are you?";

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
            Choices: [new NimChoice(Message: new NimChatMessage("assistant", NimContent.FromText("I'm well.")))]);
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

    /// <summary>Builds an HTTP context with a memory-backed response body.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext Context() => new() { Response = { Body = new MemoryStream() } };

    /// <summary>Builds the services a turn is served from, wired to a fake upstream client.</summary>
    /// <param name="client">The fake upstream client.</param>
    /// <returns>The services.</returns>
    private static MessageServices Services(FakeNimClient client) =>
        new(
            new FakeModelRouter(new(ClaudeModel, UpstreamModel, ModelTier.Sonnet, true)),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            new OptimizationOptions(),
            new HttpTimeoutOptions(),
            NullLogger<MessageServices>.Instance);

    /// <summary>Builds a JSON-bodied response for a completed upstream call.</summary>
    /// <param name="status">The status to return.</param>
    /// <param name="completion">The completion to serialize as the body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, NimChatCompletion completion) =>
        new(status) { Content = JsonContent.Create(completion, ProxyJsonContext.Default.NimChatCompletion) };
}
