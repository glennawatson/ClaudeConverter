// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Refit;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>A hosted OpenAI-compatible endpoint's HTTP surface — literal OpenAI, or Azure AI Foundry's Models endpoint.</summary>
/// <remarks>
/// Both speak the same chat completions shape NIM does for every field this proxy sends or reads,
/// so this shares <see cref="NimChatRequest"/> rather than a parallel set of wire types, the same
/// way <see cref="IOllamaApi"/> does.
/// </remarks>
[Headers("Authorization: Bearer")]
public interface IOpenAiApi
{
    /// <summary>Issues a chat completion.</summary>
    /// <param name="request">The upstream request body.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The raw upstream response, which the caller owns and must dispose.</returns>
    [Post("chat/completions")]
    Task<HttpResponseMessage> SendChatAsync([Body] NimChatRequest request, CancellationToken cancellationToken);
}
