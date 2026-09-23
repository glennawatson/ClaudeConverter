// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers resolving the per-tier header-wait override for a streamed turn.</summary>
public sealed class HttpTimeoutOptionsTests
{
    /// <summary>The base header-wait budget used across the fixtures.</summary>
    private const int BaseReadSeconds = 120;

    /// <summary>The override configured for the Opus tier in the fixtures that set one.</summary>
    private const int OpusOverrideSeconds = 300;

    /// <summary>A tier with no configured override falls back to the base budget.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnconfiguredTierUsesTheBaseBudget() =>
        await Assert.That(new HttpTimeoutOptions().ReadSecondsFor(ModelTier.Opus))
            .IsEqualTo(BaseReadSeconds);

    /// <summary>A tier the default fallback rather than a Claude tier also uses the base budget.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DefaultTierAlwaysUsesTheBaseBudget()
    {
        var options = new HttpTimeoutOptions(OpusReadSeconds: OpusOverrideSeconds);

        await Assert.That(options.ReadSecondsFor(ModelTier.Default)).IsEqualTo(BaseReadSeconds);
    }

    /// <summary>A configured override wins over the base budget for its own tier.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ConfiguredOverrideWinsForItsOwnTier()
    {
        var options = new HttpTimeoutOptions(OpusReadSeconds: OpusOverrideSeconds);

        await Assert.That(options.ReadSecondsFor(ModelTier.Opus)).IsEqualTo(OpusOverrideSeconds);
        await Assert.That(options.ReadSecondsFor(ModelTier.Sonnet)).IsEqualTo(BaseReadSeconds);
        await Assert.That(options.ReadSecondsFor(ModelTier.Haiku)).IsEqualTo(BaseReadSeconds);
    }
}
