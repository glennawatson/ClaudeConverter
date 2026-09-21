// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Controls the shared secret a client must present to use the proxy.</summary>
/// <param name="AuthToken">
/// The token clients send as <c>x-api-key</c> or as a bearer token. An empty value disables
/// authentication, which is only safe when the proxy is bound to a loopback address.
/// </param>
[System.Diagnostics.DebuggerDisplay("ProxyAuthenticationOptions: {ToString(),nq}")]
public sealed record ProxyAuthenticationOptions(string AuthToken = "")
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Authentication";
}
