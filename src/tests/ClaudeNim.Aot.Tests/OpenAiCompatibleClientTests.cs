// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the bound applied around a call to Ollama or an OpenAI-compatible endpoint.</summary>
public sealed class OpenAiCompatibleClientTests
{
    /// <summary>A minimal request used across the fixtures.</summary>
    private static readonly NimChatRequest Request = new("qwen3-coder:30b", [], 100, Stream: false);

    /// <summary>A disabled Ollama client answers as unavailable without calling the transport.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DisabledOllamaClientNeverCallsTheTransport()
    {
        var api = new FakeOpenAiApi();
        var client = new OllamaClient(api, new OllamaOptions());

        var response = await client.SendChatAsync(Request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(api.Requests.Count).IsEqualTo(0);
    }

    /// <summary>An enabled Ollama client forwards the request and returns the transport's own response.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EnabledOllamaClientForwardsToTheTransport()
    {
        var api = new FakeOpenAiApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = new OllamaClient(api, new OllamaOptions(Enabled: true));

        var response = await client.SendChatAsync(Request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(api.Requests.Count).IsEqualTo(1);
    }

    /// <summary>A disabled OpenAI-compatible client answers as unavailable without calling the transport.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DisabledOpenAiClientNeverCallsTheTransport()
    {
        var api = new FakeOpenAiApi();
        var client = new OpenAiClient(api, new OpenAiCompatibleOptions());

        var response = await client.SendChatAsync(Request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(api.Requests.Count).IsEqualTo(0);
    }

    /// <summary>An enabled OpenAI-compatible client forwards the request and returns the transport's own response.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EnabledOpenAiClientForwardsToTheTransport()
    {
        var api = new FakeOpenAiApi { OnSendChat = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = new OpenAiClient(api, new OpenAiCompatibleOptions(Enabled: true));

        var response = await client.SendChatAsync(Request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(api.Requests.Count).IsEqualTo(1);
    }
}
