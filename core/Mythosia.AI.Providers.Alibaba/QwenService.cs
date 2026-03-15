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
    public partial class QwenService : AIService
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
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            ChatBlock originalChat = null;
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

                throw new AIServiceException(
                    $"API request failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? errorContent : response.ReasonPhrase)}",
                    errorContent);
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            if (useFunctions)
                return await ProcessFunctionResponseAsync(responseContent, policy);

            return ProcessRegularResponse(responseContent);
        }

        private async Task<RoundResult> ProcessFunctionResponseAsync(
            string responseContent,
            FunctionCallingPolicy policy)
        {
            var (content, functionCall) = ExtractFunctionCall(responseContent);

            if (functionCall != null)
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"  Executing function: {functionCall.Name}");

                await ExecuteFunctionAsync(functionCall);
                return RoundResult.Continue();
            }

            if (string.IsNullOrEmpty(content))
                return RoundResult.Continue();

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
            return RoundResult.Complete(content);
        }

        private RoundResult ProcessRegularResponse(string responseContent)
        {
            var result = ExtractResponseContent(responseContent);
            if (string.IsNullOrEmpty(result))
                return RoundResult.Continue();

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
            return RoundResult.Complete(result);
        }

        private async Task ExecuteFunctionAsync(FunctionCall functionCall)
        {
            var functionCallMessage = new Message(ActorRole.Assistant, string.Empty)
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_call",
                    [MessageMetadataKeys.FunctionId] = functionCall.Id,
                    [MessageMetadataKeys.FunctionSource] = functionCall.Source,
                    [MessageMetadataKeys.FunctionName] = functionCall.Name,
                    [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(functionCall.Arguments),
                    ["model"] = Model
                }
            };

            ActivateChat.Messages.Add(functionCallMessage);

            var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);

            if (string.IsNullOrEmpty(result))
            {
                Console.WriteLine($"[WARNING] Function {functionCall.Name} returned empty result");
                result = "Function executed successfully";
            }

            var metadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.MessageType] = "function_result",
                [MessageMetadataKeys.FunctionId] = functionCall.Id,
                [MessageMetadataKeys.FunctionSource] = functionCall.Source,
                [MessageMetadataKeys.FunctionName] = functionCall.Name,
                ["model"] = Model
            };

            ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
            {
                Metadata = metadata
            });
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

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Qwen", "Image Generation");
        }

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Qwen", "Image Generation");
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
