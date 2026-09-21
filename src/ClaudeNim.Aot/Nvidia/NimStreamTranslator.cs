// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Serialization;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Turns a streamed NVIDIA NIM completion into the Anthropic event stream.</summary>
/// <param name="writer">The writer the Anthropic events are emitted through.</param>
/// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
/// <param name="logger">The diagnostic log.</param>
/// <remarks>
/// <para>
/// The two protocols disagree about structure. NIM streams flat deltas that may carry answer
/// text, a reasoning trace and tool-call fragments in any order; Anthropic streams indexed
/// content blocks that must be opened before they are appended to and closed before another
/// opens. This type owns that bookkeeping.
/// </para>
/// <para>
/// One instance serves exactly one response.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimStreamTranslator: {_thinkParser}")]
public sealed class NimStreamTranslator(AnthropicSseWriter writer, bool thinkingEnabled, ILogger logger)
{
    /// <summary>The marker every server-sent event payload line begins with.</summary>
    private const string DataPrefix = "data:";

    /// <summary>The payload the upstream sends in place of a chunk once the turn is over.</summary>
    private const string DoneMarker = "[DONE]";

    /// <summary>The index a block field holds while no block of that kind is open.</summary>
    private const int Closed = -1;

    /// <summary>The longest run of an unreadable payload that is written to the log.</summary>
    private const int LoggedPayloadLength = 400;

    /// <summary>The splitter that lifts inline reasoning tags out of streamed answer text.</summary>
    private readonly ThinkTagParser _thinkParser = new();

    /// <summary>The scratch list each parsed run of text is split into, reused across deltas.</summary>
    private readonly List<ThinkTagSegment> _segments = [];

    /// <summary>The per-slot tool call state, keyed by the index the upstream assigns.</summary>
    private readonly Dictionary<int, ToolBlockState> _tools = [];

    /// <summary>The number of chunks that were read successfully.</summary>
    private int _chunksRead;

    /// <summary>The number of chunks that could not be read.</summary>
    private int _chunksSkipped;

    /// <summary>The index the next content block to open will be given.</summary>
    private int _nextBlockIndex;

    /// <summary>The index of the open text block, or <see cref="Closed"/> when none is open.</summary>
    private int _textIndex = Closed;

    /// <summary>The index of the open thinking block, or <see cref="Closed"/> when none is open.</summary>
    private int _thinkingIndex = Closed;

    /// <summary>The characters of answer text emitted so far, used only to estimate usage.</summary>
    private int _textLength;

    /// <summary>The characters of reasoning emitted so far, used only to estimate usage.</summary>
    private int _reasoningLength;

    /// <summary>The last finish reason the upstream reported, before translation.</summary>
    private string? _finishReason;

    /// <summary>The usage the upstream reported, absent until it sends the final chunk.</summary>
    private NimUsage? _usage;

    /// <summary>Reads the upstream stream to completion, emitting Anthropic events as it goes.</summary>
    /// <param name="upstream">The NIM response body.</param>
    /// <param name="messageId">The identifier to report for the Anthropic message.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="inputTokens">The prompt size to report.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the final event has been written.</returns>
    public async ValueTask TranslateAsync(
        Stream upstream,
        string messageId,
        string model,
        int inputTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upstream);

        await WriteMessageStartAsync(messageId, model, inputTokens, cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(upstream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var chunk = ParseChunk(line);
            if (chunk is null)
            {
                continue;
            }

            // A failure after the status code was committed arrives here rather than as a status.
            // It ends the turn: continuing would emit an empty message the client reads as the
            // model having nothing to say.
            if (chunk.Error is { } failure)
            {
                await WriteErrorAsync(failure, cancellationToken).ConfigureAwait(false);
                NvidiaLog.StreamFailed(logger, failure.Message ?? string.Empty);
                return;
            }

            await ConsumeChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
        }

        await FlushAsync(inputTokens, cancellationToken).ConfigureAwait(false);
        NvidiaLog.StreamCompleted(logger, _chunksRead, _chunksSkipped);
    }

    /// <summary>Writes the event that reports an upstream failure to the client.</summary>
    /// <param name="failure">The failure the upstream reported.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    private ValueTask WriteErrorAsync(NimStreamError failure, CancellationToken cancellationToken)
    {
        var detail = ErrorResponse.Create(
            StreamErrorTypes.FromUpstream(failure.Code),
            failure.Message ?? "The upstream ended the response with an unspecified failure.");

        return writer.WriteAsync(
            StreamEventNames.Error,
            detail,
            ProxyJsonContext.Default.ErrorResponse,
            cancellationToken);
    }

    // Returns null for a keep-alive, a comment, the terminator, or a payload that does not parse.
    // A malformed chunk must not abort a turn that is otherwise progressing.
    /// <summary>Reads one upstream line into a chunk.</summary>
    /// <param name="line">The raw line read from the upstream body.</param>
    /// <returns>The parsed chunk, or <see langword="null"/> when the line carries no payload.</returns>
    private NimChatCompletionChunk? ParseChunk(string line)
    {
        if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var payload = line[DataPrefix.Length..].Trim();
        if (payload.Length == 0 || string.Equals(payload, DoneMarker, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var chunk = JsonSerializer.Deserialize(payload, ProxyJsonContext.Default.NimChatCompletionChunk);
            _chunksRead++;
            return chunk;
        }
        catch (JsonException error)
        {
            _chunksSkipped++;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var logged = payload.Length <= LoggedPayloadLength
                    ? payload
                    : payload[..LoggedPayloadLength];

                NvidiaLog.StreamChunkUnreadable(logger, logged, error);
            }

            return null;
        }
    }

    /// <summary>Writes the opening event that declares the message and its prompt size.</summary>
    /// <param name="messageId">The identifier to report for the Anthropic message.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="inputTokens">The prompt size to report.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteMessageStartAsync(
        string messageId,
        string model,
        int inputTokens,
        CancellationToken cancellationToken) =>
        writer.WriteAsync(
            StreamEventNames.MessageStart,
            new(new StreamMessage(messageId, model, new TokenUsage(inputTokens, 0), [])),
            ProxyJsonContext.Default.StreamMessageStart,
            cancellationToken);

    /// <summary>Dispatches one upstream chunk onto the matching content blocks.</summary>
    /// <param name="chunk">The chunk to consume.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the chunk has been translated.</returns>
    private async ValueTask ConsumeChunkAsync(NimChatCompletionChunk chunk, CancellationToken cancellationToken)
    {
        if (chunk.Usage is { } usage)
        {
            _usage = usage;
        }

        if (chunk.Choices is not { Count: > 0 } choices)
        {
            return;
        }

        var choice = choices[0];
        if (choice.FinishReason is { Length: > 0 } finishReason)
        {
            _finishReason = finishReason;
        }

        if (choice.Delta is not { } delta)
        {
            return;
        }

        if (delta.ReasoningContent is { Length: > 0 } reasoning)
        {
            await AppendReasoningAsync(reasoning, cancellationToken).ConfigureAwait(false);
        }

        if (delta.Content is { Length: > 0 } content)
        {
            await AppendContentAsync(content, cancellationToken).ConfigureAwait(false);
        }

        if (delta.ToolCalls is { Count: > 0 } toolCalls)
        {
            await AppendToolCallsAsync(toolCalls, cancellationToken).ConfigureAwait(false);
        }
    }

    // Answer text from GLM-style models arrives with the reasoning trace inline, so it is split
    // before any of it reaches a content block.
    /// <summary>Appends a run of answer text, separating any reasoning wrapped inside it.</summary>
    /// <param name="content">The text the upstream produced.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the text has been emitted.</returns>
    private async ValueTask AppendContentAsync(string content, CancellationToken cancellationToken)
    {
        _segments.Clear();
        _thinkParser.Feed(content, _segments);
        await EmitSegmentsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Emits the pending split segments, each onto the block its kind belongs to.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once every segment has been emitted.</returns>
    private async ValueTask EmitSegmentsAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.IsThinking)
            {
                await AppendReasoningAsync(segment.Text, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await AppendTextAsync(segment.Text, cancellationToken).ConfigureAwait(false);
        }

        _segments.Clear();
    }

    /// <summary>Appends a run of reasoning, opening a thinking block when one is not already open.</summary>
    /// <param name="reasoning">The reasoning the upstream produced.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the reasoning has been emitted, or immediately when it is suppressed.</returns>
    private async ValueTask AppendReasoningAsync(string reasoning, CancellationToken cancellationToken)
    {
        if (!thinkingEnabled)
        {
            return;
        }

        await OpenThinkingAsync(cancellationToken).ConfigureAwait(false);
        _reasoningLength += reasoning.Length;

        await WriteDeltaAsync(_thinkingIndex, StreamDelta.ForThinking(reasoning), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Appends a run of answer text, opening a text block when one is not already open.</summary>
    /// <param name="text">The text to emit.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the text has been emitted.</returns>
    private async ValueTask AppendTextAsync(string text, CancellationToken cancellationToken)
    {
        await OpenTextAsync(cancellationToken).ConfigureAwait(false);
        _textLength += text.Length;

        await WriteDeltaAsync(_textIndex, StreamDelta.ForText(text), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appends a batch of tool-call fragments, closing any block they may not interleave with.</summary>
    /// <param name="toolCalls">The fragments the upstream produced.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once every fragment has been applied.</returns>
    private async ValueTask AppendToolCallsAsync(
        List<NimToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        // Anthropic does not allow a tool block to interleave with text or reasoning.
        await CloseTextAsync(cancellationToken).ConfigureAwait(false);
        await CloseThinkingAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < toolCalls.Count; i++)
        {
            await AppendToolCallAsync(toolCalls[i], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Applies one tool-call fragment to the slot it belongs to.</summary>
    /// <param name="call">The fragment to apply.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the fragment has been applied.</returns>
    private async ValueTask AppendToolCallAsync(NimToolCall call, CancellationToken cancellationToken)
    {
        var state = StateFor(call.Index < 0 ? _tools.Count : call.Index);

        if (call.Id is { Length: > 0 } id)
        {
            state.Id = id;
        }

        if (call.Function?.Name is { Length: > 0 } name)
        {
            state.MergeName(name);
        }

        await StartToolBlockAsync(state, cancellationToken).ConfigureAwait(false);
        await AppendToolArgumentsAsync(state, call.Function?.Arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the state tracking a tool slot, creating it the first time the slot is seen.</summary>
    /// <param name="slot">The index the upstream assigned to the tool call.</param>
    /// <returns>The state for that slot.</returns>
    private ToolBlockState StateFor(int slot)
    {
        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(_tools, slot, out _);
        return state ??= new ToolBlockState();
    }

    /// <summary>Opens the content block for a tool call once its name is known.</summary>
    /// <param name="state">The state of the tool call.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the block has been opened, or immediately when it cannot be yet.</returns>
    private async ValueTask StartToolBlockAsync(ToolBlockState state, CancellationToken cancellationToken)
    {
        if (state.Started || state.Name.Length == 0)
        {
            return;
        }

        state.BlockIndex = _nextBlockIndex++;
        state.Started = true;
        state.Id ??= $"toolu_{Guid.NewGuid():N}";

        await WriteBlockStartAsync(
            state.BlockIndex,
            new(ContentBlockTypes.ToolUse, Id: state.Id, Name: state.Name, Input: JsonElements.EmptyObject),
            cancellationToken).ConfigureAwait(false);

        if (state.PendingArguments.Length == 0)
        {
            return;
        }

        var pending = state.PendingArguments;
        state.PendingArguments = string.Empty;
        await EmitToolArgumentsAsync(state, pending, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appends a fragment of tool arguments, holding it back until the block can open.</summary>
    /// <param name="state">The state of the tool call.</param>
    /// <param name="arguments">The argument fragment, which may be absent.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the fragment has been emitted or buffered.</returns>
    private async ValueTask AppendToolArgumentsAsync(
        ToolBlockState state,
        string? arguments,
        CancellationToken cancellationToken)
    {
        if (arguments is not { Length: > 0 })
        {
            return;
        }

        // Arguments can arrive before the name the block needs, so they are held until it does.
        if (!state.Started)
        {
            state.PendingArguments += arguments;
            return;
        }

        await EmitToolArgumentsAsync(state, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a fragment of tool arguments onto an open tool block.</summary>
    /// <param name="state">The state of the tool call.</param>
    /// <param name="arguments">The argument fragment to write.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the delta has been written.</returns>
    private ValueTask EmitToolArgumentsAsync(
        ToolBlockState state,
        string arguments,
        CancellationToken cancellationToken)
    {
        state.EmittedArgumentLength += arguments.Length;
        return WriteDeltaAsync(state.BlockIndex, StreamDelta.ForToolArguments(arguments), cancellationToken);
    }

    /// <summary>Opens a text block, closing the thinking block that may not overlap it.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the block is open.</returns>
    private async ValueTask OpenTextAsync(CancellationToken cancellationToken)
    {
        if (_textIndex >= 0)
        {
            return;
        }

        await CloseThinkingAsync(cancellationToken).ConfigureAwait(false);

        _textIndex = _nextBlockIndex++;
        await WriteBlockStartAsync(_textIndex, ContentBlock.ForText(string.Empty), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Opens a thinking block, closing the text block that may not overlap it.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the block is open.</returns>
    private async ValueTask OpenThinkingAsync(CancellationToken cancellationToken)
    {
        if (_thinkingIndex >= 0)
        {
            return;
        }

        await CloseTextAsync(cancellationToken).ConfigureAwait(false);

        _thinkingIndex = _nextBlockIndex++;
        await WriteBlockStartAsync(
            _thinkingIndex,
            new(ContentBlockTypes.Thinking, Thinking: string.Empty),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes the text block when one is open.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the block is closed, or immediately when none was open.</returns>
    private async ValueTask CloseTextAsync(CancellationToken cancellationToken)
    {
        var index = _textIndex;
        if (index < 0)
        {
            return;
        }

        _textIndex = Closed;
        await WriteBlockStopAsync(index, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes the thinking block when one is open.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the block is closed, or immediately when none was open.</returns>
    private async ValueTask CloseThinkingAsync(CancellationToken cancellationToken)
    {
        var index = _thinkingIndex;
        if (index < 0)
        {
            return;
        }

        _thinkingIndex = Closed;
        await WriteBlockStopAsync(index, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes every open block and writes the events that end the message.</summary>
    /// <param name="inputTokens">The prompt size to fall back on when the upstream withheld usage.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the final event has been written.</returns>
    private async ValueTask FlushAsync(int inputTokens, CancellationToken cancellationToken)
    {
        _segments.Clear();
        _thinkParser.Flush(_segments);
        await EmitSegmentsAsync(cancellationToken).ConfigureAwait(false);

        // A turn with no content at all is not a valid Anthropic message. Reasoning models that
        if (_nextBlockIndex == 0)
        {
            await AppendTextAsync(" ", cancellationToken).ConfigureAwait(false);
        }

        await CloseTextAsync(cancellationToken).ConfigureAwait(false);
        await CloseThinkingAsync(cancellationToken).ConfigureAwait(false);
        await CloseToolBlocksAsync(cancellationToken).ConfigureAwait(false);

        await WriteMessageDeltaAsync(inputTokens, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(
            StreamEventNames.MessageStop,
            StreamMessageStop.Instance,
            ProxyJsonContext.Default.StreamMessageStop,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes every tool block that was opened during the turn.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once every tool block is closed.</returns>
    private async ValueTask CloseToolBlocksAsync(CancellationToken cancellationToken)
    {
        foreach (var state in _tools.Values)
        {
            if (state.Started)
            {
                await WriteBlockStopAsync(state.BlockIndex, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes the event carrying the stop reason and the final usage.</summary>
    /// <param name="inputTokens">The prompt size to fall back on when the upstream withheld usage.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    private ValueTask WriteMessageDeltaAsync(int inputTokens, CancellationToken cancellationToken)
    {
        var stopReason = StopReasons.FromFinishReason(_finishReason);
        var outputTokens = _usage?.CompletionTokens ?? EstimateOutputTokens();

        return writer.WriteAsync(
            StreamEventNames.MessageDelta,
            new(
                new StreamStopDetail(stopReason),
                new TokenUsage(_usage?.PromptTokens ?? inputTokens, outputTokens)),
            ProxyJsonContext.Default.StreamMessageDelta,
            cancellationToken);
    }

    // Only reached when the upstream withheld usage despite being asked for it.
    /// <summary>Estimates the completion size from everything that was emitted.</summary>
    /// <returns>The estimated output token count.</returns>
    private int EstimateOutputTokens()
    {
        var characters = _textLength + _reasoningLength;
        foreach (var state in _tools.Values)
        {
            characters += state.EmittedArgumentLength + state.Name.Length;
        }

        return TokenEstimator.FromLength(characters);
    }

    /// <summary>Writes the event that opens a content block.</summary>
    /// <param name="index">The index the block is given.</param>
    /// <param name="block">The block being opened.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteBlockStartAsync(int index, ContentBlock block, CancellationToken cancellationToken) =>
        writer.WriteAsync(
            StreamEventNames.ContentBlockStart,
            new(index, block),
            ProxyJsonContext.Default.StreamContentBlockStart,
            cancellationToken);

    /// <summary>Writes the event that appends to an open content block.</summary>
    /// <param name="index">The index of the block being appended to.</param>
    /// <param name="delta">The fragment to append.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteDeltaAsync(int index, StreamDelta delta, CancellationToken cancellationToken) =>
        writer.WriteAsync(
            StreamEventNames.ContentBlockDelta,
            new(index, delta),
            ProxyJsonContext.Default.StreamContentBlockDelta,
            cancellationToken);

    /// <summary>Writes the event that closes a content block.</summary>
    /// <param name="index">The index of the block being closed.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteBlockStopAsync(int index, CancellationToken cancellationToken) =>
        writer.WriteAsync(
            StreamEventNames.ContentBlockStop,
            new(index),
            ProxyJsonContext.Default.StreamContentBlockStop,
            cancellationToken);
}
