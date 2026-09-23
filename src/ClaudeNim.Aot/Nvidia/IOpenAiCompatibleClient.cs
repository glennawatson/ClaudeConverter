// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>The policy applied around a call to an OpenAI-compatible endpoint other than NIM.</summary>
/// <remarks>
/// Deliberately simpler than <see cref="INimClient"/>: there is no retry ladder, no downgrade
/// ladder, no rate-limit pacing, and no per-tier timeout override. Those all exist to work around
/// properties specific to NVIDIA's shared free-tier endpoints — a saturated neighbour, a model that
/// rejects a field a different one accepts — and nothing implementing this interface is that kind
/// of upstream. What every implementation still needs is a bound, so a server that never answers
/// does not hang the turn forever.
/// </remarks>
public interface IOpenAiCompatibleClient
{
    /// <summary>Issues one chat completion, bounded by the deadline its shape calls for.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The upstream response, with headers received.</returns>
    Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken);
}
