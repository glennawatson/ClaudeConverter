// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Codex.Streaming;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Builds a <see cref="ResponsesStreamTranslator"/> for one attempt at a streamed turn.</summary>
/// <param name="time">The clock the turn's timestamp is read from.</param>
/// <remarks>
/// A reasoning model may have had its opening <c>&lt;think&gt;</c> written by the chat template
/// rather than by itself, whatever this turn asked for: the GLM 5.3 template seeds it
/// unconditionally and never reads <c>enable_thinking</c>.
/// </remarks>
public sealed class ResponsesStreamTranslatorFactory(TimeProvider time) : IStreamTranslatorFactory
{
    /// <inheritdoc/>
    public IStreamTranslator Create(CodexSseWriter writer, ResolvedModel resolved, TimeSpan idleTimeout, ILogger logger)
    {
        var seeded = resolved.ThinkingEnabled || NimModelCatalogDefaults.SupportsThinking(resolved.NimModel);

        return new ResponsesStreamTranslator(
            new(writer, resolved.NimModel, resolved.ThinkingEnabled, seeded, idleTimeout, time, logger));
    }
}
