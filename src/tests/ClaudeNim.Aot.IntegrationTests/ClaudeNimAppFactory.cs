// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaudeNim.Aot.IntegrationTests;

/// <summary>Hosts the real proxy in-process, talking to whatever NVIDIA credential the environment supplies.</summary>
/// <remarks>
/// Nothing here is faked: the app is built the same way the published binary is, configuration is
/// read from the same environment variables a real deployment would use, and its upstream calls
/// reach the live NVIDIA endpoint. Only the inbound transport is swapped for an in-memory one,
/// which is what makes this fast enough to run as a build step rather than something that needs a
/// separately started server. <see cref="WebHostMarker"/> only locates the assembly to host; it
/// carries no behaviour of its own.
/// </remarks>
public sealed class ClaudeNimAppFactory : WebApplicationFactory<WebHostMarker>;
