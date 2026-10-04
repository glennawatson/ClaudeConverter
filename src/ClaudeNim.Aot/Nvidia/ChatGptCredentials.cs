// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Stores and renews one subscription account profile.</summary>
internal static class ChatGptCredentials
{
    /// <summary>The resource authorized for subscription inference.</summary>
    internal const string Resource = "https://api.openai.com/v1";

    /// <summary>The required subscription permission.</summary>
    internal const string PlanScope = "chatgpt.tokens.use.direct";

    /// <summary>The OAuth token endpoint.</summary>
    internal const string TokenEndpoint = "https://auth.openai.com/api/accounts/oauth/token";

    /// <summary>The profile's registration field.</summary>
    private const string ClientIdField = "client_id";

    /// <summary>The renewable session credential field.</summary>
    private const string RefreshTokenField = "refresh_token";

    /// <summary>Resolves the selected credential profile.</summary>
    /// <param name="configured">An optional profile path.</param>
    /// <returns>The absolute profile path.</returns>
    internal static string PathFor(string configured) => string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "claudenim", "chatgpt.json")
        : Path.GetFullPath(configured);

    /// <summary>Reads a profile when it exists.</summary>
    /// <param name="path">The profile path.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The profile, or null before login.</returns>
    internal static async Task<JsonElement?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var file = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    /// <summary>Serializes login and refresh operations across processes.</summary>
    /// <param name="path">The profile path.</param>
    /// <param name="cancellationToken">Stops waiting for the lock.</param>
    /// <returns>The lock file handle.</returns>
    internal static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return OpenProtected($"{path}.lock", FileMode.OpenOrCreate);
            }
            catch (IOException)
            {
                const int retryMilliseconds = 100;
                await Task.Delay(retryMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes a complete profile atomically.</summary>
    /// <param name="path">The profile path.</param>
    /// <param name="clientId">The issued client identifier.</param>
    /// <param name="hostId">The persistent host identifier.</param>
    /// <param name="subject">The verified account subject.</param>
    /// <param name="tokens">The OAuth token response.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <param name="time">The clock used for token expiry.</param>
    /// <returns>A task that completes after the profile is saved.</returns>
    internal static async Task SaveAsync(string path, string clientId, string hostId, string subject, JsonElement tokens, CancellationToken cancellationToken, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        CheckPermission(tokens);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = OpenProtected(temporary, FileMode.CreateNew))
            {
                await using var writer = new Utf8JsonWriter(file);
                writer.WriteStartObject();
                writer.WriteString(ClientIdField, clientId);
                writer.WriteString("host_id", hostId);
                writer.WriteString(nameof(subject), subject);
                writer.WriteString("expires_at", time.GetUtcNow().AddSeconds(tokens.GetProperty("expires_in").GetInt32()));
                writer.WritePropertyName(nameof(tokens));
                tokens.WriteTo(writer);
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Reads or refreshes an access token under the profile lock.</summary>
    /// <param name="http">The authentication transport.</param>
    /// <param name="path">The selected profile.</param>
    /// <param name="cancellationToken">Stops the operation.</param>
    /// <param name="time">The clock used for token expiry.</param>
    /// <returns>The current bearer credential.</returns>
    /// <exception cref="InvalidOperationException">The profile has no saved login.</exception>
    /// <exception cref="InvalidDataException">The profile has no access token.</exception>
    internal static async Task<string> AccessTokenAsync(HttpClient http, string path, CancellationToken cancellationToken, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        await using var profileLock = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        var profile = await ReadAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Run --chatgpt-login before using subscription authentication.");
        var tokens = profile.GetProperty("tokens");
        CheckPermission(tokens);
        const int refreshMarginSeconds = 60;
        if (profile.GetProperty("expires_at").GetDateTimeOffset() <= time.GetUtcNow().AddSeconds(refreshMarginSeconds))
        {
            var clientId = profile.GetProperty(ClientIdField).GetString()!;
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = RefreshTokenField,
                [ClientIdField] = clientId,
                [RefreshTokenField] = tokens.GetProperty(RefreshTokenField).GetString()!,
                ["resource"] = Resource,
            });
            using var response = await http.PostAsync(new Uri(TokenEndpoint), body, cancellationToken).ConfigureAwait(false);
            _ = response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            tokens = MergeTokens(document.RootElement, tokens);
            await SaveAsync(path, clientId, profile.GetProperty("host_id").GetString()!, profile.GetProperty("subject").GetString()!, tokens, cancellationToken, time).ConfigureAwait(false);
        }

        return tokens.GetProperty("access_token").GetString()
            ?? throw new InvalidDataException("The subscription profile has no access token.");
    }

    /// <summary>Checks the permission and bearer credential type.</summary>
    /// <param name="tokens">The token response.</param>
    /// <exception cref="InvalidDataException">The account did not grant subscription usage.</exception>
    internal static void CheckPermission(JsonElement tokens)
    {
        if (!string.Equals(tokens.GetProperty("token_type").GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !tokens.GetProperty("scope").GetString()!.Split(' ').Contains(PlanScope, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The account did not grant subscription usage permission.");
        }
    }

    /// <summary>Retains grant fields omitted by an OAuth refresh response.</summary>
    /// <param name="replacement">The refreshed tokens.</param>
    /// <param name="previous">The previous grant.</param>
    /// <returns>The complete replacement token set.</returns>
    private static JsonElement MergeTokens(JsonElement replacement, JsonElement previous)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in replacement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            foreach (var field in new[] { "scope", "id_token", RefreshTokenField })
            {
                if (replacement.TryGetProperty(field, out _) || !previous.TryGetProperty(field, out var retained))
                {
                    continue;
                }

                writer.WritePropertyName(field);
                retained.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
        return document.RootElement.Clone();
    }

    /// <summary>Creates a file with owner-only Unix permissions.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="mode">The file creation mode.</param>
    /// <returns>The open file.</returns>
    private static FileStream OpenProtected(string path, FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new(path, options);
    }
}
