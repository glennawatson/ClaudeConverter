// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Codex.Streaming;

/// <summary>The <c>type</c> discriminator <see cref="ResponseStreamEvent"/> carries.</summary>
public static class ResponseStreamEventTypes
{
    /// <summary>The turn was created and streaming has begun.</summary>
    internal const string Created = "response.created";

    /// <summary>The turn is being produced.</summary>
    internal const string InProgress = "response.in_progress";

    /// <summary>A new output item was opened.</summary>
    internal const string OutputItemAdded = "response.output_item.added";

    /// <summary>An output item finished, carrying its final content.</summary>
    internal const string OutputItemDone = "response.output_item.done";

    /// <summary>A new content part was opened within an output item.</summary>
    internal const string ContentPartAdded = "response.content_part.added";

    /// <summary>A content part finished, carrying its final text.</summary>
    internal const string ContentPartDone = "response.content_part.done";

    /// <summary>A fragment of answer text.</summary>
    internal const string OutputTextDelta = "response.output_text.delta";

    /// <summary>An answer text part finished, carrying its final text.</summary>
    internal const string OutputTextDone = "response.output_text.done";

    /// <summary>A fragment of a tool call's JSON-encoded arguments.</summary>
    internal const string FunctionCallArgumentsDelta = "response.function_call_arguments.delta";

    /// <summary>A tool call's arguments finished, carrying the final JSON text.</summary>
    internal const string FunctionCallArgumentsDone = "response.function_call_arguments.done";

    /// <summary>A new reasoning summary part was opened.</summary>
    internal const string ReasoningSummaryPartAdded = "response.reasoning_summary_part.added";

    /// <summary>A reasoning summary part finished.</summary>
    internal const string ReasoningSummaryPartDone = "response.reasoning_summary_part.done";

    /// <summary>A fragment of a reasoning summary.</summary>
    internal const string ReasoningSummaryTextDelta = "response.reasoning_summary_text.delta";

    /// <summary>A reasoning summary finished, carrying its final text.</summary>
    internal const string ReasoningSummaryTextDone = "response.reasoning_summary_text.done";

    /// <summary>A fragment of the reasoning trace itself, when the model discloses more than a summary.</summary>
    internal const string ReasoningTextDelta = "response.reasoning_text.delta";

    /// <summary>The turn finished normally.</summary>
    internal const string Completed = "response.completed";

    /// <summary>The turn failed.</summary>
    internal const string Failed = "response.failed";

    /// <summary>The turn stopped before it finished.</summary>
    internal const string Incomplete = "response.incomplete";

    /// <summary>A failure reported outside the lifecycle of any one turn.</summary>
    internal const string Error = "error";
}
