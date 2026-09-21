// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Which server-side context management strategies a listed model offers.</summary>
/// <param name="Supported">Whether the model offers any strategy.</param>
/// <param name="ClearThinking">Whether the <c>clear_thinking_20251015</c> strategy is offered.</param>
/// <param name="ClearToolUses">Whether the <c>clear_tool_uses_20250919</c> strategy is offered.</param>
/// <param name="Compact">Whether the <c>compact_20260112</c> strategy is offered.</param>
/// <remarks>
/// These strategies are performed by Anthropic's own service, not by the model, so this proxy
/// reports none of them. A client that reads an honest <see langword="false"/> here manages its
/// own context instead of asking the upstream to; a client told <see langword="true"/> would let
/// a conversation grow past the window in the belief that something was trimming it.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ContextManagementCapability: {Supported}")]
public readonly record struct ContextManagementCapability(
    [property: JsonPropertyName("supported")] bool Supported,
    [property: JsonPropertyName("clear_thinking_20251015")] CapabilitySupport? ClearThinking,
    [property: JsonPropertyName("clear_tool_uses_20250919")] CapabilitySupport? ClearToolUses,
    [property: JsonPropertyName("compact_20260112")] CapabilitySupport? Compact)
{
    /// <summary>Gets the capability stating that no strategy is offered.</summary>
    public static ContextManagementCapability None =>
        new(false, CapabilitySupport.No, CapabilitySupport.No, CapabilitySupport.No);
}
