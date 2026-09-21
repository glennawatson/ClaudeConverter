// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>Tracks one tool call as it is assembled from streamed fragments.</summary>
/// <remarks>
/// <para>
/// An upstream splits a tool call across chunks and may send argument fragments before it has
/// sent the tool's name. A block cannot be opened without the name, so early fragments are held
/// in <see cref="PendingArguments"/> and replayed once the name arrives.
/// </para>
/// <para>
/// This is a class rather than a record because every member of it changes as the stream
/// advances. A record carries value equality, which is meaningless for a state machine and
/// actively wrong if one were ever used as a dictionary key while still being mutated.
/// </para>
/// </remarks>
internal sealed class ToolBlockState
{
    /// <summary>Gets or sets the Anthropic content block index, or -1 before the block is opened.</summary>
    public int BlockIndex { get; set; } = -1;

    /// <summary>Gets or sets the tool call identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the tool name assembled so far.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the content block has been opened.</summary>
    public bool Started { get; set; }

    /// <summary>Gets or sets argument fragments received before the block could be opened.</summary>
    public string PendingArguments { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of argument characters emitted, used to size output tokens.</summary>
    public int EmittedArgumentLength { get; set; }

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
