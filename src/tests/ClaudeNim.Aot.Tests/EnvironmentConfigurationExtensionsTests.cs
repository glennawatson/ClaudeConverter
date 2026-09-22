// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Configuration;
using Microsoft.Extensions.Configuration;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers layering the flat and .NET-style environment variable conventions onto configuration.</summary>
/// <remarks>
/// Every test here mutates process-wide environment variables, so the class runs serialized rather
/// than interleaved with the rest of the suite.
/// </remarks>
[NotInParallel]
public sealed class EnvironmentConfigurationExtensionsTests
{
    /// <summary>The legacy flat name for the NVIDIA credential.</summary>
    private const string LegacyApiKeyVariable = "NVIDIA_API_KEY";

    /// <summary>The configuration key the credential is bound to.</summary>
    private const string ApiKeyConfigurationKey = "NvidiaNim:ApiKey";

    /// <summary>A flat legacy name is mapped onto its .NET configuration key.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task FlatNameMapsOntoItsConfigurationKey()
    {
        using var scope = new EnvironmentVariableScope(LegacyApiKeyVariable, "nvapi-flat");

        var configuration = Build();

        await Assert.That(configuration[ApiKeyConfigurationKey]).IsEqualTo("nvapi-flat");
    }

    /// <summary>The .NET path form reaches a setting with no flat name at all.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DotNetPathFormReachesAnyUnnamedSetting()
    {
        using var scope = new EnvironmentVariableScope("NvidiaNim__MaxTokens", "8192");

        var configuration = Build();

        await Assert.That(configuration["NvidiaNim:MaxTokens"]).IsEqualTo("8192");
    }

    /// <summary>The prefixed .NET form also reaches a setting.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task PrefixedDotNetFormReachesASetting()
    {
        using var scope = new EnvironmentVariableScope("CLAUDENIM_ModelRouting__EnableThinking", "false");

        var configuration = Build();

        await Assert.That(configuration["ModelRouting:EnableThinking"]).IsEqualTo("false");
    }

    /// <summary>The .NET-style name overrides the flat legacy name when both are present.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DotNetFormOverridesTheFlatForm()
    {
        using var flat = new EnvironmentVariableScope(LegacyApiKeyVariable, "flat-value");
        using var path = new EnvironmentVariableScope("NvidiaNim__ApiKey", "path-value");

        var configuration = Build();

        await Assert.That(configuration[ApiKeyConfigurationKey]).IsEqualTo("path-value");
    }

    /// <summary>An unset flat name leaves the configuration key absent.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnsetFlatNameLeavesTheKeyAbsent()
    {
        using var scope = new EnvironmentVariableScope("BIG_MODEL", null);

        var configuration = Build();

        await Assert.That(configuration["ModelRouting:Opus"]).IsNull();
    }

    /// <summary>A later flat name in a group wins over an earlier alias for the same key.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task LaterAliasInAGroupWins()
    {
        using var legacy = new EnvironmentVariableScope(LegacyApiKeyVariable, "legacy");
        using var current = new EnvironmentVariableScope("NVIDIA_NIM_API_KEY", "current");

        var configuration = Build();

        await Assert.That(configuration[ApiKeyConfigurationKey]).IsEqualTo("current");
    }

    /// <summary>Chaining <c>AddClaudeNimEnvironment</c> returns the same builder.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReturnsTheSameBuilderForChaining()
    {
        var builder = new ConfigurationBuilder();

        var returned = builder.AddClaudeNimEnvironment();

        await Assert.That(ReferenceEquals(returned, builder)).IsTrue();
    }

    /// <summary>A null builder is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullBuilderThrows() =>
        await Assert.That(static () => EnvironmentConfigurationExtensions.AddClaudeNimEnvironment(null!))
            .Throws<ArgumentNullException>();

    /// <summary>Builds configuration from the environment alone.</summary>
    /// <returns>The built configuration.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IConfigurationRoot Build() => new ConfigurationBuilder().AddClaudeNimEnvironment().Build();

    /// <summary>Sets an environment variable for the lifetime of the scope, then restores it.</summary>
    private sealed class EnvironmentVariableScope : IDisposable
    {
        /// <summary>The variable name this scope owns.</summary>
        private readonly string _name;

        /// <summary>The value the variable held before the scope set it.</summary>
        private readonly string? _original;

        /// <summary>Initializes a new instance of the <see cref="EnvironmentVariableScope"/> class.</summary>
        /// <param name="name">The variable name.</param>
        /// <param name="value">The value to set for the scope's lifetime.</param>
        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
