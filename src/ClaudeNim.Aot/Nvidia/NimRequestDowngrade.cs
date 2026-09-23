// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Strips the optional parts of a request that a model may not accept.</summary>
/// <remarks>
/// <para>
/// What a NIM model tolerates varies across the catalogue, and a model that does not know a field
/// fails the whole call rather than ignoring it. Rather than keep a per-model table that would go
/// stale, the transport sends the request it wants and walks down these rungs when it is refused.
/// </para>
/// <para>
/// The order matters: the least costly thing to lose goes first. Reasoning controls only shape how
/// the model thinks, so dropping them still answers the question. Replayed reasoning is part of the
/// conversation, so it is given up only when nothing else is left.
/// </para>
/// </remarks>
public static class NimRequestDowngrade
{
    /// <summary>The number of rungs the ladder has.</summary>
    /// <remarks>
    /// Used to give the caller's retry loop a genuine upper bound: the ladder can be walked at
    /// most this many times before every optional part has been stripped and it stops offering
    /// a lighter request.
    /// </remarks>
    internal const int RungCount = 2;

    /// <summary>The field name NVIDIA's own error text names when it rejects replayed reasoning.</summary>
    private const string ReasoningContentField = "reasoning_content";

    /// <summary>The field names NVIDIA's own error text names when it rejects the reasoning controls.</summary>
    private static readonly string[] ReasoningControlFields = ["chat_template_kwargs", "reasoning_effort", "max_thinking_tokens", "nvext"];

    /// <summary>Walks the ladder one rung for a status that means the upstream rejected the request.</summary>
    /// <param name="request">The request to downgrade.</param>
    /// <param name="status">
    /// The status the rejection carried, whether it arrived as the call's own HTTP status or, for a
    /// streamed call, as the status an error payload inside an already-successful response reports.
    /// </param>
    /// <param name="errorText">
    /// The upstream's own explanation of the rejection, or <see langword="null"/> to always walk the
    /// ladder in its fixed order instead of reading it.
    /// </param>
    /// <returns>The downgraded request, or <see langword="null"/> when the status is not a rejection or nothing is left to strip.</returns>
    /// <remarks>
    /// A streamed call's status is committed before generation begins, so a rejection can arrive
    /// inside the body of a <c>200</c> instead of as the response's own status. Both shapes name the
    /// same failure and call for the same ladder — this is the one place that walks it, so a
    /// streamed rejection is not left to exhaust the plain retry budget asking again with the exact
    /// body that was just refused.
    /// </remarks>
    public static NimChatRequest? ForRejection(NimChatRequest request, int? status, string? errorText)
    {
        if (status is not ((int)HttpStatusCode.BadRequest or (int)HttpStatusCode.InternalServerError))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(errorText))
        {
            if (errorText.Contains(ReasoningContentField, StringComparison.OrdinalIgnoreCase))
            {
                return WithoutReplayedReasoning(request);
            }

            foreach (var field in ReasoningControlFields)
            {
                if (errorText.Contains(field, StringComparison.OrdinalIgnoreCase))
                {
                    return WithoutReasoningControls(request);
                }
            }
        }

        return WithoutReasoningControls(request) ?? WithoutReplayedReasoning(request);
    }

    /// <summary>Removes the controls that ask the model to reason a particular way.</summary>
    /// <param name="request">The request to downgrade.</param>
    /// <returns>The downgraded request, or <see langword="null"/> when it carried no such controls.</returns>
    public static NimChatRequest? WithoutReasoningControls(NimChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var carries = request.ChatTemplateKwargs is not null
            || request.Extensions is not null
            || request.ReasoningEffort is not null;

        return carries
            ? request with { ChatTemplateKwargs = null, Extensions = null, ReasoningEffort = null }
            : null;
    }

    /// <summary>Removes the reasoning replayed on the assistant turns of the transcript.</summary>
    /// <param name="request">The request to downgrade.</param>
    /// <returns>The downgraded request, or <see langword="null"/> when no turn carried reasoning.</returns>
    /// <remarks>
    /// A model that rejects <c>reasoning_content</c> on input rejects it on every turn of every
    /// later request, so a conversation that once produced a reasoning trace would never recover.
    /// Dropping the trace loses the model's earlier thinking but keeps the conversation alive.
    /// </remarks>
    public static NimChatRequest? WithoutReplayedReasoning(NimChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = request.Messages;
        var carries = false;

        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].ReasoningContent is null)
            {
                continue;
            }

            carries = true;
            break;
        }

        if (!carries)
        {
            return null;
        }

        var stripped = new List<NimChatMessage>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            stripped.Add(message.ReasoningContent is null ? message : message with { ReasoningContent = null });
        }

        return request with { Messages = stripped };
    }
}
