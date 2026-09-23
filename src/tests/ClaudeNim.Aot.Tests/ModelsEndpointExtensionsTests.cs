// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers listing and resolving models through the Models API.</summary>
public sealed class ModelsEndpointExtensionsTests
{
    /// <summary>The identifier of the first fixture model.</summary>
    private const string FirstModelId = "model-1";

    /// <summary>The identifier of the second fixture model.</summary>
    private const string SecondModelId = "model-2";

    /// <summary>The identifier of the third fixture model.</summary>
    private const string ThirdModelId = "model-3";

    /// <summary>The size of the fixture listing.</summary>
    private const int FixtureListingSize = 3;

    /// <summary>The size of the window expected once one model is excluded by a cursor or limit.</summary>
    private const int WindowedSize = 2;

    /// <summary>An unpaged listing returns every model with no cursor.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task UnpagedListingReturnsEveryModel()
    {
        var catalog = new FakeNimModelCatalog(ThreeModels());

        var result = await ModelsEndpointExtensions.ListModelsAsync(catalog, null, null, null, CancellationToken.None);

        var typed = (JsonHttpResult<ModelListResponse>)result;
        await Assert.That(typed.Value!.Data.Count).IsEqualTo(FixtureListingSize);
        await Assert.That(typed.Value.HasMore).IsFalse();
        await Assert.That(typed.Value.FirstId).IsEqualTo(FirstModelId);
        await Assert.That(typed.Value.LastId).IsEqualTo(ThirdModelId);
    }

    /// <summary>A limit smaller than the listing reports more remains.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task LimitSmallerThanTheListingReportsMoreRemains()
    {
        var catalog = new FakeNimModelCatalog(ThreeModels());

        var result = await ModelsEndpointExtensions.ListModelsAsync(catalog, null, null, WindowedSize, CancellationToken.None);

        var typed = (JsonHttpResult<ModelListResponse>)result;
        await Assert.That(typed.Value!.Data.Count).IsEqualTo(WindowedSize);
        await Assert.That(typed.Value.HasMore).IsTrue();
        await Assert.That(typed.Value.LastId).IsEqualTo(SecondModelId);
    }

    /// <summary>An <c>after_id</c> cursor windows the listing to what follows it.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AfterCursorWindowsToWhatFollows()
    {
        var catalog = new FakeNimModelCatalog(ThreeModels());

        var result = await ModelsEndpointExtensions.ListModelsAsync(catalog, null, FirstModelId, null, CancellationToken.None);

        var typed = (JsonHttpResult<ModelListResponse>)result;
        await Assert.That(typed.Value!.Data.Count).IsEqualTo(WindowedSize);
        await Assert.That(typed.Value.FirstId).IsEqualTo(SecondModelId);
    }

    /// <summary>A <c>before_id</c> cursor windows the listing to what precedes it.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task BeforeCursorWindowsToWhatPrecedes()
    {
        var catalog = new FakeNimModelCatalog(ThreeModels());

        var result = await ModelsEndpointExtensions.ListModelsAsync(catalog, ThirdModelId, null, null, CancellationToken.None);

        var typed = (JsonHttpResult<ModelListResponse>)result;
        await Assert.That(typed.Value!.Data.Count).IsEqualTo(WindowedSize);
        await Assert.That(typed.Value.LastId).IsEqualTo(SecondModelId);
    }

    /// <summary>A known model identifier resolves to that model.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task KnownIdentifierResolves()
    {
        var catalog = new FakeNimModelCatalog(ThreeModels());

        var result = await ModelsEndpointExtensions.GetModelAsync(catalog, SecondModelId, CancellationToken.None);

        await Assert.That(((JsonHttpResult<ModelDescriptor>)result).Value!.Id).IsEqualTo(SecondModelId);
    }

    /// <summary>An unknown model identifier resolves to a not-found error.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnknownIdentifierIsNotFound()
    {
        var catalog = new FakeNimModelCatalog(ThreeModels());

        var result = await ModelsEndpointExtensions.GetModelAsync(catalog, "no-such-model", CancellationToken.None);

        await Assert.That(((JsonHttpResult<ErrorResponse>)result).StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
    }

    /// <summary>Builds three models in listing order.</summary>
    /// <returns>The models.</returns>
    private static List<ModelDescriptor> ThreeModels() =>
    [
        new(FirstModelId, "Model One", DateTimeOffset.UnixEpoch, 1, 1, default),
        new(SecondModelId, "Model Two", DateTimeOffset.UnixEpoch, 1, 1, default),
        new(ThirdModelId, "Model Three", DateTimeOffset.UnixEpoch, 1, 1, default),
    ];
}
