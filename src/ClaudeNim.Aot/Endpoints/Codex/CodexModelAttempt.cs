// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Endpoints.Codex;

/// <summary>What one model made of a turn it was offered.</summary>
/// <param name="Response">The response worth keeping, or <see langword="null"/> when the model was unavailable.</param>
/// <param name="Status">The status an unavailable model returned, or zero when it never answered at all.</param>
/// <param name="InCooldown">Whether the model was skipped without an attempt because it is cooling down.</param>
internal readonly record struct CodexModelAttempt(HttpResponseMessage? Response, int Status, bool InCooldown = false);
