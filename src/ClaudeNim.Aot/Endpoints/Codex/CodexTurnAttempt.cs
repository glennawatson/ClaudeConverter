// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>A turn that reached a model, and the response that model gave it.</summary>
/// <param name="Turn">The turn as the model that answered it left it, which may name a fallback.</param>
/// <param name="Response">The upstream response, which the caller owns and must dispose.</param>
internal readonly record struct CodexTurnAttempt(CodexTurnContext Turn, HttpResponseMessage Response);
