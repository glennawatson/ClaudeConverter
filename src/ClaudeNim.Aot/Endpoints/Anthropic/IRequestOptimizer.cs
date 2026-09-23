// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>Answers a protocol's own housekeeping requests without calling the upstream.</summary>
/// <remarks>
/// The only implementation today is <see cref="AnthropicRequestOptimizer"/>, which recognises the
/// housekeeping traffic Claude Code itself sends. A protocol with nothing to recognise simply has
/// no implementation registered, and every one of its turns is forwarded.
/// </remarks>
public interface IRequestOptimizer
{
    /// <summary>Tries to answer a request locally.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="answer">The text to answer with, when one applies.</param>
    /// <returns><see langword="true"/> when the request was recognised and should not be forwarded.</returns>
    bool TryAnswer(MessagesRequest request, out string answer);
}
