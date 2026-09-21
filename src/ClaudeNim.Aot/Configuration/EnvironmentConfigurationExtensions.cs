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
    private static readonly (string Variable, string Key)[] FlatNames =
    [
        ("NVIDIA_API_KEY", "NvidiaNim:ApiKey"),
        ("NVIDIA_NIM_API_KEY", "NvidiaNim:ApiKey"),
        ("NVIDIA_BASE_URL", "NvidiaNim:BaseUrl"),
        ("NIM_BASE_URL", "NvidiaNim:BaseUrl"),
        ("NIM_PROXY", "NvidiaNim:Proxy"),
        ("MAX_TOKENS", "NvidiaNim:MaxTokens"),
        ("MIN_TOKENS", "NvidiaNim:MinTokens"),
        ("TEMPERATURE", "NvidiaNim:Temperature"),
        ("TOP_P", "NvidiaNim:TopP"),
        ("TOP_K", "NvidiaNim:TopK"),
        ("MIN_P", "NvidiaNim:MinP"),
        ("REPETITION_PENALTY", "NvidiaNim:RepetitionPenalty"),
        ("PRESENCE_PENALTY", "NvidiaNim:PresencePenalty"),
        ("FREQUENCY_PENALTY", "NvidiaNim:FrequencyPenalty"),
        ("SEED", "NvidiaNim:Seed"),
        ("STOP", "NvidiaNim:Stop"),
        ("PARALLEL_TOOL_CALLS", "NvidiaNim:ParallelToolCalls"),
        ("IGNORE_EOS", "NvidiaNim:IgnoreEos"),

        ("ANTHROPIC_AUTH_TOKEN", "Authentication:AuthToken"),
        ("ANTHROPIC_API_KEY", "Authentication:AuthToken"),
        ("PROXY_AUTH_TOKEN", "Authentication:AuthToken"),

        ("DEFAULT_MODEL", "ModelRouting:Default"),
        ("BIG_MODEL", "ModelRouting:Opus"),
        ("OPUS_MODEL", "ModelRouting:Opus"),
        ("MIDDLE_MODEL", "ModelRouting:Sonnet"),
        ("SONNET_MODEL", "ModelRouting:Sonnet"),
        ("SMALL_MODEL", "ModelRouting:Haiku"),
        ("HAIKU_MODEL", "ModelRouting:Haiku"),
        ("ENABLE_THINKING", "ModelRouting:EnableThinking"),
        ("ENABLE_OPUS_THINKING", "ModelRouting:EnableOpusThinking"),
        ("ENABLE_SONNET_THINKING", "ModelRouting:EnableSonnetThinking"),
        ("ENABLE_HAIKU_THINKING", "ModelRouting:EnableHaikuThinking"),

        ("MODEL_CACHE_MINUTES", "ModelCatalog:CacheMinutes"),
        ("DEFAULT_CONTEXT_WINDOW", "ModelCatalog:DefaultContextWindow"),
        ("DEFAULT_MAX_OUTPUT_TOKENS", "ModelCatalog:DefaultMaxOutputTokens"),
        ("ADVERTISE_CLAUDE_ALIASES", "ModelCatalog:AdvertiseClaudeAliases"),

        ("REQUESTS_PER_WINDOW", "RateLimits:RequestsPerWindow"),
        ("RATE_LIMIT_WINDOW_SECONDS", "RateLimits:WindowSeconds"),
        ("MAX_CONCURRENCY", "RateLimits:MaxConcurrency"),

        ("READ_TIMEOUT_SECONDS", "Timeouts:ReadSeconds"),
        ("CONNECT_TIMEOUT_SECONDS", "Timeouts:ConnectSeconds"),

        ("MOCK_QUOTA_PROBE", "Optimizations:MockQuotaProbe"),
        ("SKIP_TITLE_GENERATION", "Optimizations:SkipTitleGeneration"),
        ("SKIP_SUGGESTION_MODE", "Optimizations:SkipSuggestionMode"),
        ("DETECT_COMMAND_PREFIX", "Optimizations:DetectCommandPrefix"),
        ("MOCK_FILE_PATH_EXTRACTION", "Optimizations:MockFilePathExtraction"),
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
    /// <returns>The configuration entries the set names produce.</returns>
    private static List<KeyValuePair<string, string?>> ReadFlatNames()
    {
        var entries = new List<KeyValuePair<string, string?>>(FlatNames.Length);

        foreach (var (variable, key) in FlatNames)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                entries.Add(new(key, value));
            }
        }

        return entries;
    }
}
