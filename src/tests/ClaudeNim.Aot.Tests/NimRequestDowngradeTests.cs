// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers the rungs a rejected request is lightened through.</summary>
public sealed class NimRequestDowngradeTests
{
    /// <summary>A model identifier used across the fixtures.</summary>
    private const string Model = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The output ceiling used across the fixtures.</summary>
    private const int MaxTokens = 100;

    /// <summary>The reasoning budget used by the fixture that carries one.</summary>
    private const int ReasoningBudget = 32;

    /// <summary>Reasoning controls are removed when present.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RemovesReasoningControlsWhenPresent()
    {
        var request = Request() with { ReasoningEffort = "low", Extensions = new(ReasoningBudget) };

        var lighter = NimRequestDowngrade.WithoutReasoningControls(request);

        await Assert.That(lighter).IsNotNull();
        await Assert.That(lighter!.ReasoningEffort).IsNull();
        await Assert.That(lighter.Extensions).IsNull();
    }

    /// <summary>A request with no reasoning controls has nothing to remove.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReturnsNullWhenNoReasoningControlsArePresent() =>
        await Assert.That(NimRequestDowngrade.WithoutReasoningControls(Request())).IsNull();

    /// <summary>Replayed reasoning is stripped from every message that carries it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task RemovesReplayedReasoningFromEveryMessage()
    {
        var request = Request() with
        {
            Messages =
            [
                new(NimChatMessage.UserRole, NimContent.FromText("hi")),
                new(NimChatMessage.AssistantRole, NimContent.FromText("ok"), ReasoningContent: "because"),
            ],
        };

        var lighter = NimRequestDowngrade.WithoutReplayedReasoning(request);

        await Assert.That(lighter).IsNotNull();
        await Assert.That(NoMessageCarriesReasoning(lighter!)).IsTrue();
    }

    /// <summary>A request with no replayed reasoning has nothing to strip.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReturnsNullWhenNoMessageCarriesReasoning()
    {
        var request = Request() with { Messages = [new(NimChatMessage.UserRole, NimContent.FromText("hi"))] };

        await Assert.That(NimRequestDowngrade.WithoutReplayedReasoning(request)).IsNull();
    }

    /// <summary>Builds a minimal request.</summary>
    /// <returns>The request.</returns>
    private static NimChatRequest Request() => new(Model, [], MaxTokens, Stream: false);

    /// <summary>Determines whether every message of a request carries no replayed reasoning.</summary>
    /// <param name="request">The request to check.</param>
    /// <returns><see langword="true"/> when no message carries reasoning.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NoMessageCarriesReasoning(NimChatRequest request) =>
        request.Messages.TrueForAll(static m => m.ReasoningContent is null);
}
