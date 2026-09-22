// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;
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

    /// <summary>A request carrying reasoning controls, so a downgrade has something to drop.</summary>
    private static readonly NimChatRequest RequestWithReasoning = new(
        "nvidia/nemotron-3-super-120b-a12b",
        [new NimChatMessage("user", NimContent.FromText("hi"))],
        MaxTokens: 100,
        Stream: false,
        ReasoningEffort: "high");

    /// <summary>A single successful call is not retried.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SuccessfulCallIsNotRetried()
    {
        var api = new FakeNimApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = Client(api);

        var response = await client.SendChatAsync(RequestWithReasoning, CancellationToken.None);

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

        var response = await client.SendChatAsync(RequestWithReasoning, CancellationToken.None);

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

        var response = await client.SendChatAsync(RequestWithReasoning, CancellationToken.None);

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

        var response = await client.SendChatAsync(RequestWithReasoning, CancellationToken.None);

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

        var response = await client.SendChatAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(api.SendChatCalls).IsEqualTo(1);
    }

    /// <summary>Passing a null request is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullRequestThrows()
    {
        var client = Client(new());

        await Assert.That(async () => await client.SendChatAsync(null!, CancellationToken.None))
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

    /// <summary>Builds a client wired to a fake transport.</summary>
    /// <param name="api">The fake transport.</param>
    /// <returns>The client under test.</returns>
    private static NimClient Client(FakeNimApi api) =>
        new(api, Retries, Timeouts, TimeProvider.System, NullLogger<NimClient>.Instance);
}
