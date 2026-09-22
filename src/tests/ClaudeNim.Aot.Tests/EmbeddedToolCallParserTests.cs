// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers recovery of tool calls a model writes into its answer text.</summary>
/// <remarks>
/// Several NIM models render a tool call as marker tokens inside ordinary content instead of
/// filling in the structured field. A proxy that only reads the structured field shows the
/// client a wall of tokens and never executes the tool.
/// </remarks>
public sealed class EmbeddedToolCallParserTests
{
    /// <summary>The tool name used across the recovery fixtures.</summary>
    private const string ToolName = "get_weather";

    /// <summary>The arguments used across the recovery fixtures.</summary>
    private const string ToolArguments = """{"city":"Paris"}""";

    /// <summary>The number of runs text surrounding a single call splits into.</summary>
    private const int RunsAroundOneCall = 3;

    /// <summary>A call in the plain spelling is recovered with its name and arguments.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RecoversAPlainSpellingCall()
    {
        var runs = Run($"<|tool_call_begin|>{ToolName}<|tool_call_argument_begin|>{ToolArguments}<|tool_call_end|>");

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].IsCall).IsTrue();
        await Assert.That(runs[0].Name).IsEqualTo(ToolName);
        await Assert.That(runs[0].Payload).IsEqualTo(ToolArguments);
    }

    /// <summary>A call in the DeepSeek spelling is recovered despite using different characters.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RecoversADeepSeekSpellingCall()
    {
        var runs = Run($"<｜tool▁call▁begin｜>{ToolName}<｜tool▁sep｜>{ToolArguments}<｜tool▁call▁end｜>");

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].Name).IsEqualTo(ToolName);
        await Assert.That(runs[0].Payload).IsEqualTo(ToolArguments);
    }

    /// <summary>Text surrounding a call is preserved and kept in order.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PreservesSurroundingText()
    {
        var runs = Run($"Sure, let me check.<|tool_call_begin|>{ToolName}<|tool_call_argument_begin|>{{}}<|tool_call_end|>Done.");

        await Assert.That(runs.Count).IsEqualTo(RunsAroundOneCall);
        await Assert.That(runs[0].IsCall).IsFalse();
        await Assert.That(runs[0].Payload).IsEqualTo("Sure, let me check.");
        await Assert.That(runs[1].IsCall).IsTrue();
        await Assert.That(runs[2].Payload).IsEqualTo("Done.");
    }

    /// <summary>A call split across chunk boundaries is still recognised.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RecognisesACallSplitAcrossChunks()
    {
        var runs = Run(
            "<|tool_call_be",
            $"gin|>{ToolName}<|tool_call_argument_beg",
            $"in|>{ToolArguments}<|tool_call_end|>");

        var call = FirstCall(runs);
        await Assert.That(call.Name).IsEqualTo(ToolName);
    }

    /// <summary>Bracketing markers that carry nothing are dropped.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DropsBracketingMarkers()
    {
        var runs = Run("<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>ping<｜tool▁sep｜>{}<｜tool▁call▁end｜><｜tool▁calls▁end｜>");

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].Name).IsEqualTo("ping");
    }

    /// <summary>Arguments wrapped in a code fence are unwrapped.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnwrapsFencedArguments()
    {
        var runs = Run("<｜tool▁call▁begin｜>ping<｜tool▁sep｜>```json\n{\"a\":1}\n```<｜tool▁call▁end｜>");

        await Assert.That(runs[0].Payload).IsEqualTo("""{"a":1}""");
    }

    /// <summary>Plain text with no markers passes through untouched.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PassesPlainTextThrough()
    {
        var runs = Run("just an ordinary answer");

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].IsCall).IsFalse();
        await Assert.That(runs[0].Payload).IsEqualTo("just an ordinary answer");
    }

    /// <summary>An unterminated call is released as text rather than invented.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReleasesAnUnterminatedCallAsText()
    {
        var runs = Run($"<|tool_call_begin|>{ToolName}<|tool_call_argument_begin|>{{\"city\":\"Pa");

        await Assert.That(NoRunIsACall(runs)).IsTrue();
    }

    /// <summary>Feeds a sequence of chunks and flushes.</summary>
    /// <param name="chunks">The chunks to feed, in order.</param>
    /// <returns>The runs produced.</returns>
    private static List<EmbeddedToolCall> Run(params string[] chunks)
    {
        var parser = new EmbeddedToolCallParser();
        var runs = new List<EmbeddedToolCall>();

        foreach (var chunk in chunks)
        {
            parser.Feed(chunk, runs);
        }

        parser.Flush(runs);
        return runs;
    }

    /// <summary>Finds the first recovered call among a set of runs.</summary>
    /// <param name="runs">The runs to search.</param>
    /// <returns>The first call.</returns>
    /// <exception cref="InvalidOperationException">No run in the set is a call.</exception>
    private static EmbeddedToolCall FirstCall(List<EmbeddedToolCall> runs)
    {
        foreach (var run in runs)
        {
            if (run.IsCall)
            {
                return run;
            }
        }

        throw new InvalidOperationException("No call was recovered.");
    }

    /// <summary>Determines whether every run in a set is plain text.</summary>
    /// <param name="runs">The runs to check.</param>
    /// <returns><see langword="true"/> when none of the runs is a call.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NoRunIsACall(List<EmbeddedToolCall> runs) => runs.TrueForAll(static r => !r.IsCall);
}
