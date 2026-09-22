// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints;

/// <summary>Everything one Messages API turn is served from.</summary>
/// <param name="Router">Chooses the NIM model that serves the requested one.</param>
/// <param name="Client">The upstream transport.</param>
/// <param name="Gate">Bounds how fast requests reach the upstream.</param>
/// <param name="Nim">The configured NIM defaults.</param>
/// <param name="Retries">The configured retry behaviour, which also bounds streamed re-attempts.</param>
/// <param name="Catalog">The sizing the advertised listing is built from, which also bounds a turn.</param>
/// <param name="Optimizations">The switches for the local fast paths.</param>
/// <param name="Timeouts">The configured upstream timeouts.</param>
/// <param name="Time">The clock a re-issued streamed turn's backoff is measured against.</param>
/// <param name="Logger">The diagnostic log.</param>
/// <remarks>
/// The handler takes this one parameter rather than six. A minimal API endpoint's signature is
/// its dependency list, and six of them makes the shape of the turn harder to read than the code
/// that serves it.
/// </remarks>
public sealed record MessageServices(
    IModelRouter Router,
    INimClient Client,
    IRequestGate Gate,
    NvidiaNimOptions Nim,
    RetryOptions Retries,
    ModelCatalogOptions Catalog,
    OptimizationOptions Optimizations,
    HttpTimeoutOptions Timeouts,
    TimeProvider Time,
    ILogger<MessageServices> Logger);
