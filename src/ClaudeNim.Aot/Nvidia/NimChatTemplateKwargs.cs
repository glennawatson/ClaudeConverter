// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>
/// Arguments passed to the model's chat template, which is how NVIDIA NIM exposes reasoning
/// control on its Nemotron and DeepSeek families.
/// </summary>
/// <param name="EnableThinking">Whether the model emits a reasoning trace.</param>
/// <param name="Thinking">The older spelling of <paramref name="EnableThinking"/>, sent alongside it.</param>
/// <param name="LowEffort">
/// Whether the model should spend fewer reasoning tokens, as Nemotron 3 Super spells it.
/// </param>
/// <param name="MediumEffort">
/// Whether the model should spend fewer reasoning tokens, as Nemotron 3 Ultra spells it.
/// </param>
/// <param name="ForceNonemptyContent">
/// Whether the model must return a non-empty answer. NVIDIA documents this as required when
/// tools are combined with reasoning: without it a reasoning model can return a trace and no
/// content, which a coding client reads as an empty turn.
/// </param>
/// <remarks>
/// <para>
/// This is a top-level member of the request body. OpenAI SDKs reach it through their
/// <c>extra_body</c> escape hatch, which merges into the top level rather than nesting under a
/// member of that name; a proxy that builds the JSON itself and nests it under
/// <c>extra_body</c> sends a field the server ignores, silently losing reasoning control.
/// </para>
/// <para>
/// The effort flag is spelled per model family — <c>low_effort</c> on Nemotron 3 Super,
/// <c>medium_effort</c> on Nemotron 3 Ultra. Both are sent when the caller asks for reduced
/// effort, alongside the top-level <c>reasoning_effort</c> field that the current NIM models
/// accept directly.
/// </para>
/// <para>
/// Nothing that carries a number belongs here. An argument the template does not define is not
/// ignored — it is passed into the template and can fail during generation, which on a streamed
/// turn arrives as an error inside an otherwise successful response. Numeric controls go through
/// <see cref="NimExtensions"/>, where an unsupported field is rejected before generation starts.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("NimChatTemplateKwargs: {ToString(),nq}")]
public sealed record NimChatTemplateKwargs(
    [property: JsonPropertyName("enable_thinking")] bool? EnableThinking = null,
    [property: JsonPropertyName("thinking")] bool? Thinking = null,
    [property: JsonPropertyName("low_effort")] bool? LowEffort = null,
    [property: JsonPropertyName("medium_effort")] bool? MediumEffort = null,
    [property: JsonPropertyName("force_nonempty_content")] bool? ForceNonemptyContent = null);
