// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the splitter that lifts inline reasoning out of streamed answer text.</summary>
/// <remarks>
/// GLM and DeepSeek wrap reasoning in <c>&lt;think&gt;</c> tags inside ordinary content. The
/// reference proxies discard it; this one re-emits it. The hard case is a tag that straddles a
/// chunk boundary, which is what most of these tests are about.
/// </remarks>
public sealed class ThinkTagParserTests
{
    /// <summary>The plain answer text used across several fixtures.</summary>
    private const string Answer = "answer";

    /// <summary>The length of the answer text used to push a tag past the literal-tag threshold.</summary>
    private const int LeadLength = 250;

    /// <summary>Reasoning and answer are separated when both arrive in one chunk.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task SplitsReasoningFromAnswer()
    {
        var segments = Run("<think>weighing it up</think>the answer");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("weighing it up");
        await Assert.That(Join(segments, thinking: false)).IsEqualTo("the answer");
    }

    /// <summary>A tag split across chunks is still recognised.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RecognisesATagSplitAcrossChunks()
    {
        var segments = Run("<thi", "nk>reasoning</thi", $"nk>{Answer}");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("reasoning");
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>A partial tag is held back rather than leaking as answer text.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task HoldsBackAPartialTag()
    {
        var parser = new ThinkTagParser();
        var segments = new List<ThinkTagSegment>();

        parser.Feed($"{Answer}<thi", segments);

        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>Text with no tags passes through untouched.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PassesPlainTextThrough()
    {
        var segments = Run("just an answer");

        await Assert.That(Join(segments, thinking: false)).IsEqualTo("just an answer");
        await Assert.That(Join(segments, thinking: true)).IsEmpty();
    }

    /// <summary>An unterminated reasoning run is still released when the stream ends.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReleasesUnterminatedReasoningOnFlush()
    {
        var segments = Run("<think>cut off mid-thought");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("cut off mid-thought");
    }

    /// <summary>A run of text that merely looks like a tag opening is not swallowed.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DoesNotSwallowTextResemblingATag()
    {
        var segments = Run("a < b and c > d");

        await Assert.That(Join(segments, thinking: false)).IsEqualTo("a < b and c > d");
    }

    /// <summary>A tag appearing well into the answer is prose about tags, not reasoning.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Asked to explain how <c>&lt;think&gt;</c> tags work, the model's own answer would otherwise
    /// be swallowed as an unterminated reasoning block the moment it names the tag.
    /// </remarks>
    [Test]
    public async Task LateTagIsTakenLiterally()
    {
        var lead = new string('a', LeadLength);
        var text = $"{lead} the tag is <think>literally this</think>";
        var segments = Run(text);

        await Assert.That(Join(segments, thinking: true)).IsEmpty();
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(text);
    }

    /// <summary>A tag near the very start of the answer is still treated as reasoning.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EarlyTagIsStillReasoning()
    {
        var segments = Run($"<think>short</think>{Answer}");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("short");
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>A closing tag with no opening tag treats everything before it as reasoning.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Some chat templates seed the assistant turn with the opening <c>&lt;think&gt;</c> themselves,
    /// so the model's own completion text carries only the closing tag. This is a regression test
    /// for a live defect: without this handling the reasoning prose and the literal closing tag both
    /// leaked into the answer, because the parser only ever looked for an opening tag while not
    /// already inside one.
    /// </remarks>
    [Test]
    public async Task ImplicitOpenTreatsLeadingTextAsReasoning()
    {
        var segments = Run($"reasoning with no opening tag</think>{Answer}");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("reasoning with no opening tag");
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>A stray closing tag deep in the answer, after a real pair, is not treated as an implicit open.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StrayClosingTagAfterARealPairIsLiteral()
    {
        var segments = Run("<think>reasoning</think>the tag is </think> here");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("reasoning");
        await Assert.That(Join(segments, thinking: false)).IsEqualTo("the tag is </think> here");
    }

    /// <summary>Feeds a sequence of chunks and flushes.</summary>
    /// <param name="chunks">The chunks to feed, in order.</param>
    /// <returns>The segments produced.</returns>
    private static List<ThinkTagSegment> Run(params string[] chunks)
    {
        var parser = new ThinkTagParser();
        var segments = new List<ThinkTagSegment>();

        foreach (var chunk in chunks)
        {
            parser.Feed(chunk, segments);
        }

        parser.Flush(segments);
        return segments;
    }

    /// <summary>Joins the segments of one kind.</summary>
    /// <param name="segments">The segments to join.</param>
    /// <param name="thinking">Which kind to join.</param>
    /// <returns>The concatenated text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Join(List<ThinkTagSegment> segments, bool thinking)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.IsThinking == thinking)
            {
                _ = builder.Append(segment.Text);
            }
        }

        return builder.ToString();
    }
}
