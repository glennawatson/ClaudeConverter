// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The diagnostic messages the NVIDIA NIM transport emits.</summary>
/// <remarks>
/// Every message is a source-generated <see cref="LoggerMessageAttribute"/> delegate rather than a
/// <c>Log*</c> extension call. The extensions box each argument into an array before the level is
/// even checked, which a proxy on the hot path of every token pays for; the generated delegates
/// check the level first and never allocate when the message is not written. They are also what
/// keeps the logging path reflection-free under native AOT.
/// </remarks>
internal static partial class NvidiaLog
{
    // Retries and downgrades are warnings, not information. Each one is the proxy silently doing
    // something other than what it was asked to, and a turn that only succeeds on the third
    // attempt with half its controls dropped is a degraded turn — indistinguishable, at
    // Information, from one that worked first time. The operator watching at Warning is the one
    // who needs to know.
    /// <summary>Records that a model rejected the reasoning controls and the call is being retried.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="model">The model that rejected the request.</param>
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "NIM rejected {Model} with the reasoning controls set; retrying without them.")]
    internal static partial void ChatTemplateRejected(ILogger logger, string model);

    /// <summary>Records that a transient upstream failure is being waited out.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="status">The status the upstream returned.</param>
    /// <param name="attempt">The attempt that failed.</param>
    /// <param name="maxAttempts">The configured attempt budget.</param>
    /// <param name="delayMilliseconds">How long the next attempt waits.</param>
    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "NIM returned {Status} on attempt {Attempt} of {MaxAttempts}; retrying in {DelayMilliseconds}ms.")]
    internal static partial void RetryingAfterTransientFailure(
        ILogger logger,
        int status,
        int attempt,
        int maxAttempts,
        double delayMilliseconds);

    /// <summary>Records that an attempt which never reached a status is being made again.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="attempt">The attempt that failed.</param>
    /// <param name="maxAttempts">The configured attempt budget.</param>
    /// <param name="delayMilliseconds">How long the next attempt waits.</param>
    /// <param name="error">The transport failure.</param>
    [LoggerMessage(
        EventId = 1022,
        Level = LogLevel.Warning,
        Message = "The upstream call failed before any status on attempt {Attempt} of {MaxAttempts}; retrying in {DelayMilliseconds}ms.")]
    internal static partial void RetryingAfterTransportFailure(
        ILogger logger,
        int attempt,
        int maxAttempts,
        double delayMilliseconds,
        Exception error);

    /// <summary>Records that the retry budget ran out with the upstream still failing.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="status">The status the last attempt returned.</param>
    /// <param name="attempts">How many attempts were made.</param>
    /// <remarks>
    /// The rejection itself is reported separately, but only as the last status — nothing in it
    /// says the proxy had already spent its whole budget getting there. A turn that failed once
    /// and one that failed five times are the same line without this.
    /// </remarks>
    [LoggerMessage(
        EventId = 1015,
        Level = LogLevel.Warning,
        Message = "NIM still returned {Status} after {Attempts} attempts; giving up on the turn.")]
    internal static partial void RetriesExhausted(ILogger logger, int status, int attempts);

    /// <summary>Records that the upstream model listing returned a failure status.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="status">The HTTP status the listing returned.</param>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "NIM model listing failed with status {Status}.")]
    internal static partial void ModelListingFailed(ILogger logger, int status);

    /// <summary>Records that the upstream model listing could not be reached at all.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="error">The transport failure.</param>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Could not reach the NVIDIA NIM model listing.")]
    internal static partial void ModelListingUnreachable(ILogger logger, Exception error);

    /// <summary>Records that the upstream model listing did not answer in time.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="error">The cancellation the timeout surfaced as.</param>
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "The NVIDIA NIM model listing timed out.")]
    internal static partial void ModelListingTimedOut(ILogger logger, Exception error);

    /// <summary>Records a streamed chunk that could not be read.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="payload">The payload that could not be read.</param>
    /// <param name="error">The failure the reader raised.</param>
    /// <remarks>
    /// A chunk the proxy cannot read is skipped so the turn can continue, which is the right
    /// behaviour but makes an upstream shape change invisible. This is the record of it.
    /// </remarks>
    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "Skipped an unreadable NIM stream chunk: {Payload}")]
    internal static partial void StreamChunkUnreadable(ILogger logger, string payload, Exception error);

    /// <summary>Records the first unreadable chunk of a turn, where the rest are only recorded at debug.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="payload">The payload that could not be read.</param>
    /// <param name="error">The failure the reader raised.</param>
    /// <remarks>
    /// An upstream that changed shape produces one unreadable chunk per delta, so warning on every
    /// one would bury the log in a single turn. Warning once says the same thing: something in the
    /// stream stopped parsing. The per-chunk detail stays at debug for when that is being chased.
    /// </remarks>
    [LoggerMessage(
        EventId = 1016,
        Level = LogLevel.Warning,
        Message = "A NIM stream chunk could not be read and was skipped; further ones this turn are logged at debug. First: {Payload}")]
    internal static partial void StreamChunkUnreadableFirst(ILogger logger, string payload, Exception error);

    /// <summary>Records that a streamed turn was abandoned because the upstream went quiet.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="idleSeconds">How long the upstream produced nothing before the turn was given up on.</param>
    /// <remarks>
    /// This arrives as a cancellation, which is indistinguishable from a dropped connection by
    /// exception type alone — and the two call for different fixes, so they are reported apart.
    /// </remarks>
    [LoggerMessage(
        EventId = 1017,
        Level = LogLevel.Warning,
        Message = "NIM produced nothing for {IdleSeconds}s; the streamed turn was abandoned.")]
    internal static partial void StreamIdleTimeout(ILogger logger, double idleSeconds);

    /// <summary>Records that a turn finished without the model having produced anything.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="chunks">The number of chunks that were read.</param>
    /// <param name="finishReason">The reason the upstream gave for stopping.</param>
    /// <remarks>
    /// An empty turn is not an error at any layer — the call succeeded, the stream was well formed
    /// — so nothing else reports it. To the client it is the model having nothing to say, which
    /// for a coding client ends the work. It is the single most confusing way for this proxy to
    /// fail, so it is recorded where an operator will see it.
    /// </remarks>
    [LoggerMessage(
        EventId = 1018,
        Level = LogLevel.Warning,
        Message = "A streamed turn produced no content at all after {Chunks} chunks (finish reason: {FinishReason}).")]
    internal static partial void EmptyTurn(ILogger logger, int chunks, string finishReason);

    /// <summary>Records that the upstream ended a streamed turn with a failure.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="reason">The failure the upstream reported.</param>
    /// <param name="upstreamCode">The status the upstream gave, or 0 when it gave none.</param>
    /// <param name="upstreamType">The upstream's own classification, or "none" when it gave none.</param>
    /// <param name="reportedType">The Anthropic error class the client was given.</param>
    /// <remarks>
    /// The reason alone does not say what the client was told, and the client acts on that rather
    /// than on the prose — a turn reported as <c>overloaded_error</c> is retried where the same
    /// text as <c>api_error</c> ends the work. Both ends of the translation are recorded so the
    /// two can be told apart without reproducing the failure.
    /// </remarks>
    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "NIM ended a streamed turn with a failure (upstream status {UpstreamCode}, type {UpstreamType}; reported to the client as {ReportedType}): {Reason}")]
    internal static partial void StreamFailed(
        ILogger logger,
        string reason,
        int upstreamCode,
        string upstreamType,
        string reportedType);

    /// <summary>Records how much a streamed turn produced, once it has ended.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="chunks">The number of chunks that were read.</param>
    /// <param name="skipped">The number of chunks that could not be read.</param>
    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "NIM stream ended after {Chunks} readable chunks and {Skipped} skipped.")]
    internal static partial void StreamCompleted(ILogger logger, int chunks, int skipped);

    /// <summary>Records that the advertised listing is being built from the built-in profiles.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <remarks>
    /// The listing a client is shown is now the proxy's own idea of the catalogue rather than
    /// NVIDIA's, so a model that exists upstream can be missing from the picker and one that was
    /// retired can still be offered. That is a degraded state, not a routine one.
    /// </remarks>
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Falling back to the built-in NVIDIA NIM model list; the picker no longer reflects NVIDIA's catalogue.")]
    internal static partial void UsingBuiltInModelList(ILogger logger);

    /// <summary>Records that a Messages API turn's own upstream call failed at the transport level.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="error">The transport failure.</param>
    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Warning,
        Message = "The upstream connection failed or timed out before a response arrived.")]
    internal static partial void LogUpstreamTransportFailure(ILogger logger, Exception error);

    /// <summary>Records that a Messages API turn is being sent upstream.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="requestedModel">The Claude model name the client asked for.</param>
    /// <param name="nimModel">The NIM model it was resolved to.</param>
    /// <param name="streaming">Whether the turn was requested as a stream.</param>
    /// <remarks>
    /// Without this, nothing in the log distinguishes a request that never reached the proxy from
    /// one that reached it and was rejected — both look like silence. This is the record that a
    /// turn was accepted and where it was routed, before anything about the upstream's answer is
    /// known.
    /// </remarks>
    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Sending a turn for {RequestedModel} to NIM as {NimModel} (streaming: {Streaming}).")]
    internal static partial void SendingTurn(ILogger logger, string requestedModel, string nimModel, bool streaming);

    /// <summary>Records the status a turn's upstream call came back with.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="nimModel">The NIM model the turn was sent to.</param>
    /// <param name="status">The status the upstream returned.</param>
    /// <param name="elapsedMilliseconds">How long the call took to answer.</param>
    /// <remarks>
    /// <see cref="SendingTurn"/> records only that a turn went out. Several in a row with nothing
    /// between them is what a log of retried, failed and succeeded turns all look like, so the
    /// status each one came back with is recorded to pair with it.
    /// </remarks>
    [LoggerMessage(
        EventId = 1019,
        Level = LogLevel.Information,
        Message = "NIM answered for {NimModel} with status {Status} after {ElapsedMilliseconds}ms.")]
    internal static partial void TurnAnswered(
        ILogger logger,
        string nimModel,
        int status,
        long elapsedMilliseconds);

    /// <summary>Records that the upstream rejected a Messages API turn outright.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="status">The status the upstream returned.</param>
    /// <param name="body">The upstream's own error body, truncated.</param>
    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Warning,
        Message = "NIM rejected a turn with status {Status}: {Body}")]
    internal static partial void TurnRejectedByUpstream(ILogger logger, int status, string body);

    /// <summary>Records that the proxy's own rate limit turned away a turn before it reached NIM.</summary>
    /// <param name="logger">The log to write to.</param>
    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Warning,
        Message = "The proxy's own rate limit is saturated; a turn was rejected before reaching NIM.")]
    internal static partial void RateLimitSaturated(ILogger logger);

    /// <summary>Records the exact request sent upstream, for diagnosing why a turn's content is wrong.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="body">The serialized upstream request.</param>
    /// <remarks>
    /// The other log messages establish that a turn was sent and what came back at the transport
    /// level; neither says anything about the shape of the request itself, which is what a client
    /// sending something this proxy translates incorrectly needs. Debug-only and off by default,
    /// since a tool schema can be large and every turn would otherwise pay to log one.
    /// </remarks>
    [LoggerMessage(
        EventId = 1013,
        Level = LogLevel.Debug,
        Message = "Upstream request body: {Body}")]
    internal static partial void UpstreamRequestBody(ILogger logger, string body);

    /// <summary>Records a tool call the proxy lifted out of answer text rather than being handed.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="tool">The tool the recovered call names.</param>
    /// <remarks>
    /// The two ways a call reaches a client are indistinguishable once it gets there, and they
    /// fail differently: a call NIM parsed carries the model's arguments verbatim, while a
    /// recovered one carries arguments this proxy assembled from marker tokens. When a call
    /// arrives with arguments that make no sense, that is the first thing worth knowing, and
    /// without this line it cannot be told from the log at all.
    /// </remarks>
    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Warning,
        Message = "Recovered a {Tool} call from answer text; its arguments were assembled here, not parsed by NIM.")]
    internal static partial void EmbeddedToolCallRecovered(ILogger logger, string tool);

    /// <summary>Records the whole body of a non-streamed upstream response.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="body">The upstream response body.</param>
    /// <remarks>
    /// <see cref="RawStreamLine"/> does this for a streamed turn and had no counterpart here, so a
    /// non-streamed turn that translated cleanly into wrong content could not be compared against
    /// what NIM actually sent. Debug-only, since a turn's body can be large.
    /// </remarks>
    [LoggerMessage(
        EventId = 1021,
        Level = LogLevel.Debug,
        Message = "Upstream response body: {Body}")]
    internal static partial void UpstreamResponseBody(ILogger logger, string body);

    /// <summary>Records one raw line read from a streamed upstream response.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="line">The raw line, including lines this proxy already parses successfully.</param>
    /// <remarks>
    /// <see cref="StreamChunkUnreadable"/> only records a line once parsing has already failed;
    /// this records every line, so a turn that translates cleanly but into the wrong content can
    /// still be compared against what NIM actually sent. Debug-only for the same reason.
    /// </remarks>
    [LoggerMessage(
        EventId = 1014,
        Level = LogLevel.Debug,
        Message = "Upstream stream line: {Line}")]
    internal static partial void RawStreamLine(ILogger logger, string line);
}
