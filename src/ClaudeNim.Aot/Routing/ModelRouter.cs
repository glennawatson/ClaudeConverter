// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Routing;

/// <summary>Routes Claude model names onto configured NVIDIA NIM models.</summary>
/// <param name="Options">The configured tier mapping.</param>
/// <remarks>
/// A client may address a model three ways: by a gateway identifier this proxy advertised, by a
/// Claude tier name such as <c>claude-opus-5</c>, or by anything else. The first wins outright
/// because the client is naming a model it was offered; the second consults the tier overrides;
/// the third falls back to the configured default.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ModelRouter: {ToString(),nq}")]
public sealed record ModelRouter(ModelRoutingOptions Options) : IModelRouter
{
    /// <inheritdoc/>
    public ResolvedModel Resolve(string requestedModel)
    {
        var model = requestedModel ?? string.Empty;

        // A gateway identifier names one model outright. Substituting another would answer as a
        // model the client did not ask for, having asked for one by name — so it gets no chain.
        if (GatewayModelId.TryDecode(model, out var gatewayModel, out var gatewayThinking))
        {
            return new(model, gatewayModel, ModelTier.Default, gatewayThinking);
        }

        var tier = Classify(model);
        var nimModel = ResolveNimModel(tier);

        return new(model, nimModel, tier, ResolveThinking(tier), ResolveFallbacks(tier, nimModel));
    }

    /// <inheritdoc/>
    public ModelTier Classify(string requestedModel)
    {
        if (string.IsNullOrEmpty(requestedModel))
        {
            return ModelTier.Default;
        }

        if (requestedModel.Contains("opus", StringComparison.OrdinalIgnoreCase))
        {
            return ModelTier.Opus;
        }

        if (requestedModel.Contains("haiku", StringComparison.OrdinalIgnoreCase))
        {
            return ModelTier.Haiku;
        }

        return requestedModel.Contains("sonnet", StringComparison.OrdinalIgnoreCase)
            ? ModelTier.Sonnet
            : ModelTier.Default;
    }

    /// <summary>Determines whether a chain already names a model.</summary>
    /// <param name="chain">The chain built so far.</param>
    /// <param name="candidate">The model being considered.</param>
    /// <returns><see langword="true"/> when the chain already holds that name.</returns>
    private static bool Names(List<string> chain, string candidate)
    {
        foreach (var named in chain)
        {
            if (string.Equals(named, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves the NIM model for a given tier.</summary>
    /// <param name="tier">The tier to resolve.</param>
    /// <returns>The NIM model identifier for the tier, or the fallback model.</returns>
    private string ResolveNimModel(ModelTier tier)
    {
        var configured = tier switch
        {
            ModelTier.Opus => Options.Opus,
            ModelTier.Sonnet => Options.Sonnet,
            ModelTier.Haiku => Options.Haiku,
            _ => string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return string.IsNullOrWhiteSpace(Options.Default)
            ? ModelRoutingOptions.FallbackModel
            : Options.Default;
    }

    /// <summary>Reads the ordered fallback chain configured for a tier.</summary>
    /// <param name="tier">The tier to resolve.</param>
    /// <param name="nimModel">The model already chosen for the tier, which the chain never repeats.</param>
    /// <returns>The models to try in order, or an empty list when the tier names none.</returns>
    /// <remarks>
    /// The chosen model is filtered out rather than left in, because a chain that names it again
    /// would ask a model that has just said it cannot serve the turn, at the cost of one more
    /// upstream call and the client's patience. Duplicates go for the same reason.
    /// </remarks>
    private List<string> ResolveFallbacks(ModelTier tier, string nimModel)
    {
        var configured = tier switch
        {
            ModelTier.Opus => Options.OpusFallbacks,
            ModelTier.Sonnet => Options.SonnetFallbacks,
            ModelTier.Haiku => Options.HaikuFallbacks,
            _ => Options.DefaultFallbacks,
        };

        if (string.IsNullOrWhiteSpace(configured))
        {
            return [];
        }

        List<string> chain = [];

        foreach (var candidate in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.Equals(candidate, nimModel, StringComparison.OrdinalIgnoreCase) && !Names(chain, candidate))
            {
                chain.Add(candidate);
            }
        }

        return chain;
    }

    /// <summary>Resolves whether thinking is enabled for a given tier.</summary>
    /// <param name="tier">The tier to resolve.</param>
    /// <returns><see langword="true"/> when thinking is enabled for the tier.</returns>
    private bool ResolveThinking(ModelTier tier) => tier switch
    {
        ModelTier.Opus => Options.EnableOpusThinking ?? Options.EnableThinking,
        ModelTier.Sonnet => Options.EnableSonnetThinking ?? Options.EnableThinking,
        ModelTier.Haiku => Options.EnableHaikuThinking ?? Options.EnableThinking,
        _ => Options.EnableThinking,
    };
}
