// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers that an advertised identifier is the shape a route can actually carry.</summary>
/// <remarks>
/// <c>GET /v1/models/{id}</c> was registered against a single path segment
/// (<c>/{modelId}</c>), which routing splits on <c>/</c> before the handler ever runs. Every
/// identifier this proxy advertises contains at least two slashes, so the route matched nothing
/// real ever sent it. The route itself cannot be exercised without a hosted server; what these
/// tests pin is the fact the fix responds to — that a gateway identifier is not one segment.
/// </remarks>
public sealed class GatewayModelIdRouteTests
{
    /// <summary>The fewest slashes any advertised identifier is expected to carry.</summary>
    private const int MinimumSegmentSeparators = 2;

    /// <summary>An encoded identifier contains the slashes a single route segment cannot carry.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EncodedIdentifierContainsMultipleSegments()
    {
        var encoded = GatewayModelId.Encode("nvidia/nemotron-3-ultra-550b-a55b");

        await Assert.That(CountSlashes(encoded)).IsGreaterThanOrEqualTo(MinimumSegmentSeparators);
    }

    /// <summary>The no-thinking encoding also contains multiple segments.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NoThinkingEncodedIdentifierContainsMultipleSegments()
    {
        var encoded = GatewayModelId.EncodeWithoutThinking("z-ai/glm-5.3");

        await Assert.That(CountSlashes(encoded)).IsGreaterThanOrEqualTo(MinimumSegmentSeparators);
    }

    /// <summary>Counts the path separators an identifier carries.</summary>
    /// <param name="identifier">The identifier to count.</param>
    /// <returns>The number of <c>/</c> characters.</returns>
    private static int CountSlashes(string identifier)
    {
        var count = 0;
        foreach (var c in identifier)
        {
            if (c == '/')
            {
                count++;
            }
        }

        return count;
    }
}
