// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Codex.Streaming;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Translates subscription events as the caller reads them.</summary>
internal sealed class ChatGptResponseStream : Stream
{
    /// <summary>The upstream response owned by this stream.</summary>
    private readonly HttpResponseMessage _response;

    /// <summary>The upstream event reader.</summary>
    private readonly StreamReader _reader;

    /// <summary>The emitted tool slots.</summary>
    private readonly Dictionary<int, int> _toolSlots = [];

    /// <summary>The bytes waiting for the caller.</summary>
    private ReadOnlyMemory<byte> _pending;

    /// <summary>Whether a terminal event was received.</summary>
    private bool _finished;

    /// <summary>Initializes a new instance of the <see cref="ChatGptResponseStream"/> class.</summary>
    /// <param name="response">The owned upstream response.</param>
    /// <param name="stream">The upstream body.</param>
    internal ChatGptResponseStream(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        _reader = new(stream);
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        while (_pending.IsEmpty && !_finished)
        {
            var eventData = await ReadEventAsync(_reader, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The subscription stream ended before a terminal response.");
            var chunk = ChatGptResponseTranslator.Chunk(eventData, _toolSlots);
            _finished = eventData.Type is "response.completed" or "response.incomplete" or "response.failed" or "error";
            if (chunk is null)
            {
                continue;
            }

            var json = JsonSerializer.Serialize(chunk, ProxyJsonContext.Default.NimChatCompletionChunk);
            _pending = Encoding.UTF8.GetBytes($"data: {json}\n\n{(_finished ? "data: [DONE]\n\n" : string.Empty)}");
        }

        var count = Math.Min(buffer.Length, _pending.Length);
        _pending[..count].CopyTo(buffer);
        _pending = _pending[count..];
        return count;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Reads one complete SSE event.</summary>
    /// <param name="reader">The upstream reader.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The event, or null at the end of the stream.</returns>
    internal static async Task<ResponseStreamEvent?> ReadEventAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 && data.Length > 0)
            {
                return JsonSerializer.Deserialize(data.ToString(), ProxyJsonContext.Default.ResponseStreamEvent);
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                _ = data.Append(line.AsSpan("data:".Length).TrimStart()).Append('\n');
            }
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }
}
