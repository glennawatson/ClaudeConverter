// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Whether one capability of a listed model is available.</summary>
/// <param name="Supported">Whether the model supports the capability.</param>
/// <remarks>
/// The Models API wraps every capability in an object rather than using a bare boolean, so that
/// a capability can later gain detail without breaking a client that already reads it.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("CapabilitySupport: {Supported}")]
public readonly record struct CapabilitySupport(
    [property: JsonPropertyName("supported")] bool Supported)
{
    /// <summary>Gets the value stating that a capability is available.</summary>
    public static CapabilitySupport Yes => new(true);

    /// <summary>Gets the value stating that a capability is not available.</summary>
    public static CapabilitySupport No => new(false);

    /// <summary>Creates a value from a flag.</summary>
    /// <param name="supported">Whether the capability is available.</param>
    /// <returns>The matching value.</returns>
    public static CapabilitySupport For(bool supported) => new(supported);
}
