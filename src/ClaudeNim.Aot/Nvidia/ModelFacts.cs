// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The advertised facts about one NIM model, after defaults have been applied.</summary>
/// <param name="DisplayName">The name shown in a model picker.</param>
/// <param name="MaxInputTokens">The context window, in tokens.</param>
/// <param name="MaxTokens">The output ceiling, in tokens.</param>
/// <param name="Vision">Whether the model accepts image input.</param>
/// <param name="Thinking">Whether the model produces a reasoning trace.</param>
internal readonly record struct ModelFacts(
    string DisplayName,
    int MaxInputTokens,
    int MaxTokens,
    bool Vision,
    bool Thinking)
{
    /// <summary>Gets the capabilities these facts advertise.</summary>
    internal ModelCapabilities Capabilities => ModelCapabilities.For(Vision, Thinking);

    /// <summary>Produces the same facts with reasoning suppressed.</summary>
    /// <returns>The facts, with reasoning reported as unavailable.</returns>
    internal ModelFacts WithoutThinking() => this with { Thinking = false };
}
