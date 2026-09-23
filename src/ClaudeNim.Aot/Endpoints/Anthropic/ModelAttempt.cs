// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>What one model made of a turn it was offered.</summary>
/// <param name="Response">The response worth keeping, or <see langword="null"/> when the model was unavailable.</param>
/// <param name="Status">The status an unavailable model returned, or zero when it never answered at all.</param>
/// <param name="InCooldown">Whether the model was skipped without an attempt because it is cooling down.</param>
/// <param name="Body">The upstream's own error body, when a transient status carried one worth logging.</param>
/// <remarks>
/// The status outlives the response it came from. An unavailable model's response is disposed
/// here, since nothing will be read from it but the body, but the status is what the log line
/// about moving on needs — and "never answered" has to be distinguishable from any status a model
/// could send.
/// </remarks>
internal readonly record struct ModelAttempt(HttpResponseMessage? Response, int Status, bool InCooldown = false, string? Body = null);
