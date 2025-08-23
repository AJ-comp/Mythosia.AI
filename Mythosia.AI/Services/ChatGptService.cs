using Mythosia.AI.Builders;
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
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services
{
    public class ChatGptService : AIService
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

        /// <summary>
        /// GPT-5 전용 Responses API 요청 생성
        /// </summary>
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

        #region Request Body Building

        /// <summary>
        /// 기존 Chat Completions API 요청 바디 (GPT-4, GPT-3.5 등)
        /// </summary>
        private object BuildRequestBody()
        {
            var messagesList = new List<object>();

            // Add system message if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

            // Add conversation messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForOpenAI(message));
            }

            return new
            {
                model = ActivateChat.Model,
                messages = messagesList,
                top_p = ActivateChat.TopP,
                temperature = ActivateChat.Temperature,
                frequency_penalty = ActivateChat.FrequencyPenalty,
                presence_penalty = ActivateChat.PresencePenalty,
                max_tokens = ActivateChat.MaxTokens,
                stream = ActivateChat.Stream
            };
        }

        /// <summary>
        /// GPT-5 Responses API 요청 바디
        /// </summary>
        private object BuildGpt5ResponsesBody()
        {
            var inputList = new List<object>();

            // System message를 instructions로 처리
            string? instructions = !string.IsNullOrEmpty(ActivateChat.SystemMessage)
                ? ActivateChat.SystemMessage
                : null;

            // 대화 메시지들을 input 배열로 변환
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                var messageParts = new List<object>();

                if (!message.HasMultimodalContent)
                {
                    // Role에 따라 다른 type 사용
                    string textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";

                    messageParts.Add(new
                    {
                        type = textType,  // assistant면 output_text, user면 input_text
                        text = message.Content ?? string.Empty
                    });
                }
                else
                {
                    // 멀티모달 메시지
                    foreach (var content in message.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            // Role에 따라 다른 type 사용
                            string textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";

                            messageParts.Add(new
                            {
                                type = textType,
                                text = textContent.Text ?? string.Empty
                            });
                        }
                        else if (content is ImageContent imageContent)
                        {
                            // 이미지는 user 메시지에만 있을 것
                            messageParts.Add(new
                            {
                                type = "input_image",
                                image_url = imageContent.GetBase64Url()
                            });
                        }
                    }
                }

                // role과 content를 포함한 메시지 객체
                inputList.Add(new
                {
                    role = message.Role.ToDescription(),
                    content = messageParts
                });
            }

            // GPT-5 전용 파라미터
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["input"] = inputList,
                ["text"] = new { verbosity = "medium" },
                ["reasoning"] = new { effort = "medium" },
                ["max_output_tokens"] = (int)ActivateChat.MaxTokens
            };

            if (!string.IsNullOrEmpty(instructions))
            {
                requestBody["instructions"] = instructions;
            }

            if (ActivateChat.Stream)
            {
                requestBody["stream"] = true;
            }

            return requestBody;
        }

        /// <summary>
        /// 기존 Chat Completions 메시지 변환
        /// </summary>
        private object ConvertMessageForOpenAI(Message message)
        {
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // Handle multimodal content
            var contentList = new List<object>();
            foreach (var content in message.Contents)
            {
                contentList.Add(content.ToRequestFormat(Provider));
            }

            return new
            {
                role = message.Role.ToDescription(),
                content = contentList
            };
        }

        #endregion

        #region Response Parsing

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                // 모델에 따라 완전히 다른 파싱 함수 호출
                bool isGpt5Model = ActivateChat.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

                if (isGpt5Model)
                {
                    return ExtractGpt5Response(responseContent);
                }
                else
                {
                    return ExtractChatCompletionsResponse(responseContent);
                }
            }
            catch (Exception ex)
            {
                throw new AIServiceException($"Failed to parse OpenAI response: {ex.Message}", responseContent);
            }
        }

        /// <summary>
        /// GPT-5 전용 응답 파싱 - 완전히 독립적
        /// </summary>
        private string ExtractGpt5Response(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                // 1. output 배열 가져오기
                if (!root.TryGetProperty("output", out var outputArray))
                {
                    throw new AIServiceException("GPT-5 response missing 'output' field");
                }

                // 2. output 배열 순회
                foreach (var outputItem in outputArray.EnumerateArray())
                {
                    // 3. type이 "message"인 항목 찾기
                    if (!outputItem.TryGetProperty("type", out var typeProp))
                        continue;

                    if (typeProp.GetString() != "message")
                        continue;

                    // 4. content 배열 가져오기
                    if (!outputItem.TryGetProperty("content", out var contentArray))
                        continue;

                    // 5. content 배열 순회
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        // 6. type이 "output_text"인 항목 찾기
                        if (!contentItem.TryGetProperty("type", out var contentTypeProp))
                            continue;

                        if (contentTypeProp.GetString() != "output_text")
                            continue;

                        // 7. text 필드 추출
                        if (contentItem.TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString() ?? string.Empty;
                        }
                    }
                }

                throw new AIServiceException("No message text found in GPT-5 response");
            }
            catch (JsonException ex)
            {
                throw new AIServiceException($"GPT-5 JSON parsing error: {ex.Message}", responseContent);
            }
        }

        /// <summary>
        /// 기존 Chat Completions API 응답 파싱 - 완전히 독립적
        /// </summary>
        private string ExtractChatCompletionsResponse(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                // choices[0].message.content 경로로 직접 접근
                var choices = root.GetProperty("choices");
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                var content = message.GetProperty("content");

                return content.GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException($"Chat Completions parsing error: {ex.Message}", responseContent);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                bool isGpt5Model = ActivateChat.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

                if (isGpt5Model)
                {
                    return StreamParseGpt5(jsonData);
                }
                else
                {
                    return StreamParseChatCompletions(jsonData);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// GPT-5 스트리밍 전용 파싱
        /// </summary>
        private string StreamParseGpt5(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                // 스트리밍 델타 형식
                if (root.TryGetProperty("type", out var typeProp))
                {
                    var typeStr = typeProp.GetString();

                    // content_delta 타입
                    if (typeStr == "content_delta" &&
                        root.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var deltaText))
                    {
                        return deltaText.GetString() ?? string.Empty;
                    }

                    // 직접 text 스트리밍
                    if (typeStr == "output_text" &&
                        root.TryGetProperty("text", out var directText))
                    {
                        return directText.GetString() ?? string.Empty;
                    }
                }

                // 일반 응답 형식 (스트리밍이 아닌 경우)
                if (root.TryGetProperty("output", out var outputArray))
                {
                    foreach (var item in outputArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var itemType) &&
                            itemType.GetString() == "message" &&
                            item.TryGetProperty("content", out var contentArray))
                        {
                            foreach (var contentItem in contentArray.EnumerateArray())
                            {
                                if (contentItem.TryGetProperty("type", out var contentType) &&
                                    contentType.GetString() == "output_text" &&
                                    contentItem.TryGetProperty("text", out var textProp))
                                {
                                    return textProp.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 기존 Chat Completions 스트리밍 전용 파싱
        /// </summary>
        private string StreamParseChatCompletions(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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

        #region Image Generation

        public override async Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "b64_json"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageData = responseJson.GetProperty("data")[0].GetProperty("b64_json").GetString();

                if (string.IsNullOrEmpty(imageData))
                {
                    throw new AIServiceException("Image generation returned empty data");
                }

                return Convert.FromBase64String(imageData);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Image generation failed: {response.ReasonPhrase}", error);
            }
        }

        public override async Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "url"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageUrl = responseJson.GetProperty("data")[0].GetProperty("url").GetString();

                return imageUrl ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Image generation failed: {response.ReasonPhrase}", error);
            }
        }

        #endregion

        #region Audio Features

        /// <summary>
        /// Generates speech audio from text using OpenAI's TTS model
        /// </summary>
        public async Task<byte[]> GetSpeechAsync(string inputText, string voice = "alloy", string model = "tts-1")
        {
            var requestBody = new
            {
                model = model,
                voice = voice,
                input = inputText
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Speech generation failed: {response.ReasonPhrase}", error);
            }
        }

        /// <summary>
        /// Transcribes audio to text using OpenAI's Whisper model
        /// </summary>
        public async Task<string> TranscribeAudioAsync(byte[] audioData, string fileName, string? language = null)
        {
            using var form = new MultipartFormDataContent();

            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
            form.Add(audioContent, "file", fileName);
            form.Add(new StringContent("whisper-1"), "model");

            if (!string.IsNullOrEmpty(language))
            {
                form.Add(new StringContent(language), "language");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
            {
                Content = form
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                return responseJson.GetProperty("text").GetString() ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Audio transcription failed: {response.ReasonPhrase}", error);
            }
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


        #region Function Calling Support
        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var requestBody = BuildRequestBodyWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private object BuildRequestBodyWithFunctions()
        {
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.Role == ActorRole.Function)
                {
                    messagesList.Add(new
                    {
                        role = "function",
                        name = message.Metadata?["function_name"]?.ToString() ?? "function",
                        content = message.Content
                    });
                }
                else
                {
                    messagesList.Add(ConvertMessageForOpenAI(message));
                }
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["messages"] = messagesList,
                ["temperature"] = ActivateChat.Temperature,
                ["max_tokens"] = ActivateChat.MaxTokens,
                ["stream"] = false
            };

            // Add function definitions
            if (ActivateChat.ShouldUseFunctions)
            {
                requestBody["functions"] = ActivateChat.Functions.Select(f => new
                {
                    name = f.Name,
                    description = f.Description,
                    parameters = f.Parameters
                }).ToList();

                // Set function call mode
                requestBody["function_call"] = ActivateChat.FunctionCallMode switch
                {
                    FunctionCallMode.None => "none",
                    FunctionCallMode.Force => new { name = ActivateChat.ForceFunctionName },
                    _ => "auto"
                };
            }

            return requestBody;
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    return (string.Empty, null);
                }

                var choice = choices[0];
                if (!choice.TryGetProperty("message", out var message))
                {
                    return (string.Empty, null);
                }

                string content = null;
                FunctionCall functionCall = null;

                // Check for content
                if (message.TryGetProperty("content", out var contentElement))
                {
                    content = contentElement.GetString();
                }

                // Check for function call
                if (message.TryGetProperty("function_call", out var functionCallElement))
                {
                    functionCall = new FunctionCall
                    {
                        Name = functionCallElement.GetProperty("name").GetString(),
                        Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            functionCallElement.GetProperty("arguments").GetString())
                    };
                }

                return (content ?? string.Empty, functionCall);
            }
            catch (Exception ex)
            {
                // If parsing fails, return empty function call
                return (string.Empty, null);
            }
        }
        #endregion

        #region GPT-5 Response Helpers

        /// <summary>
        /// GPT-5 응답에서 추론 정보도 함께 추출 (선택사항)
        /// </summary>
        public class Gpt5FullResponse
        {
            public string Message { get; set; } = string.Empty;
            public string? ReasoningId { get; set; }
            public string? MessageId { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        public Gpt5FullResponse? ExtractGpt5FullResponse(string responseContent)
        {
            try
            {
                var result = new Gpt5FullResponse();
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (responseObj.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in responseObj.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeProp))
                        {
                            var type = typeProp.GetString();

                            if (type == "reasoning" && item.TryGetProperty("id", out var reasoningId))
                            {
                                result.ReasoningId = reasoningId.GetString();
                            }
                            else if (type == "message")
                            {
                                if (item.TryGetProperty("id", out var msgId))
                                    result.MessageId = msgId.GetString();

                                if (item.TryGetProperty("status", out var status))
                                    result.Status = status.GetString() ?? string.Empty;

                                if (item.TryGetProperty("content", out var contentArray))
                                {
                                    foreach (var contentItem in contentArray.EnumerateArray())
                                    {
                                        if (contentItem.TryGetProperty("type", out var contentType) &&
                                            contentType.GetString() == "output_text" &&
                                            contentItem.TryGetProperty("text", out var textProp))
                                        {
                                            result.Message = textProp.GetString() ?? string.Empty;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    return result;
                }
            }
            catch
            {
                // Silent fail
            }

            return null;
        }

        #endregion
    }
}