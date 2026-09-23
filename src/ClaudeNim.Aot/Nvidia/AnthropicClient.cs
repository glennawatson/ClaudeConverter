// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Bounds a call to real Anthropic.</summary>
/// <param name="Api">The Refit-generated Anthropic surface.</param>
/// <param name="Options">The configured Anthropic settings.</param>
[System.Diagnostics.DebuggerDisplay("AnthropicClient: {Options.BaseUrl}")]
public sealed record AnthropicClient(IAnthropicApi Api, AnthropicApiOptions Options) : IAnthropicClient
{
    /// <inheritdoc/>
    /// <remarks>
    /// A disabled provider answers with a synthetic <see cref="System.Net.HttpStatusCode.ServiceUnavailable"/>
    /// rather than attempting a call, so a model naming it in a fallback chain fails the same way an
    /// unreachable one would, without a configuration mistake reaching the network at all.
    /// </remarks>
    public async Task<HttpResponseMessage> SendMessagesAsync(MessagesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Options.Enabled)
        {
            return OpenAiCompatibleClients.Disabled("Anthropic");
        }

        var budget = TimeSpan.FromSeconds(request.IsStreaming ? Options.ReadSeconds : Options.CompletionSeconds);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);

        return await Api.SendMessagesAsync(request, timeout.Token).ConfigureAwait(false);
    }
}
