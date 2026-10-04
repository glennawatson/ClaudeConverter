// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A HTTP transport double for subscription requests.</summary>
internal sealed class FakeSubscriptionHandler : HttpMessageHandler
{
    /// <summary>Gets the most recent request address.</summary>
    public Uri? Address { get; private set; }

    /// <summary>Gets the most recent authorization header.</summary>
    public string? Authorization { get; private set; }

    /// <summary>Gets the most recent request body.</summary>
    public string? Body { get; private set; }

    /// <summary>Gets or sets the response callback.</summary>
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnSend { get; set; }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Address = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return OnSend is null ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) : await OnSend(request);
    }
}
