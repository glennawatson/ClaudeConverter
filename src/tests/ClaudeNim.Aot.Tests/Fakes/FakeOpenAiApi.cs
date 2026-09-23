// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written double serving both <see cref="IOllamaApi"/> and <see cref="IOpenAiApi"/>, which share one method shape.</summary>
internal sealed class FakeOpenAiApi : IOllamaApi, IOpenAiApi
{
    /// <summary>Gets or sets the responder invoked by <see cref="SendChatAsync"/>.</summary>
    public Func<NimChatRequest, HttpResponseMessage>? OnSendChat { get; set; }

    /// <summary>Gets the requests passed to <see cref="SendChatAsync"/>, in call order.</summary>
    public List<NimChatRequest> Requests { get; } = [];

    /// <inheritdoc cref="IOllamaApi.SendChatAsync"/>
    public Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(OnSendChat?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
