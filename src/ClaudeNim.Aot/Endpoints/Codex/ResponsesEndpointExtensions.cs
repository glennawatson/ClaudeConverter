// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Codex.Streaming;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>The Responses API, which is the route a Codex-family client actually talks through.</summary>
/// <remarks>
/// A turn is either forwarded streamed or forwarded and translated once the upstream has finished;
/// unlike the Messages API there is no local housekeeping fast path here yet. Streaming is handled
/// by writing the events directly to the response body, matching the Messages API route for the
/// same reason: the first token has to reach the client before the last one exists.
/// </remarks>
public static class ResponsesEndpointExtensions
{
    /// <summary>The content type a Codex client expects a streamed turn to arrive as.</summary>
    private const string EventStreamContentType = "text/event-stream";

    /// <summary>The longest run of a rejected upstream body that is written to the log.</summary>
    private const int LoggedBodyLength = 400;

    /// <summary>The Responses API route.</summary>
    /// <param name="endpoints">The route builder the endpoint is added to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers the Responses API route.</summary>
        /// <returns>The group the endpoint was registered in.</returns>
        public RouteGroupBuilder MapResponsesEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup("/v1/responses").WithTags("Responses");

            _ = group.MapPost("/", SendResponseAsync).WithName(nameof(SendResponseAsync));

            return group;
        }
    }

    /// <summary>Serves one Responses API turn.</summary>
    /// <param name="request">The caller's request.</param>
    /// <param name="context">The HTTP context a streamed turn is written to.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The response, or <see langword="null"/> once a streamed turn has been written.</returns>
    internal static async Task<IResult?> SendResponseAsync(
        ResponsesRequest request,
        HttpContext context,
        CodexServices services,
        CancellationToken cancellationToken)
    {
        if (InvalidRequest(request) is { } invalid)
        {
            return invalid;
        }

        var responseId = $"resp_{Guid.NewGuid():N}";

        using var scope = NvidiaLog.BeginTurn(services.Logger, responseId, request.Model);

        var turn = Route(request, responseId, services);

        using var lease = await services.Gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            NvidiaLog.RateLimitSaturated(services.Logger, turn.Resolved.NimModel);
            return CodexErrors.Result(
                StatusCodes.Status429TooManyRequests,
                "The proxy's own rate limit is saturated; retry shortly.");
        }

        NvidiaLog.SendingTurn(services.Logger, request.Model, turn.Resolved.NimModel, request.IsStreaming);
        LogUpstreamRequestBody(services.Logger, turn.Resolved.NimModel, turn.UpstreamRequest);

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
            NvidiaLog.ClientAbandonedTurn(
                services.Logger,
                inFlightModel,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, turn.Resolved.NimModel, error);
            return CodexErrors.Result(
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
    /// <param name="responseId">The identifier to report for the turn.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <returns>The turn, ready to send.</returns>
    private static CodexTurnContext Route(ResponsesRequest request, string responseId, CodexServices services)
    {
        var resolved = services.Router.Resolve(request.Model);

        return new(
            request,
            CodexRequestBuilder.Build(
                request,
                resolved.NimModel,
                resolved.ThinkingEnabled,
                services.Nim,
                services.Catalog.DefaultMaxOutputTokens),
            resolved,
            responseId);
    }

    /// <summary>Sends a turn, walking the tier's fallback chain while the upstream is unavailable.</summary>
    /// <param name="turn">The turn being served, as routing left it.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="reportAttempt">Told the model about to be asked, before each attempt that may be cancelled.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The turn as the model that answered it left it, and that model's response.</returns>
    private static async Task<CodexTurnAttempt> SendWithFallbackAsync(
        CodexTurnContext turn,
        CodexServices services,
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

        // Everything was busy, or every candidate was cooling down from having just been. Asking
        // the routed model directly here, rather than through TryOnceAsync, is deliberate: cooldown
        // only ever governs the fast walk above, not the last resort, so a chain that looks entirely
        // dead is still asked for real rather than given up on outright.
        NvidiaLog.EveryModelUnavailable(services.Logger, turn.Resolved.NimModel, alternatives.Count + 1);

        reportAttempt(turn.Resolved.NimModel);
        return new(turn, await SendDirectAsync(turn, services, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Sends a turn to the model it names directly, bypassing cooldown, and records the outcome.</summary>
    /// <param name="turn">The turn to send.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The upstream response.</returns>
    private static async Task<HttpResponseMessage> SendDirectAsync(CodexTurnContext turn, CodexServices services, CancellationToken cancellationToken)
    {
        var response = await services.Client.SendChatAsync(turn.UpstreamRequest, turn.Resolved.Tier, cancellationToken).ConfigureAwait(false);

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

    /// <summary>Makes one attempt at a turn, reporting an unavailable model as no answer at all.</summary>
    /// <param name="turn">The turn to attempt.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The response, or <see langword="null"/> when this model cannot serve the turn now.</returns>
    private static async Task<CodexModelAttempt> TryOnceAsync(
        CodexTurnContext turn,
        CodexServices services,
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
            response = await services.Client
                .SendChatAsync(turn.UpstreamRequest, turn.Resolved.Tier, maxAttempts: 1, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
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
    private static CodexTurnContext Rerouted(
        CodexTurnContext turn,
        CodexServices services,
        string next,
        IReadOnlyList<string> remaining)
    {
        var resolved = turn.Resolved with { NimModel = next, Fallbacks = remaining };

        return turn with
        {
            Resolved = resolved,
            UpstreamRequest = CodexRequestBuilder.Build(
                turn.Request,
                next,
                resolved.ThinkingEnabled,
                services.Nim,
                services.Catalog.DefaultMaxOutputTokens),
        };
    }

    /// <summary>Takes the part of a chain that has not been tried yet.</summary>
    /// <param name="alternatives">The chain being walked.</param>
    /// <param name="candidate">The one-based position just moved to.</param>
    /// <returns>The models after that position.</returns>
    private static List<string> Remaining(IReadOnlyList<string> alternatives, int candidate)
    {
        List<string> rest = [];

        for (var i = candidate; i < alternatives.Count; i++)
        {
            rest.Add(alternatives[i]);
        }

        return rest;
    }

    /// <summary>Hands a successful upstream response to the path its shape calls for.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="context">The HTTP context being answered.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The result to return, or <see langword="null"/> once a streamed body has been written.</returns>
    private static async Task<IResult?> DispatchAsync(
        HttpResponseMessage response,
        HttpContext context,
        CodexTurnContext turn,
        CodexServices services,
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
    private static bool IsUpstreamTransportFailure(Exception error) =>
        error is HttpRequestException or OperationCanceledException or IOException;

    /// <summary>Validates that a request carries the fields every turn needs.</summary>
    /// <param name="request">The caller's request, which may be null or incompletely bound.</param>
    /// <returns>An error result when the request cannot be served, or <see langword="null"/> when it can.</returns>
    private static IResult? InvalidRequest(ResponsesRequest? request)
    {
        if (request is null)
        {
            return CodexErrors.Result(
                StatusCodes.Status400BadRequest,
                "The request body was missing or unreadable.");
        }

        if (string.IsNullOrEmpty(request.Model))
        {
            return CodexErrors.Result(StatusCodes.Status400BadRequest, "The request did not name a model.");
        }

        return request.Input is { Count: > 0 }
            ? null
            : CodexErrors.Result(StatusCodes.Status400BadRequest, "The request carried no input.");
    }

    /// <summary>Forwards a streamed upstream turn as Responses API events.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="context">The HTTP context the events are written to.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>Always <see langword="null"/>, because the body has already been written.</returns>
    private static async Task<IResult?> StreamAsync(
        HttpResponseMessage response,
        HttpContext context,
        CodexTurnContext turn,
        CodexServices services,
        CancellationToken cancellationToken)
    {
        var writer = await BeginStreamAsync(context).ConfigureAwait(false);
        var inputTokens = EstimateInputTokens(turn.Request);

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
    private static async Task<CodexStreamRecoveryOutcome> RecoverFailedAttemptAsync(
        int attempt,
        CodexTurnContext active,
        IStreamTranslator translator,
        CodexServices services,
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
    private static async Task<CodexTurnAttempt> ReissueAfterBackoffAsync(
        int attempt,
        CodexTurnContext active,
        CodexServices services,
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
    private static async Task<CodexTurnAttempt?> TryDowngradeRejectedAsync(
        CodexTurnContext active,
        IStreamTranslator translator,
        CodexServices services,
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
    private static void ReportStreamEnded(
        CodexServices services,
        CodexTurnContext turn,
        StreamTurnOutcome outcome,
        long started)
    {
        if (!services.Logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var ended = outcome.ToString();
        var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        NvidiaLog.StreamedTurnEnded(services.Logger, turn.Resolved.NimModel, ended, elapsed);
    }

    /// <summary>Builds the translator one attempt at a streamed turn is served by.</summary>
    /// <param name="writer">The writer the Responses API events are emitted through.</param>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <returns>The translator.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IStreamTranslator NewTranslator(
        CodexSseWriter writer,
        in ResolvedModel resolved,
        CodexServices services) =>
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
        CodexTurnContext turn,
        int inputTokens,
        CodexServices services,
        CancellationToken cancellationToken)
    {
        Stream upstream;
        try
        {
            upstream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, turn.Resolved.NimModel, error);
            return StreamTurnOutcome.FailedBeforeOutput;
        }

        await using (upstream.ConfigureAwait(false))
        {
            return await translator
                .TranslateAsync(upstream, turn.ResponseId, turn.Request.Model, inputTokens, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Reads a non-streamed body, recording it before it is parsed.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="nimModel">The NIM model that answered.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="cancellationToken">Abandons the read when the client disconnects.</param>
    /// <returns>The parsed completion.</returns>
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

    /// <summary>Translates a completed upstream turn.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="turn">The turn being served.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The Responses API turn.</returns>
    private static async Task<IResult?> CompleteAsync(
        HttpResponseMessage response,
        CodexTurnContext turn,
        CodexServices services,
        CancellationToken cancellationToken)
    {
        var request = turn.Request;

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
            return CodexErrors.Result(
                StatusCodes.Status503ServiceUnavailable,
                "The upstream connection failed or timed out before the response body finished.");
        }

        var message = services.CompletionTranslator.Translate(
            completion,
            turn.ResponseId,
            request.Model,
            turn.Resolved.NimModel,
            EstimateInputTokens(request),
            turn.Resolved.ThinkingEnabled);

        return TypedResults.Json(message, ProxyJsonContext.Default.ResponsesResponse);
    }

    /// <summary>Estimates the prompt size of a Responses API request.</summary>
    /// <param name="request">The caller's request.</param>
    /// <returns>The estimated token count.</returns>
    private static int EstimateInputTokens(ResponsesRequest request)
    {
        var characters = request.Instructions?.Length ?? 0;

        for (var i = 0; i < request.Input.Count; i++)
        {
            var item = request.Input[i];
            characters += item.Name?.Length ?? 0;
            characters += item.Arguments?.Length ?? 0;
            characters += ContentLength(item.Content);
            characters += ContentLength(item.Summary);
        }

        return TokenEstimator.FromLength(characters);
    }

    /// <summary>Sums the text length of a list of content parts.</summary>
    /// <param name="parts">The parts to measure, which may be absent.</param>
    /// <returns>The summed length.</returns>
    private static int ContentLength(List<ResponseContentItem>? parts)
    {
        if (parts is not { Count: > 0 })
        {
            return 0;
        }

        var total = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            total += parts[i].Text?.Length ?? 0;
        }

        return total;
    }

    /// <summary>Turns a failed upstream response into a Responses API error.</summary>
    /// <param name="response">The failed upstream response.</param>
    /// <param name="nimModel">The NIM model that rejected the turn.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="cancellationToken">Abandons the read when the client disconnects.</param>
    /// <returns>The error result.</returns>
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
            return CodexErrors.Result(status, Describe(upstreamStatus, fallback));
        }

        var detail = body.Length > 0 ? body : fallback;
        ReportRejection(logger, nimModel, upstreamStatus, Truncated(detail));

        return CodexErrors.Result(status, Describe(upstreamStatus, detail));
    }

    /// <summary>Reports a rejected turn at the level the rejection actually warrants.</summary>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="nimModel">The NIM model that rejected the turn.</param>
    /// <param name="upstreamStatus">The status NIM returned.</param>
    /// <param name="body">The upstream's own error body, truncated.</param>
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
    /// <returns>The writer the Responses API events are emitted through.</returns>
    private static async Task<CodexSseWriter> BeginStreamAsync(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = EventStreamContentType;
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        await response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);

        return new(response.Body);
    }
}
