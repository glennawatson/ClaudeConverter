// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Nvidia;
using ClaudeNim.Aot.Tests.Fakes;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers which models a fallback chain should skip, and for how long.</summary>
public sealed class ModelHealthTrackerTests
{
    /// <summary>The model name shared by the fixtures.</summary>
    private const string Model = "nvidia/nemotron-3-ultra-550b-a55b";

    /// <summary>A second model name, used to prove tracking is per model.</summary>
    private const string OtherModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>How far past the cooldown window to advance the clock, to confirm the cooldown has lifted.</summary>
    private const int SecondsPastTheWindow = 2;

    /// <summary>The failure threshold and cooldown length shared by the fixtures.</summary>
    private static readonly ModelHealthOptions Options = new(FailureThreshold: 2, CooldownSeconds: 150);

    /// <summary>A model that has never failed is not in cooldown.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NeverFailedModelIsNotInCooldown() =>
        await Assert.That(new ModelHealthTracker(Options, new FakeTimeProvider()).IsInCooldown(Model)).IsFalse();

    /// <summary>A single failure, below the threshold, does not start a cooldown.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task OneFailureBelowThresholdDoesNotCooldown()
    {
        var tracker = new ModelHealthTracker(Options, new FakeTimeProvider());

        tracker.MarkUnavailable(Model);

        await Assert.That(tracker.IsInCooldown(Model)).IsFalse();
    }

    /// <summary>Reaching the failure threshold starts a cooldown.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ReachingTheThresholdStartsACooldown()
    {
        var tracker = new ModelHealthTracker(Options, new FakeTimeProvider());

        tracker.MarkUnavailable(Model);
        tracker.MarkUnavailable(Model);

        await Assert.That(tracker.IsInCooldown(Model)).IsTrue();
    }

    /// <summary>The cooldown ends once the configured window has passed.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task CooldownEndsAfterItsWindow()
    {
        var time = new FakeTimeProvider();
        var tracker = new ModelHealthTracker(Options, time);

        tracker.MarkUnavailable(Model);
        tracker.MarkUnavailable(Model);
        await Assert.That(tracker.IsInCooldown(Model)).IsTrue();

        time.Advance(TimeSpan.FromSeconds(Options.CooldownSeconds - 1));
        await Assert.That(tracker.IsInCooldown(Model)).IsTrue();

        time.Advance(TimeSpan.FromSeconds(SecondsPastTheWindow));
        await Assert.That(tracker.IsInCooldown(Model)).IsFalse();
    }

    /// <summary>A success clears the cooldown immediately, without waiting out the window.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SuccessClearsTheCooldownImmediately()
    {
        var tracker = new ModelHealthTracker(Options, new FakeTimeProvider());

        tracker.MarkUnavailable(Model);
        tracker.MarkUnavailable(Model);
        tracker.MarkAvailable(Model);

        await Assert.That(tracker.IsInCooldown(Model)).IsFalse();
    }

    /// <summary>A success resets the failure count, so a later single failure does not resume a cooldown.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task SuccessResetsTheFailureCount()
    {
        var tracker = new ModelHealthTracker(Options, new FakeTimeProvider());

        tracker.MarkUnavailable(Model);
        tracker.MarkAvailable(Model);
        tracker.MarkUnavailable(Model);

        await Assert.That(tracker.IsInCooldown(Model)).IsFalse();
    }

    /// <summary>Cooldown is tracked independently for each model.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task CooldownIsPerModel()
    {
        var tracker = new ModelHealthTracker(Options, new FakeTimeProvider());

        tracker.MarkUnavailable(Model);
        tracker.MarkUnavailable(Model);

        await Assert.That(tracker.IsInCooldown(Model)).IsTrue();
        await Assert.That(tracker.IsInCooldown(OtherModel)).IsFalse();
    }
}
