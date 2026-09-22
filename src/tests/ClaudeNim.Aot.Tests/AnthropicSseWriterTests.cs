// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers framing one Anthropic server-sent event onto a response body.</summary>
public sealed class AnthropicSseWriterTests
{
    /// <summary>The number of events expected once two have been written in order.</summary>
    private const int TwoEvents = 2;

    /// <summary>A written event is framed as <c>event:</c>, <c>data:</c>, then a blank line.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task EventIsFramedWithTheDocumentedShape()
    {
        await using var body = new MemoryStream();
        var writer = new AnthropicSseWriter(body);

        await writer.WriteAsync(
            StreamEventNames.MessageStop,
            StreamMessageStop.Instance,
            ProxyJsonContext.Default.StreamMessageStop,
            CancellationToken.None);

        var framed = Encoding.UTF8.GetString(body.ToArray());

        await Assert.That(framed.StartsWith("event: message_stop\ndata: ", StringComparison.Ordinal)).IsTrue();
        await Assert.That(framed.EndsWith("\n\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(framed).Contains("\"type\":\"message_stop\"");
    }

    /// <summary>Each event flushes so it reaches the client without waiting for the next one.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task WrittenBytesAreVisibleImmediately()
    {
        await using var body = new MemoryStream();
        var writer = new AnthropicSseWriter(body);

        await writer.WriteAsync(
            StreamEventNames.MessageStop,
            StreamMessageStop.Instance,
            ProxyJsonContext.Default.StreamMessageStop,
            CancellationToken.None);

        await Assert.That(body.Length).IsGreaterThan(0);
    }

    /// <summary>Two consecutive events are each framed and appended in order.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ConsecutiveEventsAreBothFramedInOrder()
    {
        await using var body = new MemoryStream();
        var writer = new AnthropicSseWriter(body);

        await writer.WriteAsync(
            StreamEventNames.Ping,
            StreamMessageStop.Instance,
            ProxyJsonContext.Default.StreamMessageStop,
            CancellationToken.None);
        await writer.WriteAsync(
            StreamEventNames.MessageStop,
            StreamMessageStop.Instance,
            ProxyJsonContext.Default.StreamMessageStop,
            CancellationToken.None);

        var framed = Encoding.UTF8.GetString(body.ToArray());
        var events = framed.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(events.Length).IsEqualTo(TwoEvents);
        await Assert.That(events[0].StartsWith("event: ping", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events[1].StartsWith("event: message_stop", StringComparison.Ordinal)).IsTrue();
    }
}
