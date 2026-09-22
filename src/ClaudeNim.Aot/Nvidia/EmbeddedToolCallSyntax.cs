// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>The marker tokens models use to write a tool call into their answer text.</summary>
/// <remarks>
/// <para>
/// Two spellings are in circulation. The plain one uses ASCII bars; the DeepSeek one uses fullwidth
/// bars and a dotted separator. They look nearly identical in a terminal and share not one
/// character, so both are matched exactly rather than by a pattern that would blur them.
/// </para>
/// <para>
/// This type knows the shape of the tokens and nothing about streaming;
/// <see cref="EmbeddedToolCallParser"/> owns the buffering that spans chunk boundaries.
/// </para>
/// </remarks>
public static class EmbeddedToolCallSyntax
{
    /// <summary>The token that opens a call in the plain spelling.</summary>
    private const string CallOpen = "<|tool_call_begin|>";

    /// <summary>The token that separates the name from the arguments in the plain spelling.</summary>
    private const string CallArguments = "<|tool_call_argument_begin|>";

    /// <summary>The token that closes a call in the plain spelling.</summary>
    private const string CallClose = "<|tool_call_end|>";

    /// <summary>The token that opens a call in the DeepSeek spelling.</summary>
    private const string DeepSeekOpen = "<｜tool▁call▁begin｜>";

    /// <summary>The token that separates the name from the arguments in the DeepSeek spelling.</summary>
    private const string DeepSeekSeparator = "<｜tool▁sep｜>";

    /// <summary>The token that closes a call in the DeepSeek spelling.</summary>
    private const string DeepSeekClose = "<｜tool▁call▁end｜>";

    /// <summary>The fence some templates wrap the arguments in.</summary>
    private const string Fence = "```";

    /// <summary>The opening tokens, each paired with the separator and terminator that follow it.</summary>
    private static readonly (string Open, string Separator, string Close)[] Forms =
    [
        (CallOpen, CallArguments, CallClose),
        (DeepSeekOpen, DeepSeekSeparator, DeepSeekClose),
    ];

    /// <summary>Finds the earliest call opening in a run of text.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The opening's position and the tokens that go with it, with an index of -1 when there is none.</returns>
    public static (int Index, string Open, string Separator, string Close) FindOpening(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var best = (Index: -1, Open: string.Empty, Separator: string.Empty, Close: string.Empty);

        foreach (var (open, separator, close) in Forms)
        {
            var index = text.IndexOf(open, StringComparison.Ordinal);
            if (index >= 0 && (best.Index < 0 || index < best.Index))
            {
                best = (index, open, separator, close);
            }
        }

        return best;
    }

    /// <summary>Computes the longest prefix of a run that cannot be the start of any marker.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The length of the safe prefix.</returns>
    public static int SafeLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var safe = ControlMarkers.SafeLength(text);

        foreach (var (open, _, _) in Forms)
        {
            safe = Math.Min(safe, SafeLength(text, open));
        }

        return safe;
    }

    /// <summary>Splits a call body into the tool name and its arguments.</summary>
    /// <param name="body">The text between the opening and closing tokens.</param>
    /// <param name="separator">The token that divides the name from the arguments.</param>
    /// <returns>The call, or <see langword="null"/> when no name could be read.</returns>
    public static EmbeddedToolCall? ReadCall(string body, string separator)
    {
        ArgumentNullException.ThrowIfNull(body);

        var split = body.IndexOf(separator, StringComparison.Ordinal);
        var name = (split < 0 ? body : body[..split]).Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var arguments = split < 0 ? string.Empty : body[(split + separator.Length)..];
        return EmbeddedToolCall.ForCall(name, Unfence(arguments));
    }

    /// <summary>Computes the longest prefix that cannot be the opening of one marker.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="marker">The marker being guarded against.</param>
    /// <returns>The length of the safe prefix.</returns>
    private static int SafeLength(string text, string marker)
    {
        var earliest = text.Length;
        var limit = marker.Length - 1;

        for (var length = 1; length <= limit && length <= text.Length; length++)
        {
            var start = text.Length - length;
            if (string.CompareOrdinal(text, start, marker, 0, length) == 0)
            {
                earliest = start;
            }
        }

        return earliest;
    }

    /// <summary>Removes the code fence some templates wrap the arguments in.</summary>
    /// <param name="arguments">The argument text as it was written.</param>
    /// <returns>The arguments, unfenced and trimmed.</returns>
    private static string Unfence(string arguments)
    {
        var text = arguments.Trim();

        if (!text.StartsWith(Fence, StringComparison.Ordinal))
        {
            return text;
        }

        var firstBreak = text.IndexOf('\n');
        if (firstBreak < 0)
        {
            return text;
        }

        var body = text[(firstBreak + 1)..];
        var closing = body.LastIndexOf(Fence, StringComparison.Ordinal);

        return (closing < 0 ? body : body[..closing]).Trim();
    }
}
