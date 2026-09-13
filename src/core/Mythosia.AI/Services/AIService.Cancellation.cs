using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        private readonly AsyncLocal<CancellationToken> _requestCancellation = new AsyncLocal<CancellationToken>();

        /// <summary>The caller's cancellation token for the current logical request, including summaries and retries.</summary>
        /// <remarks>Custom providers should pass this token to their transport and tools, or use
        /// CreateRequestTimeoutCts to combine it with the request policy timeout. Cancellation does not
        /// certify that the remote server stopped generating or charging for the request.</remarks>
        protected CancellationToken RequestCancellationToken => _requestCancellation.Value;

        private IDisposable BeginRequestCancellationScope(CancellationToken cancellationToken = default)
        {
            var previous = _requestCancellation.Value;
            previous.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            CancellationTokenSource? linked = null;
            if (previous.CanBeCanceled && cancellationToken.CanBeCanceled && previous != cancellationToken)
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(previous, cancellationToken);
                _requestCancellation.Value = linked.Token;
            }
            else if (cancellationToken.CanBeCanceled)
            {
                _requestCancellation.Value = cancellationToken;
            }

            return new FeatureScope(() =>
            {
                _requestCancellation.Value = previous;
                linked?.Dispose();
            });
        }

        /// <summary>Reads and decodes a response body with cancellation on .NET Standard 2.1.</summary>
        /// <remarks>Use with ResponseHeadersRead. Disposing the response interrupts a pending network read;
        /// the token is also passed to stream copying. Charset and BOM handling match HttpContent.</remarks>
        protected static async Task<string> ReadCompletionResponseBodyAsync(
            HttpResponseMessage response, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellation = cancellationToken.Register(response.Dispose);
            try
            {
                using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                using var content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentType = response.Content.Headers.ContentType;
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
        }
    }
}
