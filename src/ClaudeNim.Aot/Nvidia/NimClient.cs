// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The policy applied around the generated NVIDIA NIM transport.</summary>
/// <param name="Api">The Refit-generated NIM surface.</param>
/// <param name="Logger">The diagnostic log.</param>
/// <remarks>
/// <para>
/// <see cref="INimApi"/> describes the wire calls; this record owns the two behaviours the wire
/// shape cannot express.
/// </para>
/// <para>
/// A rejected request is retried once with the reasoning controls removed. Which chat-template
/// arguments a model accepts varies across the NVIDIA catalogue, and a model that does not know
/// one can fail the whole call. Retrying without them turns "this model does not support
/// reasoning knobs" into a plain answer rather than an error the client cannot act on.
/// </para>
/// <para>
/// A failed model listing is reported as an absent listing rather than an exception, because the
/// catalogue is expected to carry on from its built-in profiles when the upstream is unreachable.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimClient: {ToString(),nq}")]
public sealed record NimClient(INimApi Api, ILogger<NimClient> Logger) : INimClient
{
    /// <inheritdoc/>
    public async ValueTask<HttpResponseMessage> SendChatAsync(
        NimChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await Api.SendChatAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.BadRequest || request.ChatTemplateKwargs is null)
        {
            return response;
        }

        NvidiaLog.ChatTemplateRejected(Logger, request.Model);

        response.Dispose();
        return await Api.SendChatAsync(request with { ChatTemplateKwargs = null }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<NimModelList?> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await Api.ListModelsAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response.Content;
        }

        // Refit reports a transport failure as an error on the response rather than throwing,
        // so the two cases are told apart here instead of by a catch block.
        if (response.Error is { } error)
        {
            NvidiaLog.ModelListingUnreachable(Logger, error);
        }
        else
        {
            NvidiaLog.ModelListingFailed(Logger, (int)(response.StatusCode ?? HttpStatusCode.ServiceUnavailable));
        }

        return null;
    }
}
