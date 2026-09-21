// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Controls how the upstream model catalogue is cached and advertised.</summary>
/// <param name="CacheMinutes">How long a fetched NVIDIA NIM model list is reused before refetching.</param>
/// <param name="DefaultContextWindow">The context window reported for a model NIM does not size.</param>
/// <param name="DefaultMaxOutputTokens">The output ceiling reported for a model NIM does not size.</param>
/// <param name="AdvertiseClaudeAliases">
/// Whether Claude tier aliases are listed alongside NIM models. The aliases are the identifiers
/// Claude Code sends by default, so listing them keeps the native model picker consistent with
/// what the proxy will actually route.
/// </param>
[System.Diagnostics.DebuggerDisplay("ModelCatalogOptions: {ToString(),nq}")]
public sealed record ModelCatalogOptions(
    int CacheMinutes = 30,
    int DefaultContextWindow = 131_072,
    int DefaultMaxOutputTokens = 65_536,
    bool AdvertiseClaudeAliases = true)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "ModelCatalog";
}
