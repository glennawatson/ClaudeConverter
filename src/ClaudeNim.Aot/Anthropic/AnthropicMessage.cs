// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>One turn of an Anthropic conversation.</summary>
/// <param name="Role">The author of the turn; either <c>user</c> or <c>assistant</c>.</param>
/// <param name="Content">The turn's content.</param>
[System.Diagnostics.DebuggerDisplay("AnthropicMessage: {ToString(),nq}")]
public sealed record AnthropicMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] MessageContent Content)
{
    /// <summary>The <c>user</c> role.</summary>
    internal const string UserRole = "user";

    /// <summary>The <c>assistant</c> role.</summary>
    internal const string AssistantRole = "assistant";
}
