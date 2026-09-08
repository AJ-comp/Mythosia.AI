using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        private readonly object _featureGate = new object();
        private AIRequestFeatures _pendingRequestFeatures = new AIRequestFeatures();
        private readonly AsyncLocal<RequestFeatureExecution?> _requestFeatureExecution = new AsyncLocal<RequestFeatureExecution?>();
        private RequestFeatureExecution? _lastFeatureExecution;

        /// <summary>Captured options for the current logical request.</summary>
        protected AIRequestFeatures CurrentRequestFeatures => _requestFeatureExecution.Value?.Features ?? new AIRequestFeatures();
        /// <summary>The user message anchoring the current logical request in conversation history.</summary>
        protected Message? CurrentFeatureRequestMessage => _requestFeatureExecution.Value?.Message;
        public IReadOnlyList<AICitation> LastCitations => _lastFeatureExecution?.Snapshot() ?? Array.Empty<AICitation>();

        public void ConfigureRequestFeatures(AIRequestFeatures features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            var copy = features.Clone();
            ValidateFeatureValues(copy);
            lock (_featureGate)
            {
                if (copy.Reasoning != null) _pendingRequestFeatures.Reasoning = copy.Reasoning;
                if (copy.WebSearch != null) _pendingRequestFeatures.WebSearch = copy.WebSearch;
                if (copy.FileSearch != null) _pendingRequestFeatures.FileSearch = copy.FileSearch;
            }
        }

        public void ValidatePendingRequestFeatures()
        {
            AIRequestFeatures features;
            lock (_featureGate) features = _pendingRequestFeatures.Clone();
            ValidateRequestFeatures(features);
        }

        /// <summary>Reject unsupported features before any network request or history mutation.</summary>
        protected virtual void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (!features.IsEmpty)
                throw new NotSupportedException($"{Provider} model '{Model}' does not support the requested reasoning or hosted search settings.");
        }

        /// <summary>Returns a reason when protocol state requires retaining the original conversation prefix.</summary>
        protected virtual string? GetConversationCompactionBlockReason() => null;

        /// <summary>Captures pending settings once; nested calls share the snapshot, including format repairs.</summary>
        protected IDisposable BeginRequestFeaturesScope(Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (_requestFeatureExecution.Value != null) return new FeatureScope(() => { });
            return UseRequestFeatureExecution(CaptureRequestFeatures(message));
        }

        IDisposable Services.IAIRequestFeatureService.BeginRequestFeaturesScope(Message message)
            => BeginRequestFeaturesScope(message);

        private RequestFeatureExecution CaptureRequestFeatures(Message message)
        {
            AIRequestFeatures features;
            if (_requestFeatureExecution.Value != null)
                features = _requestFeatureExecution.Value.Features.Clone();
            else lock (_featureGate)
            {
                features = _pendingRequestFeatures.Clone();
                // A failed request consumes its options too, so they cannot affect an unrelated later call.
                _pendingRequestFeatures = new AIRequestFeatures();
            }
            var execution = new RequestFeatureExecution(features, message);
            _lastFeatureExecution = execution;
            ValidateRequestFeatures(features);
            return execution;
        }

        private IDisposable UseRequestFeatureExecution(RequestFeatureExecution execution)
        {
            var previous = _requestFeatureExecution.Value;
            _requestFeatureExecution.Value = execution;
            return new FeatureScope(() => _requestFeatureExecution.Value = previous);
        }

        // Summary/query-rewrite work must neither consume pending options nor inherit hosted tools.
        private IDisposable SuppressRequestFeatures(Message? message = null)
            => UseRequestFeatureExecution(new RequestFeatureExecution(new AIRequestFeatures(), message));

        /// <summary>Retains a provider source even if callers do not consume stream events.</summary>
        protected void RecordCitation(AICitation citation)
        {
            if (citation == null) throw new ArgumentNullException(nameof(citation));
            _requestFeatureExecution.Value?.Add(citation);
        }

        private static void ValidateFeatureValues(AIRequestFeatures features)
        {
            if (features.Reasoning != null && (!Enum.IsDefined(typeof(ReasoningLevel), features.Reasoning.Level) ||
                !Enum.IsDefined(typeof(CachePreservation), features.Reasoning.Cache)))
                throw new ArgumentOutOfRangeException(nameof(features), "Unknown reasoning level or cache preservation policy.");
            if (features.WebSearch?.AllowedDomains?.Any(string.IsNullOrWhiteSpace) == true)
                throw new ArgumentException("Search domains must not be empty.", nameof(features));
            if (features.FileSearch != null && (features.FileSearch.Stores.Count == 0 || features.FileSearch.Stores.Any(store => store == null)))
                throw new ArgumentException("At least one non-null file search store is required.", nameof(features));
        }

        private sealed class FeatureScope : IDisposable
        {
            private Action? _restore;
            internal FeatureScope(Action restore) { _restore = restore; }
            public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
        }

        private sealed class RequestFeatureExecution
        {
            internal AIRequestFeatures Features { get; }
            internal Message? Message { get; }
            private readonly List<AICitation> _citations = new List<AICitation>();
            internal RequestFeatureExecution(AIRequestFeatures features, Message? message) { Features = features; Message = message; }
            internal void Add(AICitation citation)
            {
                lock (_citations)
                {
                    if (_citations.Any(c => c.Provider == citation.Provider && c.Url == citation.Url && c.FileId == citation.FileId &&
                        c.Title == citation.Title && c.Text == citation.Text && c.ResponseId == citation.ResponseId &&
                        c.OutputIndex == citation.OutputIndex && c.ContentIndex == citation.ContentIndex &&
                        c.StartIndex == citation.StartIndex && c.EndIndex == citation.EndIndex)) return;
                    _citations.Add(citation.Clone());
                }
            }
            internal IReadOnlyList<AICitation> Snapshot() { lock (_citations) return _citations.Select(c => c.Clone()).ToArray(); }
        }
    }
}
