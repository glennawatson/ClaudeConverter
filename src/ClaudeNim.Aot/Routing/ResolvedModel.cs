// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Routing;

/// <summary>The outcome of routing one request's model name.</summary>
/// <param name="RequestedModel">The model identifier the client sent.</param>
/// <param name="NimModel">The NVIDIA NIM model that will serve the request.</param>
/// <param name="Tier">The tier the requested model was classified into.</param>
/// <param name="ThinkingEnabled">Whether reasoning was requested for this tier.</param>
[System.Diagnostics.DebuggerDisplay("ResolvedModel: {ToString(),nq}")]
public readonly record struct ResolvedModel(
    string RequestedModel,
    string NimModel,
    ModelTier Tier,
    bool ThinkingEnabled);
