// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers subscription credential renewal and permissions.</summary>
public sealed class ChatGptCredentialsTests
{
    /// <summary>Refresh rotates tokens while retaining the granted scope.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task RefreshSavesRotatingTokensAndRetainsScope()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "account.json");
        _ = Directory.CreateDirectory(directory);
        try
        {
            using var original = JsonDocument.Parse("""
                {"access_token":"old-access","refresh_token":"old-refresh","token_type":"Bearer",
                 "scope":"chatgpt.tokens.use.direct","id_token":"retained-id","expires_in":1}
                """);
            var time = new FakeTimeProvider();
            await ChatGptCredentials.SaveAsync(path, "issued-client", "host", "subject", original.RootElement, CancellationToken.None, time);
            using var handler = new FakeSubscriptionHandler
            {
                OnSend = static _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"access_token":"new-access","refresh_token":"new-refresh","token_type":"Bearer","expires_in":3600}
                        """,
                        Encoding.UTF8,
                        "application/json"),
                }),
            };
            var services = new ServiceCollection();
            _ = services.AddHttpClient("subscription-test").ConfigurePrimaryHttpMessageHandler(() => handler);
            await using var provider = services.BuildServiceProvider();
            using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("subscription-test");
            var access = await ChatGptCredentials.AccessTokenAsync(http, path, CancellationToken.None, time);
            var saved = await ChatGptCredentials.ReadAsync(path, CancellationToken.None);

            await Assert.That(access).IsEqualTo("new-access");
            await Assert.That(handler.Body!).Contains("client_id=issued-client");
            await Assert.That(handler.Body!).Contains("refresh_token=old-refresh");
            await Assert.That(saved!.Value.GetProperty("tokens").GetProperty("refresh_token").GetString()).IsEqualTo("new-refresh");
            await Assert.That(saved.Value.GetProperty("tokens").GetProperty("scope").GetString()).IsEqualTo(ChatGptCredentials.PlanScope);
            if (!OperatingSystem.IsWindows())
            {
                await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Identity permission alone cannot authorize subscription requests.</summary>
    /// <returns>A task that completes after the assertions.</returns>
    [Test]
    public async Task CheckPermissionRejectsIdentityOnlyGrant()
    {
        using var tokens = JsonDocument.Parse("{\"token_type\":\"Bearer\",\"scope\":\"openid profile email\"}");

        await Assert.That(() => ChatGptCredentials.CheckPermission(tokens.RootElement)).Throws<InvalidDataException>();
    }
}
