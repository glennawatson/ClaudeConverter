// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>Turns a completed upstream completion into the wire shape a protocol's client expects.</summary>
/// <remarks>
/// The only implementation today is <see cref="AnthropicCompletionTranslator"/>. A second protocol
/// implements this against its own response type rather than sharing this one's, since the two wire
/// shapes agree on nothing beyond both being built from the same <see cref="NimChatCompletion"/>.
/// </remarks>
public interface ICompletionTranslator
{
    /// <summary>Translates a completed upstream completion.</summary>
    /// <param name="completion">The upstream completion, which may be absent.</param>
    /// <param name="messageId">The identifier to report for the message.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="nimModel">The NIM model that produced the completion, which the log names.</param>
    /// <param name="inputTokens">The prompt size to report when the upstream withheld usage.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <returns>The translated message.</returns>
    MessagesResponse Translate(
        NimChatCompletion? completion,
        string messageId,
        string model,
        string nimModel,
        int inputTokens,
        bool thinkingEnabled);
}
