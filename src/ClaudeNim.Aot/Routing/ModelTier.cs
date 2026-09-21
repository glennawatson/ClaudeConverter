// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Routing;

/// <summary>The Claude capability tier a client's model name falls into.</summary>
public enum ModelTier
{
    /// <summary>No recognised tier; the fallback model serves the request.</summary>
    Default = 0,

    /// <summary>The Opus tier.</summary>
    Opus = 1,

    /// <summary>The Sonnet tier.</summary>
    Sonnet = 2,

    /// <summary>The Haiku tier.</summary>
    Haiku = 3,
}
