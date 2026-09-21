// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Primitives;

namespace ClaudeNim.Aot.Endpoints;

/// <summary>Reads the credential out of the headers a Claude client may send it in.</summary>
/// <remarks>
/// Two forms are in circulation. The official Anthropic SDKs send <c>x-api-key</c>; most
/// OpenAI-shaped tooling sends <c>Authorization: Bearer</c>. Accepting both means a client already
/// configured for either works against this proxy unchanged.
/// </remarks>
public static class CredentialHeaders
{
    /// <summary>The header the Anthropic SDKs send the credential in.</summary>
    internal const string ApiKey = "x-api-key";

    /// <summary>The prefix an <c>Authorization</c> header uses for a bearer token.</summary>
    private const string BearerPrefix = "Bearer ";

    /// <summary>Strips the bearer prefix from an <c>Authorization</c> header.</summary>
    /// <param name="header">The header values, which may be absent.</param>
    /// <returns>The token, or an empty string when the header carried none.</returns>
    public static string Bearer(StringValues header)
    {
        var value = header.Count > 0 ? header[0] : null;

        return value is not null && value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[BearerPrefix.Length..]
            : string.Empty;
    }

    /// <summary>Compares a presented credential against the configured one.</summary>
    /// <param name="presented">The credential the client sent, which may be absent.</param>
    /// <param name="expected">The configured secret.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Matches(StringValues presented, string expected) =>
        presented.Count > 0 && string.Equals(presented[0], expected, StringComparison.Ordinal);
}
