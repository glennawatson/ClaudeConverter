// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="INimModelCatalog"/> double backed by a fixed list.</summary>
/// <param name="models">The models the catalogue reports.</param>
internal sealed class FakeNimModelCatalog(List<ModelDescriptor> models) : INimModelCatalog
{
    /// <summary>Gets the models the catalogue reports.</summary>
    public List<ModelDescriptor> Models { get; } = models;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<List<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Models);

    /// <inheritdoc/>
    public ValueTask<ModelDescriptor?> FindModelAsync(string modelId, CancellationToken cancellationToken)
    {
        foreach (var model in Models)
        {
            if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<ModelDescriptor?>(model);
            }
        }

        return ValueTask.FromResult<ModelDescriptor?>(null);
    }
}
