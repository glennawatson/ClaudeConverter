// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>The Messages API, which is the route a Claude session actually talks through.</summary>
/// <remarks>
/// <para>
/// A turn takes one of three paths. A housekeeping request is answered locally; a streamed turn is
/// forwarded delta by delta; anything else is translated once the upstream has finished.
/// </para>
/// <para>
/// Streaming is handled by writing the events directly to the response body rather than by
/// returning a result object, because the whole point is that the first token reaches the client
/// before the last one exists.
/// </para>
/// </remarks>
public static class MessagesEndpointExtensions
{
    /// <summary>The content type a Claude client expects a streamed turn to arrive as.</summary>
    private const string EventStreamContentType = "text/event-stream";

    /// <summary>The longest run of a rejected upstream body that is written to the log.</summary>
    private const int LoggedBodyLength = 400;

    /// <summary>The Messages API route.</summary>
    /// <param name="endpoints">The route builder the endpoint is added to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers the Messages API route.</summary>
        /// <returns>The group the endpoint was registered in.</returns>
        public RouteGroupBuilder MapMessagesEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup("/v1/messages").WithTags("Messages");

            _ = group.MapPost("/", SendMessageAsync).WithName(nameof(SendMessageAsync));

            return group;
        }
    }

    /// <summary>Serves one Messages API turn.</summary>
    /// <param name="request">The caller's request.</param>
    /// <param name="context">The HTTP context a streamed turn is written to.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The response, or <see langword="null"/> once a streamed turn has been written.</returns>
    internal static async Task<IResult?> SendMessageAsync(
        MessagesRequest request,
        HttpContext context,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        if (InvalidRequest(request) is { } invalid)
        {
            return invalid;
        }

        var messageId = $"msg_{Guid.NewGuid():N}";

        // Every line this turn writes is nested inside the scope, including the ones written
        // deeper down by the client and the translator, which are never handed the identifier.
        using var scope = NvidiaLog.BeginTurn(services.Logger, messageId, request.Model);

        if (services.Optimizer.TryAnswer(request, out var canned))
        {
            return await AnswerLocallyAsync(request, context, messageId, canned, cancellationToken)
                .ConfigureAwait(false);
        }

        var turn = Route(request, messageId, services);

        using var lease = await services.Gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            NvidiaLog.RateLimitSaturated(services.Logger, turn.Resolved.NimModel);
            return AnthropicErrors.Result(
                StatusCodes.Status429TooManyRequests,
                "The proxy's own rate limit is saturated; retry shortly.");
        }

        NvidiaLog.SendingTurn(services.Logger, request.Model, turn.Resolved.NimModel, request.IsStreaming);
        LogUpstreamRequestBody(services.Logger, turn.Resolved.NimModel, turn.UpstreamRequest);

        // Measured around the whole call, retries and downgrades included, so the elapsed time is
        // what the client actually waited rather than what the last attempt took.
        var started = Stopwatch.GetTimestamp();

        // Walking the fallback chain changes which model is actually in flight, and a client can
        // cancel while any one of them is being asked. Deconstruction only updates turn once
        // SendWithFallbackAsync returns, so a cancellation mid-chain would otherwise be reported
        // against the model routing started at rather than the one the client was actually left
        // waiting on.
        var inFlightModel = turn.Resolved.NimModel;

        HttpResponseMessage response;
        try
        {
            (turn, response) = await SendWithFallbackAsync(turn, services, model => inFlightModel = model, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nothing else records this. Every catch below steps aside for the client's own
            // cancellation, so a turn abandoned while the upstream was still thinking left the
            // log showing only that it was sent.
            NvidiaLog.ClientAbandonedTurn(
                services.Logger,
                inFlightModel,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, turn.Resolved.NimModel, error);
            return AnthropicErrors.Result(
                StatusCodes.Status503ServiceUnavailable,
                "The upstream connection failed or timed out before a response arrived.");
        }

        using (response)
        {
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            NvidiaLog.TurnAnswered(services.Logger, turn.Resolved.NimModel, (int)response.StatusCode, elapsed);

            return await DispatchAsync(response, context, turn, services, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Decides which model serves a turn, and builds the body it will be sent as.</summary>
    /// <param name="request">The caller's request.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <returns>The turn, ready to send.</returns>
    private static TurnContext Route(MessagesRequest request, string messageId, MessageServices services)
    {
        var resolved = WithoutReasoningForMachines(services.Router.Resolve(request.Model), request);
        var bareModel = UpstreamModelId.Parse(resolved.NimModel).Model;

        return new(
            request,
            NimRequestBuilder.Build(
                request,
                bareModel,
                resolved.ThinkingEnabled && !NimModelCatalogDefaults.IsKnownToRejectThinking(bareModel),
                services.Nim,
                services.Catalog.DefaultMaxOutputTokens),
            resolved,
            messageId);
    }

    /// <summary>Sends a turn, walking the tier's fallback chain while the upstream is unavailable.</summary>
    /// <param name="turn">The turn being served, as routing left it.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="reportAttempt">Told the model about to be asked, before each attempt that may be cancelled.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The turn as the model that answered it left it, and that model's response.</returns>
    /// <remarks>
    /// <para>
    /// NVIDIA's free tier saturates one model at a time, and which one rotates through the day —
    /// so a turn that cannot be served by the model it routed to can very often be served in full
    /// by the next one down. That is only worth anything if the switch is quick: each candidate
    /// gets a single attempt here, because waiting out a busy model cannot beat asking one that is
    /// not busy, and the whole chain is walked in about as long as one retry would have taken.
    /// </para>
    /// <para>
    /// When every model in the chain is unavailable, the turn goes back to the one it routed to
    /// with the full retry budget. Patience is the right answer once there is nowhere else to go,
    /// and that is the behaviour a deployment configuring no chain at all keeps.
    /// </para>
    /// </remarks>
    private static async Task<TurnAttempt> SendWithFallbackAsync(
        TurnContext turn,
        MessageServices services,
        Action<string> reportAttempt,
        CancellationToken cancellationToken)
    {
        var alternatives = turn.Resolved.Alternatives;

        if (alternatives.Count == 0)
        {
            reportAttempt(turn.Resolved.NimModel);
            return new(turn, await SendDirectAsync(turn, services, cancellationToken).ConfigureAwait(false));
        }

        var current = turn;

        for (var candidate = 0; candidate < alternatives.Count; candidate++)
        {
            reportAttempt(current.Resolved.NimModel);
            var attempt = await TryOnceAsync(current, services, cancellationToken).ConfigureAwait(false);

            if (attempt.Response is { } answered)
            {
                return new(current, answered);
            }

            var next = alternatives[candidate];

            if (attempt.InCooldown)
            {
                NvidiaLog.SkippingCoolingDownModel(services.Logger, current.Resolved.NimModel, next);
            }
            else
            {
                NvidiaLog.FallingBackToAnotherModel(services.Logger, current.Resolved.NimModel, next, attempt.Status, attempt.Body ?? string.Empty);
            }

            current = Rerouted(turn, services, next, Remaining(alternatives, candidate + 1));
        }

        reportAttempt(current.Resolved.NimModel);
        var last = await TryOnceAsync(current, services, cancellationToken).ConfigureAwait(false);

        if (last.Response is { } served)
        {
            return new(current, served);
        }

        // Everything was busy, or every candidate was cooling down from having just been. The
        // model the turn was actually routed to is the one worth waiting for, rather than
        // whichever happened to be last in the chain — and asking it directly here, rather than
        // through TryOnceAsync, is deliberate: cooldown only ever governs the fast walk above, not
        // the last resort, so a chain that looks entirely dead is still asked for real rather than
        // given up on outright.
        NvidiaLog.EveryModelUnavailable(services.Logger, turn.Resolved.NimModel, alternatives.Count + 1);

        reportAttempt(turn.Resolved.NimModel);
        return new(turn, await SendDirectAsync(turn, services, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Sends a turn to the model it names directly, bypassing cooldown, and records the outcome.</summary>
    /// <param name="turn">The turn to send.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The upstream response.</returns>
    private static async Task<HttpResponseMessage> SendDirectAsync(TurnContext turn, MessageServices services, CancellationToken cancellationToken)
    {
        var response = await SendUpstreamAsync(turn, services, services.Retries.MaxAttempts, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            // See TryOnceAsync's own comment: a streamed 200 is not yet a finished turn, so
            // marking available here is left to whoever sees the stream through to its end.
            if (!turn.Request.IsStreaming)
            {
                services.ModelHealth.MarkAvailable(turn.Resolved.NimModel);
            }
        }
        else
        {
            services.ModelHealth.MarkUnavailable(turn.Resolved.NimModel);
        }

        return response;
    }

    /// <summary>Dispatches a call to whichever transport the turn's model identifier names.</summary>
    /// <param name="turn">The turn to send.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="maxAttempts">The attempt budget a NIM call may spend on a dropped connection.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream response.</returns>
    /// <remarks>
    /// This is the one place a model identifier's provider prefix is read to decide where a call
    /// actually goes; everywhere else, a turn just carries a string. Ollama and an OpenAI-compatible
    /// endpoint take no attempt budget of their own — neither has NIM's retry ladder — so it is only
    /// meaningful on the NIM branch.
    /// </remarks>
    private static Task<HttpResponseMessage> SendUpstreamAsync(
        TurnContext turn,
        MessageServices services,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var parsed = UpstreamModelId.Parse(turn.Resolved.NimModel);

        return parsed.Provider switch
        {
            UpstreamProvider.Ollama => services.OllamaClient.SendChatAsync(turn.UpstreamRequest, cancellationToken),
            UpstreamProvider.OpenAi => services.OpenAiClient.SendChatAsync(turn.UpstreamRequest, cancellationToken),
            _ => services.Client.SendChatAsync(turn.UpstreamRequest, turn.Resolved.Tier, maxAttempts, cancellationToken).AsTask(),
        };
    }

    /// <summary>Makes one attempt at a turn, reporting an unavailable model as no answer at all.</summary>
    /// <param name="turn">The turn to attempt.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The response, or <see langword="null"/> when this model cannot serve the turn now.</returns>
    /// <remarks>
    /// A rejection is not an unavailable model and is returned as it stands: the next model would
    /// reject the same body for the same reason, and walking a chain to collect three identical
    /// 400s helps no one.
    /// </remarks>
    private static async Task<ModelAttempt> TryOnceAsync(
        TurnContext turn,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        var model = turn.Resolved.NimModel;

        if (services.ModelHealth.IsInCooldown(model))
        {
            return new(null, 0, InCooldown: true);
        }

        HttpResponseMessage response;
        try
        {
            response = await SendUpstreamAsync(turn, services, maxAttempts: 1, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            // A model that never answered is as unavailable as one that said so.
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, model, error);
            services.ModelHealth.MarkUnavailable(model);
            return new(null, 0);
        }

        // A streamed call's 200 only means generation started, not that it finished: NVIDIA can
        // still fail it from inside the body, which the caller learns about long after this
        // returns. Marking the model available here would erase that later failure's own mark the
        // moment this same model is asked again, so a streamed call leaves marking available to
        // whoever actually saw the stream through to its end.
        if (response.IsSuccessStatusCode && !turn.Request.IsStreaming)
        {
            services.ModelHealth.MarkAvailable(model);
        }

        if (!RetrySchedule.IsTransient(response.StatusCode))
        {
            return new(response, (int)response.StatusCode);
        }

        var status = (int)response.StatusCode;
        var body = Truncated(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        response.Dispose();
        services.ModelHealth.MarkUnavailable(model);

        return new(null, status, Body: body);
    }

    /// <summary>Rebuilds a turn around the next model in its fallback chain.</summary>
    /// <param name="turn">The turn as routing left it.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="next">The model to address it to.</param>
    /// <param name="remaining">What is left of the chain after that model.</param>
    /// <returns>The turn, addressed to the next model.</returns>
    /// <remarks>
    /// The body is built again rather than having its model swapped, because a request is shaped
    /// for the model it is bound for: its output ceiling comes from that model's own, an image is
    /// forwarded or placeholdered according to whether that model has vision, and its reasoning
    /// controls are the ones that model's chat template takes.
    /// </remarks>
    private static TurnContext Rerouted(
        TurnContext turn,
        MessageServices services,
        string next,
        IReadOnlyList<string> remaining)
    {
        var resolved = turn.Resolved with { NimModel = next, Fallbacks = remaining };
        var bareModel = UpstreamModelId.Parse(next).Model;

        return turn with
        {
            Resolved = resolved,
            UpstreamRequest = NimRequestBuilder.Build(
                turn.Request,
                bareModel,
                resolved.ThinkingEnabled && !NimModelCatalogDefaults.IsKnownToRejectThinking(bareModel),
                services.Nim,
                services.Catalog.DefaultMaxOutputTokens),
        };
    }

    /// <summary>Takes the part of a chain that has not been tried yet.</summary>
    /// <param name="alternatives">The chain being walked.</param>
    /// <param name="candidate">The one-based position just moved to.</param>
    /// <returns>The models after that position.</returns>
    /// <remarks>
    /// A re-issued streamed turn walks the chain again from wherever it got to, so the models
    /// already found unavailable are dropped rather than asked a second time.
    /// </remarks>
    private static List<string> Remaining(IReadOnlyList<string> alternatives, int candidate)
    {
        List<string> rest = [];

        for (var i = candidate; i < alternatives.Count; i++)
        {
            rest.Add(alternatives[i]);
        }

        return rest;
    }

    /// <summary>Hands a successful upstream response to the translator its shape calls for.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="context">The HTTP context being answered.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The result to return, or <see langword="null"/> once a streamed body has been written.</returns>
    private static async Task<IResult?> DispatchAsync(
        HttpResponseMessage response,
        HttpContext context,
        TurnContext turn,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return await UpstreamFailureAsync(response, turn.Resolved.NimModel, services.Logger, cancellationToken).ConfigureAwait(false);
        }

        return turn.Request.IsStreaming
            ? await StreamAsync(response, context, turn, services, cancellationToken).ConfigureAwait(false)
            : await CompleteAsync(response, turn, services, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Logs the exact request sent upstream, when debug logging is enabled.</summary>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="nimModel">The NIM model the request is bound for.</param>
    /// <param name="upstreamRequest">The request about to be sent.</param>
    /// <remarks>
    /// Serializing a tool-bearing request is not free, so the check comes first rather than relying
    /// on the generated log method's own internal one -- that still evaluates this argument eagerly
    /// before the call, since it is a plain string parameter.
    /// </remarks>
    private static void LogUpstreamRequestBody(ILogger logger, string nimModel, NimChatRequest upstreamRequest)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var body = JsonSerializer.Serialize(upstreamRequest, ProxyJsonContext.Default.NimChatRequest);
        NvidiaLog.UpstreamRequestBody(logger, nimModel, body);
    }

    /// <summary>Determines whether an exception represents a failed or timed-out upstream connection.</summary>
    /// <param name="error">The exception the upstream call raised.</param>
    /// <returns><see langword="true"/> when the exception is a transport-level failure rather than a caller cancellation.</returns>
    /// <remarks>
    /// <see cref="NimClient"/> bounds its own call with an internal deadline, so a slow or dropped
    /// upstream surfaces here as <see cref="OperationCanceledException"/> or
    /// <see cref="HttpRequestException"/> rather than a non-success status code. Left uncaught, this
    /// reached Kestrel as an unhandled exception and the client saw a bare connection failure instead
    /// of an <c>overloaded_error</c> it could back off on.
    /// </remarks>
    private static bool IsUpstreamTransportFailure(Exception error) =>
        error is HttpRequestException or OperationCanceledException or IOException;

    /// <summary>Validates that a request carries the fields every turn needs.</summary>
    /// <param name="request">The caller's request, which may be null or incompletely bound.</param>
    /// <returns>An error result when the request cannot be served, or <see langword="null"/> when it can.</returns>
    /// <remarks>
    /// JSON binding does not enforce <c>model</c> or <c>messages</c> as required: a body missing
    /// either still binds successfully with that member left null, which crashed deeper in the
    /// pipeline rather than producing the client-facing error the request had actually earned.
    /// </remarks>
    private static IResult? InvalidRequest(MessagesRequest? request)
    {
        if (request is null)
        {
            return AnthropicErrors.Result(
                StatusCodes.Status400BadRequest,
                "The request body was missing or unreadable.");
        }

        if (string.IsNullOrEmpty(request.Model))
        {
            return AnthropicErrors.Result(StatusCodes.Status400BadRequest, "The request did not name a model.");
        }

        return request.Messages is { Count: > 0 }
            ? null
            : AnthropicErrors.Result(StatusCodes.Status400BadRequest, "The request carried no messages.");
    }

    /// <summary>Answers a housekeeping request without calling the upstream.</summary>
    /// <param name="request">The caller's request.</param>
    /// <param name="context">The HTTP context a streamed turn is written to.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="text">The canned answer.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The response, or <see langword="null"/> once a streamed turn has been written.</returns>
    private static async Task<IResult?> AnswerLocallyAsync(
        MessagesRequest request,
        HttpContext context,
        string messageId,
        string text,
        CancellationToken cancellationToken)
    {
        var inputTokens = TokenEstimator.Estimate(request.Messages, request.System, request.Tools);
        var outputTokens = TokenEstimator.FromText(text);

        if (!request.IsStreaming)
        {
            MessagesResponse answer = new(
                messageId,
                request.Model,
                [ContentBlock.ForText(text)],
                StopReasons.EndTurn,
                new TokenUsage(inputTokens, outputTokens));

            return TypedResults.Json(answer, ProxyJsonContext.Default.MessagesResponse);
        }

        var writer = await BeginStreamAsync(context).ConfigureAwait(false);
        await writer.WriteAsync(
            StreamEventNames.MessageStart,
            new(new StreamMessage(messageId, request.Model, new TokenUsage(inputTokens, 0), [])),
            ProxyJsonContext.Default.StreamMessageStart,
            cancellationToken).ConfigureAwait(false);

        await writer.WriteAsync(
            StreamEventNames.ContentBlockStart,
            new(0, ContentBlock.ForText(string.Empty)),
            ProxyJsonContext.Default.StreamContentBlockStart,
            cancellationToken).ConfigureAwait(false);

        await writer.WriteAsync(
            StreamEventNames.ContentBlockDelta,
            new(0, StreamDelta.ForText(text)),
            ProxyJsonContext.Default.StreamContentBlockDelta,
            cancellationToken).ConfigureAwait(false);

        await writer.WriteAsync(
            StreamEventNames.ContentBlockStop,
            new(0),
            ProxyJsonContext.Default.StreamContentBlockStop,
            cancellationToken).ConfigureAwait(false);

        await writer.WriteAsync(
            StreamEventNames.MessageDelta,
            new(new StreamStopDetail(StopReasons.EndTurn), new TokenUsage(inputTokens, outputTokens)),
            ProxyJsonContext.Default.StreamMessageDelta,
            cancellationToken).ConfigureAwait(false);

        await writer.WriteAsync(
            StreamEventNames.MessageStop,
            StreamMessageStop.Instance,
            ProxyJsonContext.Default.StreamMessageStop,
            cancellationToken).ConfigureAwait(false);

        return null;
    }

    /// <summary>Forwards a streamed upstream turn as Anthropic events.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="context">The HTTP context the events are written to.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>Always <see langword="null"/>, because the body has already been written.</returns>
    private static async Task<IResult?> StreamAsync(
        HttpResponseMessage response,
        HttpContext context,
        TurnContext turn,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        var writer = await BeginStreamAsync(context).ConfigureAwait(false);
        var request = turn.Request;
        var inputTokens = TokenEstimator.Estimate(request.Messages, request.System, request.Tools);

        var active = turn;
        var current = response;
        var owned = false;
        var started = Stopwatch.GetTimestamp();

        try
        {
            for (var attempt = 1; true; attempt++)
            {
                var translator = NewTranslator(writer, active.Resolved, services);
                var outcome = await TranslateStreamAsync(current, translator, active, inputTokens, services, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome != StreamTurnOutcome.FailedBeforeOutput)
                {
                    // Real output reached the client, which TryOnceAsync's own 200 could not
                    // promise for a streamed call — this is where that promise is actually kept.
                    services.ModelHealth.MarkAvailable(active.Resolved.NimModel);
                    ReportStreamEnded(services, active, outcome, started);
                    return null;
                }

                // Nothing has reached the client, so the whole turn can be asked for again. Past
                // the budget the failure is finally reported, held until now so that a turn which
                // succeeds on a later attempt never shows the client what it took.
                if (attempt >= services.Retries.MaxAttempts)
                {
                    await translator.WriteHeldFailureAsync(cancellationToken).ConfigureAwait(false);
                    NvidiaLog.StreamRetriesExhausted(services.Logger, active.Resolved.NimModel, attempt);
                    return null;
                }

                if (owned)
                {
                    current.Dispose();
                }

                var recovered = await RecoverFailedAttemptAsync(attempt, active, translator, services, cancellationToken)
                    .ConfigureAwait(false);
                active = recovered.Turn;
                current = recovered.Response;
                owned = true;

                if (recovered.ShouldContinue)
                {
                    continue;
                }

                // The recovery call was refused outright, which the ladder inside the client has
                // already exhausted its own attempts on. There is nothing further to try.
                await translator.WriteHeldFailureAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
        }
        finally
        {
            if (owned)
            {
                current.Dispose();
            }
        }
    }

    /// <summary>Recovers a streamed attempt that failed before producing anything.</summary>
    /// <param name="attempt">The attempt number that just failed.</param>
    /// <param name="active">The turn as it stood when the failure arrived.</param>
    /// <param name="translator">The translator the failed attempt ran through.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the recovery when the client disconnects.</param>
    /// <returns>The turn and response to continue with, and whether the caller should keep looping.</returns>
    /// <remarks>
    /// A rejection is downgraded and asked of the same model, because the same body would only be
    /// rejected again; anything else is backed off and re-issued down the fallback chain instead,
    /// since a model that just failed to start a stream is the least likely of the lot to answer.
    /// A transient failure is also counted against the model here: NVIDIA's saturation is not
    /// always visible as a rejected status before generation begins, and a model that already
    /// committed a <c>200</c> only to fail inside the stream is invisible to <see cref="TryOnceAsync"/>'s
    /// own bookkeeping otherwise, so the chain would keep offering it back up to a re-issue that
    /// re-selects the very model that just failed instead of moving on.
    /// </remarks>
    private static async Task<StreamRecoveryOutcome> RecoverFailedAttemptAsync(
        int attempt,
        TurnContext active,
        IStreamTranslator translator,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        if (await TryDowngradeRejectedAsync(active, translator, services, cancellationToken).ConfigureAwait(false) is { } downgraded)
        {
            return new(downgraded.Turn, downgraded.Response, downgraded.Response.IsSuccessStatusCode);
        }

        services.ModelHealth.MarkUnavailable(active.Resolved.NimModel);
        NvidiaLog.RetryingStreamBeforeOutput(services.Logger, active.Resolved.NimModel, attempt, services.Retries.MaxAttempts);
        var reissued = await ReissueAfterBackoffAsync(attempt, active, services, cancellationToken).ConfigureAwait(false);
        return new(reissued.Turn, reissued.Response, reissued.Response.IsSuccessStatusCode);
    }

    /// <summary>Waits out the configured backoff, then re-issues a streamed turn down the fallback chain.</summary>
    /// <param name="attempt">The attempt number the backoff is sized for.</param>
    /// <param name="active">The turn to re-issue.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the wait when the client disconnects.</param>
    /// <returns>The re-issued turn and its response.</returns>
    /// <remarks>
    /// Re-issued through the chain rather than at the same model: a stream that ends before it
    /// produces anything is how this upstream reports saturation from inside an otherwise
    /// successful response, so the model that just did it is the least likely of the lot to
    /// answer, and asking again in the same instant is the one reply guaranteed not to help.
    /// </remarks>
    private static async Task<TurnAttempt> ReissueAfterBackoffAsync(
        int attempt,
        TurnContext active,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        var backoff = RetrySchedule.Delay(attempt, services.Retries, retryAfter: null, services.Time.GetUtcNow());
        await Task.Delay(backoff, services.Time, cancellationToken).ConfigureAwait(false);

        return await SendWithFallbackAsync(active, services, static _ => { }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Retries a streamed turn at the same model with any rejected part of the request stripped.</summary>
    /// <param name="active">The turn as it stood when the mid-stream failure arrived.</param>
    /// <param name="translator">The translator the failed attempt ran through.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the retry when the client disconnects.</param>
    /// <returns>The retried turn and its response, or <see langword="null"/> when the failure was not a rejection.</returns>
    /// <remarks>
    /// A streamed call's own status is committed before generation begins, so a rejection NIM
    /// discovers afterwards has nowhere to report it but inside an error payload on an
    /// otherwise-successful response. Asking the same model again with the exact body it just
    /// rejected cannot succeed, so this is the client's own downgrade ladder read from that
    /// payload instead of from a status code.
    /// </remarks>
    private static async Task<TurnAttempt?> TryDowngradeRejectedAsync(
        TurnContext active,
        IStreamTranslator translator,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        var errorText = services.Retries.ContentAwareDowngrade ? translator.FailureMessage : null;
        if (NimRequestDowngrade.ForRejection(active.UpstreamRequest, translator.FailureStatusCode, errorText) is not { } lighter)
        {
            return null;
        }

        NvidiaLog.ChatTemplateRejected(services.Logger, active.Resolved.NimModel);
        var downgraded = active with { UpstreamRequest = lighter };
        var response = await services.Client.SendChatAsync(downgraded.UpstreamRequest, downgraded.Resolved.Tier, cancellationToken)
            .ConfigureAwait(false);
        return new(downgraded, response);
    }

    /// <summary>Records that a streamed turn reached its end, and how long it took to get there.</summary>
    /// <param name="services">The services the turn was served from.</param>
    /// <param name="turn">The turn that ended, naming the model that actually served it.</param>
    /// <param name="outcome">How the turn ended.</param>
    /// <param name="started">The timestamp translation began at.</param>
    /// <remarks>
    /// The status a streamed turn is reported with arrives in its first second; this is the only
    /// line that says it ended, which is what tells a finished turn from one still running and
    /// from one whose client hung up halfway. The level is checked first because both arguments
    /// are computed, and a turn should not pay for a line nobody is reading.
    /// </remarks>
    private static void ReportStreamEnded(
        MessageServices services,
        TurnContext turn,
        StreamTurnOutcome outcome,
        long started)
    {
        if (!services.Logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        // Both are computed, so they are read into locals past the level check rather than
        // evaluated as arguments to a line that may never be written.
        var ended = outcome.ToString();
        var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        NvidiaLog.StreamedTurnEnded(services.Logger, turn.Resolved.NimModel, ended, elapsed);
    }

    /// <summary>Builds the translator one attempt at a streamed turn is served by.</summary>
    /// <param name="writer">The writer the Anthropic events are emitted through.</param>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <returns>The translator.</returns>
    /// <remarks>
    /// One instance serves one attempt, because its bookkeeping is the state of a stream that no
    /// longer exists once the attempt fails.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IStreamTranslator NewTranslator(
        AnthropicSseWriter writer,
        in ResolvedModel resolved,
        MessageServices services) =>
        services.StreamTranslatorFactory.Create(
            writer,
            resolved,
            TimeSpan.FromSeconds(services.Timeouts.StreamIdleSeconds),
            services.Logger);

    /// <summary>Translates one attempt at a streamed turn.</summary>
    /// <param name="response">The upstream response to read.</param>
    /// <param name="translator">The translator serving this attempt.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="inputTokens">The prompt size to report.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>How the attempt ended.</returns>
    private static async Task<StreamTurnOutcome> TranslateStreamAsync(
        HttpResponseMessage response,
        IStreamTranslator translator,
        TurnContext turn,
        int inputTokens,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        Stream upstream;
        try
        {
            upstream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            // Nothing has been written yet, so this is still recoverable by asking again.
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, turn.Resolved.NimModel, error);
            return StreamTurnOutcome.FailedBeforeOutput;
        }

        await using (upstream.ConfigureAwait(false))
        {
            return await translator
                .TranslateAsync(upstream, turn.MessageId, turn.Request.Model, inputTokens, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Turns reasoning off for a turn whose answer is read by a machine.</summary>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="request">The caller's request.</param>
    /// <returns>The routing outcome, with reasoning suppressed where it can only cost time.</returns>
    /// <remarks>
    /// <para>
    /// A caller asking for a JSON schema wants the object, and nothing reads the reasoning that
    /// precedes it. On a model that reasons inline that trace is generated before the first
    /// character of the answer, over whatever prompt was sent — and these turns send a lot, because
    /// the thing being judged is the conversation. A client's own evaluator gives up waiting long
    /// before the model reaches the JSON it asked for, which reads as the assistant abandoning the
    /// work rather than as a turn that was still coming.
    /// </para>
    /// <para>
    /// Only the reasoning is dropped, not the request. What the caller asked for is still answered,
    /// and answered in the shape it asked for.
    /// </para>
    /// </remarks>
    private static ResolvedModel WithoutReasoningForMachines(in ResolvedModel resolved, MessagesRequest request) =>
        request.OutputConfig?.Format is null ? resolved : resolved with { ThinkingEnabled = false };

    /// <summary>Reads a non-streamed body, recording it before it is parsed.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="nimModel">The NIM model that answered.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="cancellationToken">Abandons the read when the client disconnects.</param>
    /// <returns>The parsed completion.</returns>
    /// <remarks>
    /// Only taken when debug logging is on, because it buffers the whole body as text first. The
    /// streamed path records every line it reads; without this, a non-streamed turn that
    /// translated cleanly into wrong content could not be compared against what NIM actually sent.
    /// </remarks>
    private static async Task<NimChatCompletion?> ReadLoggedCompletionAsync(
        HttpResponseMessage response,
        string nimModel,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        NvidiaLog.UpstreamResponseBody(logger, nimModel, body);

        return JsonSerializer.Deserialize(body, ProxyJsonContext.Default.NimChatCompletion);
    }

    /// <summary>Writes the error event for a transport failure reached before any chunk could be read.</summary>
    /// <param name="writer">The writer the event is emitted through.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask WriteStreamStartFailureAsync(AnthropicSseWriter writer, CancellationToken cancellationToken) =>
        writer.WriteAsync(
            StreamEventNames.Error,
            ErrorResponse.Create(
                AnthropicErrors.TypeFor(StatusCodes.Status503ServiceUnavailable),
                "The upstream connection failed or timed out before the stream began."),
            ProxyJsonContext.Default.ErrorResponse,
            cancellationToken);

    /// <summary>Translates a completed upstream turn.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The Anthropic message.</returns>
    private static async Task<IResult?> CompleteAsync(
        HttpResponseMessage response,
        TurnContext turn,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        var request = turn.Request;

        // The pooled client carries no timeout of its own, so the body read is bounded here.
        // Headers were already received; this covers only the wait for the rest of the body.
        using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeout.CancelAfter(TimeSpan.FromSeconds(services.Timeouts.ReadSeconds));

        NimChatCompletion? completion;
        try
        {
            completion = services.Logger.IsEnabled(LogLevel.Debug)
                ? await ReadLoggedCompletionAsync(response, turn.Resolved.NimModel, services.Logger, bodyTimeout.Token).ConfigureAwait(false)
                : await response.Content
                    .ReadFromJsonAsync(ProxyJsonContext.Default.NimChatCompletion, bodyTimeout.Token)
                    .ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, turn.Resolved.NimModel, error);
            return AnthropicErrors.Result(
                StatusCodes.Status503ServiceUnavailable,
                "The upstream connection failed or timed out before the response body finished.");
        }

        var message = services.CompletionTranslator.Translate(
            completion,
            turn.MessageId,
            request.Model,
            turn.Resolved.NimModel,
            TokenEstimator.Estimate(request.Messages, request.System, request.Tools),
            turn.Resolved.ThinkingEnabled);

        return TypedResults.Json(message, ProxyJsonContext.Default.MessagesResponse);
    }

    /// <summary>Turns a failed upstream response into an Anthropic error.</summary>
    /// <param name="response">The failed upstream response.</param>
    /// <param name="nimModel">The NIM model that rejected the turn.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="cancellationToken">Abandons the read when the client disconnects.</param>
    /// <returns>The error result.</returns>
    /// <remarks>
    /// The status code is already in hand once this runs; a transport failure reading the body
    /// that describes it just means the description is lost, not that the status is. Falling back
    /// to a generic message for that status keeps the report honest instead of crashing over it.
    /// </remarks>
    private static async Task<IResult?> UpstreamFailureAsync(
        HttpResponseMessage response,
        string nimModel,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var upstreamStatus = (int)response.StatusCode;
        var status = TranslateUpstreamStatus(upstreamStatus);
        var fallback = $"The upstream returned status {upstreamStatus}.";

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            ReportRejection(logger, nimModel, upstreamStatus, fallback);
            return AnthropicErrors.Result(status, Describe(upstreamStatus, fallback));
        }

        var detail = body.Length > 0 ? body : fallback;
        ReportRejection(logger, nimModel, upstreamStatus, Truncated(detail));

        return AnthropicErrors.Result(status, Describe(upstreamStatus, detail));
    }

    /// <summary>Reports a rejected turn at the level the rejection actually warrants.</summary>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="nimModel">The NIM model that rejected the turn.</param>
    /// <param name="upstreamStatus">The status NIM returned.</param>
    /// <param name="body">The upstream's own error body, truncated.</param>
    /// <remarks>
    /// A refused credential is the proxy's own, and no turn will succeed until it is replaced;
    /// every other rejection describes one turn. The first is an error, the rest are warnings,
    /// and the journal colours and filters them apart on exactly that distinction.
    /// </remarks>
    private static void ReportRejection(ILogger logger, string nimModel, int upstreamStatus, string body)
    {
        if (upstreamStatus is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            NvidiaLog.UpstreamCredentialRejected(logger, nimModel, upstreamStatus, body);
            return;
        }

        NvidiaLog.TurnRejectedByUpstream(logger, nimModel, upstreamStatus, body);
    }

    /// <summary>Maps an upstream status onto the one the caller should be given.</summary>
    /// <param name="upstreamStatus">The status NIM returned.</param>
    /// <returns>The status to return to the caller.</returns>
    /// <remarks>
    /// Every other status passes through, because it describes the caller's own turn. A credential
    /// failure does not: the credential NIM rejected is the proxy's, and forwarding the status
    /// verbatim tells the client that its key was refused. A coding client acts on that — it stops
    /// and asks the user to log in again, over an operator's expired NVIDIA key that logging in
    /// cannot touch. The failure is the gateway's, so it is reported as one.
    /// </remarks>
    private static int TranslateUpstreamStatus(int upstreamStatus) =>
        upstreamStatus is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? StatusCodes.Status502BadGateway
            : upstreamStatus;

    /// <summary>Says whose credential failed, for the statuses where that is the whole story.</summary>
    /// <param name="upstreamStatus">The status NIM returned.</param>
    /// <param name="detail">The upstream's own description.</param>
    /// <returns>The message to report.</returns>
    private static string Describe(int upstreamStatus, string detail) =>
        upstreamStatus is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? $"NVIDIA NIM rejected the proxy's own API key with status {upstreamStatus}. "
                + "This is the gateway's credential, not the caller's, and re-authenticating will not "
                + $"change it — the key the proxy was started with needs fixing. Upstream said: {detail}"
            : detail;

    /// <summary>Truncates a body so a log line stays a line rather than a dump of the whole payload.</summary>
    /// <param name="body">The body to truncate.</param>
    /// <returns>The body, truncated to <see cref="LoggedBodyLength"/> characters.</returns>
    private static string Truncated(string body) =>
        body.Length <= LoggedBodyLength ? body : body[..LoggedBodyLength];

    /// <summary>Puts the response into streaming mode and returns the writer for it.</summary>
    /// <param name="context">The HTTP context being written to.</param>
    /// <returns>The writer the Anthropic events are emitted through.</returns>
    private static async Task<AnthropicSseWriter> BeginStreamAsync(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = EventStreamContentType;
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // The headers have to reach the client before the first event, or the client waits.
        await response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);

        return new(response.Body);
    }
}
