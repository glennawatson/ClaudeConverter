// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Anthropic;

/// <summary>Which <c>output_config.effort</c> levels a listed model accepts.</summary>
/// <param name="Supported">Whether the model accepts an effort level at all.</param>
/// <param name="Low">Whether <c>low</c> is accepted.</param>
/// <param name="Medium">Whether <c>medium</c> is accepted.</param>
/// <param name="High">Whether <c>high</c> is accepted.</param>
/// <param name="Max">Whether <c>max</c> is accepted.</param>
/// <param name="ExtraHigh">Whether <c>xhigh</c> is accepted, or <see langword="null"/> when the model predates it.</param>
/// <remarks>
/// This is how a client discovers that it may ask for less work rather than more. A session that
/// can see <c>low</c> is available will spend a cheap level on the small errands it would
/// otherwise send at the model's default, so reporting the levels accurately is worth more than
/// it looks.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("EffortCapability: {Supported}")]
public readonly record struct EffortCapability(
    [property: JsonPropertyName("supported")] bool Supported,
    [property: JsonPropertyName("low")] CapabilitySupport Low,
    [property: JsonPropertyName("medium")] CapabilitySupport Medium,
    [property: JsonPropertyName("high")] CapabilitySupport High,
    [property: JsonPropertyName("max")] CapabilitySupport Max,
    [property: JsonPropertyName("xhigh")] CapabilitySupport? ExtraHigh)
{
    /// <summary>Creates the capability for a model, reporting every level it can be asked for.</summary>
    /// <param name="reasons">Whether the model produces a reasoning trace.</param>
    /// <returns>The capability.</returns>
    /// <remarks>
    /// All five levels are advertised together. NVIDIA NIM's own control spans only
    /// <c>low</c>, <c>medium</c> and <c>high</c>, and <see cref="EffortLevels.ToReasoningEffort"/>
    /// folds the two wider Claude levels onto <c>high</c> — so a request for any of them is
    /// honoured rather than rejected, which is what the capability is claiming.
    /// </remarks>
    public static EffortCapability For(bool reasons)
    {
        var level = CapabilitySupport.For(reasons);
        return new(reasons, level, level, level, level, level);
    }
}
