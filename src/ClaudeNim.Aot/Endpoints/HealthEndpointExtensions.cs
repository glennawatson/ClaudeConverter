// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClaudeNim.Aot.Endpoints;

/// <summary>The readiness surface, outside the authenticated Anthropic routes.</summary>
/// <remarks>
/// Health is deliberately unauthenticated so a container orchestrator can probe it without being
/// given the proxy's secret, and it answers from the model catalogue so a green result means the
/// upstream credential actually works rather than merely that the process is running.
/// </remarks>
public static class HealthEndpointExtensions
{
    /// <summary>The status reported when the proxy has models to offer.</summary>
    private const string Healthy = "ok";

    /// <summary>The status reported when the proxy is running but has nothing to offer.</summary>
    private const string Degraded = "degraded";

    /// <summary>The health routes.</summary>
    /// <param name="endpoints">The route builder the endpoints are added to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers the health routes.</summary>
        /// <returns>The group the endpoints were registered in.</returns>
        public RouteGroupBuilder MapHealthEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup("/health").WithTags("Health");

            _ = group.MapGet("/", GetHealthAsync).WithName(nameof(GetHealthAsync));
            _ = group.MapGet("/live", static () => TypedResults.Ok()).WithName("Liveness");

            return group;
        }
    }

    /// <summary>Reports whether the proxy can reach the upstream catalogue.</summary>
    /// <param name="catalog">The advertised model catalogue.</param>
    /// <param name="cancellationToken">Abandons the probe when the caller disconnects.</param>
    /// <returns>The number of models the proxy can currently offer.</returns>
    internal static async Task<IResult> GetHealthAsync(
        INimModelCatalog catalog,
        CancellationToken cancellationToken)
    {
        var models = await catalog.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        HealthReport report = new(models.Count > 0 ? Healthy : Degraded, models.Count);

        return TypedResults.Json(report, ProxyJsonContext.Default.HealthReport);
    }
}
