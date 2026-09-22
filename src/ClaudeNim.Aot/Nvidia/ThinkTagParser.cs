// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Separates inline <c>&lt;think&gt;</c> reasoning from answer text in a streamed response.</summary>
/// <remarks>
/// <para>
/// NVIDIA's Nemotron models return reasoning in a dedicated <c>reasoning_content</c> delta, but
/// the GLM and DeepSeek families hosted on the same endpoint instead wrap it in
/// <c>&lt;think&gt;</c> tags inside ordinary <c>content</c>. Without this split the tags reach the
/// client as literal answer text.
/// </para>
/// <para>
/// The reasoning is re-emitted as Anthropic <c>thinking</c> blocks rather than discarded, so a
/// client that renders reasoning still gets it.
/// </para>
/// <para>
/// A tag can straddle a chunk boundary, so any trailing run that could still grow into a tag is
/// held back until the next chunk resolves it.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ThinkTagParser: {_buffer}")]
public sealed record ThinkTagParser
{
    /// <summary>The opening <c>&lt;think&gt;</c> tag.</summary>
    private const string OpenTag = "<think>";

    /// <summary>The closing <c>&lt;/think&gt;</c> tag.</summary>
    private const string CloseTag = "</think>";

    /// <summary>How much answer text may precede an opening tag before it is taken literally.</summary>
    /// <remarks>
    /// Reasoning wrapped in tags comes first, before the answer. A tag appearing well into the
    /// answer is the model writing about tags — asked to explain <c>&lt;think&gt;</c>, it would
    /// otherwise have the rest of its reply eaten as a reasoning trace that never closes.
    /// </remarks>
    private const int LiteralTagThreshold = 200;

    /// <summary>The buffer holding partially parsed content between chunk boundaries.</summary>
    private readonly StringBuilder _buffer = new();

    /// <summary>Tracks whether the parser is currently inside a <c>&lt;think&gt;</c> block.</summary>
    private bool _insideThink;

    /// <summary>The non-whitespace answer characters emitted so far.</summary>
    private int _answerLength;

    /// <summary>Tracks whether a real open or close tag has been consumed yet.</summary>
    /// <remarks>
    /// Some chat templates seed the assistant turn with an opening <c>&lt;think&gt;</c> themselves,
    /// so the model's own completion text never contains one — only the closing tag it writes once
    /// reasoning ends. Without tracking this, that stray <c>&lt;/think&gt;</c> is never recognised
    /// (the parser is only ever looking for an opening tag while <c>_insideThink</c> is false), and
    /// both the reasoning prose and the literal tag leak into the answer as plain text.
    /// </remarks>
    private bool _sawAnyTag;

    /// <summary>Consumes a chunk of model output and appends any completed runs.</summary>
    /// <param name="chunk">The incoming content fragment.</param>
    /// <param name="segments">The list completed runs are appended to.</param>
    public void Feed(string chunk, List<ThinkTagSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (!string.IsNullOrEmpty(chunk))
        {
            _ = _buffer.Append(chunk);
        }

        var resolvable = true;
        while (resolvable && _buffer.Length > 0)
        {
            resolvable = ConsumeOnce(segments);
        }
    }

    /// <summary>Releases whatever text is still held once the stream has ended.</summary>
    /// <param name="segments">The list the trailing run is appended to.</param>
    public void Flush(List<ThinkTagSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (_buffer.Length <= 0)
        {
            return;
        }

        Emit(segments, _buffer.ToString());
        _ = _buffer.Clear();
    }

    // The longest prefix that cannot be the opening of the tag being searched for. Anything
    // after it is a partial tag that the next chunk may complete.
    /// <summary>Computes the longest safe prefix that cannot be the opening of the search tag.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="tag">The tag being searched for.</param>
    /// <returns>The length of the safe prefix.</returns>
    private static int SafeLength(string text, string tag)
    {
        var earliest = text.Length;
        var limit = tag.Length - 1;

        for (var length = 1; length <= limit && length <= text.Length; length++)
        {
            var start = text.Length - length;
            if (string.CompareOrdinal(text, start, tag, 0, length) == 0)
            {
                earliest = start;
            }
        }

        return earliest;
    }

    /// <summary>Counts the characters of a run that are not whitespace.</summary>
    /// <param name="text">The run to count.</param>
    /// <returns>The number of non-whitespace characters.</returns>
    private static int CountVisible(string text)
    {
        var visible = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            visible++;
        }

        return visible;
    }

    /// <summary>Appends a text segment to the list if non-empty.</summary>
    /// <param name="segments">The segments list to append to.</param>
    /// <param name="text">The text to add.</param>
    private void Emit(List<ThinkTagSegment> segments, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (!_insideThink)
        {
            _answerLength += CountVisible(text);
        }

        segments.Add(new(_insideThink, text));
    }

    /// <summary>Resolves as much of the buffer as one call can, without looping.</summary>
    /// <param name="segments">The list completed runs are appended to.</param>
    /// <returns><see langword="true"/> when the buffer may still hold another resolvable run.</returns>
    private bool ConsumeOnce(List<ThinkTagSegment> segments)
    {
        var text = _buffer.ToString();

        if (TryFlushPastThreshold(text, 0, segments))
        {
            return false;
        }

        if (!_insideThink && !_sawAnyTag && TryHandleImplicitOpen(text, segments))
        {
            return true;
        }

        var tag = _insideThink ? CloseTag : OpenTag;
        var index = text.IndexOf(tag, StringComparison.Ordinal);

        if (index >= 0)
        {
            // The prefix before this tag has not been counted yet, so an opening tag reached only
            // by way of a long prefix in the same chunk must be checked here too, not just against
            // the total left over from earlier chunks.
            if (TryFlushPastThreshold(text, CountVisible(text[..index]), segments))
            {
                return false;
            }

            Emit(segments, text[..index]);
            _ = _buffer.Remove(0, index + tag.Length);
            _insideThink = !_insideThink;
            _sawAnyTag = true;
            return true;
        }

        var safe = SafeLength(text, tag);
        if (safe > 0)
        {
            Emit(segments, text[..safe]);
            _ = _buffer.Remove(0, safe);
        }

        return false;
    }

    /// <summary>Flushes the whole buffer as answer text once the literal-tag threshold is passed.</summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="pendingVisible">Visible characters not yet counted in <see cref="_answerLength"/>.</param>
    /// <param name="segments">The list completed runs are appended to.</param>
    /// <returns><see langword="true"/> when the buffer was flushed.</returns>
    private bool TryFlushPastThreshold(string text, int pendingVisible, List<ThinkTagSegment> segments)
    {
        if (_insideThink || _answerLength + pendingVisible < LiteralTagThreshold)
        {
            return false;
        }

        Emit(segments, text);
        _ = _buffer.Clear();
        return true;
    }

    /// <summary>Handles a closing tag reached before any opening tag has ever been seen.</summary>
    /// <param name="text">The buffered text to scan.</param>
    /// <param name="segments">The list completed runs are appended to.</param>
    /// <returns><see langword="true"/> when an implicit open was found and consumed.</returns>
    private bool TryHandleImplicitOpen(string text, List<ThinkTagSegment> segments)
    {
        var openIndex = text.IndexOf(OpenTag, StringComparison.Ordinal);
        var closeIndex = text.IndexOf(CloseTag, StringComparison.Ordinal);

        if (closeIndex < 0 || (openIndex >= 0 && openIndex <= closeIndex))
        {
            return false;
        }

        _insideThink = true;
        Emit(segments, text[..closeIndex]);
        _insideThink = false;
        _sawAnyTag = true;
        _ = _buffer.Remove(0, closeIndex + CloseTag.Length);
        return true;
    }
}
