// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="IOpenAiCompatibleClient"/> double that never leaves the process.</summary>
internal sealed class FakeOpenAiCompatibleClient : IOpenAiCompatibleClient
{
    /// <summary>Gets or sets the responder invoked by <see cref="SendChatAsync"/>.</summary>
    /// <remarks>Left unset, a call answers as disabled — the default state every provider ships in.</remarks>
    public Func<NimChatRequest, HttpResponseMessage>? OnSendChat { get; set; }

    /// <summary>Gets the requests passed to <see cref="SendChatAsync"/>, in call order.</summary>
    public List<NimChatRequest> Requests { get; } = [];

    /// <inheritdoc/>
    public Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(OnSendChat?.Invoke(request) ?? OpenAiCompatibleClients.Disabled("test"));
    }
}
