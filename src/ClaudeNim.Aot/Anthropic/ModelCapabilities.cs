// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>What a listed model can do, as reported by the Models API.</summary>
/// <param name="Batch">Whether the model can be used through the Message Batches API.</param>
/// <param name="Citations">Whether the model generates citations.</param>
/// <param name="CodeExecution">Whether the model can use the server-side code execution tool.</param>
/// <param name="ContextManagement">Which server-side context strategies the model offers.</param>
/// <param name="Effort">Which <c>output_config.effort</c> levels the model accepts.</param>
/// <param name="ImageInput">Whether the model accepts image content blocks.</param>
/// <param name="PdfInput">Whether the model accepts PDF content blocks.</param>
/// <param name="StructuredOutputs">Whether the model supports strict schemas and JSON mode.</param>
/// <param name="Thinking">Whether the model reasons, and in which forms.</param>
/// <remarks>
/// <para>
/// This is the object a client interrogates before it decides what it is allowed to ask for — the
/// effort level it may drop to, whether reasoning can be requested adaptively, whether an image
/// can be attached. A proxy that omits it, or that reports a shape of its own invention, leaves
/// the client guessing and usually guessing high.
/// </para>
/// <para>
/// Every member here is reported from what this proxy can genuinely deliver over NVIDIA NIM, not
/// copied from Anthropic's own listing. Claiming a capability the upstream lacks fails later, at
/// the point of use, where the client can no longer choose differently.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ModelCapabilities: {ToString(),nq}")]
public readonly record struct ModelCapabilities(
    [property: JsonPropertyName("batch")] CapabilitySupport Batch,
    [property: JsonPropertyName("citations")] CapabilitySupport Citations,
    [property: JsonPropertyName("code_execution")] CapabilitySupport CodeExecution,
    [property: JsonPropertyName("context_management")] ContextManagementCapability ContextManagement,
    [property: JsonPropertyName("effort")] EffortCapability Effort,
    [property: JsonPropertyName("image_input")] CapabilitySupport ImageInput,
    [property: JsonPropertyName("pdf_input")] CapabilitySupport PdfInput,
    [property: JsonPropertyName("structured_outputs")] CapabilitySupport StructuredOutputs,
    [property: JsonPropertyName("thinking")] ThinkingCapability Thinking)
{
    /// <summary>Creates the capabilities the proxy advertises for one model.</summary>
    /// <param name="vision">Whether the model accepts image input.</param>
    /// <param name="thinking">Whether the model produces a reasoning trace.</param>
    /// <returns>The advertised capabilities.</returns>
    /// <remarks>
    /// <para>
    /// Batching, citations, code execution, PDF input and context management are reported as
    /// absent. Each is an Anthropic service feature rather than a model one, and none of them
    /// exists on the NIM side of this proxy.
    /// </para>
    /// <para>
    /// Structured outputs are reported as available: NVIDIA NIM accepts an OpenAI-style
    /// <c>response_format</c> carrying a JSON schema, which is the same guarantee the Anthropic
    /// capability describes.
    /// </para>
    /// </remarks>
    public static ModelCapabilities For(bool vision, bool thinking) =>
        new(
            CapabilitySupport.No,
            CapabilitySupport.No,
            CapabilitySupport.No,
            ContextManagementCapability.None,
            EffortCapability.For(thinking),
            CapabilitySupport.For(vision),
            CapabilitySupport.No,
            CapabilitySupport.Yes,
            ThinkingCapability.For(thinking));
}
