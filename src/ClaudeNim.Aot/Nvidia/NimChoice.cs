// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One choice of a non-streamed NIM completion.</summary>
/// <param name="Index">The choice slot.</param>
/// <param name="Message">The completed message.</param>
/// <param name="FinishReason">Why generation ended.</param>
[System.Diagnostics.DebuggerDisplay("NimChoice: {ToString(),nq}")]
public sealed record NimChoice(
    [property: JsonPropertyName("index")] int Index = 0,
    [property: JsonPropertyName("message")] NimChatMessage? Message = null,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null);
