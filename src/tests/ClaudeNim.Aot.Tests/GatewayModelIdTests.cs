// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the round trip a model identifier makes through a client's model picker.</summary>
public sealed class GatewayModelIdTests
{
    /// <summary>A reasoning-capable model used across the round-trip cases.</summary>
    private const string ReasoningModel = "z-ai/glm-5.3";

    /// <summary>An advertised identifier decodes back to the model it named.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EncodedIdentifierRoundTrips()
    {
        var encoded = GatewayModelId.Encode("nvidia/nemotron-3-ultra-550b-a55b");

        await Assert.That(GatewayModelId.TryDecode(encoded, out var model, out var thinking)).IsTrue();
        await Assert.That(model).IsEqualTo("nvidia/nemotron-3-ultra-550b-a55b");
        await Assert.That(thinking).IsTrue();
    }

    /// <summary>The no-thinking encoding round trips and reports reasoning as suppressed.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NoThinkingIdentifierRoundTrips()
    {
        var encoded = GatewayModelId.EncodeWithoutThinking(ReasoningModel);

        await Assert.That(GatewayModelId.TryDecode(encoded, out var model, out var thinking)).IsTrue();
        await Assert.That(model).IsEqualTo(ReasoningModel);
        await Assert.That(thinking).IsFalse();
    }

    /// <summary>The no-thinking encoding carries the marker Claude clients read.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// Claude clients treat any identifier containing <c>claude-3-</c> as not supporting extended
    /// thinking. That is the only channel available for saying so, which makes it load-bearing.
    /// </remarks>
    [Test]
    public async Task NoThinkingIdentifierCarriesTheClientMarker() =>
        await Assert.That(GatewayModelId.EncodeWithoutThinking(ReasoningModel)).Contains("claude-3-");

    /// <summary>A plain Claude tier name is not mistaken for a gateway identifier.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ClaudeTierNameIsNotAGatewayIdentifier() =>
        await Assert.That(GatewayModelId.TryDecode("claude-opus-5", out _, out _)).IsFalse();

    /// <summary>A prefix with nothing after it is rejected rather than decoding to an empty model.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyModelIsRejected() =>
        await Assert.That(GatewayModelId.TryDecode("anthropic/nvidia_nim/", out _, out _)).IsFalse();
}
