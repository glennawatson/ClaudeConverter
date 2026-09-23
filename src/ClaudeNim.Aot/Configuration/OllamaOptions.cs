// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Configures a local Ollama server.</summary>
/// <param name="Enabled">Whether a model naming this provider may actually be reached.</param>
/// <param name="BaseUrl">The server's OpenAI-compatible base address, rooted where <c>chat/completions</c> resolves against it.</param>
/// <param name="ApiKey">The bearer credential to send, or empty to send no <c>Authorization</c> header at all.</param>
/// <param name="ReadSeconds">How long a streamed call may wait for its headers.</param>
/// <param name="CompletionSeconds">How long a non-streamed call may take from start to finish.</param>
/// <remarks>
/// NVIDIA's free tier is what everything else in this proxy is built to work around — cooldowns,
/// pacing, fallback chains — but all of that is still one account subject to one shared quota. A
/// local model has none of that: it never returns a 429, never saturates from other tenants, and
/// never gets deprecated out from under a deployment on ten days' notice. What it does have is
/// whatever the host machine's own hardware can generate at, which for a model sized to actually
/// help with a coding turn is usually far slower than a NIM endpoint having a good day — which is
/// why <see cref="ReadSeconds"/> and <see cref="CompletionSeconds"/> default generously here.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("OllamaOptions: {BaseUrl}")]
public sealed record OllamaOptions(
    bool Enabled = false,
    string BaseUrl = "http://localhost:11434/v1",
    string ApiKey = "",
    int ReadSeconds = 60,
    int CompletionSeconds = 600)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Ollama";
}
