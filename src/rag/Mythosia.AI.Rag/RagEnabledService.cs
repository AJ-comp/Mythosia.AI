using Mythosia.AI.Models;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Wraps an AIService to intercept queries through the RAG pipeline before sending to the LLM.
    /// All calls go through IRagPipeline.ProcessAsync — the AIService itself is never modified.
    /// </summary>
    public class RagEnabledService
    {
        private readonly IAIService _innerService;
        private readonly RagBuilder? _builder;
        private RagStore? _ragStore;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Creates a RagEnabledService with lazy initialization (documents indexed on first query).
        /// </summary>
        internal RagEnabledService(IAIService innerService, RagBuilder builder)
        {
            _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>
        /// Creates a RagEnabledService with a pre-built RagStore (no lazy init needed).
        /// </summary>
        internal RagEnabledService(IAIService innerService, RagStore ragStore)
        {
            _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
            _ragStore = ragStore ?? throw new ArgumentNullException(nameof(ragStore));
        }

        /// <summary>
        /// Returns the underlying IAIService without RAG processing.
        /// </summary>
        public IAIService WithoutRag() => _innerService;

        /// <summary>Sets processing speed for the next answer, after retrieval. Fast can incur premium provider charges.</summary>
        public RagEnabledService WithSpeed(InferenceSpeed speed)
        {
            _innerService.WithSpeed(speed);
            return this;
        }

        /// <summary>Processing modes reported for the last answer. Auxiliary query rewriting is excluded.</summary>
        public IReadOnlyList<AIProcessingInfo> LastProcessing => _innerService.GetLastProcessing();

        /// <summary>Requests a reasoning level for the next answer, after RAG retrieval.</summary>
        public RagEnabledService WithReasoning(ReasoningLevel level, CachePreservation cache = CachePreservation.None)
        {
            _innerService.WithReasoning(level, cache);
            return this;
        }

        /// <summary>Adds the provider's hosted web search to the next RAG answer.</summary>
        public RagEnabledService WithWebSearch(WebSearchOptions? options = null)
        {
            _innerService.WithWebSearch(options);
            return this;
        }

        /// <summary>Adds existing provider-hosted document stores to the next RAG answer.</summary>
        public RagEnabledService WithFileSearch(params FileSearchStore[] stores)
        {
            _innerService.WithFileSearch(stores);
            return this;
        }

        /// <summary>Hosted-search sources returned by the last answer. RAG retrieval references remain on RagProcessedQuery.</summary>
        public IReadOnlyList<AICitation> LastCitations =>
            (_innerService as IAIRequestFeatureService)?.LastCitations ?? Array.Empty<AICitation>();

        #region Core Methods

        /// <summary>
        /// Retrieves RAG context and starts one run whose result, output, and steering share the same execution.
        /// </summary>
        /// <remarks>
        /// Retrieval happens once before the run starts. Steering uses the underlying provider's capability;
        /// it does not repeat retrieval. The original query remains in conversation history.
        /// </remarks>
        public Task<AIRun> StartRunAsync(
            string query,
            Action<string>? onText = null,
            RagQueryOptions? options = null,
            StreamOptions? streamOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return StartRunAsync(new Message(ActorRole.User, query), onText, options, streamOptions, cancellationToken);
        }

        /// <summary>
        /// Retrieves context for a message and starts a run without replacing the message stored in history.
        /// </summary>
        public async Task<AIRun> StartRunAsync(
            Message message,
            Action<string>? onText = null,
            RagQueryOptions? options = null,
            StreamOptions? streamOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            cancellationToken.ThrowIfCancellationRequested();
            if (!(_innerService is IAIRunService runService))
                throw new NotSupportedException("The wrapped AI service does not implement IAIRunService.");

            using var features = (_innerService as IAIRequestFeatureService)?.BeginRequestFeaturesScope(message);

            var query = message.Content ?? message.GetDisplayText();
            var processed = await RewriteAndProcessAsync(query, options, cancellationToken).ConfigureAwait(false);
            return await runService.StartRunAsync(
                message, onText, streamOptions, BuildRequestContext(processed, message), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Processes the query through RAG pipeline, then sends the request message content to the LLM.
        /// </summary>
        public async Task<string> GetCompletionAsync(string query, CancellationToken cancellationToken = default)
        {
            return await GetCompletionAsync(query, options: null, cancellationToken);
        }

        /// <summary>
        /// Processes the query through RAG pipeline with per-request query overrides,
        /// then sends the request message content to the LLM.
        /// </summary>
        public async Task<string> GetCompletionAsync(
            string query,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = new Message(ActorRole.User, query);
            using var features = (_innerService as IAIRequestFeatureService)?.BeginRequestFeaturesScope(message);
            var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await _innerService.GetCompletionAsync(
                message,
                context: BuildRequestContext(processed, message),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Processes a Message through RAG pipeline (extracts text content for retrieval).
        /// Retains non-text content in the model request without changing the original message.
        /// </summary>
        public async Task<string> GetCompletionAsync(Message message, CancellationToken cancellationToken = default)
        {
            return await GetCompletionAsync(message, options: null, cancellationToken);
        }

        /// <summary>
        /// Processes a Message through RAG pipeline (extracts text content for retrieval)
        /// with per-request query overrides. Non-text content is retained in the model request;
        /// the original message and its text in conversation history are not replaced.
        /// </summary>
        public async Task<string> GetCompletionAsync(
            Message message,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var features = (_innerService as IAIRequestFeatureService)?.BeginRequestFeaturesScope(message);
            var query = message.Content ?? message.GetDisplayText();
            var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await _innerService.GetCompletionAsync(message, context: BuildRequestContext(processed, message),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Streams the LLM response after RAG augmentation.
        /// </summary>
        /// <remarks>
        /// This public entry point is planned to become non-public in the next major version,
        /// after the replacement run API is available. It remains supported during the minor-version
        /// transition, and the RAG retrieval and augmentation implementation will be retained.
        /// Use StartRunAsync and observe the returned run's StreamAsync method for new integrations.
        /// </remarks>
        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in StreamAsync(prompt, options: null, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Streams the LLM response after RAG augmentation with per-request query overrides.
        /// </summary>
        /// <remarks>
        /// This public entry point is planned to become non-public in the next major version,
        /// after the replacement run API is available. It remains supported during the minor-version
        /// transition, and the RAG retrieval and augmentation implementation will be retained.
        /// Use StartRunAsync and observe the returned run's StreamAsync method for new integrations.
        /// </remarks>
        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            RagQueryOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = new Message(ActorRole.User, prompt);
            using var features = (_innerService as IAIRequestFeatureService)?.BeginRequestFeaturesScope(message);
            var processed = await RewriteAndProcessAsync(prompt, options, cancellationToken);

            await foreach (var chunk in _innerService.StreamAsync(
                message,
                BuildRequestContext(processed, message),
                cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Streams the LLM response as a one-off query (no conversation history).
        /// </summary>
        public async IAsyncEnumerable<string> StreamOnceAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in StreamOnceAsync(prompt, options: null, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Streams the LLM response as a one-off query with per-request query overrides
        /// (no conversation history).
        /// </summary>
        public async IAsyncEnumerable<string> StreamOnceAsync(
            string prompt,
            RagQueryOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = new Message(ActorRole.User, prompt);
            var originalMode = _innerService.StatelessMode;
            _innerService.StatelessMode = true;
            try
            {
                using var features = (_innerService as IAIRequestFeatureService)?.BeginRequestFeaturesScope(message);
                var processed = await RewriteAndProcessAsync(prompt, options, cancellationToken);
                await foreach (var chunk in _innerService.StreamAsync(
                    message,
                    BuildRequestContext(processed, message),
                    cancellationToken))
                {
                    yield return chunk;
                }
            }
            finally
            {
                _innerService.StatelessMode = originalMode;
            }
        }

        /// <summary>
        /// Performs RAG retrieval and returns the processed query (context + references) without calling the LLM.
        /// Useful for inspecting what context would be sent.
        /// </summary>
        public async Task<RagProcessedQuery> RetrieveAsync(string query, CancellationToken cancellationToken = default)
        {
            return await RetrieveAsync(query, options: null, cancellationToken);
        }

        /// <summary>
        /// Performs RAG retrieval with per-request query overrides and returns the processed query
        /// (context + references) without calling the LLM.
        /// </summary>
        public async Task<RagProcessedQuery> RetrieveAsync(
            string query,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            return await RewriteAndProcessAsync(query, options, cancellationToken);
        }

        #endregion

        #region Query Rewriting

        /// <summary>
        /// Rewrites the query using conversation history (if a rewriter is configured),
        /// then processes through the RAG pipeline.
        /// </summary>
        private async Task<RagProcessedQuery> RewriteAndProcessAsync(
            string query,
            RagQueryOptions? options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var store = await EnsureInitializedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Runtime changes affect later requests; this request keeps its selected instance through awaits.
            var queryRewriter = store.QueryRewriter;

            string searchQuery = query;
            string? rewrittenQuery = null;
            bool searchSkipped = false;
            QueryRewriteResult? rewriteResult = null;

            if (queryRewriter != null)
            {
                var history = GetConversationHistory();
                var result = await queryRewriter.RewriteAsync(query, history, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                rewriteResult = result;

                if (!result.NeedsSearch)
                {
                    searchSkipped = true;
                }
                else if (result.Query != query)
                {
                    rewrittenQuery = result.Query;
                    searchQuery = result.Query;
                }
            }

            // Build text search query from extracted keywords when available
            var keywords = rewriteResult?.Keywords;
            var textSearchQuery = keywords != null && keywords.Count > 0
                ? string.Join(" ", keywords)
                : null;

            RagProcessedQuery processed;
            if (searchSkipped)
            {
                processed = new RagProcessedQuery(
                    query, query,
                    Array.Empty<VectorDb.VectorSearchResult>(),
                    Array.Empty<VectorDb.VectorSearchResult>(),
                    new RagQueryDiagnostics());
                processed.SearchSkipped = true;
            }
            else if (textSearchQuery != null && store.Pipeline is RagPipeline concretePipeline)
            {
                processed = await concretePipeline.ProcessAsync(searchQuery, textSearchQuery, options, cancellationToken);
            }
            else
            {
                processed = await store.Pipeline.ProcessAsync(searchQuery, options, cancellationToken);
            }

            processed.RewriteResult = rewriteResult;
            processed.RewrittenQuery = rewrittenQuery;
            processed.SearchKeywords = rewriteResult?.Keywords;

            // Keep the original query in OriginalQuery (not the rewritten one)
            if (rewrittenQuery != null)
                processed.OriginalQuery = query;

            return processed;
        }

        private IReadOnlyList<ConversationTurn> GetConversationHistory()
        {
            var messages = _innerService.ActivateChat.Messages;
            if (messages.Count == 0)
                return Array.Empty<ConversationTurn>();

            return messages
                .Where(m => m.Role == ActorRole.User || m.Role == ActorRole.Assistant)
                .Select(m => new ConversationTurn(
                    m.Role == ActorRole.User ? "user" : "assistant",
                    m.Content ?? string.Empty))
                .ToList();
        }

        private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
        {
            // Augment only the outgoing text. Completion and runs must retain the same
            // media payloads while history continues to contain the original user input.
            var requestMessage = original.Clone();
            requestMessage.Content = processed.RequestMessageContent;
            if (original.HasMultimodalContent)
            {
                requestMessage.Contents = new List<MessageContent> { new TextContent(processed.RequestMessageContent) };
                requestMessage.Contents.AddRange(original.Contents.Where(content => !(content is TextContent)));
            }
            return new AIRequestContext { RequestMessageOverride = requestMessage };
        }

        #endregion

        #region Lazy Initialization

        private async Task<RagStore> EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            var store = Volatile.Read(ref _ragStore);
            if (store != null)
                return store;

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                store = Volatile.Read(ref _ragStore);
                if (store != null)
                    return store;

                if (_builder == null)
                    throw new InvalidOperationException("RagBuilder is null and RagStore is not initialized.");

                store = await _builder.BuildAsync(onDocumentEmbedded: null, cancellationToken);
                if (store.QueryRewriter == null && _builder.QueryRewriterEnabled)
                {
                    // Install the automatic default only during initialization. A later explicit clear
                    // must remain disabled, and a custom rewriter must never be replaced here.
                    store.SetQueryRewriter(new LlmQueryRewriter(_innerService, _builder.QueryRewriteMaxTokens));
                }

                // Publish only the fully configured store, including its automatic rewriter.
                Volatile.Write(ref _ragStore, store);
                return store;
            }
            finally
            {
                _initLock.Release();
            }
        }

        #endregion
    }
}
