// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Nvidia;

/// <summary>The NVIDIA-hosted models the proxy is known to work with, and the rules for excluding the ones it cannot serve.</summary>
/// <remarks>
/// <para>
/// NVIDIA's free endpoints host far more than chat models: embedding, reranking, safety
/// classification, speech, video and autonomous-driving models all appear in the same listing.
/// None of them can answer a Messages API call, so advertising them gives a coding client a
/// model picker full of entries that fail on selection.
/// </para>
/// <para>
/// Only <see cref="Profiles"/> carries stated sizing, and only where NVIDIA documents it. Every
/// other model is listed with the configured defaults instead of a guess.
/// </para>
/// </remarks>
public static class NimModelCatalogDefaults
{
    /// <summary>The context window NVIDIA documents for the Nemotron 3 hybrid Mamba-Transformer models.</summary>
    internal const int NemotronThreeContextWindow = 1_048_576;

    /// <summary>The default maximum model length NVIDIA ships for the Nemotron 3 models.</summary>
    internal const int NemotronThreeMaxOutputTokens = 262_144;

    /// <summary>The model the proxy routes to when configuration names none.</summary>
    internal const string RecommendedModel = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>The profiles of NVIDIA NIM models the proxy has stated knowledge of.</summary>
    private static readonly NimModelProfile[] KnownProfiles =
    [
        new(
            "nvidia/nemotron-3-ultra-550b-a55b",
            "Nemotron 3 Ultra 550B A55B",
            SupportsTools: true,
            SupportsVision: false,
            SupportsThinking: true,
            NemotronThreeContextWindow,
            NemotronThreeMaxOutputTokens),
        new(
            "nvidia/nemotron-3-super-120b-a12b",
            "Nemotron 3 Super 120B A12B",
            SupportsTools: true,
            SupportsVision: false,
            SupportsThinking: true,
            NemotronThreeContextWindow,
            NemotronThreeMaxOutputTokens),
        new("nvidia/nemotron-3.5-lightning-30b-a3b", "Nemotron 3.5 Lightning 30B A3B", true, false, true),
        new(
            "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning",
            "Nemotron 3 Nano Omni 30B A3B Reasoning",
            SupportsTools: true,
            SupportsVision: true,
            SupportsThinking: true),
        new("z-ai/glm-5.3", "GLM 5.3", true, false, true),
        new("z-ai/glm-5.3-flash", "GLM 5.3 Flash", true, true, true),
        new("moonshotai/kimi-k3", "Kimi K3", true, true, true),
        new("deepseek-ai/deepseek-v4.1-flash", "DeepSeek V4.1 Flash", true, false, true),
        new("meta/muse-glimmer-30b", "Muse Glimmer 30B", true, true, true),
        new("poolside/laguna-xs-2.1", "Laguna XS 2.1", true, false, true),
        new("google/gemma-4-31b-it", "Gemma 4 31B IT", true, false, false),
        new("openai/gpt-oss-20b", "GPT-OSS 20B", true, false, true),
        new("mistralai/mistral-nemotron", "Mistral Nemotron", true, false, false),
        new("meta/llama-3.2-11b-vision-instruct", "Llama 3.2 11B Vision Instruct", false, true, false),
    ];

    // Substrings that identify a model serving something other than chat completions. Matched
    // against the lower-cased identifier.
    /// <summary>The substrings that identify non-chat models to exclude from the listing.</summary>
    private static readonly string[] NonChatMarkers =
    [
        "embed", "rerank", "retriever", "reward", "bge-", "gliner",
        "guard", "safety", "jailbreak", "content-safety", "topic-control",
        "tts", "asr", "voicechat", "magpie", "studio-voice", "noise",
        "cosmos", "video", "detector", "streampetr", "bevformer", "sparsedrive",
        "translate", "paligemma", "parse", "ocr", "clip", "calibration", "kumo",
    ];

    /// <summary>Gets the models the proxy has stated knowledge of.</summary>
    /// <returns>The known profiles.</returns>
    public static ReadOnlySpan<NimModelProfile> Profiles => KnownProfiles;

    /// <summary>Finds the stated profile for a model identifier.</summary>
    /// <param name="id">The NIM model identifier.</param>
    /// <returns>The profile, or <see langword="null"/> when the model is not a known one.</returns>
    public static NimModelProfile? FindProfile(string id)
    {
        for (var i = 0; i < KnownProfiles.Length; i++)
        {
            if (string.Equals(KnownProfiles[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return KnownProfiles[i];
            }
        }

        return null;
    }

    /// <summary>Decides whether a model accepts image input.</summary>
    /// <param name="id">The NIM model identifier.</param>
    /// <returns><see langword="true"/> when images may be forwarded to the model.</returns>
    /// <remarks>
    /// This is the same source the advertised listing sizes a model from, so what a client is told
    /// about image support and what the request builder actually sends cannot drift apart. A model
    /// the proxy has no stated knowledge of is treated as text-only, matching the listing.
    /// </remarks>
    public static bool SupportsVision(string id) => FindProfile(id)?.SupportsVision ?? false;

    /// <summary>Decides whether a model produces a reasoning trace at all.</summary>
    /// <param name="id">The NIM model identifier.</param>
    /// <returns><see langword="true"/> when the model may return reasoning.</returns>
    /// <remarks>
    /// This gates the streamed reader's handling of a chat template that opens the reasoning block
    /// itself, and so cannot be read from the caller's reasoning setting alone: the GLM 5.3
    /// template seeds the tag unconditionally and never reads <c>enable_thinking</c>, so a turn
    /// that asked for no reasoning still comes back with a trace and a closing tag in the content.
    /// A model the proxy has no stated knowledge of is treated as producing none, matching the
    /// listing.
    /// </remarks>
    public static bool SupportsThinking(string id) => FindProfile(id)?.SupportsThinking ?? false;

    /// <summary>Reads the output ceiling a model is documented to allow.</summary>
    /// <param name="id">The NIM model identifier.</param>
    /// <param name="fallback">The ceiling to assume for a model NVIDIA does not size.</param>
    /// <returns>The largest completion the model will produce.</returns>
    /// <remarks>
    /// This is the same source the advertised listing sizes a model from, so what a client is told
    /// it may ask for and what the request builder actually sends cannot drift apart. They did:
    /// the listing offered Nemotron 3's documented 262144 while every turn was quietly held to a
    /// single configured 4096, which a reasoning model spends on its trace before it writes a word
    /// of the answer. The turn then ends on length with no content, which a client reads as the
    /// model having nothing to say.
    /// </remarks>
    public static int MaxOutputTokens(string id, int fallback) =>
        FindProfile(id)?.MaxOutputTokens ?? fallback;

    /// <summary>Decides whether a model identifier names something that can answer a chat completion.</summary>
    /// <param name="id">The NIM model identifier.</param>
    /// <returns><see langword="true"/> when the model should be advertised.</returns>
    public static bool IsChatModel(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (FindProfile(id) is not null)
        {
            return true;
        }

        for (var i = 0; i < NonChatMarkers.Length; i++)
        {
            if (id.Contains(NonChatMarkers[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Derives a readable name from a model identifier that has no stated profile.</summary>
    /// <param name="id">The NIM model identifier.</param>
    /// <returns>The trailing segment of the identifier, or the identifier itself.</returns>
    public static string DeriveDisplayName(string id)
    {
        var slash = id.LastIndexOf('/');
        return slash >= 0 && slash < id.Length - 1 ? id[(slash + 1)..] : id;
    }
}
