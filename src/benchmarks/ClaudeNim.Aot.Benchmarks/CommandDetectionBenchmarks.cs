// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using ClaudeNim.Aot.Optimizations;

namespace ClaudeNim.Aot.Benchmarks;

/// <summary>Measures the allocation of the local shell-command housekeeping detectors.</summary>
/// <remarks>
/// <see cref="CommandPrefix"/> and <see cref="FilePathExtraction"/> each run once per Claude Code
/// permission or file-tracking probe, over representative commands covering a subcommand-driven
/// prefix, a reading command with an option that consumes its argument, and a <c>grep</c> search.
/// </remarks>
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
[SuppressMessage("Design", "CA1052:Static holder types should be Static or NotInheritable", Justification = "BenchmarkDotNet instantiates the class itself (BDN1105); it cannot be static.")]
[SuppressMessage("Design", "SST1432:Type declares only static members", Justification = "BenchmarkDotNet instantiates the class itself (BDN1105); it cannot be static.")]
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "BenchmarkDotNet requires instance benchmark methods; a static one fails validation.")]
public class CommandDetectionBenchmarks
{
    /// <summary>How many times the command set is scanned inside one invocation.</summary>
    private const int Repetitions = 500;

    /// <summary>Representative commands covering every branch of both detectors.</summary>
    private static readonly string[] Commands =
    [
        "git commit -m \"fix retry backoff\"",
        "head -n 20 src/ClaudeNim.Aot/Nvidia/NimClient.cs",
        "grep -n \"IsTransient\" src/ClaudeNim.Aot/Nvidia/RetrySchedule.cs",
        "ls -la src/ClaudeNim.Aot",
        "cat README.md",
        "npm run build -- --watch",
    ];

    /// <summary>Runs both detectors over every command, repeated <see cref="Repetitions"/> times.</summary>
    /// <returns>The combined length of every answer produced, so the call cannot be optimised away.</returns>
    [Benchmark]
    public int DetectAll()
    {
        var total = 0;

        for (var run = 0; run < Repetitions; run++)
        {
            for (var i = 0; i < Commands.Length; i++)
            {
                total += CommandPrefix.Extract(Commands[i]).Length;
                total += FilePathExtraction.Extract(Commands[i]).Length;
            }
        }

        return total;
    }
}
