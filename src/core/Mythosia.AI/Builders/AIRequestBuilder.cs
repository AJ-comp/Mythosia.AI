using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Builders
{
    /// <summary>Builds an independent request. Every With method returns a new builder.</summary>
    /// <remarks>Service defaults are captured by CreateRequest. Conversation history is selected at
    /// execution time and is still owned by the service; builders do not enable concurrent use of a conversation.
    /// Built-in input payloads, settings, schemas and option collections are copied. Custom content and handler
    /// targets must remain immutable or be synchronized by their owner.</remarks>
    public sealed class AIRequestBuilder
    {
        private readonly AIService _service;
        private readonly AIRequest _request;

        internal AIRequestBuilder(AIService service, AIRequest request) { _service = service; _request = request; }

        /// <summary>Inspects this request's captured model, endpoint and options without executing or consuming it.</summary>
        /// <remarks>Supported means available, not enabled. Unknown requires a caller policy; execution still validates combinations.</remarks>
        public AIModelCapabilities GetCapabilities() => _service.GetRequestCapabilities(_request);

        private AIRequestBuilder Set(string name, object? value) => new AIRequestBuilder(_service, _request.WithSetting(name, value));

        public AIRequestBuilder WithTemperature(float temperature)
        {
            Range(temperature, 0, 2, nameof(temperature));
            return Set(nameof(AIService.Temperature), temperature);
        }

        public AIRequestBuilder WithTopP(float topP)
        {
            Range(topP, 0, 1, nameof(topP));
            return Set(nameof(AIService.TopP), topP);
        }

        public AIRequestBuilder WithMaxTokens(uint maxTokens)
        {
            if (maxTokens == 0) throw new ArgumentOutOfRangeException(nameof(maxTokens));
            return Set(nameof(AIService.MaxTokens), maxTokens);
        }

        public AIRequestBuilder WithFrequencyPenalty(float penalty)
        {
            Range(penalty, -2, 2, nameof(penalty));
            return Set(nameof(AIService.FrequencyPenalty), penalty);
        }

        public AIRequestBuilder WithPresencePenalty(float penalty)
        {
            Range(penalty, -2, 2, nameof(penalty));
            return Set(nameof(AIService.PresencePenalty), penalty);
        }

        public AIRequestBuilder WithSystemMessage(string systemMessage)
            => Set(nameof(AIService.SystemMessage), systemMessage ?? throw new ArgumentNullException(nameof(systemMessage)));
        public AIRequestBuilder WithStatelessMode(bool enabled = true) => Set(nameof(AIService.StatelessMode), enabled);
        public AIRequestBuilder WithFunctionsDisabled(bool disabled = true) => Set(nameof(AIService.FunctionsDisabled), disabled);

        public AIRequestBuilder WithPolicy(FunctionCallingPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (policy.MaxRounds <= 0 || policy.MaxConcurrency <= 0 || policy.TimeoutSeconds <= 0 ||
                !Enum.IsDefined(typeof(FunctionExecutionMode), policy.ExecutionMode))
                throw new ArgumentOutOfRangeException(nameof(policy), "Rounds, concurrency and specified timeout must be positive and execution mode must be valid.");
            return Set(nameof(AIService.DefaultPolicy), policy.Clone());
        }

        private FunctionCallingPolicy Policy => ((FunctionCallingPolicy)_request.Settings[nameof(AIService.DefaultPolicy)]!).Clone();
        public AIRequestBuilder WithMaxRounds(int maxRounds) { var policy = Policy; policy.MaxRounds = maxRounds; return WithPolicy(policy); }
        public AIRequestBuilder WithTimeout(int seconds) { var policy = Policy; policy.TimeoutSeconds = seconds; return WithPolicy(policy); }
        public AIRequestBuilder WithFunctionExecution(FunctionExecutionMode mode, int? maxConcurrency = null)
        {
            var policy = Policy; policy.ExecutionMode = mode;
            if (maxConcurrency.HasValue) policy.MaxConcurrency = maxConcurrency.Value;
            return WithPolicy(policy);
        }

        /// <summary>Adds copied function definitions to this request; handler contracts are unchanged.</summary>
        public AIRequestBuilder WithFunctions(params FunctionDefinition[] functions)
        {
            if (functions == null) throw new ArgumentNullException(nameof(functions));
            var existing = (List<FunctionDefinition>)_request.Settings[nameof(AIService.Functions)]!;
            return Set(nameof(AIService.Functions), existing.Concat(functions).Select(AIRequest.CopyFunction).ToList());
        }

        /// <summary>Returns a separate request selecting a processing mode. Fast opts into premium pricing.</summary>
        public AIRequestBuilder WithSpeed(InferenceSpeed speed)
        {
            if (!Enum.IsDefined(typeof(InferenceSpeed), speed)) throw new ArgumentOutOfRangeException(nameof(speed));
            var features = _request.Features.Clone();
            features.Speed = speed;
            return new AIRequestBuilder(_service, _request.WithFeatures(features));
        }

        public AIRequestBuilder WithReasoning(ReasoningLevel level, CachePreservation cache = CachePreservation.None)
        {
            if (!Enum.IsDefined(typeof(ReasoningLevel), level)) throw new ArgumentOutOfRangeException(nameof(level));
            if (!Enum.IsDefined(typeof(CachePreservation), cache)) throw new ArgumentOutOfRangeException(nameof(cache));
            var features = _request.Features.Clone();
            features.Reasoning = new ReasoningOptions { Level = level, Cache = cache };
            return new AIRequestBuilder(_service, _request.WithFeatures(features));
        }

        public AIRequestBuilder WithWebSearch(WebSearchOptions? options = null)
        {
            var features = _request.Features.Clone();
            features.WebSearch = (options ?? new WebSearchOptions()).Clone();
            if (features.WebSearch.AllowedDomains?.Any(string.IsNullOrWhiteSpace) == true)
                throw new ArgumentException("Search domains must not be empty.", nameof(options));
            return new AIRequestBuilder(_service, _request.WithFeatures(features));
        }

        public AIRequestBuilder WithFileSearch(params FileSearchStore[] stores)
        {
            if (stores == null) throw new ArgumentNullException(nameof(stores));
            if (stores.Length == 0 || stores.Any(s => s == null)) throw new ArgumentException("At least one non-null store is required.", nameof(stores));
            var features = _request.Features.Clone();
            features.FileSearch = new FileSearchOptions { Stores = stores.ToArray() };
            return new AIRequestBuilder(_service, _request.WithFeatures(features));
        }

        public AIRequestBuilder WithContext(AIRequestContext context)
            => new AIRequestBuilder(_service, _request.WithContext(AIService.CopyRequestContext(context ?? throw new ArgumentNullException(nameof(context)))!));

        public AIRequestBuilder WithProfile(AIRequestProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!Enum.IsDefined(typeof(AIRequestPurpose), profile.Purpose)) throw new ArgumentOutOfRangeException(nameof(profile));
            // Resolve common overrides now so subsequent fluent calls can replace them.
            var result = this;
            if (profile.Temperature.HasValue) result = result.WithTemperature(profile.Temperature.Value);
            if (profile.MaxTokens.HasValue) result = result.WithMaxTokens(profile.MaxTokens.Value);
            if (profile.Stateless.HasValue) result = result.WithStatelessMode(profile.Stateless.Value);
            if (profile.DisableFunctions.HasValue) result = result.WithFunctionsDisabled(profile.DisableFunctions.Value);
            return new AIRequestBuilder(_service, result._request.WithProfile(new AIRequestProfile
            { Purpose = profile.Purpose, DisableReasoning = profile.DisableReasoning, MaxTokens = profile.MaxTokens }));
        }

        /// <summary>Executes this request and returns its final text.</summary>
        public Task<string> GetCompletionAsync(CancellationToken cancellationToken = default)
            => _service.ExecuteRequestAsync(_request, cancellationToken: cancellationToken);

        internal Task<string> SendMessageAsync(CancellationToken cancellationToken = default)
            => _service.ExecuteRequestAsync(_request, applySummary: false, cancellationToken: cancellationToken);

        internal AIRequestBuilder WithInput(string prompt)
            => new AIRequestBuilder(_service, _request.WithInput(new Models.Messages.Message(ActorRole.User, prompt)));
        internal AIRequestBuilder WithStructuredOutputSchema(string schema)
            => Set(nameof(AIService._structuredOutputSchemaJson), schema);
        internal IAsyncEnumerable<string> StreamAsync(CancellationToken cancellationToken = default)
            => _service.StreamRequestAsync(_request, cancellationToken);

        /// <summary>Starts this request with streaming observation, a final result and provider-supported steering.</summary>
        public Task<AIRun> StartRunAsync(Action<string>? onText = null, StreamOptions? options = null,
            CancellationToken cancellationToken = default)
            => _service.StartRequestRunAsync(_request, onText, options, cancellationToken);

        private static void Range(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, $"Value must be between {min} and {max}.");
        }
    }
}
