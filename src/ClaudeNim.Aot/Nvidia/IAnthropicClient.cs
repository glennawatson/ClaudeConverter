// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The policy applied around a call to real Anthropic.</summary>
/// <remarks>
/// A separate interface from <see cref="IOpenAiCompatibleClient"/> because the wire shape differs:
/// that one carries <see cref="NimChatRequest"/>, the shape every OpenAI-compatible endpoint (NIM,
/// Ollama, a generic OpenAI/Azure deployment) accepts, where this one carries the proxy's own
/// client-facing <see cref="MessagesRequest"/> unchanged, since real Anthropic already speaks it.
/// </remarks>
public interface IAnthropicClient
{
    /// <summary>Issues one Messages API call, bounded by the deadline its shape calls for.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream response, with headers received.</returns>
    Task<HttpResponseMessage> SendMessagesAsync(MessagesRequest request, CancellationToken cancellationToken);
}
