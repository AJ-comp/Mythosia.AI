using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        // Service-level diagnostic callbacks. Set via WithStreamDiagnostics(...).
        // Both nullable — only the ones the caller registered fire.
        internal Action<string>? StreamRawLineCallback { get; set; }
        internal Action<StreamDiagnostics>? StreamCompleteCallback { get; set; }

        /// <summary>
        /// Reads an SSE response body line-by-line with diagnostics, async stream
        /// disposal, and structured exception wrapping. Yields raw SSE lines without
        /// any provider-specific parsing — callers handle "data:", "[DONE]", JSON, etc.
        ///
        /// Behavior:
        ///  - Disposes the underlying response stream via <see cref="Stream.DisposeAsync"/>
        ///    in the iterator's finally block. Avoids the <see cref="NotSupportedException"/>
        ///    that some HttpContent transports throw on synchronous Dispose.
        ///  - Wraps any read-side exception in <see cref="StreamReadException"/> with a
        ///    <see cref="StreamDiagnostics"/> snapshot taken at the moment of failure.
        ///  - Invokes the service-level OnRawLine callback (set via WithStreamDiagnostics)
        ///    for every line. Callback exceptions are swallowed so a faulty logger
        ///    cannot break the stream.
        ///  - Invokes the service-level OnComplete callback exactly once on iterator exit,
        ///    regardless of how the iteration ended.
        ///  - Honors <paramref name="cancellationToken"/> while a read is pending as well as
        ///    between reads. A losing pending read is observed and the iterator's finally block
        ///    disposes the reader and response stream.
        ///
        /// The caller owns the <paramref name="diagnostics"/> object and may mutate
        /// fields like <see cref="StreamDiagnostics.DataLinesProcessed"/> or
        /// <see cref="StreamDiagnostics.AccumulatedTextLength"/> while consuming lines.
        /// </summary>
        protected internal async IAsyncEnumerable<string> ReadSseLinesAsync(
            HttpResponseMessage response,
            StreamDiagnostics diagnostics,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

            var rawLineCallback = StreamRawLineCallback;
            var completeCallback = StreamCompleteCallback;

            var sw = Stopwatch.StartNew();
            Stream? stream = null;
            StreamReader? reader = null;

            try
            {
                stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line = await ReadOneLineAsync(
                        reader,
                        diagnostics,
                        sw,
                        cancellationToken).ConfigureAwait(false);
                    if (line == null) break; // graceful end of stream

                    diagnostics.LinesRead++;
                    diagnostics.LastRawLine = line;

                    if (rawLineCallback != null)
                    {
                        try { rawLineCallback(line); }
                        catch { /* user callback must not break the stream */ }
                    }

                    yield return line;
                }
            }
            finally
            {
                diagnostics.Elapsed = sw.Elapsed;

                // Every disposal step is guarded so that:
                //  (a) a Dispose-time exception (NotSupportedException etc.) does not mask
                //      the real read-side exception that may already be propagating,
                //  (b) OnComplete is guaranteed to fire even if disposal fails.
                if (reader != null)
                {
                    try { reader.Dispose(); }
                    catch { /* swallow */ }
                }

                if (stream != null)
                {
                    try { await stream.DisposeAsync().ConfigureAwait(false); }
                    catch { /* swallow */ }
                }

                if (completeCallback != null)
                {
                    try { completeCallback(diagnostics); }
                    catch { /* user callback must not break finally */ }
                }
            }
        }

        private static async Task<string?> ReadOneLineAsync(
            StreamReader reader,
            StreamDiagnostics diagnostics,
            Stopwatch sw,
            CancellationToken cancellationToken)
        {
            try
            {
                var readTask = reader.ReadLineAsync();
                if (!cancellationToken.CanBeCanceled)
                    return await readTask.ConfigureAwait(false);

                var cancellationSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Task completedTask;
                using (cancellationToken.Register(
                    state => ((TaskCompletionSource<bool>)state).TrySetResult(true),
                    cancellationSignal))
                {
                    completedTask = await Task.WhenAny(
                        readTask,
                        cancellationSignal.Task).ConfigureAwait(false);
                }

                if (completedTask == readTask)
                    return await readTask.ConfigureAwait(false);

                // StreamReader's netstandard2.1 ReadLineAsync overload has no token. The
                // enclosing iterator will now dispose the reader/stream in finally; observe the
                // abandoned read so a disposal fault can never surface as an unobserved exception.
                ObservePendingRead(readTask);
                throw new OperationCanceledException(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Elapsed = sw.Elapsed;
                throw new StreamReadException(
                    $"SSE read failed after {diagnostics.LinesRead} line(s): {ex.GetType().FullName}: {ex.Message}",
                    diagnostics,
                    ex);
            }
        }

        private static void ObservePendingRead(Task<string> pendingRead)
        {
            _ = pendingRead.ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                        _ = task.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
