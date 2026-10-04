// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Runs browser authorization for a local subscription profile.</summary>
internal static class ChatGptLogin
{
    /// <summary>The first registration client identifier.</summary>
    private const string RegistrationClient = "dynamic_agent_client";

    /// <summary>The loopback callback used for this application.</summary>
    private const string Callback = "http://127.0.0.1:1455/auth/callback";

    /// <summary>The issued registration field.</summary>
    private const string ClientIdField = "client_id";

    /// <summary>Signs in and saves a validated profile.</summary>
    /// <param name="http">The authentication transport.</param>
    /// <param name="path">The profile path.</param>
    /// <param name="output">The command's terminal output.</param>
    /// <param name="cancellationToken">Stops the login attempt.</param>
    /// <returns>A task that completes after login.</returns>
    /// <exception cref="InvalidDataException">The returned account does not match the profile.</exception>
    internal static async Task SignInAsync(HttpClient http, string path, TextWriter output, CancellationToken cancellationToken)
    {
        await using var profileLock = await ChatGptCredentials.LockAsync(path, cancellationToken).ConfigureAwait(false);
        var previous = await ChatGptCredentials.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var hostId = await HostIdAsync(ChatGptCredentials.PathFor(string.Empty), cancellationToken).ConfigureAwait(false);
        var clientId = previous?.GetProperty(ClientIdField).GetString() ?? RegistrationClient;
        var state = RandomValue();
        var nonce = RandomValue();
        var verifier = RandomValue();
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:1455/");
        listener.Start();
        var url = AuthorizationUrl(clientId, hostId, state, nonce, verifier);
        await output.WriteLineAsync("Continue with ChatGPT. Open this URL in your browser:".AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(url.AsMemory(), cancellationToken).ConfigureAwait(false);
        var callback = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var issuedId = ValidateCallback(callback.Request, state, clientId);
            var tokens = await ExchangeAsync(http, issuedId, callback.Request.QueryString["code"]!, verifier, cancellationToken).ConfigureAwait(false);
            var keys = await KeysAsync(http, cancellationToken).ConfigureAwait(false);
            var subject = ChatGptTokenValidator.Validate(tokens.GetProperty("id_token").GetString()!, keys, issuedId, nonce);
            if (previous is { } saved && saved.GetProperty(nameof(subject)).GetString() != subject)
            {
                throw new InvalidDataException("The selected account does not match this profile. Choose another profile path.");
            }

            await ChatGptCredentials.SaveAsync(path, issuedId, hostId, subject, tokens, cancellationToken).ConfigureAwait(false);
            var message = "Sign-in complete. You can close this window."u8.ToArray();
            callback.Response.ContentType = "text/plain; charset=utf-8";
            await callback.Response.OutputStream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"Subscription credentials saved to {path}".AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            callback.Response.Close();
        }
    }

    /// <summary>Creates the authorization URL.</summary>
    /// <param name="clientId">The registration identifier.</param>
    /// <param name="hostId">The persistent host identifier.</param>
    /// <param name="state">The callback state.</param>
    /// <param name="nonce">The identity nonce.</param>
    /// <param name="verifier">The PKCE verifier.</param>
    /// <returns>The browser URL.</returns>
    internal static string AuthorizationUrl(string clientId, string hostId, string state, string nonce, string verifier)
    {
        var query = new Dictionary<string, string?>
        {
            [ClientIdField] = clientId,
            ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code",
            ["redirect_uri"] = Callback,
            ["scope"] = $"openid profile email offline_access resource.invoke {ChatGptCredentials.PlanScope}",
            ["resource"] = ChatGptCredentials.Resource,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
        };
        if (clientId == RegistrationClient)
        {
            query.Add("agent_name_hint", "ClaudeConverter");
        }

        return QueryHelpers.AddQueryString("https://auth.openai.com/api/accounts/authorize", query);
    }

    /// <summary>Checks the callback before exchanging any code.</summary>
    /// <param name="request">The browser callback.</param>
    /// <param name="state">The expected state.</param>
    /// <param name="clientId">The pending registration identifier.</param>
    /// <returns>The issued client identifier.</returns>
    /// <exception cref="InvalidDataException">The callback does not match the pending login.</exception>
    private static string ValidateCallback(HttpListenerRequest request, string state, string clientId)
    {
        if (request.Url?.AbsolutePath != "/auth/callback" || request.QueryString[nameof(state)] != state)
        {
            throw new InvalidDataException("The login callback does not match this attempt.");
        }

        if (request.QueryString["error"] is not null || string.IsNullOrEmpty(request.QueryString["code"]))
        {
            throw new InvalidDataException("The login was declined or returned no authorization code.");
        }

        var issuedId = request.QueryString[ClientIdField] ?? clientId;
        if (issuedId == RegistrationClient || (clientId != RegistrationClient && issuedId != clientId))
        {
            throw new InvalidDataException("The login returned an invalid client identifier.");
        }

        return issuedId;
    }

    /// <summary>Exchanges the authorization code.</summary>
    /// <param name="http">The authentication transport.</param>
    /// <param name="clientId">The issued client identifier.</param>
    /// <param name="code">The authorization code.</param>
    /// <param name="verifier">The PKCE verifier.</param>
    /// <param name="cancellationToken">Stops the exchange.</param>
    /// <returns>The token response.</returns>
    private static async Task<JsonElement> ExchangeAsync(HttpClient http, string clientId, string code, string verifier, CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            [ClientIdField] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = Callback,
            ["resource"] = ChatGptCredentials.Resource,
        });
        using var response = await http.PostAsync(new Uri(ChatGptCredentials.TokenEndpoint), body, cancellationToken).ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    /// <summary>Fetches the trusted issuer's signing keys.</summary>
    /// <param name="http">The authentication transport.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The public key set.</returns>
    /// <exception cref="InvalidDataException">The signing key address is not trusted.</exception>
    private static async Task<JsonElement> KeysAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var discovery = JsonDocument.Parse(await http.GetStringAsync(new Uri("https://auth.openai.com/.well-known/openid-configuration"), cancellationToken).ConfigureAwait(false));
        var keyUri = new Uri(discovery.RootElement.GetProperty("jwks_uri").GetString()!);
        if (keyUri.Scheme != Uri.UriSchemeHttps || keyUri.Host != "auth.openai.com")
        {
            throw new InvalidDataException("The issuer returned an unexpected signing key address.");
        }

        using var keys = JsonDocument.Parse(await http.GetStringAsync(keyUri, cancellationToken).ConfigureAwait(false));
        return keys.RootElement.Clone();
    }

    /// <summary>Reads or creates the stable host identifier.</summary>
    /// <param name="path">The shared host registration path.</param>
    /// <param name="cancellationToken">Stops the file operation.</param>
    /// <returns>The stable host identifier.</returns>
    private static async Task<string> HostIdAsync(string path, CancellationToken cancellationToken)
    {
        var hostPath = $"{path}.host";
        await using var hostLock = await ChatGptCredentials.LockAsync(hostPath, cancellationToken).ConfigureAwait(false);
        if (File.Exists(hostPath))
        {
            return await File.ReadAllTextAsync(hostPath, cancellationToken).ConfigureAwait(false);
        }

        var hostId = $"urn:uuid:{Guid.NewGuid():D}";
        await File.WriteAllTextAsync(hostPath, hostId, cancellationToken).ConfigureAwait(false);
        return hostId;
    }

    /// <summary>Creates a fresh authorization secret.</summary>
    /// <returns>A random base64url value.</returns>
    private static string RandomValue()
    {
        const int secretBytes = 32;
        return Encode(RandomNumberGenerator.GetBytes(secretBytes));
    }

    /// <summary>Encodes a base64url value.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The encoded value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
