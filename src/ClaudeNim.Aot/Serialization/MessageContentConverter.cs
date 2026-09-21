// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Anthropic;

namespace ClaudeNim.Aot.Serialization;

/// <summary>Reads and writes <see cref="MessageContent"/>, which Anthropic encodes either as a bare string or as an array of content blocks.</summary>
public sealed class MessageContentConverter : JsonConverter<MessageContent>
{
    /// <inheritdoc/>
    public override MessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (reader.TokenType == JsonTokenType.String)
        {
            return MessageContent.FromText(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Message content must be a string or an array of content blocks.");
        }

        var blocks = new List<ContentBlock>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var block = JsonSerializer.Deserialize(ref reader, ProxyJsonContext.Default.ContentBlock);
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return MessageContent.FromBlocks(blocks);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MessageContent value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        if (value.Blocks is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        for (var i = 0; i < value.Blocks.Count; i++)
        {
            JsonSerializer.Serialize(writer, value.Blocks[i], ProxyJsonContext.Default.ContentBlock);
        }

        writer.WriteEndArray();
    }
}
