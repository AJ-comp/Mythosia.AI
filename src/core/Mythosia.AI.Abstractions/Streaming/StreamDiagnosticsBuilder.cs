using System;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Fluent configurator for service-level streaming diagnostics. Each <c>On*</c>
    /// method registers an independent callback — only what you call gets wired up.
    /// Used by <c>service.WithStreamDiagnostics(d =&gt; d.OnRawLine(...).OnComplete(...))</c>.
    /// New diagnostic hooks can be added here in future versions without breaking
    /// existing callers, since adding methods to a class is purely additive.
    /// </summary>
    public class StreamDiagnosticsBuilder
    {
        internal Action<string>? RawLineCallback { get; private set; }
        internal Action<StreamDiagnostics>? CompleteCallback { get; private set; }

        /// <summary>
        /// Fires for every raw SSE line received from the response, before any
        /// provider-specific parsing. Wire to a Debug-level logger to see exactly
        /// what the server sent — useful when a self-hosted backend produces
        /// non-standard or truncated SSE lines.
        /// </summary>
        public StreamDiagnosticsBuilder OnRawLine(Action<string> callback)
        {
            RawLineCallback = callback ?? throw new ArgumentNullException(nameof(callback));
            return this;
        }

        /// <summary>
        /// Fires exactly once when the SSE reader exits — both on normal completion
        /// and on failure. Receives a <see cref="StreamDiagnostics"/> snapshot with
        /// lines read, characters accumulated, elapsed time, and the last raw line.
        /// Wire to telemetry to detect "stream died after N chunks" patterns.
        /// </summary>
        public StreamDiagnosticsBuilder OnComplete(Action<StreamDiagnostics> callback)
        {
            CompleteCallback = callback ?? throw new ArgumentNullException(nameof(callback));
            return this;
        }
    }
}
