using Mythosia.AI.Rag;

namespace Mythosia.AI.Samples.ChatUi;

// Sample-level request policy. Preserve the optional retrieval interface so contextual
// providers continue to receive a whole document, while legacy providers retain batching.
internal class TimedEmbeddingProvider(IEmbeddingProvider inner, TimeSpan timeout) : IEmbeddingProvider
{
    public int Dimensions => inner.Dimensions;
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => ExecuteAsync(ct => inner.GetEmbeddingAsync(text, ct), cancellationToken);
    public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        => ExecuteAsync(ct => inner.GetEmbeddingsAsync(texts, ct), cancellationToken);

    protected async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(timeout);
        try { return await action(operation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { throw new TimeoutException("The embedding operation exceeded the configured timeout."); }
    }
}

internal sealed class TimedRetrievalEmbeddingProvider(IRetrievalEmbeddingProvider inner, TimeSpan timeout)
    : TimedEmbeddingProvider(inner, timeout), IRetrievalEmbeddingProvider
{
    public Task<IReadOnlyList<float[]>> GetDocumentEmbeddingsAsync(EmbeddingDocument document, CancellationToken cancellationToken = default)
        => ExecuteAsync(ct => inner.GetDocumentEmbeddingsAsync(document, ct), cancellationToken);
    public Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
        => ExecuteAsync(ct => inner.GetQueryEmbeddingAsync(query, ct), cancellationToken);
}
