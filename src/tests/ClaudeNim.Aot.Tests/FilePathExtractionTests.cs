// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Optimizations;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers which files a command is reported as having read.</summary>
public sealed class FilePathExtractionTests
{
    /// <summary>The answer for a command that read nothing.</summary>
    private const string NoPaths = "<filepaths>\n</filepaths>";

    /// <summary>A command that reads files reports them.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReadingCommandReportsItsFiles() =>
        await Assert.That(FilePathExtraction.Extract("cat a.txt b.txt"))
            .IsEqualTo("<filepaths>\na.txt\nb.txt\n</filepaths>");

    /// <summary>A command that only lists files reports none.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// <c>ls</c> names files without revealing their contents, so nothing was read. Reporting
    /// them would let the session believe it has context it never received.
    /// </remarks>
    [Test]
    public async Task ListingCommandReportsNothing()
    {
        await Assert.That(FilePathExtraction.Extract("ls -la /tmp")).IsEqualTo(NoPaths);
        await Assert.That(FilePathExtraction.Extract("find . -name x")).IsEqualTo(NoPaths);
    }

    /// <summary>Options are not mistaken for paths.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task OptionsAreNotPaths() =>
        await Assert.That(FilePathExtraction.Extract("head -n 20 log.txt"))
            .IsEqualTo("<filepaths>\nlog.txt\n</filepaths>");

    /// <summary>A grep pattern is not reported as a file.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task GrepPatternIsNotAPath() =>
        await Assert.That(FilePathExtraction.Extract("grep needle haystack.txt"))
            .IsEqualTo("<filepaths>\nhaystack.txt\n</filepaths>");

    /// <summary>A grep pattern supplied by option leaves every operand a path.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task GrepPatternOptionLeavesEveryOperandAPath() =>
        await Assert.That(FilePathExtraction.Extract("grep -e needle a.txt b.txt"))
            .IsEqualTo("<filepaths>\na.txt\nb.txt\n</filepaths>");

    /// <summary>A grep with only a pattern reports nothing.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task GrepWithoutFilesReportsNothing() =>
        await Assert.That(FilePathExtraction.Extract("grep needle")).IsEqualTo(NoPaths);

    /// <summary>A command invoked by path is still recognised by its name.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task QualifiedCommandIsRecognised() =>
        await Assert.That(FilePathExtraction.Extract("/usr/bin/cat a.txt"))
            .IsEqualTo("<filepaths>\na.txt\n</filepaths>");

    /// <summary>An unrecognised command reports nothing rather than guessing.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnknownCommandReportsNothing() =>
        await Assert.That(FilePathExtraction.Extract("curl https://example.com"))
            .IsEqualTo(NoPaths);
}
