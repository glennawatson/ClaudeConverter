// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Configuration;
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
/// both a streamed and a non-streamed call: a stream is bounded by idle time in
/// <see cref="NimStreamTranslator"/> instead, and both kinds bound the wait for a response's
/// headers here, since that phase is unbounded on either shape of call.
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
    public async ValueTask<HttpResponseMessage> SendChatAsync(
        NimChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = request;
        var response = await SendAsync(current, cancellationToken).ConfigureAwait(false);

        // Bounded by the retry budget plus the fixed number of downgrade rungs: a downgrade never
        // repeats once applied, so the ladder cannot cycle back to a request already tried.
        var ceiling = Retries.MaxAttempts + NimRequestDowngrade.RungCount;
        var attempts = 1;
        var retried = false;

        for (var attempt = 1; attempt < ceiling && !response.IsSuccessStatusCode; attempt++)
        {
            if (Downgrade(current, response.StatusCode) is { } lighter)
            {
                NvidiaLog.ChatTemplateRejected(Logger, current.Model);
                response.Dispose();
                current = lighter;
                response = await SendAsync(current, cancellationToken).ConfigureAwait(false);
                attempts++;
                continue;
            }

            if (attempt >= Retries.MaxAttempts || !RetrySchedule.IsTransient(response.StatusCode))
            {
                break;
            }

            var delay = RetrySchedule.Delay(attempt, Retries, response.Headers.RetryAfter, Time.GetUtcNow());
            NvidiaLog.RetryingAfterTransientFailure(
                Logger,
                (int)response.StatusCode,
                attempt,
                Retries.MaxAttempts,
                delay.TotalMilliseconds);

            response.Dispose();
            await Task.Delay(delay, Time, cancellationToken).ConfigureAwait(false);
            response = await SendAsync(current, cancellationToken).ConfigureAwait(false);
            attempts++;
            retried = true;
        }

        // Only worth saying when more than one attempt was spent: a turn that failed outright is
        // already reported by the caller, and repeating it here would double every rejection.
        if (retried && !response.IsSuccessStatusCode)
        {
            NvidiaLog.RetriesExhausted(Logger, (int)response.StatusCode, attempts);
        }

        return response;
    }

    /// <inheritdoc/>
    public async ValueTask<NimModelList?> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var timeout = BoundedToken(cancellationToken);
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
    private static NimChatRequest? Downgrade(NimChatRequest request, HttpStatusCode status) =>
        status is not (HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError)
            ? null
            : NimRequestDowngrade.WithoutReasoningControls(request)
                ?? NimRequestDowngrade.WithoutReplayedReasoning(request);

    /// <summary>Issues one chat completion call, bounded by the header-wait timeout.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream response, with headers received.</returns>
    /// <remarks>
    /// The bound applies only up to the point headers arrive, which is where the awaited task
    /// completes for a streamed response as well as a plain one. It says nothing about how long a
    /// streamed body then takes to finish, which <see cref="NimStreamTranslator"/> bounds instead.
    /// </remarks>
    private async ValueTask<HttpResponseMessage> SendAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        using var timeout = BoundedToken(cancellationToken);
        return await Api.SendChatAsync(request, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Links a token to the configured header-wait timeout.</summary>
    /// <param name="cancellationToken">The caller's own cancellation.</param>
    /// <returns>The linked source; disposing it releases the timer.</returns>
    private CancellationTokenSource BoundedToken(CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(Timeouts.ReadSeconds));
        return linked;
    }
}
