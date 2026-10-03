using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        // The SSE reader and error-body reader await body DisposeAsync. HttpResponseMessage
        // still owns the original content and must be disposed, but its synchronous cleanup
        // may call Dispose on that same async-only stream. That secondary failure must not
        // turn success or caller cancellation into a fault, or replace the original read error.
        private sealed class ClaudeStreamingResponseCleanup : IDisposable
        {
            private readonly HttpResponseMessage _response;

            internal ClaudeStreamingResponseCleanup(HttpResponseMessage response)
            {
                _response = response;
            }

            public void Dispose()
            {
                try { _response.Dispose(); }
                catch { /* Response/content cleanup must not replace the operation's outcome. */ }
            }
        }

        private static async Task<string> ReadClaudeStreamingErrorBodyAsync(
            HttpResponseMessage response, CancellationToken cancellationToken)
        {
            Task<Stream>? sourceTask = null;
            Stream? source = null;
            try
            {
                var contentType = response.Content.Headers.ContentType;
                // Acquire before checking an already-canceled token: once headers have arrived,
                // an immediately available body must still receive awaited asynchronous cleanup.
                sourceTask = response.Content.ReadAsStreamAsync();
                source = await AwaitClaudeBodyReadAsync(sourceTask, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                using var buffer = new MemoryStream();
                var bytes = new byte[81920];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = source.ReadAsync(bytes, 0, bytes.Length, cancellationToken);
                    var count = await AwaitClaudeBodyReadAsync(read, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count == 0) break;
                    // Only this method writes to the destination. A noncooperative pending read
                    // can finish after cancellation without accessing the disposed MemoryStream.
                    buffer.Write(bytes, 0, count);
                }

                using var content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentType = contentType;
                var result = await content.ReadAsStringAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested &&
                (exception is OperationCanceledException || exception is IOException ||
                 exception is ObjectDisposedException || exception is HttpRequestException))
            {
                throw new OperationCanceledException("The response read was canceled.", exception, cancellationToken);
            }
            finally
            {
                if (source != null)
                    await DisposeClaudeBodyAsync(source).ConfigureAwait(false);
                else if (sourceTask != null)
                {
                    if (sourceTask.IsCompleted)
                        await DisposeLateClaudeBodyAsync(sourceTask).ConfigureAwait(false);
                    else
                        // ReadAsStreamAsync has no token in netstandard2.1. Do not keep the
                        // canceled request alive indefinitely; clean up its body if it arrives.
                        _ = DisposeLateClaudeBodyAsync(sourceTask);
                }
            }
        }

        private static async Task<T> AwaitClaudeBodyReadAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            if (operation.IsCompleted || !cancellationToken.CanBeCanceled)
                return await operation.ConfigureAwait(false);

            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state).TrySetResult(true), canceled))
            {
                await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false);
            }
            if (operation.IsCompleted)
                return await operation.ConfigureAwait(false);

            // Cancellation callbacks only signal; they never synchronously dispose a transport.
            // Observe the losing read because asynchronous body cleanup can make it fault later.
            _ = operation.ContinueWith(
                task => { if (task.IsFaulted) _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new OperationCanceledException(cancellationToken);
        }

        private static async Task DisposeLateClaudeBodyAsync(Task<Stream> sourceTask)
        {
            try
            {
                var source = await sourceTask.ConfigureAwait(false);
                await DisposeClaudeBodyAsync(source).ConfigureAwait(false);
            }
            catch { /* Observe a late acquisition failure after the request has been canceled. */ }
        }

        private static async Task DisposeClaudeBodyAsync(Stream source)
        {
            try { await source.DisposeAsync().ConfigureAwait(false); }
            catch { /* Preserve the read outcome, just as the shared SSE reader does. */ }
        }
    }
}
