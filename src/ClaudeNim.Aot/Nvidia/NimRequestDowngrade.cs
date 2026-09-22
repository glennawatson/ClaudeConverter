// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
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
