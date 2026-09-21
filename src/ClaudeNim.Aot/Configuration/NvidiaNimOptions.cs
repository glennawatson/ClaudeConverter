// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Configuration;

/// <summary>Connection and sampling defaults for the NVIDIA NIM upstream.</summary>
/// <param name="ApiKey">The NVIDIA NIM API key used as the bearer credential.</param>
/// <param name="BaseUrl">The OpenAI-compatible base address of the NIM endpoint.</param>
/// <param name="Proxy">An optional outbound HTTP proxy, or an empty string for none.</param>
/// <param name="MaxTokens">The ceiling applied to the requested completion length.</param>
/// <param name="Temperature">The sampling temperature applied when the caller supplies none.</param>
/// <param name="TopP">The nucleus sampling cutoff applied when the caller supplies none.</param>
/// <param name="TopK">The top-k sampling cutoff, or a negative value to leave it unset.</param>
/// <param name="MinP">The minimum token probability, or zero to leave it unset.</param>
/// <param name="RepetitionPenalty">The repetition penalty; a value of one leaves it unset.</param>
/// <param name="PresencePenalty">The presence penalty; a value of zero leaves it unset.</param>
/// <param name="FrequencyPenalty">The frequency penalty; a value of zero leaves it unset.</param>
/// <param name="MinTokens">The minimum number of tokens to generate; zero leaves it unset.</param>
/// <param name="Seed">The deterministic sampling seed, or <see langword="null"/> for none.</param>
/// <param name="Stop">An additional stop sequence, or an empty string for none.</param>
/// <param name="ParallelToolCalls">Whether the model may emit parallel tool calls.</param>
/// <param name="IgnoreEos">Whether the end-of-sequence token is ignored.</param>
[System.Diagnostics.DebuggerDisplay("NvidiaNimOptions: {ToString(),nq}")]
public sealed record NvidiaNimOptions(
    string ApiKey = "",
    string BaseUrl = NvidiaNimOptions.DefaultBaseUrl,
    string Proxy = "",
    int MaxTokens = 4096,
    double Temperature = 1.0,
    double TopP = 1.0,
    int TopK = -1,
    double MinP = 0.0,
    double RepetitionPenalty = 1.0,
    double PresencePenalty = 0.0,
    double FrequencyPenalty = 0.0,
    int MinTokens = 0,
    int? Seed = null,
    string Stop = "",
    bool ParallelToolCalls = true,
    bool IgnoreEos = false)
{
    /// <summary>The configuration section these options are bound from.</summary>
    internal const string SectionName = "NvidiaNim";

    /// <summary>The public NVIDIA-hosted endpoint used when no base address is configured.</summary>
    internal const string DefaultBaseUrl = "https://integrate.api.nvidia.com/v1";
}
