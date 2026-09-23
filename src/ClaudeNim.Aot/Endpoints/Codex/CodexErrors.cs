// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Http;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Builds the error bodies a Responses API client knows how to act on.</summary>
/// <remarks>
/// The Codex counterpart of <c>Endpoints.Anthropic.AnthropicErrors</c>, mapping the same upstream
/// statuses onto the error vocabulary OpenAI's own APIs use instead of Anthropic's.
/// </remarks>
public static class CodexErrors
{
    /// <summary>The non-standard status Anthropic uses to say the upstream is overloaded, which NIM also emits.</summary>
    internal const int StatusOverloaded = 529;

    /// <summary>Creates an error result.</summary>
    /// <param name="statusCode">The status to return.</param>
    /// <param name="message">Prose describing the failure.</param>
    /// <returns>The error result.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IResult Result(int statusCode, string message) =>
        TypedResults.Json(
            CodexErrorResponse.Create(TypeFor(statusCode), message),
            ProxyJsonContext.Default.CodexErrorResponse,
            statusCode: statusCode);

    /// <summary>Maps a status code onto the error class that describes it.</summary>
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
        StatusCodes.Status503ServiceUnavailable or StatusOverloaded => "server_error",
        _ => "server_error",
    };
}
