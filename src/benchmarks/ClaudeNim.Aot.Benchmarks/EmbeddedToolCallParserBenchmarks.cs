// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Benchmarks;

/// <summary>Measures the allocation the embedded-tool-call recovery pass does per turn.</summary>
/// <remarks>
/// <see cref="EmbeddedToolCallParser"/> runs on every run of answer text a Kimi- or DeepSeek-style
/// model produces, so a transcript carrying a marker-token call is fed through it in small
/// fragments, repeated enough times that a GC allocation tick sample is meaningful.
/// </remarks>
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class EmbeddedToolCallParserBenchmarks
{
    /// <summary>How many times the transcript is replayed inside one invocation.</summary>
    private const int Repetitions = 200;

    /// <summary>The chunk-sized fragments of an answer carrying one embedded tool call, as a stream would deliver them.</summary>
    private static readonly string[] Chunks =
    [
        "I'll check the file for you.\n\n",
        "<|tool_call_begin|>read_file",
        "<|tool_call_argument_begin|>```json\n",
        "{\"path\": \"src/Program.cs\", \"start",
        "_line\": 1, \"end_line\": 40}\n```",
        "<|tool_call_end|>\n\nHere is what I found.",
    ];

    /// <summary>The runs each replay is split into, reused the way the translator reuses its own list.</summary>
    private readonly List<EmbeddedToolCall> _runs = [];

    /// <summary>Feeds a full transcript through a fresh parser, repeated <see cref="Repetitions"/> times.</summary>
    /// <returns>The number of runs produced, so the call cannot be optimised away.</returns>
    [Benchmark]
    public int FeedTranscript()
    {
        var total = 0;

        for (var run = 0; run < Repetitions; run++)
        {
            var parser = new EmbeddedToolCallParser();

            for (var i = 0; i < Chunks.Length; i++)
            {
                _runs.Clear();
                parser.Feed(Chunks[i], _runs);
                total += _runs.Count;
            }

            _runs.Clear();
            parser.Flush(_runs);
            total += _runs.Count;
        }

        return total;
    }
}
