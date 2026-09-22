// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>One spelling of the tool-call shape that carries its arguments as a JSON blob.</summary>
/// <param name="Open">The token that opens a call.</param>
/// <param name="Separator">The token that divides the tool name from its arguments.</param>
/// <param name="Close">The token that closes a call.</param>
[System.Diagnostics.DebuggerDisplay("EmbeddedCallForm: {Open}")]
public readonly record struct EmbeddedCallForm(string Open, string Separator, string Close);
