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
    /// <summary>Records that a model rejected the reasoning controls and the call is being retried.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="model">The model that rejected the request.</param>
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "NIM rejected {Model} with the reasoning controls set; retrying without them.")]
    internal static partial void ChatTemplateRejected(ILogger logger, string model);

    /// <summary>Records that a transient upstream failure is being waited out.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="status">The status the upstream returned.</param>
    /// <param name="delayMilliseconds">How long the next attempt waits.</param>
    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Information,
        Message = "NIM returned {Status}; retrying in {DelayMilliseconds}ms.")]
    internal static partial void RetryingAfterTransientFailure(
        ILogger logger,
        int status,
        double delayMilliseconds);

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

    /// <summary>Records that the upstream ended a streamed turn with a failure.</summary>
    /// <param name="logger">The log to write to.</param>
    /// <param name="reason">The failure the upstream reported.</param>
    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "NIM ended a streamed turn with a failure: {Reason}")]
    internal static partial void StreamFailed(ILogger logger, string reason);

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
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Falling back to the built-in NVIDIA NIM model list.")]
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
}
