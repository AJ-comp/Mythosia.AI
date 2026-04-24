using System;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Thrown when an SSE streaming read fails. Wraps the underlying exception
    /// (e.g. <see cref="System.IO.IOException"/>, <see cref="System.Net.Http.HttpRequestException"/>)
    /// and attaches a <see cref="StreamDiagnostics"/> snapshot taken at the moment of failure
    /// so callers can see how far the stream got before dying.
    /// </summary>
    public class StreamReadException : Exception
    {
        /// <summary>
        /// State of the SSE reader at the point of failure.
        /// </summary>
        public StreamDiagnostics Diagnostics { get; }

        public StreamReadException(string message, StreamDiagnostics diagnostics, Exception innerException)
            : base(message, innerException)
        {
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }
    }
}
