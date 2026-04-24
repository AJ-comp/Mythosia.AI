using System;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Captures observability data about a single SSE streaming round.
    /// Populated by the SSE reader and surfaced via <see cref="StreamOptions.DiagnosticsCallback"/>
    /// or attached to <see cref="StreamReadException"/> when a read fails. Lets callers tell
    /// "stream silently ended" apart from "transport error after N chunks".
    /// </summary>
    public class StreamDiagnostics
    {
        /// <summary>
        /// Total raw lines read from the SSE stream (including comments, blank lines,
        /// and "data:" lines). Reaches the line that triggered a failure, if any.
        /// </summary>
        public int LinesRead { get; set; }

        /// <summary>
        /// Number of lines the provider's chunk parser accepted as content.
        /// </summary>
        public int DataLinesProcessed { get; set; }

        /// <summary>
        /// Number of lines that hit a parse failure (typically swallowed by the
        /// provider's per-chunk catch block).
        /// </summary>
        public int ParseFailures { get; set; }

        /// <summary>
        /// Total characters appended to the assistant text buffer during this round.
        /// </summary>
        public long AccumulatedTextLength { get; set; }

        /// <summary>
        /// The most recent raw SSE line received. Useful when the stream dies mid-line —
        /// reveals whether the last line was truncated or non-standard.
        /// </summary>
        public string? LastRawLine { get; set; }

        /// <summary>
        /// Wall-clock time spent reading the stream.
        /// </summary>
        public TimeSpan Elapsed { get; set; }

        public override string ToString()
        {
            return $"lines={LinesRead}, data={DataLinesProcessed}, parseFail={ParseFailures}, " +
                   $"chars={AccumulatedTextLength}, elapsed={Elapsed.TotalMilliseconds:F0}ms, " +
                   $"lastLine={(LastRawLine != null ? $"\"{Truncate(LastRawLine, 120)}\"" : "(none)")}";
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
