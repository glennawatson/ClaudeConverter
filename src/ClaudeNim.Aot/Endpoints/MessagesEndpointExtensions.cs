// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Optimizations;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints;

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

        if (RequestOptimizer.TryAnswer(request, services.Optimizations, out var canned))
        {
            return await AnswerLocallyAsync(request, context, messageId, canned, cancellationToken)
                .ConfigureAwait(false);
        }

        var resolved = services.Router.Resolve(request.Model);
        var upstreamRequest = NimRequestBuilder.Build(
            request,
            resolved.NimModel,
            resolved.ThinkingEnabled,
            services.Nim);

        using var lease = await services.Gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            NvidiaLog.RateLimitSaturated(services.Logger);
            return AnthropicErrors.Result(
                StatusCodes.Status429TooManyRequests,
                "The proxy's own rate limit is saturated; retry shortly.");
        }

        NvidiaLog.SendingTurn(services.Logger, request.Model, resolved.NimModel, request.IsStreaming);
        LogUpstreamRequestBody(services.Logger, upstreamRequest);

        HttpResponseMessage response;
        try
        {
            response = await services.Client.SendChatAsync(upstreamRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, error);
            return AnthropicErrors.Result(
                StatusCodes.Status503ServiceUnavailable,
                "The upstream connection failed or timed out before a response arrived.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await UpstreamFailureAsync(response, services.Logger, cancellationToken).ConfigureAwait(false);
            }

            return request.IsStreaming
                ? await StreamAsync(response, context, request, resolved, messageId, services, cancellationToken)
                    .ConfigureAwait(false)
                : await CompleteAsync(response, request, resolved, messageId, services, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    /// <summary>Logs the exact request sent upstream, when debug logging is enabled.</summary>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="upstreamRequest">The request about to be sent.</param>
    /// <remarks>
    /// Serializing a tool-bearing request is not free, so the check comes first rather than relying
    /// on the generated log method's own internal one -- that still evaluates this argument eagerly
    /// before the call, since it is a plain string parameter.
    /// </remarks>
    private static void LogUpstreamRequestBody(ILogger logger, NimChatRequest upstreamRequest)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var body = JsonSerializer.Serialize(upstreamRequest, ProxyJsonContext.Default.NimChatRequest);
        NvidiaLog.UpstreamRequestBody(logger, body);
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
    /// <param name="request">The caller's request.</param>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>Always <see langword="null"/>, because the body has already been written.</returns>
    private static async Task<IResult?> StreamAsync(
        HttpResponseMessage response,
        HttpContext context,
        MessagesRequest request,
        ResolvedModel resolved,
        string messageId,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        var writer = await BeginStreamAsync(context).ConfigureAwait(false);
        var idleTimeout = TimeSpan.FromSeconds(services.Timeouts.StreamIdleSeconds);

        // A reasoning model may have had its opening <think> written by the chat template rather
        // than by itself, whatever this turn asked for: the GLM 5.3 template seeds it
        // unconditionally and never reads enable_thinking.
        var seeded = resolved.ThinkingEnabled || NimModelCatalogDefaults.SupportsThinking(resolved.NimModel);
        var translator = new NimStreamTranslator(
            writer,
            resolved.ThinkingEnabled,
            seeded,
            idleTimeout,
            services.Logger);

        Stream upstream;
        try
        {
            upstream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            // Headers for the 200 response are already flushed at this point, so there is no
            // status code left to change; the client learns about this the same way it learns
            // about any other mid-stream failure, through an error event on the stream itself.
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, error);
            await WriteStreamStartFailureAsync(writer, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using (upstream.ConfigureAwait(false))
        {
            await translator.TranslateAsync(
                upstream,
                messageId,
                request.Model,
                TokenEstimator.Estimate(request.Messages, request.System, request.Tools),
                cancellationToken).ConfigureAwait(false);
        }

        return null;
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
    /// <param name="request">The caller's request.</param>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="services">The services the turn is served from.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The Anthropic message.</returns>
    private static async Task<IResult?> CompleteAsync(
        HttpResponseMessage response,
        MessagesRequest request,
        ResolvedModel resolved,
        string messageId,
        MessageServices services,
        CancellationToken cancellationToken)
    {
        // The pooled client carries no timeout of its own, so the body read is bounded here.
        // Headers were already received; this covers only the wait for the rest of the body.
        using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeout.CancelAfter(TimeSpan.FromSeconds(services.Timeouts.ReadSeconds));

        NimChatCompletion? completion;
        try
        {
            completion = await response.Content
                .ReadFromJsonAsync(ProxyJsonContext.Default.NimChatCompletion, bodyTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.LogUpstreamTransportFailure(services.Logger, error);
            return AnthropicErrors.Result(
                StatusCodes.Status503ServiceUnavailable,
                "The upstream connection failed or timed out before the response body finished.");
        }

        var message = NimCompletionTranslator.Translate(
            completion,
            messageId,
            request.Model,
            TokenEstimator.Estimate(request.Messages, request.System, request.Tools),
            resolved.ThinkingEnabled);

        return TypedResults.Json(message, ProxyJsonContext.Default.MessagesResponse);
    }

    /// <summary>Turns a failed upstream response into an Anthropic error.</summary>
    /// <param name="response">The failed upstream response.</param>
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
            NvidiaLog.TurnRejectedByUpstream(logger, upstreamStatus, fallback);
            return AnthropicErrors.Result(status, Describe(upstreamStatus, fallback));
        }

        var detail = body.Length > 0 ? body : fallback;
        NvidiaLog.TurnRejectedByUpstream(logger, upstreamStatus, Truncated(detail));
        return AnthropicErrors.Result(status, Describe(upstreamStatus, detail));
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
