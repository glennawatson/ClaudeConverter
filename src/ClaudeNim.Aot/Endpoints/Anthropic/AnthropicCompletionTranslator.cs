// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>Turns a completed upstream completion into an Anthropic message.</summary>
/// <param name="logger">The diagnostic log.</param>
/// <remarks>
/// A thin, DI-registered wrapper around <see cref="NimCompletionTranslator"/>'s own translation
/// rules, so the Messages API route depends on <see cref="ICompletionTranslator"/> rather than on a
/// concrete Nvidia-namespace type.
/// </remarks>
public sealed class AnthropicCompletionTranslator(ILogger<AnthropicCompletionTranslator> logger) : ICompletionTranslator
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MessagesResponse Translate(
        NimChatCompletion? completion,
        string messageId,
        string model,
        string nimModel,
        int inputTokens,
        bool thinkingEnabled) =>
        NimCompletionTranslator.Translate(completion, messageId, model, nimModel, inputTokens, thinkingEnabled, logger);
}
