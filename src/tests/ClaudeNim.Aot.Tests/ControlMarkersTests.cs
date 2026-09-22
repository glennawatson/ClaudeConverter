// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers dropping the chat-template control markers that leak into answer text.</summary>
/// <remarks>
/// The Nemotron templates append a reasoning-effort marker to the last user turn, and a model
/// trained on that text writes one back often enough that it reached a client as though it were
/// the answer. The templates are documented on each model's card; the markers here are quoted from
/// them.
/// </remarks>
public sealed class ControlMarkersTests
{
    /// <summary>The answer text the fixtures wrap a marker in.</summary>
    private const string Answer = "done";

    /// <summary>The marker seen leaking in a live session.</summary>
    private const string MethodicalExecution = "{methodical_execution}";

    /// <summary>Every known control marker is dropped from answer text.</summary>
    /// <param name="marker">The marker to strip.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    [Arguments("{methodical_execution}")]
    [Arguments("{reasoning effort: low}")]
    [Arguments("{reasoning effort: efficient}")]
    [Arguments("<|im_start|>")]
    [Arguments("<|im_end|>")]
    [Arguments("<｜begin▁of▁sentence｜>")]
    [Arguments("<｜end▁of▁sentence｜>")]
    [Arguments("<｜User｜>")]
    [Arguments("<｜Assistant｜>")]
    [Arguments("<｜System｜>")]
    [Arguments("<｜latest_reminder｜>")]
    public async Task StripsAKnownMarker(string marker) =>
        await Assert.That(ControlMarkers.Strip($"{marker}{Answer}")).IsEqualTo(Answer);

    /// <summary>Braces the model wrote on purpose survive.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The effort markers are prose in braces, so a pattern broad enough to match them would eat
    /// the JSON and the code a coding client exists to carry.
    /// </remarks>
    [Test]
    public async Task LeavesOrdinaryBracedTextAlone()
    {
        const string Code = "public void M() { return; }";
        const string Json = """{"effort": "low"}""";

        await Assert.That(ControlMarkers.Strip(Code)).IsEqualTo(Code);
        await Assert.That(ControlMarkers.Strip(Json)).IsEqualTo(Json);
    }

    /// <summary>A marker arriving in fragments is still dropped rather than let through in halves.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task StripsAMarkerSplitAcrossChunks()
    {
        var parser = new EmbeddedToolCallParser();
        var runs = new List<EmbeddedToolCall>();

        parser.Feed("{methodi", runs);
        parser.Feed("cal_execu", runs);
        parser.Feed($"tion}}{Answer}", runs);
        parser.Flush(runs);

        await Assert.That(Text(runs)).IsEqualTo(Answer);
    }

    /// <summary>A marker on its own leaves no text block behind at all.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AMarkerAloneProducesNoText()
    {
        var parser = new EmbeddedToolCallParser();
        var runs = new List<EmbeddedToolCall>();

        parser.Feed(MethodicalExecution, runs);
        parser.Flush(runs);

        await Assert.That(Text(runs)).IsEmpty();
    }

    /// <summary>Joins the text of every non-call run.</summary>
    /// <param name="runs">The runs to join.</param>
    /// <returns>The concatenated text.</returns>
    private static string Text(List<EmbeddedToolCall> runs)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var run in runs)
        {
            if (!run.IsCall)
            {
                _ = builder.Append(run.Payload);
            }
        }

        return builder.ToString();
    }
}
