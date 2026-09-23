// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Codex;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Translates a Responses API request into the NVIDIA NIM chat completions request that serves it.</summary>
/// <remarks>
/// The Responses API represents one assistant turn as several sibling <see cref="ResponseInputItem"/>
/// entries — a <c>reasoning</c> item, a <c>message</c> item, then one <c>function_call</c> item per
/// tool call — where NIM's chat shape wants all of that folded into a single assistant message. This
/// builder accumulates the sibling items and flushes them together, mirroring what
/// <see cref="NimRequestBuilder"/> does for Anthropic's own nested content blocks.
/// </remarks>
public static class CodexRequestBuilder
{
    /// <summary>The sentence that carries a failed tool call into text the upstream shape can hold.</summary>
    private const string FailedToolCallText =
        "This tool call FAILED and produced no result. Do not repeat it unchanged.";

    /// <summary>Builds the upstream request.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="model">The NIM model the router selected.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="options">The configured NIM defaults.</param>
    /// <param name="defaultMaxOutputTokens">The output ceiling to assume for a model NVIDIA does not size.</param>
    /// <returns>The upstream request body.</returns>
    public static NimChatRequest Build(
        ResponsesRequest request,
        string model,
        bool thinkingEnabled,
        NvidiaNimOptions options,
        int defaultMaxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var messages = BuildMessages(request, thinkingEnabled, NimModelCatalogDefaults.SupportsVision(model));
        var tools = BuildTools(request.Tools);
        var maxTokens = ResolveMaxTokens(
            request.MaxOutputTokens ?? 0,
            NimModelCatalogDefaults.MaxOutputTokens(model, defaultMaxOutputTokens),
            options.MaxTokens);

        return new(
            model,
            messages,
            maxTokens,
            request.IsStreaming,
            StreamOptions: StreamOptionsOrNull(request),
            Temperature: ResolveTemperature(request, options),
            TopP: ResolveTopP(request, options),
            TopK: TopKOrNull(options),
            MinP: NullIf(options.MinP, 0.0),
            RepetitionPenalty: NullIf(options.RepetitionPenalty, 1.0),
            PresencePenalty: NullIf(options.PresencePenalty, 0.0),
            FrequencyPenalty: NullIf(options.FrequencyPenalty, 0.0),
            MinTokens: MinTokensOrNull(options),
            Seed: options.Seed,
            Stop: ResolveStop(options),
            IgnoreEos: IgnoreEosOrNull(options),
            Tools: tools,
            ToolChoice: ToolChoiceOrNull(request, tools),
            ParallelToolCalls: ResolveParallelToolCalls(request, options, tools is not null),
            ChatTemplateKwargs: BuildTemplateArguments(request, thinkingEnabled, tools is not null),
            ReasoningEffort: ReasoningEffortOrNull(request, thinkingEnabled),
            ResponseFormat: BuildResponseFormat(request.Text?.Format));
    }

    /// <summary>Builds the streamed-usage options, when the turn is streamed.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <returns>The options, or <see langword="null"/> for a non-streamed turn.</returns>
    private static NimStreamOptions? StreamOptionsOrNull(ResponsesRequest request) =>
        request.IsStreaming ? new NimStreamOptions(true) : null;

    /// <summary>Resolves the reasoning effort to send, when reasoning is enabled.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <returns>The effort, or <see langword="null"/> when reasoning is off.</returns>
    private static string? ReasoningEffortOrNull(ResponsesRequest request, bool thinkingEnabled) =>
        thinkingEnabled ? ToReasoningEffort(request.Reasoning?.Effort) : null;

    /// <summary>Resolves the tool choice to send, when tools are offered.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="tools">The tools built for the upstream request.</param>
    /// <returns>The tool choice, or <see langword="null"/> when no tools are offered.</returns>
    private static JsonElement? ToolChoiceOrNull(ResponsesRequest request, List<NimTool>? tools) =>
        tools is null ? null : TranslateToolChoice(request.ToolChoice) ?? ToolChoiceTranslator.Auto;

    /// <summary>Resolves the sampling temperature, preferring the caller's over the configured default.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns>The temperature, or <see langword="null"/> when neither is set.</returns>
    private static double? ResolveTemperature(ResponsesRequest request, NvidiaNimOptions options) =>
        request.Temperature ?? NullIf(options.Temperature, 1.0);

    /// <summary>Resolves the nucleus-sampling cutoff, preferring the caller's over the configured default.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns>The cutoff, or <see langword="null"/> when neither is set.</returns>
    private static double? ResolveTopP(ResponsesRequest request, NvidiaNimOptions options) =>
        request.TopP ?? NullIf(options.TopP, 1.0);

    /// <summary>Resolves the configured stop sequence.</summary>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns>The stop sequence, or <see langword="null"/> when none is configured.</returns>
    private static List<string>? ResolveStop(NvidiaNimOptions options) =>
        string.IsNullOrEmpty(options.Stop) ? null : [options.Stop];

    /// <summary>Resolves the completion length, bounded by what the model can actually produce.</summary>
    /// <param name="requested">The caller's requested max output tokens.</param>
    /// <param name="modelCeiling">The largest completion the model is documented to allow.</param>
    /// <param name="whenUnspecified">The length to use when the caller asks for none.</param>
    /// <returns>The effective maximum tokens.</returns>
    private static int ResolveMaxTokens(int requested, int modelCeiling, int whenUnspecified)
    {
        var wanted = requested > 0 ? requested : whenUnspecified;

        if (wanted <= 0)
        {
            return modelCeiling;
        }

        return modelCeiling > 0 && wanted > modelCeiling ? modelCeiling : wanted;
    }

    /// <summary>Maps a Responses API reasoning effort onto the OpenAI-style value NIM accepts.</summary>
    /// <param name="effort">The requested effort, which may be <see langword="null"/>.</param>
    /// <returns>The upstream effort value, or <see langword="null"/> when none applies.</returns>
    private static string? ToReasoningEffort(string? effort) => effort switch
    {
        "minimal" => "low",
        "low" or "medium" or "high" => effort,
        _ => null,
    };

    /// <summary>Translates a Responses API <c>tool_choice</c> into the value NIM accepts.</summary>
    /// <param name="toolChoice">The caller's <c>tool_choice</c>, which may be absent.</param>
    /// <returns>The upstream value, or <see langword="null"/> when the caller expressed no preference.</returns>
    /// <remarks>
    /// A bare string (<c>auto</c>, <c>none</c>, <c>required</c>) is already the value NIM's
    /// OpenAI-compatible surface accepts. A named-function object nests the name one level
    /// shallower than the chat-completions shape NIM expects, so that form alone is rebuilt.
    /// </remarks>
    private static JsonElement? TranslateToolChoice(JsonElement? toolChoice)
    {
        if (toolChoice is not { } choice)
        {
            return null;
        }

        if (choice.ValueKind == JsonValueKind.String)
        {
            return choice;
        }

        return choice.ValueKind == JsonValueKind.Object
            && choice.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), CodexTool.FunctionType, StringComparison.Ordinal)
            && choice.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
            ? ToolChoiceTranslator.NamedFunction(name.GetString() ?? string.Empty)
            : null;
    }

    /// <summary>Translates a Responses API structured-output request into NIM's <c>response_format</c>.</summary>
    /// <param name="format">The requested output format, which may be <see langword="null"/>.</param>
    /// <returns>The upstream <c>response_format</c>, or <see langword="null"/> when none was asked for.</returns>
    private static JsonElement? BuildResponseFormat(CodexTextFormat? format)
    {
        if (format?.Schema is not { } schema || !string.Equals(format.Type, CodexTextFormat.JsonSchema, StringComparison.Ordinal))
        {
            return null;
        }

        ResponseFormatEnvelope envelope = new(format.Type, new(schema));
        return JsonSerializer.SerializeToElement(envelope, Serialization.ProxyJsonContext.Default.ResponseFormatEnvelope);
    }

    /// <summary>Reads the configured top-k cutoff, treating a negative value as unset.</summary>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns>The cutoff, or <see langword="null"/> when it is unset.</returns>
    private static int? TopKOrNull(NvidiaNimOptions options) => options.TopK >= 0 ? options.TopK : null;

    /// <summary>Reads the configured minimum length, treating zero as unset.</summary>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns>The minimum, or <see langword="null"/> when it is unset.</returns>
    private static int? MinTokensOrNull(NvidiaNimOptions options) =>
        options.MinTokens > 0 ? options.MinTokens : null;

    /// <summary>Reads the configured end-of-sequence override, sending nothing when it is off.</summary>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns><see langword="true"/> when set, otherwise <see langword="null"/>.</returns>
    private static bool? IgnoreEosOrNull(NvidiaNimOptions options) => options.IgnoreEos ? true : null;

    /// <summary>Returns the value unless it equals the neutral value within floating-point epsilon.</summary>
    /// <param name="value">The value to test.</param>
    /// <param name="neutral">The neutral value to compare against.</param>
    /// <returns>The value, or null if it equals the neutral value.</returns>
    private static double? NullIf(double value, double neutral) =>
        Math.Abs(value - neutral) < double.Epsilon ? null : value;

    /// <summary>Resolves whether parallel tool calls should be enabled.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="options">The configured NIM defaults.</param>
    /// <param name="hasTools">Whether the request includes tool definitions.</param>
    /// <returns>True to disable parallel calls, false to enable, or null to use the upstream default.</returns>
    private static bool? ResolveParallelToolCalls(ResponsesRequest request, NvidiaNimOptions options, bool hasTools)
    {
        if (!hasTools)
        {
            return null;
        }

        if (request.ParallelToolCalls == false)
        {
            return false;
        }

        return options.ParallelToolCalls ? null : false;
    }

    /// <summary>Builds template arguments for reasoning and tool support.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="hasTools">Whether the request includes tool definitions.</param>
    /// <returns>The template arguments, or null if neither reasoning nor tools are needed.</returns>
    private static NimChatTemplateKwargs? BuildTemplateArguments(ResponsesRequest request, bool thinkingEnabled, bool hasTools)
    {
        if (!thinkingEnabled && !hasTools)
        {
            return null;
        }

        if (!thinkingEnabled)
        {
            return new(EnableThinking: false, Thinking: false);
        }

        var effort = request.Reasoning?.Effort;
        var reduced = string.Equals(effort, "low", StringComparison.Ordinal) || string.Equals(effort, "minimal", StringComparison.Ordinal);
        var moderate = string.Equals(effort, "medium", StringComparison.Ordinal);

        return new(
            EnableThinking: true,
            Thinking: true,
            LowEffort: reduced ? true : null,
            MediumEffort: moderate ? true : null,
            ForceNonemptyContent: hasTools ? true : null);
    }

    /// <summary>Converts Responses API tool definitions into NIM tools.</summary>
    /// <param name="tools">The caller's tool definitions.</param>
    /// <returns>The NIM tools, or null if no tools are defined.</returns>
    private static List<NimTool>? BuildTools(List<CodexTool>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return null;
        }

        var result = new List<NimTool>(tools.Count);
        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            if (!string.Equals(tool.Type, CodexTool.FunctionType, StringComparison.Ordinal) || tool.Name is not { Length: > 0 } name)
            {
                continue;
            }

            var declared = ToolChoiceTranslator.SchemaOrEmpty(tool.Parameters);
            var schema = ToolParameterAliases.Apply(ToolSchemaSanitizer.Sanitize(declared));
            result.Add(new(new NimFunctionDefinition(name, tool.Description ?? string.Empty, schema)));
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>Builds the messages list for the upstream request.</summary>
    /// <param name="request">The caller's Responses API request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    /// <returns>The upstream messages.</returns>
    private static List<NimChatMessage> BuildMessages(ResponsesRequest request, bool thinkingEnabled, bool vision)
    {
        var messages = new List<NimChatMessage>(request.Input.Count + 1);

        if (request.Instructions is { Length: > 0 } instructions)
        {
            messages.Add(new(NimChatMessage.SystemRole, NimContent.FromText(instructions)));
        }

        AssistantTurnAccumulator accumulator = new();

        for (var i = 0; i < request.Input.Count; i++)
        {
            ProcessItem(request.Input[i], accumulator, messages, thinkingEnabled, vision);
        }

        accumulator.Flush(messages);

        return messages;
    }

    /// <summary>Folds one item into the assistant turn being accumulated, or appends it directly.</summary>
    /// <param name="item">The item to process.</param>
    /// <param name="accumulator">The assistant turn being accumulated.</param>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    private static void ProcessItem(
        ResponseInputItem item,
        AssistantTurnAccumulator accumulator,
        List<NimChatMessage> messages,
        bool thinkingEnabled,
        bool vision)
    {
        if (TryAccumulate(item, accumulator, thinkingEnabled))
        {
            return;
        }

        accumulator.Flush(messages);
        AppendOtherItem(messages, item, vision);
    }

    /// <summary>Tries to fold an item that belongs to the assistant turn currently being accumulated.</summary>
    /// <param name="item">The item to try.</param>
    /// <param name="accumulator">The assistant turn being accumulated.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <returns><see langword="true"/> when the item was folded in.</returns>
    private static bool TryAccumulate(ResponseInputItem item, AssistantTurnAccumulator accumulator, bool thinkingEnabled)
    {
        if (string.Equals(item.Type, ResponseItemTypes.Reasoning, StringComparison.Ordinal))
        {
            accumulator.AddReasoning(item, thinkingEnabled);
            return true;
        }

        if (string.Equals(item.Type, ResponseItemTypes.FunctionCall, StringComparison.Ordinal))
        {
            accumulator.AddFunctionCall(item);
            return true;
        }

        if (string.Equals(item.Type, ResponseItemTypes.Message, StringComparison.Ordinal)
            && string.Equals(item.Role, ResponseItemTypes.AssistantRole, StringComparison.Ordinal))
        {
            accumulator.AddAssistantText(item);
            return true;
        }

        return false;
    }

    /// <summary>Appends an item that ends the assistant turn being accumulated, once it has been flushed.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="item">The item to append.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    private static void AppendOtherItem(List<NimChatMessage> messages, ResponseInputItem item, bool vision)
    {
        if (string.Equals(item.Type, ResponseItemTypes.FunctionCallOutput, StringComparison.Ordinal))
        {
            AppendFunctionCallOutput(messages, item);
            return;
        }

        if (string.Equals(item.Type, ResponseItemTypes.Message, StringComparison.Ordinal))
        {
            AppendUserOrSystemMessage(messages, item, vision);
        }
    }

    /// <summary>Appends a <c>user</c>, <c>system</c>, or <c>developer</c> message item.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="item">The item to append.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    private static void AppendUserOrSystemMessage(List<NimChatMessage> messages, ResponseInputItem item, bool vision)
    {
        if (item.Content is not { Count: > 0 } parts)
        {
            return;
        }

        var role = MessageRole(item.Role);
        var text = new StringBuilder();
        var images = vision ? new List<string>() : null;

        for (var i = 0; i < parts.Count; i++)
        {
            CollectPart(parts[i], text, images);
        }

        AppendCollectedMessage(messages, role, text, images);
    }

    /// <summary>Resolves the upstream role a <c>message</c> item's own role maps onto.</summary>
    /// <param name="role">The item's own role.</param>
    /// <returns>The upstream role.</returns>
    private static string MessageRole(string? role) =>
        string.Equals(role, ResponseItemTypes.SystemRole, StringComparison.Ordinal)
        || string.Equals(role, ResponseItemTypes.DeveloperRole, StringComparison.Ordinal)
            ? NimChatMessage.SystemRole
            : NimChatMessage.UserRole;

    /// <summary>Folds one content part into the text or image list being collected for a message.</summary>
    /// <param name="part">The part to collect.</param>
    /// <param name="text">The text accumulated so far.</param>
    /// <param name="images">The images collected so far, or <see langword="null"/> when the model has no vision.</param>
    private static void CollectPart(ResponseContentItem part, StringBuilder text, List<string>? images)
    {
        if (images is not null
            && string.Equals(part.Type, ResponseContentTypes.InputImage, StringComparison.Ordinal)
            && part.ImageUrl is { Length: > 0 } url)
        {
            images.Add(url);
            return;
        }

        if (part.Text is not { Length: > 0 } value)
        {
            return;
        }

        if (text.Length > 0)
        {
            _ = text.Append('\n');
        }

        _ = text.Append(value);
    }

    /// <summary>Appends the text and images collected for one message.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="role">The upstream role to send the message as.</param>
    /// <param name="text">The text collected for the message.</param>
    /// <param name="images">The images collected for the message, or <see langword="null"/> when the model has no vision.</param>
    private static void AppendCollectedMessage(List<NimChatMessage> messages, string role, StringBuilder text, List<string>? images)
    {
        if (images is not { Count: > 0 })
        {
            if (text.Length > 0)
            {
                messages.Add(new(role, NimContent.FromText(text.ToString())));
            }

            return;
        }

        var contentParts = new List<NimContentPart>(images.Count + 1);
        if (text.Length > 0)
        {
            contentParts.Add(NimContentPart.ForText(text.ToString()));
        }

        for (var i = 0; i < images.Count; i++)
        {
            contentParts.Add(NimContentPart.ForImage(images[i]));
        }

        messages.Add(new(role, NimContent.FromParts(contentParts)));
    }

    /// <summary>Appends a <c>function_call_output</c> item as a <c>tool</c> message.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="item">The item to append.</param>
    private static void AppendFunctionCallOutput(List<NimChatMessage> messages, ResponseInputItem item)
    {
        if (item.CallId is not { Length: > 0 } callId)
        {
            return;
        }

        messages.Add(new(
            NimChatMessage.ToolRole,
            NimContent.FromText(ToolResultText(item.Output)),
            ToolCallId: callId));
    }

    /// <summary>Renders a tool result, saying so in the text when the call failed.</summary>
    /// <param name="output">The <c>function_call_output</c> payload.</param>
    /// <returns>The text the upstream tool message carries.</returns>
    /// <remarks>
    /// The Responses API carries no explicit failure flag on a tool result the way Anthropic's
    /// <c>is_error</c> does; a caller that wants a failure understood as one has to say so in the
    /// text, which is passed through unchanged either way.
    /// </remarks>
    private static string ToolResultText(FunctionCallOutput? output)
    {
        if (output is not { } value)
        {
            return FailedToolCallText;
        }

        if (value.Text is { Length: > 0 } text)
        {
            return text;
        }

        if (value.Items is not { Count: > 0 } items)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Text is not { Length: > 0 } part)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                _ = builder.Append('\n');
            }

            _ = builder.Append(part);
        }

        return builder.ToString();
    }

    /// <summary>Accumulates the sibling items that make up one assistant turn until it is flushed.</summary>
    private sealed class AssistantTurnAccumulator
    {
        /// <summary>The answer text accumulated so far.</summary>
        private readonly StringBuilder _text = new();

        /// <summary>The reasoning text accumulated so far.</summary>
        private readonly StringBuilder _reasoning = new();

        /// <summary>The tool calls accumulated so far.</summary>
        private List<NimToolCall>? _toolCalls;

        /// <summary>Whether anything has been accumulated since the last flush.</summary>
        private bool _hasContent;

        /// <summary>Folds a <c>reasoning</c> item's disclosed text into the turn.</summary>
        /// <param name="item">The item to fold in.</param>
        /// <param name="thinkingEnabled">Whether reasoning should be forwarded upstream at all.</param>
        public void AddReasoning(ResponseInputItem item, bool thinkingEnabled)
        {
            _hasContent = true;

            if (!thinkingEnabled)
            {
                return;
            }

            AppendParts(_reasoning, item.Summary);
            AppendParts(_reasoning, item.Content);
        }

        /// <summary>Folds an assistant <c>message</c> item's text into the turn.</summary>
        /// <param name="item">The item to fold in.</param>
        public void AddAssistantText(ResponseInputItem item)
        {
            _hasContent = true;
            AppendParts(_text, item.Content);
        }

        /// <summary>Folds a <c>function_call</c> item into the turn.</summary>
        /// <param name="item">The item to fold in.</param>
        public void AddFunctionCall(ResponseInputItem item)
        {
            if (item.CallId is not { Length: > 0 } callId || item.Name is not { Length: > 0 } name)
            {
                return;
            }

            _hasContent = true;
            _toolCalls ??= [];
            _toolCalls.Add(new(_toolCalls.Count, callId, new NimFunctionCall(name, item.Arguments ?? "{}")));
        }

        /// <summary>Emits the accumulated turn as one upstream assistant message, and resets.</summary>
        /// <param name="messages">The upstream message list to append to.</param>
        public void Flush(List<NimChatMessage> messages)
        {
            if (!_hasContent)
            {
                return;
            }

            messages.Add(new(
                NimChatMessage.AssistantRole,
                NimContent.FromText(_text.ToString()),
                _toolCalls,
                ReasoningContent: _reasoning.Length > 0 ? _reasoning.ToString() : null));

            _ = _text.Clear();
            _ = _reasoning.Clear();
            _toolCalls = null;
            _hasContent = false;
        }

        /// <summary>Appends the text of every part in a list, separating parts with a blank line.</summary>
        /// <param name="builder">The builder to append to.</param>
        /// <param name="parts">The parts to append.</param>
        private static void AppendParts(StringBuilder builder, List<ResponseContentItem>? parts)
        {
            if (parts is not { Count: > 0 })
            {
                return;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i].Text is not { Length: > 0 } text)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    _ = builder.Append("\n\n");
                }

                _ = builder.Append(text);
            }
        }
    }
}
