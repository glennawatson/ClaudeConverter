// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Codex.Streaming;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Turns a streamed NVIDIA NIM completion into the Responses API event stream.</summary>
/// <param name="writer">The writer the Responses API events are emitted through.</param>
/// <param name="nimModel">The NIM model the turn was sent to, which every line this logs names.</param>
/// <param name="thinkingEnabled">Whether reasoning should be forwarded to the client.</param>
/// <param name="reasoningMayBeSeeded">
/// Whether the model's chat template may open the reasoning block itself, so that the completion
/// begins inside it and carries only a closing tag.
/// </param>
/// <param name="idleTimeout">How long the upstream may produce nothing before the turn is abandoned.</param>
/// <param name="time">The clock the turn's timestamp is read from.</param>
/// <param name="logger">The diagnostic log.</param>
/// <remarks>
/// <para>
/// This is the Responses API counterpart of <see cref="NimStreamTranslator"/>. The two protocols
/// disagree about structure the same way their non-streamed translators do: NIM streams flat deltas
/// that may carry answer text, a reasoning trace, and tool-call fragments in any order; the
/// Responses API streams a reasoning item, a message item, and one <c>function_call</c> item per
/// tool call, each opened before it is appended to and closed once it is complete.
/// </para>
/// <para>
/// A tool call's arguments are streamed as <c>function_call_arguments.delta</c> fragments as they
/// arrive and closed once the call is known to be finished, matching this project's own reference
/// implementation of a Codex-compatible server. Codex CLI itself does not read the delta events —
/// it reads the whole call from the <c>output_item.done</c> that closes it — so this is for the
/// benefit of other Responses API clients that do.
/// </para>
/// <para>
/// One instance serves exactly one response.
/// </para>
/// </remarks>
public sealed class CodexStreamTranslator(
    CodexSseWriter writer,
    string nimModel,
    bool thinkingEnabled,
    bool reasoningMayBeSeeded,
    TimeSpan idleTimeout,
    TimeProvider time,
    ILogger logger)
{
    /// <summary>The marker every server-sent event payload line begins with.</summary>
    private const string DataPrefix = "data:";

    /// <summary>The payload the upstream sends in place of a chunk once the turn is over.</summary>
    private const string DoneMarker = "[DONE]";

    /// <summary>The index an item field holds while no item of that kind is open.</summary>
    private const int Closed = -1;

    /// <summary>The longest run of an unreadable payload that is written to the log.</summary>
    private const int LoggedPayloadLength = 400;

    /// <summary>The latch value meaning the opening event has been written, committing the turn.</summary>
    private const int Committed = 1;

    /// <summary>The failure reported when every attempt failed without the upstream saying why.</summary>
    private static readonly NimStreamError UnreachableUpstream = new(
        "The upstream connection failed or timed out before the turn produced anything.",
        Code: StatusCodes.Status503ServiceUnavailable);

    /// <summary>The splitter that lifts inline reasoning tags out of streamed answer text.</summary>
    private readonly ThinkTagParser _thinkParser = new(reasoningMayBeSeeded);

    /// <summary>The scratch list each parsed run of text is split into, reused across deltas.</summary>
    private readonly List<ThinkTagSegment> _segments = [];

    /// <summary>The parser recovering tool calls a model wrote as marker tokens inside its answer.</summary>
    private readonly EmbeddedToolCallParser _embeddedParser = new();

    /// <summary>The scratch list each parsed run of answer text is split into, reused across deltas.</summary>
    private readonly List<EmbeddedToolCall> _runs = [];

    /// <summary>The structured tool calls being assembled, keyed by the slot the upstream assigned.</summary>
    private readonly Dictionary<int, CodexToolCallState> _tools = [];

    /// <summary>The reasoning accumulated so far, for the final <c>output_item.done</c>.</summary>
    private readonly StringBuilder _reasoningText = new();

    /// <summary>The answer text accumulated so far, for the final <c>output_item.done</c>.</summary>
    private readonly StringBuilder _answerText = new();

    /// <summary>The turn's next unused output index.</summary>
    private int _nextOutputIndex;

    /// <summary>The reasoning item's output index, or <see cref="Closed"/> before it is opened.</summary>
    private int _reasoningIndex = Closed;

    /// <summary>The message item's output index, or <see cref="Closed"/> before it is opened.</summary>
    private int _textIndex = Closed;

    /// <summary>The reasoning item's own identifier, once it is opened.</summary>
    private string? _reasoningItemId;

    /// <summary>The message item's own identifier, once it is opened.</summary>
    private string? _messageItemId;

    /// <summary>The next synthetic slot a tool call recovered from answer text is tracked under.</summary>
    /// <remarks>Negative, so it can never collide with a real upstream tool-call index.</remarks>
    private int _nextRecoveredSlot = -1;

    /// <summary>The sequence number the next event is written with.</summary>
    private int _sequence;

    /// <summary>Whether the opening event has been written, committing the turn.</summary>
    private int _started;

    /// <summary>The number of chunks read so far.</summary>
    private int _chunksRead;

    /// <summary>The number of chunks that failed to parse.</summary>
    private int _chunksSkipped;

    /// <summary>The finish reason the upstream reported, once it has.</summary>
    private string? _finishReason;

    /// <summary>The usage the upstream reported, once it has.</summary>
    private NimUsage? _usage;

    /// <summary>The mid-stream failure the upstream reported, once it has.</summary>
    private NimStreamError? _failure;

    /// <summary>The identifier to report for the turn, once the turn commits.</summary>
    private string _responseId = string.Empty;

    /// <summary>The model identifier to echo back, once the turn commits.</summary>
    private string _model = string.Empty;

    /// <summary>The prompt size to report, once the turn commits.</summary>
    private int _inputTokens;

    /// <summary>Gets the status the mid-stream failure reported, or <see langword="null"/> when the turn has not failed this way.</summary>
    public int? FailureStatusCode => _failure?.Code;

    /// <summary>Reads the upstream stream to completion, emitting Responses API events as it goes.</summary>
    /// <param name="upstream">The NIM response body.</param>
    /// <param name="responseId">The identifier to report for the turn.</param>
    /// <param name="model">The model identifier to echo back to the client.</param>
    /// <param name="inputTokens">The prompt size to report.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>How the attempt ended.</returns>
    public async ValueTask<StreamTurnOutcome> TranslateAsync(
        Stream upstream,
        string responseId,
        string model,
        int inputTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upstream);

        _responseId = responseId;
        _model = model;
        _inputTokens = inputTokens;

        using var reader = new StreamReader(upstream);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (await ReadToCompletionAsync(reader, idle, cancellationToken).ConfigureAwait(false))
        {
            return Volatile.Read(ref _started) == Committed ? StreamTurnOutcome.Failed : StreamTurnOutcome.FailedBeforeOutput;
        }

        await FlushAsync(cancellationToken).ConfigureAwait(false);
        NvidiaLog.StreamCompleted(logger, nimModel, _chunksRead, _chunksSkipped);

        return StreamTurnOutcome.Completed;
    }

    /// <summary>Reports the failure on the stream, once no further attempt will be made.</summary>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WriteHeldFailureAsync(CancellationToken cancellationToken) =>
        WriteErrorAsync(_failure ?? UnreachableUpstream, cancellationToken);

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
            if (idle.IsCancellationRequested)
            {
                NvidiaLog.StreamIdleTimeout(logger, nimModel, idleTimeout.TotalSeconds);
            }
            else
            {
                LogStreamFailure(new(error.Message));
            }

            _failure = new(
                "The upstream connection failed or timed out before the turn finished.",
                Code: StatusCodes.Status503ServiceUnavailable);

            if (Volatile.Read(ref _started) == Committed)
            {
                await WriteErrorAsync(_failure, cancellationToken).ConfigureAwait(false);
            }

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
            NvidiaLog.RawStreamLine(logger, nimModel, line);
        }

        var chunk = ParseChunk(line);
        if (chunk is null)
        {
            return false;
        }

        if (chunk.Error is { } failure)
        {
            LogStreamFailure(failure);
            _failure = failure;

            if (Volatile.Read(ref _started) == Committed)
            {
                await WriteErrorAsync(failure, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        await ConsumeChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <summary>Records a mid-stream failure alongside what the client was told about it.</summary>
    /// <param name="failure">The failure the upstream reported.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogStreamFailure(NimStreamError failure) =>
        NvidiaLog.StreamFailed(
            logger,
            nimModel,
            failure.Message ?? string.Empty,
            failure.Code ?? 0,
            failure.Type ?? "none",
            StreamErrorTypes.FromUpstream(failure));

    /// <summary>Writes the error event for an upstream failure.</summary>
    /// <param name="failure">The failure the upstream reported.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteErrorAsync(NimStreamError failure, CancellationToken cancellationToken) =>
        writer.WriteAsync(
            new(
                ResponseStreamEventTypes.Error,
                NextSequence(),
                Response: new ResponsesResponse(
                    _responseId.Length > 0 ? _responseId : $"resp_{Guid.NewGuid():N}",
                    _model,
                    ResponsesResponse.StatusFailed,
                    [],
                    Error: new ResponseError(
                        failure.Message ?? "The upstream ended the response with an unspecified failure.",
                        StreamErrorTypes.FromUpstream(failure)))),
            cancellationToken);

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
            var clipped = payload.Length <= LoggedPayloadLength ? payload : payload[..LoggedPayloadLength];

            if (_chunksSkipped == 1)
            {
                NvidiaLog.StreamChunkUnreadableFirst(logger, nimModel, clipped, error);
            }
            else if (logger.IsEnabled(LogLevel.Debug))
            {
                NvidiaLog.StreamChunkUnreadable(logger, nimModel, clipped, error);
            }

            return null;
        }
    }

    /// <summary>Dispatches one upstream chunk onto the matching output items.</summary>
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

    /// <summary>Returns and advances the event sequence number.</summary>
    /// <returns>The sequence number to use for the next event.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NextSequence() => _sequence++;

    /// <summary>Writes the turn's opening event, the first time any output item is about to open.</summary>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written, or immediately once it already has.</returns>
    /// <remarks>
    /// Held back until the first real output rather than written up front: a failure reached before
    /// this fires has shown the client nothing, and the turn can still be asked for again.
    /// </remarks>
    private async ValueTask EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, Committed) == Committed)
        {
            return;
        }

        await writer.WriteAsync(
            new(
                ResponseStreamEventTypes.Created,
                NextSequence(),
                Response: new ResponsesResponse(_responseId, _model, ResponsesResponse.StatusInProgress, [])),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases text held back in case the chat template had seeded a reasoning block.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once any released text has been emitted.</returns>
    private async ValueTask ReleaseSeededHoldAsync(CancellationToken cancellationToken)
    {
        _segments.Clear();
        _thinkParser.NoteExplicitReasoning(_segments);
        await EmitSegmentsAsync(cancellationToken).ConfigureAwait(false);
    }

    // Answer text from GLM-style models arrives with the reasoning trace inline, so it is split
    // before any of it reaches an output item.
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

    /// <summary>Emits the pending split segments, each onto the field its kind belongs to.</summary>
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

    /// <summary>Opens the reasoning item, once it is not already open.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the item is open.</returns>
    private async ValueTask OpenReasoningAsync(CancellationToken cancellationToken)
    {
        if (_reasoningIndex >= 0)
        {
            return;
        }

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        _reasoningIndex = _nextOutputIndex++;
        _reasoningItemId = $"rs_{Guid.NewGuid():N}";

        var item = new ResponseInputItem(ResponseItemTypes.Reasoning, Id: _reasoningItemId, Status: ResponsesResponse.StatusInProgress);
        await writer.WriteAsync(
            new(ResponseStreamEventTypes.OutputItemAdded, NextSequence(), Item: item, OutputIndex: _reasoningIndex),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appends a run of reasoning, opening the reasoning item when one is not already open.</summary>
    /// <param name="reasoning">The reasoning the upstream produced.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the reasoning has been emitted, or immediately when it is suppressed.</returns>
    private async ValueTask AppendReasoningAsync(string reasoning, CancellationToken cancellationToken)
    {
        if (!thinkingEnabled)
        {
            return;
        }

        await OpenReasoningAsync(cancellationToken).ConfigureAwait(false);
        _ = _reasoningText.Append(reasoning);

        await writer.WriteAsync(
            new(
                ResponseStreamEventTypes.ReasoningTextDelta,
                NextSequence(),
                ItemId: _reasoningItemId,
                OutputIndex: _reasoningIndex,
                ContentIndex: 0,
                Delta: reasoning),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens the message item, once it is not already open.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the item is open.</returns>
    private async ValueTask OpenTextAsync(CancellationToken cancellationToken)
    {
        if (_textIndex >= 0)
        {
            return;
        }

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        _textIndex = _nextOutputIndex++;
        _messageItemId = $"msg_{Guid.NewGuid():N}";

        var item = new ResponseInputItem(
            ResponseItemTypes.Message,
            Id: _messageItemId,
            Role: ResponseItemTypes.AssistantRole,
            Status: ResponsesResponse.StatusInProgress);

        await writer.WriteAsync(
            new(ResponseStreamEventTypes.OutputItemAdded, NextSequence(), Item: item, OutputIndex: _textIndex),
            cancellationToken).ConfigureAwait(false);
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

    /// <summary>Emits the pending recovered runs, each onto the field its kind belongs to.</summary>
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

    /// <summary>Appends a run of plain text, opening the message item when one is not already open.</summary>
    /// <param name="text">The text to emit.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the text has been emitted.</returns>
    private async ValueTask EmitPlainTextAsync(string text, CancellationToken cancellationToken)
    {
        await OpenTextAsync(cancellationToken).ConfigureAwait(false);
        _ = _answerText.Append(text);

        await writer.WriteAsync(
            new(
                ResponseStreamEventTypes.OutputTextDelta,
                NextSequence(),
                ItemId: _messageItemId,
                OutputIndex: _textIndex,
                ContentIndex: 0,
                Delta: text),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Emits a tool call that was recovered from the answer text.</summary>
    /// <param name="run">The recovered call.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the call has been emitted.</returns>
    /// <remarks>
    /// The call is already whole by the time it is recovered, so it is opened and filled in one go
    /// rather than streamed in fragments, and tracked under a synthetic slot so it closes at flush
    /// like any other.
    /// </remarks>
    private async ValueTask EmitRecoveredCallAsync(EmbeddedToolCall run, CancellationToken cancellationToken)
    {
        NvidiaLog.EmbeddedToolCallRecovered(logger, nimModel, run.Name);

        CodexToolCallState state = new() { Id = $"call_{Guid.NewGuid():N}" };
        state.MergeName(run.Name);
        var slot = _nextRecoveredSlot;
        _nextRecoveredSlot--;
        _tools[slot] = state;

        await StartToolItemAsync(state, cancellationToken).ConfigureAwait(false);

        if (run.Payload.Length > 0)
        {
            await EmitToolArgumentsAsync(state, run.Payload, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Appends a batch of structurally reported tool-call fragments.</summary>
    /// <param name="toolCalls">The fragments the upstream produced.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once every fragment has been applied.</returns>
    private async ValueTask AppendToolCallsAsync(List<NimToolCall> toolCalls, CancellationToken cancellationToken)
    {
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

        await StartToolItemAsync(state, cancellationToken).ConfigureAwait(false);
        await AppendToolArgumentsAsync(state, call.Function?.Arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the state tracking a tool slot, creating it the first time the slot is seen.</summary>
    /// <param name="slot">The index the upstream assigned to the tool call.</param>
    /// <returns>The state for that slot.</returns>
    private CodexToolCallState StateFor(int slot)
    {
        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(_tools, slot, out _);
        return state ??= new CodexToolCallState();
    }

    /// <summary>Opens the output item for a tool call once its name is known.</summary>
    /// <param name="state">The state of the tool call.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the item has been opened, or immediately when it cannot be yet.</returns>
    private async ValueTask StartToolItemAsync(CodexToolCallState state, CancellationToken cancellationToken)
    {
        if (state.Started || state.Name.Length == 0)
        {
            return;
        }

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        state.OutputIndex = _nextOutputIndex++;
        state.Started = true;
        state.Id ??= $"call_{Guid.NewGuid():N}";
        state.ItemId = $"fc_{Guid.NewGuid():N}";

        var item = new ResponseInputItem(
            ResponseItemTypes.FunctionCall,
            Id: state.ItemId,
            CallId: state.Id,
            Name: state.Name,
            Arguments: string.Empty,
            Status: ResponsesResponse.StatusInProgress);

        await writer.WriteAsync(
            new(ResponseStreamEventTypes.OutputItemAdded, NextSequence(), Item: item, OutputIndex: state.OutputIndex),
            cancellationToken).ConfigureAwait(false);

        if (state.PendingArguments.Length == 0)
        {
            return;
        }

        var pending = state.PendingArguments;
        state.PendingArguments = string.Empty;
        await EmitToolArgumentsAsync(state, pending, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appends a fragment of tool arguments, holding it back until the item can open.</summary>
    /// <param name="state">The state of the tool call.</param>
    /// <param name="arguments">The argument fragment, which may be absent.</param>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once the fragment has been emitted or buffered.</returns>
    private async ValueTask AppendToolArgumentsAsync(CodexToolCallState state, string? arguments, CancellationToken cancellationToken)
    {
        if (arguments is not { Length: > 0 })
        {
            return;
        }

        // Arguments can arrive before the name the item needs, so they are held until it does.
        if (!state.Started)
        {
            state.PendingArguments += arguments;
            return;
        }

        await EmitToolArgumentsAsync(state, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a fragment of tool arguments onto an open tool item.</summary>
    /// <param name="state">The state of the tool call.</param>
    /// <param name="arguments">The argument fragment to write.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the delta has been written.</returns>
    private ValueTask EmitToolArgumentsAsync(CodexToolCallState state, string arguments, CancellationToken cancellationToken)
    {
        _ = state.Arguments.Append(arguments);
        return writer.WriteAsync(
            new(
                ResponseStreamEventTypes.FunctionCallArgumentsDelta,
                NextSequence(),
                ItemId: state.ItemId,
                OutputIndex: state.OutputIndex,
                Delta: arguments),
            cancellationToken);
    }

    /// <summary>Closes every open item, and writes the turn's final event.</summary>
    /// <param name="cancellationToken">Abandons the translation when the client disconnects.</param>
    /// <returns>A task that completes once every closing event has been written.</returns>
    private async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        _segments.Clear();
        _thinkParser.Flush(_segments);
        await EmitSegmentsAsync(cancellationToken).ConfigureAwait(false);

        _runs.Clear();
        _embeddedParser.Flush(_runs);
        await EmitRunsAsync(cancellationToken).ConfigureAwait(false);

        if (_reasoningIndex < 0 && _textIndex < 0 && _tools.Count == 0)
        {
            // A turn that produced nothing observable still needs a message the client can render.
            await OpenTextAsync(cancellationToken).ConfigureAwait(false);
        }

        var output = BuildFinalOutput(out var indices);
        for (var i = 0; i < output.Count; i++)
        {
            await writer.WriteAsync(
                new(ResponseStreamEventTypes.OutputItemDone, NextSequence(), Item: output[i], OutputIndex: indices[i]),
                cancellationToken).ConfigureAwait(false);
        }

        await WriteCompletedAsync(output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds every item the turn produced, in output order.</summary>
    /// <param name="indices">The output index each returned item was opened at.</param>
    /// <returns>The items.</returns>
    private List<ResponseInputItem> BuildFinalOutput(out List<int> indices)
    {
        List<ResponseInputItem> output = [];
        indices = [];

        if (_reasoningIndex >= 0)
        {
            output.Add(new(
                ResponseItemTypes.Reasoning,
                Id: _reasoningItemId,
                Status: ResponsesResponse.StatusCompleted,
                Summary: [ResponseContentItem.ForSummaryText(_reasoningText.ToString())]));
            indices.Add(_reasoningIndex);
        }

        if (_textIndex >= 0)
        {
            output.Add(new(
                ResponseItemTypes.Message,
                Id: _messageItemId,
                Role: ResponseItemTypes.AssistantRole,
                Status: ResponsesResponse.StatusCompleted,
                Content: [ResponseContentItem.ForOutputText(_answerText.ToString())]));
            indices.Add(_textIndex);
        }

        AppendToolOutput(output, indices);

        return output;
    }

    /// <summary>Appends every finished tool call, ordered by the output index it opened at.</summary>
    /// <param name="output">The items being composed.</param>
    /// <param name="indices">The output index each item in <paramref name="output"/> was opened at.</param>
    private void AppendToolOutput(List<ResponseInputItem> output, List<int> indices)
    {
        List<CodexToolCallState> tools = [];
        foreach (var state in _tools.Values)
        {
            if (state.Started)
            {
                tools.Add(state);
            }
        }

        tools.Sort(static (a, b) => a.OutputIndex.CompareTo(b.OutputIndex));

        for (var i = 0; i < tools.Count; i++)
        {
            var state = tools[i];
            output.Add(new(
                ResponseItemTypes.FunctionCall,
                Id: state.ItemId,
                CallId: state.Id,
                Name: state.Name,
                Arguments: state.Arguments.ToString(),
                Status: ResponsesResponse.StatusCompleted));
            indices.Add(state.OutputIndex);
        }
    }

    /// <summary>Writes the turn's final event, once every item has closed.</summary>
    /// <param name="output">Every item the turn produced.</param>
    /// <param name="cancellationToken">Abandons the write when the client disconnects.</param>
    /// <returns>A task that completes once the event has been written.</returns>
    private ValueTask WriteCompletedAsync(List<ResponseInputItem> output, CancellationToken cancellationToken)
    {
        var incomplete = string.Equals(_finishReason, "length", StringComparison.Ordinal);
        var status = incomplete ? ResponsesResponse.StatusIncomplete : ResponsesResponse.StatusCompleted;

        var outputTokens = _usage?.CompletionTokens
            ?? Anthropic.TokenEstimator.FromLength(_answerText.Length + _reasoningText.Length);
        var inputTokensReported = _usage?.PromptTokens ?? _inputTokens;

        var response = new ResponsesResponse(
            _responseId,
            _model,
            status,
            output,
            CreatedAt: time.GetUtcNow().ToUnixTimeSeconds(),
            OutputText: _answerText.Length > 0 ? _answerText.ToString() : null,
            Usage: new ResponseUsage(inputTokensReported, outputTokens, inputTokensReported + outputTokens),
            IncompleteDetails: incomplete ? new IncompleteDetails("max_output_tokens") : null);

        var eventType = incomplete ? ResponseStreamEventTypes.Incomplete : ResponseStreamEventTypes.Completed;

        return writer.WriteAsync(new(eventType, NextSequence(), Response: response), cancellationToken);
    }
}
