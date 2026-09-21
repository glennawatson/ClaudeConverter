// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>One run of model output classified as either reasoning or answer text.</summary>
/// <param name="IsThinking">Whether the run came from inside a reasoning tag.</param>
/// <param name="Text">The run's text.</param>
[System.Diagnostics.DebuggerDisplay("ThinkTagSegment: {ToString(),nq}")]
public readonly record struct ThinkTagSegment(bool IsThinking, string Text);
