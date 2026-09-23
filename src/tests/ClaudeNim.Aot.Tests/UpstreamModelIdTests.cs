// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers how a configured model identifier is split into the provider it addresses and the name that provider knows it by.</summary>
public sealed class UpstreamModelIdTests
{
    /// <summary>A bare identifier with no recognised prefix is NIM, unchanged.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BareIdentifierIsNim()
    {
        var parsed = UpstreamModelId.Parse("nvidia/nemotron-3-super-120b-a12b");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.Nim);
        await Assert.That(parsed.Model).IsEqualTo("nvidia/nemotron-3-super-120b-a12b");
    }

    /// <summary>An <c>ollama:</c> prefix addresses Ollama, keeping any colon inside the tag itself.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task OllamaPrefixAddressesOllamaAndKeepsTheTagsOwnColon()
    {
        var parsed = UpstreamModelId.Parse("ollama:qwen3-coder:30b");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.Ollama);
        await Assert.That(parsed.Model).IsEqualTo("qwen3-coder:30b");
    }

    /// <summary>An <c>openai:</c> prefix addresses the OpenAI-compatible provider.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task OpenAiPrefixAddressesTheOpenAiCompatibleProvider()
    {
        var parsed = UpstreamModelId.Parse("openai:gpt-5-mini");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.OpenAi);
        await Assert.That(parsed.Model).IsEqualTo("gpt-5-mini");
    }

    /// <summary>An <c>azure:</c> prefix is an alias for the same OpenAI-compatible provider.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task AzurePrefixIsAnAliasForTheOpenAiCompatibleProvider()
    {
        var parsed = UpstreamModelId.Parse("azure:gpt-5-mini");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.OpenAi);
        await Assert.That(parsed.Model).IsEqualTo("gpt-5-mini");
    }

    /// <summary>A <c>claude:</c> prefix addresses real Anthropic.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ClaudePrefixAddressesRealAnthropic()
    {
        var parsed = UpstreamModelId.Parse("claude:claude-opus-5");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.Anthropic);
        await Assert.That(parsed.Model).IsEqualTo("claude-opus-5");
    }

    /// <summary>An <c>anthropic:</c> prefix is an alias for the same real-Anthropic provider.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task AnthropicPrefixIsAnAliasForRealAnthropic()
    {
        var parsed = UpstreamModelId.Parse("anthropic:claude-opus-5");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.Anthropic);
        await Assert.That(parsed.Model).IsEqualTo("claude-opus-5");
    }

    /// <summary>A prefix this proxy does not recognise is kept as a whole NIM model name, not stripped.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task UnrecognisedPrefixIsKeptAsAWholeNimModelName()
    {
        var parsed = UpstreamModelId.Parse("together:llama-3.1-70b");

        await Assert.That(parsed.Provider).IsEqualTo(UpstreamProvider.Nim);
        await Assert.That(parsed.Model).IsEqualTo("together:llama-3.1-70b");
    }
}
