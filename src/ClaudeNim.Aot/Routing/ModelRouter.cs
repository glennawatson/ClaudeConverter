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

        if (GatewayModelId.TryDecode(model, out var gatewayModel, out var gatewayThinking))
        {
            return new(model, gatewayModel, ModelTier.Default, gatewayThinking);
        }

        var tier = Classify(model);
        return new(model, ResolveNimModel(tier), tier, ResolveThinking(tier));
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
