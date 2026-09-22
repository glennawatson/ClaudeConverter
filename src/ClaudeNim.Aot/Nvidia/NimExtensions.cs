// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>NVIDIA's own extensions to the OpenAI request body.</summary>
/// <param name="MaxThinkingTokens">A ceiling on the tokens the model may spend reasoning.</param>
/// <remarks>
/// <para>
/// This is where NVIDIA puts the fields that have no OpenAI equivalent. Sending an unknown member
/// here is rejected outright with a list of the accepted ones, which makes it a far safer channel
/// than the chat template — where an unknown argument is passed to the template and can fail deep
/// inside generation instead.
/// </para>
/// <para>
/// <paramref name="MaxThinkingTokens"/> is the documented home for a reasoning budget. Not every
/// runner implements it: Nemotron 3 Ultra rejects it with a 400, which is why it is only sent when
/// the caller explicitly asked for a budget and why the transport retries without it.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimExtensions: {ToString(),nq}")]
public sealed record NimExtensions(
    [property: JsonPropertyName("max_thinking_tokens")] int? MaxThinkingTokens = null);
