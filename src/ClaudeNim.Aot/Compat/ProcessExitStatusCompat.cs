// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics;

namespace ClaudeNim.Aot.Compat;

/// <summary>Waits for successful process exit across supported frameworks.</summary>
internal static class ProcessExitStatusCompat
{
    /// <summary>Returns true only for a successful exit, checking signal termination on .NET 11.</summary>
    /// <param name="process">The started process.</param>
    /// <param name="cancellationToken">Stops waiting for exit.</param>
    /// <returns>Whether the process exited successfully; .NET 10 represents signals as nonzero exit codes.</returns>
    internal static async Task<bool> SucceededAsync(Process process, CancellationToken cancellationToken)
    {
#if NET11_0_OR_GREATER
        var status = await process.WaitForExitStatusAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return !status.Canceled && status.Signal is null && status.ExitCode == 0;
#else
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0;
#endif
    }
}
