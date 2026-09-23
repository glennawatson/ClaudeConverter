// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Configures real Anthropic as a fallback behind NVIDIA NIM.</summary>
/// <param name="Enabled">Whether a model naming this provider may actually be reached.</param>
/// <param name="BaseUrl">Anthropic's own base address, rooted where <c>v1/messages</c> resolves against it.</param>
/// <param name="ApiKey">The Anthropic API key, sent as <c>x-api-key</c> rather than a bearer token.</param>
/// <param name="ApiVersion">The <c>anthropic-version</c> header every call is sent with.</param>
/// <param name="ReadSeconds">How long a streamed call may wait for its headers.</param>
/// <param name="CompletionSeconds">How long a non-streamed call may take from start to finish.</param>
/// <remarks>
/// <para>
/// A model addressed this way is not translated at all: the Messages API is this proxy's own
/// client-facing wire format already, so the caller's request is forwarded close to verbatim —
/// only the model field changes — and the response is passed back byte for byte rather than run
/// through <see cref="Nvidia.NimStreamTranslator"/> or <see cref="Endpoints.Anthropic.ICompletionTranslator"/>,
/// neither of which understands Anthropic's own shape. This only exists behind the Anthropic
/// route: an OpenAI Responses API client has no equivalent passthrough, since real Anthropic does
/// not speak that shape.
/// </para>
/// <para>
/// This is deliberately a standalone API key rather than the OAuth credential a Claude Pro or Max
/// subscription authenticates with — Anthropic's own Consumer Terms of Service reserve that
/// credential for the official Claude Code CLI and Claude.ai, and enforce it server-side outside
/// of them.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("AnthropicApiOptions: {BaseUrl}")]
public sealed record AnthropicApiOptions(
    bool Enabled = false,
    string BaseUrl = "https://api.anthropic.com",
    string ApiKey = "",
    string ApiVersion = "2023-06-01",
    int ReadSeconds = 120,
    int CompletionSeconds = 600)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "AnthropicApi";
}
