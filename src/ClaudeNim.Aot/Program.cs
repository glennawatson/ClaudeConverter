// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics.CodeAnalysis;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot;

/// <summary>The proxy's entry point.</summary>
/// <remarks>
/// <para>
/// A plain class with a <see cref="Main"/> method, rather than top-level statements, so the
/// startup sequence reads like ordinary code. Nothing needs to reference this type directly — the
/// integration tests host the app in-process via <see cref="WebHostMarker"/> instead, since
/// <c>WebApplicationFactory&lt;TEntryPoint&gt;</c> only uses its type argument to locate the entry
/// assembly, not to call anything on the type itself. That lets this class stay
/// <see langword="static"/>, which is what it actually is: every member here is static.
/// </para>
/// <para>
/// Everything <see cref="Main"/> does beyond gluing calls together already lives in a separately
/// tested static helper — <see cref="EnvironmentConfigurationExtensions.AddClaudeNimEnvironment"/>,
/// <see cref="ProxyServiceExtensions.ConfigureJsonSerialization"/>,
/// <see cref="ProxyServiceExtensions.AddClaudeNimProxy"/>, and
/// <see cref="ProxyEndpointExtensions.MapClaudeNimEndpoints"/>. What is left — building the real
/// host and awaiting <c>RunAsync</c> until the process is asked to shut down — cannot run inside a
/// unit test without blocking it, so this class is excluded from the coverage report rather than
/// left showing as untested business logic.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure host wiring; every step it calls is unit tested independently, and RunAsync blocks until process shutdown.")]
public static class Program
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

        // Scopes are on so every line of a turn names the turn it belongs to. The tracing
        // identifiers the logger would otherwise prepend to each of those lines describe a
        // distributed trace this proxy is not part of, and they are long: dropping them is what
        // keeps the scope worth printing.
        _ = builder.Logging.Configure(static options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);

        // Written for whoever is reading it: the journal when systemd is running this, and the
        // terminal otherwise.
        if (ConsoleLogging.Formatter(
            builder.Configuration[ConsoleLogging.FormatterNameKey],
            Environment.GetEnvironmentVariable("JOURNAL_STREAM"),
            Environment.GetEnvironmentVariable("INVOCATION_ID")) is { } formatter)
        {
            _ = builder.Logging.AddConsole(options => options.FormatterName = formatter);
        }

        _ = builder.Services.ConfigureHttpJsonOptions(ProxyServiceExtensions.ConfigureJsonSerialization);

        _ = builder.Services.AddClaudeNimProxy(builder.Configuration);

        var app = builder.Build();

        _ = app.MapClaudeNimEndpoints();

        await app.RunAsync().ConfigureAwait(false);
    }
}
