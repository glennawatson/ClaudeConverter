// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>One spelling of the tool-call shape that names each argument with its own tag.</summary>
/// <param name="CallOpen">The token that opens a call.</param>
/// <param name="CallClose">The token that closes a call.</param>
/// <param name="FunctionOpen">The token that opens the tag naming the tool.</param>
/// <param name="FunctionClose">The token that closes the tag naming the tool.</param>
/// <param name="ParameterOpen">The token that opens the tag naming one argument.</param>
/// <param name="ParameterClose">The token that closes the tag naming one argument.</param>
[System.Diagnostics.DebuggerDisplay("NamedCallForm: {CallOpen}")]
public readonly record struct NamedCallForm(
    string CallOpen,
    string CallClose,
    string FunctionOpen,
    string FunctionClose,
    string ParameterOpen,
    string ParameterClose);
