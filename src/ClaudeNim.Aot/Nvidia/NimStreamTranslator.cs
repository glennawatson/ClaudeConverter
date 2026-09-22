// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Turns a streamed NVIDIA NIM completion into the Anthropic event stream.</summary>
/// <param name="writer">The writer the Anthropic events are emitted through.</param>
/// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
/// <param name="reasoningMayBeSeeded">
/// Whether the model's chat template may open the reasoning block itself, so that the completion
/// begins inside it and carries only a closing tag.
/// </param>
/// <param name="idleTimeout">How long the upstream may produce nothing before the turn is abandoned.</param>
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
public sealed class NimStreamTranslator(
    AnthropicSseWriter writer,
    bool thinkingEnabled,
    bool reasoningMayBeSeeded,
    TimeSpan idleTimeout,
    ILogger logger)
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
    private readonly ThinkTagParser _thinkParser = new(reasoningMayBeSeeded);

    /// <summary>The scratch list each parsed run of text is split into, reused across deltas.</summary>
    private readonly List<ThinkTagSegment> _segments = [];

    /// <summary>The recovery pass for tool calls a model writes into its answer text.</summary>
    private readonly EmbeddedToolCallParser _embeddedParser = new();

    /// <summary>The scratch list the recovered runs are collected into, reused across deltas.</summary>
    private readonly List<EmbeddedToolCall> _runs = [];

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

        // A streamed turn is bounded by silence rather than by duration: the clock is restarted
        // before every line, so a long answer runs to completion while a dead connection does not.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (await ReadToCompletionAsync(reader, idle, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await FlushAsync(inputTokens, cancellationToken).ConfigureAwait(false);
        NvidiaLog.StreamCompleted(logger, _chunksRead, _chunksSkipped);
    }

    /// <summary>Clips a payload so one log line stays a line rather than a dump of the whole chunk.</summary>
    /// <param name="payload">The payload to clip.</param>
    /// <returns>The payload, no longer than <see cref="LoggedPayloadLength"/>.</returns>
    private static string Clipped(string payload) =>
        payload.Length <= LoggedPayloadLength ? payload : payload[..LoggedPayloadLength];

    /// <summary>Determines whether an exception represents a failed or timed-out upstream connection.</summary>
    /// <param name="error">The exception the read raised.</param>
    /// <returns><see langword="true"/> when the exception is a transport-level failure.</returns>
    private static bool IsUpstreamTransportFailure(Exception error) =>
        error is HttpRequestException or OperationCanceledException or IOException;

    /// <summary>Reads and consumes every line of the upstream body, surfacing a transport failure as an error event.</summary>
    /// <param name="reader">The reader positioned on the upstream body.</param>
    /// <param name="idle">The linked source the idle timeout is applied through.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns><see langword="true"/> when the turn already ended and no further writing should happen.</returns>
    /// <remarks>
    /// By the time this runs, headers for a 200 response have already reached the client: there is
    /// no status code left to change. A dropped connection or an idle timeout here used to reach
    /// Kestrel as an unhandled exception, which the client saw as the connection simply dying mid
    /// answer rather than a turn that ended with a reason. The caller's own token firing is not this
    /// kind of failure — that is the client disconnecting, and there is no one left to write to.
    /// </remarks>
    private async ValueTask<bool> ReadToCompletionAsync(
        StreamReader reader,
        CancellationTokenSource idle,
        CancellationToken cancellationToken)
    {
        string? line;
        try
        {
            while ((line = await ReadLineOrTimeoutAsync(reader, idle).ConfigureAwait(false)) is not null)
            {
                if (await ConsumeLineAsync(line, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }
        catch (Exception error) when (IsUpstreamTransportFailure(error) && !cancellationToken.IsCancellationRequested)
        {
            // The idle clock fires as a cancellation on the linked source, which by type alone is
            // the same exception a dropped connection raises. The two call for different fixes.
            if (idle.IsCancellationRequested)
            {
                NvidiaLog.StreamIdleTimeout(logger, idleTimeout.TotalSeconds);
            }
            else
            {
                NvidiaLog.StreamFailed(logger, error.Message);
            }

            await WriteErrorAsync(
                new("The upstream connection failed or timed out before the turn finished.", Code: StatusCodes.Status503ServiceUnavailable),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>Reads the next line, restarting the idle clock first.</summary>
    /// <param name="reader">The reader positioned on the upstream body.</param>
    /// <param name="idle">The linked source the idle timeout is applied through.</param>
    /// <returns>The line, or <see langword="null"/> once the stream has ended.</returns>
    private async ValueTask<string?> ReadLineOrTimeoutAsync(StreamReader reader, CancellationTokenSource idle)
    {
        idle.CancelAfter(idleTimeout);
        return await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
    }

    /// <summary>Parses and applies one line of the upstream body.</summary>
    /// <param name="line">The raw line read from the upstream body.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns><see langword="true"/> when the line ended the turn and no further line should be read.</returns>
    private async ValueTask<bool> ConsumeLineAsync(string line, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            NvidiaLog.RawStreamLine(logger, line);
        }

        var chunk = ParseChunk(line);
        if (chunk is null)
        {
            return false;
        }

        // A failure after the status code was committed arrives here rather than as a status. It
        // ends the turn: continuing would emit an empty message the client reads as the model
        // having nothing to say.
        if (chunk.Error is { } failure)
        {
            await WriteErrorAsync(failure, cancellationToken).ConfigureAwait(false);
            NvidiaLog.StreamFailed(logger, failure.Message ?? string.Empty);
            return true;
        }

        await ConsumeChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
        return false;
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

            // Already the rare path, so the clip is paid for once rather than guarded twice.
            var logged = Clipped(payload);

            if (_chunksSkipped == 1)
            {
                NvidiaLog.StreamChunkUnreadableFirst(logger, logged, error);
            }
            else if (logger.IsEnabled(LogLevel.Debug))
            {
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
            await ReleaseSeededHoldAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>Releases text held back in case the chat template had seeded a reasoning block.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once any released text has been emitted.</returns>
    /// <remarks>
    /// A model reporting reasoning on its own field is not one that seeds an inline tag, so the
    /// wait can end the moment the first such delta arrives rather than on the holdback limit.
    /// </remarks>
    private async ValueTask ReleaseSeededHoldAsync(CancellationToken cancellationToken)
    {
        _segments.Clear();
        _thinkParser.NoteExplicitReasoning(_segments);
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

    /// <summary>Appends a run of answer text, recovering any tool call written into it.</summary>
    /// <param name="text">The text the upstream produced.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the run has been emitted.</returns>
    /// <remarks>
    /// Models that render a tool call as marker tokens inside their answer would otherwise stream
    /// the markers to the client as literal text and never execute the tool.
    /// </remarks>
    private async ValueTask AppendTextAsync(string text, CancellationToken cancellationToken)
    {
        _runs.Clear();
        _embeddedParser.Feed(text, _runs);
        await EmitRunsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Emits the pending recovered runs, each onto the block its kind belongs to.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once every run has been emitted.</returns>
    private async ValueTask EmitRunsAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            if (run.IsCall)
            {
                await EmitRecoveredCallAsync(run, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await EmitPlainTextAsync(run.Payload, cancellationToken).ConfigureAwait(false);
        }

        _runs.Clear();
    }

    /// <summary>Emits a tool call that was recovered from the answer text.</summary>
    /// <param name="run">The recovered call.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the call has been emitted.</returns>
    /// <remarks>
    /// The call is already whole by the time it is recovered, so it is opened, filled and closed
    /// in one go rather than streamed in fragments.
    /// </remarks>
    private async ValueTask EmitRecoveredCallAsync(EmbeddedToolCall run, CancellationToken cancellationToken)
    {
        await CloseTextAsync(cancellationToken).ConfigureAwait(false);
        await CloseThinkingAsync(cancellationToken).ConfigureAwait(false);

        var index = _nextBlockIndex++;
        var identifier = $"toolu_{Guid.NewGuid():N}";

        await WriteBlockStartAsync(
            index,
            new(ContentBlockTypes.ToolUse, Id: identifier, Name: run.Name, Input: JsonElements.EmptyObject),
            cancellationToken).ConfigureAwait(false);

        if (run.Payload.Length > 0)
        {
            _textLength += run.Payload.Length;
            await WriteDeltaAsync(index, StreamDelta.ForToolArguments(run.Payload), cancellationToken)
                .ConfigureAwait(false);
        }

        await WriteBlockStopAsync(index, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appends a run of plain text, opening a text block when one is not already open.</summary>
    /// <param name="text">The text to emit.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the text has been emitted.</returns>
    private async ValueTask EmitPlainTextAsync(string text, CancellationToken cancellationToken)
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

        _runs.Clear();
        _embeddedParser.Flush(_runs);
        await EmitRunsAsync(cancellationToken).ConfigureAwait(false);

        // A turn with no content at all is not a valid Anthropic message. Reasoning models that
        if (_nextBlockIndex == 0)
        {
            NvidiaLog.EmptyTurn(logger, _chunksRead, _finishReason ?? "none reported");
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
