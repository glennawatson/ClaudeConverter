// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Text.Json;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Distinguishes backend failures and model token limits from invalid caller input.</summary>
internal static class UpstreamFallbackPolicy
{
    /// <summary>Determines whether a failure needs inspection before trying another model.</summary>
    /// <param name="status">The upstream response status.</param>
    /// <returns>True for backend failures and statuses that can carry model-specific token limits.</returns>
    internal static bool NeedsInspection(HttpStatusCode status) =>
        IsBackendUnavailable(status) || status is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity;

    /// <summary>Determines whether a backend's credential, quota or availability prevents serving the turn.</summary>
    /// <param name="status">The upstream response status.</param>
    /// <returns>True when another configured backend may serve the turn.</returns>
    internal static bool IsBackendUnavailable(HttpStatusCode status) =>
        RetrySchedule.IsTransient(status) || status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired;

    /// <summary>Recognizes a model's context window rejection without treating malformed input as retryable.</summary>
    /// <param name="body">The complete upstream error body.</param>
    /// <param name="cancellationToken">Abandons inspection when the client disconnects.</param>
    /// <returns>True when the model cannot fit this turn's tokens.</returns>
    internal static bool IsContextLimit(string body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = JsonDocument.Parse(body);
            var detail = document.RootElement;

            if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("error", out var nested))
            {
                detail = nested;
            }

            return detail.ValueKind == JsonValueKind.String
                ? DescribesContextLimit(detail.GetString())
                : detail.ValueKind == JsonValueKind.Object && IsContextLimitObject(detail);
        }
        catch (JsonException)
        {
            return DescribesContextLimit(body);
        }
    }

    /// <summary>Checks a structured error without confusing content filtering with a token limit.</summary>
    /// <param name="error">The upstream error object.</param>
    /// <returns>True when its code or message identifies a context limit.</returns>
    private static bool IsContextLimitObject(JsonElement error)
    {
        var code = StringProperty(error, "code");
        if (code is "content_filter" or "content_policy_violation")
        {
            return false;
        }

        return code is "context_length_exceeded" or "context_window_exceeded" or "max_tokens_exceeded"
            || DescribesContextLimit(StringProperty(error, "message") ?? StringProperty(error, "detail"));
    }

    /// <summary>Reads an error field only when its value is a string.</summary>
    /// <param name="error">The upstream error object.</param>
    /// <param name="name">The field to read.</param>
    /// <returns>The field's text, or null for absent and non-string fields.</returns>
    private static string? StringProperty(JsonElement error, string name) =>
        error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Recognizes explicit token-window descriptions rather than generic request errors.</summary>
    /// <param name="message">The provider's error message.</param>
    /// <returns>True when the message names a context limit.</returns>
    private static bool DescribesContextLimit(string? message) =>
        message is not null
        && (message.Contains("maximum context length", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context length exceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context window exceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase)
            || message.Contains("input is too long", StringComparison.OrdinalIgnoreCase));
}
