// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Validates identity before saving subscription credentials.</summary>
internal static class ChatGptTokenValidator
{
    /// <summary>The trusted identity issuer.</summary>
    internal const string Issuer = "https://auth.openai.com";

    /// <summary>Validates the signed identity token.</summary>
    /// <param name="token">The identity token.</param>
    /// <param name="keys">The trusted issuer's public keys.</param>
    /// <param name="clientId">The issued client identifier.</param>
    /// <param name="nonce">The pending login nonce.</param>
    /// <param name="time">The clock used to validate expiry.</param>
    /// <returns>The verified account subject.</returns>
    /// <exception cref="InvalidDataException">The token is invalid for this login.</exception>
    internal static string Validate(string token, JsonElement keys, string clientId, string nonce, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidDataException("The identity token has an invalid format.");
        }

        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        if (header.RootElement.GetProperty("alg").GetString() != "RS256")
        {
            throw new InvalidDataException("The identity token uses an unsupported signature.");
        }

        VerifySignature(parts, header.RootElement.GetProperty("kid").GetString(), keys);
        var claims = payload.RootElement;
        var audience = claims.GetProperty("aud");
        var matches = MatchesAudience(audience, clientId);
        if (!matches || claims.GetProperty("iss").GetString() != Issuer
            || claims.GetProperty(nameof(nonce)).GetString() != nonce
            || claims.GetProperty("exp").GetInt64() <= time.GetUtcNow().ToUnixTimeSeconds())
        {
            throw new InvalidDataException("The identity token does not match this login.");
        }

        return claims.GetProperty("sub").GetString()
            ?? throw new InvalidDataException("The identity token has no account subject.");
    }

    /// <summary>Decodes a base64url value.</summary>
    /// <param name="value">The encoded value.</param>
    /// <returns>The decoded bytes.</returns>
    internal static byte[] Decode(string value)
    {
        const int blockLength = 4;
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + blockLength - 1) / blockLength * blockLength, '='));
    }

    /// <summary>Verifies the token with the issuer's selected RSA key.</summary>
    /// <param name="parts">The encoded token parts.</param>
    /// <param name="keyId">The signing key identifier.</param>
    /// <param name="keys">The issuer's key set.</param>
    /// <exception cref="InvalidDataException">The token signature is invalid.</exception>
    private static void VerifySignature(string[] parts, string? keyId, JsonElement keys)
    {
        foreach (var key in keys.GetProperty(nameof(keys)).EnumerateArray())
        {
            if (key.GetProperty("kid").GetString() != keyId || key.GetProperty("kty").GetString() != "RSA")
            {
                continue;
            }

            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.GetProperty("n").GetString()!), Exponent = Decode(key.GetProperty("e").GetString()!), });
            if (rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return;
            }

            break;
        }

        throw new InvalidDataException("The identity token signature is invalid.");
    }

    /// <summary>Checks the token audience in either permitted JWT form.</summary>
    /// <param name="audience">The audience claim.</param>
    /// <param name="clientId">The expected client identifier.</param>
    /// <returns>Whether the token addresses this client.</returns>
    private static bool MatchesAudience(JsonElement audience, string clientId)
    {
        if (audience.ValueKind == JsonValueKind.String)
        {
            return audience.GetString() == clientId;
        }

        foreach (var value in audience.EnumerateArray())
        {
            if (value.GetString() == clientId)
            {
                return true;
            }
        }

        return false;
    }
}
