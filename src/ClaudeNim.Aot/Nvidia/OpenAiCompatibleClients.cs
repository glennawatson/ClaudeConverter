// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;

namespace ClaudeNim.Aot.Nvidia;

/// <summary>Mechanics shared by the OpenAI-compatible client implementations.</summary>
public static class OpenAiCompatibleClients
{
    /// <summary>Builds the response a disabled provider answers a call with, in place of attempting one.</summary>
    /// <param name="provider">The provider's own name, for the body a caller may log or forward.</param>
    /// <returns>A synthetic <see cref="HttpStatusCode.ServiceUnavailable"/> response.</returns>
    public static HttpResponseMessage Disabled(string provider) =>
        new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent($"{{\"error\":{{\"message\":\"{provider} is not enabled on this proxy.\",\"code\":503}}}}") };
}
