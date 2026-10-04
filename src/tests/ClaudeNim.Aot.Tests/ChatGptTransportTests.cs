// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers subscription transport with local HTTP doubles.</summary>
public sealed class ChatGptTransportTests
{
    /// <summary>The named test transport.</summary>
    private const string ClientName = "subscription-test";

    /// <summary>A completed Responses stream used by both caller modes.</summary>
    private const string CompletedEvents = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"hello\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"response-1\",\"model\":\"gpt-test\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"hello\"}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1,\"total_tokens\":2}}}\n\n";

    /// <summary>Both caller modes use subscription credentials at the Responses endpoint.</summary>
    /// <param name="stream">Whether the caller asks for streaming.</param>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SendUsesSubscriptionCredentialsAndTranslatesResponse(bool stream)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "account.json");
        _ = Directory.CreateDirectory(directory);
        try
        {
            using var tokens = JsonDocument.Parse("""
                {"access_token":"test-access","refresh_token":"test-refresh","token_type":"Bearer",
                 "scope":"chatgpt.tokens.use.direct","expires_in":3600}
                """);
            await ChatGptCredentials.SaveAsync(path, "issued", "host", "subject", tokens.RootElement, CancellationToken.None);
            using var handler = new FakeSubscriptionHandler
            {
                OnSend = static _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CompletedEvents, Encoding.UTF8, "text/event-stream") }),
            };
            var services = new ServiceCollection();
            _ = services.AddHttpClient(ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            await using var provider = services.BuildServiceProvider();
            using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
            var api = new FakeOpenAiApi();
            var client = new OpenAiClient(api, new OpenAiCompatibleOptions(Enabled: true, Authentication: "ChatGpt", CredentialsPath: path), http);
            using var response = await client.SendChatAsync(new("gpt-test", [], 1, stream), CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync();

            await Assert.That(handler.Authorization).IsEqualTo("Bearer test-access");
            await Assert.That(handler.Address?.AbsoluteUri).IsEqualTo("https://api.openai.com/v1/responses");
            using var requestBody = JsonDocument.Parse(handler.Body!);
            await Assert.That(requestBody.RootElement.GetProperty("store").GetBoolean()).IsFalse();
            await Assert.That(requestBody.RootElement.GetProperty(nameof(stream)).GetBoolean()).IsTrue();
            await Assert.That(body).Contains("hello");
            await Assert.That(body).Contains(stream ? "[DONE]" : "completion_tokens");
            await Assert.That(api.Requests.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A missing profile gives a login error without calling either backend.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task MissingLoginReturnsUnauthorized()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "account.json");
        try
        {
            using var handler = new FakeSubscriptionHandler();
            var services = new ServiceCollection();
            _ = services.AddHttpClient(ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            await using var provider = services.BuildServiceProvider();
            using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
            using var response = await ChatGptTransport.SendAsync(http, path, new("gpt-test", [], 1, Stream: false), CancellationToken.None);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(handler.Address).IsNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A truncated stream must fail instead of appearing complete.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task StreamRejectsPrematureEnd()
    {
        using var upstream = new HttpResponseMessage(HttpStatusCode.OK);
        await using var body = new MemoryStream("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n"u8.ToArray());
        await using var stream = new ChatGptResponseStream(upstream, body);
        using var reader = new StreamReader(stream);

        await Assert.That(async () => await reader.ReadToEndAsync()).Throws<IOException>();
    }
}
