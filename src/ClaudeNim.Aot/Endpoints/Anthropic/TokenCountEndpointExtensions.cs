// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>The token counting route Claude clients size a conversation against.</summary>
/// <remarks>
/// The count is answered locally. NVIDIA NIM exposes no tokenizer endpoint, and a client calls
/// this on nearly every keystroke of a long session, so forwarding it would add a round trip to
/// each one. The figure is an estimate; see <see cref="TokenEstimator"/> for why an exact count
/// is not available to a proxy.
/// </remarks>
public static class TokenCountEndpointExtensions
{
    /// <summary>The token counting route.</summary>
    /// <param name="endpoints">The route builder the endpoint is added to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers the token counting route.</summary>
        /// <returns>The group the endpoint was registered in.</returns>
        public RouteGroupBuilder MapTokenCountEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup("/v1/messages/count_tokens").WithTags("Messages");

            _ = group.MapPost("/", CountTokens).WithName(nameof(CountTokens));

            return group;
        }
    }

    /// <summary>Estimates how many input tokens a request would consume.</summary>
    /// <param name="request">The conversation to measure.</param>
    /// <returns>The estimated count.</returns>
    internal static IResult CountTokens(TokenCountRequest request)
    {
        if (request is null)
        {
            return AnthropicErrors.Result(
                StatusCodes.Status400BadRequest,
                "The request body was missing or unreadable.");
        }

        // A body carrying no messages at all is a well-formed question with the answer zero, and
        // the estimator refuses a null list outright. Left unguarded that reaches Kestrel as an
        // unhandled exception, so a client asking how large an empty conversation is takes a 500
        // rather than a number.
        TokenCountResponse response = new(
            TokenEstimator.Estimate(request.Messages ?? [], request.System, request.Tools));

        return TypedResults.Json(response, ProxyJsonContext.Default.TokenCountResponse);
    }
}
