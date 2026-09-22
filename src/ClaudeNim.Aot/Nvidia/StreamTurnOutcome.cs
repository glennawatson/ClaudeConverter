// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>How a streamed turn ended, and whether anything can still be done about it.</summary>
/// <remarks>
/// The distinction that matters is not success against failure but whether the client has already
/// been given part of an answer. Once a content block is open the turn is committed: the only
/// honest thing left is to report the failure on the stream. Before that, nothing has been
/// promised and the whole turn can be asked for again without the client ever knowing.
/// </remarks>
public enum StreamTurnOutcome
{
    /// <summary>The turn was translated to its end.</summary>
    Completed = 0,

    /// <summary>The turn failed after the client had already been given output.</summary>
    Failed = 1,

    /// <summary>The turn failed before producing anything, so it can be asked for again.</summary>
    FailedBeforeOutput = 2,
}
