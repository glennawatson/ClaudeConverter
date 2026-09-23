// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Tracks one tool call as it is assembled from streamed fragments, for the Responses API.</summary>
/// <remarks>
/// A class rather than a record because every member changes as the stream advances, matching
/// <see cref="Anthropic.Streaming.ToolBlockState"/> for the same reason.
/// </remarks>
internal sealed class CodexToolCallState
{
    /// <summary>Gets or sets the turn's output index for this call, or -1 before it is opened.</summary>
    public int OutputIndex { get; set; } = -1;

    /// <summary>Gets or sets the output item's own identifier, distinct from <see cref="Id"/>.</summary>
    public string? ItemId { get; set; }

    /// <summary>Gets or sets the tool call identifier a matching <c>function_call_output</c> answers.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the tool name assembled so far.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the item has been opened.</summary>
    public bool Started { get; set; }

    /// <summary>Gets the JSON-encoded arguments assembled so far.</summary>
    public StringBuilder Arguments { get; } = new();

    /// <summary>Gets or sets argument fragments received before the item could be opened.</summary>
    public string PendingArguments { get; set; } = string.Empty;

    /// <summary>Merges an incoming tool-name fragment into <see cref="Name"/>.</summary>
    /// <param name="fragment">The incoming name fragment.</param>
    /// <remarks>
    /// Names arrive split across chunks, and some upstreams repeat the prefix already sent rather
    /// than continuing from it. Extending only when the incoming value is not already a prefix of
    /// what is held avoids doubling a repeated name.
    /// </remarks>
    internal void MergeName(string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
        {
            return;
        }

        if (Name.Length == 0 || fragment.StartsWith(Name, StringComparison.Ordinal))
        {
            Name = fragment;
            return;
        }

        if (!Name.StartsWith(fragment, StringComparison.Ordinal))
        {
            Name += fragment;
        }
    }
}
