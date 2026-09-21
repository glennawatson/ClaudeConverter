// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Optimizations;

/// <summary>Answers Claude Code's housekeeping requests without calling the upstream.</summary>
/// <remarks>
/// <para>
/// A coding session sends more than the turns the user typed. It also probes the credential,
/// asks for a conversation title, asks for typeahead suggestions, and asks the model to pick
/// apart a shell command or a tool result. Each of those has a known, content-free answer, and
/// each otherwise costs a network round trip and a billed completion.
/// </para>
/// <para>
/// Every fast path is individually switchable, and a request that is not recognised is simply
/// forwarded — the failure mode of a missed match is an ordinary upstream call, not an error.
/// </para>
/// </remarks>
public static class RequestOptimizer
{
    /// <summary>The answer returned for a quota probe.</summary>
    private const string QuotaAnswer = "ok";

    /// <summary>The answer returned when title generation is skipped.</summary>
    private const string TitleAnswer = "Conversation";

    /// <summary>The answer returned when file path extraction is skipped.</summary>
    /// <remarks>Claude Code reads this as "the tool result mentioned no files", which is inert.</remarks>
    private const string FilePathAnswer = "<filepaths></filepaths>";

    /// <summary>The answer returned when command prefix detection is skipped.</summary>
    /// <remarks>
    /// Claude Code matches this string against the user's permission rules. Returning a guessed
    /// prefix could widen a match the user never granted, so the answer is deliberately one that
    /// matches no rule: the command is then judged on its own, which errs towards asking.
    /// </remarks>
    private const string CommandPrefixAnswer = "none";

    /// <summary>Tries to answer a request locally.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="options">The switches for each fast path.</param>
    /// <param name="answer">The text to answer with, when one applies.</param>
    /// <returns><see langword="true"/> when the request was recognised and should not be forwarded.</returns>
    public static bool TryAnswer(MessagesRequest request, OptimizationOptions options, out string answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MockQuotaProbe && IsQuotaProbe(request))
        {
            answer = QuotaAnswer;
            return true;
        }

        var prompt = Prompt(request);
        answer = Match(prompt, options);
        return answer.Length > 0 || IsSuppressed(prompt, options);
    }

    /// <summary>Matches a prompt against the fast paths that produce an answer.</summary>
    /// <param name="prompt">The lower-cased prompt text.</param>
    /// <param name="options">The switches for each fast path.</param>
    /// <returns>The answer, or an empty string when nothing matched.</returns>
    private static string Match(string prompt, OptimizationOptions options)
    {
        if (options.SkipTitleGeneration && Contains(prompt, OptimizationMarkers.TitleGeneration))
        {
            return TitleAnswer;
        }

        if (options.DetectCommandPrefix && Contains(prompt, OptimizationMarkers.CommandPrefix))
        {
            return CommandPrefixAnswer;
        }

        return options.MockFilePathExtraction && Contains(prompt, OptimizationMarkers.FilePathExtraction)
            ? FilePathAnswer
            : string.Empty;
    }

    /// <summary>Determines whether a prompt is one that is answered with nothing at all.</summary>
    /// <param name="prompt">The lower-cased prompt text.</param>
    /// <param name="options">The switches for each fast path.</param>
    /// <returns><see langword="true"/> when the request should be answered with empty content.</returns>
    private static bool IsSuppressed(string prompt, OptimizationOptions options) =>
        options.SkipSuggestionMode && Contains(prompt, OptimizationMarkers.SuggestionMode);

    /// <summary>Determines whether a request is the probe that checks the credential's quota.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> when the request is a quota probe.</returns>
    /// <remarks>
    /// The probe is recognised by its shape as well as its text: a one-token ceiling with a single
    /// one-word message is not something a real turn produces.
    /// </remarks>
    private static bool IsQuotaProbe(MessagesRequest request)
    {
        if (request.MaxTokens > OptimizationMarkers.QuotaProbeMaxTokens || request.Messages.Count != 1)
        {
            return false;
        }

        var text = ContentText.Extract(request.Messages[0].Content).Trim();
        return string.Equals(text, OptimizationMarkers.QuotaProbe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Flattens the parts of a request the markers are matched against.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The prompt text, lower-cased for matching.</returns>
    /// <remarks>
    /// Claude Code carries these instructions in the system prompt, but has moved individual ones
    /// into the first user message across releases, so both are searched.
    /// </remarks>
    private static string Prompt(MessagesRequest request)
    {
        var system = ContentText.Extract(request.System);
        var first = request.Messages.Count > 0
            ? ContentText.Extract(request.Messages[0].Content)
            : string.Empty;

        return $"{system}\n{first}".ToLowerInvariant();
    }

    /// <summary>Determines whether a prompt carries a marker.</summary>
    /// <param name="prompt">The lower-cased prompt text.</param>
    /// <param name="marker">The marker to look for, which is already lower-cased.</param>
    /// <returns><see langword="true"/> when the marker is present.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Contains(string prompt, string marker) =>
        prompt.Contains(marker, StringComparison.Ordinal);
}
