// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Http.Headers;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers which upstream failures are retried and how long the backoff waits.</summary>
public sealed class RetryScheduleTests
{
    /// <summary>The base delay used by the doubling and jitter fixtures.</summary>
    private const int BaseDelayMilliseconds = 1_000;

    /// <summary>A ceiling high enough that the doubling cases are never clamped.</summary>
    private const int UnreachedCeilingMilliseconds = 100_000;

    /// <summary>The narrow ceiling the clamping case is bounded by.</summary>
    private const int NarrowCeilingMilliseconds = 3_000;

    /// <summary>An attempt number comfortably past where the narrow ceiling clamps the delay.</summary>
    private const int AttemptWellPastTheCeiling = 10;

    /// <summary>The first attempt of a turn.</summary>
    private const int FirstAttempt = 1;

    /// <summary>The second attempt of a turn.</summary>
    private const int SecondAttempt = 2;

    /// <summary>The third attempt of a turn.</summary>
    private const int ThirdAttempt = 3;

    /// <summary>The factor each successive delay is expected to grow by.</summary>
    private const int ExpectedGrowthFactor = 2;

    /// <summary>How long an excessive Retry-After instruction asks the fixture to wait.</summary>
    private const int ExcessiveRetryAfterMinutes = 10;

    /// <summary>The instant used as "now" for the absolute Retry-After cases.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The delay a relative Retry-After header carries in its fixture.</summary>
    private static readonly TimeSpan RelativeRetryAfter = TimeSpan.FromSeconds(5);

    /// <summary>How far ahead of "now" an absolute Retry-After header points in its fixture.</summary>
    private static readonly TimeSpan AbsoluteRetryAfterOffset = TimeSpan.FromSeconds(10);

    /// <summary>The narrow ceiling the Retry-After clamping case is bounded by.</summary>
    private static readonly TimeSpan NarrowRetryAfterCeiling = TimeSpan.FromMilliseconds(5_000);

    /// <summary>The statuses that mean "not now" and should be retried.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task TransientStatusesAreRetried()
    {
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.ServiceUnavailable)).IsTrue();
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.TooManyRequests)).IsTrue();
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.InternalServerError)).IsTrue();
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.GatewayTimeout)).IsTrue();
    }

    /// <summary>A status that means "not this" is never retried.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RejectionStatusesAreNotRetried()
    {
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.BadRequest)).IsFalse();
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.Unauthorized)).IsFalse();
        await Assert.That(RetrySchedule.IsTransient(HttpStatusCode.OK)).IsFalse();
    }

    /// <summary>The delay doubles with each attempt, up to the configured ceiling.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DelayDoublesUpToTheCeiling()
    {
        var options = new RetryOptions(
            BaseDelayMilliseconds: BaseDelayMilliseconds,
            MaxDelayMilliseconds: UnreachedCeilingMilliseconds,
            UseJitter: false);

        var first = RetrySchedule.Delay(FirstAttempt, options, null, Now).TotalMilliseconds;
        var second = RetrySchedule.Delay(SecondAttempt, options, null, Now).TotalMilliseconds;
        var third = RetrySchedule.Delay(ThirdAttempt, options, null, Now).TotalMilliseconds;

        await Assert.That(first).IsEqualTo(BaseDelayMilliseconds);
        await Assert.That(second).IsEqualTo(first * ExpectedGrowthFactor);
        await Assert.That(third).IsEqualTo(second * ExpectedGrowthFactor);
    }

    /// <summary>The delay never exceeds the configured ceiling.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DelayIsClampedToTheCeiling()
    {
        var options = new RetryOptions(
            BaseDelayMilliseconds: BaseDelayMilliseconds,
            MaxDelayMilliseconds: NarrowCeilingMilliseconds,
            UseJitter: false);

        var delay = RetrySchedule.Delay(AttemptWellPastTheCeiling, options, null, Now).TotalMilliseconds;

        await Assert.That(delay).IsEqualTo(NarrowCeilingMilliseconds);
    }

    /// <summary>Jitter keeps the delay inside the window rather than always at its edge.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task JitterStaysWithinTheWindow()
    {
        var options = new RetryOptions(
            BaseDelayMilliseconds: BaseDelayMilliseconds,
            MaxDelayMilliseconds: UnreachedCeilingMilliseconds);

        var delay = RetrySchedule.Delay(FirstAttempt, options, null, Now);

        await Assert.That(delay.TotalMilliseconds).IsGreaterThanOrEqualTo(0);
        await Assert.That(delay.TotalMilliseconds).IsLessThanOrEqualTo(BaseDelayMilliseconds);
    }

    /// <summary>A relative Retry-After header is honoured over the computed backoff.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RelativeRetryAfterIsHonoured()
    {
        var options = new RetryOptions(
            BaseDelayMilliseconds: BaseDelayMilliseconds,
            MaxDelayMilliseconds: UnreachedCeilingMilliseconds);
        var retryAfter = new RetryConditionHeaderValue(RelativeRetryAfter);

        var delay = RetrySchedule.Delay(FirstAttempt, options, retryAfter, Now);

        await Assert.That(delay).IsEqualTo(RelativeRetryAfter);
    }

    /// <summary>An absolute Retry-After header is measured against the supplied clock.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AbsoluteRetryAfterIsMeasuredFromNow()
    {
        var options = new RetryOptions(
            BaseDelayMilliseconds: BaseDelayMilliseconds,
            MaxDelayMilliseconds: UnreachedCeilingMilliseconds);
        var retryAfter = new RetryConditionHeaderValue(Now + AbsoluteRetryAfterOffset);

        var delay = RetrySchedule.Delay(FirstAttempt, options, retryAfter, Now);

        await Assert.That(delay).IsEqualTo(AbsoluteRetryAfterOffset);
    }

    /// <summary>A Retry-After instruction is still clamped to the configured ceiling.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RetryAfterIsClampedToTheCeiling()
    {
        var options = new RetryOptions(
            BaseDelayMilliseconds: BaseDelayMilliseconds,
            MaxDelayMilliseconds: (int)NarrowRetryAfterCeiling.TotalMilliseconds);
        var retryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(ExcessiveRetryAfterMinutes));

        var delay = RetrySchedule.Delay(FirstAttempt, options, retryAfter, Now);

        await Assert.That(delay).IsEqualTo(NarrowRetryAfterCeiling);
    }
}
