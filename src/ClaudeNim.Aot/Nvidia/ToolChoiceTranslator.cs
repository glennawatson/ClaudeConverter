// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.Text.Json;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Maps the Anthropic <c>tool_choice</c> object onto the OpenAI-style value NIM accepts.</summary>
/// <remarks>
/// The two vocabularies differ: Anthropic's <c>any</c> is OpenAI's <c>required</c>, and
/// Anthropic's <c>tool</c> becomes a nested function reference. A proxy that sends a constant
/// <c>auto</c> whenever tools are present silently drops forced tool use, so a client that asked
/// the model to call a specific tool gets a free-form answer instead.
/// </remarks>
public static class ToolChoiceTranslator
{
    /// <summary>The initial buffer size for named function JSON serialization.</summary>
    private const int NamedFunctionBufferBytes = 64;

    /// <summary>The backing document for the <c>auto</c> tool choice value.</summary>
    private static readonly JsonDocument AutoDocument = JsonDocument.Parse("\"auto\"");

    /// <summary>The backing document for the <c>required</c> tool choice value.</summary>
    private static readonly JsonDocument RequiredDocument = JsonDocument.Parse("\"required\"");

    /// <summary>The backing document for the <c>none</c> tool choice value.</summary>
    private static readonly JsonDocument NoneDocument = JsonDocument.Parse("\"none\"");

    /// <summary>Gets the value that lets the model decide whether to call a tool.</summary>
    public static JsonElement Auto => AutoDocument.RootElement;

    /// <summary>Translates an Anthropic tool choice.</summary>
    /// <param name="toolChoice">The caller's <c>tool_choice</c>, which may be absent.</param>
    /// <returns>The upstream value, or <see langword="null"/> when the caller expressed no preference.</returns>
    public static JsonElement? Translate(JsonElement? toolChoice) =>
        toolChoice is { ValueKind: JsonValueKind.Object } choice
        && choice.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
            ? type.GetString() switch
            {
                "auto" => Auto,
                "any" => RequiredDocument.RootElement,
                "none" => NoneDocument.RootElement,
                "tool" => NamedFunctionOrAuto(choice),
                _ => null,
            }
            : null;

    /// <summary>Reads whether the caller asked to suppress parallel tool calls.</summary>
    /// <param name="toolChoice">The caller's <c>tool_choice</c>, which may be absent.</param>
    /// <returns><see langword="true"/> when parallel calls were explicitly disabled.</returns>
    public static bool DisablesParallelToolCalls(JsonElement? toolChoice) =>
        toolChoice is { ValueKind: JsonValueKind.Object } choice
        && choice.TryGetProperty("disable_parallel_tool_use", out var flag)
        && flag.ValueKind == JsonValueKind.True;

    /// <summary>Returns a tool schema, substituting an empty object schema when the caller sent none.</summary>
    /// <param name="schema">The declared schema.</param>
    /// <returns>A usable schema.</returns>
    public static JsonElement SchemaOrEmpty(JsonElement? schema) =>
        schema is { ValueKind: JsonValueKind.Object } value ? value : JsonElements.EmptyObjectSchema;

    /// <summary>Builds a JSON document for a named function tool choice.</summary>
    /// <param name="toolName">The tool name to encode.</param>
    /// <returns>The named function choice element.</returns>
    /// <remarks>
    /// Internal rather than private: the Responses API's own named-tool-choice shape nests the
    /// name one level shallower than Anthropic's, but decodes to this same NIM-side shape once the
    /// name is extracted, so <see cref="Nvidia.CodexRequestBuilder"/> reuses this rather than
    /// building the JSON a second time.
    /// </remarks>
    internal static JsonElement NamedFunction(string toolName)
    {
        var buffer = new ArrayBufferWriter<byte>(NamedFunctionBufferBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WriteStartObject("function");
            writer.WriteString("name", toolName);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.WrittenMemory).RootElement.Clone();
    }

    /// <summary>Extracts the tool name and builds a named function choice, or returns auto if missing.</summary>
    /// <param name="choice">The tool choice object.</param>
    /// <returns>The named function choice, or the auto choice if the name is missing.</returns>
    private static JsonElement NamedFunctionOrAuto(JsonElement choice) =>
        choice.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? NamedFunction(name.GetString() ?? string.Empty)
            : Auto;
}
