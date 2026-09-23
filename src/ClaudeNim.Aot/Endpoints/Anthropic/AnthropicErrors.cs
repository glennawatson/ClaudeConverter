// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Http;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>Builds the error bodies Anthropic clients know how to act on.</summary>
/// <remarks>
/// A client decides whether to retry, re-authenticate, or surface a message to the user from the
/// error <c>type</c>, not from the status code. Returning a raw upstream failure, or labelling
/// everything <c>api_error</c>, costs the client that decision — so the upstream status is mapped
/// onto the vocabulary the client actually understands.
/// </remarks>
public static class AnthropicErrors
{
    /// <summary>The non-standard status Anthropic uses to say the upstream is overloaded.</summary>
    internal const int StatusOverloaded = 529;

    /// <summary>Creates an error result.</summary>
    /// <param name="statusCode">The status to return.</param>
    /// <param name="message">Prose describing the failure.</param>
    /// <returns>The error result.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IResult Result(int statusCode, string message) =>
        TypedResults.Json(
            ErrorResponse.Create(TypeFor(statusCode), message),
            ProxyJsonContext.Default.ErrorResponse,
            statusCode: statusCode);

    /// <summary>Maps a status code onto the Anthropic error class that describes it.</summary>
    /// <param name="statusCode">The status being returned.</param>
    /// <returns>The error class.</returns>
    public static string TypeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "invalid_request_error",
        StatusCodes.Status401Unauthorized => "authentication_error",
        StatusCodes.Status403Forbidden => "permission_error",
        StatusCodes.Status404NotFound => "not_found_error",
        StatusCodes.Status413PayloadTooLarge => "request_too_large",
        StatusCodes.Status429TooManyRequests => "rate_limit_error",

        // NVIDIA reports saturation as a plain 503. Anthropic clients back off on
        // overloaded_error and treat a bare api_error as fatal, so the two are folded together.
        StatusCodes.Status503ServiceUnavailable or StatusOverloaded => "overloaded_error",
        _ => "api_error",
    };
}
