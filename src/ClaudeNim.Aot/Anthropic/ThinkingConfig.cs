// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The caller's extended-thinking request.</summary>
/// <param name="Type">
/// The thinking mode; see <see cref="Adaptive"/>, <see cref="Enabled"/> and <see cref="Disabled"/>.
/// </param>
/// <param name="BudgetTokens">The reasoning ceiling used by the pre-4.6 <see cref="Enabled"/> form.</param>
/// <param name="Display">How reasoning is surfaced; <c>summarized</c>, <c>omitted</c> or <c>updates</c>.</param>
/// <remarks>
/// Claude 4.6 and later replaced the fixed <c>budget_tokens</c> ceiling with
/// <see cref="Adaptive"/>, and the Claude 5 family rejects <c>budget_tokens</c> outright. The
/// proxy accepts every form so that clients pinned to either convention keep working, and
/// reduces them to a single on/off decision plus an optional budget for the upstream.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ThinkingConfig: {IsEnabled}")]
public sealed record ThinkingConfig(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("budget_tokens")] int? BudgetTokens = null,
    [property: JsonPropertyName("display")] string? Display = null)
{
    /// <summary>The model decides how much to think. The current default.</summary>
    internal const string Adaptive = "adaptive";

    /// <summary>Thinking is on with a fixed <c>budget_tokens</c> ceiling. Pre-4.6 clients only.</summary>
    internal const string Enabled = "enabled";

    /// <summary>Thinking is off.</summary>
    internal const string Disabled = "disabled";

    /// <summary>Gets a value indicating whether this configuration asks for reasoning.</summary>
    public bool IsEnabled =>
        string.Equals(Type, Adaptive, StringComparison.Ordinal)
        || string.Equals(Type, Enabled, StringComparison.Ordinal);
}
