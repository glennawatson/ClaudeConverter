// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Turns a completed upstream completion into a Responses API turn.</summary>
/// <remarks>
/// The Codex counterpart of <c>Endpoints.Anthropic.ICompletionTranslator</c>. The two protocols
/// share nothing beyond both being built from the same <see cref="NimChatCompletion"/>, so this is
/// its own interface rather than a shared generic one.
/// </remarks>
public interface ICompletionTranslator
{
    /// <summary>Translates a completed upstream completion.</summary>
    /// <param name="completion">The upstream completion, which may be absent.</param>
    /// <param name="responseId">The identifier to report for the turn.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="nimModel">The NIM model that produced the completion, which the log names.</param>
    /// <param name="inputTokens">The prompt size to report when the upstream withheld usage.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
    /// <returns>The translated turn.</returns>
    ResponsesResponse Translate(
        NimChatCompletion? completion,
        string responseId,
        string model,
        string nimModel,
        int inputTokens,
        bool thinkingEnabled);
}
