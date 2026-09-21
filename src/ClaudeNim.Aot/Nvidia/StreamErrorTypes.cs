// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Maps a mid-stream upstream failure onto the Anthropic error class for it.</summary>
/// <remarks>
/// A client decides whether to retry, back off, or give up from the error <c>type</c>. A failure
/// that arrives inside a 200 response still carries the status it would have had, so that status
/// is what the classification is taken from.
/// </remarks>
public static class StreamErrorTypes
{
    /// <summary>The class used when the upstream gave no usable status.</summary>
    private const string Fallback = "api_error";

    /// <summary>Classifies a mid-stream failure.</summary>
    /// <param name="code">The status the upstream reported alongside the failure.</param>
    /// <returns>The Anthropic error class.</returns>
    public static string FromUpstream(int? code) =>
        code is { } status ? Endpoints.AnthropicErrors.TypeFor(status) : Fallback;
}
