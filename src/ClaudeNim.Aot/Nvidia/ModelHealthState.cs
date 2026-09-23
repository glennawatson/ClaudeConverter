// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Tracks one model's recent failures and, once it has failed enough of them, its cooldown.</summary>
/// <remarks>
/// A class rather than a record, and locked rather than lock-free: several turns can report a
/// failure or a success for the same model at once, and a cooldown decided from a half-updated
/// failure count is worse than the small amount of contention a lock costs here.
/// </remarks>
internal sealed class ModelHealthState
{
    /// <summary>The object every read and write of this state is taken under.</summary>
    private readonly Lock _gate = new();

    /// <summary>The number of failures reported since the last success.</summary>
    private int _consecutiveFailures;

    /// <summary>When the cooldown this model is in ends, or <see langword="null"/> when it is not in one.</summary>
    private DateTimeOffset? _cooldownUntil;

    /// <summary>Determines whether the model is currently in cooldown.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when a cooldown is active.</returns>
    internal bool IsInCooldown(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _cooldownUntil is { } until && now < until;
        }
    }

    /// <summary>Records a failure, starting or extending the cooldown once the threshold is reached.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="threshold">How many consecutive failures start a cooldown.</param>
    /// <param name="cooldown">How long the cooldown lasts once started.</param>
    internal void RecordFailure(DateTimeOffset now, int threshold, TimeSpan cooldown)
    {
        lock (_gate)
        {
            _consecutiveFailures++;

            if (_consecutiveFailures >= threshold)
            {
                _cooldownUntil = now + cooldown;
            }
        }
    }

    /// <summary>Records a success, clearing any cooldown immediately rather than waiting it out.</summary>
    internal void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _cooldownUntil = null;
        }
    }
}
