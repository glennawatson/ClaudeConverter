// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>Builds an <see cref="AnthropicStreamTranslator"/> for one attempt at a streamed turn.</summary>
/// <remarks>
/// A reasoning model may have had its opening <c>&lt;think&gt;</c> written by the chat template
/// rather than by itself, whatever this turn asked for: the GLM 5.3 template seeds it
/// unconditionally and never reads <c>enable_thinking</c>.
/// </remarks>
public sealed class AnthropicStreamTranslatorFactory : IStreamTranslatorFactory
{
    /// <inheritdoc/>
    public IStreamTranslator Create(AnthropicSseWriter writer, ResolvedModel resolved, TimeSpan idleTimeout, ILogger logger)
    {
        var seeded = resolved.ThinkingEnabled || NimModelCatalogDefaults.SupportsThinking(resolved.NimModel);

        return new AnthropicStreamTranslator(
            new(writer, resolved.NimModel, resolved.ThinkingEnabled, seeded, idleTimeout, logger));
    }
}
