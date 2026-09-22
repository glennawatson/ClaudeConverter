// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net.Http.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Optimizations;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
            return AnthropicErrors.Result(
                StatusCodes.Status429TooManyRequests,
                "The proxy's own rate limit is saturated; retry shortly.");
        }

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
                return await UpstreamFailureAsync(response, cancellationToken).ConfigureAwait(false);
            }

            return request.IsStreaming
                ? await StreamAsync(response, context, request, resolved, messageId, services, cancellationToken)
                    .ConfigureAwait(false)
                : await CompleteAsync(response, request, resolved, messageId, services.Timeouts, cancellationToken)
                    .ConfigureAwait(false);
        }
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
        var translator = new NimStreamTranslator(writer, resolved.ThinkingEnabled, idleTimeout, services.Logger);

        await using var upstream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await translator.TranslateAsync(
            upstream,
            messageId,
            request.Model,
            TokenEstimator.Estimate(request.Messages, request.System, request.Tools),
            cancellationToken).ConfigureAwait(false);

        return null;
    }

    /// <summary>Translates a completed upstream turn.</summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="request">The caller's request.</param>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="timeouts">The configured upstream timeouts.</param>
    /// <param name="cancellationToken">Abandons the turn when the client disconnects.</param>
    /// <returns>The Anthropic message.</returns>
    private static async Task<IResult?> CompleteAsync(
        HttpResponseMessage response,
        MessagesRequest request,
        ResolvedModel resolved,
        string messageId,
        HttpTimeoutOptions timeouts,
        CancellationToken cancellationToken)
    {
        // The pooled client carries no timeout of its own, so the body read is bounded here.
        // Headers were already received; this covers only the wait for the rest of the body.
        using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeout.CancelAfter(TimeSpan.FromSeconds(timeouts.ReadSeconds));

        var completion = await response.Content
            .ReadFromJsonAsync(ProxyJsonContext.Default.NimChatCompletion, bodyTimeout.Token)
            .ConfigureAwait(false);

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
    /// <param name="cancellationToken">Abandons the read when the client disconnects.</param>
    /// <returns>The error result.</returns>
    private static async Task<IResult?> UpstreamFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;

        return AnthropicErrors.Result(
            status,
            body.Length > 0 ? body : $"The upstream returned status {status}.");
    }

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
