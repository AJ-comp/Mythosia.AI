namespace Mythosia.AI.Samples.ChatUi
{
    internal record ConfigureRequest(string? ApiKey, string? Model, string? SystemMessage, string? BaseUrl, string? Platform, string? ModelIdOverride);
    internal record ChatRequest(string? Message, RagPipelineSettingsRequest? RagSettings, VectorStoreConfigRequest? VectorStore);
    internal record SettingsRequest(
        float? Temperature,
        float? TopP,
        int? MaxTokens,
        int? MaxMessageCount,
        float? FrequencyPenalty,
        float? PresencePenalty,
        bool? StatelessMode,
        string? SystemMessage,
        bool? ReasoningEnabled,
        string? ReasoningLevel,
        string? ReasoningType);
    internal record CodeSnippetRequest(string? UserMessage);
    internal record TogglePresetRequest(bool Enabled);
    internal record SummaryPolicyRequest(bool Enabled, string? TriggerType, int Threshold, int KeepRecent);
    internal record RagPipelineSettingsRequest(
        int? ChunkSize,
        int? ChunkOverlap,
        string? Chunker,
        string? EmbeddingProvider,
        string? EmbeddingModel,
        int? EmbeddingDimensions,
        string? EmbeddingBaseUrl,
        Mythosia.AI.Rag.RagFilter? FinalFilter,
        Mythosia.AI.Rag.RagRetrievalDerivation? RetrievalDerivation,
        string? PromptTemplate,
        bool? QueryRewriterEnabled,
        string? RewriterModelOverride,
        string? RewriterApiKey,
        bool? HybridSearchEnabled,
        float? HybridSearchVectorWeight,
        bool? RerankEnabled,
        string? RerankProvider,
        string? RerankModel,
        string? RerankBaseUrl,
        string? RerankApiKey);
    internal record WhyMissingRequest(string? Query, string? ExpectedText);
    internal record QueryScoresRequest(string? Query, string? ExpectedText);
    internal record VectorStoreConfigRequest(
        string? Provider,
        string? ConnectionString,
        string? TableName,
        string? SchemaName,
        int? Dimension,
        bool? EnsureSchema,
        string? OpenAiApiKey = null,
        string? QdrantHost = null,
        int? QdrantPort = null,
        string? QdrantApiKey = null,
        bool? QdrantUseTls = null,
        string? QdrantCollectionName = null,
        string? PineconeIndexHost = null,
        string? PineconeApiKey = null,
        string? PineconeNamespace = null);
}
