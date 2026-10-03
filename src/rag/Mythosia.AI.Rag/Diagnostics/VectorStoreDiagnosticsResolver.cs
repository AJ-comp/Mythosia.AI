using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Diagnostics
{
    internal static class VectorStoreDiagnosticsResolver
    {
#pragma warning disable CS0618 // Preserve the dispatch contract of existing custom stores.
        internal static IVectorStoreDiagnostics? Resolve(IVectorStore store)
        {
            // A public class method can take precedence over a default interface bridge.
            // Prefer the original interface slots when a legacy store exposes both.
            return store is IRagDiagnosticsStore legacy
                ? new LegacyAdapter(legacy)
                : store as IVectorStoreDiagnostics;
        }

        private sealed class LegacyAdapter : IVectorStoreDiagnostics
        {
            private readonly IRagDiagnosticsStore _store;

            internal LegacyAdapter(IRagDiagnosticsStore store) => _store = store;

            public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(CancellationToken cancellationToken = default)
                => _store.ListAllRecordsAsync(cancellationToken);

            public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
                float[] queryVector, CancellationToken cancellationToken = default)
                => _store.ScoredListAsync(queryVector, cancellationToken);
        }
#pragma warning restore CS0618
    }
}
