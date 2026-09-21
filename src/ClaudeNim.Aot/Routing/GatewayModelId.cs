// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Routing;

/// <summary>
/// Encodes a NIM model identifier so it survives a round trip through a Claude client's model
/// picker, and decodes it again on the way back.
/// </summary>
/// <remarks>
/// <para>
/// A client that lists models and then sends one back expects to get that model. Advertising a
/// bare NIM identifier does not achieve that, because the client classifies a model by its name
/// and will route an unrecognised one to its own default tier.
/// </para>
/// <para>
/// Two encodings exist per model. The plain one carries the identifier through unchanged. The
/// no-thinking one exists because Claude clients treat any identifier containing
/// <c>claude-3-</c> as not supporting extended thinking, which is the only way to ask for a
/// reasoning-capable model with its reasoning turned off.
/// </para>
/// </remarks>
public static class GatewayModelId
{
    /// <summary>The prefix for encoded gateway model identifiers with thinking enabled.</summary>
    private const string Prefix = "anthropic/nvidia_nim/";

    /// <summary>The prefix for encoded gateway model identifiers with thinking suppressed.</summary>
    private const string NoThinkingPrefix = "claude-3-nim-no-think/nvidia_nim/";

    /// <summary>Encodes a NIM model identifier with reasoning left enabled.</summary>
    /// <param name="nimModel">The NIM model identifier.</param>
    /// <returns>The gateway identifier.</returns>
    public static string Encode(string nimModel) => Prefix + nimModel;

    /// <summary>Encodes a NIM model identifier with reasoning suppressed.</summary>
    /// <param name="nimModel">The NIM model identifier.</param>
    /// <returns>The gateway identifier.</returns>
    public static string EncodeWithoutThinking(string nimModel) => NoThinkingPrefix + nimModel;

    /// <summary>Recovers the NIM model identifier from a gateway identifier.</summary>
    /// <param name="modelId">The identifier the client sent.</param>
    /// <param name="nimModel">The decoded NIM model identifier.</param>
    /// <param name="thinkingEnabled">Whether the encoding asked for reasoning.</param>
    /// <returns><see langword="true"/> when the identifier was a gateway one.</returns>
    public static bool TryDecode(string modelId, out string nimModel, out bool thinkingEnabled)
    {
        if (modelId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            nimModel = modelId[Prefix.Length..].TrimEnd('/');
            thinkingEnabled = true;
            return nimModel.Length > 0;
        }

        if (modelId.StartsWith(NoThinkingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            nimModel = modelId[NoThinkingPrefix.Length..].TrimEnd('/');
            thinkingEnabled = false;
            return nimModel.Length > 0;
        }

        nimModel = string.Empty;
        thinkingEnabled = false;
        return false;
    }
}
