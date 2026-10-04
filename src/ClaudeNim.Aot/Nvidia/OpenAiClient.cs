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

        var hosted = request with
        {
            ChatTemplateKwargs = null,
            Extensions = null,
            TopK = null,
            MinP = null,
            RepetitionPenalty = null,
            MinTokens = null,
            IgnoreEos = null,
        };

        if (Uri.TryCreate(Options.BaseUrl, UriKind.Absolute, out var endpoint)
            && string.Equals(endpoint.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase))
        {
            hosted = PreparePublicApi(hosted);
        }

        return await Api.SendChatAsync(hosted, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Uses the public API token limit and supported reasoning fields.</summary>
    /// <param name="request">The shared request.</param>
    /// <returns>The public API request.</returns>
    private static NimChatRequest PreparePublicApi(NimChatRequest request)
    {
        var reasoning = IsReasoningModel(request.Model);
        var messages = new List<NimChatMessage>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            messages.Add(message with { ReasoningContent = null });
        }

        return request with
        {
            Messages = messages,
            MaxTokens = 0,
            MaxCompletionTokens = request.MaxTokens,
            ReasoningEffort = reasoning ? request.ReasoningEffort : null,
            Temperature = reasoning ? null : request.Temperature,
            TopP = reasoning ? null : request.TopP,
            Stop = reasoning ? null : request.Stop,
            PresencePenalty = reasoning ? null : request.PresencePenalty,
            FrequencyPenalty = reasoning ? null : request.FrequencyPenalty,
            Seed = null,
        };
    }

    /// <summary>Identifies public API reasoning model families.</summary>
    /// <param name="model">The upstream model identifier.</param>
    /// <returns>Whether the model uses reasoning controls.</returns>
    private static bool IsReasoningModel(string model) => model.StartsWith("gpt-5", StringComparison.Ordinal)
        || model.StartsWith("gpt-6", StringComparison.Ordinal)
        || model.StartsWith("o1", StringComparison.Ordinal)
        || model.StartsWith("o3", StringComparison.Ordinal)
        || model.StartsWith("o4", StringComparison.Ordinal);
}
