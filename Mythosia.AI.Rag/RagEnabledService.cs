using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
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
        }

        /// <summary>
        /// Returns the underlying AIService without RAG processing.
        /// </summary>
        public AIService WithoutRag() => _innerService;

        #region Core Methods

        /// <summary>
        /// Processes the query through RAG pipeline, then sends the augmented prompt to the LLM.
        /// </summary>
        public async Task<string> GetCompletionAsync(string query)
        {
            var store = await EnsureInitializedAsync();
            var processed = await store.Pipeline.ProcessAsync(query);
            return await _innerService.GetCompletionAsync(processed.AugmentedPrompt);
        }

        /// <summary>
        /// Processes a Message through RAG pipeline (extracts text content for retrieval).
        /// </summary>
        public async Task<string> GetCompletionAsync(Message message)
        {
            var query = message.Content ?? message.GetDisplayText();
            var store = await EnsureInitializedAsync();
            var processed = await store.Pipeline.ProcessAsync(query);
            return await _innerService.GetCompletionAsync(processed.AugmentedPrompt);
        }

        /// <summary>
        /// Streams the LLM response after RAG augmentation.
        /// </summary>
        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var store = await EnsureInitializedAsync(cancellationToken);
            var processed = await store.Pipeline.ProcessAsync(prompt, cancellationToken);

            await foreach (var chunk in _innerService.StreamAsync(processed.AugmentedPrompt, cancellationToken))
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
            var store = await EnsureInitializedAsync(cancellationToken);
            var processed = await store.Pipeline.ProcessAsync(prompt, cancellationToken);

            await foreach (var chunk in _innerService.StreamOnceAsync(processed.AugmentedPrompt, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Performs RAG retrieval and returns the processed query (context + references) without calling the LLM.
        /// Useful for inspecting what context would be sent.
        /// </summary>
        public async Task<RagProcessedQuery> RetrieveAsync(string query, CancellationToken cancellationToken = default)
        {
            var store = await EnsureInitializedAsync(cancellationToken);
            return await store.Pipeline.ProcessAsync(query, cancellationToken);
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

                _ragStore = await _builder.BuildAsync(cancellationToken);
                return _ragStore;
            }
            finally
            {
                _initLock.Release();
            }
        }

        #endregion
    }
}
