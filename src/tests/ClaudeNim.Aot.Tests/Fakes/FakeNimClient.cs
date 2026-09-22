// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>A hand-written <see cref="INimClient"/> double that never leaves the process.</summary>
internal sealed class FakeNimClient : INimClient
{
    /// <summary>Gets or sets the responder invoked by <see cref="SendChatAsync"/>.</summary>
    public Func<NimChatRequest, HttpResponseMessage>? OnSendChat { get; set; }

    /// <summary>Gets or sets the responder invoked by <see cref="ListModelsAsync"/>.</summary>
    public Func<NimModelList?>? OnListModels { get; set; }

    /// <summary>Gets the requests passed to <see cref="SendChatAsync"/>, in call order.</summary>
    public List<NimChatRequest> Requests { get; } = [];

    /// <inheritdoc/>
    public ValueTask<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return ValueTask.FromResult(OnSendChat?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<NimModelList?> ListModelsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(OnListModels?.Invoke());
}
