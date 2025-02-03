using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public class GeminiService : AIService
    {
        public GeminiService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://generativelanguage.googleapis.com/", httpClient)
        {
            // 기본 모델 설정(예: gemini-1.5-flash)
            var chatBlock = new ChatBlock(AIModel.Gemini15Flash)
            {
                Temperature = 1.0f,
                TopP = 0.8f,
                MaxTokens = 1024
            };
            AddNewChat(chatBlock);
        }

        #region Message Request (GenerateContent, StreamGenerateContent)

        /// <summary>
        /// Gemini API 호출에 사용될 HttpRequestMessage 생성.
        /// </summary>
        protected override HttpRequestMessage CreateMessageRequest()
        {
            // contents 배열 구성
            var contentsList = new List<object>();

            // (1) SystemMessage -> role=user
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = ActivateChat.SystemMessage }
                    }
                });
            }

            // (2) 유저/모델 메시지
            foreach (var msg in ActivateChat.GetLatestMessages())
            {
                // ActorRole.User => "user"
                // ActorRole.Assistant => "model"
                string roleString = msg.Role == ActorRole.User ? "user" : "model";
                contentsList.Add(new
                {
                    role = roleString,
                    parts = new[]
                    {
                        new { text = msg.Content }
                    }
                });
            }

            // generationConfig
            var generationConfig = new
            {
                temperature = ActivateChat.Temperature,
                topP = ActivateChat.TopP,
                topK = 40,
                maxOutputTokens = (int)ActivateChat.MaxTokens
            };

            var requestBody = new
            {
                contents = contentsList,
                generationConfig
            };

            // Endpoint 결정 (스트리밍 or 비스트리밍)
            // 예: gemini-1.5-flash
            string modelName = ActivateChat.Model;
            var endPoint = ActivateChat.Stream
                ? $"/v1beta/models/{modelName}:streamGenerateContent?alt=sse&key={ApiKey}"
                : $"/v1beta/models/{modelName}:generateContent?key={ApiKey}";

            var requestJson = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, endPoint)
            {
                Content = content
            };

            return request;
        }

        #endregion

        #region Extract or Parse

        /// <summary>
        /// (비스트리밍) 전체 응답 한 번에 받기
        /// </summary>
        protected override string ExtractResponseContent(string responseContent)
        {
            // 예: { "candidates": [ { "content": { "parts": [{ "text": "..."}], "role": "model" } } ] }
            //     "totalTokens": ... 등 추가 필드가 있을 수 있음

            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var partsArr) &&
                        partsArr.ValueKind == JsonValueKind.Array &&
                        partsArr.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in partsArr.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                sb.Append(textElem.GetString());
                            }
                        }
                        return sb.ToString();
                    }
                }
            }
            catch
            {
                // 무시
            }

            return string.Empty;
        }

        /// <summary>
        /// (스트리밍) SSE JSON 청크 해석
        /// </summary>
        protected override string StreamParseJson(string jsonData)
        {
            // SSE 데이터: data: { "candidates": [ { ... } ] }
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var partsArr) &&
                        partsArr.ValueKind == JsonValueKind.Array &&
                        partsArr.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in partsArr.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                sb.Append(textElem.GetString());
                            }
                        }
                        return sb.ToString();
                    }
                }
            }
            catch
            {
                // parse 실패 시 무시
            }
            return string.Empty;
        }

        #endregion

        #region CountTokens

        /// <summary>
        /// 대화 전체를 Gemini API의 countTokens로 계산
        /// </summary>
        public override async Task<uint> GetInputTokenCountAsync()
        {
            var requestBody = BuildCountTokensRequestBodyFromConversation();
            return await SendCountTokensRequestAsync(requestBody);
        }

        /// <summary>
        /// 단일 prompt 하나에 대한 토큰 수 계산
        /// </summary>
        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var requestBody = BuildCountTokensRequestBodyFromPrompt(prompt);
            return await SendCountTokensRequestAsync(requestBody);
        }

        private object BuildCountTokensRequestBodyFromConversation()
        {
            var contentsList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                });
            }

            foreach (var msg in ActivateChat.GetLatestMessages())
            {
                var roleString = (msg.Role == ActorRole.User) ? "user" : "model";
                contentsList.Add(new
                {
                    role = roleString,
                    parts = new[] { new { text = msg.Content } }
                });
            }

            return new { contents = contentsList };
        }

        private object BuildCountTokensRequestBodyFromPrompt(string prompt)
        {
            var contentsList = new List<object>
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            };

            return new { contents = contentsList };
        }

        /// <summary>
        /// 실제 countTokens HTTP 호출 + totalTokens 파싱
        /// </summary>
        private async Task<uint> SendCountTokensRequestAsync(object requestBody)
        {
            string modelName = ActivateChat.Model; // ex) "gemini-1.5-flash"
            var endPoint = $"/v1beta/models/{modelName}:countTokens?key={ApiKey}";

            var requestJson = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, endPoint)
            {
                Content = content
            };

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini countTokens 요청 실패: {response.ReasonPhrase}, detail: {errorMsg}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);

            if (doc.RootElement.TryGetProperty("totalTokens", out var totalTokensElem))
            {
                return (uint)totalTokensElem.GetInt32();
            }

            return 0;
        }

        #endregion

        #region NotSupported (Images, etc.)

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new NotSupportedException("Gemini API does not currently support direct image generation output.");
        }

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new NotSupportedException("Gemini API does not currently support direct image generation output.");
        }

        #endregion
    }
}
