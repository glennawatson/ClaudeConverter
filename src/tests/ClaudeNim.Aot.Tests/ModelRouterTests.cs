// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers routing a Claude model name onto a configured NVIDIA NIM model.</summary>
public sealed class ModelRouterTests
{
    /// <summary>The upstream model identifier used across the gateway-encoding fixtures.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-ultra-550b-a55b";

    /// <summary>The model a tier resolves to across the fallback fixtures.</summary>
    private const string FallbackFirst = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The first alternative across the fallback fixtures.</summary>
    private const string FallbackSecond = "nvidia/nemotron-3.5-lightning-30b-a3b";

    /// <summary>The second alternative across the fallback fixtures.</summary>
    private const string FallbackThird = "deepseek-ai/deepseek-v4.1-flash";

    /// <summary>The number of alternatives the ordered-chain fixture configures.</summary>
    private const int ConfiguredFallbackCount = 2;

    /// <summary>The Sonnet-tier name the classification and chain fixtures ask for.</summary>
    private const string SonnetName = "claude-sonnet-5";

    /// <summary>A gateway identifier the proxy itself advertised is decoded, not classified.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task GatewayIdentifierWinsOutright()
    {
        var router = new ModelRouter(new ModelRoutingOptions());
        var encoded = GatewayModelId.Encode(UpstreamModel);

        var resolved = router.Resolve(encoded);

        await Assert.That(resolved.NimModel).IsEqualTo(UpstreamModel);
        await Assert.That(resolved.Tier).IsEqualTo(ModelTier.Default);
        await Assert.That(resolved.ThinkingEnabled).IsTrue();
    }

    /// <summary>A no-thinking gateway identifier decodes with reasoning suppressed.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NoThinkingGatewayIdentifierDisablesReasoning()
    {
        var router = new ModelRouter(new ModelRoutingOptions());
        var encoded = GatewayModelId.EncodeWithoutThinking(UpstreamModel);

        var resolved = router.Resolve(encoded);

        await Assert.That(resolved.ThinkingEnabled).IsFalse();
    }

    /// <summary>A tier name is classified and routed to its configured model.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task TierNameRoutesToItsConfiguredModel()
    {
        var options = new ModelRoutingOptions(Opus: "vendor/opus-model");
        var router = new ModelRouter(options);

        var resolved = router.Resolve("claude-opus-5");

        await Assert.That(resolved.Tier).IsEqualTo(ModelTier.Opus);
        await Assert.That(resolved.NimModel).IsEqualTo("vendor/opus-model");
    }

    /// <summary>An unconfigured tier falls back to the configured default.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnconfiguredTierFallsBackToDefault()
    {
        var options = new ModelRoutingOptions(Default: "vendor/default-model");
        var router = new ModelRouter(options);

        var resolved = router.Resolve(SonnetName);

        await Assert.That(resolved.NimModel).IsEqualTo("vendor/default-model");
    }

    /// <summary>An unconfigured tier and an unconfigured default fall back to the fixed fallback model.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NoConfigurationFallsBackToTheFixedModel()
    {
        var router = new ModelRouter(new ModelRoutingOptions(Default: string.Empty));

        var resolved = router.Resolve("claude-haiku-4-5");

        await Assert.That(resolved.NimModel).IsEqualTo(ModelRoutingOptions.FallbackModel);
    }

    /// <summary>An unrecognised name classifies as the default tier.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnrecognisedNameClassifiesAsDefault()
    {
        var router = new ModelRouter(new ModelRoutingOptions());

        await Assert.That(router.Classify("gpt-4")).IsEqualTo(ModelTier.Default);
    }

    /// <summary>An empty name classifies as the default tier.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyNameClassifiesAsDefault() =>
        await Assert.That(new ModelRouter(new ModelRoutingOptions()).Classify(string.Empty)).IsEqualTo(ModelTier.Default);

    /// <summary>A tier-specific thinking override wins over the global setting.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TierSpecificThinkingOverrideWins()
    {
        var options = new ModelRoutingOptions(EnableThinking: true, EnableOpusThinking: false);
        var router = new ModelRouter(options);

        var resolved = router.Resolve("claude-opus-5");

        await Assert.That(resolved.ThinkingEnabled).IsFalse();
    }

    /// <summary>Haiku classification and Sonnet classification both resolve correctly.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task HaikuAndSonnetClassify()
    {
        var router = new ModelRouter(new ModelRoutingOptions());

        await Assert.That(router.Classify("claude-haiku-4-5")).IsEqualTo(ModelTier.Haiku);
        await Assert.That(router.Classify(SonnetName)).IsEqualTo(ModelTier.Sonnet);
    }

    /// <summary>A tier's fallback chain is read in the order it was configured.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task TierFallbackChainIsResolvedInOrder()
    {
        var options = new ModelRoutingOptions(
            Sonnet: FallbackFirst,
            SonnetFallbacks: $"{FallbackSecond}, {FallbackThird}");
        var router = new ModelRouter(options);

        var resolved = router.Resolve(SonnetName);

        await Assert.That(resolved.NimModel).IsEqualTo(FallbackFirst);
        await Assert.That(resolved.Alternatives.Count).IsEqualTo(ConfiguredFallbackCount);
        await Assert.That(resolved.Alternatives[0]).IsEqualTo(FallbackSecond);
        await Assert.That(resolved.Alternatives[1]).IsEqualTo(FallbackThird);
    }

    /// <summary>A chain never names the model the tier already resolved to, nor repeats one.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Either would spend an upstream call, and the client's patience, asking a model that has
    /// just said it cannot serve the turn.
    /// </remarks>
    [Test]
    public async Task ChainDropsTheResolvedModelAndDuplicates()
    {
        var options = new ModelRoutingOptions(
            Sonnet: FallbackFirst,
            SonnetFallbacks: $"{FallbackFirst},{FallbackSecond},{FallbackSecond}");
        var router = new ModelRouter(options);

        var resolved = router.Resolve(SonnetName);

        await Assert.That(resolved.Alternatives.Count).IsEqualTo(1);
        await Assert.That(resolved.Alternatives[0]).IsEqualTo(FallbackSecond);
    }

    /// <summary>A tier with no configured chain resolves to no alternatives at all.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnconfiguredChainHasNoAlternatives() =>
        await Assert.That(new ModelRouter(new ModelRoutingOptions()).Resolve(SonnetName).Alternatives.Count)
            .IsEqualTo(0);

    /// <summary>A model named by gateway identifier is never substituted for another.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// The client picked that model from the listing this proxy advertised. Answering as a
    /// different one would be answering as a model it did not ask for, having asked by name.
    /// </remarks>
    [Test]
    public async Task GatewayIdentifierGetsNoFallbackChain()
    {
        var options = new ModelRoutingOptions(DefaultFallbacks: FallbackSecond);
        var router = new ModelRouter(options);

        var resolved = router.Resolve(GatewayModelId.Encode(UpstreamModel));

        await Assert.That(resolved.Alternatives.Count).IsEqualTo(0);
    }
}
