using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService : OpenAICompatibleService
    {
        public override string Provider => AlibabaProvider.Name;

        public QwenThinking ThinkingMode { get; set; } = QwenThinking.Off;

        public string? ModelIdOverride { get; set; }

        private readonly EndpointPlatform _endpointPlatform;

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("qwen3")) return 8192;
            if (model.Contains("qwen-max")) return 8192;
            if (model.Contains("qwen-plus")) return 8192;
            if (model.Contains("qwen-turbo")) return 8192;
            return 8192;
        }

        public QwenService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://dashscope.aliyuncs.com/compatible-mode/v1/", httpClient)
        {
            Model = AlibabaModels.QwenMax;
            MaxTokens = 8000;
        }

        public QwenService(string apiKey, string model, HttpClient httpClient)
            : this(apiKey, httpClient)
        {
            ChangeModel(model);
        }

        public QwenService(string baseUrl, Mythosia.AI.Providers.Alibaba.EndpointPlatform platform, HttpClient httpClient)
            : base(null, NormalizeCustomBaseUrl(baseUrl), httpClient)
        {
            _endpointPlatform = platform;
            Model = AlibabaModels.QwenMax;
            MaxTokens = 8000;
        }

        public QwenService(string baseUrl, Mythosia.AI.Providers.Alibaba.EndpointPlatform platform, string model, HttpClient httpClient)
            : this(baseUrl, platform, httpClient)
        {
            ChangeModel(model);
        }

        private static string NormalizeCustomBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return baseUrl;

            var uri = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
            if (uri.AbsolutePath.Contains("/v1/") || uri.AbsolutePath.Contains("/v1"))
                return baseUrl;

            var trimmed = baseUrl.TrimEnd('/');
            return trimmed + "/v1/";
        }

        public override async Task<string> GetCompletionAsync(Message message)
        {
            var policy = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            ChatBlock? originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                Stream = false;
                ActivateChat.Messages.Add(message);

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    var result = await ProcessSingleRoundAsync(round, policy, cts.Token);
                    if (result.IsComplete)
                        return result.Content;
                }

                throw new AIServiceException($"Maximum rounds ({policy.MaxRounds}) exceeded");
            }
            catch (OperationCanceledException)
            {
                throw new AIServiceException($"Request timeout after {policy.TimeoutSeconds} seconds");
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        private async Task<RoundResult> ProcessSingleRoundAsync(
            int round,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
                Console.WriteLine($"[Qwen Round {round + 1}/{policy.MaxRounds}]");

            bool useFunctions = ShouldUseFunctions;
            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new RateLimitExceededException(
                        "Qwen rate limit exceeded. Please try again later.",
                        TimeSpan.FromSeconds(60));
                }

                // The body carries the diagnosis (e.g. vLLM's "maximum context length is N tokens"),
                // while ReasonPhrase is just "Bad Request" — so translation must read the body.
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, errorContent);
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            if (useFunctions)
                return await ProcessFunctionResponseAsync(responseContent, policy, cancellationToken);

            return ProcessRegularResponse(responseContent);
        }

        private async Task<RoundResult> ProcessFunctionResponseAsync(
            string responseContent,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            var (content, functionCalls) = ExtractFunctionCalls(responseContent);

            if (functionCalls.Calls.Count > 0)
            {
                var results = await ProcessFunctionCallsAsync(
                    functionCalls,
                    policy,
                    cancellationToken);
                AddFunctionCallBatchToHistory(
                    content,
                    functionCalls,
                    new Dictionary<string, object> { ["model"] = Model });
                AddFunctionResultBatchToHistory(
                    results,
                    new Dictionary<string, object> { ["model"] = Model });
                return RoundResult.Continue();
            }

            var finishReason = _protocol.ExtractFinishReason(responseContent);
            if (!string.IsNullOrEmpty(finishReason))
            {
                if (!string.Equals(finishReason, "stop", StringComparison.Ordinal))
                {
                    throw new AIServiceException(
                        $"Qwen ended the response with finish_reason={finishReason}; the partial response was not saved.");
                }

                if (!string.IsNullOrEmpty(content))
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
                return RoundResult.Complete(content ?? string.Empty);
            }

            if (string.IsNullOrEmpty(content))
                return RoundResult.Continue();

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
            return RoundResult.Complete(content);
        }

        private RoundResult ProcessRegularResponse(string responseContent)
        {
            var result = ExtractResponseContent(responseContent);
            var finishReason = _protocol.ExtractFinishReason(responseContent);
            if (!string.IsNullOrEmpty(finishReason) &&
                !string.Equals(finishReason, "stop", StringComparison.Ordinal))
            {
                throw new AIServiceException(
                    $"Qwen ended the response with finish_reason={finishReason}; the partial response was not saved.");
            }

            if (string.Equals(finishReason, "stop", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(result))
            {
                return RoundResult.Complete(string.Empty);
            }

            if (string.IsNullOrEmpty(result))
                return RoundResult.Continue();

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
            return RoundResult.Complete(result);
        }

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var encoding = TikToken.EncodingForModel("gpt-4");

            var allMessagesBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                allMessagesBuilder.Append(SystemMessage).Append('\n');
            }

            foreach (var message in GetLatestMessages())
            {
                allMessagesBuilder.Append(message.Role).Append('\n');
                allMessagesBuilder.Append(message.GetDisplayText()).Append('\n');
            }

            var tokens = encoding.Encode(allMessagesBuilder.ToString());
            return await Task.FromResult((uint)tokens.Count);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = TikToken.EncodingForModel("gpt-4");
            var tokens = encoding.Encode(prompt);
            return await Task.FromResult((uint)tokens.Count);
        }

        public QwenService UseMaxModel()
        {
            ChangeModel(AlibabaModels.QwenMax);
            return this;
        }

        public QwenService UsePlusModel()
        {
            ChangeModel(AlibabaModels.QwenPlus);
            return this;
        }

        public QwenService UseTurboModel()
        {
            ChangeModel(AlibabaModels.QwenTurbo);
            return this;
        }

        public QwenService UseQwen3_235BModel()
        {
            ChangeModel(AlibabaModels.Qwen3_235B);
            return this;
        }

        public QwenService UseQwen3_32BModel()
        {
            ChangeModel(AlibabaModels.Qwen3_32B);
            return this;
        }

        public QwenService WithQwenParameters(Mythosia.AI.Providers.Alibaba.QwenThinking thinking = Mythosia.AI.Providers.Alibaba.QwenThinking.On)
        {
            ThinkingMode = thinking;
            return this;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return base.ApplyProviderSpecificRequestProfile(profile);

            var backupThinkingMode = ThinkingMode;
            ThinkingMode = Mythosia.AI.Providers.Alibaba.QwenThinking.Off;

            return () =>
            {
                ThinkingMode = backupThinkingMode;
            };
        }

        internal string GetEffectiveModelId()
        {
            if (!string.IsNullOrWhiteSpace(ModelIdOverride))
                return ModelIdOverride;
            if (_endpointPlatform == Mythosia.AI.Providers.Alibaba.EndpointPlatform.Ollama)
                return ConvertToOllamaId(Model);
            return Model;
        }

        private static string ConvertToOllamaId(string model)
        {
            for (int i = 0; i < model.Length - 1; i++)
            {
                if (model[i] == '-' && char.IsDigit(model[i + 1]))
                    return model.Substring(0, i) + ":" + model.Substring(i + 1);
            }
            return model;
        }
    }
}
