// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Supplies the model listing the proxy advertises to Claude clients.</summary>
public interface INimModelCatalog
{
    /// <summary>Gets every model the proxy can route to, in listing order.</summary>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The advertised models.</returns>
    ValueTask<List<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);

    /// <summary>Finds one advertised model by identifier.</summary>
    /// <param name="modelId">The identifier to look up.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The model, or <see langword="null"/> when it is not advertised.</returns>
    ValueTask<ModelDescriptor?> FindModelAsync(string modelId, CancellationToken cancellationToken);
}
