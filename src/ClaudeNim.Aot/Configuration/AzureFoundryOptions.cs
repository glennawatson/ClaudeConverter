// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Configures Azure Foundry independently of other hosted backends.</summary>
/// <param name="Enabled">Whether Azure routes use this endpoint rather than the legacy OpenAi settings.</param>
/// <param name="BaseUrl">The resource endpoint, optionally ending in /openai/v1.</param>
/// <param name="ApiKey">The resource API key, sent only to Azure.</param>
/// <param name="ReadSeconds">The streamed response header timeout, in seconds.</param>
/// <param name="CompletionSeconds">The non-streamed response timeout, in seconds.</param>
/// <param name="ModelNames">Underlying model names keyed by deployment name, for deployments with custom names.</param>
[System.Diagnostics.DebuggerDisplay("AzureFoundryOptions: {BaseUrl}")]
public sealed record AzureFoundryOptions(
    bool Enabled = false,
    string BaseUrl = "",
    string ApiKey = "",
    int ReadSeconds = 60,
    int CompletionSeconds = 600,
    Dictionary<string, string>? ModelNames = null)
{
    /// <summary>The configuration section used by this provider.</summary>
    internal const string SectionName = "AzureFoundry";

    /// <summary>Resolves the v1 chat-completions base address.</summary>
    /// <returns>The endpoint with a trailing separator.</returns>
    internal string Endpoint()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return "http://localhost/openai/v1/";
        }

        var endpoint = BaseUrl.TrimEnd('/');
        return endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{endpoint}/"
            : $"{endpoint}/openai/v1/";
    }
}
