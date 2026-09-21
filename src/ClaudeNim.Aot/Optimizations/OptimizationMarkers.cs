// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Optimizations;

/// <summary>The phrases that identify a Claude Code housekeeping request.</summary>
/// <remarks>
/// <para>
/// These are matched against the request's own prompt text. They are deliberately long and
/// specific: a missed match simply sends the request upstream as normal, which is harmless, while
/// a false match answers a real question with a canned string, which is not. Length is the cheapest
/// defence against the second.
/// </para>
/// <para>
/// The phrases track prompts that Claude Code owns and may reword between releases. Treat a fast
/// path that stops firing as a wording change rather than a fault.
/// </para>
/// </remarks>
public static class OptimizationMarkers
{
    /// <summary>The probe Claude Code issues to confirm the credential still has quota.</summary>
    internal const string QuotaProbe = "quota";

    /// <summary>The ceiling a quota probe is sent with, which no real turn uses.</summary>
    internal const int QuotaProbeMaxTokens = 1;

    /// <summary>The prompt that asks for a short conversation title.</summary>
    internal const string TitleGeneration = "write a 5-10 word title";

    /// <summary>The prompt that asks for typeahead suggestions.</summary>
    internal const string SuggestionMode = "you are a suggestion generator";

    /// <summary>The prompt that asks which prefix a shell command should be permission-matched on.</summary>
    internal const string CommandPrefix = "process bash commands that an ai coding agent wants to run";

    /// <summary>The prompt that asks for the file paths mentioned in a tool result.</summary>
    internal const string FilePathExtraction = "extract any file paths that this command reads or modifies";
}
