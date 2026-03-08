using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Samples.ChatUi
{
    internal sealed class ChatUiRagEndpointState
    {
        public IVectorStore VectorStore { get; set; } = new InMemoryVectorStore();
        public string VectorStoreProvider { get; set; } = "inmemory";

        public string PgConnectionString { get; set; } = "";
        public string PgTableName { get; set; } = "vectors";
        public string PgSchemaName { get; set; } = "public";
        public int PgDimension { get; set; } = 1536;
        public bool PgEnsureSchema { get; set; } = true;

        public string QdrantHost { get; set; } = "localhost";
        public int QdrantPort { get; set; } = 6334;
        public string? QdrantApiKey { get; set; }
        public bool QdrantUseTls { get; set; }
        public int QdrantDimension { get; set; } = 1536;
        public string QdrantCollectionName { get; set; } = "default";

        public string PineconeIndexHost { get; set; } = "";
        public string? PineconeApiKey { get; set; }
        public string PineconeNamespace { get; set; } = "default";

        public string RagEmbeddingOpenAiKey { get; set; } = "";
        public string? RewriterApiKey { get; set; }
    }
}
