// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Codex;

namespace ClaudeNim.Aot.Serialization;

/// <summary>Reads and writes <see cref="FunctionCallOutput"/>, which Codex encodes either as a bare string or as a bare array of content parts.</summary>
public sealed class FunctionCallOutputConverter : JsonConverter<FunctionCallOutput>
{
    /// <inheritdoc/>
    public override FunctionCallOutput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (reader.TokenType == JsonTokenType.String)
        {
            return FunctionCallOutput.FromText(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A function call's output must be a string or an array of content parts.");
        }

        var items = new List<ResponseContentItem>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var item = JsonSerializer.Deserialize(ref reader, ProxyJsonContext.Default.ResponseContentItem);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return FunctionCallOutput.FromItems(items);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, FunctionCallOutput value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        if (value.Items is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        for (var i = 0; i < value.Items.Count; i++)
        {
            JsonSerializer.Serialize(writer, value.Items[i], ProxyJsonContext.Default.ResponseContentItem);
        }

        writer.WriteEndArray();
    }
}
