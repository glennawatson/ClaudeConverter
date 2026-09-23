// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Codex;

/// <summary>The <c>type</c> discriminator <see cref="ResponseContentItem"/> carries.</summary>
public static class ResponseContentTypes
{
    /// <summary>Plain text supplied as input.</summary>
    internal const string InputText = "input_text";

    /// <summary>An image supplied as input.</summary>
    internal const string InputImage = "input_image";

    /// <summary>Plain text produced as output.</summary>
    internal const string OutputText = "output_text";

    /// <summary>A declined answer.</summary>
    internal const string Refusal = "refusal";

    /// <summary>A reasoning trace's own content, when the model discloses more than a summary.</summary>
    internal const string ReasoningText = "reasoning_text";

    /// <summary>One part of a reasoning item's summary.</summary>
    internal const string SummaryText = "summary_text";
}
