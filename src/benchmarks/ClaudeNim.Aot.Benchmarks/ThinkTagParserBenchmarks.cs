// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Benchmarks;

/// <summary>Measures the allocation the streamed reasoning-tag split does per turn.</summary>
/// <remarks>
/// <see cref="ThinkTagParser"/> runs once per streamed content delta, so a realistic transcript is
/// fed to it in the small fragments a network read would actually deliver, repeated enough times
/// inside one iteration that a GC allocation tick sample is meaningful rather than a one-tick
/// artifact.
/// </remarks>
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class ThinkTagParserBenchmarks
{
    /// <summary>How many times the transcript is replayed inside one invocation.</summary>
    private const int Repetitions = 200;

    /// <summary>The chunk-sized fragments of a reasoning-then-answer transcript, as a stream would deliver them.</summary>
    private static readonly string[] Chunks =
    [
        "<think>",
        "Let me work through this step by ",
        "step. The user wants to know how the ",
        "retry backoff behaves under a 503",
        ".\n\nFirst I should check the transient",
        " status codes, then the jitter window.",
        "</think>",
        "The proxy retries a 503 with exponential ",
        "backoff and full jitter, honouring any ",
        "Retry-After header the upstream sends. ",
        "Each attempt roughly doubles the prior ",
        "delay up to a configured ceiling.",
    ];

    /// <summary>The segments each replay is split into, reused the way the translator reuses its own list.</summary>
    private readonly List<ThinkTagSegment> _segments = [];

    /// <summary>Feeds a full transcript through a fresh parser, repeated <see cref="Repetitions"/> times.</summary>
    /// <returns>The number of segments produced, so the call cannot be optimised away.</returns>
    [Benchmark]
    public int FeedTranscript()
    {
        var total = 0;

        for (var run = 0; run < Repetitions; run++)
        {
            var parser = new ThinkTagParser();

            for (var i = 0; i < Chunks.Length; i++)
            {
                _segments.Clear();
                parser.Feed(Chunks[i], _segments);
                total += _segments.Count;
            }

            _segments.Clear();
            parser.Flush(_segments);
            total += _segments.Count;
        }

        return total;
    }
}
