// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers building the advertised model listing from the upstream catalogue.</summary>
public sealed class NimModelCatalogTests
{
    /// <summary>The NIM model identifier used across the fixtures that do not care which model it is.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The identifier of a model the catalogue's own profile marks as reasoning-capable.</summary>
    private const string ReasoningModel = "nvidia/nemotron-3-ultra-550b-a55b";

    /// <summary>The Claude alias identifier used across the alias fixtures.</summary>
    private const string SonnetAliasId = "claude-sonnet-5";

    /// <summary>The number of variants a reasoning-capable model advertises.</summary>
    private const int ReasoningModelVariants = 2;

    /// <summary>The number of upstream calls expected once the cache has expired and refreshed.</summary>
    private const int CallsAfterCacheExpiry = 2;

    /// <summary>A cache lifetime the fixtures advance past to force a refresh.</summary>
    private static readonly TimeSpan CacheLifetimeElapsed = TimeSpan.FromMinutes(2);

    /// <summary>A reasoning-capable model advertises two variants: with and without thinking.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReasoningModelAdvertisesTwoVariants()
    {
        var client = new FakeNimClient { OnListModels = static () => new([new(ReasoningModel)]) };
        using var catalog = Catalog(client);

        var models = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(models.Count).IsGreaterThanOrEqualTo(ReasoningModelVariants);
    }

    /// <summary>A failed upstream listing falls back to the built-in profiles.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task FailedListingFallsBackToBuiltInProfiles()
    {
        var client = new FakeNimClient { OnListModels = static () => null };
        using var catalog = Catalog(client);

        var models = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(models.Count).IsGreaterThan(0);
    }

    /// <summary>The cache is not refreshed again before it expires.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CacheIsNotRefreshedBeforeExpiry()
    {
        var calls = 0;
        NimModelList? ListModels()
        {
            calls++;
            return new([new(UpstreamModel)]);
        }

        var client = new FakeNimClient { OnListModels = ListModels };
        using var catalog = Catalog(client);

        _ = await catalog.GetModelsAsync(CancellationToken.None);
        _ = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(calls).IsEqualTo(1);
    }

    /// <summary>The cache refreshes again once its lifetime has elapsed.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CacheRefreshesOnceExpired()
    {
        var calls = 0;
        NimModelList? ListModels()
        {
            calls++;
            return new([new(UpstreamModel)]);
        }

        var client = new FakeNimClient { OnListModels = ListModels };
        var time = new FakeTimeProvider();
        using var catalog = new NimModelCatalog(
            client,
            new ModelCatalogOptions(CacheMinutes: 1),
            new ModelRoutingOptions(),
            NullLogger<NimModelCatalog>.Instance,
            time);

        _ = await catalog.GetModelsAsync(CancellationToken.None);
        time.Advance(CacheLifetimeElapsed);
        _ = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(calls).IsEqualTo(CallsAfterCacheExpiry);
    }

    /// <summary>Finding a known model by identifier returns it, case-insensitively.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FindModelIsCaseInsensitive()
    {
        var client = new FakeNimClient { OnListModels = static () => new([new(UpstreamModel)]) };
        using var catalog = Catalog(client);
        var models = await catalog.GetModelsAsync(CancellationToken.None);

        var found = await catalog.FindModelAsync(models[0].Id.ToUpperInvariant(), CancellationToken.None);

        await Assert.That(found).IsNotNull();
    }

    /// <summary>Finding an unknown model returns null.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task FindingAnUnknownModelReturnsNull()
    {
        var client = new FakeNimClient { OnListModels = static () => new([new(UpstreamModel)]) };
        using var catalog = Catalog(client);

        var found = await catalog.FindModelAsync("no-such-model", CancellationToken.None);

        await Assert.That(found).IsNull();
    }

    /// <summary>A refresh that cannot reach NVIDIA keeps serving the listing it already had.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The fallback listing is the built-in subset, which is smaller than a live one. Letting a
    /// failed refresh replace a good listing with it would shrink a client's model picker every
    /// time the upstream had a bad minute, and a model that was there a moment ago would stop
    /// being selectable. Stale is not wrong; the next expiry tries again.
    /// </remarks>
    [Test]
    public async Task FailedRefreshKeepsThePreviousListing()
    {
        var reachable = true;
        var client = new FakeNimClient { OnListModels = () => reachable ? new([new(UpstreamModel)]) : null };
        var time = new FakeTimeProvider();

        using var catalog = new NimModelCatalog(
            client,
            new ModelCatalogOptions(CacheMinutes: 1),
            new ModelRoutingOptions(),
            NullLogger<NimModelCatalog>.Instance,
            time);

        var live = await catalog.GetModelsAsync(CancellationToken.None);

        reachable = false;
        time.Advance(CacheLifetimeElapsed);
        var afterFailure = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(afterFailure.Count).IsEqualTo(live.Count);
        await Assert.That(afterFailure.Exists(static m => m.Id.Contains(UpstreamModel, StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>An unreachable listing on the very first call still yields the built-in models.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// There is nothing stale to keep on a cold start, so the built-in subset is the best listing
    /// available and a picker with some models in it beats one with none.
    /// </remarks>
    [Test]
    public async Task UnreachableListingOnAColdStartFallsBackToTheBuiltIns()
    {
        var client = new FakeNimClient { OnListModels = static () => null };

        using var catalog = new NimModelCatalog(
            client,
            new ModelCatalogOptions(CacheMinutes: 1),
            new ModelRoutingOptions(),
            NullLogger<NimModelCatalog>.Instance,
            new FakeTimeProvider());

        var models = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(models.Count).IsGreaterThan(0);
    }

    /// <summary>Claude aliases are advertised when configured, routing through the tier mapping.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ClaudeAliasesAreAdvertisedWhenConfigured()
    {
        var client = new FakeNimClient { OnListModels = static () => new([]) };
        using var catalog = new NimModelCatalog(
            client,
            new ModelCatalogOptions(),
            new ModelRoutingOptions(Opus: "vendor/opus"),
            NullLogger<NimModelCatalog>.Instance,
            TimeProvider.System);

        var models = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(models.Exists(static m => m.Id == "claude-opus-5")).IsTrue();
        await Assert.That(models.Exists(static m => m.Id == "claude-opus-5-5")).IsTrue();
    }

    /// <summary>Claude aliases are omitted when not configured.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ClaudeAliasesAreOmittedWhenNotConfigured()
    {
        var client = new FakeNimClient { OnListModels = static () => new([]) };
        using var catalog = new NimModelCatalog(
            client,
            new ModelCatalogOptions(AdvertiseClaudeAliases: false),
            new ModelRoutingOptions(),
            NullLogger<NimModelCatalog>.Instance,
            TimeProvider.System);

        var models = await catalog.GetModelsAsync(CancellationToken.None);

        await Assert.That(models.Exists(static m => m.Id == "claude-opus-5")).IsFalse();
    }

    /// <summary>Concurrent callers share one refresh rather than issuing one each.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ConcurrentCallersShareOneRefresh()
    {
        var calls = 0;
        NimModelList? ListModels()
        {
            calls++;
            return new([new(UpstreamModel)]);
        }

        var client = new FakeNimClient { OnListModels = ListModels };
        using var catalog = Catalog(client);

        var first = catalog.GetModelsAsync(CancellationToken.None);
        var second = catalog.GetModelsAsync(CancellationToken.None);
        _ = await first;
        _ = await second;

        await Assert.That(calls).IsEqualTo(1);
    }

    /// <summary>Uses <see cref="FakeNimModelCatalog"/> directly to cover a fixed-listing double.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FixedCatalogReportsItsConfiguredModels()
    {
        List<Anthropic.ModelDescriptor> models = [new(SonnetAliasId, "Claude Sonnet 5", DateTimeOffset.UnixEpoch, 1, 1, default)];
        var catalog = new FakeNimModelCatalog(models);

        var listed = await catalog.GetModelsAsync(CancellationToken.None);
        var found = await catalog.FindModelAsync(SonnetAliasId, CancellationToken.None);
        var missing = await catalog.FindModelAsync("missing", CancellationToken.None);

        await Assert.That(listed).IsEqualTo(models);
        await Assert.That(found?.Id).IsEqualTo(SonnetAliasId);
        await Assert.That(missing).IsNull();
    }

    /// <summary>Builds a catalogue wired to a fake client with a long-lived cache.</summary>
    /// <param name="client">The fake client.</param>
    /// <returns>The catalogue under test.</returns>
    private static NimModelCatalog Catalog(FakeNimClient client) =>
        new(client, new ModelCatalogOptions(), new ModelRoutingOptions(), NullLogger<NimModelCatalog>.Instance, TimeProvider.System);
}
