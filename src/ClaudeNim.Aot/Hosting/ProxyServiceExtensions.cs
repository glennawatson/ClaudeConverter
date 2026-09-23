// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using CodexEndpoints = ClaudeNim.Aot.Endpoints.Codex;

namespace ClaudeNim.Aot.Hosting;

/// <summary>Registers everything the proxy is assembled from.</summary>
/// <remarks>
/// <para>
/// Options are bound once at startup and registered as the records themselves rather than behind
/// <c>IOptions&lt;T&gt;</c>. Positional records have no parameterless constructor for the options
/// factory to call, and nothing here is reloaded while the process runs, so the indirection would
/// buy nothing.
/// </para>
/// <para>
/// The upstream transport is registered through Refit's own
/// <c>AddRefitGeneratedClient</c>, which wires the compile-time interface implementation into
/// <see cref="IHttpClientFactory"/>. Taking the serializer context directly is what keeps the
/// whole transport reflection-free, which is what native AOT requires.
/// </para>
/// </remarks>
public static class ProxyServiceExtensions
{
    /// <summary>How long a pooled upstream connection is reused before being replaced.</summary>
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The settings the generated transport resolves and serializes with.</summary>
    /// <remarks>
    /// <para>
    /// The serializer is the proxy's own source-generated context, which keeps both directions on
    /// the fast path and free of reflection.
    /// </para>
    /// <para>
    /// RFC 3986 resolution is chosen deliberately over Refit's legacy mode. The legacy mode
    /// requires every route to begin with a slash and then resolves it against the host, which
    /// discards the <c>/v1</c> the base address is rooted at and turns every upstream call into a
    /// 404. Under RFC 3986 the routes stay relative and compose onto the configured path, so a
    /// self-hosted NIM behind a path prefix works as well as NVIDIA's own endpoint.
    /// </para>
    /// </remarks>
    private static readonly RefitSettings TransportSettings = BuildTransportSettings();

    /// <summary>The services this proxy is assembled from.</summary>
    /// <param name="services">The container the services are added to.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Registers the proxy's services.</summary>
        /// <param name="configuration">The bound configuration.</param>
        /// <returns>The same container, so calls can be chained.</returns>
        public IServiceCollection AddClaudeNimProxy(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            var nim = configuration.GetSection(NvidiaNimOptions.SectionName).Get<NvidiaNimOptions>()
                ?? new NvidiaNimOptions();
            var timeouts = configuration.GetSection(HttpTimeoutOptions.SectionName).Get<HttpTimeoutOptions>()
                ?? new HttpTimeoutOptions();

            _ = services.AddSingleton(nim);
            _ = services.AddSingleton(timeouts);
            _ = services.AddSingleton(
                configuration.GetSection(ModelRoutingOptions.SectionName).Get<ModelRoutingOptions>()
                ?? new ModelRoutingOptions());
            _ = services.AddSingleton(
                configuration.GetSection(ModelCatalogOptions.SectionName).Get<ModelCatalogOptions>()
                ?? new ModelCatalogOptions());
            _ = services.AddSingleton(
                configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                ?? new RateLimitOptions());
            _ = services.AddSingleton(
                configuration.GetSection(OptimizationOptions.SectionName).Get<OptimizationOptions>()
                ?? new OptimizationOptions());
            _ = services.AddSingleton(
                configuration.GetSection(ProxyAuthenticationOptions.SectionName).Get<ProxyAuthenticationOptions>()
                ?? new ProxyAuthenticationOptions());
            _ = services.AddSingleton(
                configuration.GetSection(RetryOptions.SectionName).Get<RetryOptions>()
                ?? new RetryOptions());
            _ = services.AddSingleton(
                configuration.GetSection(ModelHealthOptions.SectionName).Get<ModelHealthOptions>()
                ?? new ModelHealthOptions());

            _ = services.AddSingleton(TimeProvider.System);
            _ = services.AddSingleton<IModelRouter, ModelRouter>();
            _ = services.AddSingleton<IRequestGate, RequestGate>();
            _ = services.AddSingleton<IRequestPacer, RequestPacer>();
            _ = services.AddSingleton<INimClient, NimClient>();
            _ = services.AddSingleton<INimModelCatalog, NimModelCatalog>();
            _ = services.AddSingleton<IModelHealthTracker, ModelHealthTracker>();

            // Each protocol registers its own IRequestOptimizer (or none, if it has no housekeeping
            // traffic worth recognising), ICompletionTranslator and IStreamTranslatorFactory behind
            // that protocol's own interfaces — the two share nothing beyond both translating out of
            // the same Nim* wire types, which is why both sets carry the same interface names in
            // different namespaces rather than one shared abstraction.
            _ = services.AddSingleton<IRequestOptimizer, AnthropicRequestOptimizer>();
            _ = services.AddSingleton<ICompletionTranslator, AnthropicCompletionTranslator>();
            _ = services.AddSingleton<IStreamTranslatorFactory, AnthropicStreamTranslatorFactory>();
            _ = services.AddSingleton<MessageServices>();

            _ = services.AddSingleton<CodexEndpoints.ICompletionTranslator, CodexEndpoints.ResponsesCompletionTranslator>();
            _ = services.AddSingleton<CodexEndpoints.IStreamTranslatorFactory, CodexEndpoints.ResponsesStreamTranslatorFactory>();
            _ = services.AddSingleton<CodexEndpoints.CodexServices>();

            _ = services.AddSingleton<ProxyAuthenticationFilter>();

            return services.AddNimTransport(nim, timeouts);
        }

        /// <summary>Registers the generated Refit surface and the connection it runs over.</summary>
        /// <param name="nim">The configured NIM settings.</param>
        /// <param name="timeouts">The configured upstream timeouts.</param>
        /// <returns>The same container, so calls can be chained.</returns>
        private IServiceCollection AddNimTransport(NvidiaNimOptions nim, HttpTimeoutOptions timeouts)
        {
            _ = services
                .AddRefitGeneratedClient<INimApi>(ProxyJsonContext.Default, static _ => TransportSettings)
                .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new(BaseAddress(nim.BaseUrl));

                    // HttpClient.Timeout would apply to the whole call, streamed body included,
                    // and cut off a long answer partway through. The header phase is bounded
                    // explicitly by NimClient instead; a streamed body is bounded by idle time in
                    // NimStreamTranslator, and a non-streamed body by the same header timeout
                    // applied again around the read.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .AddAuthorizationHeaderValueProvider(static (provider, _, _) =>
                    ValueTask.FromResult(provider.GetRequiredService<NvidiaNimOptions>().ApiKey))
                .ConfigurePrimaryHttpMessageHandler(() => Handler(nim, timeouts));

            return services;
        }
    }

    /// <summary>Points minimal API's own JSON serialization at the proxy's generated context.</summary>
    /// <param name="options">The JSON options minimal API resolves request and response bodies through.</param>
    /// <remarks>
    /// The slim builder's JSON options default to reflection. Inserting the generated context ahead
    /// of the default resolver chain is what lets minimal API's own serialization run under native
    /// AOT, where <c>JsonSerializerIsReflectionEnabledByDefault</c> is off.
    /// </remarks>
    public static void ConfigureJsonSerialization(JsonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, ProxyJsonContext.Default);
    }

    /// <summary>Builds the settings the generated transport resolves and serializes with.</summary>
    /// <returns>The settings.</returns>
    private static RefitSettings BuildTransportSettings()
    {
        var serializer = new SystemTextJsonContentSerializer(ProxyJsonContext.Default.Options);

        return new(serializer) { UrlResolution = UrlResolutionMode.Rfc3986 };
    }

    /// <summary>Builds the primary handler the upstream connection is made through.</summary>
    /// <param name="nim">The configured NIM settings.</param>
    /// <param name="timeouts">The configured upstream timeouts.</param>
    /// <returns>The handler.</returns>
    private static SocketsHttpHandler Handler(NvidiaNimOptions nim, HttpTimeoutOptions timeouts)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(timeouts.ConnectSeconds),
            PooledConnectionLifetime = ConnectionLifetime,
            AutomaticDecompression = DecompressionMethods.All,
        };

        if (!string.IsNullOrWhiteSpace(nim.Proxy))
        {
            handler.Proxy = new WebProxy(nim.Proxy);
            handler.UseProxy = true;
        }

        return handler;
    }

    /// <summary>Normalises the configured base address so a relative route appends to it.</summary>
    /// <param name="baseUrl">The configured base address.</param>
    /// <returns>The address with a trailing separator.</returns>
    private static string BaseAddress(string baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl) ? NvidiaNimOptions.DefaultBaseUrl : baseUrl;
        return value.EndsWith('/') ? value : $"{value}/";
    }
}
