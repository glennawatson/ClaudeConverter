// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Configuration;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Translates an Anthropic Messages request into the NVIDIA NIM chat completions request that serves it.</summary>
public static class NimRequestBuilder
{
    /// <summary>Builds the upstream request.</summary>
    /// <param name="request">The caller's Anthropic request.</param>
    /// <param name="model">The NIM model the router selected.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="options">The configured NIM defaults.</param>
    /// <returns>The upstream request body.</returns>
    public static NimChatRequest Build(
        MessagesRequest request,
        string model,
        bool thinkingEnabled,
        NvidiaNimOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var messages = BuildMessages(request, thinkingEnabled, NimModelCatalogDefaults.SupportsVision(model));
        var tools = BuildTools(request.Tools);
        var maxTokens = ResolveMaxTokens(request.MaxTokens, options.MaxTokens);

        // Usage is only reported on a streamed turn when it is asked for up front. Without it
        // the output token count has to be guessed from response length.
        var streamOptions = request.IsStreaming ? new NimStreamOptions(true) : (NimStreamOptions?)null;
        var effort = thinkingEnabled ? EffortLevels.ToReasoningEffort(request.OutputConfig?.Effort) : null;
        var toolChoice = tools is null
            ? (JsonElement?)null
            : ToolChoiceTranslator.Translate(request.ToolChoice) ?? ToolChoiceTranslator.Auto;

        return new(
            model,
            messages,
            maxTokens,
            request.IsStreaming,
            StreamOptions: streamOptions,
            Temperature: request.Temperature ?? NullIf(options.Temperature, 1.0),
            TopP: request.TopP ?? NullIf(options.TopP, 1.0),
            TopK: request.TopK ?? TopKOrNull(options),
            MinP: NullIf(options.MinP, 0.0),
            RepetitionPenalty: NullIf(options.RepetitionPenalty, 1.0),
            PresencePenalty: NullIf(options.PresencePenalty, 0.0),
            FrequencyPenalty: NullIf(options.FrequencyPenalty, 0.0),
            MinTokens: MinTokensOrNull(options),
            Seed: options.Seed,
            Stop: ResolveStop(request.StopSequences, options.Stop),
            IgnoreEos: IgnoreEosOrNull(options),
            Tools: tools,
            ToolChoice: toolChoice,
            ParallelToolCalls: ResolveParallelToolCalls(request, options, tools is not null),
            ChatTemplateKwargs: BuildTemplateArguments(request, thinkingEnabled, tools is not null),
            ReasoningEffort: effort,
            Extensions: BuildExtensions(request, thinkingEnabled));
    }

    /// <summary>Resolves the maximum tokens by selecting the smaller of requested and configured ceiling.</summary>
    /// <param name="requested">The caller's requested max tokens.</param>
    /// <param name="ceiling">The configured maximum ceiling.</param>
    /// <returns>The effective maximum tokens.</returns>
    private static int ResolveMaxTokens(int requested, int ceiling)
    {
        if (requested <= 0)
        {
            return ceiling;
        }

        return ceiling > 0 && requested > ceiling ? ceiling : requested;
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

    /// <summary>Resolves the stop sequences, preferring requested over the configured default.</summary>
    /// <param name="requested">The caller's requested stop sequences.</param>
    /// <param name="configured">The configured default stop sequence.</param>
    /// <returns>The stop sequences, or null if neither requested nor configured.</returns>
    private static List<string>? ResolveStop(List<string>? requested, string configured)
    {
        if (requested is { Count: > 0 })
        {
            return requested;
        }

        return string.IsNullOrEmpty(configured) ? null : [configured];
    }

    /// <summary>Resolves whether parallel tool calls should be enabled.</summary>
    /// <param name="request">The caller's Anthropic request.</param>
    /// <param name="options">The configured NIM defaults.</param>
    /// <param name="hasTools">Whether the request includes tool definitions.</param>
    /// <returns>True to disable parallel calls, false to enable, or null to use the upstream default.</returns>
    private static bool? ResolveParallelToolCalls(
        MessagesRequest request,
        NvidiaNimOptions options,
        bool hasTools)
    {
        if (!hasTools)
        {
            return null;
        }

        if (ToolChoiceTranslator.DisablesParallelToolCalls(request.ToolChoice))
        {
            return false;
        }

        return options.ParallelToolCalls ? null : false;
    }

    /// <summary>Builds template arguments for reasoning and tool support.</summary>
    /// <param name="request">The caller's Anthropic request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="hasTools">Whether the request includes tool definitions.</param>
    /// <returns>The template arguments, or null if neither reasoning nor tools are needed.</returns>
    private static NimChatTemplateKwargs? BuildTemplateArguments(
        MessagesRequest request,
        bool thinkingEnabled,
        bool hasTools)
    {
        if (!thinkingEnabled && !hasTools)
        {
            return null;
        }

        if (!thinkingEnabled)
        {
            return new(EnableThinking: false, Thinking: false);
        }

        var effort = request.OutputConfig?.Effort;
        var reduced = string.Equals(effort, EffortLevels.Low, StringComparison.Ordinal);
        var moderate = string.Equals(effort, EffortLevels.Medium, StringComparison.Ordinal);

        // NVIDIA documents force_nonempty_content as required when tools are combined with
        // reasoning: without it a reasoning model can return a trace and no answer, which a
        // coding client reads as an empty turn.
        // No reasoning budget is set here. The chat template is the wrong channel for it — an
        // argument the template does not know is passed through and fails deep inside generation,
        // which on a streamed turn surfaces as an error inside a 200 response that no status-code
        // retry can catch. The budget goes through nvext instead, where an unsupported field is
        // rejected up front.
        return new(
            EnableThinking: true,
            Thinking: true,
            LowEffort: reduced ? true : null,
            MediumEffort: moderate ? true : null,
            ForceNonemptyContent: hasTools ? true : null);
    }

    /// <summary>Builds NVIDIA's own request extensions.</summary>
    /// <param name="request">The caller's Anthropic request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <returns>The extensions, or <see langword="null"/> when there is nothing to send.</returns>
    /// <remarks>
    /// The budget is only forwarded when the caller explicitly asked for one. Deriving it from
    /// <c>max_tokens</c> looks harmless and is not — it made every reasoning turn on models whose
    /// runner lacks the field come back empty.
    /// </remarks>
    private static NimExtensions? BuildExtensions(MessagesRequest request, bool thinkingEnabled) =>
        !thinkingEnabled || request.Thinking?.BudgetTokens is not { } budget || budget <= 0
            ? null
            : new NimExtensions(budget);

    /// <summary>Converts Anthropic tool definitions into NIM tools.</summary>
    /// <param name="tools">The caller's tool definitions.</param>
    /// <returns>The NIM tools, or null if no tools are defined.</returns>
    private static List<NimTool>? BuildTools(List<ToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return null;
        }

        var result = new List<NimTool>(tools.Count);
        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            var declared = ToolChoiceTranslator.SchemaOrEmpty(tool.InputSchema);
            var schema = ToolParameterAliases.Apply(ToolSchemaSanitizer.Sanitize(declared));
            result.Add(new(new NimFunctionDefinition(tool.Name, tool.Description ?? string.Empty, schema)));
        }

        return result;
    }

    /// <summary>Builds the messages list for the upstream request.</summary>
    /// <param name="request">The caller's Anthropic request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    /// <returns>The upstream messages.</returns>
    private static List<NimChatMessage> BuildMessages(MessagesRequest request, bool thinkingEnabled, bool vision)
    {
        var messages = new List<NimChatMessage>(request.Messages.Count + 1);

        var system = ContentText.Extract(request.System);
        if (system.Length > 0)
        {
            messages.Add(new(NimChatMessage.SystemRole, NimContent.FromText(system)));
        }

        for (var i = 0; i < request.Messages.Count; i++)
        {
            var message = request.Messages[i];
            if (string.Equals(message.Role, AnthropicMessage.AssistantRole, StringComparison.Ordinal))
            {
                AppendAssistant(messages, message, thinkingEnabled);
            }
            else
            {
                AppendUser(messages, message, vision);
            }
        }

        return messages;
    }

    /// <summary>Appends a user message to the upstream message list.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="message">The Anthropic user message.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    /// <remarks>
    /// An image is forwarded when the model can see it and replaced with a placeholder when it
    /// cannot. The listing reports the same distinction, so a client is never told a model accepts
    /// images and then given a turn the picture was quietly dropped from.
    /// </remarks>
    private static void AppendUser(List<NimChatMessage> messages, AnthropicMessage message, bool vision)
    {
        var content = message.Content;
        if (content.Text is not null)
        {
            messages.Add(new(NimChatMessage.UserRole, NimContent.FromText(content.Text)));
            return;
        }

        if (content.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        var text = new StringBuilder();
        var images = vision ? new List<string>() : null;

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (string.Equals(block.Type, ContentBlockTypes.ToolResult, StringComparison.Ordinal))
            {
                FlushUserText(messages, text, images);
                messages.Add(new(
                    NimChatMessage.ToolRole,
                    NimContent.FromText(ContentText.FromToolResult(block.Content)),
                    ToolCallId: block.ToolUseId));
                continue;
            }

            if (images is not null
                && string.Equals(block.Type, ContentBlockTypes.Image, StringComparison.Ordinal)
                && ImageUrl(block.Source) is { } url)
            {
                images.Add(url);
                continue;
            }

            AppendBlockText(text, block, vision);
        }

        FlushUserText(messages, text, images);
    }

    /// <summary>Appends the readable text of one block to the turn being composed.</summary>
    /// <param name="text">The text accumulated for this turn.</param>
    /// <param name="block">The block to append.</param>
    /// <param name="vision">Whether the target model accepts image input.</param>
    private static void AppendBlockText(StringBuilder text, ContentBlock block, bool vision)
    {
        var blockText = block.Type switch
        {
            ContentBlockTypes.Text => block.Text,
            ContentBlockTypes.Image => vision ? null : "[Image]",
            _ => null,
        };

        if (string.IsNullOrEmpty(blockText))
        {
            return;
        }

        if (text.Length > 0)
        {
            _ = text.Append('\n');
        }

        _ = text.Append(blockText);
    }

    /// <summary>Renders an Anthropic image source as the URL an OpenAI content part carries.</summary>
    /// <param name="source">The image source, which may be absent.</param>
    /// <returns>The URL, or <see langword="null"/> when the source cannot be rendered.</returns>
    /// <remarks>
    /// Anthropic sends base64 bytes with a separate media type; the OpenAI shape wants both folded
    /// into one <c>data:</c> URL. A source of any other kind is passed through as given.
    /// </remarks>
    private static string? ImageUrl(ImageSource? source)
    {
        if (source is not { } image || string.IsNullOrEmpty(image.Data))
        {
            return null;
        }

        return string.IsNullOrEmpty(image.MediaType)
            ? image.Data
            : $"data:{image.MediaType};base64,{image.Data}";
    }

    /// <summary>Appends accumulated user text as a message if non-empty.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="text">The accumulated text to flush.</param>
    /// <param name="images">The images gathered for this turn, or null when the model has no vision.</param>
    private static void FlushUserText(List<NimChatMessage> messages, StringBuilder text, List<string>? images)
    {
        if (images is { Count: > 0 })
        {
            var parts = new List<NimContentPart>(images.Count + 1);
            if (text.Length > 0)
            {
                parts.Add(NimContentPart.ForText(text.ToString()));
            }

            for (var i = 0; i < images.Count; i++)
            {
                parts.Add(NimContentPart.ForImage(images[i]));
            }

            messages.Add(new(NimChatMessage.UserRole, NimContent.FromParts(parts)));
            _ = text.Clear();
            images.Clear();
            return;
        }

        if (text.Length == 0)
        {
            return;
        }

        messages.Add(new(NimChatMessage.UserRole, NimContent.FromText(text.ToString())));
        _ = text.Clear();
    }

    // Folds one assistant block into the accumulating text, reasoning and tool calls, returning
    // the tool-call list so the caller can keep the "none yet" case as a null.
    /// <summary>Collects an assistant content block into text, reasoning, and tool calls.</summary>
    /// <param name="block">The content block to collect.</param>
    /// <param name="thinkingEnabled">Whether reasoning blocks should be collected.</param>
    /// <param name="text">The text accumulator.</param>
    /// <param name="reasoning">The reasoning accumulator.</param>
    /// <param name="toolCalls">The tool calls list, or null if no calls have been collected yet.</param>
    /// <returns>The updated tool calls list.</returns>
    private static List<NimToolCall>? CollectAssistantBlock(
        ContentBlock block,
        bool thinkingEnabled,
        StringBuilder text,
        StringBuilder reasoning,
        List<NimToolCall>? toolCalls)
    {
        if (string.Equals(block.Type, ContentBlockTypes.Text, StringComparison.Ordinal))
        {
            Append(text, block.Text);
            return toolCalls;
        }

        if (thinkingEnabled && string.Equals(block.Type, ContentBlockTypes.Thinking, StringComparison.Ordinal))
        {
            Append(reasoning, block.Thinking);
            return toolCalls;
        }

        if (!string.Equals(block.Type, ContentBlockTypes.ToolUse, StringComparison.Ordinal))
        {
            return toolCalls;
        }

        toolCalls ??= [];
        toolCalls.Add(new(
            toolCalls.Count,
            block.Id,
            new NimFunctionCall(block.Name, RenderToolInput(block.Input))));

        return toolCalls;
    }

    /// <summary>Appends an assistant message to the upstream message list.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="message">The Anthropic assistant message.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    private static void AppendAssistant(List<NimChatMessage> messages, AnthropicMessage message, bool thinkingEnabled)
    {
        var content = message.Content;
        if (content.Text is not null)
        {
            messages.Add(new(NimChatMessage.AssistantRole, NimContent.FromText(content.Text)));
            return;
        }

        if (content.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        List<NimToolCall>? toolCalls = null;

        for (var i = 0; i < blocks.Count; i++)
        {
            toolCalls = CollectAssistantBlock(blocks[i], thinkingEnabled, text, reasoning, toolCalls);
        }

        // upstreams differ on whether they accept a missing one during history replay.
        var answer = text.Length > 0 ? text.ToString() : string.Empty;

        messages.Add(new(
            NimChatMessage.AssistantRole,
            NimContent.FromText(answer),
            toolCalls,
            ReasoningContent: reasoning.Length > 0 ? reasoning.ToString() : null));
    }

    /// <summary>Renders tool input as a JSON string.</summary>
    /// <param name="input">The tool input JSON element.</param>
    /// <returns>The JSON text, or an empty object if input is undefined.</returns>
    private static string RenderToolInput(JsonElement? input) =>
        input is { ValueKind: not JsonValueKind.Undefined } value ? value.GetRawText() : "{}";

    /// <summary>Appends a value to the builder, separating blocks with double newlines.</summary>
    /// <param name="builder">The string builder to append to.</param>
    /// <param name="value">The value to append.</param>
    private static void Append(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            _ = builder.Append("\n\n");
        }

        _ = builder.Append(value);
    }
}
