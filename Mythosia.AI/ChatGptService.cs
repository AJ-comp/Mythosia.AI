using System.Net.Http;
using System.Text;
using System.Text.Json;

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


        protected override HttpRequestMessage CreateRequest()
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
    }
}
