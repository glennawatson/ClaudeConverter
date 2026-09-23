// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Controls when a model is taken out of rotation for repeated failures.</summary>
/// <param name="FailureThreshold">How many consecutive failures a model needs before it is put into cooldown.</param>
/// <param name="CooldownSeconds">How long a model stays out of rotation once it trips the threshold.</param>
/// <remarks>
/// <para>
/// A fallback chain that starts every turn at the model it routed to pays the full cost of
/// discovering that model is still down — a real attempt, a real failure — on every single turn
/// until it recovers, even though the previous turn just learned the same thing five seconds ago.
/// A model in cooldown is skipped without an attempt instead, and the chain moves straight to the
/// next one.
/// </para>
/// <para>
/// The threshold is more than one on purpose. NVIDIA's shared endpoints have the occasional single
/// bad request that a lone retry clears; putting a model in cooldown on the first failure would
/// pull it out of rotation for something that was never a pattern. Failing repeatedly in a row is
/// the signal a genuine outage looks like.
/// </para>
/// <para>
/// A model recovers the moment it answers successfully, not only once the cooldown expires — a
/// turn that reaches a cooling-down model because every other candidate was exhausted still tries
/// it for real, and success there clears the cooldown immediately rather than waiting out the rest
/// of the window. That is also what stops a whole chain going quiet forever: once every model in it
/// is cooling down, the last resort this proxy already falls back to — waiting on the originally
/// routed model with its full retry budget — asks it directly, bypassing cooldown entirely, so a
/// chain that looks entirely dead is still probed rather than given up on outright.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ModelHealthOptions: {ToString(),nq}")]
public sealed record ModelHealthOptions(
    int FailureThreshold = 2,
    int CooldownSeconds = 150)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "ModelHealth";
}
