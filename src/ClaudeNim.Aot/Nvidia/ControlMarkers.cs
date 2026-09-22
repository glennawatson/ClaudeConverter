// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>The chat-template control markers that carry nothing an Anthropic client can use.</summary>
/// <remarks>
/// <para>
/// A chat template is a prompt format, not a wire format. It writes turn delimiters and mode
/// markers into the text it renders, and the model — having been trained on that text — sometimes
/// writes one back. Anthropic's protocol has no place to put them, so a proxy that forwards
/// content verbatim shows the client a stray <c>{methodical_execution}</c> or
/// <c>&lt;|im_end|&gt;</c> as though the model had said it. Translating means dropping them.
/// </para>
/// <para>
/// Every marker here is matched exactly, never by pattern. The effort markers are ordinary prose
/// inside braces, and a pattern broad enough to catch them would eat any brace-delimited text the
/// model wrote on purpose — which, for a coding client, is most of its output.
/// </para>
/// </remarks>
public static class ControlMarkers
{
    // The effort markers the Nemotron templates append to the last user turn. The proxy asks for
    // reduced effort through low_effort and medium_effort, so it is the cause of these appearing
    // in the transcript at all, and the model echoing one back is a leak of the proxy's own doing.
    /// <summary>The reasoning-effort markers the Nemotron chat templates write into the prompt.</summary>
    private static readonly string[] EffortMarkers =
    [
        "{reasoning effort: low}",
        "{reasoning effort: efficient}",
        "{methodical_execution}",
    ];

    // Turn delimiters. These normally stop generation rather than appear in it, but a model that
    // runs past its own stop token writes one into the answer.
    /// <summary>The turn delimiters the chat templates use to frame a message.</summary>
    private static readonly string[] TurnMarkers =
    [
        "<|im_start|>",
        "<|im_end|>",
        "<｜begin▁of▁sentence｜>",
        "<｜end▁of▁sentence｜>",
        "<｜User｜>",
        "<｜Assistant｜>",
        "<｜System｜>",
        "<｜latest_reminder｜>",
    ];

    /// <summary>The markers that bracket a run of tool calls without carrying anything themselves.</summary>
    private static readonly string[] ToolCallBrackets =
    [
        "<｜tool▁calls▁begin｜>",
        "<｜tool▁calls▁end｜>",
        "<|tool_calls_begin|>",
        "<|tool_calls_end|>",
        "<｜DSML｜>",
        "<|DSML|>",
    ];

    /// <summary>Removes every control marker from a run of answer text.</summary>
    /// <param name="text">The text to clean.</param>
    /// <returns>The text with the markers removed.</returns>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var cleaned = text;
        cleaned = Remove(cleaned, EffortMarkers);
        cleaned = Remove(cleaned, TurnMarkers);
        cleaned = Remove(cleaned, ToolCallBrackets);

        return cleaned;
    }

    /// <summary>Computes the longest prefix of a run that cannot be the start of any marker.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The length of the safe prefix.</returns>
    /// <remarks>
    /// A marker can straddle a chunk boundary. Releasing a prefix that could still grow into one
    /// would let the marker through in two halves, which is exactly what stripping is meant to
    /// prevent.
    /// </remarks>
    public static int SafeLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var safe = text.Length;
        safe = Math.Min(safe, SafeLength(text, EffortMarkers));
        safe = Math.Min(safe, SafeLength(text, TurnMarkers));
        safe = Math.Min(safe, SafeLength(text, ToolCallBrackets));

        return safe;
    }

    /// <summary>Removes every marker of one set from a run of text.</summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="markers">The markers to remove.</param>
    /// <returns>The cleaned text.</returns>
    private static string Remove(string text, string[] markers)
    {
        var cleaned = text;
        for (var i = 0; i < markers.Length; i++)
        {
            cleaned = cleaned.Replace(markers[i], string.Empty, StringComparison.Ordinal);
        }

        return cleaned;
    }

    /// <summary>Computes the longest prefix that cannot be the opening of any marker in one set.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="markers">The markers to guard against.</param>
    /// <returns>The length of the safe prefix.</returns>
    private static int SafeLength(string text, string[] markers)
    {
        var safe = text.Length;
        for (var i = 0; i < markers.Length; i++)
        {
            safe = Math.Min(safe, SafeLength(text, markers[i]));
        }

        return safe;
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
}
