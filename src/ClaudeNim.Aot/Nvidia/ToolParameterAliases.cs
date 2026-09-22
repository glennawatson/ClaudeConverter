// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Renames the tool parameters NVIDIA NIM cannot accept, and puts them back afterwards.</summary>
/// <remarks>
/// <para>
/// A tool whose schema declares a parameter named <c>type</c> makes some NIM models answer with an
/// internal server error — Nemotron 3 Super does, GLM 5.3 does not. The name collides with the
/// discriminator the function-calling wrapper uses, and where it fails it fails as a 500 rather
/// than a validation error, so it reads as an outage rather than a request the caller could fix.
/// </para>
/// <para>
/// The rename is applied to every model rather than only the ones known to need it. The
/// alternative is a per-model table of reserved names, which would be wrong the moment NVIDIA
/// changed a runner; the rename costs nothing on a model that would have accepted the original,
/// because it is undone before the client sees the call.
/// </para>
/// <para>
/// The prefix is deliberately unlikely to occur in a real schema, so restoring it cannot corrupt
/// an argument the model genuinely meant to send.
/// </para>
/// </remarks>
public static class ToolParameterAliases
{
    /// <summary>The prefix an aliased parameter name carries.</summary>
    private const string Prefix = "nim_alias_";

    /// <summary>The initial size of the buffer a rewritten schema is composed into.</summary>
    private const int InitialBufferBytes = 1024;

    /// <summary>The parameter names NIM refuses to accept.</summary>
    private static readonly string[] Reserved = ["type"];

    /// <summary>Determines whether any of a request's tools will need aliasing.</summary>
    /// <param name="tools">The tools the caller declared.</param>
    /// <returns><see langword="true"/> when at least one schema uses a reserved name.</returns>
    /// <remarks>
    /// The streamed path uses this to decide whether tool arguments have to be held back and
    /// corrected before the client sees them. Aliasing is rare, so the common case keeps streaming
    /// each fragment the moment it arrives.
    /// </remarks>
    public static bool AppliesTo(List<Anthropic.ToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return false;
        }

        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i].InputSchema is { } schema && NeedsAliasing(schema))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rewrites a tool schema so none of its parameters use a reserved name.</summary>
    /// <param name="schema">The declared schema.</param>
    /// <returns>The schema, rewritten only when it needed to be.</returns>
    public static JsonElement Apply(JsonElement schema)
    {
        if (!NeedsAliasing(schema))
        {
            return schema;
        }

        var buffer = new ArrayBufferWriter<byte>(InitialBufferBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteAliased(writer, schema);
        }

        return JsonElement.Parse(buffer.WrittenSpan).Clone();
    }

    /// <summary>Restores the original parameter names in a tool call's arguments.</summary>
    /// <param name="arguments">The arguments the model produced.</param>
    /// <returns>The arguments, with any aliased name renamed back.</returns>
    public static JsonElement Restore(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !ContainsAlias(arguments))
        {
            return arguments;
        }

        var buffer = new ArrayBufferWriter<byte>(InitialBufferBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var member in arguments.EnumerateObject())
            {
                writer.WritePropertyName(
                    member.Name.StartsWith(Prefix, StringComparison.Ordinal)
                        ? member.Name[Prefix.Length..]
                        : member.Name);
                member.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonElement.Parse(buffer.WrittenSpan).Clone();
    }

    /// <summary>Determines whether a schema declares a parameter NIM refuses.</summary>
    /// <param name="schema">The declared schema.</param>
    /// <returns><see langword="true"/> when a reserved name is present.</returns>
    private static bool NeedsAliasing(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var member in properties.EnumerateObject())
        {
            if (IsReserved(member.Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether an argument object carries an aliased name.</summary>
    /// <param name="arguments">The arguments the model produced.</param>
    /// <returns><see langword="true"/> when a name needs restoring.</returns>
    private static bool ContainsAlias(JsonElement arguments)
    {
        foreach (var member in arguments.EnumerateObject())
        {
            if (member.Name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes a schema with its reserved parameter names renamed.</summary>
    /// <param name="writer">The writer the schema is composed into.</param>
    /// <param name="schema">The declared schema.</param>
    private static void WriteAliased(Utf8JsonWriter writer, JsonElement schema)
    {
        writer.WriteStartObject();

        foreach (var member in schema.EnumerateObject())
        {
            if (string.Equals(member.Name, "properties", StringComparison.Ordinal))
            {
                writer.WritePropertyName(member.Name);
                WriteRenamedMembers(writer, member.Value);
                continue;
            }

            if (string.Equals(member.Name, "required", StringComparison.Ordinal))
            {
                writer.WritePropertyName(member.Name);
                WriteRenamedRequired(writer, member.Value);
                continue;
            }

            writer.WritePropertyName(member.Name);
            member.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes a properties object with its reserved names renamed.</summary>
    /// <param name="writer">The writer the object is composed into.</param>
    /// <param name="properties">The declared properties.</param>
    private static void WriteRenamedMembers(Utf8JsonWriter writer, JsonElement properties)
    {
        writer.WriteStartObject();

        foreach (var member in properties.EnumerateObject())
        {
            writer.WritePropertyName(IsReserved(member.Name) ? Prefix + member.Name : member.Name);
            member.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes a required list with its reserved names renamed.</summary>
    /// <param name="writer">The writer the list is composed into.</param>
    /// <param name="required">The declared required names.</param>
    private static void WriteRenamedRequired(Utf8JsonWriter writer, JsonElement required)
    {
        if (required.ValueKind != JsonValueKind.Array)
        {
            required.WriteTo(writer);
            return;
        }

        writer.WriteStartArray();

        foreach (var name in required.EnumerateArray())
        {
            if (name.ValueKind == JsonValueKind.String && name.GetString() is { } text && IsReserved(text))
            {
                writer.WriteStringValue(Prefix + text);
                continue;
            }

            name.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    /// <summary>Determines whether a parameter name is one NIM refuses.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns><see langword="true"/> when the name is reserved.</returns>
    private static bool IsReserved(string name)
    {
        foreach (var reserved in Reserved)
        {
            if (string.Equals(name, reserved, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
