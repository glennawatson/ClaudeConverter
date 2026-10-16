// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers which backend failures and request token limits permit another model.</summary>
public sealed class UpstreamFallbackPolicyTests
{
    /// <summary>Only backend failures and model-specific rejection statuses need fallback inspection.</summary>
    /// <param name="status">The upstream status to classify.</param>
    /// <param name="expected">Whether it can trigger fallback.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(HttpStatusCode.OK, false)]
    [Arguments(HttpStatusCode.NotFound, false)]
    [Arguments(HttpStatusCode.BadRequest, true)]
    [Arguments(HttpStatusCode.RequestEntityTooLarge, true)]
    [Arguments(HttpStatusCode.UnprocessableEntity, true)]
    [Arguments(HttpStatusCode.PaymentRequired, true)]
    [Arguments(HttpStatusCode.Unauthorized, true)]
    [Arguments(HttpStatusCode.Forbidden, true)]
    [Arguments(HttpStatusCode.TooManyRequests, true)]
    [Arguments(HttpStatusCode.ServiceUnavailable, true)]
    public async Task NeedsInspection_FailureStatuses_IdentifiesFallbackCandidates(HttpStatusCode status, bool expected) =>
        await Assert.That(UpstreamFallbackPolicy.NeedsInspection(status)).IsEqualTo(expected);

    /// <summary>Context limits stay request-specific instead of marking the model unavailable for every caller.</summary>
    /// <param name="status">The upstream status to classify.</param>
    /// <param name="expected">Whether the backend is unavailable.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(HttpStatusCode.BadRequest, false)]
    [Arguments(HttpStatusCode.RequestEntityTooLarge, false)]
    [Arguments(HttpStatusCode.UnprocessableEntity, false)]
    [Arguments(HttpStatusCode.PaymentRequired, true)]
    [Arguments(HttpStatusCode.Unauthorized, true)]
    [Arguments(HttpStatusCode.Forbidden, true)]
    [Arguments(HttpStatusCode.TooManyRequests, true)]
    public async Task IsBackendUnavailable_RejectionStatuses_DistinguishesRequestLimits(HttpStatusCode status, bool expected) =>
        await Assert.That(UpstreamFallbackPolicy.IsBackendUnavailable(status)).IsEqualTo(expected);

    /// <summary>Explicit context limits are recognized without treating unrelated request errors as token limits.</summary>
    /// <param name="body">The upstream rejection body.</param>
    /// <param name="expected">Whether it describes a context limit.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("""{"error":{"code":"context_length_exceeded"}}""", true)]
    [Arguments("""{"error":{"code":"context_window_exceeded"}}""", true)]
    [Arguments("""{"code":"max_tokens_exceeded"}""", true)]
    [Arguments("""{"error":{"message":"Maximum context length exceeded"}}""", true)]
    [Arguments("""{"detail":"The input is too long"}""", true)]
    [Arguments("""{"error":"The prompt is too long"}""", true)]
    [Arguments("Context length exceeded", true)]
    [Arguments("Context window exceeded", true)]
    [Arguments("""{"error":{"code":"content_filter","message":"maximum context length"}}""", false)]
    [Arguments("""{"error":{"code":"content_policy_violation","message":"The prompt is too long"}}""", false)]
    [Arguments("""{"error":{"code":42,"message":false}}""", false)]
    [Arguments("""{"error":{"message":"Malformed tool schema"}}""", false)]
    [Arguments("Malformed request", false)]
    [Arguments("[]", false)]
    [Arguments("null", false)]
    public async Task IsContextLimit_ErrorBodies_RecognizesOnlyTokenLimits(string body, bool expected, CancellationToken cancellationToken) =>
        await Assert.That(UpstreamFallbackPolicy.IsContextLimit(body, cancellationToken)).IsEqualTo(expected);

    /// <summary>A cancelled caller does not parse another backend error.</summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task IsContextLimit_CancelledCaller_Throws(CancellationToken cancellationToken)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await source.CancelAsync();

        await Assert.That(() => UpstreamFallbackPolicy.IsContextLimit("{}", source.Token)).Throws<OperationCanceledException>();
    }
}
