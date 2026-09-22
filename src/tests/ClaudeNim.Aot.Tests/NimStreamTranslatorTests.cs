// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Nvidia;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers turning a streamed NVIDIA NIM completion into the Anthropic event stream.</summary>
public sealed class NimStreamTranslatorTests
{
    /// <summary>The prompt size fed to every translation.</summary>
    private const int InputTokens = 5;

    /// <summary>The rendered content-block payload for a recovered tool named "search".</summary>
    private const string ToolNameField = "\"name\":\"search\"";

    /// <summary>The marker that ends an upstream SSE stream.</summary>
    private const string DoneEvent = "data: [DONE]\n\n";

    /// <summary>How long the fixtures let the translator wait between lines; never actually reached.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Plain answer text streamed in one delta becomes a text block.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task PlainAnswerTextBecomesATextBlock()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"content":"hello"}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("\"text\":\"hello\"");
    }

    /// <summary>A reasoning fragment becomes a thinking block, opened before the answer text.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReasoningThenAnswerProducesBothBlockTypes()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"reasoning_content":"thinking..."}}]}

            data: {"choices":[{"delta":{"content":"answer"}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("\"thinking\":\"thinking...\"");
        await Assert.That(body).Contains("\"text\":\"answer\"");
    }

    /// <summary>Reasoning is suppressed entirely when the resolved model has thinking disabled.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReasoningIsSuppressedWhenDisabled()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"reasoning_content":"thinking..."}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse, thinkingEnabled: false);

        await Assert.That(body).DoesNotContain("thinking...");
    }

    /// <summary>
    /// A tool call whose name arrives whole in the first fragment opens its block immediately, and
    /// argument fragments that follow are appended as JSON deltas onto that already-open block.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ToolCallOpensOnItsFirstNameFragmentAndAppendsArguments()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"search"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":1}"}}]}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains(ToolNameField);
        await Assert.That(body).Contains("call_1");
        await Assert.That(body).Contains("input_json_delta");
    }

    /// <summary>Arguments that arrive before the tool's name are held and replayed once it opens.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ArgumentsArrivingBeforeTheNameAreHeldAndReplayed()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":1}"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"search"}}]}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("content_block_start");
        await Assert.That(body).Contains(ToolNameField);
    }

    /// <summary>A tool call written into the answer as marker tokens is recovered as a tool-use block.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmbeddedToolCallInStreamedTextIsRecovered()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"content":"<|tool_call_begin|>search<|tool_call_argument_begin|>{}<|tool_call_end|>"}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains(ToolNameField);
    }

    /// <summary>A failure reported mid-stream ends the turn with an Anthropic error event.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task MidStreamFailureEndsTheTurnWithAnErrorEvent()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"content":"partial"}}]}

            data: {"error":{"message":"upstream died","code":503}}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("event: error");
        await Assert.That(body).Contains("upstream died");
        await Assert.That(body).DoesNotContain("message_stop");
    }

    /// <summary>A malformed chunk is skipped without ending the turn.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task MalformedChunkIsSkippedWithoutEndingTheTurn()
    {
        const string Sse = """
            data: {not json}

            data: {"choices":[{"delta":{"content":"still works"}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("still works");
        await Assert.That(body).Contains("message_stop");
    }

    /// <summary>A turn that produces nothing still yields a single placeholder text block.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EmptyTurnYieldsAPlaceholderBlock()
    {
        var body = await TranslateAsync(DoneEvent);

        await Assert.That(body).Contains("content_block_start");
        await Assert.That(body).Contains("content_block_stop");
    }

    /// <summary>Usage reported on the final chunk is forwarded verbatim in the closing event.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReportedUsageIsForwardedInTheClosingEvent()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"content":"hi"}}]}

            data: {"usage":{"prompt_tokens":7,"completion_tokens":3,"total_tokens":10}}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("\"output_tokens\":3");
    }

    /// <summary>A keep-alive comment line and a blank line are both ignored.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task KeepAliveAndBlankLinesAreIgnored()
    {
        const string Sse = """
            : keep-alive


            data: {"choices":[{"delta":{"content":"hi"}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse);

        await Assert.That(body).Contains("\"text\":\"hi\"");
    }

    /// <summary>A seeded reasoning block streamed in fragments reaches the client as thinking, not answer text.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// NVIDIA documents this shape: without the reasoning parser configured upstream, "the
    /// reasoning text and the <c>&lt;/think&gt;</c> marker appear in content instead". The GLM 5.3
    /// template seeds the opening tag unconditionally, so the completion carries only the close.
    /// </remarks>
    [Test]
    public async Task SeededReasoningInContentBecomesAThinkingBlock()
    {
        const string Sse = """
            data: {"choices":[{"delta":{"content":"weighing "}}]}

            data: {"choices":[{"delta":{"content":"it up</think>the answer"}}]}

            data: [DONE]

            """;

        var body = await TranslateAsync(Sse, reasoningMayBeSeeded: true);

        await Assert.That(body).Contains("\"thinking\":\"weighing it up\"");
        await Assert.That(body).Contains("\"text\":\"the answer\"");
        await Assert.That(body).DoesNotContain("</think>");
    }

    /// <summary>Runs the translator over a raw SSE body and returns the framed Anthropic output as text.</summary>
    /// <param name="upstreamSse">The upstream server-sent event body to feed the translator.</param>
    /// <param name="thinkingEnabled">Whether reasoning should be forwarded.</param>
    /// <param name="reasoningMayBeSeeded">Whether the chat template may have opened the reasoning block itself.</param>
    /// <returns>The UTF-8 text written to the Anthropic response body.</returns>
    private static async Task<string> TranslateAsync(
        string upstreamSse,
        bool thinkingEnabled = true,
        bool reasoningMayBeSeeded = false)
    {
        await using var output = new MemoryStream();
        var writer = new AnthropicSseWriter(output);
        var translator = new NimStreamTranslator(
            writer,
            thinkingEnabled,
            reasoningMayBeSeeded,
            IdleTimeout,
            NullLogger.Instance);

        await using var upstream = new MemoryStream(Encoding.UTF8.GetBytes(upstreamSse));
        await translator.TranslateAsync(upstream, "msg_1", "claude-sonnet-5", InputTokens, CancellationToken.None);

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
