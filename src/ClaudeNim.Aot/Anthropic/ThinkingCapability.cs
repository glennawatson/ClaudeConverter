// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Whether a listed model reasons, and in which forms it can be asked to.</summary>
/// <param name="Supported">Whether the model produces a reasoning trace.</param>
/// <param name="Types">The <c>thinking.type</c> forms the model accepts.</param>
[System.Diagnostics.DebuggerDisplay("ThinkingCapability: {Supported}")]
public readonly record struct ThinkingCapability(
    [property: JsonPropertyName("supported")] bool Supported,
    [property: JsonPropertyName("types")] ThinkingTypes Types)
{
    /// <summary>Creates the capability for a model.</summary>
    /// <param name="reasons">Whether the model produces a reasoning trace.</param>
    /// <returns>The capability.</returns>
    /// <remarks>
    /// Both forms are advertised together, because the proxy accepts both: the pre-4.6
    /// <c>enabled</c> form with a budget and the current <c>adaptive</c> form reduce to the same
    /// on-or-off decision before they reach NIM.
    /// </remarks>
    public static ThinkingCapability For(bool reasons)
    {
        var form = CapabilitySupport.For(reasons);
        return new(reasons, new ThinkingTypes(form, form));
    }
}
