namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>Perplexity embedding model identifiers. Standard and contextualized models use different vector spaces.</summary>
    public static class PerplexityEmbeddingModels
    {
        public const string Standard0_6B = "pplx-embed-v1-0.6b";
        public const string Standard4B = "pplx-embed-v1-4b";
        public const string Context0_6B = "pplx-embed-context-v1-0.6b";
        public const string Context4B = "pplx-embed-context-v1-4b";
    }
}
