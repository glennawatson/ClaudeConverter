// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Hosting;
using Microsoft.Extensions.Logging.Console;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers choosing the console format from where the output is actually going.</summary>
public sealed class ConsoleLoggingTests
{
    /// <summary>A value standing in for the file descriptor systemd names in JOURNAL_STREAM.</summary>
    private const string JournalStream = "9:123456";

    /// <summary>A value standing in for the identifier systemd gives a unit start.</summary>
    private const string InvocationId = "d027919c24034905a80ef9db40752f9d";

    /// <summary>Output going to the journal is written with the priority prefix the journal reads.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// Without the prefix every line is filed as plain information, so a warning is neither
    /// coloured by <c>journalctl</c> nor found by <c>journalctl -p warning</c>.
    /// </remarks>
    [Test]
    public async Task JournalledOutputUsesTheSystemdFormatter() =>
        await Assert.That(ConsoleLogging.Formatter(null, JournalStream, InvocationId))
            .IsEqualTo(ConsoleFormatterNames.Systemd);

    /// <summary>A unit whose output was redirected is still recognised as a unit.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SystemdUnitWithoutAJournalStreamStillUsesTheSystemdFormatter() =>
        await Assert.That(ConsoleLogging.Formatter(null, null, InvocationId))
            .IsEqualTo(ConsoleFormatterNames.Systemd);

    /// <summary>Output going to a terminal keeps the format a person reads.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TerminalOutputKeepsTheDefaultFormatter() =>
        await Assert.That(ConsoleLogging.Formatter(null, null, null)).IsNull();

    /// <summary>A formatter named in configuration is never overridden.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// The detection supplies a default for the case configuration says nothing about; an operator
    /// who has named a format has already answered the question.
    /// </remarks>
    [Test]
    public async Task ConfiguredFormatterWins() =>
        await Assert.That(ConsoleLogging.Formatter(ConsoleFormatterNames.Json, JournalStream, InvocationId)).IsNull();

    /// <summary>A blank configured name counts as no answer at all.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task BlankConfiguredFormatterIsTreatedAsAbsent() =>
        await Assert.That(ConsoleLogging.Formatter("  ", JournalStream, null))
            .IsEqualTo(ConsoleFormatterNames.Systemd);
}
