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
/// <see cref="DetectCommandPrefix"/> defaults to <see langword="false"/>, unlike the others: its
/// two markers (<c>&lt;policy_spec&gt;</c> and <c>Command:</c>) are not unique to the legacy
/// manual-permission-mode prefix question it was built for. Claude Code's Auto Mode classifier's
/// own <c>xml_s1</c> stage sends a request carrying the same markers, and this fast path answered
/// it with a bare extracted prefix (e.g. <c>"ls"</c>) instead of forwarding it for a real
/// classification, which the classifier cannot parse as a verdict — it retries several times and
/// then fails closed with "Auto mode could not evaluate this action". A missed match here is
/// harmless (a real upstream call); a false match silently broke Auto Mode entirely.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("OptimizationOptions: {ToString(),nq}")]
public sealed record OptimizationOptions(
    bool MockQuotaProbe = true,
    bool SkipTitleGeneration = true,
    bool SkipSuggestionMode = true,
    bool DetectCommandPrefix = false,
    bool MockFilePathExtraction = true)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Optimizations";
}
