// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Refit;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The NVIDIA NIM OpenAI-compatible HTTP surface.</summary>
/// <remarks>
/// <para>
/// Refit's interface stub generator turns this declaration into the request-building code at
/// compile time, so the transport carries no runtime emit and stays usable under native AOT. The
/// bodies and responses are serialized through the proxy's own
/// <see cref="Serialization.ProxyJsonContext"/>, which keeps the whole path source-generated.
/// </para>
/// <para>
/// The credential is supplied by <c>RefitSettings.AuthorizationHeaderValueGetter</c> rather than
/// being passed through this interface, so no call site ever handles the key.
/// </para>
/// <para>
/// The routes are relative and carry no leading slash. Under RFC 3986 resolution a leading slash
/// is an absolute path and discards the base address's own path — which would quietly drop the
/// <c>/v1</c> that every NIM deployment is rooted at and turn each call into a 404.
/// </para>
/// </remarks>
[Headers("Authorization: Bearer")]
public interface INimApi
{
    /// <summary>Issues a chat completion.</summary>
    /// <param name="request">The upstream request body.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The raw upstream response, which the caller owns and must dispose.</returns>
    /// <remarks>
    /// The response is returned unread so a streamed turn can be forwarded delta by delta; reading
    /// it to completion first would defeat streaming entirely.
    /// </remarks>
    [Post("chat/completions")]
    Task<HttpResponseMessage> SendChatAsync([Body] NimChatRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the models the configured credential can reach.</summary>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream listing, wrapped so a failure status does not throw.</returns>
    [Get("models")]
    Task<IApiResponse<NimModelList>> ListModelsAsync(CancellationToken cancellationToken);
}
