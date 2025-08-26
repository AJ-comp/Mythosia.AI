using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
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

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService : AIService
    {
        public override AIProvider Provider => AIProvider.OpenAI;

        public ChatGptService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.openai.com/v1/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Gpt4oLatest)
            {
                MaxTokens = 16000
            };
            AddNewChat(chatBlock);
        }

        #region Core Completion Methods

        // ChatGptService.cs
        // ChatGptService.cs
        public override async Task<string> GetCompletionAsync(Message message)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            try
            {
                if (StatelessMode)
                {
                    return await ProcessStatelessRequestAsync(message, true);
                }

                ActivateChat.Stream = false;
                ActivateChat.Messages.Add(message);

                // 깔끔해진 메인 루프
                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    var result = await ProcessSingleRoundAsync(round, policy, cts.Token);

                    if (result.IsComplete)
                    {
                        return result.Content;
                    }
                    // Continue to next round if not complete
                }

                throw new AIServiceException($"Maximum rounds ({policy.MaxRounds}) exceeded");
            }
            catch (OperationCanceledException)
            {
                throw new AIServiceException($"Request timeout after {policy.TimeoutSeconds} seconds");
            }
        }

        /// <summary>
        /// 단일 라운드 처리
        /// </summary>
        private async Task<RoundResult> ProcessSingleRoundAsync(
            int round,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
            {
                Console.WriteLine($"[Round {round + 1}/{policy.MaxRounds}]");
            }

            // 1. API 요청 생성 및 전송
            var response = await SendApiRequestAsync(cancellationToken);

            // 2. 응답 처리
            var responseContent = await response.Content.ReadAsStringAsync();

            // 3. Function 지원 여부에 따라 처리
            bool useFunctions = ActivateChat.Functions?.Count > 0
                               && ActivateChat.EnableFunctions
                               && !FunctionsDisabled;

            if (useFunctions)
            {
                return await ProcessFunctionResponseAsync(responseContent, policy);
            }
            else
            {
                return ProcessRegularResponseAsync(responseContent);
            }
        }


        /// <summary>
        /// API 요청 전송
        /// </summary>
        private async Task<HttpResponseMessage> SendApiRequestAsync(CancellationToken cancellationToken)
        {
            bool useFunctions = ActivateChat.Functions?.Count > 0
                               && ActivateChat.EnableFunctions
                               && !FunctionsDisabled;

            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
            }

            return response;
        }

        /// <summary>
        /// Function 응답 처리
        /// </summary>
        private async Task<RoundResult> ProcessFunctionResponseAsync(
            string responseContent,
            FunctionCallingPolicy policy)
        {
            var (content, functionCall) = ExtractFunctionCall(responseContent);

            // Function 호출이 있는 경우
            if (functionCall != null)
            {
                if (policy.EnableLogging)
                {
                    Console.WriteLine($"  Executing function: {functionCall.Name}");
                }

                await ExecuteFunctionAsync(functionCall);

                // 다음 라운드 필요
                return RoundResult.Continue();
            }

            // Function 호출 없이 최종 응답이 온 경우
            if (!string.IsNullOrEmpty(content))
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
                return RoundResult.Complete(content);
            }

            // 응답이 비어있는 경우 (다음 라운드 시도)
            return RoundResult.Continue();
        }

        /// <summary>
        /// 일반 응답 처리 (Function 없음)
        /// </summary>
        private RoundResult ProcessRegularResponseAsync(string responseContent)
        {
            var result = ExtractResponseContent(responseContent);

            if (!string.IsNullOrEmpty(result))
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
                return RoundResult.Complete(result);
            }

            return RoundResult.Continue();
        }

        /// <summary>
        /// Function 실행 및 결과 저장
        /// </summary>
        private async Task ExecuteFunctionAsync(FunctionCall functionCall)
        {
            var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);

            ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
            {
                Metadata = new Dictionary<string, object>
                {
                    ["function_name"] = functionCall.Name,
                    ["arguments"] = functionCall.Arguments
                }
            });
        }


        private async Task<string> ProcessStatelessRequestAsync(Message message, bool useFunctions)
        {
            var tempChat = new ChatBlock(ActivateChat.Model)
            {
                SystemMessage = ActivateChat.SystemMessage,
                Temperature = ActivateChat.Temperature,
                TopP = ActivateChat.TopP,
                MaxTokens = ActivateChat.MaxTokens,
                Functions = useFunctions ? ActivateChat.Functions : new List<FunctionDefinition>(),
                EnableFunctions = useFunctions
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;

            try
            {
                var request = useFunctions
                    ? CreateFunctionMessageRequest()
                    : CreateMessageRequest();

                var response = await HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (useFunctions)
                    {
                        var (content, functionCall) = ExtractFunctionCall(responseContent);
                        if (functionCall != null)
                        {
                            var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);
                            return $"Function result: {result}";
                        }
                        return content;
                    }

                    return ExtractResponseContent(responseContent);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        #endregion

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            // GPT-5 모델 체크
            bool isGpt5Model = ActivateChat.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

            if (isGpt5Model)
            {
                return CreateGpt5Request();
            }

            // 기존 Chat Completions API (GPT-4, GPT-3.5 등)
            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private HttpRequestMessage CreateGpt5Request()
        {
            var requestBody = BuildGpt5ResponsesBody();
            var jsonString = JsonSerializer.Serialize(requestBody);

            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            // GPT-5는 /v1/responses 엔드포인트 사용
            var endpoint = ActivateChat.Stream ? "responses?stream=true" : "responses";
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        #endregion

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");

            var allMessagesBuilder = new StringBuilder();

            // Add system message
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                allMessagesBuilder.Append(ActivateChat.SystemMessage).Append('\n');
            }

            // Add all messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.HasMultimodalContent)
                {
                    foreach (var content in message.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            allMessagesBuilder.Append(textContent.Text).Append('\n');
                        }
                        else if (content is ImageContent)
                        {
                            // Images consume fixed tokens based on detail level
                            allMessagesBuilder.Append("[IMAGE]").Append('\n');
                        }
                    }
                }
                else
                {
                    allMessagesBuilder.Append(message.Role).Append('\n')
                                      .Append(message.Content).Append('\n');
                }
            }

            var textTokens = (uint)encoding.Encode(allMessagesBuilder.ToString()).Count;

            // Add image tokens
            var imageTokens = ActivateChat.Messages
                .SelectMany(m => m.Contents)
                .OfType<ImageContent>()
                .Sum(img => img.EstimateTokens());

            return await Task.FromResult(textTokens + (uint)imageTokens);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");
            return await Task.FromResult((uint)encoding.Encode(prompt).Count);
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            var currentModel = ActivateChat.Model;

            // Check if current model supports vision
            bool supportsVision = currentModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.Contains("gpt-4o") ||
                                 currentModel.Contains("gpt-4-turbo") ||
                                 currentModel.Contains("vision");

            if (!supportsVision)
            {
                // Switch to a vision-capable model
                if (currentModel.Contains("mini"))
                {
                    // If using mini model, switch to full gpt-4o
                    ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                }
                else
                {
                    // For other models, switch to gpt-4o
                    ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                }

                Console.WriteLine($"[GetCompletionWithImageAsync] Switched from {currentModel} to {ActivateChat.Model} for vision support");
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        #endregion

        #region OpenAI-Specific Features

        /// <summary>
        /// Fine-tunes the response with specific OpenAI parameters
        /// </summary>
        public ChatGptService WithOpenAIParameters(float? presencePenalty = null, float? frequencyPenalty = null, int? bestOf = null)
        {
            if (presencePenalty.HasValue)
            {
                ActivateChat.PresencePenalty = presencePenalty.Value;
            }
            if (frequencyPenalty.HasValue)
            {
                ActivateChat.FrequencyPenalty = frequencyPenalty.Value;
            }
            return this;
        }

        /// <summary>
        /// GPT-5 전용 파라미터 설정
        /// </summary>
        public ChatGptService WithGpt5Parameters(string reasoningEffort = "medium", string verbosity = "medium")
        {
            // ChatBlock을 확장하거나 별도 속성으로 관리 필요
            // 현재는 메타데이터로 저장
            if (ActivateChat.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: ChatBlock에 GPT-5 전용 속성 추가 시 구현
                Console.WriteLine($"[GPT-5 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}");
            }
            return this;
        }

        #endregion
    }
}