// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Codex;

/// <summary>The <c>type</c> and role discriminators <see cref="ResponseInputItem"/> carries.</summary>
public static class ResponseItemTypes
{
    /// <summary>A turn of conversation content.</summary>
    internal const string Message = "message";

    /// <summary>A tool invocation the model asked for.</summary>
    internal const string FunctionCall = "function_call";

    /// <summary>The result of a tool invocation.</summary>
    internal const string FunctionCallOutput = "function_call_output";

    /// <summary>A reasoning trace, with or without a summary.</summary>
    internal const string Reasoning = "reasoning";

    /// <summary>The <c>user</c> role.</summary>
    internal const string UserRole = "user";

    /// <summary>The <c>assistant</c> role.</summary>
    internal const string AssistantRole = "assistant";

    /// <summary>The <c>system</c> role.</summary>
    internal const string SystemRole = "system";

    /// <summary>The <c>developer</c> role, Codex's preferred name for a system-level instruction.</summary>
    internal const string DeveloperRole = "developer";
}
