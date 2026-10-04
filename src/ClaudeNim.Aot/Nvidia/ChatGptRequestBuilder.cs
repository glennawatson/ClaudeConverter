// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Builds a subscription request from the shared conversation.</summary>
internal static class ChatGptRequestBuilder
{
    /// <summary>The function tool discriminator.</summary>
    private const string FunctionType = "function";

    /// <summary>Creates the request body.</summary>
    /// <param name="request">The shared conversation.</param>
    /// <returns>The subscription request.</returns>
    internal static JsonElement Build(NimChatRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model);
            writer.WriteBoolean("stream", true);
            writer.WriteBoolean("store", false);
            writer.WriteStartArray("input");
            foreach (var message in request.Messages)
            {
                WriteMessage(writer, message);
            }

            writer.WriteEndArray();
            if (request.ReasoningEffort is { } effort)
            {
                writer.WriteStartObject("reasoning");
                writer.WriteString(nameof(effort), effort);
                writer.WriteEndObject();
            }

            WriteTools(writer, request.Tools);
            WriteToolChoice(writer, request.ToolChoice);
            if (request.ParallelToolCalls is { } parallel)
            {
                writer.WriteBoolean("parallel_tool_calls", parallel);
            }

            if (request.ResponseFormat is { } format)
            {
                writer.WriteStartObject("text");
                writer.WriteStartObject(nameof(format));
                writer.WriteString("type", format.GetProperty("type").GetString());
                if (format.TryGetProperty("json_schema", out var schema))
                {
                    writer.WriteString("name", "response");
                    writer.WritePropertyName(nameof(schema));
                    schema.GetProperty(nameof(schema)).WriteTo(writer);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
        return document.RootElement.Clone();
    }

    /// <summary>Writes text, images, and tool history.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="message">The message to write.</param>
    private static void WriteMessage(Utf8JsonWriter writer, NimChatMessage message)
    {
        if (message.Role == "tool")
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function_call_output");
            writer.WriteString("call_id", message.ToolCallId);
            writer.WriteString("output", message.Content?.Text ?? string.Empty);
            writer.WriteEndObject();
            return;
        }

        WriteContent(writer, message);
        WriteCalls(writer, message.ToolCalls);
    }

    /// <summary>Writes a message body.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="message">The message to write.</param>
    private static void WriteContent(Utf8JsonWriter writer, NimChatMessage message)
    {
        var content = message.Content;
        if (content?.Text is not { Length: > 0 } && content?.Parts is not { Count: > 0 })
        {
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("role", message.Role == "system" ? "developer" : message.Role);
        writer.WriteStartArray(nameof(content));
        if (content?.Text is { } text)
        {
            WriteText(writer, message.Role, text);
        }

        if (content?.Parts is { } parts)
        {
            WriteParts(writer, message.Role, parts);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Writes multimodal parts.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="role">The message author.</param>
    /// <param name="parts">The message parts.</param>
    private static void WriteParts(Utf8JsonWriter writer, string role, List<NimContentPart> parts)
    {
        foreach (var part in parts)
        {
            if (part.ImageUrl is { } image)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "input_image");
                writer.WriteString("image_url", image.Url);
                writer.WriteEndObject();
            }
            else if (part.Text is { } partText)
            {
                WriteText(writer, role, partText);
            }
        }
    }

    /// <summary>Writes tool call history.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="calls">The calls to write.</param>
    private static void WriteCalls(Utf8JsonWriter writer, List<NimToolCall>? calls)
    {
        if (calls is not null)
        {
            foreach (var call in calls)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "function_call");
                writer.WriteString("call_id", call.Id);
                writer.WriteString("name", call.Function?.Name);
                writer.WriteString("arguments", call.Function?.Arguments);
                writer.WriteEndObject();
            }
        }
    }

    /// <summary>Writes a message text part.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="role">The message author.</param>
    /// <param name="text">The part text.</param>
    private static void WriteText(Utf8JsonWriter writer, string role, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", role == "assistant" ? "output_text" : "input_text");
        writer.WriteString(nameof(text), text);
        writer.WriteEndObject();
    }

    /// <summary>Groups tools in the subscription namespace.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="tools">The caller's tools.</param>
    private static void WriteTools(Utf8JsonWriter writer, List<NimTool>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return;
        }

        writer.WriteStartArray(nameof(tools));
        writer.WriteStartObject();
        writer.WriteString("type", "namespace");
        writer.WriteString("name", "proxy");
        writer.WriteString("description", "Tools supplied by the caller.");
        writer.WriteStartArray(nameof(tools));
        foreach (var tool in tools)
        {
            writer.WriteStartObject();
            writer.WriteString("type", FunctionType);
            writer.WriteString("name", tool.Function.Name);
            writer.WriteString("description", tool.Function.Description);
            writer.WriteBoolean("strict", false);
            writer.WritePropertyName("parameters");
            tool.Function.Parameters.WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    /// <summary>Preserves the caller's tool choice.</summary>
    /// <param name="writer">The JSON destination.</param>
    /// <param name="choice">The caller's tool choice.</param>
    private static void WriteToolChoice(Utf8JsonWriter writer, JsonElement? choice)
    {
        if (choice is not { } value)
        {
            return;
        }

        writer.WritePropertyName("tool_choice");
        if (value.ValueKind == JsonValueKind.String)
        {
            value.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", FunctionType);
        writer.WriteString("namespace", "proxy");
        writer.WriteString("name", value.GetProperty(FunctionType).GetProperty("name").GetString());
        writer.WriteEndObject();
    }
}
