// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Endpoints.Codex;
using ClaudeNim.Aot.Hosting;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers registering the proxy's services and routes.</summary>
public sealed class HostingTests
{
    /// <summary>Registering the proxy's services resolves every documented service.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RegisteredServicesResolve()
    {
        var services = new ServiceCollection();

        var returned = services.AddClaudeNimProxy(new ConfigurationBuilder().Build());

        await Assert.That(ReferenceEquals(returned, services)).IsTrue();

        var provider = services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<IModelRouter>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<IRequestGate>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<INimClient>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<INimModelCatalog>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<MessageServices>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<CodexServices>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<ProxyAuthenticationFilter>()).IsNotNull();
    }

    /// <summary>A null services collection is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullServicesThrows() =>
        await Assert.That(static () => ProxyServiceExtensions.AddClaudeNimProxy(null!, new ConfigurationBuilder().Build()))
            .Throws<ArgumentNullException>();

    /// <summary>A null configuration is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullConfigurationThrows() =>
        await Assert.That(static () => new ServiceCollection().AddClaudeNimProxy(null!)).Throws<ArgumentNullException>();

    /// <summary>Every route group registers without throwing, once the services it depends on are present.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RoutesRegisterAgainstARealApplication()
    {
        var builder = WebApplication.CreateSlimBuilder();
        _ = builder.Services.AddClaudeNimProxy(builder.Configuration);
        var app = builder.Build();

        var mapped = app.MapClaudeNimEndpoints();

        await Assert.That(ReferenceEquals(mapped, app)).IsTrue();
    }

    /// <summary>A null endpoint route builder is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullEndpointsThrows() =>
        await Assert.That(static () => ProxyEndpointExtensions.MapClaudeNimEndpoints(null!)).Throws<ArgumentNullException>();

    /// <summary>Configuring JSON serialization inserts the generated context ahead of the default resolver.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ConfigureJsonSerializationInsertsTheGeneratedContext()
    {
        var options = new JsonOptions();

        ProxyServiceExtensions.ConfigureJsonSerialization(options);

        await Assert.That(options.SerializerOptions.TypeInfoResolverChain[0]).IsEqualTo(ProxyJsonContext.Default);
    }

    /// <summary>A null JSON options instance is rejected immediately.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ConfigureJsonSerializationNullOptionsThrows() =>
        await Assert.That(static () => ProxyServiceExtensions.ConfigureJsonSerialization(null!)).Throws<ArgumentNullException>();
}
