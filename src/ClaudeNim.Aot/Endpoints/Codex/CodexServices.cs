// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.RateLimiting;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Everything one Responses API turn is served from.</summary>
/// <param name="Router">Chooses the NIM model that serves the requested one.</param>
/// <param name="Client">The upstream transport.</param>
/// <param name="Gate">Bounds how fast requests reach the upstream.</param>
/// <param name="Nim">The configured NIM defaults.</param>
/// <param name="Retries">The configured retry behaviour, which also bounds streamed re-attempts.</param>
/// <param name="Catalog">The sizing the advertised listing is built from, which also bounds a turn.</param>
/// <param name="CompletionTranslator">Turns a completed upstream completion into a Responses API turn.</param>
/// <param name="StreamTranslatorFactory">Builds the translator each streamed attempt is served through.</param>
/// <param name="Timeouts">The configured upstream timeouts.</param>
/// <param name="Time">The clock a re-issued streamed turn's backoff is measured against.</param>
/// <param name="Logger">The diagnostic log.</param>
/// <remarks>
/// No request optimizer: Codex's own housekeeping traffic, if it has any, has not been observed and
/// characterised the way Claude Code's has, so nothing here is answered locally yet.
/// </remarks>
public sealed record CodexServices(
    IModelRouter Router,
    INimClient Client,
    IRequestGate Gate,
    NvidiaNimOptions Nim,
    RetryOptions Retries,
    ModelCatalogOptions Catalog,
    ICompletionTranslator CompletionTranslator,
    IStreamTranslatorFactory StreamTranslatorFactory,
    HttpTimeoutOptions Timeouts,
    TimeProvider Time,
    ILogger<CodexServices> Logger);
