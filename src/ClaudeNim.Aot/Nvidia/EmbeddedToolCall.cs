// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>One run of model output, classified as answer text or as a tool call.</summary>
/// <param name="Name">The tool being called, or an empty string for a run of text.</param>
/// <param name="Payload">The call's JSON arguments, or the text of a text run.</param>
/// <remarks>
/// The two are carried by one type because they arrive interleaved in a single stream of content
/// and have to be emitted in the order they were produced.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("EmbeddedToolCall: {Name}")]
public readonly record struct EmbeddedToolCall(string Name, string Payload)
{
    /// <summary>Gets a value indicating whether this run is a tool call rather than text.</summary>
    public bool IsCall => Name.Length > 0;

    /// <summary>Creates a run of answer text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The created run.</returns>
    public static EmbeddedToolCall ForText(string text) => new(string.Empty, text);

    /// <summary>Creates a tool call.</summary>
    /// <param name="name">The tool being called.</param>
    /// <param name="arguments">The call's JSON arguments.</param>
    /// <returns>The created run.</returns>
    public static EmbeddedToolCall ForCall(string name, string arguments) => new(name, arguments);
}
