// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using Refit;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The Azure Foundry v1 chat-completions transport.</summary>
public interface IAzureFoundryApi
{
    /// <summary>Sends a conversation to an Azure deployment.</summary>
    /// <param name="request">The shared conversation.</param>
    /// <param name="cancellationToken">Abandons the upstream request.</param>
    /// <returns>The upstream response, owned by the caller.</returns>
    [Post("chat/completions")]
    Task<HttpResponseMessage> SendChatAsync([Body] NimChatRequest request, CancellationToken cancellationToken);

    /// <summary>Sends tools and reasoning to an Azure Responses deployment.</summary>
    /// <param name="request">The Responses request body.</param>
    /// <param name="cancellationToken">Abandons the upstream request.</param>
    /// <returns>The upstream event stream, owned by the caller.</returns>
    [Post("responses")]
    Task<HttpResponseMessage> SendResponsesAsync([Body] JsonElement request, CancellationToken cancellationToken);
}
