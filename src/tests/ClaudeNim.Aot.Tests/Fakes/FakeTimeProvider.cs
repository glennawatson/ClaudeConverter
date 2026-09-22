// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="TimeProvider"/> double whose clock only moves when told to.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    /// <summary>The current time, advanced only by <see cref="Advance"/>.</summary>
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="delta">How far to advance.</param>
    internal void Advance(TimeSpan delta) => _now += delta;
}
