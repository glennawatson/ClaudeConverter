// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Optimizations;

/// <summary>The markers that identify a Claude Code housekeeping request.</summary>
/// <remarks>
/// <para>
/// These are taken from the detection rules in
/// <see href="https://github.com/diyism/cc-nim">cc-nim</see>, which were derived from the prompts
/// Claude Code actually sends, rather than guessed from their descriptions. Several of them are
/// structural markers the prompt wraps its payload in — <c>&lt;policy_spec&gt;</c>,
/// <c>[SUGGESTION MODE:</c>, <c>&lt;filepaths&gt;</c> — which makes them far more reliable to match
/// on than any run of English prose in the instruction itself.
/// </para>
/// <para>
/// A missed match simply sends the request upstream, which is harmless. A false match answers a
/// real question with a canned string, so every rule here requires more than one marker to agree.
/// </para>
/// </remarks>
public static class OptimizationMarkers
{
    /// <summary>The entire content of the probe Claude Code sends to confirm the credential works.</summary>
    internal const string QuotaProbe = "quota";

    /// <summary>The output ceiling a quota probe is sent with, which no real turn uses.</summary>
    internal const int QuotaProbeMaxTokens = 1;

    /// <summary>The word every conversation-title prompt contains.</summary>
    internal const string Title = "title";

    /// <summary>The bracket a suggestion request wraps its payload in.</summary>
    internal const string SuggestionMode = "[SUGGESTION MODE:";

    /// <summary>The element a command permission prompt wraps its policy in.</summary>
    internal const string PolicySpec = "<policy_spec>";

    /// <summary>The label that introduces the shell command being asked about.</summary>
    internal const string CommandLabel = "Command:";

    /// <summary>The label that introduces the command's output.</summary>
    internal const string OutputLabel = "Output:";

    /// <summary>The element a file-path extraction prompt asks to be filled in.</summary>
    internal const string FilePaths = "filepaths";

    /// <summary>The phrases that, with <see cref="Title"/>, confirm a title-generation prompt.</summary>
    /// <remarks>
    /// The word "title" alone appears in far too much ordinary conversation to act on. One of
    /// these has to appear with it, all of which belong to the instruction rather than the topic.
    /// </remarks>
    private static readonly string[] TitleCorroborators =
    [
        "sentence-case title",
        "return json",
        "coding session",
        "this session",
    ];

    /// <summary>Determines whether a prompt carries a phrase that confirms a title request.</summary>
    /// <param name="system">The system prompt.</param>
    /// <returns><see langword="true"/> when one of the corroborating phrases is present.</returns>
    internal static bool IsTitleCorroborated(string system)
    {
        foreach (var phrase in TitleCorroborators)
        {
            if (system.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
