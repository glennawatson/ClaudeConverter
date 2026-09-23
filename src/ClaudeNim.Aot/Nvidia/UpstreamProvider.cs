// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Which transport a model identifier resolves to.</summary>
public enum UpstreamProvider
{
    /// <summary>NVIDIA NIM, the default when a model identifier carries no recognised prefix.</summary>
    Nim = 0,

    /// <summary>A local Ollama server.</summary>
    Ollama = 1,

    /// <summary>An OpenAI-compatible endpoint — literal OpenAI, or Azure AI Foundry's Models endpoint.</summary>
    OpenAi = 2,

    /// <summary>Real Anthropic, reached close to verbatim through the Anthropic route only.</summary>
    Anthropic = 3,
}
