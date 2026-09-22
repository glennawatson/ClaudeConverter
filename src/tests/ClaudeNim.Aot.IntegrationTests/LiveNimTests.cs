// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ClaudeNim.Aot.IntegrationTests;

/// <summary>Exercises the proxy end to end against the live NVIDIA endpoint.</summary>
/// <remarks>
/// <para>
/// This is deliberately not a mocked test. The unit test project pins the behaviour of each piece
/// in isolation; this one asks whether the whole thing still answers real questions against the
/// real upstream, which is the only place a NIM-specific quirk can actually be seen.
/// </para>
/// <para>
/// Every test starts with <see cref="LiveCredential.IsAvailable"/>: a machine with no NVIDIA
/// credential configured skips rather than fails, since these tests exist to be run deliberately
/// against a real account, not as a gate every build has to clear.
/// </para>
/// </remarks>
public sealed class LiveNimTests : IDisposable
{
    /// <summary>Why a test is skipped when no credential is configured.</summary>
    private const string SkipReason = "No NVIDIA_API_KEY in the environment.";

    /// <summary>The Messages API route.</summary>
    private const string MessagesPath = "/v1/messages";

    /// <summary>The token-counting route.</summary>
    private const string CountTokensPath = "/v1/messages/count_tokens";

    /// <summary>The Models API route.</summary>
    private const string ModelsPath = "/v1/models";

    /// <summary>A model reference used for the sampling and reasoning fixtures.</summary>
    private const string SuperModel = "anthropic/nvidia_nim/nvidia/nemotron-3-super-120b-a12b";

    /// <summary>A model reference used for the reasoning fixture, via its Ultra-backed Claude alias.</summary>
    private const string OpusAlias = "claude-opus-5";

    /// <summary>A model reference used for the vision fixture.</summary>
    private const string VisionModel = "anthropic/nvidia_nim/nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";

    /// <summary>A model reference used for the streamed-reasoning fixture.</summary>
    private const string UltraModel = "anthropic/nvidia_nim/nvidia/nemotron-3-ultra-550b-a55b";

    /// <summary>A 1x1 PNG, used as the smallest possible real image payload.</summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    /// <summary>The app the tests run against.</summary>
    private readonly ClaudeNimAppFactory _factory = new();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _factory.Dispose();

    /// <summary>The health route reports itself ready and reachable.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task HealthReportsReady()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        using var client = _factory.CreateClient();
        using var response = await GetAsync(client, "/health");
        var doc = JsonElement.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(doc.GetProperty("status").GetString()).IsEqualTo("ok");
        await Assert.That(doc.GetProperty("model_count").GetInt32()).IsGreaterThan(0);
    }

    /// <summary>The listing carries the documented shape, not an invented one.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ModelsListingHasTheDocumentedShape()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        using var client = _factory.CreateClient();
        using var response = await GetAsync(client, $"{ModelsPath}?limit=3");
        var root = JsonElement.Parse(await response.Content.ReadAsStringAsync());
        var first = root.GetProperty("data")[0];

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(first.TryGetProperty("max_input_tokens", out _)).IsTrue();
        await Assert.That(first.TryGetProperty("capabilities", out _)).IsTrue();
        await Assert.That(first.TryGetProperty("object", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("has_more", out _)).IsTrue();
    }

    /// <summary>The cursor advances rather than repeating the same page.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ModelsPaginationCursorAdvances()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        using var client = _factory.CreateClient();

        using var page1 = await GetAsync(client, $"{ModelsPath}?limit=2");
        var doc1 = JsonElement.Parse(await page1.Content.ReadAsStringAsync());
        var lastId = doc1.GetProperty("last_id").GetString()!;
        var firstOfPage1 = doc1.GetProperty("data")[0].GetProperty("id").GetString();

        using var page2 = await GetAsync(client, $"{ModelsPath}?limit=2&after_id={Uri.EscapeDataString(lastId)}");
        var doc2 = JsonElement.Parse(await page2.Content.ReadAsStringAsync());
        var firstOfPage2 = doc2.GetProperty("data")[0].GetProperty("id").GetString();

        await Assert.That(doc1.GetProperty("has_more").GetBoolean()).IsTrue();
        await Assert.That(firstOfPage2).IsNotEqualTo(firstOfPage1);
    }

    /// <summary>Looking up a real advertised identifier resolves it, slashes and all.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// Every gateway identifier contains at least two slashes; a route declared as a single path
    /// segment cannot match one at all.
    /// </remarks>
    [Test]
    public async Task GetSingleModelResolvesARealGatewayIdentifier()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        using var client = _factory.CreateClient();

        using var listing = await GetAsync(client, $"{ModelsPath}?limit=1");
        var listingDoc = JsonElement.Parse(await listing.Content.ReadAsStringAsync());
        var id = listingDoc.GetProperty("data")[0].GetProperty("id").GetString()!;

        using var response = await GetAsync(client, $"{ModelsPath}/{id}");

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    /// <summary>Looking up an identifier the credential cannot reach reports it plainly.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task GetSingleModelReportsAnUnknownIdentifier()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        using var client = _factory.CreateClient();
        using var response = await GetAsync(client, $"{ModelsPath}/does-not-exist");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>Token counting answers with a positive estimate rather than forwarding upstream.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CountTokensAnswersLocally()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = """{"model":"claude-sonnet-5","messages":[{"role":"user","content":"hello world, this is a test"}]}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, CountTokensPath, Body);
        var doc = JsonElement.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(doc.GetProperty("input_tokens").GetInt32()).IsGreaterThan(0);
    }

    /// <summary>A plain non-streamed turn answers with the requested text.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NonStreamedCompletionAnswers()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$"""{"model":"{{SuperModel}}","max_tokens":60,"messages":[{"role":"user","content":"Reply with exactly: PONG"}]}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("PONG");
    }

    /// <summary>A reasoning turn produces both a thinking block and an answer.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReasoningTurnProducesThinkingAndAnAnswer()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$"""{"model":"{{OpusAlias}}","max_tokens":150,"messages":[{"role":"user","content":"What is 12*13? Think it through."}]}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("\"type\":\"thinking\"");
        await Assert.That(text).Contains("156");
    }

    /// <summary>A tool whose schema declares a parameter literally named <c>type</c> works.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect: a <c>type</c> parameter made some NIM models
    /// answer with an internal server error, because the name collides with the function-calling
    /// wrapper's own discriminator.
    /// </remarks>
    [Test]
    public async Task ToolWithReservedTypeParameterWorks()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$$"""
        {
          "model": "{{{SuperModel}}}",
          "max_tokens": 150,
          "messages": [{"role":"user","content":"Call the pick tool with type set to red."}],
          "tools": [{"name":"pick","description":"Pick a colour","input_schema":{"type":"object","properties":{"type":{"type":"string"}},"required":["type"]}}]
        }
        """;

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(text).Contains("\"type\":\"tool_use\"");
        await Assert.That(text).Contains("\"type\":\"red\"");
    }

    /// <summary>An image is forwarded to a model the catalogue states has vision.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ImageIsForwardedToAVisionModel()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$$"""
        {
          "model": "{{{VisionModel}}}",
          "max_tokens": 80,
          "messages": [{"role":"user","content":[
            {"type":"text","text":"What colour is this image? Answer in one word."},
            {"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{OnePixelPng}}}"}}
          ]}]
        }
        """;

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    /// <summary>A streamed turn produces well-formed, correctly bracketed server-sent events.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedCompletionProducesWellFormedEvents()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$"""{"model":"{{SuperModel}}","max_tokens":100,"stream":true,"messages":[{"role":"user","content":"Count from 1 to 3."}]}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("event: message_start");
        await Assert.That(text).Contains("event: content_block_delta");
        await Assert.That(text).Contains("event: message_stop");
        await Assert.That(CountOccurrences(text, "content_block_start"))
            .IsEqualTo(CountOccurrences(text, "content_block_stop"));
    }

    /// <summary>A streamed reasoning turn brackets a thinking block and a text block separately.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedReasoningTurnBracketsBothBlocks()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$"""{"model":"{{UltraModel}}","max_tokens":150,"stream":true,"messages":[{"role":"user","content":"Count from 1 to 3."}]}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("\"type\":\"thinking\"");
        await Assert.That(text).Contains("\"type\":\"text\"");
    }

    /// <summary>The quota probe is answered locally rather than spending upstream quota.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task QuotaProbeIsAnsweredLocally()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = """{"model":"claude-sonnet-5","max_tokens":1,"messages":[{"role":"user","content":"quota"}]}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("Quota check passed");
    }

    /// <summary>Title generation is answered locally rather than spending upstream quota.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TitleGenerationIsAnsweredLocally()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = """
        {
          "model": "claude-sonnet-5",
          "max_tokens": 30,
          "system": "Write a short sentence-case title for this coding session",
          "messages": [{"role":"user","content":"help me fix a bug"}]
        }
        """;

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("Conversation");
    }

    /// <summary>Command-prefix detection is answered locally from the command it carries.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CommandPrefixDetectionIsAnsweredLocally()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = """
        {
          "model": "claude-sonnet-5",
          "max_tokens": 30,
          "messages": [{"role":"user","content":"<policy_spec> rules\nCommand:\ngit commit -m hi\nOutput:\nok"}]
        }
        """;

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(text).Contains("git commit");
    }

    /// <summary>A request missing required fields answers with a clean error, not a crash.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect: a body missing <c>model</c> or <c>messages</c>
    /// bound successfully with those members left null, which crashed deeper in the pipeline with
    /// a bare 500 rather than the <c>invalid_request_error</c> the request had earned.
    /// </remarks>
    [Test]
    public async Task MalformedRequestAnswersWithACleanError()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = """{"max_tokens":10}""";

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(text).Contains("invalid_request_error");
    }

    /// <summary>A multi-turn conversation carrying a replayed tool result round-trips.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task MultiTurnToolResultRoundTrips()
    {
        Skip.Unless(LiveCredential.IsAvailable, SkipReason);

        const string Body = $$$"""
        {
          "model": "{{{SuperModel}}}",
          "max_tokens": 100,
          "messages": [
            {"role":"user","content":"What is the weather in Paris?"},
            {"role":"assistant","content":[{"type":"tool_use","id":"toolu_abc123","name":"get_weather","input":{"city":"Paris"}}]},
            {"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_abc123","content":"Sunny, 22C"}]}
          ],
          "tools": [{"name":"get_weather","description":"Get weather","input_schema":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}]
        }
        """;

        using var client = _factory.CreateClient();
        using var response = await PostAsync(client, MessagesPath, Body);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    /// <summary>Gets a route relative to the running app.</summary>
    /// <param name="client">The client the request is sent through.</param>
    /// <param name="path">The route to fetch.</param>
    /// <returns>The response.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        client.GetAsync(new Uri(path, UriKind.Relative));

    /// <summary>Posts a JSON body to the running app.</summary>
    /// <param name="client">The client the request is sent through.</param>
    /// <param name="path">The route to post to.</param>
    /// <param name="body">The raw JSON body.</param>
    /// <returns>The response.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string body) =>
        client.PostAsJsonAsync(new Uri(path, UriKind.Relative), JsonElement.Parse(body));

    /// <summary>Counts how many times a substring occurs in a run of text.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="value">The substring to count.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
