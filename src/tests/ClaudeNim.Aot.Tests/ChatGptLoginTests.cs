// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;
using Microsoft.AspNetCore.WebUtilities;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the public registration request.</summary>
public sealed class ChatGptLoginTests
{
    /// <summary>Registration binds the consent request to the host and PKCE challenge.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task AuthorizationUrlIncludesPlanPermissionAndPkce()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var url = new Uri(ChatGptLogin.AuthorizationUrl("dynamic_agent_client", "urn:uuid:test", "state", "nonce", verifier));
        var query = QueryHelpers.ParseQuery(url.Query);

        await Assert.That(url.Host).IsEqualTo("auth.openai.com");
        await Assert.That(query["code_challenge"].ToString()).IsEqualTo("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
        await Assert.That(query["scope"].ToString()).Contains("chatgpt.tokens.use.direct");
        await Assert.That(query["redirect_uri"].ToString()).IsEqualTo("http://127.0.0.1:1455/auth/callback");
        await Assert.That(query["ext_agent_host_id"].ToString()).IsEqualTo("urn:uuid:test");
        await Assert.That(query["agent_name_hint"].ToString()).IsEqualTo("ClaudeConverter");
    }

    /// <summary>Returning login reuses its issued registration.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task ReturningAuthorizationOmitsRegistrationName()
    {
        var url = new Uri(ChatGptLogin.AuthorizationUrl("issued-client", "host", "state", "nonce", "verifier"));
        var query = QueryHelpers.ParseQuery(url.Query);

        await Assert.That(query["client_id"].ToString()).IsEqualTo("issued-client");
        await Assert.That(query.ContainsKey("agent_name_hint")).IsFalse();
    }
}
