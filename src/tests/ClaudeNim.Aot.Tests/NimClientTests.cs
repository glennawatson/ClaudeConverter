// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Refit;
using Refit.Testing;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the retry, downgrade and model-listing policy <see cref="NimClient"/> applies around the wire calls.</summary>
public sealed class NimClientTests
{
    /// <summary>The number of upstream calls a transient failure followed by success takes.</summary>
    private const int CallsAfterOneTransientFailure = 2;

    /// <summary>The number of requests a single downgrade rung takes.</summary>
    private const int RequestsAfterOneDowngrade = 2;

    /// <summary>The request URL used by the listing fixtures, which is never actually dialled.</summary>
    private const string ListingUrl = "https://example.invalid/models";

    /// <summary>The retry settings used across the fixtures, with delays small enough for a fast test.</summary>
    private static readonly RetryOptions Retries = new(MaxAttempts: 3, BaseDelayMilliseconds: 1, MaxDelayMilliseconds: 2, UseJitter: false);

    /// <summary>The timeout settings used across the fixtures.</summary>
    private static readonly HttpTimeoutOptions Timeouts = new();

    /// <summary>A pacer with spacing disabled, so it never slows a fixture down.</summary>
    private static readonly IRequestPacer NoPacing = new RequestPacer(new RateLimitOptions(MinGapMilliseconds: 0), TimeProvider.System);

    /// <summary>Timeout settings whose header wait runs out at once, while the completion budget does not.</summary>
    private static readonly HttpTimeoutOptions ImmediateDeadline = new(ReadSeconds: 0, CompletionSeconds: 30);

    /// <summary>Timeout settings whose header wait runs out at once for every tier but Opus.</summary>
    private static readonly HttpTimeoutOptions TieredDeadline = new(ReadSeconds: 0, CompletionSeconds: 30, OpusReadSeconds: 1);

    /// <summary>An answer slow enough that a header wait of zero has long since run out.</summary>
    private static readonly TimeSpan SlowAnswerDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>An answer that never arrives, so only a deadline can end the call.</summary>
    private static readonly TimeSpan UnreachedAnswerDelay = TimeSpan.FromMinutes(5);

    /// <summary>A request carrying reasoning controls, so a downgrade has something to drop.</summary>
    private static readonly NimChatRequest RequestWithReasoning = new(
        "nvidia/nemotron-3-super-120b-a12b",
        [new NimChatMessage("user", NimContent.FromText("hi"))],
        MaxTokens: 100,
        Stream: false,
        ReasoningEffort: "high");

    /// <summary>A streamed request, which is bounded by the header wait rather than the completion budget.</summary>
    private static readonly NimChatRequest StreamedRequest = new(
        "nvidia/nemotron-3-super-120b-a12b",
        [new NimChatMessage("user", NimContent.FromText("hi"))],
        MaxTokens: 100,
        Stream: true);

    /// <summary>A single successful call is not retried.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SuccessfulCallIsNotRetried()
    {
        var api = new FakeNimApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = Client(api);

        var response = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(api.SendChatCalls).IsEqualTo(1);
    }

    /// <summary>A transient failure is retried with the same body after a backoff.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TransientFailureIsRetriedWithTheSameBody()
    {
        var attempt = 0;
        var api = new FakeNimApi
        {
            OnSendChat = _ =>
            {
                var current = attempt;
                attempt++;
                return current == 0
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            },
        };
        var client = Client(api);

        var response = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(api.SendChatCalls).IsEqualTo(CallsAfterOneTransientFailure);
    }

    /// <summary>A rejected request is retried with progressively less of it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RejectedRequestIsDowngraded()
    {
        List<NimChatRequest> seen = [];
        var api = new FakeNimApi
        {
            OnSendChat = request =>
            {
                seen.Add(request);
                return request.ReasoningEffort is null
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : new HttpResponseMessage(HttpStatusCode.BadRequest);
            },
        };
        var client = Client(api);

        var response = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(seen.Count).IsEqualTo(RequestsAfterOneDowngrade);
        await Assert.That(seen[0].ReasoningEffort).IsEqualTo("high");
        await Assert.That(seen[1].ReasoningEffort).IsNull();
    }

    /// <summary>A rejection that cannot be downgraded further is returned once the ladder is exhausted.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ExhaustedDowngradeLadderReturnsTheLastFailure()
    {
        var api = new FakeNimApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.BadRequest) };
        var client = Client(api);

        var response = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>A non-transient failure that cannot be downgraded is returned without retrying.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NonTransientFailureIsReturnedWithoutRetry()
    {
        var request = new NimChatRequest("m", [new NimChatMessage("user", NimContent.FromText("hi"))], MaxTokens: 100, Stream: false);
        var api = new FakeNimApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.Forbidden) };
        var client = Client(api);

        var response = await client.SendChatAsync(request, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(api.SendChatCalls).IsEqualTo(1);
    }

    /// <summary>Passing a null request is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullRequestThrows()
    {
        var client = Client(new());

        await Assert.That(async () => await client.SendChatAsync(null!, ModelTier.Default, CancellationToken.None))
            .Throws<ArgumentNullException>();
    }

    /// <summary>A successful listing call returns the upstream content.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SuccessfulListingReturnsContent()
    {
        NimModelList expected = new([new("nvidia/nemotron-3-super-120b-a12b")]);
        var api = new FakeNimApi { OnListModels = () => new StubApiResponse<NimModelList> { StatusCode = HttpStatusCode.OK, IsSuccessStatusCode = true, Content = expected } };
        var client = Client(api);

        var result = await client.ListModelsAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(expected);
    }

    /// <summary>A listing call that failed at the transport level reports no listing.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnreachableListingReturnsNull()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ListingUrl);
        var error = new ApiRequestException(request, HttpMethod.Get, new(), new HttpRequestException("The host could not be reached."));

        var api = new FakeNimApi { OnListModels = () => new StubApiResponse<NimModelList> { Error = error } };
        var client = Client(api);

        var result = await client.ListModelsAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>A listing call that failed with only a status code reports no listing.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task FailedStatusListingReturnsNull()
    {
        var api = new FakeNimApi { OnListModels = static () => new StubApiResponse<NimModelList> { StatusCode = HttpStatusCode.InternalServerError, IsSuccessStatusCode = false } };
        var client = Client(api);

        var result = await client.ListModelsAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>An attempt that never reached a status is made again.</summary>
    /// <param name="failure">The transport failure the first attempt raises.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect. A status the upstream returns was retried; an
    /// attempt that got no status at all escaped the ladder and failed the turn outright. A
    /// dropped connection and a header wait that ran out are the two ways NVIDIA's endpoints go
    /// quiet under load, so the turn was giving up on the most ordinary failure there is.
    /// </remarks>
    [Test]
    [Arguments(typeof(HttpRequestException))]
    [Arguments(typeof(TaskCanceledException))]
    [Arguments(typeof(IOException))]
    public async Task AttemptThatNeverReachedAStatusIsRetried(Type failure)
    {
        var attempt = 0;
        var api = new FakeNimApi
        {
            OnSendChat = _ =>
            {
                var current = attempt;
                attempt++;
                return current == 0
                    ? throw (Exception)Activator.CreateInstance(failure)!
                    : new HttpResponseMessage(HttpStatusCode.OK);
            },
        };

        var response = await Client(api).SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(attempt).IsEqualTo(CallsAfterOneTransientFailure);
    }

    /// <summary>A call this proxy abandoned on its own deadline is not asked again.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect. The deadline is the whole budget an attempt
    /// was given, so retrying one that ran out spends the same wait two more times — and the
    /// client, running a deadline of its own, gave up long before the third attempt came back.
    /// </remarks>
    [Test]
    public async Task ExpiredDeadlineIsNotRetried()
    {
        var api = new FakeNimApi { AnswerDelay = UnreachedAnswerDelay };
        var client = new NimClient(api, Retries, ImmediateDeadline, NoPacing, TimeProvider.System, NullLogger<NimClient>.Instance);

        _ = await Assert.That(async () => await client.SendChatAsync(StreamedRequest, ModelTier.Default, CancellationToken.None))
            .Throws<TaskCanceledException>();

        await Assert.That(api.SendChatCalls).IsEqualTo(1);
    }

    /// <summary>A non-streamed call is bounded by the completion budget, not the header wait.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// NIM sends nothing at all until a non-streamed answer has finished generating, so the wait
    /// for its headers is the generation. Bounding it by the header wait cut off every answer that
    /// took longer than one — which, on a reasoning model working through a long prompt, is most
    /// of the ones worth waiting for.
    /// </remarks>
    [Test]
    public async Task NonStreamedCallOutlivesTheHeaderWait()
    {
        var api = new FakeNimApi { AnswerDelay = SlowAnswerDelay, OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = new NimClient(api, Retries, ImmediateDeadline, NoPacing, TimeProvider.System, NullLogger<NimClient>.Instance);

        var response = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    /// <summary>A tier's configured header-wait override is used instead of the base budget.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect: the largest model in the catalogue can
    /// legitimately take well over the base header-wait budget to start streaming, and the fixed
    /// budget applied to every tier alike downgraded that turn to a weaker fallback model on every
    /// slow start. Proven end to end, through the real deadline the streamed call is bounded by,
    /// rather than only against <see cref="HttpTimeoutOptions.ReadSecondsFor"/> in isolation.
    /// </remarks>
    [Test]
    public async Task TierOverrideExtendsTheHeaderWaitForItsOwnTier()
    {
        var api = new FakeNimApi { AnswerDelay = SlowAnswerDelay, OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = new NimClient(api, Retries, TieredDeadline, NoPacing, TimeProvider.System, NullLogger<NimClient>.Instance);

        var response = await client.SendChatAsync(StreamedRequest, ModelTier.Opus, CancellationToken.None);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    /// <summary>A tier with no configured override is not granted another tier's extended budget.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task UnrelatedTierDoesNotBorrowAnotherTiersOverride()
    {
        var api = new FakeNimApi { AnswerDelay = SlowAnswerDelay };
        var client = new NimClient(api, Retries, TieredDeadline, NoPacing, TimeProvider.System, NullLogger<NimClient>.Instance);

        _ = await Assert.That(async () => await client.SendChatAsync(StreamedRequest, ModelTier.Sonnet, CancellationToken.None))
            .Throws<TaskCanceledException>();
    }

    /// <summary>A deadline that ran out is reported at a level an operator runs at.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ExpiredDeadlineIsReportedAsAWarning()
    {
        var api = new FakeNimApi { AnswerDelay = UnreachedAnswerDelay };
        var logger = new CapturingLogger<NimClient>();
        var client = new NimClient(api, Retries, ImmediateDeadline, NoPacing, TimeProvider.System, logger);

        _ = await Assert.That(async () => await client.SendChatAsync(StreamedRequest, ModelTier.Default, CancellationToken.None))
            .Throws<TaskCanceledException>();

        var expired = Warnings(logger).Find(static entry => entry.Message.Contains("did not answer", StringComparison.Ordinal));
        await Assert.That(expired.Message).IsNotNull();
        await Assert.That(expired.Message).Contains(StreamedRequest.Model);
    }

    /// <summary>A caller that has gone away is not retried for.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CancelledCallerIsNotRetriedFor()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var api = new FakeNimApi { OnSendChat = static _ => throw new TaskCanceledException() };

        _ = await Assert.That(async () => await Client(api).SendChatAsync(RequestWithReasoning, ModelTier.Default, cancelled.Token))
            .Throws<TaskCanceledException>();

        await Assert.That(api.SendChatCalls).IsEqualTo(1);
    }

    /// <summary>A spent retry budget is reported at a level an operator runs at.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The rejection itself is reported by the caller as the last status, which reads the same
    /// whether the proxy tried once or spent every attempt getting there. Without this record a
    /// turn that cost three upstream calls is indistinguishable from one that cost one.
    /// </remarks>
    [Test]
    public async Task ExhaustedRetriesAreReportedAsAWarning()
    {
        var api = new FakeNimApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        var logger = new CapturingLogger<NimClient>();
        var client = new NimClient(api, Retries, Timeouts, NoPacing, TimeProvider.System, logger);

        _ = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        var exhausted = Warnings(logger).Find(static entry => entry.Message.Contains("giving up", StringComparison.Ordinal));
        await Assert.That(exhausted.Message).IsNotNull();
        await Assert.That(exhausted.Message).Contains("503");
        await Assert.That(exhausted.Message).Contains(RequestWithReasoning.Model);
    }

    /// <summary>Each waited-out attempt is reported at a level an operator runs at.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ARetriedAttemptIsReportedAsAWarning()
    {
        var api = new FakeNimApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        var logger = new CapturingLogger<NimClient>();
        var client = new NimClient(api, Retries, Timeouts, NoPacing, TimeProvider.System, logger);

        _ = await client.SendChatAsync(RequestWithReasoning, ModelTier.Default, CancellationToken.None);

        var retrying = Warnings(logger).FindAll(static entry => entry.Message.Contains("retrying in", StringComparison.Ordinal));
        await Assert.That(retrying.Count).IsGreaterThan(0);
        await Assert.That(retrying[0].Message).Contains(RequestWithReasoning.Model);
    }

    /// <summary>Collects the entries that survive an operator running at warning level.</summary>
    /// <param name="logger">The logger the run wrote to.</param>
    /// <returns>The warning-and-above entries.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<LogEntry> Warnings(CapturingLogger<NimClient> logger) =>
        logger.Entries.FindAll(static entry => entry.Level >= LogLevel.Warning);

    /// <summary>Builds a client wired to a fake transport.</summary>
    /// <param name="api">The fake transport.</param>
    /// <returns>The client under test.</returns>
    private static NimClient Client(FakeNimApi api) =>
        new(api, Retries, Timeouts, NoPacing, TimeProvider.System, NullLogger<NimClient>.Instance);
}
