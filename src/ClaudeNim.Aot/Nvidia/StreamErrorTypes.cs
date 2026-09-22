// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Maps a mid-stream upstream failure onto the Anthropic error class for it.</summary>
/// <remarks>
/// A client decides whether to retry, back off, or give up from the error <c>type</c>. A failure
/// that arrives inside a 200 response still carries the status it would have had, so that status
/// is what the classification is taken from where there is one.
/// </remarks>
public static class StreamErrorTypes
{
    /// <summary>The class used when the upstream gave nothing to classify on.</summary>
    private const string Fallback = "api_error";

    /// <summary>The class that tells a client to back off and try the turn again.</summary>
    private const string Overloaded = "overloaded_error";

    /// <summary>The word NVIDIA uses when it is turning work away for capacity.</summary>
    private const string SaturationWord = "overload";

    /// <summary>Classifies a mid-stream failure.</summary>
    /// <param name="failure">The failure the upstream reported.</param>
    /// <returns>The Anthropic error class.</returns>
    /// <remarks>
    /// <para>
    /// NVIDIA reports mid-stream saturation as prose with no status and no type —
    /// <c>Service temporarily overloaded</c> — and the fallback for that is <c>api_error</c>,
    /// which a client treats as fatal. So a turn that failed for a reason that clears on its own
    /// ends the work instead of being retried, which is the opposite of what the status would have
    /// said had the same failure arrived before the headers.
    /// </para>
    /// <para>
    /// Reading the prose is not a good way to learn a status and is only reached when the upstream
    /// supplied neither of the two fields that are. It costs nothing when wrong: a client that
    /// retries a genuinely fatal turn gets the same failure again, where one that gives up on a
    /// transient turn has already lost it.
    /// </para>
    /// </remarks>
    public static string FromUpstream(NimStreamError? failure)
    {
        if (failure?.Code is { } status)
        {
            return Endpoints.AnthropicErrors.TypeFor(status);
        }

        if (failure?.Type is { Length: > 0 } declared)
        {
            return declared;
        }

        return failure?.Message?.Contains(SaturationWord, StringComparison.OrdinalIgnoreCase) == true
            ? Overloaded
            : Fallback;
    }
}
