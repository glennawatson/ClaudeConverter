// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Threading.RateLimiting;

namespace ClaudeNim.Aot.RateLimiting;

/// <summary>The permits held for one upstream call, released when disposed.</summary>
/// <param name="Concurrency">The in-flight permit, or <see langword="null"/> when unbounded.</param>
/// <param name="Window">The sliding-window permit, or <see langword="null"/> when unbounded.</param>
[System.Diagnostics.DebuggerDisplay("RequestLease: {IsAcquired}")]
public readonly record struct RequestLease(RateLimitLease? Concurrency, RateLimitLease? Window) : IDisposable
{
    /// <summary>Gets a value indicating whether every required permit was granted.</summary>
    public bool IsAcquired =>
        (Concurrency?.IsAcquired ?? true) && (Window?.IsAcquired ?? true);

    /// <summary>Returns the permits to their limiters.</summary>
    public void Dispose()
    {
        Window?.Dispose();
        Concurrency?.Dispose();
    }
}
