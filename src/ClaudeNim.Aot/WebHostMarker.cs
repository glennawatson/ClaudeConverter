// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot;

/// <summary>Locates this assembly for <c>WebApplicationFactory&lt;TEntryPoint&gt;</c>.</summary>
/// <remarks>
/// <c>WebApplicationFactory</c> only uses its type argument to find the entry assembly; it never
/// constructs or calls anything on the type itself. A dedicated marker means <see cref="Program"/>
/// can stay <see langword="static"/>, which is what it actually is, without the integration tests
/// needing an instantiable stand-in for it.
/// </remarks>
public sealed class WebHostMarker
{
    /// <summary>Gets the proxy's assembly, for anything that needs to reflect over it without hosting the app.</summary>
    public System.Reflection.Assembly Assembly => GetType().Assembly;
}
