// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>An <see cref="ILogger{TCategoryName}"/> double that records every message it is given.</summary>
/// <typeparam name="TCategoryName">The category the logger is typed for.</typeparam>
internal sealed class CapturingLogger<TCategoryName> : ILogger<TCategoryName>
{
    /// <summary>Gets the messages written so far, in call order.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Messages.Add(formatter(state, exception));
    }
}
