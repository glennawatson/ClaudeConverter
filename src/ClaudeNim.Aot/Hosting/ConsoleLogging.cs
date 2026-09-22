// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Microsoft.Extensions.Logging.Console;

namespace ClaudeNim.Aot.Hosting;

/// <summary>Picks the console format that suits where the proxy's output is actually going.</summary>
/// <remarks>
/// <para>
/// Under systemd the output is not a console at all — it is a pipe into the journal, and the
/// journal has its own idea of how a log line is coloured and filtered. It takes that from the
/// entry's priority, which it reads from a <c>&lt;N&gt;</c> prefix on each line; without one every
/// line this proxy writes is filed as plain information, so a warning is neither yellow in
/// <c>journalctl</c> nor found by <c>journalctl -p warning</c>.
/// </para>
/// <para>
/// The systemd formatter writes exactly that prefix, and writes each entry on one line rather than
/// two, which matters more than it sounds once scopes are on: the default format puts the category
/// and the scopes on their own line above every message.
/// </para>
/// <para>
/// A formatter named in configuration always wins. This only supplies a default for the case the
/// configuration says nothing about, which is the case an operator is least likely to have thought
/// about.
/// </para>
/// </remarks>
public static class ConsoleLogging
{
    /// <summary>The configuration key naming the console formatter.</summary>
    internal const string FormatterNameKey = "Logging:Console:FormatterName";

    /// <summary>Chooses the console formatter to install, if any.</summary>
    /// <param name="configuredName">The formatter configuration names, which may be absent.</param>
    /// <param name="journalStream">The <c>JOURNAL_STREAM</c> variable systemd sets on a journalled stream.</param>
    /// <param name="invocationId">The <c>INVOCATION_ID</c> variable systemd sets on every unit it starts.</param>
    /// <returns>The formatter to install, or <see langword="null"/> to leave the default in place.</returns>
    public static string? Formatter(string? configuredName, string? journalStream, string? invocationId) =>
        string.IsNullOrWhiteSpace(configuredName) && RunsUnderSystemd(journalStream, invocationId)
            ? ConsoleFormatterNames.Systemd
            : null;

    /// <summary>Determines whether this process was started by systemd.</summary>
    /// <param name="journalStream">The <c>JOURNAL_STREAM</c> variable systemd sets on a journalled stream.</param>
    /// <param name="invocationId">The <c>INVOCATION_ID</c> variable systemd sets on every unit it starts.</param>
    /// <returns><see langword="true"/> when the output is going to the journal.</returns>
    /// <remarks>
    /// <c>JOURNAL_STREAM</c> is the precise signal — it says this process's own standard output is
    /// the journal — but a unit that redirects its output loses it, so <c>INVOCATION_ID</c> stands
    /// in as the weaker "started by systemd at all" answer.
    /// </remarks>
    private static bool RunsUnderSystemd(string? journalStream, string? invocationId) =>
        !string.IsNullOrWhiteSpace(journalStream) || !string.IsNullOrWhiteSpace(invocationId);
}
