// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Routing;

/// <summary>Chooses the NVIDIA NIM model that serves an incoming Claude model name.</summary>
public interface IModelRouter
{
    /// <summary>Routes one model name.</summary>
    /// <param name="requestedModel">The model identifier the client sent.</param>
    /// <returns>The routing outcome.</returns>
    ResolvedModel Resolve(string requestedModel);

    /// <summary>Classifies a model name into a capability tier.</summary>
    /// <param name="requestedModel">The model identifier the client sent.</param>
    /// <returns>The tier the name falls into.</returns>
    ModelTier Classify(string requestedModel);
}
