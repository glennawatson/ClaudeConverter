// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>How a streamed attempt that failed before producing anything was recovered.</summary>
/// <param name="Turn">The turn to continue with, which may name a downgraded body or a different model.</param>
/// <param name="Response">The upstream response, which the caller owns and must dispose.</param>
/// <param name="ShouldContinue">Whether the response succeeded and another attempt should be read.</param>
internal readonly record struct CodexStreamRecoveryOutcome(CodexTurnContext Turn, HttpResponseMessage Response, bool ShouldContinue);
