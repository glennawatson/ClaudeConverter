// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the Anthropic-to-OpenAI tool choice translation.</summary>
/// <remarks>
/// The reference proxies send a constant <c>auto</c>, which silently discards forced tool use.
/// These tests pin each vocabulary difference that makes that a bug.
/// </remarks>
public sealed class ToolChoiceTranslatorTests
{
    /// <summary>Anthropic's <c>any</c> is OpenAI's <c>required</c>, not <c>auto</c>.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AnyBecomesRequired()
    {
        var translated = ToolChoiceTranslator.Translate(Parse("""{"type":"any"}"""));

        await Assert.That(translated?.GetString()).IsEqualTo("required");
    }

    /// <summary>A named tool becomes a nested function reference rather than being dropped.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NamedToolBecomesFunctionReference()
    {
        var translated = ToolChoiceTranslator.Translate(Parse("""{"type":"tool","name":"read_file"}"""));

        await Assert.That(translated.HasValue).IsTrue();
        await Assert.That(translated!.Value.GetProperty("type").GetString()).IsEqualTo("function");
        await Assert.That(translated.Value.GetProperty("function").GetProperty("name").GetString())
            .IsEqualTo("read_file");
    }

    /// <summary><c>none</c> must survive; forwarding <c>auto</c> would let the model call a tool.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NoneIsPreserved()
    {
        var translated = ToolChoiceTranslator.Translate(Parse("""{"type":"none"}"""));

        await Assert.That(translated?.GetString()).IsEqualTo("none");
    }

    /// <summary>A named tool with no name falls back to <c>auto</c> rather than producing invalid JSON.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NamedToolWithoutNameFallsBackToAuto()
    {
        var translated = ToolChoiceTranslator.Translate(Parse("""{"type":"tool"}"""));

        await Assert.That(translated?.GetString()).IsEqualTo("auto");
    }

    /// <summary>An absent choice stays absent, so the upstream applies its own default.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AbsentChoiceTranslatesToNothing() =>
        await Assert.That(ToolChoiceTranslator.Translate(null)).IsNull();

    /// <summary>The parallel-call suppression flag is read from the choice object.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ParallelSuppressionIsRead()
    {
        var choice = Parse("""{"type":"auto","disable_parallel_tool_use":true}""");

        await Assert.That(ToolChoiceTranslator.DisablesParallelToolCalls(choice)).IsTrue();
        await Assert.That(ToolChoiceTranslator.DisablesParallelToolCalls(Parse("""{"type":"auto"}""")))
            .IsFalse();
    }

    /// <summary>Parses a tool choice fragment.</summary>
    /// <param name="json">The fragment.</param>
    /// <returns>The parsed element.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonElement Parse(string json) => JsonElement.Parse(json);
}
