// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics;
using ClaudeNim.Aot.Compat;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers process success detection on each supported framework.</summary>
public sealed class ProcessExitStatusCompatTests
{
    /// <summary>A successful command is distinguished from a CLI argument failure.</summary>
    /// <param name="argument">The dotnet command argument.</param>
    /// <param name="expected">Whether that command succeeds.</param>
    /// <param name="cancellationToken">Stops the process and output reads.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("--version", true)]
    [Arguments("--not-a-real-dotnet-option", false)]
    public async Task SucceededAsync_CommandExit_ReturnsSuccess(string argument, bool expected, CancellationToken cancellationToken)
    {
        var command = new ProcessStartInfo { FileName = "dotnet", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        command.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = command };
        _ = process.Start();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var succeeded = await ProcessExitStatusCompat.SucceededAsync(process, cancellationToken);
        _ = await output;
        _ = await errors;

        await Assert.That(succeeded).IsEqualTo(expected);
    }
}
