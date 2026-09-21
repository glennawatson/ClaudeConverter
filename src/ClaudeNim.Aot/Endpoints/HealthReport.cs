// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json.Serialization;

namespace ClaudeNim.Aot.Endpoints;

/// <summary>The body returned from the health route.</summary>
/// <param name="Status">Either <c>ok</c> or <c>degraded</c>.</param>
/// <param name="ModelCount">How many models the proxy can currently offer.</param>
[System.Diagnostics.DebuggerDisplay("HealthReport: {ToString(),nq}")]
public readonly record struct HealthReport(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("model_count")] int ModelCount);
