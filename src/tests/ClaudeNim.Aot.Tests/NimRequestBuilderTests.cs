// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the translation of an Anthropic request into the NIM request that serves it.</summary>
public sealed class NimRequestBuilderTests
{
    /// <summary>The output ceiling a fixture request asks for.</summary>
    private const int RequestedMaxTokens = 1024;

    /// <summary>The reasoning ceiling used where a caller supplies one explicitly.</summary>
    private const int ExplicitReasoningBudget = 2048;

    /// <summary>An output ceiling far beyond anything the configuration allows.</summary>
    private const int OversizedMaxTokens = 999_999;

    /// <summary>The configured output ceiling used by the bounding case.</summary>
    private const int ConfiguredCeiling = 4096;

    /// <summary>The model identifier passed through to the upstream request.</summary>
    private const string UpstreamModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The settings a request is built against.</summary>
    private static readonly NvidiaNimOptions Options = new();

    /// <summary>A streamed turn asks for usage, so token counts do not have to be guessed.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StreamedTurnRequestsUsage()
    {
        var built = NimRequestBuilder.Build(Request(streaming: true), UpstreamModel, false, Options);

        await Assert.That(built.Stream).IsTrue();
        await Assert.That(built.StreamOptions?.IncludeUsage).IsTrue();
    }

    /// <summary>A non-streamed turn does not carry stream options.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NonStreamedTurnOmitsStreamOptions() =>
        await Assert.That(NimRequestBuilder.Build(Request(), UpstreamModel, false, Options).StreamOptions)
            .IsNull();

    /// <summary>A reasoning budget is never invented from the output ceiling.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    /// <remarks>
    /// Nemotron 3 Ultra's runner rejects <c>reasoning_budget</c> outright, and on a streamed turn
    /// does so inside a 200 response. Deriving it from <c>max_tokens</c> made every reasoning turn
    /// come back empty, which is the regression this test exists to prevent.
    /// </remarks>
    [Test]
    public async Task ReasoningBudgetIsNotDerivedFromMaxTokens()
    {
        var built = NimRequestBuilder.Build(Request(), UpstreamModel, true, Options);

        await Assert.That(built.ChatTemplateKwargs?.ReasoningBudget).IsNull();
    }

    /// <summary>A reasoning budget the caller asked for is forwarded.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ExplicitReasoningBudgetIsForwarded()
    {
        var thinking = new ThinkingConfig("enabled", BudgetTokens: ExplicitReasoningBudget);
        var built = NimRequestBuilder.Build(Request(thinking: thinking), UpstreamModel, true, Options);

        await Assert.That(built.ChatTemplateKwargs?.ReasoningBudget).IsEqualTo(ExplicitReasoningBudget);
    }

    /// <summary>Reasoning control is a top-level member, not nested under an envelope.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReasoningIsRequestedAtTheTopLevel()
    {
        var built = NimRequestBuilder.Build(Request(), UpstreamModel, true, Options);

        await Assert.That(built.ChatTemplateKwargs?.EnableThinking).IsTrue();
        await Assert.That(built.ChatTemplateKwargs?.Thinking).IsTrue();
    }

    /// <summary>A turn with neither reasoning nor tools sends no template arguments at all.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task PlainTurnSendsNoTemplateArguments() =>
        await Assert.That(NimRequestBuilder.Build(Request(), UpstreamModel, false, Options).ChatTemplateKwargs)
            .IsNull();

    /// <summary>The configured ceiling bounds an oversized request.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task OutputCeilingIsApplied()
    {
        var request = new MessagesRequest(
            "claude-sonnet-5",
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromText("hello"))],
            OversizedMaxTokens);

        var bounded = Options with { MaxTokens = ConfiguredCeiling };
        var built = NimRequestBuilder.Build(request, UpstreamModel, false, bounded);

        await Assert.That(built.MaxTokens).IsEqualTo(ConfiguredCeiling);
    }

    /// <summary>Builds a minimal request.</summary>
    /// <param name="streaming">Whether the turn streams.</param>
    /// <param name="thinking">The caller's thinking configuration.</param>
    /// <returns>The Anthropic request.</returns>
    private static MessagesRequest Request(bool streaming = false, ThinkingConfig? thinking = null) =>
        new(
            "claude-sonnet-5",
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromText("hello"))],
            RequestedMaxTokens,
            Stream: streaming,
            Thinking: thinking);
}
