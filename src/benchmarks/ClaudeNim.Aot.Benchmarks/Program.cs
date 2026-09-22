// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Running;

namespace ClaudeNim.Aot.Benchmarks;

/// <summary>The benchmark host's entry point.</summary>
public static class Program
{
    /// <summary>Runs the benchmarks named on the command line, or every benchmark when none are named.</summary>
    /// <param name="args">The BenchmarkDotNet command-line arguments.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
