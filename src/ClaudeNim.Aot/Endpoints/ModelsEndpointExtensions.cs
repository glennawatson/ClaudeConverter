// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ClaudeNim.Aot.Endpoints;

/// <summary>The Models API, which is how a Claude client discovers what it can run.</summary>
/// <remarks>
/// <para>
/// This is the surface the comparable proxies get wrong, and the reason a model picker pointed at
/// one shows nothing usable. Three things have to be right together: the entry shape, the cursor,
/// and the single-model route.
/// </para>
/// <para>
/// The cursor is real here. <c>before_id</c> and <c>after_id</c> select a window of the listing,
/// <c>limit</c> bounds it, and <c>has_more</c>, <c>first_id</c> and <c>last_id</c> describe the
/// page that was actually returned. Hard-coding <c>has_more</c> to false, which is the common
/// shortcut, strands every model past the first page.
/// </para>
/// <para>
/// <c>GET /v1/models/{id}</c> is a required part of the API, not an optional extra: a client that
/// resumes a session with a stored model identifier asks for that one model before it will use it.
/// </para>
/// </remarks>
public static class ModelsEndpointExtensions
{
    /// <summary>The page size used when the caller names none, matching the documented default.</summary>
    private const int DefaultLimit = 20;

    /// <summary>The largest page the listing will return, matching the documented maximum.</summary>
    private const int MaximumLimit = 1000;

    /// <summary>The position reported for an identifier that is absent or unknown.</summary>
    private const int NotFound = -1;

    /// <summary>The Models API routes.</summary>
    /// <param name="endpoints">The route builder the endpoints are added to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers the Models API routes.</summary>
        /// <returns>The group the endpoints were registered in.</returns>
        public RouteGroupBuilder MapModelsEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup("/v1/models").WithTags("Models");

            _ = group.MapGet("/", ListModelsAsync).WithName(nameof(ListModelsAsync));
            _ = group.MapGet("/{modelId}", GetModelAsync).WithName(nameof(GetModelAsync));

            return group;
        }
    }

    /// <summary>Returns one page of the advertised listing.</summary>
    /// <param name="catalog">The advertised model catalogue.</param>
    /// <param name="beforeId">Returns the models that precede this identifier.</param>
    /// <param name="afterId">Returns the models that follow this identifier.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The page, with a cursor describing it.</returns>
    internal static async Task<IResult> ListModelsAsync(
        INimModelCatalog catalog,
        [FromQuery(Name = "before_id")] string? beforeId,
        [FromQuery(Name = "after_id")] string? afterId,
        [FromQuery(Name = "limit")] int? limit,
        CancellationToken cancellationToken)
    {
        var models = await catalog.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var window = Window(models, beforeId, afterId);
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);
        var page = window.Count <= size ? window : window.GetRange(0, size);

        ModelListResponse response = new(
            page,
            window.Count > page.Count,
            page.Count > 0 ? page[0].Id : null,
            page.Count > 0 ? page[^1].Id : null);

        return TypedResults.Json(response, ProxyJsonContext.Default.ModelListResponse);
    }

    /// <summary>Returns one advertised model.</summary>
    /// <param name="catalog">The advertised model catalogue.</param>
    /// <param name="modelId">The identifier to look up.</param>
    /// <param name="cancellationToken">Abandons the call when the client disconnects.</param>
    /// <returns>The model, or a not-found error.</returns>
    internal static async Task<IResult> GetModelAsync(
        INimModelCatalog catalog,
        string modelId,
        CancellationToken cancellationToken)
    {
        var model = await catalog.FindModelAsync(modelId, cancellationToken).ConfigureAwait(false);

        return model is null
            ? AnthropicErrors.Result(
                StatusCodes.Status404NotFound,
                $"The model '{modelId}' is not available.")
            : TypedResults.Json(model, ProxyJsonContext.Default.ModelDescriptor);
    }

    /// <summary>Applies the caller's cursor to the full listing.</summary>
    /// <param name="models">The full listing.</param>
    /// <param name="beforeId">The identifier the window ends before, when supplied.</param>
    /// <param name="afterId">The identifier the window starts after, when supplied.</param>
    /// <returns>The selected window, which is the whole listing when no cursor was given.</returns>
    private static List<ModelDescriptor> Window(
        List<ModelDescriptor> models,
        string? beforeId,
        string? afterId)
    {
        var start = IndexOf(models, afterId) + 1;
        var end = beforeId is null ? models.Count : Math.Max(IndexOf(models, beforeId), start);

        return start == 0 && end == models.Count
            ? models
            : models.GetRange(start, Math.Max(end - start, 0));
    }

    /// <summary>Finds a model's position in the listing.</summary>
    /// <param name="models">The full listing.</param>
    /// <param name="modelId">The identifier to find, which may be absent.</param>
    /// <returns>The index, or <see cref="NotFound"/> when the identifier is absent or unknown.</returns>
    private static int IndexOf(List<ModelDescriptor> models, string? modelId)
    {
        if (string.IsNullOrEmpty(modelId))
        {
            return NotFound;
        }

        for (var i = 0; i < models.Count; i++)
        {
            if (string.Equals(models[i].Id, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return NotFound;
    }
}
