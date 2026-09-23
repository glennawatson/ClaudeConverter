// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>Tracks which NIM models have recently failed, so a fallback chain can skip a dead one.</summary>
/// <remarks>
/// Shared across every protocol this proxy serves rather than owned by one route: a model's health
/// is a property of the model, not of which client asked for it, and a fallback chain discovering a
/// model is down on an Anthropic turn should protect a Codex turn that reaches the same model a
/// moment later.
/// </remarks>
public interface IModelHealthTracker
{
    /// <summary>Determines whether a model is currently in cooldown.</summary>
    /// <param name="nimModel">The NIM model to check.</param>
    /// <returns><see langword="true"/> when the model should be skipped rather than attempted.</returns>
    bool IsInCooldown(string nimModel);

    /// <summary>Records that a model failed, putting it in cooldown once it has failed enough in a row.</summary>
    /// <param name="nimModel">The NIM model that failed.</param>
    void MarkUnavailable(string nimModel);

    /// <summary>Records that a model answered successfully, clearing any cooldown immediately.</summary>
    /// <param name="nimModel">The NIM model that answered.</param>
    void MarkAvailable(string nimModel);
}
