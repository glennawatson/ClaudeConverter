// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Sends subscription requests through the public Responses endpoint.</summary>
internal static class ChatGptTransport
{
    /// <summary>The JSON media type.</summary>
    private const string JsonMediaType = "application/json";

    /// <summary>Sends a conversation with the selected subscription profile.</summary>
    /// <param name="http">The subscription transport.</param>
    /// <param name="profilePath">The credential profile.</param>
    /// <param name="request">The shared conversation.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>A shared completion or translating event stream.</returns>
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient http, string profilePath, NimChatRequest request, CancellationToken cancellationToken)
    {
        string token;
        try
        {
            token = await ChatGptCredentials.AccessTokenAsync(http, profilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or JsonException or KeyNotFoundException)
        {
            return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"Run --chatgpt-login to authorize subscription usage.\"}}", Encoding.UTF8, JsonMediaType) };
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{ChatGptCredentials.Resource}/responses"));
        message.Headers.Authorization = new("Bearer", token);
        message.Content = new StringContent(ChatGptRequestBuilder.Build(request).GetRawText(), Encoding.UTF8, JsonMediaType);
        var upstream = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!upstream.IsSuccessStatusCode)
        {
            return upstream;
        }

        var transferred = false;
        try
        {
            var body = await upstream.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (request.Stream)
            {
                var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChatGptResponseStream(upstream, body)) };
                result.Content.Headers.ContentType = new("text/event-stream");
                transferred = true;
                return result;
            }

            using var reader = new StreamReader(body);
            return await ReadCompletionAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!transferred)
            {
                upstream.Dispose();
            }
        }
    }

    /// <summary>Collects upstream events for a non-streaming caller.</summary>
    /// <param name="reader">The upstream event reader.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The shared completion.</returns>
    /// <exception cref="IOException">The upstream stream ended early.</exception>
    private static async Task<HttpResponseMessage> ReadCompletionAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await ChatGptResponseStream.ReadEventAsync(reader, cancellationToken).ConfigureAwait(false) is { } eventData)
        {
            if (eventData.Type is "response.completed" or "response.incomplete" && eventData.Response is { } terminal)
            {
                var completion = ChatGptResponseTranslator.Complete(terminal);
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(completion, ProxyJsonContext.Default.NimChatCompletion), Encoding.UTF8, JsonMediaType), };
            }

            if (eventData.Type is "response.failed" or "error")
            {
                return new(HttpStatusCode.BadGateway) { Content = new StringContent("{\"error\":{\"message\":\"The subscription request failed.\"}}", Encoding.UTF8, JsonMediaType) };
            }
        }

        throw new IOException("The subscription stream ended before a terminal response.");
    }
}
