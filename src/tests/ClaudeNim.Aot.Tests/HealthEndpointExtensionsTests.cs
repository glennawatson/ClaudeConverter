// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers reporting whether the proxy can reach the upstream catalogue.</summary>
public sealed class HealthEndpointExtensionsTests
{
    /// <summary>A catalogue offering models reports healthy.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task CatalogueWithModelsReportsHealthy()
    {
        var catalog = new FakeNimModelCatalog([new("claude-sonnet-5", "Claude Sonnet 5", DateTimeOffset.UnixEpoch, 1, 1, default)]);

        var result = await HealthEndpointExtensions.GetHealthAsync(catalog, CancellationToken.None);

        var typed = (JsonHttpResult<HealthReport>)result;
        await Assert.That(typed.Value.Status).IsEqualTo("ok");
        await Assert.That(typed.Value.ModelCount).IsEqualTo(1);
    }

    /// <summary>An empty catalogue reports degraded.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyCatalogueReportsDegraded()
    {
        var catalog = new FakeNimModelCatalog([]);

        var result = await HealthEndpointExtensions.GetHealthAsync(catalog, CancellationToken.None);

        await Assert.That(((JsonHttpResult<HealthReport>)result).Value.Status).IsEqualTo("degraded");
    }
}
