// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Tracks which NIM models have recently failed, so a fallback chain can skip a dead one.</summary>
/// <param name="options">The configured failure threshold and cooldown length.</param>
/// <param name="time">The clock cooldowns are measured against.</param>
[System.Diagnostics.DebuggerDisplay("ModelHealthTracker: {_models.Count} tracked")]
public sealed class ModelHealthTracker(ModelHealthOptions options, TimeProvider time) : IModelHealthTracker
{
    /// <summary>The state tracked for each model that has ever failed.</summary>
    /// <remarks>
    /// A model that has never failed has no entry at all, rather than one created the first time it
    /// is asked about: almost every model in a deployment's catalogue is never routed to, and giving
    /// each of them an entry the moment the tracker is asked about them would track models nothing
    /// is actually using.
    /// </remarks>
    private readonly ConcurrentDictionary<string, ModelHealthState> _models = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool IsInCooldown(string nimModel) =>
        _models.TryGetValue(nimModel, out var state) && state.IsInCooldown(time.GetUtcNow());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkUnavailable(string nimModel) =>
        _models.GetOrAdd(nimModel, static _ => new())
            .RecordFailure(time.GetUtcNow(), options.FailureThreshold, TimeSpan.FromSeconds(options.CooldownSeconds));

    /// <inheritdoc/>
    public void MarkAvailable(string nimModel)
    {
        if (_models.TryGetValue(nimModel, out var state))
        {
            state.RecordSuccess();
        }
    }
}
