// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>The <c>event:</c> names of the Anthropic streaming protocol.</summary>
public static class StreamEventNames
{
    /// <summary>Opens the message and carries its identifier and prompt usage.</summary>
    internal const string MessageStart = "message_start";

    /// <summary>Opens a content block at a given index.</summary>
    internal const string ContentBlockStart = "content_block_start";

    /// <summary>Appends to the open content block at a given index.</summary>
    internal const string ContentBlockDelta = "content_block_delta";

    /// <summary>Closes the content block at a given index.</summary>
    internal const string ContentBlockStop = "content_block_stop";

    /// <summary>Carries the stop reason and final output usage.</summary>
    internal const string MessageDelta = "message_delta";

    /// <summary>Ends the message.</summary>
    internal const string MessageStop = "message_stop";

    /// <summary>Reports a transport or upstream failure.</summary>
    internal const string Error = "error";

    /// <summary>Keeps an idle connection open.</summary>
    internal const string Ping = "ping";
}
