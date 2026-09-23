// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Endpoints.Anthropic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the locally answered token-counting route.</summary>
public sealed class TokenCountEndpointExtensionsTests
{
    /// <summary>The character length of the fixture user text.</summary>
    private const int SampleTextLength = 40;

    /// <summary>A well-formed request is answered with an estimated count.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task WellFormedRequestIsAnsweredWithAnEstimate()
    {
        List<AnthropicMessage> messages = [new(AnthropicMessage.UserRole, MessageContent.FromText(new('a', SampleTextLength)))];
        var request = new TokenCountRequest("claude-sonnet-5", messages);

        var result = TokenCountEndpointExtensions.CountTokens(request);

        await Assert.That(((JsonHttpResult<TokenCountResponse>)result).Value.InputTokens).IsGreaterThan(0);
    }

    /// <summary>A body carrying no messages is answered rather than crashing.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect. The estimator refuses a null list outright,
    /// and nothing guarded it, so the question "how large is an empty conversation" reached
    /// Kestrel as an unhandled exception and came back a 500 instead of a number.
    /// </remarks>
    [Test]
    public async Task RequestWithNoMessagesIsAnswered()
    {
        var result = TokenCountEndpointExtensions.CountTokens(new("claude-sonnet-5", null!));

        await Assert.That(((JsonHttpResult<TokenCountResponse>)result).Value.InputTokens).IsGreaterThanOrEqualTo(0);
    }

    /// <summary>A missing request body is rejected with a clean error rather than crashing.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task MissingRequestBodyIsRejected()
    {
        var result = TokenCountEndpointExtensions.CountTokens(null!);

        await Assert.That(((JsonHttpResult<ErrorResponse>)result).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }
}
