// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Controls how a transient upstream failure is retried.</summary>
/// <param name="MaxAttempts">How many times a request may be sent in total, including the first.</param>
/// <param name="BaseDelayMilliseconds">The delay before the first retry, doubled on each attempt.</param>
/// <param name="MaxDelayMilliseconds">The ceiling the doubling is clamped to.</param>
/// <param name="UseJitter">Whether each delay is spread randomly across its window.</param>
/// <param name="ContentAwareDowngrade">
/// Whether a rejected request is downgraded by reading what the upstream's own error names, rather
/// than always trying the same rung first.
/// </param>
/// <remarks>
/// <para>
/// Only failures that stand a chance of succeeding on a second attempt are retried; a rejected
/// request is not retried on the same body, because it would be rejected again.
/// </para>
/// <para>
/// Jitter matters more than it looks. Several sessions hitting a saturated endpoint will back off
/// on the same schedule and return together, reproducing the burst that caused the rejection.
/// Spreading each delay randomly across its window breaks that up, which is why it defaults on.
/// </para>
/// <para>
/// The budget is sized for what it is actually spent on. What NVIDIA's shared endpoints return
/// under load is a plain <c>503 Service temporarily overloaded</c> or a <c>429</c>, and neither
/// clears inside a second — so three attempts spread over about two seconds was not patience, it
/// was three ways of asking during the same bad moment and then failing the turn. Five attempts on
/// a doubling delay spend around fifteen seconds before giving up, which is shorter than the turn
/// the client is waiting on and long enough for a saturation spike to pass.
/// </para>
/// <para>
/// A rejection's own text usually names the field it did not like — <c>reasoning_content</c> on a
/// replayed assistant turn, or <c>chat_template_kwargs</c>/<c>reasoning_effort</c>/<c>nvext</c> on
/// the controls asking the model to reason a particular way. Reading it first means the one rung
/// that actually applies is tried, rather than always trying the same rung and only reaching the
/// right one on a second round trip. A rejection whose text names nothing recognised still falls
/// back to trying reasoning controls first, then replayed reasoning, exactly as before.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("RetryOptions: {ToString(),nq}")]
public sealed record RetryOptions(
    int MaxAttempts = 5,
    int BaseDelayMilliseconds = 1_000,
    int MaxDelayMilliseconds = 30_000,
    bool UseJitter = true,
    bool ContentAwareDowngrade = true)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "Retries";
}
