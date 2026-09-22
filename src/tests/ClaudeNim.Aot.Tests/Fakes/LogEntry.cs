// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Microsoft.Extensions.Logging;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>One message a capturing logger recorded.</summary>
/// <param name="Level">The level it was written at.</param>
/// <param name="EventId">The identifier of the message.</param>
/// <param name="Message">The formatted text.</param>
/// <remarks>
/// The level is carried because the text alone cannot say whether a message would survive the
/// level an operator actually runs at, which for the failure records is the whole point of
/// writing them.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("LogEntry: {Level} {EventId}")]
internal readonly record struct LogEntry(LogLevel Level, int EventId, string Message);
