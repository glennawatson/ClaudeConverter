// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Mechanics shared by the OpenAI-compatible client implementations.</summary>
public static class OpenAiCompatibleClients
{
    /// <summary>Builds the response a disabled provider answers a call with, in place of attempting one.</summary>
    /// <param name="provider">The provider's own name, for the body a caller may log or forward.</param>
    /// <returns>A synthetic <see cref="HttpStatusCode.ServiceUnavailable"/> response.</returns>
    public static HttpResponseMessage Disabled(string provider) =>
        new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent($"{{\"error\":{{\"message\":\"{provider} is not enabled on this proxy.\",\"code\":503}}}}") };

    /// <summary>Removes model-runner controls from a hosted request.</summary>
    /// <param name="request">The shared conversation.</param>
    /// <returns>The hosted chat request.</returns>
    internal static NimChatRequest PrepareHostedRequest(NimChatRequest request) => request with
    {
        ChatTemplateKwargs = null,
        Extensions = null,
        TopK = null,
        MinP = null,
        RepetitionPenalty = null,
        MinTokens = null,
        IgnoreEos = null,
    };

    /// <summary>Uses the OpenAI completion-token limit and supported model controls.</summary>
    /// <param name="request">The hosted conversation.</param>
    /// <param name="model">The underlying model name, which may differ from its deployment name.</param>
    /// <returns>The OpenAI chat request.</returns>
    internal static NimChatRequest PrepareOpenAiRequest(NimChatRequest request, string model)
    {
        var reasoning = IsReasoningModel(model);
        var messages = new List<NimChatMessage>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            messages.Add(message with { ReasoningContent = null });
        }

        return request with
        {
            Messages = messages,
            MaxTokens = 0,
            MaxCompletionTokens = request.MaxCompletionTokens ?? request.MaxTokens,
            ReasoningEffort = reasoning ? request.ReasoningEffort : null,
            Temperature = reasoning ? null : request.Temperature,
            TopP = reasoning ? null : request.TopP,
            Stop = reasoning ? null : request.Stop,
            PresencePenalty = reasoning ? null : request.PresencePenalty,
            FrequencyPenalty = reasoning ? null : request.FrequencyPenalty,
            Seed = null,
        };
    }

    /// <summary>Identifies OpenAI models on multi-publisher Azure endpoints.</summary>
    /// <param name="model">The underlying model name.</param>
    /// <returns>Whether the OpenAI request controls apply.</returns>
    internal static bool IsOpenAiModel(string model) =>
        model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) || IsReasoningModel(model);

    /// <summary>Identifies model families that reject sampling controls.</summary>
    /// <param name="model">The underlying model name.</param>
    /// <returns>Whether the model uses reasoning controls.</returns>
    private static bool IsReasoningModel(string model) => model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
}
