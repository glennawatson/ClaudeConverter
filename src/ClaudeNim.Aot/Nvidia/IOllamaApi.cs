// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Refit;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>A local Ollama server's OpenAI-compatible HTTP surface.</summary>
/// <remarks>
/// Ollama's own <c>/v1/chat/completions</c> route accepts and returns the same shape NIM's does
/// for every field this proxy actually sends or reads, so it shares <see cref="NimChatRequest"/>
/// rather than carrying a parallel set of wire types. A field it does not recognise — NVIDIA's own
/// <c>nvext</c> extension, for one — is simply ignored rather than rejected, the same tolerance any
/// OpenAI-compatible server extends to fields outside what it defines.
/// </remarks>
[Headers("Authorization: Bearer")]
public interface IOllamaApi
{
    /// <summary>Issues a chat completion.</summary>
    /// <param name="request">The upstream request body.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The raw upstream response, which the caller owns and must dispose.</returns>
    [Post("chat/completions")]
    Task<HttpResponseMessage> SendChatAsync([Body] NimChatRequest request, CancellationToken cancellationToken);
}
