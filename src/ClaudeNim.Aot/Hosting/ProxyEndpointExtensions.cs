// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Endpoints.Codex;
using Microsoft.AspNetCore.Routing;

namespace ClaudeNim.Aot.Hosting;

/// <summary>Registers the proxy's routes.</summary>
/// <remarks>
/// Each area registers itself through its own <c>Map…Endpoints</c> method, so the route table for
/// a feature lives beside the code that serves it rather than accumulating in the entry point.
/// The authentication filter is applied here, once, to every protocol group: health stays open so
/// an orchestrator can probe it without holding the proxy's secret.
/// </remarks>
public static class ProxyEndpointExtensions
{
    /// <summary>The proxy's routes.</summary>
    /// <param name="endpoints">The route builder the endpoints are added to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers every route the proxy serves.</summary>
        /// <returns>The same builder, so calls can be chained.</returns>
        public IEndpointRouteBuilder MapClaudeNimEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            _ = endpoints.MapHealthEndpoints();

            _ = endpoints.MapMessagesEndpoints().AddEndpointFilter<ProxyAuthenticationFilter>();
            _ = endpoints.MapTokenCountEndpoints().AddEndpointFilter<ProxyAuthenticationFilter>();
            _ = endpoints.MapModelsEndpoints().AddEndpointFilter<ProxyAuthenticationFilter>();

            _ = endpoints.MapResponsesEndpoints().AddEndpointFilter<ProxyAuthenticationFilter>();

            return endpoints;
        }
    }
}
