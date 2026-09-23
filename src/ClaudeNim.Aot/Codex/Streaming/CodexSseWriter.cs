// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.Text.Json;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Codex.Streaming;

/// <summary>Writes Responses API server-sent events to a response body.</summary>
/// <param name="Output">The response body to write to.</param>
/// <remarks>
/// Each event is one <c>data:</c> line carrying the event's JSON, discriminated by its own
/// <c>type</c> field, then a blank line — the Responses API sends no separate SSE <c>event:</c>
/// line the way Anthropic's does. The event is flushed immediately: a streamed turn is only useful
/// if it reaches the client as it is produced.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("CodexSseWriter: {_buffer}")]
public sealed record CodexSseWriter(Stream Output)
{
    /// <summary>The initial size of the buffer.</summary>
    private const int InitialBufferBytes = 1024;

    /// <summary>The buffer each event is composed into before it reaches the stream.</summary>
    private readonly ArrayBufferWriter<byte> _buffer = new(InitialBufferBytes);

    /// <summary>Gets the byte sequence that begins a data line.</summary>
    private static ReadOnlySpan<byte> DataPrefix => "data: "u8;

    /// <summary>Gets the byte sequence that terminates an event.</summary>
    private static ReadOnlySpan<byte> EventSuffix => "\n\n"u8;

    /// <summary>Writes one event.</summary>
    /// <param name="payload">The event to write.</param>
    /// <param name="cancellationToken">Aborts the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been flushed.</returns>
    public async ValueTask WriteAsync(ResponseStreamEvent payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _buffer.Clear();
        _buffer.Write(DataPrefix);

        await using (var writer = new Utf8JsonWriter(_buffer))
        {
            JsonSerializer.Serialize(writer, payload, ProxyJsonContext.Default.ResponseStreamEvent);
        }

        _buffer.Write(EventSuffix);

        await Output.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await Output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
