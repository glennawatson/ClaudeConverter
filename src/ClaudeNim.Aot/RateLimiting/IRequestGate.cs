// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.RateLimiting;

/// <summary>Bounds how fast, and how many at a time, requests reach the upstream.</summary>
public interface IRequestGate
{
    /// <summary>Waits until the caller may issue an upstream request.</summary>
    /// <param name="cancellationToken">Abandons the wait when the client disconnects.</param>
    /// <returns>A lease that must be disposed once the upstream call has finished.</returns>
    ValueTask<RequestLease> AcquireAsync(CancellationToken cancellationToken);
}
