// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the approximate token accounting used for progress display and local answers.</summary>
public sealed class TokenEstimatorTests
{
    /// <summary>The identifier a tool-use fixture block is given.</summary>
    private const string ToolCallId = "toolu_1";

    /// <summary>A message length long enough for the per-character estimate to be measurable.</summary>
    private const int LongMessageLength = 400;

    /// <summary>The token floor a message that long is expected to clear.</summary>
    private const int LongMessageTokenFloor = 90;

    /// <summary>An image payload large enough to exceed the minimum image token cost.</summary>
    private const int LargeImageDataLength = 4000;

    /// <summary>NVIDIA's default token cost for an image with no measurable size.</summary>
    private const int DefaultImageTokens = 765;

    /// <summary>The floor applied to a tiny image.</summary>
    private const int MinimumImageTokens = 85;

    /// <summary>An empty conversation still estimates at least one token.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyConversationEstimatesAtLeastOneToken() =>
        await Assert.That(TokenEstimator.Estimate([], null, null)).IsGreaterThanOrEqualTo(1);

    /// <summary>A system prompt adds to the estimate.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SystemPromptAddsToTheEstimate()
    {
        var without = TokenEstimator.Estimate([], null, null);
        var with = TokenEstimator.Estimate([], MessageContent.FromText("You are a careful assistant."), null);

        await Assert.That(with).IsGreaterThan(without);
    }

    /// <summary>A text message contributes its character length to the estimate.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TextMessageContributesToTheEstimate()
    {
        List<AnthropicMessage> messages = [new("user", MessageContent.FromText(new('a', LongMessageLength)))];

        await Assert.That(TokenEstimator.Estimate(messages, null, null)).IsGreaterThan(LongMessageTokenFloor);
    }

    /// <summary>A block-form message walks every block kind.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task BlockFormMessageWalksEveryBlockKind()
    {
        List<ContentBlock> blocks =
        [
            ContentBlock.ForText("hello"),
            new(ContentBlockTypes.Thinking, Thinking: "reasoning about it"),
            new(ContentBlockTypes.RedactedThinking, Data: "opaque"),
            new(ContentBlockTypes.ToolUse, Id: ToolCallId, Name: "search", Input: JsonElements.EmptyObject),
            new(ContentBlockTypes.ToolResult, ToolUseId: ToolCallId, Content: JsonElements.ParseOrEmpty("\"result\"")),
            new(ContentBlockTypes.Image, Source: new("base64", "image/png", new('A', LargeImageDataLength))),
            new("unknown_block_type"),
        ];
        List<AnthropicMessage> messages = [new("assistant", MessageContent.FromBlocks(blocks))];

        await Assert.That(TokenEstimator.Estimate(messages, null, null)).IsGreaterThan(0);
    }

    /// <summary>A tool definition adds its name, description and schema to the estimate.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ToolDefinitionAddsToTheEstimate()
    {
        var without = TokenEstimator.Estimate([], null, null);
        List<ToolDefinition> tools = [new("search", "Searches the web", JsonElements.EmptyObjectSchema)];
        var with = TokenEstimator.Estimate([], null, tools);

        await Assert.That(with).IsGreaterThan(without);
    }

    /// <summary>An image with no data estimates the flat default cost.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ImageWithNoDataUsesTheDefaultCost()
    {
        List<ContentBlock> blocks = [new(ContentBlockTypes.Image, Source: null)];
        List<AnthropicMessage> messages = [new("user", MessageContent.FromBlocks(blocks))];

        await Assert.That(TokenEstimator.Estimate(messages, null, null)).IsGreaterThanOrEqualTo(DefaultImageTokens);
    }

    /// <summary>A tiny image floors at the minimum image token cost.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TinyImageFloorsAtTheMinimum()
    {
        List<ContentBlock> blocks = [new(ContentBlockTypes.Image, Source: new("base64", "image/png", "AA=="))];
        List<AnthropicMessage> messages = [new("user", MessageContent.FromBlocks(blocks))];

        await Assert.That(TokenEstimator.Estimate(messages, null, null)).IsGreaterThanOrEqualTo(MinimumImageTokens);
    }

    /// <summary>A null or empty text estimates zero tokens.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NullOrEmptyTextEstimatesZero()
    {
        await Assert.That(TokenEstimator.FromText(null)).IsEqualTo(0);
        await Assert.That(TokenEstimator.FromText(string.Empty)).IsEqualTo(0);
    }

    /// <summary>A non-positive character count estimates zero tokens.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NonPositiveLengthEstimatesZero() =>
        await Assert.That(TokenEstimator.FromLength(0)).IsEqualTo(0);

    /// <summary>A message passed as null throws.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NullMessagesThrows() =>
        await Assert.That(static () => TokenEstimator.Estimate(null!, null, null)).Throws<ArgumentNullException>();

    /// <summary>A tool result rendered as a text array contributes its concatenated text.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ToolResultTextArrayContributesToTheEstimate()
    {
        using var document = JsonDocument.Parse("""[{"type":"text","text":"one"},{"type":"text","text":"two"}]""");
        List<ContentBlock> blocks = [new(ContentBlockTypes.ToolResult, ToolUseId: ToolCallId, Content: document.RootElement.Clone())];
        List<AnthropicMessage> messages = [new("user", MessageContent.FromBlocks(blocks))];

        await Assert.That(TokenEstimator.Estimate(messages, null, null)).IsGreaterThan(0);
    }
}
