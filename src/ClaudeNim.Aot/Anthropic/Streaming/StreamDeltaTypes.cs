// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The <c>delta.type</c> discriminators of a <c>content_block_delta</c> event.</summary>
public static class StreamDeltaTypes
{
    /// <summary>Appends to a text block.</summary>
    internal const string Text = "text_delta";

    /// <summary>Appends to a thinking block.</summary>
    internal const string Thinking = "thinking_delta";

    /// <summary>Appends a fragment of a tool call's JSON arguments.</summary>
    internal const string InputJson = "input_json_delta";
}
