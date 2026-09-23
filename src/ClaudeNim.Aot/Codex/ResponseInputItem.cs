// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>One item of a Responses API <c>input</c> or <c>output</c> array.</summary>
/// <param name="Type">The item discriminator; see <see cref="ResponseItemTypes"/>.</param>
/// <param name="Id">The item identifier, assigned by whichever side produced it.</param>
/// <param name="Role">The author of a <c>message</c> item.</param>
/// <param name="Content">The parts of a <c>message</c> item, or the disclosed trace of a <c>reasoning</c> item.</param>
/// <param name="Status">Whether the item is still being produced.</param>
/// <param name="CallId">The call a <c>function_call</c> or <c>function_call_output</c> item carries or answers.</param>
/// <param name="Name">The tool name of a <c>function_call</c> item.</param>
/// <param name="Arguments">The JSON-encoded arguments of a <c>function_call</c> item.</param>
/// <param name="Output">The result of a <c>function_call_output</c> item.</param>
/// <param name="Summary">The summary parts of a <c>reasoning</c> item.</param>
/// <remarks>
/// One flat type serves both directions of the conversation and every item kind, matching
/// <see cref="Anthropic.ContentBlock"/>'s reasoning: native AOT cannot generate the polymorphic
/// resolver a type hierarchy over
/// <c>message</c>/<c>function_call</c>/<c>function_call_output</c>/<c>reasoning</c> would need.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ResponseInputItem: {ToString(),nq}")]
public sealed record ResponseInputItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("content")] List<ResponseContentItem>? Content = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("call_id")] string? CallId = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("arguments")] string? Arguments = null,
    [property: JsonPropertyName("output")] FunctionCallOutput? Output = null,
    [property: JsonPropertyName("summary")] List<ResponseContentItem>? Summary = null)
{
    /// <summary>Creates a <c>message</c> item.</summary>
    /// <param name="role">The author.</param>
    /// <param name="content">The message parts.</param>
    /// <returns>The created item.</returns>
    public static ResponseInputItem ForMessage(string role, List<ResponseContentItem> content) =>
        new(ResponseItemTypes.Message, Role: role, Content: content);

    /// <summary>Creates a <c>function_call</c> item.</summary>
    /// <param name="callId">The call identifier.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="arguments">The JSON-encoded arguments.</param>
    /// <returns>The created item.</returns>
    public static ResponseInputItem ForFunctionCall(string callId, string name, string arguments) =>
        new(ResponseItemTypes.FunctionCall, CallId: callId, Name: name, Arguments: arguments);

    /// <summary>Creates a <c>reasoning</c> item.</summary>
    /// <param name="summary">The summary parts.</param>
    /// <returns>The created item.</returns>
    public static ResponseInputItem ForReasoning(List<ResponseContentItem> summary) =>
        new(ResponseItemTypes.Reasoning, Summary: summary);
}
