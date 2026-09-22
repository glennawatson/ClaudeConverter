// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Optimizations;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers which requests are answered locally and which are forwarded.</summary>
/// <remarks>
/// The detection rules come from the prompts Claude Code actually sends. A false match answers a
/// real question with a canned string, so the negative cases here matter as much as the positive
/// ones.
/// </remarks>
public sealed class RequestOptimizerTests
{
    /// <summary>The output ceiling an ordinary turn uses.</summary>
    private const int OrdinaryMaxTokens = 1024;

    /// <summary>Every fast path enabled.</summary>
    private static readonly OptimizationOptions AllOn = new();

    /// <summary>A quota probe is recognised by its shape as well as its text.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task QuotaProbeIsAnswered()
    {
        var recognised = RequestOptimizer.TryAnswer(Request("quota", maxTokens: 1), AllOn, out var answer);

        await Assert.That(recognised).IsTrue();
        await Assert.That(answer).IsEqualTo("Quota check passed.");
    }

    /// <summary>The word "quota" in an ordinary turn is not a probe.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task OrdinaryTurnMentioningQuotaIsForwarded() =>
        await Assert.That(RequestOptimizer.TryAnswer(Request("quota"), AllOn, out _)).IsFalse();

    /// <summary>A suggestion request is answered with nothing.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task SuggestionRequestIsAnsweredEmpty()
    {
        var request = Request("[SUGGESTION MODE: complete this] git che");
        var recognised = RequestOptimizer.TryAnswer(request, AllOn, out var answer);

        await Assert.That(recognised).IsTrue();
        await Assert.That(answer).IsEmpty();
    }

    /// <summary>A prefix request is answered from the command it carries.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PrefixRequestIsAnsweredFromItsCommand()
    {
        var request = Request("<policy_spec> rules here\nCommand:\ngit commit -m hi\nOutput:\nok");
        var recognised = RequestOptimizer.TryAnswer(request, AllOn, out var answer);

        await Assert.That(recognised).IsTrue();
        await Assert.That(answer).IsEqualTo("git commit");
    }

    /// <summary>A file-path request is answered from the command it carries.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FilePathRequestIsAnsweredFromItsCommand()
    {
        var request = Request("Command:\ncat notes.md\nOutput:\nsome text\nReturn filepaths");
        var recognised = RequestOptimizer.TryAnswer(request, AllOn, out var answer);

        await Assert.That(recognised).IsTrue();
        await Assert.That(answer).IsEqualTo("<filepaths>\nnotes.md\n</filepaths>");
    }

    /// <summary>A title request needs a corroborating phrase, not just the word "title".</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task TitleRequestNeedsCorroboration()
    {
        var bare = Request("hello", system: "Give this a title");
        await Assert.That(RequestOptimizer.TryAnswer(bare, AllOn, out _)).IsFalse();

        var real = Request("hello", system: "Write a short sentence-case title for this coding session");
        var recognised = RequestOptimizer.TryAnswer(real, AllOn, out var answer);

        await Assert.That(recognised).IsTrue();
        await Assert.That(answer).IsEqualTo("Conversation");
    }

    /// <summary>
    /// Auto Mode's classifier system prompt naturally contains both of
    /// <see cref="OptimizationOptions.SkipTitleGeneration"/>'s markers, and its turn replays the
    /// full transcript rather than asking a standalone question — reproduced here (with a realistic
    /// message count) as a regression test for the collision that silently broke Auto Mode when the
    /// message-count guard did not yet exist.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// A single message, matching what was actually observed live: Auto Mode's classifier packs its
    /// huge system prompt and transcript into as few messages as it likes, so a real reproduction
    /// has to be large in content length, not message count, to be faithful to the live failure.
    /// </remarks>
    [Test]
    public async Task ClassifierLikeSystemPromptWithAReplayedTranscriptIsNotTreatedAsATitleRequest()
    {
        var hugeSystemPrompt =
            $"You are evaluating an action taken during this coding session against a policy. No title is required. {new string('x', OptimizationMarkers.MaxHousekeepingContentLength)}";

        var request = Request("Command:\nls -la /tmp\nOutput:\n", system: hugeSystemPrompt);

        await Assert.That(RequestOptimizer.TryAnswer(request, AllOn, out _)).IsFalse();
    }

    /// <summary>An ordinary turn is forwarded untouched.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task OrdinaryTurnIsForwarded() =>
        await Assert.That(RequestOptimizer.TryAnswer(Request("refactor this method"), AllOn, out _))
            .IsFalse();

    /// <summary>A disabled fast path forwards the request it would have answered.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DisabledFastPathForwards()
    {
        var options = AllOn with { DetectCommandPrefix = false };
        var request = Request("<policy_spec> rules\nCommand:\ngit commit\nOutput:\nok");

        await Assert.That(RequestOptimizer.TryAnswer(request, options, out _)).IsFalse();
    }

    /// <summary>Builds a request carrying one user turn.</summary>
    /// <param name="text">The user text.</param>
    /// <param name="system">The system prompt.</param>
    /// <param name="maxTokens">The output ceiling.</param>
    /// <returns>The request.</returns>
    private static MessagesRequest Request(
        string text,
        string? system = null,
        int maxTokens = OrdinaryMaxTokens) =>
        new(
            "claude-sonnet-5",
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromText(text))],
            maxTokens,
            System: system is null ? null : MessageContent.FromText(system));
}
