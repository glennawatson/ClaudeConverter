// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;

namespace ClaudeNim.Aot.Serialization;

/// <summary>Small <see cref="JsonElement"/> values the translators need to synthesise.</summary>
public static class JsonElements
{
    /// <summary>The cached JSON document for an empty object schema.</summary>
    private static readonly JsonDocument EmptyObjectSchemaDocument =
        JsonDocument.Parse("""{"type":"object","properties":{}}""");

    /// <summary>The cached JSON document for an empty object.</summary>
    private static readonly JsonDocument EmptyObjectDocument = JsonDocument.Parse("{}");

    /// <summary>Gets the schema used for a tool that declares no arguments.</summary>
    public static JsonElement EmptyObjectSchema => EmptyObjectSchemaDocument.RootElement;

    /// <summary>Gets an empty JSON object.</summary>
    public static JsonElement EmptyObject => EmptyObjectDocument.RootElement;

    /// <summary>Parses a JSON fragment, returning <see cref="EmptyObject"/> when it is not valid JSON.</summary>
    /// <param name="json">The fragment to parse.</param>
    /// <returns>The parsed element, or an empty object.</returns>
    /// <remarks>
    /// A model can emit malformed tool arguments. Substituting an empty object keeps the turn
    /// alive and lets the client's own schema validation report the problem, which is a better
    /// outcome than failing the whole response.
    /// </remarks>
    public static JsonElement ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EmptyObject;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyObject;
        }
    }
}
