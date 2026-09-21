// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>One upstream model identifier and its publication time.</summary>
/// <param name="Id">The NIM model identifier.</param>
/// <param name="Published">When the model was published.</param>
internal readonly record struct NimCatalogEntry(string Id, DateTimeOffset Published);
