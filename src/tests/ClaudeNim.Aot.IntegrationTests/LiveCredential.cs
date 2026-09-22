// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.IntegrationTests;

/// <summary>Reads the NVIDIA credential these tests run against from the environment.</summary>
/// <remarks>
/// These tests spend real quota against the live NVIDIA endpoint, so they run only when a
/// credential is actually present rather than failing a machine that has none configured. The
/// same environment variable the proxy itself reads is used here, so exporting it once is enough
/// to make both the proxy and this suite work.
/// </remarks>
public static class LiveCredential
{
    /// <summary>The environment variable the credential is read from.</summary>
    private const string VariableName = "NVIDIA_API_KEY";

    /// <summary>Gets a value indicating whether a credential is available to test against.</summary>
    public static bool IsAvailable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName));
}
