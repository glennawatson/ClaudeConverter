// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One choice within a streamed NIM chunk.</summary>
/// <param name="Index">The choice slot.</param>
/// <param name="Delta">The incremental payload.</param>
/// <param name="FinishReason">Why generation ended, on the final chunk for the choice.</param>
[System.Diagnostics.DebuggerDisplay("NimStreamChoice: {ToString(),nq}")]
public sealed record NimStreamChoice(
    [property: JsonPropertyName("index")] int Index = 0,
    [property: JsonPropertyName("delta")] NimDelta? Delta = null,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null);
