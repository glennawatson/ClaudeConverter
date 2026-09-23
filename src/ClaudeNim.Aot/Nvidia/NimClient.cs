// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The policy applied around the generated NVIDIA NIM transport.</summary>
/// <param name="Api">The Refit-generated NIM surface.</param>
/// <param name="Retries">The configured retry behaviour.</param>
/// <param name="Timeouts">The configured upstream timeouts.</param>
/// <param name="Time">The clock the backoff delays and timeouts are measured against.</param>
/// <param name="Logger">The diagnostic log.</param>
/// <remarks>
/// <para>
/// <see cref="INimApi"/> describes the wire calls; this record owns the behaviours the wire shape
/// cannot express. There are three, and they answer different questions.
/// </para>
/// <para>
/// A <em>rejected</em> request is retried with less of it. What a model accepts varies across the
/// catalogue, and a rejection means "not this" — so the same body would be rejected again. Each
/// attempt drops the least costly thing still present; see <see cref="NimRequestDowngrade"/>.
/// </para>
/// <para>
/// A <em>transient</em> failure is retried with the same body after a backoff, because it means
/// "not now". NVIDIA reports saturation as a plain 503, which clears on its own.
/// </para>
/// <para>
/// The pooled <see cref="HttpClient"/> carries no timeout of its own, because one bound cannot fit
/// both a streamed and a non-streamed call. A stream is bounded by idle time in
/// <see cref="NimStreamTranslator"/> once it is running, and by a short header wait here before it
/// is. A non-streamed call is bounded here by <see cref="HttpTimeoutOptions.CompletionSeconds"/>,
/// because NIM sends it nothing until the answer is finished — the wait for its headers is the
/// whole generation, not a phase before one.
/// </para>
/// <para>
/// A failed model listing is reported as an absent listing rather than an exception, because the
/// catalogue is expected to carry on from its built-in profiles when the upstream is unreachable.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimClient: {ToString(),nq}")]
public sealed record NimClient(
    INimApi Api,
    RetryOptions Retries,
    HttpTimeoutOptions Timeouts,
    TimeProvider Time,
    ILogger<NimClient> Logger) : INimClient
{
    /// <inheritdoc/>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public ValueTask<HttpResponseMessage> SendChatAsync(
        NimChatRequest request,
        ModelTier tier,
        CancellationToken cancellationToken) =>
        SendChatAsync(request, tier, Retries.MaxAttempts, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<HttpResponseMessage> SendChatAsync(
        NimChatRequest request,
        ModelTier tier,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A caller cannot buy itself more patience than the operator configured, and one
        // attempt is the floor: a call that is never made cannot answer.
        var budget = Math.Clamp(maxAttempts, 1, Retries.MaxAttempts);

        var current = request;
        var response = await SendAsync(current, tier, budget, cancellationToken).ConfigureAwait(false);

        // Bounded by the retry budget plus the fixed number of downgrade rungs: a downgrade never
        // repeats once applied, so the ladder cannot cycle back to a request already tried.
        var ceiling = budget + NimRequestDowngrade.RungCount;
        var attempts = 1;
        var retried = false;

        for (var attempt = 1; attempt < ceiling && !response.IsSuccessStatusCode; attempt++)
        {
            if (Downgrade(current, response.StatusCode) is { } lighter)
            {
                NvidiaLog.ChatTemplateRejected(Logger, current.Model);
                response.Dispose();
                current = lighter;
                response = await SendAsync(current, tier, budget, cancellationToken).ConfigureAwait(false);
                attempts++;
                continue;
            }

            if (attempt >= budget || !RetrySchedule.IsTransient(response.StatusCode))
            {
                break;
            }

            var delay = RetrySchedule.Delay(attempt, Retries, response.Headers.RetryAfter, Time.GetUtcNow());
            NvidiaLog.RetryingAfterTransientFailure(
                Logger,
                current.Model,
                (int)response.StatusCode,
                attempt,
                budget,
                delay.TotalMilliseconds);

            response.Dispose();
            await Task.Delay(delay, Time, cancellationToken).ConfigureAwait(false);
            response = await SendAsync(current, tier, budget, cancellationToken).ConfigureAwait(false);
            attempts++;
            retried = true;
        }

        // Only worth saying when more than one attempt was spent: a turn that failed outright is
        // already reported by the caller, and repeating it here would double every rejection.
        if (retried && !response.IsSuccessStatusCode)
        {
            NvidiaLog.RetriesExhausted(Logger, current.Model, (int)response.StatusCode, attempts);
        }

        return response;
    }

    /// <inheritdoc/>
    public async ValueTask<NimModelList?> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var timeout = BoundedToken(TimeSpan.FromSeconds(Timeouts.ReadSeconds), cancellationToken);
        using var response = await Api.ListModelsAsync(timeout.Token).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response.Content;
        }

        // Refit reports a transport failure as an error on the response rather than throwing,
        // so the two cases are told apart here instead of by a catch block.
        if (response.Error is { } error)
        {
            NvidiaLog.ModelListingUnreachable(Logger, error);
        }
        else
        {
            NvidiaLog.ModelListingFailed(Logger, (int)(response.StatusCode ?? HttpStatusCode.ServiceUnavailable));
        }

        return null;
    }

    /// <summary>Finds the next rung of the downgrade ladder for a rejected request.</summary>
    /// <param name="request">The request that was rejected.</param>
    /// <param name="status">The status the upstream returned.</param>
    /// <returns>A lighter request, or <see langword="null"/> when there is nothing left to drop.</returns>
    /// <remarks>
    /// A 500 counts as a rejection here as well as a 400. NVIDIA answers a request its schema
    /// validator cannot handle with an internal error rather than a bad request, so treating 500
    /// as purely transient would retry the same unacceptable body until the attempts ran out.
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static NimChatRequest? Downgrade(NimChatRequest request, HttpStatusCode status) =>
        NimRequestDowngrade.ForRejection(request, (int)status);

    /// <summary>Determines whether a failed attempt is worth making again.</summary>
    /// <param name="error">The exception the attempt raised.</param>
    /// <param name="cancellationToken">The caller's own cancellation.</param>
    /// <returns><see langword="true"/> when the attempt failed for a reason that may not recur.</returns>
    /// <remarks>
    /// A status the upstream returns is already retried by the caller's ladder; an attempt that
    /// never got a status at all was not, and simply failed the turn. A dropped connection is the
    /// ordinary way NVIDIA's endpoints go quiet under load, and it costs nothing to ask again.
    /// The caller's own cancellation is excluded: that is the client leaving, and there is no one
    /// left to retry for.
    /// </remarks>
    private static bool IsRetryableTransport(Exception error, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && error is HttpRequestException or IOException or OperationCanceledException;

    /// <summary>Determines whether an attempt was ended by this proxy's own deadline.</summary>
    /// <param name="timeout">The deadline the attempt was bounded by.</param>
    /// <param name="cancellationToken">The caller's own cancellation.</param>
    /// <returns><see langword="true"/> when the deadline ran out with the client still waiting.</returns>
    /// <remarks>
    /// This is not the dropped connection above, and it is the one failure here worth telling
    /// apart, because asking again is the wrong answer to it. The deadline is the whole budget an
    /// attempt was given; a model that did not finish inside it will not finish inside the next
    /// one either, so a retry spends the same wait over again and the client — which is running a
    /// deadline of its own — gives up while the proxy is still busy on attempt three.
    /// </remarks>
    private static bool ExpiredDeadline(CancellationTokenSource timeout, CancellationToken cancellationToken) =>
        timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

    /// <summary>Links a token to a deadline.</summary>
    /// <param name="budget">How long the call it bounds may take.</param>
    /// <param name="cancellationToken">The caller's own cancellation.</param>
    /// <returns>The linked source; disposing it releases the timer.</returns>
    private static CancellationTokenSource BoundedToken(TimeSpan budget, CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(budget);
        return linked;
    }

    /// <summary>Works out how long one attempt at a call may take.</summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="tier">The tier the turn was routed to, which a streamed call's bound may be overridden for.</param>
    /// <returns>The deadline the attempt is bounded by.</returns>
    /// <remarks>
    /// A streamed call gets the short bound, because what it is waiting for is headers and those
    /// arrive long before the answer does for most models — but not for all of them, which is why
    /// the bound can be overridden per tier; see <see cref="HttpTimeoutOptions.ReadSecondsFor"/>. A
    /// non-streamed call gets the long one, because NIM withholds the status line until the answer
    /// is complete and so the same wait covers the entire generation.
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private TimeSpan Budget(NimChatRequest request, ModelTier tier) =>
        TimeSpan.FromSeconds(request.Stream ? Timeouts.ReadSecondsFor(tier) : Timeouts.CompletionSeconds);

    /// <summary>Issues one chat completion call, bounded by the deadline its shape calls for.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="tier">The tier the turn was routed to, which a streamed call's bound may be overridden for.</param>
    /// <param name="maxAttempts">The attempt budget this call may spend on a dropped connection.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream response, with headers received.</returns>
    /// <remarks>
    /// The bound applies only up to the point headers arrive, which is where the awaited task
    /// completes for a streamed response as well as a plain one. It says nothing about how long a
    /// streamed body then takes to finish, which <see cref="NimStreamTranslator"/> bounds instead.
    /// </remarks>
    private async ValueTask<HttpResponseMessage> SendAsync(
        NimChatRequest request,
        ModelTier tier,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var budget = Budget(request, tier);

        for (var attempt = 1; true; attempt++)
        {
            using var timeout = BoundedToken(budget, cancellationToken);

            try
            {
                return await Api.SendChatAsync(request, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (ExpiredDeadline(timeout, cancellationToken))
            {
                NvidiaLog.UpstreamDeadlineExpired(Logger, request.Model, budget.TotalSeconds, request.Stream, error);
                throw;
            }
            catch (Exception error) when (IsRetryableTransport(error, cancellationToken) && attempt < maxAttempts)
            {
                var delay = RetrySchedule.Delay(attempt, Retries, retryAfter: null, Time.GetUtcNow());
                NvidiaLog.RetryingAfterTransportFailure(Logger, request.Model, attempt, maxAttempts, delay.TotalMilliseconds, error);
                await Task.Delay(delay, Time, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
