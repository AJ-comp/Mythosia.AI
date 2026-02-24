using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// A pre-built, shareable RAG index. Build once, share across multiple AIService instances.
    /// </summary>
    public class RagStore
    {
        /// <summary>
        /// The underlying RAG pipeline used for query processing.
        /// </summary>
        internal IRagPipeline Pipeline { get; }

        /// <summary>
        /// The vector store containing the indexed documents.
        /// </summary>
        internal IVectorStore VectorStore { get; }

        internal RagStore(IRagPipeline pipeline, IVectorStore vectorStore)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            VectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        }

        /// <summary>
        /// Processes a query through the RAG pipeline: embed → search → build context.
        /// Returns the augmented prompt and references without calling an LLM.
        /// </summary>
        public Task<RagProcessedQuery> QueryAsync(string query, CancellationToken cancellationToken = default)
        {
            return Pipeline.ProcessAsync(query, cancellationToken);
        }

        /// <summary>
        /// Builds a RagStore by loading, splitting, embedding, and indexing all configured documents.
        /// The resulting store can be shared across multiple AIService instances.
        /// </summary>
        /// <example>
        /// <code>
        /// var ragStore = await RagStore.BuildAsync(config => config
        ///     .AddDocuments("./knowledge-base/")
        ///     .UseOpenAIEmbedding(apiKey)
        /// );
        /// 
        /// var claude = new ClaudeService(key, http).WithRag(ragStore);
        /// var gpt = new ChatGptService(key, http).WithRag(ragStore);
        /// </code>
        /// </example>
        public static async Task<RagStore> BuildAsync(
            Action<RagBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            var builder = new RagBuilder();
            configure(builder);
            return await builder.BuildAsync(cancellationToken);
        }
    }
}
