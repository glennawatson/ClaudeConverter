// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic;

/// <summary>The <c>type</c> discriminators Anthropic uses for content blocks.</summary>
public static class ContentBlockTypes
{
    /// <summary>Plain assistant or user text.</summary>
    internal const string Text = "text";

    /// <summary>Model reasoning surfaced to the client.</summary>
    internal const string Thinking = "thinking";

    /// <summary>Reasoning the provider returned in opaque form.</summary>
    internal const string RedactedThinking = "redacted_thinking";

    /// <summary>A tool invocation produced by the model.</summary>
    internal const string ToolUse = "tool_use";

    /// <summary>The caller's answer to a <see cref="ToolUse"/> block.</summary>
    internal const string ToolResult = "tool_result";

    /// <summary>An image supplied by the caller.</summary>
    internal const string Image = "image";
}
