// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the assembly-locating marker used by <c>WebApplicationFactory&lt;TEntryPoint&gt;</c>.</summary>
public sealed class WebHostMarkerTests
{
    /// <summary>The exposed assembly is the one the marker itself is declared in.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AssemblyIsTheProxysOwnAssembly()
    {
        var marker = new WebHostMarker();

        await Assert.That(marker.Assembly).IsEqualTo(typeof(WebHostMarker).Assembly);
    }
}
