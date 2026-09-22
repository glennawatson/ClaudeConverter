// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers stripping the boolean JSON Schema subschemas NVIDIA NIM's validator rejects.</summary>
public sealed class ToolSchemaSanitizerTests
{
    /// <summary>The reserved keyword the boolean-subschema fixtures target.</summary>
    private const string AdditionalPropertiesKeyword = "additionalProperties";

    /// <summary>The JSON Schema string type name asserted against across the fixtures.</summary>
    private const string StringSchemaType = "string";

    /// <summary>The array length expected once a boolean entry has been dropped from a two-item array.</summary>
    private const int LengthAfterDroppingOneEntry = 1;

    /// <summary>The scalar <c>minLength</c> value the survives-unchanged fixture carries.</summary>
    private const int SampleMinLength = 3;

    /// <summary>A schema with no boolean subschema is returned unchanged, by reference.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CleanSchemaIsReturnedByReference()
    {
        var schema = Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);

        await Assert.That(sanitized.Equals(schema)).IsTrue();
    }

    /// <summary>A top-level boolean <c>additionalProperties</c> is dropped.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BooleanAdditionalPropertiesIsDropped()
    {
        var schema = Parse("""{"type":"object","additionalProperties":false}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);

        await Assert.That(sanitized.TryGetProperty(AdditionalPropertiesKeyword, out _)).IsFalse();
        await Assert.That(sanitized.GetProperty("type").GetString()).IsEqualTo("object");
    }

    /// <summary>A boolean subschema nested inside <c>properties</c> is dropped.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BooleanSubschemaInPropertiesIsDropped()
    {
        var schema = Parse("""{"type":"object","properties":{"a":true,"b":{"type":"string"}}}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);
        var properties = sanitized.GetProperty("properties");

        await Assert.That(properties.TryGetProperty("a", out _)).IsFalse();
        await Assert.That(properties.GetProperty("b").GetProperty("type").GetString()).IsEqualTo(StringSchemaType);
    }

    /// <summary>A boolean entry inside an <c>allOf</c> array is dropped, keeping the rest.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BooleanEntryInSchemaListIsDropped()
    {
        var schema = Parse("""{"allOf":[true,{"type":"string"}]}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);
        var all = sanitized.GetProperty("allOf");

        await Assert.That(all.GetArrayLength()).IsEqualTo(LengthAfterDroppingOneEntry);
        await Assert.That(all[0].GetProperty("type").GetString()).IsEqualTo(StringSchemaType);
    }

    /// <summary>A boolean entry inside a schema map, such as <c>$defs</c>, is dropped.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BooleanEntryInSchemaMapIsDropped()
    {
        var schema = Parse("""{"$defs":{"a":true,"b":{"type":"string"}}}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);
        var defs = sanitized.GetProperty("$defs");

        await Assert.That(defs.TryGetProperty("a", out _)).IsFalse();
        await Assert.That(defs.GetProperty("b").GetProperty("type").GetString()).IsEqualTo(StringSchemaType);
    }

    /// <summary>A boolean subschema nested inside an array value is found and removed.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task BooleanSubschemaInsideAnArrayValueIsRemoved()
    {
        var schema = Parse("""{"examples":[{"additionalProperties":false}]}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);

        await Assert.That(sanitized.GetProperty("examples")[0].TryGetProperty(AdditionalPropertiesKeyword, out _)).IsFalse();
    }

    /// <summary>Ordinary scalar members survive sanitisation unchanged.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ScalarMembersSurviveUnchanged()
    {
        var schema = Parse("""{"type":"object","additionalProperties":false,"minLength":3,"nullable":null}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);

        await Assert.That(sanitized.GetProperty("minLength").GetInt32()).IsEqualTo(SampleMinLength);
        await Assert.That(sanitized.GetProperty("nullable").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    /// <summary>A schema-value key whose value is not boolean recurses instead of being dropped.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NonBooleanSchemaValueRecurses()
    {
        var schema = Parse("""{"items":{"additionalProperties":false}}""");

        var sanitized = ToolSchemaSanitizer.Sanitize(schema);

        await Assert.That(sanitized.GetProperty("items").TryGetProperty(AdditionalPropertiesKeyword, out _)).IsFalse();
    }

    /// <summary>Parses a JSON fragment into a standalone, disposal-safe element.</summary>
    /// <param name="json">The fragment to parse.</param>
    /// <returns>The cloned root element.</returns>
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
