// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>One message in a NIM chat completions transcript.</summary>
/// <param name="Role">The author; <c>system</c>, <c>user</c>, <c>assistant</c> or <c>tool</c>.</param>
/// <param name="Content">The message body, as text or as multimodal parts.</param>
/// <param name="ToolCalls">Tool calls issued by an assistant message.</param>
/// <param name="ToolCallId">The call a <c>tool</c> message answers.</param>
/// <param name="ReasoningContent">Reasoning replayed on an assistant message.</param>
[System.Diagnostics.DebuggerDisplay("NimChatMessage: {ToString(),nq}")]
public sealed record NimChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] NimContent? Content = null,
    [property: JsonPropertyName("tool_calls")] List<NimToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null)
{
    /// <summary>The <c>system</c> role.</summary>
    internal const string SystemRole = "system";

    /// <summary>The <c>user</c> role.</summary>
    internal const string UserRole = "user";

    /// <summary>The <c>assistant</c> role.</summary>
    internal const string AssistantRole = "assistant";

    /// <summary>The <c>tool</c> role.</summary>
    internal const string ToolRole = "tool";
}
