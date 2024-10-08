using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public class ClaudeService : AIService
    {
        public ClaudeService(string apiKey, HttpClient httpClient) : base(apiKey, "https://api.anthropic.com/v1/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Claude3_5Sonnet);
            chatBlock.MaxTokens = 8192;

            AddNewChat(chatBlock);
        }


        protected override string StreamParseJson(string jsonData)
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
            if (jsonElement.TryGetProperty("delta", out JsonElement deltaElement) &&
                deltaElement.TryGetProperty("text", out JsonElement textElement))
            {
                return textElement.GetString();
            }

            return string.Empty;
        }

        protected override HttpRequestMessage CreateRequest()
        {
            // 요청 바디 생성
            var requestBody = ActivateChat.ToClaudeRequestBody();

            // HttpRequestMessage를 생성하고 메서드와 엔드포인트 설정
            var request = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            // 헤더 추가
            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            return request;
        }


        public override async Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new NotSupportedException();
        }

        public override async Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new NotSupportedException();
        }



        protected override string ExtractResponseContent(string responseContent)
        {
            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseObj.GetProperty("content")[0].GetProperty("text").GetString();
        }
    }

}
