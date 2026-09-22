// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Recovers tool calls that a model wrote into its answer text instead of returning.</summary>
/// <remarks>
/// <para>
/// Not every model on the NVIDIA catalogue fills in the <c>tool_calls</c> field. Several of the
/// Kimi and DeepSeek families emit the call as special tokens inside ordinary <c>content</c>,
/// because that is how their chat template renders one. A proxy that reads only the structured
/// field shows the client a wall of marker tokens and never executes a tool — the tool support it
/// advertised simply does not work on those models.
/// </para>
/// <para>
/// A marker can straddle a chunk boundary, so any trailing run that could still grow into one is
/// held back until the next chunk resolves it — the same hold-back <see cref="ThinkTagParser"/>
/// performs, for the same reason. The tokens themselves are described by
/// <see cref="EmbeddedToolCallSyntax"/>.
/// </para>
/// </remarks>
public sealed class EmbeddedToolCallParser
{
    /// <summary>The buffer holding partially parsed content between chunk boundaries.</summary>
    private readonly StringBuilder _buffer = new();

    /// <summary>Consumes a chunk of model output and appends any completed runs.</summary>
    /// <param name="chunk">The incoming content fragment.</param>
    /// <param name="runs">The list completed runs are appended to.</param>
    public void Feed(string chunk, List<EmbeddedToolCall> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (!string.IsNullOrEmpty(chunk))
        {
            _ = _buffer.Append(chunk);
        }

        while (_buffer.Length > 0 && Step(runs))
        {
            // Step consumes what it can; the loop ends when only a partial marker is left.
        }
    }

    /// <summary>Releases whatever text is still held once the stream has ended.</summary>
    /// <param name="runs">The list the trailing run is appended to.</param>
    /// <remarks>
    /// An unterminated call is released as text. It is what the model actually produced, and
    /// inventing a call from half a marker would be worse than showing the caller the truth.
    /// </remarks>
    public void Flush(List<EmbeddedToolCall> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (_buffer.Length == 0)
        {
            return;
        }

        EmitText(runs, _buffer.ToString());
        _ = _buffer.Clear();
    }

    /// <summary>Appends a run of answer text, dropping the markers that carry nothing.</summary>
    /// <param name="runs">The list completed runs are appended to.</param>
    /// <param name="text">The text to append.</param>
    private static void EmitText(List<EmbeddedToolCall> runs, string text)
    {
        var cleaned = ControlMarkers.Strip(text);

        if (cleaned.Length > 0)
        {
            runs.Add(EmbeddedToolCall.ForText(cleaned));
        }
    }

    /// <summary>Consumes as much of the buffer as can be resolved.</summary>
    /// <param name="runs">The list completed runs are appended to.</param>
    /// <returns><see langword="true"/> when more of the buffer may still be consumable.</returns>
    private bool Step(List<EmbeddedToolCall> runs)
    {
        var text = _buffer.ToString();
        var opening = EmbeddedToolCallSyntax.FindOpening(text);
        var named = NamedToolCallSyntax.FindOpening(text);

        if (named.Found && (!opening.Found || named.Index < opening.Index))
        {
            return StepNamed(runs, text, named.Index, named.Form);
        }

        if (!opening.Found)
        {
            return ReleaseSafePrefix(runs, text);
        }

        EmitText(runs, text[..opening.Index]);

        var body = text[(opening.Index + opening.Form.Open.Length)..];
        var close = body.IndexOf(opening.Form.Close, StringComparison.Ordinal);

        if (close < 0)
        {
            // The call has started but not finished; hold it until the rest arrives.
            _ = _buffer.Remove(0, opening.Index);
            return false;
        }

        if (EmbeddedToolCallSyntax.ReadCall(body[..close], opening.Form.Separator) is { } call)
        {
            runs.Add(call);
        }

        _ = _buffer.Remove(0, opening.Index + opening.Form.Open.Length + close + opening.Form.Close.Length);
        return true;
    }

    /// <summary>Consumes one call written in the shape that names each argument with its own tag.</summary>
    /// <param name="runs">The list completed runs are appended to.</param>
    /// <param name="text">The buffered text.</param>
    /// <param name="index">Where the call opens.</param>
    /// <param name="form">The spelling the call was written in.</param>
    /// <returns><see langword="true"/> when more of the buffer may still be consumable.</returns>
    private bool StepNamed(List<EmbeddedToolCall> runs, string text, int index, in NamedCallForm form)
    {
        EmitText(runs, text[..index]);

        var body = text[(index + form.CallOpen.Length)..];
        var close = body.IndexOf(form.CallClose, StringComparison.Ordinal);

        if (close < 0)
        {
            // The call has started but not finished; hold it until the rest arrives.
            _ = _buffer.Remove(0, index);
            return false;
        }

        if (NamedToolCallSyntax.ReadCall(body[..close], form) is { } call)
        {
            runs.Add(call);
        }

        _ = _buffer.Remove(0, index + form.CallOpen.Length + close + form.CallClose.Length);
        return true;
    }

    /// <summary>Releases the text that cannot be the start of a marker.</summary>
    /// <param name="runs">The list completed runs are appended to.</param>
    /// <param name="text">The buffered text.</param>
    /// <returns><see langword="false"/>, because nothing further can be resolved yet.</returns>
    private bool ReleaseSafePrefix(List<EmbeddedToolCall> runs, string text)
    {
        var safe = Math.Min(EmbeddedToolCallSyntax.SafeLength(text), NamedToolCallSyntax.SafeLength(text));

        if (safe > 0)
        {
            EmitText(runs, text[..safe]);
            _ = _buffer.Remove(0, safe);
        }

        return false;
    }
}
