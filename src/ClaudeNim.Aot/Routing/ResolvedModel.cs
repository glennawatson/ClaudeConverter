// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Routing;

/// <summary>The outcome of routing one request's model name.</summary>
/// <param name="RequestedModel">The model identifier the client sent.</param>
/// <param name="NimModel">The NVIDIA NIM model that will serve the request.</param>
/// <param name="Tier">The tier the requested model was classified into.</param>
/// <param name="ThinkingEnabled">Whether reasoning was requested for this tier.</param>
/// <param name="Fallbacks">The models to try in order, should the one above turn out to be unavailable.</param>
/// <remarks>
/// The fallbacks travel with the routing outcome rather than being looked up again later, because
/// the tier that produced them is decided here and nowhere else along the turn knows it.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ResolvedModel: {ToString(),nq}")]
public readonly record struct ResolvedModel(
    string RequestedModel,
    string NimModel,
    ModelTier Tier,
    bool ThinkingEnabled,
    IReadOnlyList<string>? Fallbacks = null)
{
    /// <summary>Gets the models to try in order, should the resolved one be unavailable.</summary>
    public IReadOnlyList<string> Alternatives => Fallbacks ?? [];
}
