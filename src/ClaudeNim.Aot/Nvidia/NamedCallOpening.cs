// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Where a named-argument tool call opens, and the spelling it opened in.</summary>
/// <param name="Index">The offset the call opens at, or a negative value when there is none.</param>
/// <param name="Form">The spelling the call was written in.</param>
[System.Diagnostics.DebuggerDisplay("NamedCallOpening: {Index}")]
public readonly record struct NamedCallOpening(int Index, NamedCallForm Form)
{
    /// <summary>The index reported when the text holds no call opening.</summary>
    private const int None = -1;

    /// <summary>Gets the opening that reports no call at all.</summary>
    public static NamedCallOpening NotFound => new(None, default);

    /// <summary>Gets a value indicating whether an opening was found.</summary>
    public bool Found => Index >= 0;
}
