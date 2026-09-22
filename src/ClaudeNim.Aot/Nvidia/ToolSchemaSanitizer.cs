// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Removes the JSON Schema constructs NVIDIA NIM's tool validator rejects.</summary>
/// <remarks>
/// <para>
/// Draft 2020-12 allows a boolean in any position a schema may appear, so
/// <c>"additionalProperties": false</c> is legal and Claude Code emits it. NIM's validator
/// expects an object there and fails the whole request, taking every other tool with it.
/// </para>
/// <para>
/// Dropping the boolean loosens the contract rather than breaking it: the model may now pass an
/// argument the client did not declare, which the client's own validation rejects, where leaving
/// it in means no tool call happens at all.
/// </para>
/// </remarks>
public static class ToolSchemaSanitizer
{
    /// <summary>The initial buffer size for sanitized schema serialization.</summary>
    private const int InitialBufferBytes = 512;

    // Keys whose value is itself a schema, so a boolean there is a subschema.
    /// <summary>The JSON Schema keywords whose value is itself a schema.</summary>
    private static readonly string[] SchemaValueKeys =
    [
        "additionalProperties", "additionalItems", "unevaluatedProperties", "unevaluatedItems",
        "items", "contains", "propertyNames", "if", "then", "else", "not",
    ];

    // Keys whose value is an array of schemas.
    /// <summary>The JSON Schema keywords whose value is an array of subschemas.</summary>
    private static readonly string[] SchemaListKeys = ["allOf", "anyOf", "oneOf", "prefixItems"];

    // Keys whose value is an object mapping names to schemas.
    /// <summary>The JSON Schema keywords whose value is an object mapping names to subschemas.</summary>
    private static readonly string[] SchemaMapKeys =
    [
        "properties", "patternProperties", "$defs", "definitions", "dependentSchemas",
    ];

    /// <summary>Returns a schema NIM will accept.</summary>
    /// <param name="schema">The client's tool schema.</param>
    /// <returns>The original schema when it is already acceptable, otherwise a cleaned copy.</returns>
    public static JsonElement Sanitize(JsonElement schema)
    {
        if (!ContainsBooleanSubschema(schema))
        {
            return schema;
        }

        var buffer = new ArrayBufferWriter<byte>(InitialBufferBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSchema(writer, schema);
        }

        return JsonDocument.Parse(buffer.WrittenMemory).RootElement.Clone();
    }

    /// <summary>Checks if a JSON element contains a boolean subschema.</summary>
    /// <param name="element">The element to scan.</param>
    /// <returns>True if a boolean subschema is found.</returns>
    private static bool ContainsBooleanSubschema(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ObjectContainsBooleanSubschema(element),
        JsonValueKind.Array => ArrayContainsBooleanSubschema(element),
        _ => false,
    };

    /// <summary>Checks if a JSON object contains a boolean subschema.</summary>
    /// <param name="element">The object element to scan.</param>
    /// <returns>True if a boolean subschema is found.</returns>
    private static bool ObjectContainsBooleanSubschema(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            // A schema-bearing key's value counts whether the boolean is the value itself (a
            // subschema such as "items") or one entry of the list or map of subschemas it
            // introduces (such as "allOf" or "properties") — the writer strips both shapes.
            if (IsSchemaBearingKey(property.Name) && ValueOrEntryIsBoolean(property.Value))
            {
                return true;
            }

            if (ContainsBooleanSubschema(property.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks whether a schema-bearing value is itself boolean, or holds a boolean entry.</summary>
    /// <param name="value">The value of a schema-bearing key.</param>
    /// <returns>True if the value or one of its immediate entries is a boolean subschema.</returns>
    private static bool ValueOrEntryIsBoolean(JsonElement value)
    {
        if (IsBoolean(value))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (IsBoolean(item))
                {
                    return true;
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in value.EnumerateObject())
            {
                if (IsBoolean(entry.Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Checks if a JSON array contains a boolean subschema.</summary>
    /// <param name="element">The array element to scan.</param>
    /// <returns>True if a boolean subschema is found.</returns>
    private static bool ArrayContainsBooleanSubschema(JsonElement element)
    {
        foreach (var item in element.EnumerateArray())
        {
            if (ContainsBooleanSubschema(item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks if a JSON element is a boolean value.</summary>
    /// <param name="element">The element to test.</param>
    /// <returns>True if the element is true or false.</returns>
    private static bool IsBoolean(JsonElement element) =>
        element.ValueKind is JsonValueKind.True or JsonValueKind.False;

    /// <summary>Checks if a key name can have a schema as its value.</summary>
    /// <param name="name">The key name to test.</param>
    /// <returns>True if the key can contain a schema.</returns>
    private static bool IsSchemaBearingKey(string name) =>
        Contains(SchemaValueKeys, name) || Contains(SchemaListKeys, name) || Contains(SchemaMapKeys, name);

    /// <summary>Searches for a key name in an array using ordinal comparison.</summary>
    /// <param name="keys">The array of keys to search.</param>
    /// <param name="name">The name to search for.</param>
    /// <returns>True if the name is found in the array.</returns>
    private static bool Contains(string[] keys, string name)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            if (string.Equals(keys[i], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes a JSON schema to the writer, sanitizing boolean subschemas.</summary>
    /// <param name="writer">The writer to compose the schema into.</param>
    /// <param name="element">The schema element to write.</param>
    private static void WriteSchema(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            WriteValue(writer, element);
            return;
        }

        writer.WriteStartObject();
        foreach (var property in element.EnumerateObject())
        {
            WriteMember(writer, property.Name, property.Value);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes a single schema member, sanitizing boolean subschemas.</summary>
    /// <param name="writer">The writer to compose the member into.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member value.</param>
    private static void WriteMember(Utf8JsonWriter writer, string name, JsonElement value)
    {
        if (Contains(SchemaValueKeys, name))
        {
            if (!IsBoolean(value))
            {
                writer.WritePropertyName(name);
                WriteSchema(writer, value);
            }

            return;
        }

        if (Contains(SchemaListKeys, name) && value.ValueKind == JsonValueKind.Array)
        {
            writer.WritePropertyName(name);
            WriteSchemaList(writer, value);
            return;
        }

        if (Contains(SchemaMapKeys, name) && value.ValueKind == JsonValueKind.Object)
        {
            writer.WritePropertyName(name);
            WriteSchemaMap(writer, value);
            return;
        }

        writer.WritePropertyName(name);
        WriteValue(writer, value);
    }

    /// <summary>Writes an array of schemas, filtering out boolean subschemas.</summary>
    /// <param name="writer">The writer to compose the array into.</param>
    /// <param name="value">The array element to write.</param>
    private static void WriteSchemaList(Utf8JsonWriter writer, JsonElement value)
    {
        writer.WriteStartArray();
        foreach (var item in value.EnumerateArray())
        {
            if (!IsBoolean(item))
            {
                WriteSchema(writer, item);
            }
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes an object mapping names to schemas, filtering out boolean entries.</summary>
    /// <param name="writer">The writer to compose the object into.</param>
    /// <param name="value">The object element to write.</param>
    private static void WriteSchemaMap(Utf8JsonWriter writer, JsonElement value)
    {
        writer.WriteStartObject();
        foreach (var entry in value.EnumerateObject())
        {
            if (IsBoolean(entry.Value))
            {
                continue;
            }

            writer.WritePropertyName(entry.Name);
            WriteSchema(writer, entry.Value);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes a JSON value, recursing for objects and arrays.</summary>
    /// <param name="writer">The writer to compose the value into.</param>
    /// <param name="element">The value element to write.</param>
    private static void WriteValue(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            WriteSchema(writer, element);
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            WriteValueArray(writer, element);
            return;
        }

        element.WriteTo(writer);
    }

    /// <summary>Writes a JSON array, recursing for each element.</summary>
    /// <param name="writer">The writer to compose the array into.</param>
    /// <param name="element">The array element to write.</param>
    private static void WriteValueArray(Utf8JsonWriter writer, JsonElement element)
    {
        writer.WriteStartArray();
        foreach (var item in element.EnumerateArray())
        {
            WriteValue(writer, item);
        }

        writer.WriteEndArray();
    }
}
