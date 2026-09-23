// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Routing;

namespace ClaudeNim.Aot.Endpoints.Anthropic;

/// <summary>The one turn being served, in the four forms the path below needs it in.</summary>
/// <param name="Request">The caller's request.</param>
/// <param name="UpstreamRequest">The translated body, kept so a streamed turn can be asked for again.</param>
/// <param name="Resolved">The routing outcome.</param>
/// <param name="MessageId">The identifier to report for the message.</param>
/// <remarks>
/// These four travel together from the moment routing is decided until the last event is written,
/// and passing them separately made every method along the way take most of its parameters just to
/// hand them on.
/// </remarks>
internal sealed record TurnContext(
    MessagesRequest Request,
    NimChatRequest UpstreamRequest,
    ResolvedModel Resolved,
    string MessageId);
