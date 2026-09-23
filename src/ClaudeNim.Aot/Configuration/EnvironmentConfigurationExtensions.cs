// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Microsoft.Extensions.Configuration;

namespace ClaudeNim.Aot.Configuration;

/// <summary>Layers environment-variable configuration over the settings file.</summary>
/// <remarks>
/// <para>
/// The proxy is most often run from a shell or a container, where a settings file is awkward and a
/// credential in one is a liability. Two independent styles are therefore supported, and both win
/// over <c>appsettings.json</c>.
/// </para>
/// <para>
/// The first is the .NET convention: any setting can be addressed by its full path with a double
/// underscore for the separator, optionally behind the <c>CLAUDENIM_</c> prefix — so
/// <c>CLAUDENIM_NvidiaNim__MaxTokens=8192</c> and <c>NvidiaNim__MaxTokens=8192</c> both work, and
/// nothing has to be enumerated here for a setting to be reachable.
/// </para>
/// <para>
/// The second is the flat, shouted name the equivalent Node and Python proxies use —
/// <c>NVIDIA_API_KEY</c>, <c>BIG_MODEL</c>, <c>ANTHROPIC_AUTH_TOKEN</c>. Those names are what
/// existing deployment scripts already export, so they are mapped explicitly rather than leaving
/// a working script silently ignored. They are read first, which lets the .NET-style names
/// override them when both are present.
/// </para>
/// </remarks>
public static class EnvironmentConfigurationExtensions
{
    /// <summary>The prefix the .NET-style environment names may optionally carry.</summary>
    private const string Prefix = "CLAUDENIM_";

    /// <summary>The flat environment names, each paired with the configuration key it feeds.</summary>
    /// <remarks>
    /// Several keys appear more than once. Where they do, the later name wins, so the first entry
    /// of a group is the legacy spelling and the last is this proxy's own.
    /// </remarks>
    private static readonly EnvironmentBinding[] FlatNames =
    [
        new("NVIDIA_API_KEY", "NvidiaNim:ApiKey"),
        new("NVIDIA_NIM_API_KEY", "NvidiaNim:ApiKey"),
        new("NVIDIA_BASE_URL", "NvidiaNim:BaseUrl"),
        new("NIM_BASE_URL", "NvidiaNim:BaseUrl"),
        new("NIM_PROXY", "NvidiaNim:Proxy"),
        new("MAX_TOKENS", "NvidiaNim:MaxTokens"),
        new("MIN_TOKENS", "NvidiaNim:MinTokens"),
        new("TEMPERATURE", "NvidiaNim:Temperature"),
        new("TOP_P", "NvidiaNim:TopP"),
        new("TOP_K", "NvidiaNim:TopK"),
        new("MIN_P", "NvidiaNim:MinP"),
        new("REPETITION_PENALTY", "NvidiaNim:RepetitionPenalty"),
        new("PRESENCE_PENALTY", "NvidiaNim:PresencePenalty"),
        new("FREQUENCY_PENALTY", "NvidiaNim:FrequencyPenalty"),
        new("SEED", "NvidiaNim:Seed"),
        new("STOP", "NvidiaNim:Stop"),
        new("PARALLEL_TOOL_CALLS", "NvidiaNim:ParallelToolCalls"),
        new("IGNORE_EOS", "NvidiaNim:IgnoreEos"),

        new("ANTHROPIC_AUTH_TOKEN", "Authentication:AuthToken"),
        new("ANTHROPIC_API_KEY", "Authentication:AuthToken"),
        new("PROXY_AUTH_TOKEN", "Authentication:AuthToken"),

        new("DEFAULT_MODEL", "ModelRouting:Default"),
        new("BIG_MODEL", "ModelRouting:Opus"),
        new("OPUS_MODEL", "ModelRouting:Opus"),
        new("MIDDLE_MODEL", "ModelRouting:Sonnet"),
        new("SONNET_MODEL", "ModelRouting:Sonnet"),
        new("SMALL_MODEL", "ModelRouting:Haiku"),
        new("HAIKU_MODEL", "ModelRouting:Haiku"),
        new("FALLBACK_MODELS", "ModelRouting:DefaultFallbacks"),
        new("BIG_FALLBACK_MODELS", "ModelRouting:OpusFallbacks"),
        new("OPUS_FALLBACK_MODELS", "ModelRouting:OpusFallbacks"),
        new("MIDDLE_FALLBACK_MODELS", "ModelRouting:SonnetFallbacks"),
        new("SONNET_FALLBACK_MODELS", "ModelRouting:SonnetFallbacks"),
        new("SMALL_FALLBACK_MODELS", "ModelRouting:HaikuFallbacks"),
        new("HAIKU_FALLBACK_MODELS", "ModelRouting:HaikuFallbacks"),

        new("ENABLE_THINKING", "ModelRouting:EnableThinking"),
        new("ENABLE_OPUS_THINKING", "ModelRouting:EnableOpusThinking"),
        new("ENABLE_SONNET_THINKING", "ModelRouting:EnableSonnetThinking"),
        new("ENABLE_HAIKU_THINKING", "ModelRouting:EnableHaikuThinking"),

        new("MODEL_CACHE_MINUTES", "ModelCatalog:CacheMinutes"),
        new("DEFAULT_CONTEXT_WINDOW", "ModelCatalog:DefaultContextWindow"),
        new("DEFAULT_MAX_OUTPUT_TOKENS", "ModelCatalog:DefaultMaxOutputTokens"),
        new("ADVERTISE_CLAUDE_ALIASES", "ModelCatalog:AdvertiseClaudeAliases"),

        new("REQUESTS_PER_WINDOW", "RateLimits:RequestsPerWindow"),
        new("RATE_LIMIT_WINDOW_SECONDS", "RateLimits:WindowSeconds"),
        new("MAX_CONCURRENCY", "RateLimits:MaxConcurrency"),
        new("MIN_GAP_MILLISECONDS", "RateLimits:MinGapMilliseconds"),

        new("CONTENT_AWARE_DOWNGRADE", "Retries:ContentAwareDowngrade"),

        new("READ_TIMEOUT_SECONDS", "Timeouts:ReadSeconds"),
        new("CONNECT_TIMEOUT_SECONDS", "Timeouts:ConnectSeconds"),
        new("STREAM_IDLE_TIMEOUT_SECONDS", "Timeouts:StreamIdleSeconds"),
        new("COMPLETION_TIMEOUT_SECONDS", "Timeouts:CompletionSeconds"),
        new("OPUS_READ_TIMEOUT_SECONDS", "Timeouts:OpusReadSeconds"),
        new("SONNET_READ_TIMEOUT_SECONDS", "Timeouts:SonnetReadSeconds"),
        new("HAIKU_READ_TIMEOUT_SECONDS", "Timeouts:HaikuReadSeconds"),

        new("MODEL_FAILURE_THRESHOLD", "ModelHealth:FailureThreshold"),
        new("MODEL_COOLDOWN_SECONDS", "ModelHealth:CooldownSeconds"),

        new("OLLAMA_ENABLED", "Ollama:Enabled"),
        new("OLLAMA_BASE_URL", "Ollama:BaseUrl"),
        new("OLLAMA_API_KEY", "Ollama:ApiKey"),
        new("OLLAMA_READ_TIMEOUT_SECONDS", "Ollama:ReadSeconds"),
        new("OLLAMA_COMPLETION_TIMEOUT_SECONDS", "Ollama:CompletionSeconds"),

        new("OPENAI_ENABLED", "OpenAi:Enabled"),
        new("OPENAI_BASE_URL", "OpenAi:BaseUrl"),
        new("OPENAI_API_KEY", "OpenAi:ApiKey"),
        new("OPENAI_READ_TIMEOUT_SECONDS", "OpenAi:ReadSeconds"),
        new("OPENAI_COMPLETION_TIMEOUT_SECONDS", "OpenAi:CompletionSeconds"),

        // CLAUDE_API_* rather than ANTHROPIC_API_* deliberately: ANTHROPIC_API_KEY above already
        // means the credential a caller authenticates to this proxy with, and reusing it here for
        // the credential this proxy authenticates to real Anthropic with would make one name bind
        // two unrelated settings depending on which one happened to be set.
        new("CLAUDE_API_ENABLED", "AnthropicApi:Enabled"),
        new("CLAUDE_API_BASE_URL", "AnthropicApi:BaseUrl"),
        new("CLAUDE_API_KEY", "AnthropicApi:ApiKey"),
        new("CLAUDE_API_VERSION", "AnthropicApi:ApiVersion"),
        new("CLAUDE_API_READ_TIMEOUT_SECONDS", "AnthropicApi:ReadSeconds"),
        new("CLAUDE_API_COMPLETION_TIMEOUT_SECONDS", "AnthropicApi:CompletionSeconds"),

        new("MOCK_QUOTA_PROBE", "Optimizations:MockQuotaProbe"),
        new("SKIP_TITLE_GENERATION", "Optimizations:SkipTitleGeneration"),
        new("SKIP_SUGGESTION_MODE", "Optimizations:SkipSuggestionMode"),
        new("DETECT_COMMAND_PREFIX", "Optimizations:DetectCommandPrefix"),
        new("MOCK_FILE_PATH_EXTRACTION", "Optimizations:MockFilePathExtraction"),
    ];

    /// <summary>The configuration sources this proxy layers over its settings file.</summary>
    /// <param name="builder">The builder the sources are added to.</param>
    extension(IConfigurationBuilder builder)
    {
        /// <summary>Adds both environment-variable styles to a configuration builder.</summary>
        /// <returns>The same builder, so calls can be chained.</returns>
        public IConfigurationBuilder AddClaudeNimEnvironment()
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.AddInMemoryCollection(ReadFlatNames());
            _ = builder.AddEnvironmentVariables();
            return builder.AddEnvironmentVariables(Prefix);
        }
    }

    /// <summary>Reads every flat environment name that is set.</summary>
    /// <returns>The configuration entries the set names produce, the later alias of a group winning.</returns>
    /// <remarks>
    /// <see cref="Microsoft.Extensions.Configuration.Memory.MemoryConfigurationProvider"/> rejects a
    /// duplicate key outright, so a deployment that sets both aliases of one group (for example
    /// <c>NVIDIA_API_KEY</c> and <c>NVIDIA_NIM_API_KEY</c> together) needs the later entry to
    /// overwrite the earlier one here rather than being handed to it as two entries for one key.
    /// </remarks>
    private static List<KeyValuePair<string, string?>> ReadFlatNames()
    {
        var byKey = new Dictionary<string, string?>(FlatNames.Length, StringComparer.Ordinal);

        foreach (var (variable, key) in FlatNames)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                byKey[key] = value;
            }
        }

        return [.. byKey];
    }
}
