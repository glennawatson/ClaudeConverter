// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>The tool-call markers that name each argument separately instead of carrying JSON.</summary>
/// <remarks>
/// <para>
/// <see cref="EmbeddedToolCallSyntax"/> covers the shapes that write the arguments as one JSON
/// blob. The Nemotron and DeepSeek V4.1 templates do not: they render a call as nested tags with
/// one tag per argument, so the JSON an Anthropic <c>tool_use</c> block needs has to be assembled
/// rather than lifted out.
/// </para>
/// <para>
/// Both shapes are the same tree with different spellings, so one reader serves both:
/// </para>
/// <code>
/// &lt;tool_call&gt;&lt;function=search&gt;&lt;parameter=query&gt;
/// rain
/// &lt;/parameter&gt;&lt;/function&gt;&lt;/tool_call&gt;
/// </code>
/// <code>
/// &lt;｜DSML｜ calls&gt;&lt;｜DSML｜ invoke name="search"&gt;
/// &lt;｜DSML｜ parameter name="query" string="true"&gt;rain&lt;/｜DSML｜ parameter&gt;
/// &lt;/｜DSML｜ invoke&gt;&lt;/｜DSML｜ calls&gt;
/// </code>
/// </remarks>
public static class NamedToolCallSyntax
{
    /// <summary>The initial buffer size for assembling one call's arguments.</summary>
    private const int ArgumentsBufferBytes = 256;

    /// <summary>The attribute a DeepSeek parameter carries when its value is already JSON.</summary>
    private const string RawValueAttribute = "string=\"false\"";

    /// <summary>The attribute a DeepSeek parameter carries when its value is text.</summary>
    private const string TextValueAttribute = "string=\"true\"";

    /// <summary>The attribute prefix both spellings use when the name is given as an attribute.</summary>
    private const string NameAttribute = "name=\"";

    /// <summary>The length of a carriage-return line break.</summary>
    private const int WindowsBreakLength = 2;

    /// <summary>The spellings of the named-argument call shape.</summary>
    private static readonly NamedCallForm[] Forms =
    [
        new("<tool_call>", "</tool_call>", "<function=", "</function>", "<parameter=", "</parameter>"),
        new(
            "<｜DSML｜ calls>",
            "</｜DSML｜ calls>",
            "<｜DSML｜ invoke",
            "</｜DSML｜ invoke>",
            "<｜DSML｜ parameter",
            "</｜DSML｜ parameter>"),
    ];

    /// <summary>Finds the earliest call opening in a run of text.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The opening, which reports itself as not found when the text holds none.</returns>
    public static NamedCallOpening FindOpening(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var best = NamedCallOpening.NotFound;

        for (var i = 0; i < Forms.Length; i++)
        {
            var index = text.IndexOf(Forms[i].CallOpen, StringComparison.Ordinal);
            if (index >= 0 && (!best.Found || index < best.Index))
            {
                best = new(index, Forms[i]);
            }
        }

        return best;
    }

    /// <summary>Computes the longest prefix of a run that cannot be the start of any call opening.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The length of the safe prefix.</returns>
    public static int SafeLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var safe = text.Length;
        for (var i = 0; i < Forms.Length; i++)
        {
            safe = Math.Min(safe, SafeLength(text, Forms[i].CallOpen));
        }

        return safe;
    }

    /// <summary>Reads one call from the text between its opening and closing tags.</summary>
    /// <param name="body">The text between the opening and closing tags.</param>
    /// <param name="form">The spelling the call was written in.</param>
    /// <returns>The call, or <see langword="null"/> when no tool name could be read.</returns>
    public static EmbeddedToolCall? ReadCall(string body, in NamedCallForm form)
    {
        ArgumentNullException.ThrowIfNull(body);

        var functionIndex = body.IndexOf(form.FunctionOpen, StringComparison.Ordinal);
        if (functionIndex < 0)
        {
            return null;
        }

        return ReadTag(body, functionIndex + form.FunctionOpen.Length) is { Name.Length: > 0 } function
            ? EmbeddedToolCall.ForCall(function.Name, ReadArguments(body, function.End, form))
            : null;
    }

    /// <summary>Assembles the JSON arguments from every parameter tag in a call body.</summary>
    /// <param name="body">The call body.</param>
    /// <param name="start">The offset just past the function tag.</param>
    /// <param name="form">The spelling the call was written in.</param>
    /// <returns>The arguments as JSON.</returns>
    private static string ReadArguments(string body, int start, in NamedCallForm form)
    {
        var buffer = new ArrayBufferWriter<byte>(ArgumentsBufferBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            var cursor = start;
            while (cursor < body.Length)
            {
                var open = body.IndexOf(form.ParameterOpen, cursor, StringComparison.Ordinal);
                if (open < 0 || ReadTag(body, open + form.ParameterOpen.Length) is not { } parameter)
                {
                    break;
                }

                var close = body.IndexOf(form.ParameterClose, parameter.End, StringComparison.Ordinal);
                var end = close < 0 ? body.Length : close;

                if (parameter.Name.Length > 0)
                {
                    writer.WritePropertyName(parameter.Name);
                    WriteValue(writer, Unpad(body[parameter.End..end]), parameter.Attributes);
                }

                cursor = close < 0 ? body.Length : close + form.ParameterClose.Length;
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Writes one argument, as JSON where the value is JSON and as a string otherwise.</summary>
    /// <param name="writer">The writer to compose the value into.</param>
    /// <param name="value">The value as the model wrote it.</param>
    /// <param name="attributes">The attributes of the parameter tag.</param>
    /// <remarks>
    /// DeepSeek states the answer on the tag, so it is read rather than guessed at. Nemotron does
    /// not, and a tool schema that wants a number will reject the string <c>"5"</c>, so a value
    /// that is already valid JSON of some other kind is passed through as that kind. A value that
    /// parses as a JSON string is still written as a string, which is what it is.
    /// </remarks>
    private static void WriteValue(Utf8JsonWriter writer, string value, string attributes)
    {
        if (attributes.Contains(TextValueAttribute, StringComparison.Ordinal))
        {
            writer.WriteStringValue(value);
            return;
        }

        var raw = attributes.Contains(RawValueAttribute, StringComparison.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(value);
            if (raw || document.RootElement.ValueKind is not JsonValueKind.String)
            {
                document.RootElement.WriteTo(writer);
                return;
            }
        }
        catch (JsonException)
        {
            // Not JSON, so it is the text it looks like.
        }

        writer.WriteStringValue(value);
    }

    /// <summary>Removes the line breaks a template puts around a value on its own line.</summary>
    /// <param name="value">The value as the model wrote it.</param>
    /// <returns>The value without its surrounding line break.</returns>
    /// <remarks>
    /// Only one break is removed at each end. Trimming outright would eat the indentation of a
    /// multi-line value, which for a coding client is usually the code it was asked to write.
    /// </remarks>
    private static string Unpad(string value)
    {
        var text = value;

        if (text.StartsWith('\n'))
        {
            text = text[1..];
        }
        else if (text.StartsWith("\r\n", StringComparison.Ordinal))
        {
            text = text[WindowsBreakLength..];
        }

        if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        return text.EndsWith('\r') ? text[..^1] : text;
    }

    /// <summary>Reads the name and attributes of one opening tag.</summary>
    /// <param name="text">The text holding the tag.</param>
    /// <param name="start">The offset just past the tag's opening token.</param>
    /// <returns>The tag, or <see langword="null"/> when it is not terminated.</returns>
    /// <remarks>
    /// The two spellings give the name differently — Nemotron as the rest of the opening token,
    /// DeepSeek as a <c>name</c> attribute — so both are read from the same span.
    /// </remarks>
    private static NamedTag? ReadTag(string text, int start)
    {
        var end = text.IndexOf('>', start);
        if (end < 0)
        {
            return null;
        }

        var attributes = text[start..end].Trim();
        var name = attributes;

        var named = attributes.IndexOf(NameAttribute, StringComparison.Ordinal);
        if (named >= 0)
        {
            var valueStart = named + NameAttribute.Length;
            var quote = attributes.IndexOf('"', valueStart);
            name = quote < 0 ? string.Empty : attributes[valueStart..quote];
        }

        return new(name, attributes, end + 1);
    }

    /// <summary>Computes the longest prefix that cannot be the opening of one marker.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="marker">The marker being guarded against.</param>
    /// <returns>The length of the safe prefix.</returns>
    private static int SafeLength(string text, string marker)
    {
        var earliest = text.Length;
        var limit = marker.Length - 1;

        for (var length = 1; length <= limit && length <= text.Length; length++)
        {
            var start = text.Length - length;
            if (string.CompareOrdinal(text, start, marker, 0, length) == 0)
            {
                earliest = start;
            }
        }

        return earliest;
    }

    /// <summary>One opening tag's name, attributes and end offset.</summary>
    /// <param name="Name">The name the tag carries.</param>
    /// <param name="Attributes">The whole attribute span, used to read the value's declared kind.</param>
    /// <param name="End">The offset just past the tag's closing angle bracket.</param>
    private readonly record struct NamedTag(string Name, string Attributes, int End);
}
