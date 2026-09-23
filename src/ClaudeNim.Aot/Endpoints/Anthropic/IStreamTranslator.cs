// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>Reads an upstream stream to completion, emitting one protocol's own events as it goes.</summary>
/// <remarks>One instance serves exactly one attempt at one streamed turn.</remarks>
public interface IStreamTranslator
{
    /// <summary>Reads the upstream stream to completion, emitting events as it goes.</summary>
    /// <param name="upstream">The NIM response body.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="inputTokens">The prompt size to report.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>How the attempt ended.</returns>
    ValueTask<StreamTurnOutcome> TranslateAsync(
        Stream upstream,
        string messageId,
        string model,
        int inputTokens,
        CancellationToken cancellationToken);

    /// <summary>Reports the failure on the stream, once no further attempt will be made.</summary>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    ValueTask WriteHeldFailureAsync(CancellationToken cancellationToken);
}
