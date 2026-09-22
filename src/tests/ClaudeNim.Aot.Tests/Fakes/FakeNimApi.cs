// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;
using Refit;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="INimApi"/> double that never leaves the process.</summary>
/// <remarks>
/// No dynamic-proxy mocking library is used in this repo; every double is a plain class
/// implementing the interface directly, with settable delegates controlling its behavior.
/// </remarks>
internal sealed class FakeNimApi : INimApi
{
    /// <summary>Gets or sets the responder invoked by <see cref="SendChatAsync"/>.</summary>
    public Func<NimChatRequest, HttpResponseMessage>? OnSendChat { get; set; }

    /// <summary>Gets or sets the responder invoked by <see cref="ListModelsAsync"/>.</summary>
    public Func<IApiResponse<NimModelList>>? OnListModels { get; set; }

    /// <summary>Gets the number of times <see cref="SendChatAsync"/> was called.</summary>
    public int SendChatCalls { get; private set; }

    /// <summary>Gets the number of times <see cref="ListModelsAsync"/> was called.</summary>
    public int ListModelsCalls { get; private set; }

    /// <inheritdoc/>
    public Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        SendChatCalls++;
        var response = OnSendChat?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        return Task.FromResult(response);
    }

    /// <inheritdoc/>
    public Task<IApiResponse<NimModelList>> ListModelsAsync(CancellationToken cancellationToken)
    {
        ListModelsCalls++;
        var response = OnListModels?.Invoke()
            ?? new ApiResponse<NimModelList>(new HttpResponseMessage(System.Net.HttpStatusCode.OK), new([]), new RefitSettings());
        return Task.FromResult(response);
    }
}
