using Mythosia.AI.Rag.Diagnostics;
using System;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Extension methods for convenient access to RAG diagnostics.
    /// </summary>
    public static class RagDiagnosticsExtensions
    {
        /// <summary>
        /// Creates a diagnostic session for this RagStore.
        /// Use to debug search quality issues, run health checks, or compare splitter configurations.
        /// </summary>
        /// <example>
        /// <code>
        /// var report = await ragStore.Diagnose()
        ///     .WhyMissingAsync("최근 연봉이 얼마?", "$1");
        /// Console.WriteLine(report.ToReport());
        /// </code>
        /// </example>
        public static RagDiagnosticSession Diagnose(this RagStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var pipeline = store.Pipeline as RagPipeline
                ?? throw new InvalidOperationException("Diagnose() requires the underlying pipeline to be a RagPipeline.");
            return new RagDiagnosticSession(new RagDiagnostics(store), pipeline);
        }

        /// <summary>
        /// Creates a diagnostic session for this RagPipeline.
        /// </summary>
        public static RagDiagnosticSession Diagnose(this RagPipeline pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            return new RagDiagnosticSession(new RagDiagnostics(pipeline), pipeline);
        }
    }
}
