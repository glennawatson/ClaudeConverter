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

    /// <summary>The length of a run long enough to give up waiting for a seeded closing tag.</summary>
    private const int HoldbackOverrun = 2100;

    /// <summary>The reasoning text used across several fixtures.</summary>
    private const string Reasoning = "reasoning";

    /// <summary>Answer text carrying no tags at all.</summary>
    private const string PlainAnswer = "just an answer";

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

        await Assert.That(Join(segments, thinking: true)).IsEqualTo(Reasoning);
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
        var segments = Run(PlainAnswer);

        await Assert.That(Join(segments, thinking: false)).IsEqualTo(PlainAnswer);
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

        await Assert.That(Join(segments, thinking: true)).IsEqualTo(Reasoning);
        await Assert.That(Join(segments, thinking: false)).IsEqualTo("the tag is </think> here");
    }

    /// <summary>A seeded reasoning block split across chunks is still separated from the answer.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect. The single-chunk case was handled, but on a
    /// real streamed turn the reasoning arrives a fragment at a time: each fragment was emitted as
    /// answer text on arrival, and by the time the closing tag landed the literal-tag threshold had
    /// been passed, so the prose and a raw <c>&lt;/think&gt;</c> both reached the client.
    /// </remarks>
    [Test]
    public async Task SeededReasoningSplitAcrossChunksIsStillReasoning()
    {
        var segments = RunSeeded("the user ", "wants me to ", $"continue</think>{Answer}");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo("the user wants me to continue");
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>A seeded reasoning run longer than the holdback is released as answer text.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The turn cannot be held indefinitely on the chance a closing tag is still coming, so the
    /// text goes out. The closing tag that eventually arrives is still dropped rather than shown.
    /// </remarks>
    [Test]
    public async Task SeededReasoningPastTheHoldbackBecomesAnswerWithoutALiteralTag()
    {
        var lead = new string('a', HoldbackOverrun);
        var segments = RunSeeded(lead, $"</think>{Answer}");

        await Assert.That(Join(segments, thinking: true)).IsEmpty();
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(lead + Answer);
    }

    /// <summary>A model that never writes a closing tag still has its answer delivered as answer text.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task SeededWaitReleasesPlainAnswerText()
    {
        var segments = RunSeeded("just ", "an answer");

        await Assert.That(Join(segments, thinking: true)).IsEmpty();
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(PlainAnswer);
    }

    /// <summary>A model writing its own opening tag is parsed normally despite the seeded wait.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task SeededWaitYieldsToAModelThatWritesItsOwnTags()
    {
        var segments = RunSeeded("<think>reason", $"ing</think>{Answer}");

        await Assert.That(Join(segments, thinking: true)).IsEqualTo(Reasoning);
        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>Reasoning reported on its own field ends the wait for a seeded tag at once.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A model with a <c>reasoning_content</c> field never seeds an inline tag, so its answer must
    /// not be held back waiting for one that cannot arrive.
    /// </remarks>
    [Test]
    public async Task ExplicitReasoningReleasesTheHeldAnswerImmediately()
    {
        var parser = new ThinkTagParser(true);
        var segments = new List<ThinkTagSegment>();

        parser.Feed(Answer, segments);
        await Assert.That(segments).IsEmpty();

        parser.NoteExplicitReasoning(segments);

        await Assert.That(Join(segments, thinking: false)).IsEqualTo(Answer);
    }

    /// <summary>Feeds a sequence of chunks to a parser expecting a seeded reasoning block, and flushes.</summary>
    /// <param name="chunks">The chunks to feed, in order.</param>
    /// <returns>The segments produced.</returns>
    private static List<ThinkTagSegment> RunSeeded(params string[] chunks)
    {
        var parser = new ThinkTagParser(true);
        var segments = new List<ThinkTagSegment>();

        foreach (var chunk in chunks)
        {
            parser.Feed(chunk, segments);
        }

        parser.Flush(segments);
        return segments;
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
