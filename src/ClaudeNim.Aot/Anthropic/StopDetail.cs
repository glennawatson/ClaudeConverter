// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Why a turn was declined, alongside the <c>refusal</c> stop reason that carries it.</summary>
/// <param name="Category">The policy area the refusal falls under, or <see langword="null"/> when the upstream named none.</param>
/// <param name="Explanation">What the upstream said about the refusal, when it said anything.</param>
/// <param name="Type">The object discriminator, always <c>refusal</c>.</param>
/// <remarks>
/// <para>
/// Anthropic populates this only when <c>stop_reason</c> is <c>refusal</c>, and leaves it null for
/// every other reason, so a client reads it behind that check. The category is an open set — the
/// documented values include <c>cyber</c>, <c>bio</c> and <c>reasoning_extraction</c> — and NIM
/// names none of them, so the field is sent null rather than guessed at.
/// </para>
/// <para>
/// This exists because a filtered turn is otherwise the most misleading answer this proxy can
/// give. NVIDIA reports one as a finish reason with no content behind it, which translated to
/// <c>end_turn</c> with an empty message: the client reads that as the model having nothing to
/// say and stops, where the truth is that the turn was declined and something else should be
/// tried.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("StopDetail: {ToString(),nq}")]
public sealed record StopDetail(
    [property: JsonPropertyName("category")] string? Category = null,
    [property: JsonPropertyName("explanation")] string? Explanation = null,
    [property: JsonPropertyName("type")] string Type = StopReasons.Refusal)
{
    /// <summary>Gets the detail reported for a turn NVIDIA's own filters declined.</summary>
    public static StopDetail Filtered { get; } = new(
        Explanation: "The upstream model's content filter declined this turn.");
}
