using Mythosia.AI.Services.Base;
using System;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Extension methods for AIService to enable RAG functionality.
    /// Follows the same Fluent API style as Mythosia.AI's WithFunction, WithSystemMessage, etc.
    /// </summary>
    public static class RagServiceExtensions
    {
        /// <summary>
        /// Enables RAG on this AIService with the specified configuration.
        /// Documents are loaded and indexed lazily on the first query.
        /// </summary>
        /// <example>
        /// <code>
        /// var service = new ClaudeService(apiKey, httpClient)
        ///     .WithRag(rag => rag
        ///         .AddDocument("manual.pdf")
        ///         .AddDocument("policy.txt")
        ///     );
        /// 
        /// var response = await service.GetCompletionAsync("환불 정책이 어떻게 되나요?");
        /// </code>
        /// </example>
        public static RagEnabledService WithRag(this AIService service, Action<RagBuilder> configure)
        {
            var builder = new RagBuilder();
            configure(builder);
            return new RagEnabledService(service, builder);
        }

        /// <summary>
        /// Enables RAG on this AIService using a pre-built RagStore.
        /// Use this to share a single index across multiple AIService instances.
        /// </summary>
        /// <example>
        /// <code>
        /// var ragStore = await RagStore.BuildAsync(config => config
        ///     .AddDocuments("./knowledge-base/")
        ///     .UseOpenAIEmbedding(apiKey)
        /// );
        /// 
        /// var claude = new ClaudeService(claudeKey, http).WithRag(ragStore);
        /// var gpt = new ChatGptService(gptKey, http).WithRag(ragStore);
        /// </code>
        /// </example>
        public static RagEnabledService WithRag(this AIService service, RagStore ragStore)
        {
            return new RagEnabledService(service, ragStore);
        }
    }
}
