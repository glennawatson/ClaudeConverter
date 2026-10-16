// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using Microsoft.Extensions.Configuration;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers Azure discovery and protected local configuration.</summary>
public sealed class AzureFoundrySetupTests
{
    /// <summary>The standard Windows Azure CLI wrapper path used by command tests.</summary>
    private const string WindowsCli = @"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd";

    /// <summary>The account name in the single-account discovery response.</summary>
    private const string AccountName = "account";

    /// <summary>The profile filename in the isolated test directories.</summary>
    private const string ProfileName = "azure.json";

    /// <summary>The simulated credential that must stay out of command output.</summary>
    private const string PrivateKey = "private-key";

    /// <summary>A profile path that validation failures must not write.</summary>
    private const string UnusedProfile = "unused-azure.json";

    /// <summary>The CLI operation returning deployments.</summary>
    private const string DeploymentCommand = "deployment";

    /// <summary>Available deployments become Azure routes with NVIDIA backup models.</summary>
    /// <param name="cancellationToken">Stops the test operations.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ConfigureAsync_ChatDeployments_SavesAzureRoutingAndNvidiaFallbacks(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, ProfileName);
        _ = Directory.CreateDirectory(directory);
        try
        {
            await using var output = new StringWriter();
            await AzureFoundrySetup.ConfigureAsync([], path, output, ExecuteAsync, cancellationToken);
            var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
            await Assert.That(configuration["ModelRouting:Default"]).IsEqualTo("azure:best");
            await Assert.That(configuration["ModelRouting:Opus"]).IsEqualTo("azure:best");
            await Assert.That(configuration["ModelRouting:Sonnet"]).IsEqualTo("azure:balanced");
            await Assert.That(configuration["ModelRouting:Haiku"]).IsEqualTo("azure:quick");
            await Assert.That(configuration["ModelRouting:FallbackForExplicitModels"]).IsEqualTo("True");
            await Assert.That(configuration["ModelRouting:DefaultFallbacks"]).IsEqualTo("nvidia/nemotron-3-super-120b-a12b,deepseek-ai/deepseek-v4.1-flash");
            await Assert.That(configuration["AzureFoundry:ModelNames:best"]).IsEqualTo("gpt-6.1-sol");
            await Assert.That(configuration["AzureFoundry:BaseUrl"]).IsEqualTo("https://example.services.ai.azure.com/");
            await Assert.That(configuration["AzureFoundry:ApiKey"]).IsEqualTo(PrivateKey);
            await Assert.That(configuration["ModelCatalog:AdditionalModels"]).IsEqualTo("azure:best,azure:balanced,azure:quick,azure:reasoning");
            await Assert.That(output.ToString()).DoesNotContain(PrivateKey);
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

    /// <summary>Account selection rejects shell operators before Azure CLI can run.</summary>
    /// <param name="name">The unsafe resource group name.</param>
    /// <param name="cancellationToken">Stops the setup operation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("group&command")]
    [Arguments("group%PATH%")]
    [Arguments("group!value")]
    [Arguments("group|command")]
    [Arguments("group>file")]
    [Arguments("group^value")]
    public async Task ConfigureAsync_UnsafeResourceName_DoesNotRunCli(string name, CancellationToken cancellationToken)
    {
        var called = false;
        await using var output = new StringWriter();
        await Assert.That(() => AzureFoundrySetup.ConfigureAsync(
            [name, AccountName],
            Path.Combine(Path.GetTempPath(), UnusedProfile),
            output,
            (args, token) =>
            {
                called = true;
                return ExecuteAsync(args, token);
            },
            cancellationToken)).Throws<InvalidOperationException>();

        await Assert.That(called).IsFalse();
    }

    /// <summary>Ambiguous discovery cannot silently choose another Azure account.</summary>
    /// <param name="cancellationToken">Stops account discovery.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ConfigureAsync_MultipleAccounts_RequiresExplicitSelection(CancellationToken cancellationToken)
    {
        await using var output = new StringWriter();
        await Assert.That(() => AzureFoundrySetup.ConfigureAsync(
            [],
            Path.Combine(Path.GetTempPath(), UnusedProfile),
            output,
            static (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult("""
                    [
                      {"name":"first","resourceGroup":"one","kind":"OpenAI","endpoint":"https://one.example.com/"},
                      {"name":"second","resourceGroup":"two","kind":"AIServices","endpoint":"https://two.example.com/"}
                    ]
                    """);
            },
            cancellationToken)).Throws<InvalidOperationException>();

        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
    }

    /// <summary>Explicit account selection uses actual custom deployments when preferred models are absent.</summary>
    /// <param name="cancellationToken">Stops discovery and profile saving.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ConfigureAsync_SelectedAccountWithCustomDeployment_UsesAvailableModelForAllTiers(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, ProfileName);
        var calls = new List<string[]>();
        _ = Directory.CreateDirectory(directory);
        try
        {
            await using var output = new StringWriter();
            await AzureFoundrySetup.ConfigureAsync(
                ["two", "second"],
                path,
                output,
                (args, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    calls.Add(args);
                    if (args.Contains(DeploymentCommand, StringComparer.Ordinal))
                    {
                        return Task.FromResult("""[{"name":"custom","model":{"name":"DeepSeek-V4-Pro"},"capabilities":{"chatCompletion":"true"}}]""");
                    }

                    return args.Contains("keys", StringComparer.Ordinal)
                        ? ExecuteAsync(args, token)
                        : Task.FromResult("""
                            [
                              {"name":"first","resourceGroup":"one","kind":"OpenAI","endpoint":"https://one.example.com/"},
                              {"name":"second","resourceGroup":"two","kind":"AIServices","endpoint":"https://two.example.com/"}
                            ]
                            """);
                },
                cancellationToken);
            var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
            foreach (var tier in new[] { "Default", "Opus", "Sonnet", "Haiku" })
            {
                await Assert.That(configuration[$"ModelRouting:{tier}"]).IsEqualTo("azure:custom");
            }

            await Assert.That(configuration["AzureFoundry:BaseUrl"]).IsEqualTo("https://two.example.com/");
            await Assert.That(calls[1].Contains("two", StringComparer.Ordinal)).IsTrue();
            await Assert.That(calls[1].Contains("second", StringComparer.Ordinal)).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Image, embedding and failed deployments cannot replace a working local profile.</summary>
    /// <param name="cancellationToken">Stops discovery and profile reads.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ConfigureAsync_NoReadyChatDeployments_PreservesExistingProfile(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, ProfileName);
        _ = Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "unchanged", cancellationToken);
            await using var output = new StringWriter();
            await Assert.That(() => AzureFoundrySetup.ConfigureAsync(
                [],
                path,
                output,
                static (args, token) => args.Contains(DeploymentCommand, StringComparer.Ordinal)
                    ? Task.FromResult("""
                        [
                          {"name":"failed","provisioningState":"Failed","capabilities":{"chatCompletion":"true"}},
                          {"name":"image","capabilities":{"imageGenerations":"true"}},
                          {"name":"embedding","capabilities":{"embeddings":"true"}},
                          {"name":"not-chat","capabilities":{"chatCompletion":"false"}},
                          {"name":"unknown","capabilities":null}
                        ]
                        """)
                    : ExecuteAsync(args, token),
                cancellationToken)).Throws<InvalidOperationException>();

            await Assert.That(await File.ReadAllTextAsync(path, cancellationToken)).IsEqualTo("unchanged");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A cancelled setup cannot run discovery or leave a partial profile.</summary>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ConfigureAsync_CancelledOperation_DoesNotSaveProfile(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await source.CancelAsync();
        await using var output = new StringWriter();
        await Assert.That(() => AzureFoundrySetup.ConfigureAsync([], path, output, ExecuteAsync, source.Token)).Throws<OperationCanceledException>();

        await Assert.That(File.Exists(path)).IsFalse();
    }

    /// <summary>Direct Azure CLI invocations preserve separate argument values without a shell.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CommandFor_Executable_UsesArgumentList()
    {
        const int expectedArguments = 2;
        var command = AzureFoundrySetup.CommandFor("/usr/bin/az", [AccountName, "list"], windows: false);

        await Assert.That(command.UseShellExecute).IsFalse();
        await Assert.That(command.ArgumentList.Count).IsEqualTo(expectedArguments);
        await Assert.That(command.ArgumentList[0]).IsEqualTo(AccountName);
        await Assert.That(command.Arguments).IsEqualTo(string.Empty);
    }

    /// <summary>The standard Windows CLI wrapper remains a separate, safely quoted argument.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CommandFor_WindowsWrapper_UsesInstalledShimWithoutComposedArguments()
    {
        var command = AzureFoundrySetup.CommandFor(WindowsCli, [AccountName, "list"], windows: true);

        await Assert.That(Path.GetFileName(command.FileName)).IsEqualTo("cmd.exe");
        await Assert.That(command.ArgumentList[3]).IsEqualTo("call");
        await Assert.That(command.ArgumentList[4]).IsEqualTo(WindowsCli);
        await Assert.That(command.ArgumentList[5]).IsEqualTo(AccountName);
        await Assert.That(command.Arguments).IsEqualTo(string.Empty);
    }

    /// <summary>Windows wrapper arguments reject shell expansion and command chaining.</summary>
    /// <param name="argument">The unsafe argument.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("&command")]
    [Arguments("%PATH%")]
    [Arguments("!value")]
    [Arguments("|command")]
    [Arguments("^value")]
    [Arguments("<file")]
    [Arguments(">file")]
    [Arguments("\"value")]
    public async Task CommandFor_WindowsWrapperWithUnsafeArgument_RejectsInvocation(string argument) =>
        await Assert.That(() => AzureFoundrySetup.CommandFor(WindowsCli, [argument], windows: true)).Throws<InvalidOperationException>();

    /// <summary>A relative profile override becomes an absolute path.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PathFor_ConfiguredPath_ResolvesAbsolutePath() =>
        await Assert.That(AzureFoundrySetup.PathFor("azure-custom.json")).IsEqualTo(Path.GetFullPath("azure-custom.json"));

    /// <summary>Malformed Azure CLI output cannot expose response content in setup errors.</summary>
    /// <param name="cancellationToken">Stops the command operation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RunAsync_InvalidJson_DoesNotPrintResponse(CancellationToken cancellationToken)
    {
        await using var output = new StringWriter();
        await using var errors = new StringWriter();
        var result = await AzureFoundrySetup.RunAsync(
            [],
            Path.Combine(Path.GetTempPath(), UnusedProfile),
            output,
            errors,
            static (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(PrivateKey);
            },
            cancellationToken);

        await Assert.That(result).IsEqualTo(1);
        await Assert.That(errors.ToString()).Contains("invalid JSON");
        await Assert.That(errors.ToString()).DoesNotContain(PrivateKey);
    }

    /// <summary>File errors produce a useful message without exposing exception content.</summary>
    /// <param name="cancellationToken">Stops the command operation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RunAsync_FileFailure_DoesNotPrintExceptionContent(CancellationToken cancellationToken)
    {
        await using var output = new StringWriter();
        await using var errors = new StringWriter();
        var result = await AzureFoundrySetup.RunAsync(
            [],
            Path.Combine(Path.GetTempPath(), UnusedProfile),
            output,
            errors,
            static (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromException<string>(new IOException(PrivateKey));
            },
            cancellationToken);

        await Assert.That(result).IsEqualTo(1);
        await Assert.That(errors.ToString()).Contains("access permissions");
        await Assert.That(errors.ToString()).DoesNotContain(PrivateKey);
    }

    /// <summary>Returns the account, deployment and credential discovery responses.</summary>
    /// <param name="args">The Azure CLI arguments.</param>
    /// <param name="cancellationToken">Stops the simulated operation.</param>
    /// <returns>The discovery JSON for the requested operation.</returns>
    private static Task<string> ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = args.Contains("keys", StringComparer.Ordinal)
            ? """{"key1":"private-key"}"""
            : """[{"name":"account","resourceGroup":"group","kind":"AIServices","endpoint":"https://example.services.ai.azure.com/"}]""";
        if (args.Contains(DeploymentCommand, StringComparer.Ordinal))
        {
            result =
                """
                [
                  {"name":"best","model":{"name":"gpt-6.1-sol","format":"OpenAI"},"capabilities":{"chatCompletion":"true"}},
                  {"name":"balanced","model":{"name":"gpt-6-astra","format":"OpenAI"},"capabilities":{"chatCompletion":"true"}},
                  {"name":"quick","model":{"name":"gpt-6-luna","format":"OpenAI"},"capabilities":{"chatCompletion":"true"}},
                  {"name":"reasoning","model":{"name":"DeepSeek-V4-Pro","format":"DeepSeek"},"capabilities":{"chatCompletion":true}},
                  {"name":"image","model":{"name":"FLUX.2-pro","format":"Black Forest Labs"},"capabilities":{"imageGenerations":"true"}},
                  {"name":"embedding","model":{"name":"text-embedding-3-large","format":"OpenAI"},"capabilities":{"embeddings":"true"}}
                ]
                """;
        }

        return Task.FromResult(result);
    }
}
