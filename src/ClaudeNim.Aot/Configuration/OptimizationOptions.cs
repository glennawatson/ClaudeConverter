// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Switches for the fast paths that answer Claude Code housekeeping probes locally.</summary>
/// <param name="MockQuotaProbe">Whether quota probes are answered locally.</param>
/// <param name="SkipTitleGeneration">Whether conversation title generation is skipped.</param>
/// <param name="SkipSuggestionMode">Whether typeahead suggestion requests are skipped.</param>
/// <param name="DetectCommandPrefix">Whether shell command prefixes are extracted locally.</param>
/// <param name="MockFilePathExtraction">Whether file path extraction is answered locally.</param>
/// <remarks>
/// Each of these requests has a known, content-free answer. Serving them from the proxy
/// removes a network round trip and, on a metered upstream, a billed completion.
/// <para>
/// <see cref="DetectCommandPrefix"/>'s two markers (<c>&lt;policy_spec&gt;</c> and <c>Command:</c>)
/// and <see cref="SkipTitleGeneration"/>'s two markers (the word "title" plus a corroborating
/// phrase such as "coding session") are not unique to the legacy requests they were built for —
/// Claude Code's Auto Mode classifier's own turn can carry the same marker text, in a system prompt
/// describing evaluating an action taken "during this coding session". Both fast paths answered a
/// full classifier turn — huge system prompt, replayed transcript and all — with a canned reply
/// (a bare extracted command prefix, or the literal string <c>"Conversation"</c>) instead of
/// forwarding it for a real classification, which the classifier cannot parse as a verdict; it
/// retried and then failed closed with "Auto mode could not evaluate this action", with no error
/// surfaced anywhere that pointed back at this proxy. Reproduced directly against the proxy.
/// </para>
/// <para>
/// The actual fix is <see cref="Optimizations.OptimizationMarkers.MaxHousekeepingContentLength"/>:
/// a genuine title-generation or command-prefix request is a single, standalone question — a few
/// hundred characters — never a replayed conversation. A message-count guard alone does not catch
/// Auto Mode's classifier turn, since it can pack its ~150,000-character system prompt and
/// transcript into as few messages as it likes; <c>RequestOptimizer</c> instead requires the
/// combined system-prompt and user-text length stay short before trusting either fast path's
/// marker text — the same shape-over-content principle <see cref="MockQuotaProbe"/>'s own check
/// already used. Both fast paths stay on by default; a missed match is still harmless (an ordinary
/// upstream call), and a request shaped like Auto Mode's classifier turn no longer matches either
/// one — verified directly against the proxy with the exact request shape a live classifier
/// failure was traced to.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("OptimizationOptions: {ToString(),nq}")]
public sealed record OptimizationOptions(
    bool MockQuotaProbe = true,
    bool SkipTitleGeneration = true,
    bool SkipSuggestionMode = true,
    bool DetectCommandPrefix = true,
    bool MockFilePathExtraction = true)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Optimizations";
}
