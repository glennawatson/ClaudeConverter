// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>What <see cref="CodexCompletionTranslator"/> and the streamed turn translator need beyond the completion itself.</summary>
/// <param name="InputTokens">The prompt size to report when the upstream withheld usage.</param>
/// <param name="ThinkingEnabled">Whether reasoning should be forwarded to the client.</param>
/// <param name="Time">The clock the turn's timestamp is read from.</param>
/// <param name="Logger">The diagnostic log.</param>
/// <remarks>
/// Bundled into one type because <c>Translate</c> already takes the completion, its identifiers,
/// and the model names on top of these four — past which a positional parameter list stops
/// describing the call and starts obscuring it.
/// </remarks>
public sealed record CodexCompletionContext(int InputTokens, bool ThinkingEnabled, TimeProvider Time, ILogger Logger);
