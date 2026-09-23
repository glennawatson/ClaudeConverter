// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>A model identifier as it appears in configuration, split into the provider it addresses and the name that provider knows it by.</summary>
/// <param name="Provider">The transport the model resolves to.</param>
/// <param name="Model">The model name as that provider expects it on the wire, with any provider prefix stripped.</param>
/// <remarks>
/// Every place a model is named in configuration — <c>ModelRouting:Opus</c>, an entry in
/// <c>SonnetFallbacks</c>, the default model itself — accepts the same syntax, so a deployment can
/// mix providers in one ordered chain without a parallel list or a different setting per provider.
/// A bare identifier such as <c>nvidia/nemotron-3-super-120b-a12b</c> is NIM, unchanged from before
/// this existed; <c>ollama:qwen3-coder:30b</c>, <c>openai:gpt-5-mini</c> or <c>claude:claude-opus-5</c>
/// name a different one. The prefix is proxy-internal addressing and never reaches the wire — only
/// <see cref="Model"/> does.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("UpstreamModelId: {ToString(),nq}")]
public readonly record struct UpstreamModelId(UpstreamProvider Provider, string Model)
{
    /// <summary>The prefix that addresses a local Ollama server.</summary>
    private const string OllamaPrefix = "ollama";

    /// <summary>The prefixes that address an OpenAI-compatible endpoint.</summary>
    private const string OpenAiPrefix = "openai";

    /// <summary>An alias for <see cref="OpenAiPrefix"/>, since an Azure AI Foundry deployment speaks the same shape.</summary>
    private const string AzurePrefix = "azure";

    /// <summary>The prefix that addresses real Anthropic.</summary>
    private const string ClaudePrefix = "claude";

    /// <summary>An alias for <see cref="ClaudePrefix"/>.</summary>
    private const string AnthropicPrefix = "anthropic";

    /// <summary>Parses a configured model identifier.</summary>
    /// <param name="modelId">The identifier as configuration names it.</param>
    /// <returns>The provider it addresses, and the bare model name that provider expects.</returns>
    /// <remarks>
    /// Only the first colon is a delimiter: an Ollama tag such as <c>qwen3-coder:30b</c> carries one
    /// of its own, which stays part of <see cref="Model"/> rather than being mistaken for another
    /// prefix. A prefix this proxy does not recognise is not an error — the whole identifier is kept
    /// as a NIM model name, since NIM's own identifiers are never expected to collide with one.
    /// </remarks>
    public static UpstreamModelId Parse(string modelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        var separator = modelId.IndexOf(':');
        if (separator <= 0)
        {
            return new(UpstreamProvider.Nim, modelId);
        }

        var prefix = modelId[..separator];
        var rest = modelId[(separator + 1)..];

        return prefix switch
        {
            OllamaPrefix => new(UpstreamProvider.Ollama, rest),
            OpenAiPrefix or AzurePrefix => new(UpstreamProvider.OpenAi, rest),
            ClaudePrefix or AnthropicPrefix => new(UpstreamProvider.Anthropic, rest),
            _ => new(UpstreamProvider.Nim, modelId),
        };
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Provider}:{Model}";
}
