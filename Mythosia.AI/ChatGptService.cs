using Azure.Core;
using OpenAI.Chat;
using SharpToken;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public class ChatGptService : AIService
    {
        public ChatGptService(string apiKey, HttpClient httpClient) : base(apiKey, "https://api.openai.com/v1/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Gpt4oMini);
            chatBlock.MaxTokens = 16000;

            AddNewChat(chatBlock);
        }


        protected override string StreamParseJson(string jsonData)
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
            if (jsonElement
                .GetProperty("choices")[0]
                .GetProperty("delta")
                .TryGetProperty("content", out JsonElement contentElement))
            {
                return contentElement.GetString();
            }

            return string.Empty;
        }


        protected override HttpRequestMessage CreateMessageRequest()
        {
            // 요청 바디 생성
            var requestBody = ActivateChat.ToChatGptRequestBody();

            // HttpContent 생성 및 Content-Type 헤더 설정
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // HttpRequestMessage를 생성하고 엔드포인트 및 메서드 설정
            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            // Authorization 헤더는 HttpRequestMessage의 Headers에 설정
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            return request;
        }


        protected override string ExtractResponseContent(string responseContent)
        {
            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseObj.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }



        public async Task<byte[]> GetSpeechAsync(string inputText, string voice)
        {
            // 요청 바디 생성
            var requestBody = new
            {
                model = "tts-1",
                voice = voice,
                input = inputText
            };

            // HttpContent 생성 및 Content-Type 헤더 설정
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // HttpRequestMessage 생성
            var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
            {
                Content = content
            };

            // Authorization 헤더 설정
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            // 요청 전송
            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                // 응답 내용을 바이트 배열로 읽어들임
                var audioData = await response.Content.ReadAsByteArrayAsync();
                return audioData;
            }
            else
            {
                throw new Exception($"API 요청 실패: {response.ReasonPhrase}");
            }
        }




        public override async Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            // 요청 바디 생성
            var requestBody = new
            {
                model = "dall-e-3", // DALL·E 3 모델을 명시적으로 지정
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "b64_json"
            };

            // HttpContent 생성 및 Content-Type 헤더 설정
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // HttpRequestMessage 생성
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };

            // Authorization 헤더 설정
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            // 요청 전송
            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();

                // 응답 파싱
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // 이미지 데이터 추출
                var imageData = responseJson.GetProperty("data")[0].GetProperty("b64_json").GetString();

                // Base64 문자열을 byte[]로 변환
                return Convert.FromBase64String(imageData);
            }
            else
            {
                throw new Exception($"API 요청 실패: {response.ReasonPhrase}");
            }
        }



        /// <summary>
        /// 텍스트 프롬프트를 기반으로 이미지를 생성하고, 생성된 이미지의 URL을 반환합니다.
        /// </summary>
        /// <param name="prompt">이미지 생성에 사용할 텍스트 프롬프트</param>
        /// <param name="size">이미지 크기 (예: "1024x1024")</param>
        /// <returns>생성된 이미지의 URL</returns>
        public override async Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3", // DALL·E 3 모델을 명시적으로 지정
                prompt = prompt,
                n = 1, // 생성할 이미지 수를 1로 고정
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

                // 첫 번째 이미지의 URL 추출
                var imageUrl = responseJson.GetProperty("data")[0].GetProperty("url").GetString();

                return imageUrl;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"API 요청 실패: {response.ReasonPhrase}, 내용: {errorContent}");
            }
        }

        public async override Task<uint> GetInputTokenCountAsync()
        {
            var encoding = GptEncoding.GetEncodingForModel("gpt-4o");

            var allMessagesBuilder = new StringBuilder(ActivateChat.SystemMessage).Append('\n');
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                allMessagesBuilder.Append(message.Role).Append('\n')
                                  .Append(message.Content).Append('\n');
            }

            var allMessages = allMessagesBuilder.ToString();
            return (uint)encoding.CountTokens(allMessages);
        }


        public async override Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = GptEncoding.GetEncodingForModel("gpt-4o");

            return (uint)encoding.CountTokens(prompt);
        }
    }
}
