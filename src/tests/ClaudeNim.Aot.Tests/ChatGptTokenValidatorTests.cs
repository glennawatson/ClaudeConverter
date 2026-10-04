// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers signed subscription account identities.</summary>
public sealed class ChatGptTokenValidatorTests
{
    /// <summary>The fixture account's client identifier.</summary>
    private const string ClientId = "test-client";

    /// <summary>The fixture login nonce.</summary>
    private const string Nonce = "test-nonce";

    /// <summary>A signed identity returns its verified subject.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task ValidateReturnsVerifiedSubject()
    {
        using var key = RSA.Create();
        var (token, keys) = Grant(key);
        var subject = ChatGptTokenValidator.Validate(token, keys, ClientId, Nonce, new FakeTimeProvider());

        await Assert.That(subject).IsEqualTo("account");
    }

    /// <summary>An unrelated nonce or client cannot activate a profile.</summary>
    /// <param name="clientId">The expected audience.</param>
    /// <param name="nonce">The expected nonce.</param>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    [Arguments("other-client", Nonce)]
    [Arguments(ClientId, "other-nonce")]
    public async Task ValidateRejectsAnotherLogin(string clientId, string nonce)
    {
        using var key = RSA.Create();
        var (token, keys) = Grant(key);

        await Assert.That(() => ChatGptTokenValidator.Validate(token, keys, clientId, nonce, new FakeTimeProvider())).Throws<InvalidDataException>();
    }

    /// <summary>Expired identities cannot activate a profile.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task ValidateRejectsExpiredToken()
    {
        using var key = RSA.Create();
        var (token, keys) = Grant(key);
        var time = new FakeTimeProvider();
        time.Advance(TimeSpan.FromHours(1));

        await Assert.That(() => ChatGptTokenValidator.Validate(token, keys, ClientId, Nonce, time)).Throws<InvalidDataException>();
    }

    /// <summary>An unknown signing key cannot activate a profile.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task ValidateRejectsInvalidSignature()
    {
        using var signingKey = RSA.Create();
        using var otherKey = RSA.Create();
        var (token, _) = Grant(signingKey);
        var (_, keys) = Grant(otherKey);

        await Assert.That(() => ChatGptTokenValidator.Validate(token, keys, ClientId, Nonce, new FakeTimeProvider())).Throws<InvalidDataException>();
    }

    /// <summary>Creates a signed identity and its public key set.</summary>
    /// <param name="rsa">The signing key.</param>
    /// <returns>The token and public keys.</returns>
    private static (string Token, JsonElement Keys) Grant(RSA rsa)
    {
        var header = Encode("{\"alg\":\"RS256\",\"kid\":\"test\"}"u8.ToArray());
        var payload = Encode("""{"iss":"https://auth.openai.com","aud":"test-client","nonce":"test-nonce","exp":3600,"sub":"account"}"""u8.ToArray());
        var signed = $"{header}.{payload}";
        var signature = Encode(rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var parameters = rsa.ExportParameters(false);
        using var keys = JsonDocument.Parse($$"""{"keys":[{"kid":"test","kty":"RSA","n":"{{Encode(parameters.Modulus!)}}","e":"{{Encode(parameters.Exponent!)}}"}]}""");
        return ($"{signed}.{signature}", keys.RootElement.Clone());
    }

    /// <summary>Encodes a JWT part.</summary>
    /// <param name="bytes">The part bytes.</param>
    /// <returns>The encoded part.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
