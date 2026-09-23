// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Enforces a minimum spacing between outbound NIM calls.</summary>
/// <param name="options">The configured minimum gap.</param>
/// <param name="time">The clock the spacing is measured against.</param>
/// <remarks>
/// A window and a concurrency ceiling both bound total volume, but neither stops several calls
/// leaving at once — walking a fallback chain can ask three models within the same second and
/// still sit well inside both. This paces every physical call evenly instead, the same shape of
/// traffic a single well-behaved client produces, which is what NVIDIA's free endpoints appear to
/// actually react well to.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("RequestPacer: {options.MinGapMilliseconds}ms")]
public sealed class RequestPacer(RateLimitOptions options, TimeProvider time) : IRequestPacer
{
    /// <summary>Serialises the check-and-reserve step below, so two waiters cannot both claim the same slot.</summary>
    private readonly Lock _gate = new();

    /// <summary>The last moment a call was released to go, or <see cref="DateTimeOffset.MinValue"/> before the first.</summary>
    private DateTimeOffset _lastReleasedUtc = DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public async ValueTask WaitForTurnAsync(CancellationToken cancellationToken)
    {
        if (options.MinGapMilliseconds <= 0)
        {
            return;
        }

        var minGap = TimeSpan.FromMilliseconds(options.MinGapMilliseconds);

        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = time.GetUtcNow();
                var elapsed = now - _lastReleasedUtc;
                if (elapsed >= minGap)
                {
                    _lastReleasedUtc = now;
                    return;
                }

                wait = minGap - elapsed;
            }

            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
        }
    }
}
