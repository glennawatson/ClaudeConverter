// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>The identifiers one attempt to read NVIDIA's catalogue produced.</summary>
/// <param name="Entries">The chat-capable identifiers and their publication dates.</param>
/// <param name="FromUpstream">Whether NVIDIA supplied them, rather than the built-in profiles.</param>
[System.Diagnostics.DebuggerDisplay("CatalogEntries: {Entries.Count} entries, upstream {FromUpstream}")]
internal readonly record struct CatalogEntries(List<NimCatalogEntry> Entries, bool FromUpstream);
