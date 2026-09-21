// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The incremental payload of one streamed choice.</summary>
/// <param name="Role">The author, sent on the first chunk only.</param>
/// <param name="Content">A fragment of the answer.</param>
/// <param name="ReasoningContent">A fragment of the reasoning trace.</param>
/// <param name="ToolCalls">Fragments of the tool calls being assembled.</param>
[System.Diagnostics.DebuggerDisplay("NimDelta: {ToString(),nq}")]
public sealed record NimDelta(
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null,
    [property: JsonPropertyName("tool_calls")] List<NimToolCall>? ToolCalls = null);
