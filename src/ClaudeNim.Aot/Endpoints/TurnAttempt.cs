// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Endpoints;

/// <summary>A turn that reached a model, and the response that model gave it.</summary>
/// <param name="Turn">The turn as the model that answered it left it, which may name a fallback.</param>
/// <param name="Response">The upstream response, which the caller owns and must dispose.</param>
/// <remarks>
/// The turn travels back out with the response because walking a fallback chain changes it: the
/// model that answered, the body it was sent and what is left of the chain are all different from
/// what routing decided, and every later step — the log, the translator, a streamed re-issue —
/// needs the version that actually happened.
/// </remarks>
internal readonly record struct TurnAttempt(TurnContext Turn, HttpResponseMessage Response);
