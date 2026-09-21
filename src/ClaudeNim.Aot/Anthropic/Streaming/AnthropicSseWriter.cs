// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ClaudeNim.Aot.Anthropic.Streaming;

/// <summary>Writes Anthropic server-sent events to a response body.</summary>
/// <param name="Output">The response body to write to.</param>
/// <remarks>
/// Each event is framed as <c>event: &lt;name&gt;</c> then <c>data: &lt;json&gt;</c> then a blank
/// line, and flushed immediately. A streamed turn is only useful if it reaches the client as it
/// is produced, so buffering a completed event would defeat the point of streaming at all.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("AnthropicSseWriter: {_buffer}")]
public sealed record AnthropicSseWriter(Stream Output)
{
    /// <summary>The initial size of the buffer.</summary>
    private const int InitialBufferBytes = 1024;

    /// <summary>The buffer each event is composed into before it reaches the stream.</summary>
    private readonly ArrayBufferWriter<byte> _buffer = new(InitialBufferBytes);

    /// <summary>Gets the byte sequence that begins an event line.</summary>
    private static ReadOnlySpan<byte> EventPrefix => "event: "u8;

    /// <summary>Gets the byte sequence that begins a data line.</summary>
    private static ReadOnlySpan<byte> DataPrefix => "\ndata: "u8;

    /// <summary>Gets the byte sequence that terminates an event.</summary>
    private static ReadOnlySpan<byte> EventSuffix => "\n\n"u8;

    /// <summary>Writes one event.</summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="eventName">The event name; see <see cref="StreamEventNames"/>.</param>
    /// <param name="payload">The payload serialized into the data line.</param>
    /// <param name="typeInfo">The source-generated contract for <typeparamref name="TPayload"/>.</param>
    /// <param name="cancellationToken">Aborts the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been flushed.</returns>
    public async ValueTask WriteAsync<TPayload>(
        string eventName,
        TPayload payload,
        JsonTypeInfo<TPayload> typeInfo,
        CancellationToken cancellationToken)
    {
        _buffer.Clear();
        _buffer.Write(EventPrefix);
        WriteEventName(eventName);
        _buffer.Write(DataPrefix);

        await using (var writer = new Utf8JsonWriter(_buffer))
        {
            JsonSerializer.Serialize(writer, payload, typeInfo);
        }

        _buffer.Write(EventSuffix);

        await Output.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await Output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // Transcodes straight into the output buffer. This runs once per streamed delta, so the
    // intermediate array a GetBytes overload would return is worth avoiding.
    /// <summary>Transcodes the event name into the buffer.</summary>
    /// <param name="eventName">The event name to transcode.</param>
    private void WriteEventName(string eventName)
    {
        var length = Encoding.UTF8.GetByteCount(eventName);
        var span = _buffer.GetSpan(length);
        _ = Encoding.UTF8.GetBytes(eventName, span);
        _buffer.Advance(length);
    }
}
