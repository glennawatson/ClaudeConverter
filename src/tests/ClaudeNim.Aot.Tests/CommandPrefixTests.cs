// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Optimizations;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the prefix a shell command is permission-matched on.</summary>
/// <remarks>
/// This answer feeds a permission decision, so the cases that matter most are the ones where
/// being wrong would widen a match the user never granted.
/// </remarks>
public sealed class CommandPrefixTests
{
    /// <summary>A bare command is its own prefix.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task BareCommandIsItsOwnPrefix() =>
        await Assert.That(CommandPrefix.Extract("ls -la")).IsEqualTo("ls");

    /// <summary>A subcommand-driven tool carries its subcommand into the prefix.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Allowing <c>git commit</c> must not also allow <c>git push</c>, which it would if the
    /// prefix stopped at <c>git</c>.
    /// </remarks>
    [Test]
    public async Task SubcommandIsPartOfThePrefix()
    {
        await Assert.That(CommandPrefix.Extract("git commit -m x")).IsEqualTo("git commit");
        await Assert.That(CommandPrefix.Extract("npm install left-pad")).IsEqualTo("npm install");
        await Assert.That(CommandPrefix.Extract("docker run alpine")).IsEqualTo("docker run");
    }

    /// <summary>An option is not mistaken for a subcommand.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task OptionIsNotASubcommand() =>
        await Assert.That(CommandPrefix.Extract("git --version")).IsEqualTo("git");

    /// <summary>A command substitution is reported rather than summarised.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The text after a substitution is not what will run, so no prefix taken from it would be
    /// honest. Both spellings have to be caught.
    /// </remarks>
    [Test]
    public async Task SubstitutionIsReported()
    {
        await Assert.That(CommandPrefix.Extract("echo $(rm -rf /)")).IsEqualTo("command_injection_detected");
        await Assert.That(CommandPrefix.Extract("echo `whoami`")).IsEqualTo("command_injection_detected");
    }

    /// <summary>Leading environment assignments are kept with the command they apply to.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EnvironmentAssignmentsArePreserved() =>
        await Assert.That(CommandPrefix.Extract("FOO=1 ls")).IsEqualTo("FOO=1 ls");

    /// <summary>A command with nothing but assignments yields no prefix.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AssignmentsAloneYieldNoPrefix() =>
        await Assert.That(CommandPrefix.Extract("FOO=1 BAR=2")).IsEqualTo("none");

    /// <summary>An empty command yields no prefix rather than throwing.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EmptyCommandYieldsNoPrefix()
    {
        await Assert.That(CommandPrefix.Extract(string.Empty)).IsEqualTo("none");
        await Assert.That(CommandPrefix.Extract("   ")).IsEqualTo("none");
    }

    /// <summary>An unbalanced quote yields no prefix rather than a guess.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnbalancedQuoteYieldsNoPrefix() =>
        await Assert.That(CommandPrefix.Extract("echo \"unterminated")).IsEqualTo("none");
}
