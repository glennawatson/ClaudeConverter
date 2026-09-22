// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="IModelRouter"/> double that resolves every request to a fixed outcome.</summary>
/// <param name="resolved">The outcome every call to <see cref="Resolve"/> returns.</param>
internal sealed class FakeModelRouter(ResolvedModel resolved) : IModelRouter
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResolvedModel Resolve(string requestedModel) => resolved;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ModelTier Classify(string requestedModel) => resolved.Tier;
}
