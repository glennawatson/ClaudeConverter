// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>Why an incomplete Responses API turn stopped.</summary>
/// <param name="Reason">The stated reason, for example <c>max_output_tokens</c>.</param>
[System.Diagnostics.DebuggerDisplay("IncompleteDetails: {Reason}")]
public sealed record IncompleteDetails([property: JsonPropertyName("reason")] string Reason);
