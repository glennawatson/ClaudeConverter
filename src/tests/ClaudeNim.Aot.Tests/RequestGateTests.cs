// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.RateLimiting;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the concurrency ceiling and sliding window <see cref="RequestGate"/> applies.</summary>
public sealed class RequestGateTests
{
    /// <summary>An unbounded gate always grants a lease.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnboundedGateGrantsALease()
    {
        using var gate = new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 0));

        using var lease = await gate.AcquireAsync(CancellationToken.None);

        await Assert.That(lease.IsAcquired).IsTrue();
    }

    /// <summary>A gate with a concurrency ceiling grants a lease inside the limit.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ConcurrencyBoundedGateGrantsALeaseWithinTheLimit()
    {
        using var gate = new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 2));

        using var lease = await gate.AcquireAsync(CancellationToken.None);

        await Assert.That(lease.IsAcquired).IsTrue();
    }

    /// <summary>A gate with a window limit grants a lease inside the limit.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task WindowBoundedGateGrantsALeaseWithinTheLimit()
    {
        using var gate = new RequestGate(new RateLimitOptions(RequestsPerWindow: 5, WindowSeconds: 60, MaxConcurrency: 0));

        using var lease = await gate.AcquireAsync(CancellationToken.None);

        await Assert.That(lease.IsAcquired).IsTrue();
    }

    /// <summary>Disposing a lease releases the permits it holds.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DisposingALeaseReleasesItsPermits()
    {
        using var gate = new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 1));

        var first = await gate.AcquireAsync(CancellationToken.None);
        await Assert.That(first.IsAcquired).IsTrue();
        first.Dispose();

        using var second = await gate.AcquireAsync(CancellationToken.None);

        await Assert.That(second.IsAcquired).IsTrue();
    }

    /// <summary>A cancelled acquisition on a bounded gate propagates cancellation.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CancelledAcquisitionThrows()
    {
        using var gate = new RequestGate(new RateLimitOptions(RequestsPerWindow: 0, MaxConcurrency: 1));

        using var first = await gate.AcquireAsync(CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.That(async () => await gate.AcquireAsync(cancelled.Token)).Throws<OperationCanceledException>();
    }

    /// <summary>Disposing the gate itself does not throw.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DisposingTheGateDoesNotThrow()
    {
        var gate = new RequestGate(new RateLimitOptions());

        await Assert.That(gate.Dispose).ThrowsNothing();
    }

    /// <summary>A null options argument is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullOptionsThrows() =>
        await Assert.That(static () => new RequestGate(null!)).Throws<ArgumentNullException>();
}
