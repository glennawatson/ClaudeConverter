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

        var messages = BuildMessages(request, thinkingEnabled);
        var tools = BuildTools(request.Tools);
        var maxTokens = ResolveMaxTokens(request.MaxTokens, options.MaxTokens);

        // Usage is only reported on a streamed turn when it is asked for up front. Without it
        // the output token count has to be guessed from response length.
        var streamOptions = request.IsStreaming ? new NimStreamOptions(true) : (NimStreamOptions?)null;
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
            TopK: request.TopK ?? (options.TopK >= 0 ? options.TopK : null),
            MinP: NullIf(options.MinP, 0.0),
            RepetitionPenalty: NullIf(options.RepetitionPenalty, 1.0),
            PresencePenalty: NullIf(options.PresencePenalty, 0.0),
            FrequencyPenalty: NullIf(options.FrequencyPenalty, 0.0),
            MinTokens: options.MinTokens > 0 ? options.MinTokens : null,
            Seed: options.Seed,
            Stop: ResolveStop(request.StopSequences, options.Stop),
            IgnoreEos: options.IgnoreEos ? true : null,
            Tools: tools,
            ToolChoice: toolChoice,
            ParallelToolCalls: ResolveParallelToolCalls(request, options, tools is not null),
            ChatTemplateKwargs: BuildTemplateArguments(request, thinkingEnabled, tools is not null));
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
        // The reasoning budget is only ever forwarded when the caller asked for one. Deriving it
        // from max_tokens looks harmless and is not: Nemotron 3 Ultra's runner rejects the field
        // outright ("thinking_token_budget is not yet supported"), and on a streamed turn it does
        // so as an error inside a 200 response, which no status-code retry can catch. Sending it
        // unasked turned every reasoning turn into an empty one.
        return new(
            EnableThinking: true,
            Thinking: true,
            LowEffort: reduced ? true : null,
            MediumEffort: moderate ? true : null,
            ReasoningBudget: request.Thinking?.BudgetTokens,
            ForceNonemptyContent: hasTools ? true : null);
    }

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
            var schema = ToolSchemaSanitizer.Sanitize(ToolChoiceTranslator.SchemaOrEmpty(tool.InputSchema));
            result.Add(new(new NimFunctionDefinition(tool.Name, tool.Description ?? string.Empty, schema)));
        }

        return result;
    }

    /// <summary>Builds the messages list for the upstream request.</summary>
    /// <param name="request">The caller's Anthropic request.</param>
    /// <param name="thinkingEnabled">Whether reasoning was requested for this tier.</param>
    /// <returns>The upstream messages.</returns>
    private static List<NimChatMessage> BuildMessages(MessagesRequest request, bool thinkingEnabled)
    {
        var messages = new List<NimChatMessage>(request.Messages.Count + 1);

        var system = ContentText.Extract(request.System);
        if (system.Length > 0)
        {
            messages.Add(new(NimChatMessage.SystemRole, system));
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
                AppendUser(messages, message);
            }
        }

        return messages;
    }

    /// <summary>Appends a user message to the upstream message list.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="message">The Anthropic user message.</param>
    private static void AppendUser(List<NimChatMessage> messages, AnthropicMessage message)
    {
        var content = message.Content;
        if (content.Text is not null)
        {
            messages.Add(new(NimChatMessage.UserRole, content.Text));
            return;
        }

        if (content.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        var text = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (string.Equals(block.Type, ContentBlockTypes.ToolResult, StringComparison.Ordinal))
            {
                FlushUserText(messages, text);
                messages.Add(new(
                    NimChatMessage.ToolRole,
                    ContentText.FromToolResult(block.Content),
                    ToolCallId: block.ToolUseId));
                continue;
            }

            var blockText = block.Type switch
            {
                ContentBlockTypes.Text => block.Text,
                ContentBlockTypes.Image => "[Image]",
                _ => null,
            };

            if (string.IsNullOrEmpty(blockText))
            {
                continue;
            }

            if (text.Length > 0)
            {
                _ = text.Append('\n');
            }

            _ = text.Append(blockText);
        }

        FlushUserText(messages, text);
    }

    /// <summary>Appends accumulated user text as a message if non-empty.</summary>
    /// <param name="messages">The upstream message list to append to.</param>
    /// <param name="text">The accumulated text to flush.</param>
    private static void FlushUserText(List<NimChatMessage> messages, StringBuilder text)
    {
        if (text.Length == 0)
        {
            return;
        }

        messages.Add(new(NimChatMessage.UserRole, text.ToString()));
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
            messages.Add(new(NimChatMessage.AssistantRole, content.Text));
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
            answer,
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
