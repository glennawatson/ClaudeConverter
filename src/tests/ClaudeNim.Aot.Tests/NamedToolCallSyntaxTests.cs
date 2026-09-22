// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers recovering a tool call written as nested tags with one tag per argument.</summary>
/// <remarks>
/// This is the shape the Nemotron chat templates instruct the model to produce, and the shape
/// DeepSeek V4.1 renders as DSML. Neither carries the arguments as JSON, so the JSON an Anthropic
/// <c>tool_use</c> block needs is assembled from the parameter tags. Both spellings are quoted
/// from the published templates.
/// </remarks>
public sealed class NamedToolCallSyntaxTests
{
    /// <summary>The tool name used across the fixtures.</summary>
    private const string ToolName = "search";

    /// <summary>A Nemotron-style call becomes a tool call with JSON arguments.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReadsANemotronCall()
    {
        const string Text = """
            <tool_call>
            <function=search>
            <parameter=query>
            rain tomorrow
            </parameter>
            </function>
            </tool_call>
            """;

        var runs = Run(Text);

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].Name).IsEqualTo(ToolName);
        await Assert.That(runs[0].Payload).IsEqualTo("""{"query":"rain tomorrow"}""");
    }

    /// <summary>A DeepSeek DSML call becomes a tool call with JSON arguments.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReadsADeepSeekCall()
    {
        const string Text = """
            <｜DSML｜ calls><｜DSML｜ invoke name="search"><｜DSML｜ parameter name="query" string="true">rain</｜DSML｜ parameter></｜DSML｜ invoke></｜DSML｜ calls>
            """;

        var runs = Run(Text);

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].Name).IsEqualTo(ToolName);
        await Assert.That(runs[0].Payload).IsEqualTo("""{"query":"rain"}""");
    }

    /// <summary>A parameter declared as non-string keeps the JSON kind it was written in.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// A tool schema that wants a number rejects the string "5", so the declared kind is honoured
    /// rather than everything being quoted.
    /// </remarks>
    [Test]
    public async Task HonoursADeclaredNonStringValue()
    {
        const string Text = """
            <｜DSML｜ calls><｜DSML｜ invoke name="search"><｜DSML｜ parameter name="limit" string="false">5</｜DSML｜ parameter></｜DSML｜ invoke></｜DSML｜ calls>
            """;

        await Assert.That(Run(Text)[0].Payload).IsEqualTo("""{"limit":5}""");
    }

    /// <summary>A value declared as a string stays a string even when it looks like a number.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task KeepsADeclaredStringValueQuoted()
    {
        const string Text = """
            <｜DSML｜ calls><｜DSML｜ invoke name="search"><｜DSML｜ parameter name="zip" string="true">90210</｜DSML｜ parameter></｜DSML｜ invoke></｜DSML｜ calls>
            """;

        await Assert.That(Run(Text)[0].Payload).IsEqualTo("""{"zip":"90210"}""");
    }

    /// <summary>Several arguments all reach the assembled JSON.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReadsEveryParameter()
    {
        const string Text = """
            <tool_call>
            <function=search>
            <parameter=query>
            rain
            </parameter>
            <parameter=limit>
            5
            </parameter>
            </function>
            </tool_call>
            """;

        await Assert.That(Run(Text)[0].Payload).IsEqualTo("""{"query":"rain","limit":5}""");
    }

    /// <summary>A multi-line value keeps its indentation.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// The template puts the value on its own line, so one break is removed at each end and no
    /// more — trimming outright would eat the indentation of the code a coding client carries.
    /// </remarks>
    [Test]
    public async Task KeepsTheIndentationOfAMultiLineValue()
    {
        const string Text = "<tool_call><function=write><parameter=body>\n    indented\nline two\n</parameter></function></tool_call>";

        await Assert.That(Run(Text)[0].Payload).IsEqualTo("""{"body":"    indented\nline two"}""");
    }

    /// <summary>A call split across chunks is still recovered whole.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RecoversACallSplitAcrossChunks()
    {
        var runs = Run("<tool_", "call><function=sea", "rch><parameter=query>rain</parameter>", "</function></tool_call>");

        await Assert.That(runs.Count).IsEqualTo(1);
        await Assert.That(runs[0].Name).IsEqualTo(ToolName);
    }

    /// <summary>Answer text around a call is kept, and the markers are not shown.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task KeepsSurroundingTextAndHidesTheMarkers()
    {
        var runs = Run("before <tool_call><function=search><parameter=query>rain</parameter></function></tool_call> after");

        await Assert.That(Text(runs)).IsEqualTo("before  after");
        await Assert.That(HasCall(runs)).IsTrue();
    }

    /// <summary>An unterminated call is released as the text the model actually produced.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReleasesAnUnterminatedCallAsText()
    {
        var runs = Run("<tool_call><function=search>");

        await Assert.That(HasCall(runs)).IsFalse();
    }

    /// <summary>Reports whether any run is a tool call.</summary>
    /// <param name="runs">The runs to inspect.</param>
    /// <returns><see langword="true"/> when at least one run is a call.</returns>
    private static bool HasCall(List<EmbeddedToolCall> runs)
    {
        foreach (var run in runs)
        {
            if (run.IsCall)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>Feeds a sequence of chunks through the streamed parser and flushes.</summary>
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
}
