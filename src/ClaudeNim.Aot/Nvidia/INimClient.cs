// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Talks to the NVIDIA NIM OpenAI-compatible endpoint.</summary>
public interface INimClient
{
    /// <summary>Issues a chat completion, streamed or not according to the request.</summary>
    /// <param name="request">The upstream request body.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream response, which the caller owns and must dispose.</returns>
    ValueTask<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the models the configured credential can reach.</summary>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream listing, or <see langword="null"/> when it could not be fetched.</returns>
    ValueTask<NimModelList?> ListModelsAsync(CancellationToken cancellationToken);
}
