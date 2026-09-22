// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Optimizations;

/// <summary>Answers Claude Code's housekeeping requests without calling the upstream.</summary>
/// <remarks>
/// <para>
/// A coding session sends more than the turns the user typed. It also probes the credential, asks
/// for a conversation title, asks for typeahead suggestions, and asks which prefix a shell command
/// should be permission-matched on or which files it read. The last two have mechanical answers,
/// and the rest have content-free ones; each otherwise costs a round trip and a billed completion.
/// </para>
/// <para>
/// Every fast path is individually switchable, and a request that is not recognised is forwarded —
/// the failure mode of a missed match is an ordinary upstream call, not an error.
/// </para>
/// </remarks>
public static class RequestOptimizer
{
    /// <summary>The answer returned for a quota probe.</summary>
    private const string QuotaAnswer = "Quota check passed.";

    /// <summary>The answer returned when title generation is skipped.</summary>
    private const string TitleAnswer = "Conversation";

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

        var user = UserText(request);
        var system = ContentText.Extract(request.System);
        var housekeepingSized =
            user.Length + system.Length <= OptimizationMarkers.MaxHousekeepingContentLength;

        if (housekeepingSized && TryAnswerFromUserText(user, options, out answer))
        {
            return true;
        }

        if (housekeepingSized && options.SkipTitleGeneration && IsTitleRequest(system))
        {
            answer = TitleAnswer;
            return true;
        }

        answer = string.Empty;
        return false;
    }

    /// <summary>Tries to answer from the markers a request carries in its user turns.</summary>
    /// <param name="user">The flattened user text.</param>
    /// <param name="options">The switches for each fast path.</param>
    /// <param name="answer">The text to answer with, when one applies.</param>
    /// <returns><see langword="true"/> when the request was recognised.</returns>
    private static bool TryAnswerFromUserText(string user, OptimizationOptions options, out string answer)
    {
        if (options.SkipSuggestionMode && user.Contains(OptimizationMarkers.SuggestionMode, StringComparison.Ordinal))
        {
            answer = string.Empty;
            return true;
        }

        if (options.DetectCommandPrefix && IsPrefixRequest(user))
        {
            answer = CommandPrefix.Extract(Section(user, OptimizationMarkers.CommandLabel));
            return true;
        }

        if (options.MockFilePathExtraction && IsFilePathRequest(user))
        {
            answer = FilePathExtraction.Extract(Section(user, OptimizationMarkers.CommandLabel));
            return true;
        }

        answer = string.Empty;
        return false;
    }

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

    /// <summary>Determines whether a prompt asks which prefix a command should be matched on.</summary>
    /// <param name="user">The flattened user text.</param>
    /// <returns><see langword="true"/> when the prompt is a prefix request.</returns>
    /// <remarks>
    /// The caller has already checked the combined content is short — see
    /// <see cref="OptimizationMarkers.MaxHousekeepingContentLength"/> — so these two markers, which
    /// Auto Mode's own classifier turn can also carry, are only trusted on a request shaped like
    /// the standalone question this fast path was built to answer.
    /// </remarks>
    private static bool IsPrefixRequest(string user) =>
        user.Contains(OptimizationMarkers.PolicySpec, StringComparison.Ordinal)
        && user.Contains(OptimizationMarkers.CommandLabel, StringComparison.Ordinal);

    /// <summary>Determines whether a prompt asks which files a command read.</summary>
    /// <param name="user">The flattened user text.</param>
    /// <returns><see langword="true"/> when the prompt is a file-path request.</returns>
    /// <remarks>
    /// All three markers are required. The prompt carries the command, its output, and the element
    /// the answer goes in, and a run of text with only one of those is something else.
    /// </remarks>
    private static bool IsFilePathRequest(string user) =>
        user.Contains(OptimizationMarkers.CommandLabel, StringComparison.Ordinal)
        && user.Contains(OptimizationMarkers.OutputLabel, StringComparison.Ordinal)
        && user.Contains(OptimizationMarkers.FilePaths, StringComparison.OrdinalIgnoreCase);

    /// <summary>Determines whether a system prompt asks for a conversation title.</summary>
    /// <param name="system">The system prompt.</param>
    /// <returns><see langword="true"/> when the prompt is a title request.</returns>
    private static bool IsTitleRequest(string system) =>
        system.Contains(OptimizationMarkers.Title, StringComparison.OrdinalIgnoreCase)
        && OptimizationMarkers.IsTitleCorroborated(system);

    /// <summary>Reads the run of text a labelled section introduces.</summary>
    /// <param name="user">The flattened user text.</param>
    /// <param name="label">The label the section starts at.</param>
    /// <returns>The section's text, trimmed, or an empty string when the label is absent.</returns>
    /// <remarks>
    /// The section ends at whichever comes first: the next label, a blank line, or the start of an
    /// element. The prompt lays the command out on its own, so anything past that boundary belongs
    /// to a different part of the question.
    /// </remarks>
    private static string Section(string user, string label)
    {
        var start = user.IndexOf(label, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var body = user[(start + label.Length)..];
        var end = body.Length;

        foreach (var terminator in new[] { OptimizationMarkers.OutputLabel, "\n\n", "<" })
        {
            var cut = body.IndexOf(terminator, StringComparison.Ordinal);
            if (cut >= 0 && cut < end)
            {
                end = cut;
            }
        }

        return body[..end].Trim();
    }

    /// <summary>Flattens the user turns a marker may appear in.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The concatenated user text.</returns>
    private static string UserText(MessagesRequest request)
    {
        var messages = request.Messages;
        if (messages.Count == 0)
        {
            return string.Empty;
        }

        if (messages.Count == 1)
        {
            return ContentText.Extract(messages[0].Content);
        }

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(messages[i].Role, AnthropicMessage.UserRole, StringComparison.Ordinal))
            {
                _ = builder.Append(ContentText.Extract(messages[i].Content)).Append('\n');
            }
        }

        return builder.ToString();
    }
}
