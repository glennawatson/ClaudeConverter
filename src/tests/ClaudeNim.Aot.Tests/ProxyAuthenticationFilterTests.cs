// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using ClaudeNim.Aot.Endpoints;
using Microsoft.AspNetCore.Http;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers rejecting a request that does not present the configured shared secret.</summary>
public sealed class ProxyAuthenticationFilterTests
{
    /// <summary>The shared secret the configured fixtures expect.</summary>
    private const string Secret = "top-secret";

    /// <summary>An empty configured secret disables the check outright.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task EmptySecretDisablesTheCheck()
    {
        var filter = new ProxyAuthenticationFilter(new(string.Empty));

        var result = await filter.InvokeAsync(Invocation(), static _ => ValueTask.FromResult<object?>("next"));

        await Assert.That(result).IsEqualTo("next");
    }

    /// <summary>The correct <c>x-api-key</c> header is authorized.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CorrectApiKeyHeaderIsAuthorized()
    {
        var filter = new ProxyAuthenticationFilter(new(Secret));
        var context = Invocation();
        context.HttpContext.Request.Headers[CredentialHeaders.ApiKey] = Secret;

        var result = await filter.InvokeAsync(context, static _ => ValueTask.FromResult<object?>("next"));

        await Assert.That(result).IsEqualTo("next");
    }

    /// <summary>The correct bearer token is authorized.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task CorrectBearerTokenIsAuthorized()
    {
        var filter = new ProxyAuthenticationFilter(new(Secret));
        var context = Invocation();
        context.HttpContext.Request.Headers.Authorization = $"Bearer {Secret}";

        var result = await filter.InvokeAsync(context, static _ => ValueTask.FromResult<object?>("next"));

        await Assert.That(result).IsEqualTo("next");
    }

    /// <summary>A request with no credential is rejected.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task MissingCredentialIsRejected()
    {
        var filter = new ProxyAuthenticationFilter(new(Secret));

        var result = await filter.InvokeAsync(Invocation(), static _ => ValueTask.FromResult<object?>("next"));

        await Assert.That(result).IsNotEqualTo("next");
    }

    /// <summary>A wrong credential is rejected.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task WrongCredentialIsRejected()
    {
        var filter = new ProxyAuthenticationFilter(new(Secret));
        var context = Invocation();
        context.HttpContext.Request.Headers[CredentialHeaders.ApiKey] = "wrong";

        var result = await filter.InvokeAsync(context, static _ => ValueTask.FromResult<object?>("next"));

        await Assert.That(result).IsNotEqualTo("next");
    }

    /// <summary>Builds an invocation context over a fresh HTTP context.</summary>
    /// <returns>The invocation context.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static EndpointFilterInvocationContext Invocation() => EndpointFilterInvocationContext.Create(new DefaultHttpContext());
}
