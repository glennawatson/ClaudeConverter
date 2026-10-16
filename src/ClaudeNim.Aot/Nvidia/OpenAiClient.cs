// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Bounds a call to a hosted OpenAI-compatible endpoint.</summary>
/// <param name="Api">The Refit-generated surface.</param>
/// <param name="Options">The configured endpoint settings.</param>
/// <param name="SubscriptionHttp">The subscription HTTP transport.</param>
[System.Diagnostics.DebuggerDisplay("OpenAiClient: {Options.BaseUrl}")]
public sealed record OpenAiClient(IOpenAiApi Api, OpenAiCompatibleOptions Options, HttpClient? SubscriptionHttp = null) : IOpenAiCompatibleClient
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

        if (string.Equals(Options.Authentication, "ChatGpt", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionHttp is null
                ? OpenAiCompatibleClients.Disabled("Subscription transport")
                : await ChatGptTransport.SendAsync(SubscriptionHttp, ChatGptCredentials.PathFor(Options.CredentialsPath), request, timeout.Token).ConfigureAwait(false);
        }

        if (!string.Equals(Options.Authentication, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            return OpenAiCompatibleClients.Disabled("Unknown authentication mode");
        }

        var hosted = OpenAiCompatibleClients.PrepareHostedRequest(request);

        if (UsesOpenAiControls(Options.BaseUrl, hosted.Model))
        {
            hosted = OpenAiCompatibleClients.PrepareOpenAiRequest(hosted, hosted.Model);
        }

        return await Api.SendChatAsync(hosted, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Recognizes OpenAI and Azure GPT endpoints while preserving other compatible servers.</summary>
    /// <param name="baseUrl">The configured endpoint.</param>
    /// <param name="model">The hosted model name.</param>
    /// <returns>Whether OpenAI-specific request controls apply.</returns>
    private static bool UsesOpenAiControls(string baseUrl, string model)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        var publicApi = string.Equals(endpoint.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);
        var azure = endpoint.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)
            || endpoint.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
            || endpoint.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase);
        return publicApi || (azure && OpenAiCompatibleClients.IsOpenAiModel(model));
    }
}
