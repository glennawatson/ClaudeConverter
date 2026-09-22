// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Http.Headers;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Decides whether an upstream failure is worth another attempt, and how long to wait.</summary>
/// <remarks>
/// <para>
/// This is deliberately hand-rolled rather than taken from a resilience library. The whole policy
/// is one predicate and one delay calculation, both of which want to be read and reasoned about
/// directly — and a proxy compiled ahead of time has good reason not to take a dependency it does
/// not need.
/// </para>
/// <para>
/// The delay is exponential with equal jitter: attempt <c>n</c> waits half of <c>base × 2ⁿ</c>
/// plus a random share of the other half, clamped to the configured ceiling. Jittered rather than
/// fixed because the failure being retried is usually congestion, and a fixed schedule returns
/// every waiting caller at the same instant. Half the window is waited unconditionally because
/// full jitter has no floor: it will happily come back in twenty milliseconds, which against a
/// saturated endpoint is not a second chance but the same burst repeated, and it collapses a
/// budget meant to span seconds into one that is spent before the upstream has recovered.
/// </para>
/// </remarks>
public static class RetrySchedule
{
    /// <summary>The factor each successive delay window is multiplied by.</summary>
    private const double BackoffFactor = 2.0;

    /// <summary>How many equal parts a delay window is split into: one waited, one jittered.</summary>
    private const double WindowParts = 2.0;

    /// <summary>The statuses worth sending the same request again for.</summary>
    /// <remarks>
    /// Each of these is the upstream saying "not now" rather than "not this". NVIDIA reports
    /// saturation as a plain 503 and a transient fault as a 500, and both clear on their own.
    /// </remarks>
    private static readonly HttpStatusCode[] TransientStatuses =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    /// <summary>Determines whether a response is worth another attempt.</summary>
    /// <param name="status">The status the upstream returned.</param>
    /// <returns><see langword="true"/> when the same request may succeed later.</returns>
    public static bool IsTransient(HttpStatusCode status)
    {
        foreach (var candidate in TransientStatuses)
        {
            if (status == candidate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Works out how long to wait before an attempt.</summary>
    /// <param name="attempt">The attempt about to be made, counting the first as one.</param>
    /// <param name="options">The configured retry behaviour.</param>
    /// <param name="retryAfter">The upstream's own instruction, when it sent one.</param>
    /// <param name="now">The current time, used only for the absolute form of that instruction.</param>
    /// <returns>The delay to wait.</returns>
    /// <remarks>
    /// An upstream that says when to come back is believed, because it knows something the
    /// schedule does not. It is still clamped to the ceiling so a long instruction cannot hold a
    /// client's turn open indefinitely.
    /// </remarks>
    public static TimeSpan Delay(
        int attempt,
        RetryOptions options,
        RetryConditionHeaderValue? retryAfter,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ceiling = TimeSpan.FromMilliseconds(options.MaxDelayMilliseconds);

        if (FromRetryAfter(retryAfter, now) is { } instructed)
        {
            return instructed > ceiling ? ceiling : instructed;
        }

        var scaled = options.BaseDelayMilliseconds * Math.Pow(BackoffFactor, attempt < 1 ? 0 : attempt - 1);
        var window = Math.Min(scaled, options.MaxDelayMilliseconds);

        return TimeSpan.FromMilliseconds(options.UseJitter ? Spread(window) : window);
    }

    /// <summary>Picks a point at random inside the upper half of a delay window.</summary>
    /// <param name="window">The width of the window, in milliseconds.</param>
    /// <returns>The chosen delay, in milliseconds, which is never less than half the window.</returns>
    /// <remarks>
    /// This is scheduling, not secrecy. The only property required of the number is that two
    /// callers rarely pick the same one, which a cryptographic generator would deliver at a cost
    /// nothing here benefits from.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Jitter spreads retry timing; it is not used for anything security-sensitive.")]
    private static double Spread(double window)
    {
        var half = window / WindowParts;
        return half + (Random.Shared.NextDouble() * half);
    }

    /// <summary>Reads the delay an upstream asked for.</summary>
    /// <param name="retryAfter">The header the upstream sent, which may be absent.</param>
    /// <param name="now">The current time, for the absolute form of the header.</param>
    /// <returns>The delay, or <see langword="null"/> when none was given.</returns>
    private static TimeSpan? FromRetryAfter(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is not { } date)
        {
            return null;
        }

        var wait = date - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
