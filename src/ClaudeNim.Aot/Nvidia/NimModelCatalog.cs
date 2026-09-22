// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Builds the advertised model listing from the live NVIDIA NIM catalogue.</summary>
/// <remarks>
/// <para>
/// This is what lets a Claude client discover what it can actually run. The listing is built
/// from the models the configured credential can reach, filtered to those that can answer a
/// chat completion, and encoded so that selecting one routes back to the same model.
/// </para>
/// <para>
/// Each reasoning-capable model is advertised twice, once with reasoning and once without,
/// because a reasoning model with its trace turned off is a genuinely different choice for a
/// coding session and there is no other way for a client to express it.
/// </para>
/// <para>
/// A failed fetch falls back to the models the proxy has stated knowledge of rather than
/// returning nothing, so the picker still works when the upstream is briefly unreachable.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimModelCatalog: {_client}")]
public sealed class NimModelCatalog : INimModelCatalog, IDisposable
{
    // NVIDIA's listing carries a creation stamp that is not always a plausible Unix time. A
    // year below this is treated as absent rather than surfaced as a 1970s publication date.
    /// <summary>The earliest year a model creation timestamp is considered plausible.</summary>
    private const int EarliestPlausiblePublishYear = 2020;

    // Each model may be advertised with and without reasoning.
    /// <summary>The number of variants each model may be advertised with (with and without reasoning).</summary>
    private const int VariantsPerModel = 2;

    /// <summary>The publication date used when a model's creation timestamp is missing or implausible.</summary>
    private static readonly DateTimeOffset PublishedFallback = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The NIM client used to fetch the catalogue.</summary>
    private readonly INimClient _client;

    /// <summary>The catalogue settings.</summary>
    private readonly ModelCatalogOptions _options;

    /// <summary>The configured tier mapping, used for the Claude aliases.</summary>
    private readonly ModelRoutingOptions _routing;

    /// <summary>The diagnostic log.</summary>
    private readonly ILogger<NimModelCatalog> _logger;

    /// <summary>The clock the cache expiry is measured against.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The gate that keeps concurrent callers from refreshing the catalogue at once.</summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>The cached listing, or null when nothing has been fetched yet.</summary>
    private List<ModelDescriptor>? _cached;

    /// <summary>The time at which the cache was populated.</summary>
    private DateTimeOffset _cachedAt;

    /// <summary>Initializes a new instance of the <see cref="NimModelCatalog"/> class.</summary>
    /// <param name="client">The NIM client used to fetch the catalogue.</param>
    /// <param name="options">The catalogue settings.</param>
    /// <param name="routing">The configured tier mapping, used for the Claude aliases.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <param name="timeProvider">The clock the cache expiry is measured against.</param>
    public NimModelCatalog(
        INimClient client,
        ModelCatalogOptions options,
        ModelRoutingOptions routing,
        ILogger<NimModelCatalog> logger,
        TimeProvider timeProvider)
    {
        _client = client;
        _options = options;
        _routing = routing;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async ValueTask<List<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var cached = _cached;
        if (cached is not null && !IsExpired())
        {
            return cached;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && !IsExpired())
            {
                return _cached;
            }

            var (models, fromUpstream) = await BuildAsync(cancellationToken).ConfigureAwait(false);
            _cachedAt = _timeProvider.GetUtcNow();

            // A refresh that could not reach NVIDIA produces the built-in subset, which is a worse
            // listing than the one already held. Replacing a good listing with it would shrink a
            // client's model picker every time the upstream had a bad minute, so the stale one is
            // kept and the clock restarted; the next expiry tries again.
            if (!fromUpstream && _cached is { Count: > 0 } stale)
            {
                NvidiaLog.ServingStaleModelList(_logger, stale.Count);
                return stale;
            }

            _cached = models;
            return models;
        }
        finally
        {
            _ = _refreshGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ModelDescriptor?> FindModelAsync(string modelId, CancellationToken cancellationToken)
    {
        var models = await GetModelsAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < models.Count; i++)
        {
            if (string.Equals(models[i].Id, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return models[i];
            }
        }

        return null;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _refreshGate.Dispose();

    /// <summary>Converts a Unix timestamp to a plausible publication date.</summary>
    /// <param name="created">The creation timestamp from NVIDIA's catalogue.</param>
    /// <returns>The publication date, or the fallback when the timestamp is missing or implausible.</returns>
    private static DateTimeOffset ToPublished(long created)
    {
        if (created <= 0)
        {
            return PublishedFallback;
        }

        var candidate = DateTimeOffset.FromUnixTimeSeconds(created);
        return candidate.Year < EarliestPlausiblePublishYear ? PublishedFallback : candidate;
    }

    /// <summary>Determines whether the cached listing has expired.</summary>
    /// <returns><see langword="true"/> when the cache has exceeded its configured lifetime.</returns>
    private bool IsExpired() =>
        _timeProvider.GetUtcNow() - _cachedAt >= TimeSpan.FromMinutes(_options.CacheMinutes);

    /// <summary>Builds the complete advertised model listing.</summary>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The advertised listing, and whether it came from NVIDIA rather than the built-ins.</returns>
    private async ValueTask<CatalogRefresh> BuildAsync(CancellationToken cancellationToken)
    {
        var (upstream, fromUpstream) = await FetchIdentifiersAsync(cancellationToken).ConfigureAwait(false);
        var models = new List<ModelDescriptor>(upstream.Count * VariantsPerModel);

        for (var i = 0; i < upstream.Count; i++)
        {
            AddVariants(models, upstream[i].Id, upstream[i].Published);
        }

        if (_options.AdvertiseClaudeAliases)
        {
            AddClaudeAliases(models);
        }

        return new(models, fromUpstream);
    }

    /// <summary>Fetches the model identifiers and published dates from NVIDIA's catalogue.</summary>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The identifiers and dates of chat-capable models, and whether NVIDIA supplied them.</returns>
    private async ValueTask<CatalogEntries> FetchIdentifiersAsync(CancellationToken cancellationToken)
    {
        var listing = await TryListAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<NimCatalogEntry>();
        var data = listing?.Data;

        if (data is not null)
        {
            for (var i = 0; i < data.Count; i++)
            {
                var model = data[i];
                if (NimModelCatalogDefaults.IsChatModel(model.Id))
                {
                    entries.Add(new(model.Id, ToPublished(model.Created)));
                }
            }
        }

        if (entries.Count > 0)
        {
            return new(entries, true);
        }

        NvidiaLog.UsingBuiltInModelList(_logger);
        foreach (var profile in NimModelCatalogDefaults.Profiles)
        {
            entries.Add(new(profile.Id, PublishedFallback));
        }

        return new(entries, false);
    }

    /// <summary>Fetches the upstream listing, tolerating an unreachable endpoint or timeout.</summary>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The upstream listing, or <see langword="null"/> when it could not be read.</returns>
    private async ValueTask<NimModelList?> TryListAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            NvidiaLog.ModelListingUnreachable(_logger, error);
            return null;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            NvidiaLog.ModelListingTimedOut(_logger, error);
            return null;
        }
    }

    /// <summary>Builds the facts about a model from its profile or defaults.</summary>
    /// <param name="nimModel">The NIM model identifier.</param>
    /// <param name="thinkingOverride">Forces the reasoning capability, or <see langword="null"/> to keep the profile's own.</param>
    /// <returns>The model facts.</returns>
    private ModelFacts BuildFacts(string nimModel, bool? thinkingOverride) =>
        NimModelCatalogDefaults.FindProfile(nimModel) is { } profile
            ? FactsFromProfile(profile, thinkingOverride ?? true)
            : FactsFromDefaults(nimModel, thinkingOverride ?? true);

    /// <summary>Builds model facts from a known profile.</summary>
    /// <param name="profile">The model profile.</param>
    /// <param name="allowThinking">Whether thinking capability is permitted.</param>
    /// <returns>The model facts.</returns>
    private ModelFacts FactsFromProfile(NimModelProfile profile, bool allowThinking) =>
        new(
            profile.DisplayName,
            profile.MaxInputTokens ?? _options.DefaultContextWindow,
            profile.MaxOutputTokens ?? _options.DefaultMaxOutputTokens,
            profile.SupportsVision,
            profile.SupportsThinking && allowThinking);

    // A model the proxy has no stated knowledge of is still advertised, using the configured
    // defaults. Hiding it would conceal a model the credential can genuinely reach.
    /// <summary>Builds model facts from configured defaults for a model without a profile.</summary>
    /// <param name="nimModel">The NIM model identifier.</param>
    /// <param name="allowThinking">Whether thinking capability is permitted.</param>
    /// <returns>The model facts.</returns>
    private ModelFacts FactsFromDefaults(string nimModel, bool allowThinking) =>
        new(
            NimModelCatalogDefaults.DeriveDisplayName(nimModel),
            _options.DefaultContextWindow,
            _options.DefaultMaxOutputTokens,
            false,
            allowThinking);

    /// <summary>Adds model variants to the listing, with and without thinking if applicable.</summary>
    /// <param name="models">The models list to populate.</param>
    /// <param name="nimModel">The NIM model identifier.</param>
    /// <param name="published">The model's publication date.</param>
    private void AddVariants(List<ModelDescriptor> models, string nimModel, DateTimeOffset published)
    {
        var facts = BuildFacts(nimModel, thinkingOverride: null);

        models.Add(new(
            GatewayModelId.Encode(nimModel),
            facts.DisplayName,
            published,
            facts.MaxInputTokens,
            facts.MaxTokens,
            facts.Capabilities));

        if (!facts.Thinking)
        {
            return;
        }

        models.Add(new(
            GatewayModelId.EncodeWithoutThinking(nimModel),
            $"{facts.DisplayName} (no thinking)",
            published,
            facts.MaxInputTokens,
            facts.MaxTokens,
            facts.WithoutThinking().Capabilities));
    }

    /// <summary>Adds the standard Claude model aliases.</summary>
    /// <param name="models">The models list to populate.</param>
    private void AddClaudeAliases(List<ModelDescriptor> models)
    {
        AddAlias(models, "claude-opus-5", "Claude Opus 5", _routing.Opus, _routing.EnableOpusThinking);
        AddAlias(models, "claude-sonnet-5", "Claude Sonnet 5", _routing.Sonnet, _routing.EnableSonnetThinking);
        AddAlias(models, "claude-haiku-4-5", "Claude Haiku 4.5", _routing.Haiku, _routing.EnableHaikuThinking);
    }

    /// <summary>Adds a single Claude model alias.</summary>
    /// <param name="models">The models list to populate.</param>
    /// <param name="aliasId">The alias model ID.</param>
    /// <param name="tierName">The tier name for display.</param>
    /// <param name="configuredModel">The configured NIM model to route to.</param>
    /// <param name="tierThinking">The tier-specific thinking override, or <see langword="null"/> for the global setting.</param>
    private void AddAlias(
        List<ModelDescriptor> models,
        string aliasId,
        string tierName,
        string configuredModel,
        bool? tierThinking)
    {
        var target = ResolveTarget(configuredModel);
        var facts = BuildFacts(target, tierThinking ?? _routing.EnableThinking);

        models.Add(new(
            aliasId,
            $"{tierName} via {facts.DisplayName}",
            PublishedFallback,
            facts.MaxInputTokens,
            facts.MaxTokens,
            facts.Capabilities));
    }

    /// <summary>Resolves a configured model reference to an actual model, with fallbacks.</summary>
    /// <param name="configuredModel">The configured model reference.</param>
    /// <returns>The resolved model identifier.</returns>
    private string ResolveTarget(string configuredModel)
    {
        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            return configuredModel;
        }

        return string.IsNullOrWhiteSpace(_routing.Default)
            ? ModelRoutingOptions.FallbackModel
            : _routing.Default;
    }
}
