// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the rename applied to tool parameters some NIM models reject.</summary>
/// <remarks>
/// A schema parameter named <c>type</c> makes some models answer with an internal server error.
/// The rename has to be invisible from both ends: the schema sent upstream and the arguments
/// handed back to the client.
/// </remarks>
public sealed class ToolParameterAliasesTests
{
    /// <summary>The alias a reserved <c>type</c> parameter is renamed to.</summary>
    private const string AliasedTypeName = "nim_alias_type";

    /// <summary>A schema with no reserved names is left untouched.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SchemaWithoutReservedNamesIsUnchanged()
    {
        var schema = JsonElement.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""");

        var aliased = ToolParameterAliases.Apply(schema);

        await Assert.That(aliased.GetRawText()).IsEqualTo(schema.GetRawText());
    }

    /// <summary>A reserved parameter name is renamed in both properties and required.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task ReservedParameterIsRenamed()
    {
        var schema = JsonElement.Parse(
            """{"type":"object","properties":{"type":{"type":"string"}},"required":["type"]}""");

        var aliased = ToolParameterAliases.Apply(schema);
        var properties = aliased.GetProperty("properties");

        await Assert.That(properties.TryGetProperty("type", out _)).IsFalse();
        await Assert.That(properties.TryGetProperty(AliasedTypeName, out _)).IsTrue();

        var required = ReadRequired(aliased);
        await Assert.That(required.Contains(AliasedTypeName, StringComparer.Ordinal)).IsTrue();
    }

    /// <summary>A renamed argument is restored to its original name.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RenamedArgumentIsRestored()
    {
        var arguments = JsonElement.Parse($$"""{"{{AliasedTypeName}}":"a","name":"b"}""");

        var restored = ToolParameterAliases.Restore(arguments);

        await Assert.That(restored.GetProperty("type").GetString()).IsEqualTo("a");
        await Assert.That(restored.GetProperty("name").GetString()).IsEqualTo("b");
        await Assert.That(restored.TryGetProperty(AliasedTypeName, out _)).IsFalse();
    }

    /// <summary>Arguments carrying no aliased name are returned unchanged.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ArgumentsWithoutAliasAreUnchanged()
    {
        var arguments = JsonElement.Parse("""{"city":"Paris"}""");

        var restored = ToolParameterAliases.Restore(arguments);

        await Assert.That(restored.GetRawText()).IsEqualTo(arguments.GetRawText());
    }

    /// <summary>A round trip through apply and restore recovers the original argument name.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RoundTripsThroughApplyAndRestore()
    {
        var schema = JsonElement.Parse(
            """{"type":"object","properties":{"type":{"type":"string"}},"required":["type"]}""");

        var aliasedSchema = ToolParameterAliases.Apply(schema);
        var aliasedName = SoleProperty(aliasedSchema);

        var modelArguments = JsonElement.Parse($$"""{"{{aliasedName}}":"a"}""");
        var restored = ToolParameterAliases.Restore(modelArguments);

        await Assert.That(restored.GetProperty("type").GetString()).IsEqualTo("a");
    }

    /// <summary>Reads the required-property names of a schema.</summary>
    /// <param name="schema">The schema to read.</param>
    /// <returns>The required names.</returns>
    private static List<string?> ReadRequired(JsonElement schema)
    {
        var names = new List<string?>();
        foreach (var name in schema.GetProperty("required").EnumerateArray())
        {
            names.Add(name.GetString());
        }

        return names;
    }

    /// <summary>Reads the single property name of a schema's <c>properties</c> object.</summary>
    /// <param name="schema">The schema to read.</param>
    /// <returns>The property name.</returns>
    /// <exception cref="InvalidOperationException">The schema declared no properties.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string SoleProperty(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Single().Name;
}
