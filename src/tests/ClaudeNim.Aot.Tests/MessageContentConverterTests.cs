// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers reading and writing <see cref="MessageContent"/>'s bare-string-or-blocks union.</summary>
public sealed class MessageContentConverterTests
{
    /// <summary>A bare JSON string round-trips as the text form.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BareStringRoundTripsAsText()
    {
        var content = JsonSerializer.Deserialize("\"hello\"", ProxyJsonContext.Default.MessageContent);

        await Assert.That(content.IsText).IsTrue();
        await Assert.That(content.Text).IsEqualTo("hello");

        var written = JsonSerializer.Serialize(content, ProxyJsonContext.Default.MessageContent);
        await Assert.That(written).IsEqualTo("\"hello\"");
    }

    /// <summary>A JSON array of blocks round-trips as the block form.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BlockArrayRoundTripsAsBlocks()
    {
        var content = JsonSerializer.Deserialize("""[{"type":"text","text":"hi"}]""", ProxyJsonContext.Default.MessageContent);

        await Assert.That(content.IsText).IsFalse();
        await Assert.That(content.Blocks!.Count).IsEqualTo(1);
        await Assert.That(content.Blocks[0].Text).IsEqualTo("hi");

        var written = JsonSerializer.Serialize(content, ProxyJsonContext.Default.MessageContent);
        await Assert.That(written).Contains("\"text\":\"hi\"");
    }

    /// <summary>A JSON null round-trips as the default, empty content value.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task NullRoundTripsAsDefault()
    {
        var content = JsonSerializer.Deserialize("null", ProxyJsonContext.Default.MessageContent);

        await Assert.That(content.IsText).IsFalse();
        await Assert.That(content.Blocks).IsNull();

        var written = JsonSerializer.Serialize(content, ProxyJsonContext.Default.MessageContent);
        await Assert.That(written).IsEqualTo("null");
    }

    /// <summary>A payload that is neither a string, array nor null is rejected.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task InvalidShapeThrows() =>
        await Assert.That(static () => JsonSerializer.Deserialize("42", ProxyJsonContext.Default.MessageContent))
            .Throws<JsonException>();
}
