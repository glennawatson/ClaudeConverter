// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using Microsoft.AspNetCore.Http;

namespace ClaudeNim.Aot.Endpoints;

/// <summary>Rejects a request that does not present the configured shared secret.</summary>
/// <param name="Options">The configured secret.</param>
/// <remarks>
/// An empty secret disables the check outright, which is only safe on a loopback binding. The
/// header forms accepted are described by <see cref="CredentialHeaders"/>.
/// </remarks>
public sealed record ProxyAuthenticationFilter(ProxyAuthenticationOptions Options) : IEndpointFilter
{
    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return IsAuthorized(context.HttpContext.Request)
            ? await next(context).ConfigureAwait(false)
            : AnthropicErrors.Result(
                StatusCodes.Status401Unauthorized,
                "The request did not present a valid credential.");
    }

    /// <summary>Determines whether a request presented the configured secret.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> when the request may proceed.</returns>
    private bool IsAuthorized(HttpRequest request)
    {
        var expected = Options.AuthToken;

        return string.IsNullOrEmpty(expected)
            || CredentialHeaders.Matches(request.Headers[CredentialHeaders.ApiKey], expected)
            || string.Equals(
                CredentialHeaders.Bearer(request.Headers.Authorization),
                expected,
                StringComparison.Ordinal);
    }
}
