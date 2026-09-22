// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Serialization;

/// <summary>Writes a NIM message body as either a bare string or a list of parts.</summary>
/// <remarks>
/// The OpenAI shape allows <c>content</c> to be either, and the choice is not cosmetic: a model
/// without vision rejects the list form. The union cannot be expressed without a converter, which
/// costs this one type its serialization fast path but not the messages containing it.
/// </remarks>
public sealed class NimContentConverter : JsonConverter<NimContent>
{
    /// <inheritdoc/>
    public override NimContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? NimContent.FromText(reader.GetString() ?? string.Empty)
            : NimContent.FromText(string.Empty);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, NimContent value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value.Parts is not { } parts)
        {
            writer.WriteStringValue(value.Text ?? string.Empty);
            return;
        }

        writer.WriteStartArray();
        for (var i = 0; i < parts.Count; i++)
        {
            JsonSerializer.Serialize(writer, parts[i], ProxyJsonContext.Default.NimContentPart);
        }

        writer.WriteEndArray();
    }
}
