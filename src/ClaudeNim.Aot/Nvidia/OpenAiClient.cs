// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Bounds a call to a hosted OpenAI-compatible endpoint.</summary>
/// <param name="Api">The Refit-generated surface.</param>
/// <param name="Options">The configured endpoint settings.</param>
[System.Diagnostics.DebuggerDisplay("OpenAiClient: {Options.BaseUrl}")]
public sealed record OpenAiClient(IOpenAiApi Api, OpenAiCompatibleOptions Options) : IOpenAiCompatibleClient
{
    /// <inheritdoc/>
    /// <remarks>
    /// A disabled provider answers with a synthetic <see cref="System.Net.HttpStatusCode.ServiceUnavailable"/>
    /// rather than attempting a call, so a model naming it in a fallback chain fails the same way an
    /// unreachable one would, without a configuration mistake reaching the network at all.
    /// </remarks>
    public async Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Options.Enabled)
        {
            return OpenAiCompatibleClients.Disabled("OpenAI-compatible");
        }

        var budget = TimeSpan.FromSeconds(request.Stream ? Options.ReadSeconds : Options.CompletionSeconds);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);

        return await Api.SendChatAsync(request, timeout.Token).ConfigureAwait(false);
    }
}
