// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Endpoints;
using Microsoft.Extensions.Primitives;

namespace ClaudeNim.Aot.Tests;

/// <summary>Covers reading the credential out of the headers a Claude client may present it in.</summary>
public sealed class CredentialHeadersTests
{
    /// <summary>The expected credential used across the fixtures.</summary>
    private const string ExpectedCredential = "secret";

    /// <summary>A bearer header yields the token after the prefix.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task BearerHeaderYieldsTheToken() =>
        await Assert.That(CredentialHeaders.Bearer(new("Bearer abc123"))).IsEqualTo("abc123");

    /// <summary>A header without the bearer prefix yields an empty string.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task NonBearerHeaderYieldsEmpty() =>
        await Assert.That(CredentialHeaders.Bearer(new("abc123"))).IsEqualTo(string.Empty);

    /// <summary>An absent header yields an empty string.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AbsentHeaderYieldsEmpty() =>
        await Assert.That(CredentialHeaders.Bearer(StringValues.Empty)).IsEqualTo(string.Empty);

    /// <summary>A presented credential matching the expected one reports a match.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task MatchingCredentialReportsAMatch() =>
        await Assert.That(CredentialHeaders.Matches(new(ExpectedCredential), ExpectedCredential)).IsTrue();

    /// <summary>A presented credential differing from the expected one reports no match.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task DifferingCredentialReportsNoMatch() =>
        await Assert.That(CredentialHeaders.Matches(new("wrong"), ExpectedCredential)).IsFalse();

    /// <summary>An absent header reports no match against any expected value.</summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Test]
    public async Task AbsentHeaderReportsNoMatch() =>
        await Assert.That(CredentialHeaders.Matches(StringValues.Empty, ExpectedCredential)).IsFalse();
}
