// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The stop information carried by a <c>message_delta</c> event.</summary>
/// <param name="StopReason">Why generation ended; see <see cref="StopReasons"/>.</param>
/// <param name="StopSequence">The stop sequence that ended generation, when one did.</param>
/// <param name="StopDetails">Why a declined turn was declined; absent for every stop reason but <c>refusal</c>.</param>
[System.Diagnostics.DebuggerDisplay("StreamStopDetail: {ToString(),nq}")]
public sealed record StreamStopDetail(
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("stop_sequence")] string? StopSequence = null,
    [property: JsonPropertyName("stop_details")] StopDetail? StopDetails = null);
