// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>One entry of the Models API listing.</summary>
/// <param name="Id">The identifier a client passes as <c>model</c>.</param>
/// <param name="DisplayName">The human-readable name shown in a model picker.</param>
/// <param name="CreatedAt">When the model was published.</param>
/// <param name="MaxInputTokens">The context window, in tokens.</param>
/// <param name="MaxTokens">The output ceiling, in tokens.</param>
/// <param name="Capabilities">What the model supports.</param>
/// <param name="Type">The object discriminator, always <c>model</c>.</param>
/// <remarks>
/// The sizing fields are <c>max_input_tokens</c> and <c>max_tokens</c>; the Models API has no
/// <c>context_window</c> member. Proxies that invent one, or that emit the OpenAI
/// <c>object</c>/<c>created</c> pair beside the Anthropic <c>type</c>/<c>created_at</c> pair,
/// produce a listing clients cannot size a request against.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ModelDescriptor: {ToString(),nq}")]
public sealed record ModelDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("max_input_tokens")] int MaxInputTokens,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("capabilities")] ModelCapabilities Capabilities,
    [property: JsonPropertyName("type")] string Type = "model");
