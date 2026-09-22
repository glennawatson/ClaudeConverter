// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>One flat environment variable, and the configuration key it feeds.</summary>
/// <param name="Variable">The environment variable name.</param>
/// <param name="Key">The configuration key it is bound to.</param>
[System.Diagnostics.DebuggerDisplay("EnvironmentBinding: {Variable} -> {Key}")]
internal readonly record struct EnvironmentBinding(string Variable, string Key);
