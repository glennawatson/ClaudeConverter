// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="IAnthropicClient"/> double that never leaves the process.</summary>
internal sealed class FakeAnthropicClient : IAnthropicClient
{
    /// <summary>Gets or sets the responder invoked by <see cref="SendMessagesAsync"/>.</summary>
    /// <remarks>Left unset, a call answers as disabled — the default state every provider ships in.</remarks>
    public Func<MessagesRequest, HttpResponseMessage>? OnSendMessages { get; set; }

    /// <summary>Gets the requests passed to <see cref="SendMessagesAsync"/>, in call order.</summary>
    public List<MessagesRequest> Requests { get; } = [];

    /// <inheritdoc/>
    public Task<HttpResponseMessage> SendMessagesAsync(MessagesRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(OnSendMessages?.Invoke(request) ?? OpenAiCompatibleClients.Disabled("test"));
    }
}
