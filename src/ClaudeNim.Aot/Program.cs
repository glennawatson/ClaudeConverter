// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics.CodeAnalysis;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Hosting;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeNim.Aot;

/// <summary>The proxy's entry point.</summary>
/// <remarks>
/// A plain class with a <see cref="Main"/> method, rather than top-level statements, so the type
/// can be referenced directly — by <c>WebApplicationFactory&lt;Program&gt;</c> in the integration
/// tests, and by anything else that needs to host the app in-process. It is not declared
/// <see langword="static"/> only because a static type cannot be used as a generic type argument;
/// the compiler's own implicit constructor is never called by anything in this codebase.
/// </remarks>
[SuppressMessage(
    "Design",
    "SST1432:Type declares only static members",
    Justification = "Kept an instance type so it can be used as a generic argument for WebApplicationFactory<Program>.")]
public sealed class Program
{
    /// <summary>Builds and runs the proxy.</summary>
    /// <param name="args">The process command-line arguments.</param>
    /// <returns>A task that completes once the host shuts down.</returns>
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        // Environment variables win over the settings file: the proxy is usually run from a shell
        // or a container, where a credential in a file on disk is the thing you least want.
        _ = builder.Configuration.AddClaudeNimEnvironment();

        // The slim builder's JSON options still default to reflection. Pointing them at the
        // generated context is what lets minimal API's own serialization run under native AOT.
        _ = builder.Services.ConfigureHttpJsonOptions(static options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ProxyJsonContext.Default));

        _ = builder.Services.AddClaudeNimProxy(builder.Configuration);

        var app = builder.Build();

        _ = app.MapClaudeNimEndpoints();

        await app.RunAsync().ConfigureAwait(false);
    }
}
