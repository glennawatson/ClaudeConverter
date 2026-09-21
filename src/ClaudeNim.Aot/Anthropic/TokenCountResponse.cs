// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>The body returned from <c>POST /v1/messages/count_tokens</c>.</summary>
/// <param name="InputTokens">The number of tokens the request would consume.</param>
[System.Diagnostics.DebuggerDisplay("TokenCountResponse: {ToString(),nq}")]
public readonly record struct TokenCountResponse(
    [property: JsonPropertyName("input_tokens")] int InputTokens);
