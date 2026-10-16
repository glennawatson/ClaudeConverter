// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Sends shared chat requests to Azure Foundry deployments.</summary>
/// <param name="Api">The generated Azure transport.</param>
/// <param name="Options">The Azure endpoint and model settings.</param>
[System.Diagnostics.DebuggerDisplay("AzureFoundryClient: {Options.BaseUrl}")]
public sealed record AzureFoundryClient(IAzureFoundryApi Api, AzureFoundryOptions Options) : IOpenAiCompatibleClient
{
    /// <inheritdoc/>
    public async Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Options.Enabled)
        {
            return OpenAiCompatibleClients.Disabled("Azure Foundry");
        }

        var model = Options.ModelNames is { } names && names.TryGetValue(request.Model, out var underlying)
            ? underlying
            : request.Model;
        var hosted = OpenAiCompatibleClients.PrepareHostedRequest(request);
        if (OpenAiCompatibleClients.IsOpenAiModel(model))
        {
            hosted = OpenAiCompatibleClients.PrepareOpenAiRequest(hosted, model);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.Stream ? Options.ReadSeconds : Options.CompletionSeconds));
        if (model.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase) && request.Tools is { Count: > 0 })
        {
            var body = ChatGptRequestBuilder.Build(hosted, subscription: false, cancellationToken: timeout.Token);
            var response = await Api.SendResponsesAsync(body, timeout.Token).ConfigureAwait(false);
            return await ChatGptTransport.TranslateAsync(response, request.Stream, timeout.Token).ConfigureAwait(false);
        }

        return await Api.SendChatAsync(hosted, timeout.Token).ConfigureAwait(false);
    }
}
