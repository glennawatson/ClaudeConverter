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

    /// <summary>Gets or sets how long a call takes to answer, during which it can still be abandoned.</summary>
    public TimeSpan AnswerDelay { get; set; }

    /// <summary>Gets the number of times <see cref="SendChatAsync"/> was called.</summary>
    public int SendChatCalls { get; private set; }

    /// <summary>Gets the number of times <see cref="ListModelsAsync"/> was called.</summary>
    public int ListModelsCalls { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The token is honoured the way the real transport honours one — a call that is abandoned
    /// while it is still waiting raises <see cref="TaskCanceledException"/> rather than answering
    /// — so a fixture can exercise what the policy above does with a deadline that ran out.
    /// </remarks>
    public async Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
    {
        SendChatCalls++;

        if (AnswerDelay > TimeSpan.Zero)
        {
            await Task.Delay(AnswerDelay, cancellationToken).ConfigureAwait(false);
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        return OnSendChat?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK);
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
