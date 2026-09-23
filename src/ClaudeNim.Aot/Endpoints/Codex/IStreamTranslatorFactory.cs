// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Codex.Streaming;
using ClaudeNim.Aot.Routing;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>Builds the translator one attempt at a streamed turn is served by.</summary>
public interface IStreamTranslatorFactory
{
    /// <summary>Builds the translator for one attempt at a streamed turn.</summary>
    /// <param name="writer">The writer the Responses API events are emitted through.</param>
    /// <param name="resolved">The routing outcome.</param>
    /// <param name="idleTimeout">How long the upstream may produce nothing before the turn is abandoned.</param>
    /// <param name="logger">The diagnostic log.</param>
    /// <returns>The translator.</returns>
    IStreamTranslator Create(CodexSseWriter writer, ResolvedModel resolved, TimeSpan idleTimeout, ILogger logger);
}
