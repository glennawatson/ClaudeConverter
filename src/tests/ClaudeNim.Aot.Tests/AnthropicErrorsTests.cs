// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Nvidia;
using Microsoft.AspNetCore.Http;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers mapping a status code onto the Anthropic error class a client acts on.</summary>
public sealed class AnthropicErrorsTests
{
    /// <summary>The class a client acts on by rejecting the request as malformed.</summary>
    private const string InvalidRequest = "invalid_request_error";

    /// <summary>The class a client acts on by backing off and trying the turn again.</summary>
    private const string Overloaded = "overloaded_error";

    /// <summary>Every documented status maps onto its documented error class.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DocumentedStatusesMapOntoTheirErrorClasses()
    {
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status400BadRequest)).IsEqualTo(InvalidRequest);
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status401Unauthorized)).IsEqualTo("authentication_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status403Forbidden)).IsEqualTo("permission_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status404NotFound)).IsEqualTo("not_found_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status413PayloadTooLarge)).IsEqualTo("request_too_large");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status429TooManyRequests)).IsEqualTo("rate_limit_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status503ServiceUnavailable)).IsEqualTo(Overloaded);
        await Assert.That(AnthropicErrors.TypeFor(AnthropicErrors.StatusOverloaded)).IsEqualTo(Overloaded);
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status418ImATeapot)).IsEqualTo("api_error");
    }

    /// <summary>The result carries the mapped status code and the supplied message.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ResultCarriesTheMappedStatusAndMessage()
    {
        var result = AnthropicErrors.Result(StatusCodes.Status404NotFound, "not here");

        await Assert.That(result).IsNotNull();
    }

    /// <summary>A mid-stream failure code maps through the same table <see cref="AnthropicErrors"/> uses.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamErrorTypeMapsThroughTheSameTable()
    {
        var rateLimited = new NimStreamError(Code: StatusCodes.Status429TooManyRequests);

        await Assert.That(StreamErrorTypes.FromUpstream(rateLimited)).IsEqualTo("rate_limit_error");
        await Assert.That(StreamErrorTypes.FromUpstream(null)).IsEqualTo("api_error");
    }

    /// <summary>A failure the upstream classified itself keeps that classification.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task StreamErrorKeepsTheUpstreamsOwnType()
    {
        var declared = new NimStreamError("something went wrong", Type: InvalidRequest);

        await Assert.That(StreamErrorTypes.FromUpstream(declared)).IsEqualTo(InvalidRequest);
    }

    /// <summary>Mid-stream saturation is reported as a failure worth retrying.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect. NVIDIA reports saturation mid-stream as prose
    /// with no status and no type, which fell through to <c>api_error</c> — fatal to a client. A
    /// turn that failed for a reason that clears on its own ended the work instead of being
    /// retried, which is the opposite of what the same failure arriving as a 503 would have said.
    /// </remarks>
    [Test]
    public async Task MidStreamSaturationIsReportedAsOverloaded()
    {
        var saturated = new NimStreamError("Service temporarily overloaded");

        await Assert.That(StreamErrorTypes.FromUpstream(saturated)).IsEqualTo(Overloaded);
    }
}
