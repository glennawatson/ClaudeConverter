// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Serialization;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers writing a NIM message body as either a bare string or a list of multimodal parts.</summary>
public sealed class NimContentConverterTests
{
    /// <summary>Text-form content writes as a bare JSON string.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task TextFormWritesAsABareString()
    {
        var written = JsonSerializer.Serialize(NimContent.FromText("hi"), ProxyJsonContext.Default.NimContent);

        await Assert.That(written).IsEqualTo("\"hi\"");
    }

    /// <summary>Parts-form content writes as a JSON array of parts.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task PartsFormWritesAsAnArray()
    {
        var content = NimContent.FromParts([NimContentPart.ForText("hi")]);

        var written = JsonSerializer.Serialize(content, ProxyJsonContext.Default.NimContent);

        await Assert.That(written.StartsWith('[')).IsTrue();
        await Assert.That(written).Contains("\"hi\"");
    }

    /// <summary>A bare JSON string reads back as text-form content.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task BareStringReadsAsTextForm()
    {
        var content = JsonSerializer.Deserialize("\"hi\"", ProxyJsonContext.Default.NimContent);

        await Assert.That(content.IsText).IsTrue();
        await Assert.That(content.Text).IsEqualTo("hi");
    }

    /// <summary>A JSON array reads back as empty text, since NIM never needs to read its own parts form back.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ArrayReadsAsEmptyText()
    {
        var content = JsonSerializer.Deserialize("""[{"type":"text","text":"hi"}]""", ProxyJsonContext.Default.NimContent);

        await Assert.That(content.Text).IsEqualTo(string.Empty);
    }
}
