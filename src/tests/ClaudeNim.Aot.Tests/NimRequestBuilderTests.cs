// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

    /// <summary>A model identifier the catalogue states supports image input.</summary>
    private const string VisionModel = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";

    /// <summary>The Claude model name used across the fixture requests.</summary>
    private const string ClaudeModel = "claude-sonnet-5";

    /// <summary>The user text used by the minimal fixture requests.</summary>
    private const string UserText = "hello";

    /// <summary>The structured-output discriminator, and the member the schema nests under.</summary>
    private const string JsonSchema = "json_schema";

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
    /// Nemotron 3 Ultra's runner rejects a thinking-token budget outright. Deriving one from
    /// <c>max_tokens</c> made every reasoning turn come back empty, and on a streamed turn the
    /// failure arrived inside a 200 response where no status-code retry could catch it. That is
    /// the regression this test exists to prevent.
    /// </remarks>
    [Test]
    public async Task ReasoningBudgetIsNotDerivedFromMaxTokens()
    {
        var built = NimRequestBuilder.Build(Request(), UpstreamModel, true, Options);

        await Assert.That(built.Extensions).IsNull();
    }

    /// <summary>A reasoning budget the caller asked for is forwarded.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ExplicitReasoningBudgetIsForwarded()
    {
        var thinking = new ThinkingConfig("enabled", BudgetTokens: ExplicitReasoningBudget);
        var built = NimRequestBuilder.Build(Request(thinking: thinking), UpstreamModel, true, Options);

        await Assert.That(built.Extensions?.MaxThinkingTokens).IsEqualTo(ExplicitReasoningBudget);
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
            ClaudeModel,
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromText("hello"))],
            OversizedMaxTokens);

        var bounded = Options with { MaxTokens = ConfiguredCeiling };
        var built = NimRequestBuilder.Build(request, UpstreamModel, false, bounded);

        await Assert.That(built.MaxTokens).IsEqualTo(ConfiguredCeiling);
    }

    /// <summary>An image is forwarded to a model the catalogue states can see it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The listing tells a client a vision model accepts images; the request actually sent has to
    /// agree, or the capability advertised and the capability delivered quietly diverge.
    /// </remarks>
    [Test]
    public async Task ImageIsForwardedToAVisionModel()
    {
        var request = RequestWithImage();

        var built = NimRequestBuilder.Build(request, VisionModel, false, Options);

        var message = UserMessage(built);
        await Assert.That(message.Content?.IsText).IsFalse();

        var parts = message.Content!.Value.Parts!;
        await Assert.That(parts.Exists(static p => p.Type == NimContentPart.ImageType)).IsTrue();
    }

    /// <summary>An image is replaced with a placeholder for a model the catalogue states cannot see it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ImageIsPlaceholderedForATextOnlyModel()
    {
        var request = RequestWithImage();

        var built = NimRequestBuilder.Build(request, UpstreamModel, false, Options);

        var message = UserMessage(built);
        await Assert.That(message.Content?.IsText).IsTrue();
        await Assert.That(message.Content?.Text).Contains("[Image]");
    }

    /// <summary>A document is always replaced with a placeholder; no NIM model accepts one.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task DocumentIsAlwaysPlaceholdered()
    {
        List<ContentBlock> blocks =
        [
            ContentBlock.ForText("Summarise this."),
            new(ContentBlockTypes.Document, Source: new ImageSource("base64", "application/pdf", "Zm9v")),
        ];
        var request = new MessagesRequest(
            ClaudeModel,
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromBlocks(blocks))],
            RequestedMaxTokens);

        var built = NimRequestBuilder.Build(request, VisionModel, false, Options);

        var message = UserMessage(built);
        await Assert.That(message.Content?.IsText).IsTrue();
        await Assert.That(message.Content?.Text).Contains("[Document]");
    }

    /// <summary>A structured-output request reaches NIM in the nested OpenAI <c>response_format</c> shape.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task StructuredOutputIsTranslatedToResponseFormat()
    {
        var request = RequestWithFormat();

        var built = NimRequestBuilder.Build(request, UpstreamModel, false, Options);

        var format = built.ResponseFormat;
        await Assert.That(format).IsNotNull();
        await Assert.That(format!.Value.GetProperty("type").GetString()).IsEqualTo(JsonSchema);
        await Assert.That(format.Value.GetProperty(JsonSchema).GetProperty("schema").GetProperty("type").GetString())
            .IsEqualTo("object");
    }

    /// <summary>A structured-output request names its schema, which NIM requires.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is a regression test for a live defect: the schema was forwarded on its own, and NIM
    /// rejected every structured-output turn with <c>missing field `name`</c> before the model was
    /// reached. Claude Code asks for structured output on its internal evaluator calls, so the
    /// failure surfaced as hook and goal checks erroring out rather than as a model problem.
    /// </remarks>
    [Test]
    public async Task StructuredOutputCarriesASchemaName()
    {
        var built = NimRequestBuilder.Build(RequestWithFormat(), UpstreamModel, false, Options);

        var schema = built.ResponseFormat!.Value.GetProperty(JsonSchema);
        await Assert.That(schema.TryGetProperty("name", out var name)).IsTrue();
        await Assert.That(name.GetString()).IsNotNullOrEmpty();
    }

    /// <summary>A request with no output format carries no <c>response_format</c>.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AbsentOutputFormatSendsNoResponseFormat() =>
        await Assert.That(NimRequestBuilder.Build(Request(), UpstreamModel, false, Options).ResponseFormat)
            .IsNull();

    /// <summary>Finds the single user message in an upstream request.</summary>
    /// <param name="request">The upstream request.</param>
    /// <returns>The user message.</returns>
    /// <exception cref="InvalidOperationException">The request carried no user message.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NimChatMessage UserMessage(NimChatRequest request) =>
        request.Messages.Single(static m => string.Equals(m.Role, NimChatMessage.UserRole, StringComparison.Ordinal));

    /// <summary>Builds a request whose user turn carries an image alongside text.</summary>
    /// <returns>The request.</returns>
    private static MessagesRequest RequestWithImage()
    {
        List<ContentBlock> blocks =
        [
            ContentBlock.ForText("What is this?"),
            new(ContentBlockTypes.Image, Source: new ImageSource("base64", "image/png", "Zm9v")),
        ];

        return new(
            ClaudeModel,
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromBlocks(blocks))],
            RequestedMaxTokens);
    }

    /// <summary>Builds a minimal request.</summary>
    /// <param name="streaming">Whether the turn streams.</param>
    /// <param name="thinking">The caller's thinking configuration.</param>
    /// <returns>The Anthropic request.</returns>
    private static MessagesRequest Request(bool streaming = false, ThinkingConfig? thinking = null) =>
        new(
            ClaudeModel,
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromText(UserText))],
            RequestedMaxTokens,
            Stream: streaming,
            Thinking: thinking);

    /// <summary>Builds a request asking for a structured JSON output.</summary>
    /// <returns>The Anthropic request.</returns>
    private static MessagesRequest RequestWithFormat() =>
        new(
            ClaudeModel,
            [new AnthropicMessage(AnthropicMessage.UserRole, MessageContent.FromText(UserText))],
            RequestedMaxTokens,
            OutputConfig: new OutputConfig(
                Format: new OutputFormat(
                    Schema: JsonSerializer.SerializeToElement(new { type = "object" }))));
}
