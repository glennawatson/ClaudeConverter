// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Nvidia;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Turns a completed upstream completion into a Responses API turn.</summary>
/// <param name="time">The clock the turn's timestamp is read from.</param>
/// <param name="logger">The diagnostic log.</param>
/// <remarks>
/// A thin, DI-registered wrapper around <see cref="Nvidia.CodexCompletionTranslator"/>'s own
/// translation rules, so the Responses API route depends on <see cref="ICompletionTranslator"/>
/// rather than on a concrete Nvidia-namespace type.
/// </remarks>
public sealed class ResponsesCompletionTranslator(TimeProvider time, ILogger<ResponsesCompletionTranslator> logger) : ICompletionTranslator
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResponsesResponse Translate(
        NimChatCompletion? completion,
        string responseId,
        string model,
        string nimModel,
        int inputTokens,
        bool thinkingEnabled) =>
        CodexCompletionTranslator.Translate(
            completion,
            responseId,
            model,
            nimModel,
            new(inputTokens, thinkingEnabled, time, logger));
}
