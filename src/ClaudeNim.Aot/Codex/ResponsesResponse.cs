// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Codex;

/// <summary>The OpenAI Responses API turn a Codex-family client expects back.</summary>
/// <param name="Id">The identifier to report for the turn.</param>
/// <param name="Model">The model identifier to echo back to the client.</param>
/// <param name="Status">The turn's status; see <see cref="StatusCompleted"/> and its siblings.</param>
/// <param name="Output">The items the turn produced.</param>
/// <param name="Object">The object discriminator, always <c>response</c>.</param>
/// <param name="CreatedAt">The Unix timestamp, in seconds, the turn was produced at.</param>
/// <param name="OutputText">The convenience concatenation of every <c>output_text</c> part in <paramref name="Output"/>.</param>
/// <param name="Usage">The turn's token accounting.</param>
/// <param name="IncompleteDetails">Why the turn stopped early, present only when <paramref name="Status"/> is <see cref="StatusIncomplete"/>.</param>
/// <param name="Error">The failure, present only when <paramref name="Status"/> is <see cref="StatusFailed"/>.</param>
[System.Diagnostics.DebuggerDisplay("ResponsesResponse: {Id}")]
public sealed record ResponsesResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("output")] List<ResponseInputItem> Output,
    [property: JsonPropertyName("object")] string Object = "response",
    [property: JsonPropertyName("created_at")] long CreatedAt = 0,
    [property: JsonPropertyName("output_text")] string? OutputText = null,
    [property: JsonPropertyName("usage")] ResponseUsage? Usage = null,
    [property: JsonPropertyName("incomplete_details")] IncompleteDetails? IncompleteDetails = null,
    [property: JsonPropertyName("error")] ResponseError? Error = null)
{
    /// <summary>The turn finished normally.</summary>
    internal const string StatusCompleted = "completed";

    /// <summary>The turn failed.</summary>
    internal const string StatusFailed = "failed";

    /// <summary>The turn stopped before it finished, for example at the output ceiling.</summary>
    internal const string StatusIncomplete = "incomplete";

    /// <summary>The turn is still being produced.</summary>
    internal const string StatusInProgress = "in_progress";
}
