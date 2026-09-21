// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Threading.RateLimiting;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.RateLimiting;

/// <summary>Applies a sliding request window and a concurrency ceiling to upstream calls.</summary>
/// <remarks>
/// <para>
/// Free NVIDIA endpoints answer HTTP 429 rather than queueing, and a coding client reacts to a
/// 429 by retrying, which makes the overload worse. Shaping traffic here turns a burst into a
/// short wait instead.
/// </para>
/// <para>
/// Both limiters come from <c>System.Threading.RateLimiting</c>. A sliding window is used rather
/// than a fixed one because a fixed bucket lets twice the limit through either side of a
/// boundary, which is exactly the burst the upstream rejects.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("RequestGate: {_concurrency}")]
public sealed class RequestGate : IRequestGate, IDisposable
{
    // The window divided into segments; more segments make the window slide more smoothly at
    // the cost of tracking more buckets.
    /// <summary>The number of segments the sliding window is divided into.</summary>
    private const int WindowSegments = 8;

    /// <summary>The concurrency limiter, or null when no concurrency limit is configured.</summary>
    private readonly ConcurrencyLimiter? _concurrency;

    /// <summary>The sliding window rate limiter, or null when no rate limit is configured.</summary>
    private readonly SlidingWindowRateLimiter? _window;

    /// <summary>Initializes a new instance of the <see cref="RequestGate"/> class.</summary>
    /// <param name="options">The configured limits.</param>
    public RequestGate(RateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxConcurrency > 0)
        {
            _concurrency = new(new ConcurrencyLimiterOptions { PermitLimit = options.MaxConcurrency, QueueLimit = int.MaxValue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst, });
        }

        if (options.RequestsPerWindow > 0 && options.WindowSeconds > 0)
        {
            _window = new(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = options.RequestsPerWindow,
                Window = TimeSpan.FromSeconds(options.WindowSeconds),
                SegmentsPerWindow = WindowSegments,
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        }
    }

    /// <inheritdoc/>
    public async ValueTask<RequestLease> AcquireAsync(CancellationToken cancellationToken)
    {
        // The in-flight permit is taken first. Taking the window permit first would spend rate
        // while the call is still queued behind other requests.
        RateLimitLease? concurrency = null;
        if (_concurrency is not null)
        {
            concurrency = await _concurrency.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            RateLimitLease? window = null;
            if (_window is not null)
            {
                window = await _window.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            }

            return new(concurrency, window);
        }
        catch (OperationCanceledException)
        {
            concurrency?.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _window?.Dispose();
        _concurrency?.Dispose();
    }
}
