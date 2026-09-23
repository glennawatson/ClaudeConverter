// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Turns a streamed NVIDIA NIM completion into the Responses API event stream.</summary>
/// <param name="inner">The translator this wraps.</param>
/// <remarks>
/// A thin wrapper around <see cref="CodexStreamTranslator"/>, so the Responses API route depends on
/// <see cref="IStreamTranslator"/> rather than on a concrete Nvidia-namespace type.
/// </remarks>
public sealed class ResponsesStreamTranslator(CodexStreamTranslator inner) : IStreamTranslator
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<StreamTurnOutcome> TranslateAsync(
        Stream upstream,
        string responseId,
        string model,
        int inputTokens,
        CancellationToken cancellationToken) =>
        inner.TranslateAsync(upstream, responseId, model, inputTokens, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WriteHeldFailureAsync(CancellationToken cancellationToken) =>
        inner.WriteHeldFailureAsync(cancellationToken);
}
