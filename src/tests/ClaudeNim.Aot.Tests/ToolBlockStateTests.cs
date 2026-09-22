// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Anthropic.Streaming;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers merging streamed name fragments while a tool call is being assembled.</summary>
public sealed class ToolBlockStateTests
{
    /// <summary>The full tool name the merge fixtures converge on.</summary>
    private const string FullName = "search";

    /// <summary>The leading fragment of <see cref="FullName"/>.</summary>
    private const string LeadingFragment = "sea";

    /// <summary>The trailing fragment of <see cref="FullName"/>.</summary>
    private const string TrailingFragment = "rch";

    /// <summary>An empty name takes the first fragment outright.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyNameTakesTheFirstFragment()
    {
        var state = new ToolBlockState();

        state.MergeName(LeadingFragment);

        await Assert.That(state.Name).IsEqualTo(LeadingFragment);
    }

    /// <summary>A fragment continuing the held name extends it.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task ContinuingFragmentExtendsTheName()
    {
        var state = new ToolBlockState();
        state.MergeName(LeadingFragment);

        state.MergeName(FullName);

        await Assert.That(state.Name).IsEqualTo(FullName);
    }

    /// <summary>A fragment repeating the held name in full is not doubled.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task RepeatedFragmentIsNotDoubled()
    {
        var state = new ToolBlockState();
        state.MergeName(FullName);

        state.MergeName(FullName);

        await Assert.That(state.Name).IsEqualTo(FullName);
    }

    /// <summary>An unrelated fragment is appended to what is held.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task UnrelatedFragmentIsAppended()
    {
        var state = new ToolBlockState();
        state.MergeName(LeadingFragment);

        state.MergeName(TrailingFragment);

        await Assert.That(state.Name).IsEqualTo(FullName);
    }

    /// <summary>An empty fragment leaves the held name unchanged.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptyFragmentLeavesNameUnchanged()
    {
        var state = new ToolBlockState();
        state.MergeName(FullName);

        state.MergeName(string.Empty);

        await Assert.That(state.Name).IsEqualTo(FullName);
    }

    /// <summary>Every field starts at its documented default.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Test]
    public async Task FieldsStartAtTheirDocumentedDefaults()
    {
        var state = new ToolBlockState();

        await Assert.That(state.BlockIndex).IsEqualTo(-1);
        await Assert.That(state.Id).IsNull();
        await Assert.That(state.Name).IsEqualTo(string.Empty);
        await Assert.That(state.Started).IsFalse();
        await Assert.That(state.PendingArguments).IsEqualTo(string.Empty);
        await Assert.That(state.EmittedArgumentLength).IsEqualTo(0);
    }
}
