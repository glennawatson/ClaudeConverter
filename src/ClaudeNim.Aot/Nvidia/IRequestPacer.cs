// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Spaces every outbound NIM call evenly, rather than letting several leave in a burst.</summary>
public interface IRequestPacer
{
    /// <summary>Waits until this call's turn to leave, then reserves the next one.</summary>
    /// <param name="cancellationToken">Abandons the wait when the caller no longer needs the call made.</param>
    /// <returns>A task that completes once it is this call's turn.</returns>
    ValueTask WaitForTurnAsync(CancellationToken cancellationToken);
}
