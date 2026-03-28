using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
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
        private readonly AIService _innerService;
        private readonly RagBuilder? _builder;
        private RagStore? _ragStore;
        private IQueryRewriter? _queryRewriter;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Creates a RagEnabledService with lazy initialization (documents indexed on first query).
        /// </summary>
        internal RagEnabledService(AIService innerService, RagBuilder builder)
        {
            _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>
        /// Creates a RagEnabledService with a pre-built RagStore (no lazy init needed).
        /// </summary>
        internal RagEnabledService(AIService innerService, RagStore ragStore)
        {
            _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
            _ragStore = ragStore ?? throw new ArgumentNullException(nameof(ragStore));
            ResolveQueryRewriter();
        }

        /// <summary>
        /// Returns the underlying AIService without RAG processing.
        /// </summary>
        public AIService WithoutRag() => _innerService;

        #region Core Methods

        /// <summary>
        /// Processes the query through RAG pipeline, then sends the request message content to the LLM.
        /// </summary>
        public async Task<string> GetCompletionAsync(string query)
        {
            return await GetCompletionAsync(query, options: null);
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
            var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
            return await _innerService.GetCompletionAsync(
                new Message(ActorRole.User, query),
                BuildRequestContext(processed));
        }

        /// <summary>
        /// Processes a Message through RAG pipeline (extracts text content for retrieval).
        /// </summary>
        public async Task<string> GetCompletionAsync(Message message)
        {
            return await GetCompletionAsync(message, options: null);
        }

        /// <summary>
        /// Processes a Message through RAG pipeline (extracts text content for retrieval)
        /// with per-request query overrides.
        /// </summary>
        public async Task<string> GetCompletionAsync(
            Message message,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            var query = message.Content ?? message.GetDisplayText();
            var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
            return await _innerService.GetCompletionAsync(message, BuildRequestContext(processed));
        }

        /// <summary>
        /// Streams the LLM response after RAG augmentation.
        /// </summary>
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
        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            RagQueryOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var processed = await RewriteAndProcessAsync(prompt, options, cancellationToken);

            await foreach (var chunk in _innerService.StreamAsync(
                new Message(ActorRole.User, prompt),
                BuildRequestContext(processed),
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
            var processed = await RewriteAndProcessAsync(prompt, options, cancellationToken);

            var originalMode = _innerService.StatelessMode;
            _innerService.StatelessMode = true;
            try
            {
                await foreach (var chunk in _innerService.StreamAsync(
                    new Message(ActorRole.User, prompt),
                    BuildRequestContext(processed),
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
            var store = await EnsureInitializedAsync(cancellationToken);

            string searchQuery = query;
            string? rewrittenQuery = null;
            bool searchSkipped = false;
            QueryRewriteResult? rewriteResult = null;

            if (_queryRewriter != null)
            {
                var history = GetConversationHistory();
                var result = await _queryRewriter.RewriteAsync(query, history, cancellationToken);
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

        private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
        {
            return new AIRequestContext
            {
                RequestMessageOverride = new Message(ActorRole.User, processed.RequestMessageContent)
            };
        }

        #endregion

        #region Lazy Initialization

        private async Task<RagStore> EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            if (_ragStore != null)
                return _ragStore;

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_ragStore != null)
                    return _ragStore;

                if (_builder == null)
                    throw new InvalidOperationException("RagBuilder is null and RagStore is not initialized.");

                _ragStore = await _builder.BuildAsync(onDocumentEmbedded: null, cancellationToken);
                ResolveQueryRewriter();
                return _ragStore;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private void ResolveQueryRewriter()
        {
            if (_ragStore == null) return;

            if (_ragStore.QueryRewriter != null)
            {
                // Custom IQueryRewriter was provided via builder
                _queryRewriter = _ragStore.QueryRewriter;
            }
            else if (_builder != null && _builder.QueryRewriterEnabled)
            {
                // WithQueryRewriter() was called without a custom implementation;
                // use the inner AIService as the LLM for rewriting.
                var rewriter = new LlmQueryRewriter(_innerService, _builder.QueryRewriteMaxTokens);
                _queryRewriter = rewriter;
                _ragStore.SetQueryRewriter(rewriter);
            }
        }

        #endregion
    }
}
