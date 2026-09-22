// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Nvidia;
using Microsoft.AspNetCore.Http;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers mapping a status code onto the Anthropic error class a client acts on.</summary>
public sealed class AnthropicErrorsTests
{
    /// <summary>Every documented status maps onto its documented error class.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DocumentedStatusesMapOntoTheirErrorClasses()
    {
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status400BadRequest)).IsEqualTo("invalid_request_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status401Unauthorized)).IsEqualTo("authentication_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status403Forbidden)).IsEqualTo("permission_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status404NotFound)).IsEqualTo("not_found_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status413PayloadTooLarge)).IsEqualTo("request_too_large");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status429TooManyRequests)).IsEqualTo("rate_limit_error");
        await Assert.That(AnthropicErrors.TypeFor(StatusCodes.Status503ServiceUnavailable)).IsEqualTo("overloaded_error");
        await Assert.That(AnthropicErrors.TypeFor(AnthropicErrors.StatusOverloaded)).IsEqualTo("overloaded_error");
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
        await Assert.That(StreamErrorTypes.FromUpstream(StatusCodes.Status429TooManyRequests)).IsEqualTo("rate_limit_error");
        await Assert.That(StreamErrorTypes.FromUpstream(null)).IsEqualTo("api_error");
    }
}
