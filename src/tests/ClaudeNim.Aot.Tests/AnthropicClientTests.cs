// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the bound applied around a call to real Anthropic.</summary>
public sealed class AnthropicClientTests
{
    /// <summary>A minimal request used across the fixtures.</summary>
    private static readonly MessagesRequest Request = new(
        "claude-opus-5",
        [new(AnthropicMessage.UserRole, MessageContent.FromText("hi"))],
        100);

    /// <summary>A disabled Anthropic client answers as unavailable without calling the transport.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DisabledAnthropicClientNeverCallsTheTransport()
    {
        var api = new FakeAnthropicApi();
        var client = new AnthropicClient(api, new AnthropicApiOptions());

        var response = await client.SendMessagesAsync(Request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(api.Requests.Count).IsEqualTo(0);
    }

    /// <summary>An enabled Anthropic client forwards the request and returns the transport's own response.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EnabledAnthropicClientForwardsToTheTransport()
    {
        var api = new FakeAnthropicApi { OnSendMessages = static _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var client = new AnthropicClient(api, new AnthropicApiOptions(Enabled: true));

        var response = await client.SendMessagesAsync(Request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(api.Requests.Count).IsEqualTo(1);
    }
}
