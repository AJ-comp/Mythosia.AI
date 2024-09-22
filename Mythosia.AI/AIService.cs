using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public enum AIModel
    {
        Claude3_5Sonnet,
        Claude3Opus,
        Claude3Haiku,
        Gpt3_5Turbo,
        Gpt4,
        Gpt4o,
        Gpt4oMini,
        Gpt4Turbo
    }

    public abstract class AIService
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;
        public AIModel Model { get; set; }
        public float Temperature { get; set; } = 0.7f;
        public uint MaxTokens { get; set; } = 1024;

        protected AIService(string apiKey, string baseUrl, HttpClient httpClient)
        {
            ApiKey = apiKey;
            HttpClient = httpClient;
            httpClient.BaseAddress = new Uri(baseUrl); // BaseAddress 설정
        }

        public virtual async Task<string> GetCompletionAsync(string prompt)
        {
            // CreateRequest로 HttpRequestMessage 생성
            var request = CreateRequest(prompt, false);

            // HttpClient를 사용해 요청 전송
            var response = await HttpClient.SendAsync(request);

            // 요청 성공 여부 확인
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return ExtractResponseContent(responseContent); // 응답 내용 처리
            }
            else
            {
                throw new Exception($"API request failed: {response.ReasonPhrase}");
            }
        }

        protected abstract string StreamParseJson(string jsonData);

        public virtual async Task StreamCompletionAsync(string prompt, Action<string> MessageReceived)
        {
            var request = CreateRequest(prompt, true);
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API request failed: {response.ReasonPhrase}");

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]")
                    break;

                try
                {
                    var content = StreamParseJson(jsonData);
                    if (!string.IsNullOrEmpty(content))
                    {
                        MessageReceived(content);
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"JSON parsing error: {ex.Message}");
                }
            }
        }

        protected abstract HttpRequestMessage CreateRequest(string prompt, bool isStream);

        protected abstract string ExtractResponseContent(string responseContent);


        protected string GetModelString()
        {
            return Model switch
            {
                AIModel.Claude3_5Sonnet => "claude-3-5-sonnet-20240620",
                AIModel.Claude3Opus => "claude-3-opus-20240229",
                AIModel.Claude3Haiku => "claude-3-haiku-20240307",
                AIModel.Gpt3_5Turbo => "gpt-3.5-turbo-1106",
                AIModel.Gpt4 => "gpt-4-0613",
                AIModel.Gpt4Turbo => "gpt-4-1106-preview",
                AIModel.Gpt4o => "gpt-4o-2024-08-06",
                AIModel.Gpt4oMini => "gpt-4o-mini-2024-07-18",
                _ => throw new ArgumentException("not supported model", nameof(Model))
            };
        }
    }
}
