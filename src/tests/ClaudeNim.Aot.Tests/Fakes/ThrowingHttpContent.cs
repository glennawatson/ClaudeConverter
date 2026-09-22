// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Net.Sockets;

namespace ClaudeNim.Aot.Tests.Fakes;

/// <summary>An <see cref="HttpContent"/> that fails however it is read, simulating a dropped upstream connection.</summary>
internal sealed class ThrowingHttpContent : HttpContent
{
    /// <inheritdoc/>
    protected override Task<Stream> CreateContentReadStreamAsync() => throw BuildFailure();

    /// <inheritdoc/>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw BuildFailure();

    /// <inheritdoc/>
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    /// <summary>Builds the exception thrown from every read attempt.</summary>
    /// <returns>The exception to throw.</returns>
    private static IOException BuildFailure() =>
        new("Simulated transport failure.", new SocketException((int)SocketError.ConnectionReset));
}
