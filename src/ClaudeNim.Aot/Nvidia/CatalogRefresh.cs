// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The outcome of one attempt to refresh the advertised listing.</summary>
/// <param name="Models">The listing that was built.</param>
/// <param name="FromUpstream">Whether NVIDIA supplied it, rather than the built-in profiles.</param>
/// <remarks>
/// The flag is what lets a failed refresh be told from a successful one that happened to return
/// the same models, which is the difference between keeping a good listing and replacing it with
/// a smaller one.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("CatalogRefresh: {Models.Count} models, upstream {FromUpstream}")]
internal readonly record struct CatalogRefresh(List<ModelDescriptor> Models, bool FromUpstream);
