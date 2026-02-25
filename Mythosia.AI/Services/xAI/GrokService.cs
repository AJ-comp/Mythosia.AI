using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services.xAI
{
    public partial class GrokService : AIService
    {
        public override AIProvider Provider => AIProvider.xAI;

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("grok-4")) return 131072;
            if (model.Contains("grok-3")) return 131072;
            return 131072;
        }

        public GrokService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.x.ai/v1/", httpClient)
        {
            Model = AIModel.Grok3.ToDescription();
            MaxTokens = 8000;
            AddNewChat(new ChatBlock());
        }

        #region Core Completion Methods

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
                Console.WriteLine($"[Grok Round {round + 1}/{policy.MaxRounds}]");

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
                        "xAI rate limit exceeded. Please try again later.",
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

        #endregion

        #region Token Counting

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

        #endregion

        #region xAI-Specific Features

        /// <summary>
        /// xAI Grok supports vision (image inputs)
        /// </summary>
        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        /// <summary>
        /// xAI Grok doesn't support image generation
        /// </summary>
        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("xAI Grok", "Image Generation");
        }

        /// <summary>
        /// xAI Grok doesn't support image generation
        /// </summary>
        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("xAI Grok", "Image Generation");
        }

        /// <summary>
        /// Switches to Grok 3 Mini for faster, lightweight reasoning
        /// </summary>
        public GrokService UseMiniModel()
        {
            ChangeModel(AIModel.Grok3Mini);
            return this;
        }

        /// <summary>
        /// Switches to Grok 4 flagship reasoning model
        /// </summary>
        public GrokService UseGrok4Model()
        {
            ChangeModel(AIModel.Grok4);
            return this;
        }

        /// <summary>
        /// Switches to Grok 4.1 Fast model for speed-optimized tasks
        /// </summary>
        public GrokService UseGrok4FastModel()
        {
            ChangeModel(AIModel.Grok4_1Fast);
            return this;
        }

        /// <summary>
        /// Sets Grok-specific parameters for code generation
        /// </summary>
        public GrokService WithCodeGenerationMode(string language = "python")
        {
            var systemPrompt = $"You are an expert {language} programmer. Generate clean, efficient, and well-documented code.";
            SystemMessage = systemPrompt;
            Temperature = 0.1f;
            return this;
        }

        /// <summary>
        /// Gets completion with Chain of Thought prompting
        /// </summary>
        public async Task<string> GetCompletionWithCoTAsync(string prompt)
        {
            var cotPrompt = $"{prompt}\n\nPlease think step by step and show your reasoning process.";
            return await GetCompletionAsync(cotPrompt);
        }

        #endregion
    }
}
