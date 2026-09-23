// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the spacing a request pacer enforces between outbound calls.</summary>
/// <remarks>
/// <see cref="RequestPacer"/> waits out its remaining gap through <c>Task.Delay(TimeSpan, TimeProvider, ...)</c>,
/// which only accelerates under a <see cref="TimeProvider"/> that implements <c>CreateTimer</c> — a hand-rolled
/// fake that only overrides <c>GetUtcNow</c> does not, so these fixtures use a gap small enough in real time to
/// keep the suite fast, the same way the retry-backoff fixtures elsewhere in this project do.
/// </remarks>
public sealed class RequestPacerTests
{
    /// <summary>The minimum gap shared by the fixtures, small enough that waiting it out costs the suite nothing.</summary>
    private const int MinGapMilliseconds = 50;

    /// <summary>How long past the gap a fixture waits, to allow for scheduling jitter.</summary>
    private const int PastTheGapMilliseconds = MinGapMilliseconds * 2;

    /// <summary>A second call made before the gap has passed waits out the remainder of it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task SecondCallWaitsOutTheRemainderOfTheGap()
    {
        var pacer = new RequestPacer(new RateLimitOptions(MinGapMilliseconds: MinGapMilliseconds), TimeProvider.System);

        await pacer.WaitForTurnAsync(CancellationToken.None);

        var waiting = pacer.WaitForTurnAsync(CancellationToken.None).AsTask();
        await Assert.That(waiting.IsCompleted).IsFalse();

        await waiting;
    }

    /// <summary>A call made after the gap has already passed is released immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CallMadeAfterTheGapIsReleasedImmediately()
    {
        var pacer = new RequestPacer(new RateLimitOptions(MinGapMilliseconds: MinGapMilliseconds), TimeProvider.System);

        await pacer.WaitForTurnAsync(CancellationToken.None);
        await Task.Delay(PastTheGapMilliseconds, CancellationToken.None);

        var next = pacer.WaitForTurnAsync(CancellationToken.None).AsTask();
        await Assert.That(next.IsCompleted).IsTrue();
    }

    /// <summary>A zero gap never makes a caller wait.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ZeroGapNeverWaits()
    {
        var pacer = new RequestPacer(new RateLimitOptions(MinGapMilliseconds: 0), TimeProvider.System);

        var first = pacer.WaitForTurnAsync(CancellationToken.None).AsTask();
        await Assert.That(first.IsCompleted).IsTrue();

        var second = pacer.WaitForTurnAsync(CancellationToken.None).AsTask();
        await Assert.That(second.IsCompleted).IsTrue();
    }
}
