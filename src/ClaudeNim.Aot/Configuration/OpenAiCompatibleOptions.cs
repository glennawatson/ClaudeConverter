// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Configures a hosted OpenAI-compatible endpoint — literal OpenAI, or Azure AI Foundry's Models endpoint.</summary>
/// <param name="Enabled">Whether a model naming this provider may actually be reached.</param>
/// <param name="BaseUrl">The endpoint's base address, rooted where <c>chat/completions</c> resolves against it.</param>
/// <param name="ApiKey">The bearer credential to send, or empty to send no <c>Authorization</c> header at all.</param>
/// <param name="ReadSeconds">How long a streamed call may wait for its headers.</param>
/// <param name="CompletionSeconds">How long a non-streamed call may take from start to finish.</param>
/// <remarks>
/// Left disabled and unconfigured, a model naming this provider simply never answers, which is the
/// state every deployment starts in until it opts in with a real endpoint and key. See
/// <see cref="OllamaOptions"/> for the same shape configured for a local server instead.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("OpenAiCompatibleOptions: {BaseUrl}")]
public sealed record OpenAiCompatibleOptions(
    bool Enabled = false,
    string BaseUrl = "",
    string ApiKey = "",
    int ReadSeconds = 60,
    int CompletionSeconds = 600)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "OpenAi";
}
