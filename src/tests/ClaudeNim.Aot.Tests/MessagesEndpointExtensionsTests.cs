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
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
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

    /// <summary>The model the fallback fixtures configure as the tier's alternative.</summary>
    private const string FallbackModel = "nvidia/nemotron-3.5-lightning-30b-a3b";

    /// <summary>The number of upstream calls a turn takes when its first model steps aside for one fallback.</summary>
    private const int CallsAfterOneFallback = 2;

    /// <summary>The number of upstream calls a turn takes when a one-model chain is walked and then waited out.</summary>
    private const int CallsAfterExhaustedChain = 3;

    /// <summary>The number of upstream calls a turn takes when its model cools down mid-stream and falls back.</summary>
    private const int CallsUntilMidStreamFailureCoolsDown = 3;

    /// <summary>The reply text a fallback fixture's upstream answers with.</summary>
    private const string ServedResponseText = "served";

    /// <summary>The reply text a streamed retry fixture's upstream answers with once it succeeds.</summary>
    private const string RecoveredResponseText = "recovered";

    /// <summary>Retry settings whose backoff is short enough that a re-issued turn does not slow the suite.</summary>
    private static readonly RetryOptions FastRetries = new(BaseDelayMilliseconds: 1, MaxDelayMilliseconds: 2, UseJitter: false);

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

        const string Answer = $$$"""
            data: {"choices":[{"delta":{"content":"{{{RecoveredResponseText}}}"}}]}

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
        await Assert.That(body).Contains(RecoveredResponseText);
        await Assert.That(body).DoesNotContain("overloaded");
    }

    /// <summary>Mid-stream transient failures count toward the model's cooldown, not just an outright refusal.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// NVIDIA's saturation is not always visible as a refused status before generation begins: a
    /// model can commit a <c>200</c> and still fail inside the stream, repeatedly, without that
    /// ever surfacing as the kind of failure the chain walk's own cooldown check reacts to. Left
    /// uncounted, a turn would keep re-issuing to the exact model that just failed until the whole
    /// retry budget was spent, instead of falling back once the model proves itself unavailable.
    /// </remarks>
    [Test]
    public async Task MidStreamTransientFailuresCountTowardCooldown()
    {
        const string Overloaded = """
            data: {"error":{"message":"Service temporarily overloaded","code":503}}

            data: [DONE]

            """;

        const string Answer = $$$"""
            data: {"choices":[{"delta":{"content":"{{{RecoveredResponseText}}}"}}]}

            data: [DONE]

            """;

        var modelHealth = new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System);
        var client = new FakeNimClient { OnSendChat = static request => SaturatedResponse(request.Model == UpstreamModel ? Overloaded : Answer) };

        var context = Context();
        var request = new MessagesRequest(
            ClaudeModel,
            [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))],
            OrdinaryMaxTokens,
            Stream: true);

        _ = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            context,
            Services(client, FallbackModel, modelHealth),
            CancellationToken.None);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

        await Assert.That(client.Requests.Count).IsEqualTo(CallsUntilMidStreamFailureCoolsDown);
        await Assert.That(client.Requests[0].Model).IsEqualTo(UpstreamModel);
        await Assert.That(client.Requests[1].Model).IsEqualTo(UpstreamModel);
        await Assert.That(client.Requests[2].Model).IsEqualTo(FallbackModel);
        await Assert.That(modelHealth.IsInCooldown(UpstreamModel)).IsTrue();
        await Assert.That(body).Contains(RecoveredResponseText);
    }

    /// <summary>A streamed rejection is downgraded and retried at the same model, not the whole retry budget.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A streamed call's own status is committed before generation begins, so a rejection NVIDIA
    /// discovers afterwards arrives as an error payload inside an already-successful response
    /// rather than as a status code. Retrying that with the identical body cannot succeed; this
    /// covers that the reasoning controls are stripped and the same model asked again, the way the
    /// non-streamed path already reacts to the same rejection arriving as a real status code.
    /// </remarks>
    [Test]
    public async Task StreamedRejectionIsDowngradedAndRetriedAtSameModel()
    {
        const string Rejected = """
            data: {"error":{"message":"Invalid request","code":400}}

            data: [DONE]

            """;

        const string Answer = $$$"""
            data: {"choices":[{"delta":{"content":"{{{RecoveredResponseText}}}"}}]}

            data: [DONE]

            """;

        var client = new FakeNimClient { OnSendChat = static request => SaturatedResponse(request.ChatTemplateKwargs is null ? Answer : Rejected) };

        var context = Context();
        var request = new MessagesRequest(
            ClaudeModel,
            [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))],
            OrdinaryMaxTokens,
            Stream: true);

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, context, Services(client), CancellationToken.None);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneStreamRetry);
        await Assert.That(client.Requests[0].Model).IsEqualTo(UpstreamModel);
        await Assert.That(client.Requests[1].Model).IsEqualTo(UpstreamModel);
        await Assert.That(client.Requests[1].ChatTemplateKwargs).IsNull();
        await Assert.That(body).Contains(RecoveredResponseText);
    }

    /// <summary>A turn the routed model cannot serve is served by the next model in the chain.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// NVIDIA's free tier saturates one model at a time and rotates which one through the day, so
    /// the turn the routed model has to refuse is very often one the next model answers in full.
    /// </remarks>
    [Test]
    public async Task UnavailableModelIsReplacedByItsFallback()
    {
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);

        var client = new FakeNimClient
        {
            OnSendChat = request => request.Model == UpstreamModel
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : JsonResponse(HttpStatusCode.OK, completion),
        };

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        var result = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            Context(),
            Services(client, FallbackModel),
            CancellationToken.None);

        var message = ((JsonHttpResult<MessagesResponse>)result!).Value!;

        await Assert.That(message.Content[0].Text).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(CallsAfterOneFallback);
        await Assert.That(client.Requests[1].Model).IsEqualTo(FallbackModel);
    }

    /// <summary>A fallback entry naming a provider dispatches to that provider, not NIM.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is the general mechanism a deployment mixes providers with: an ordinary entry in the
    /// same configured chain, walked exactly like a NIM candidate, just addressed elsewhere — no
    /// special-cased "local last resort" step exists separately from this.
    /// </remarks>
    [Test]
    public async Task FallbackEntryNamingOllamaDispatchesToTheOllamaClient()
    {
        const string OllamaFallback = "ollama:qwen3-coder:30b";
        const string OllamaBareModel = "qwen3-coder:30b";

        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        var client = new FakeNimClient { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        var ollama = new FakeOpenAiCompatibleClient { OnSendChat = _ => JsonResponse(HttpStatusCode.OK, completion) };

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        var result = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            Context(),
            Services(client, OllamaFallback, ollama),
            CancellationToken.None);

        var message = ((JsonHttpResult<MessagesResponse>)result!).Value!;

        await Assert.That(message.Content[0].Text).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(1);
        await Assert.That(ollama.Requests.Count).IsEqualTo(1);
        await Assert.That(ollama.Requests[0].Model).IsEqualTo(OllamaBareModel);
    }

    /// <summary>The fallback warning carries the upstream's own body, not just the status.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A bare status code says a model was unavailable but not why, which is exactly the question
    /// asked when the same status recurs across a cascade of turns. NVIDIA's own explanation is
    /// read and logged here so the journal answers that question without a live repro.
    /// </remarks>
    [Test]
    public async Task FallbackWarningIncludesTheUpstreamBody()
    {
        const string UpstreamDetail = "GPU capacity exhausted for this route";

        var client = new FakeNimClient
        {
            OnSendChat = static request => request.Model == UpstreamModel
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(UpstreamDetail) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
        };
        var logger = new CapturingLogger<MessageServices>();

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        _ = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            Context(),
            Services(client, FallbackModel, logger),
            CancellationToken.None);

        var fellBack = logger.Entries.Find(static entry => entry.Message.Contains(UpstreamDetail, StringComparison.Ordinal));

        await Assert.That(fellBack.Message).IsNotNull();
    }

    /// <summary>Walking the chain spends one attempt a model, not the whole retry budget.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Waiting out a busy model cannot beat asking one that is not busy, so the chain is walked
    /// first and quickly. Only once every model has refused is the routed one waited on with the
    /// full budget, which is the behaviour of a deployment that configures no chain at all.
    /// </remarks>
    [Test]
    public async Task ChainIsWalkedOnOneAttemptEachThenWaitedOut()
    {
        var client = new FakeNimClient { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        _ = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            Context(),
            Services(client, FallbackModel),
            CancellationToken.None);

        await Assert.That(client.Budgets.Count).IsEqualTo(CallsAfterExhaustedChain);
        await Assert.That(client.Budgets[0]).IsEqualTo(1);
        await Assert.That(client.Budgets[1]).IsEqualTo(1);
        await Assert.That(client.Budgets[2]).IsGreaterThan(1);
        await Assert.That(client.Requests[2].Model).IsEqualTo(UpstreamModel);
    }

    /// <summary>A model that has failed repeatedly across turns is skipped, without an upstream call, until its cooldown lifts.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A model that has gone inactive for a while otherwise costs every turn a wasted call and a
    /// retry wait before the chain reaches a model that is actually up. Once the routed model has
    /// failed enough times in a row, a later turn should go straight to its fallback instead.
    /// </remarks>
    [Test]
    public async Task RepeatedlyFailingModelIsSkippedWithoutAnUpstreamCall()
    {
        var modelHealth = new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System);
        var completion = new NimChatCompletion(
            Choices: [new NimChoice(Message: new NimChatMessage(AssistantRole, NimContent.FromText(ServedResponseText)))]);
        var client = new FakeNimClient
        {
            OnSendChat = request => request.Model == UpstreamModel
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse(HttpStatusCode.OK, completion),
        };
        var services = Services(client, FallbackModel, modelHealth);
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), services, CancellationToken.None);
        _ = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), services, CancellationToken.None);
        var callsBeforeCooldown = client.Requests.Count;

        var result = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), services, CancellationToken.None);

        var message = ((JsonHttpResult<MessagesResponse>)result!).Value!;
        await Assert.That(message.Content[0].Text).IsEqualTo(ServedResponseText);
        await Assert.That(client.Requests.Count).IsEqualTo(callsBeforeCooldown + 1);
        await Assert.That(client.Requests[^1].Model).IsEqualTo(FallbackModel);
    }

    /// <summary>A refused credential is reported at error, where every other rejection is a warning.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// It is the one failure here an operator has to act on: the key being refused is the proxy's
    /// own, so no turn will succeed until it is replaced. The journal colours and filters on the
    /// level, which is what makes that distinction visible at a glance.
    /// </remarks>
    [Test]
    public async Task RefusedCredentialIsReportedAsAnError()
    {
        var client = new FakeNimClient { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("no key") } };
        var logger = new CapturingLogger<MessageServices>();

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        _ = await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client, logger), CancellationToken.None);

        var refused = logger.Entries.Find(static entry => entry.Message.Contains("own credential", StringComparison.Ordinal));

        await Assert.That(refused.Message).IsNotNull();
        await Assert.That(refused.Level).IsEqualTo(LogLevel.Error);
    }

    /// <summary>A client that abandons a turn mid-fallback is reported against the model actually in flight.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect: the abandonment line named the model routing
    /// started at, not the fallback model the client was actually left waiting on, because
    /// deconstructing the fallback call's result into <c>turn</c> never runs when that call throws.
    /// </remarks>
    [Test]
    public async Task ClientAbandonmentDuringFallbackNamesTheModelInFlight()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeNimClient();
        client.OnSendChat = _ =>
        {
            if (client.Requests.Count == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            cts.Cancel();
            throw new TaskCanceledException();
        };

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);
        var logger = new CapturingLogger<MessageServices>();

        await Assert.That(async () =>
                await MessagesEndpointExtensions.SendMessageAsync(request, Context(), Services(client, FallbackModel, logger), cts.Token))
            .Throws<TaskCanceledException>();

        var abandoned = logger.Messages.Find(static m => m.Contains("gave up", StringComparison.Ordinal));

        await Assert.That(abandoned).IsNotNull();
        await Assert.That(abandoned!).Contains(FallbackModel);
        await Assert.That(abandoned).DoesNotContain(UpstreamModel);
    }

    /// <summary>A spent chain says so, rather than leaving the log at the last model it tried.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Without this line the log ends on "trying X instead" and then shows retries against the
    /// model the turn started at, which reads as though the fallback never happened at all.
    /// </remarks>
    [Test]
    public async Task ExhaustedChainIsReportedBeforeWaitingOnTheRoutedModel()
    {
        var client = new FakeNimClient { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        var logger = new CapturingLogger<MessageServices>();

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        _ = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            Context(),
            Services(client, FallbackModel, logger),
            CancellationToken.None);

        var spent = logger.Messages.Find(static m => m.Contains("were unavailable", StringComparison.Ordinal));

        await Assert.That(spent).IsNotNull();
        await Assert.That(spent!).Contains(UpstreamModel);
    }

    /// <summary>A rejected request is not walked down the chain to be rejected again.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A rejection is a property of the body, not of the model's availability. The next model
    /// would refuse the same body for the same reason, so the chain buys nothing but latency.
    /// </remarks>
    [Test]
    public async Task RejectedRequestIsNotWalkedDownTheChain()
    {
        var client = new FakeNimClient { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("no") } };

        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(OrdinaryUserText))];
        var request = new MessagesRequest(ClaudeModel, messages, OrdinaryMaxTokens);

        var result = await MessagesEndpointExtensions.SendMessageAsync(
            request,
            Context(),
            Services(client, FallbackModel),
            CancellationToken.None);

        await Assert.That(((JsonHttpResult<ErrorResponse>)result!).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(client.Requests.Count).IsEqualTo(1);
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
            FastRetries,
            new ModelCatalogOptions(),
            new AnthropicRequestOptimizer(new OptimizationOptions()),
            new AnthropicCompletionTranslator(NullLogger<AnthropicCompletionTranslator>.Instance),
            new AnthropicStreamTranslatorFactory(),
            new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System),
            new HttpTimeoutOptions(),
            new FakeOpenAiCompatibleClient(),
            new FakeOpenAiCompatibleClient(),
            TimeProvider.System,
            NullLogger<MessageServices>.Instance);

    /// <summary>Builds the services a turn is served from, with a fallback chain behind the routed model.</summary>
    /// <param name="client">The fake upstream client.</param>
    /// <param name="fallback">The model the routed one steps aside for.</param>
    /// <param name="logger">The log the turn writes to.</param>
    /// <returns>The services.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MessageServices Services(FakeNimClient client, string fallback, ILogger<MessageServices>? logger = null) =>
        new(
            new FakeModelRouter(new(ClaudeModel, UpstreamModel, ModelTier.Sonnet, true, [fallback])),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            FastRetries,
            new ModelCatalogOptions(),
            new AnthropicRequestOptimizer(new OptimizationOptions()),
            new AnthropicCompletionTranslator(NullLogger<AnthropicCompletionTranslator>.Instance),
            new AnthropicStreamTranslatorFactory(),
            new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System),
            new HttpTimeoutOptions(),
            new FakeOpenAiCompatibleClient(),
            new FakeOpenAiCompatibleClient(),
            TimeProvider.System,
            logger ?? NullLogger<MessageServices>.Instance);

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
            FastRetries,
            new ModelCatalogOptions(),
            new AnthropicRequestOptimizer(new OptimizationOptions()),
            new AnthropicCompletionTranslator(NullLogger<AnthropicCompletionTranslator>.Instance),
            new AnthropicStreamTranslatorFactory(),
            new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System),
            new HttpTimeoutOptions(),
            new FakeOpenAiCompatibleClient(),
            new FakeOpenAiCompatibleClient(),
            TimeProvider.System,
            logger);

    /// <summary>Builds the services a turn is served from, with a fallback chain and a caller-supplied health tracker.</summary>
    /// <param name="client">The fake upstream client.</param>
    /// <param name="fallback">The model the routed one steps aside for.</param>
    /// <param name="modelHealth">The tracker to share across the calls under test.</param>
    /// <returns>The services.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MessageServices Services(FakeNimClient client, string fallback, IModelHealthTracker modelHealth) =>
        new(
            new FakeModelRouter(new(ClaudeModel, UpstreamModel, ModelTier.Sonnet, true, [fallback])),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            FastRetries,
            new ModelCatalogOptions(),
            new AnthropicRequestOptimizer(new OptimizationOptions()),
            new AnthropicCompletionTranslator(NullLogger<AnthropicCompletionTranslator>.Instance),
            new AnthropicStreamTranslatorFactory(),
            modelHealth,
            new HttpTimeoutOptions(),
            new FakeOpenAiCompatibleClient(),
            new FakeOpenAiCompatibleClient(),
            TimeProvider.System,
            NullLogger<MessageServices>.Instance);

    /// <summary>Builds the services a turn is served from, with a fallback chain and a caller-supplied Ollama client.</summary>
    /// <param name="client">The fake NIM upstream client.</param>
    /// <param name="fallback">The model the routed one steps aside for, which may name a non-NIM provider.</param>
    /// <param name="ollamaClient">The client a fallback entry naming Ollama dispatches to.</param>
    /// <returns>The services.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MessageServices Services(FakeNimClient client, string fallback, FakeOpenAiCompatibleClient ollamaClient) =>
        new(
            new FakeModelRouter(new(ClaudeModel, UpstreamModel, ModelTier.Sonnet, true, [fallback])),
            client,
            new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0)),
            new NvidiaNimOptions(),
            FastRetries,
            new ModelCatalogOptions(),
            new AnthropicRequestOptimizer(new OptimizationOptions()),
            new AnthropicCompletionTranslator(NullLogger<AnthropicCompletionTranslator>.Instance),
            new AnthropicStreamTranslatorFactory(),
            new ModelHealthTracker(new ModelHealthOptions(), TimeProvider.System),
            new HttpTimeoutOptions(),
            ollamaClient,
            new FakeOpenAiCompatibleClient(),
            TimeProvider.System,
            NullLogger<MessageServices>.Instance);

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
