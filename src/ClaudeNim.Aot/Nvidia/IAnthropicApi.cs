// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using Refit;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Real Anthropic's own Messages API.</summary>
/// <remarks>
/// Unlike <see cref="IOllamaApi"/> and <see cref="IOpenAiApi"/>, this carries the proxy's own
/// client-facing <see cref="MessagesRequest"/> body rather than <see cref="NimChatRequest"/>: real
/// Anthropic already speaks the shape a Claude session sends, so nothing here translates it.
/// </remarks>
public interface IAnthropicApi
{
    /// <summary>Issues a Messages API call.</summary>
    /// <param name="request">The upstream request body.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The raw upstream response, which the caller owns and must dispose.</returns>
    [Post("v1/messages")]
    Task<HttpResponseMessage> SendMessagesAsync([Body] MessagesRequest request, CancellationToken cancellationToken);
}
