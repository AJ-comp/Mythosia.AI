using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public enum AIModel
    {
        [Description("claude-3-5-sonnet-20240620")]
        Claude3_5Sonnet,

        [Description("claude-3-opus-20240229")]
        Claude3Opus,

        [Description("claude-3-haiku-20240307")]
        Claude3Haiku,

        [Description("gpt-3.5-turbo-1106")]
        Gpt3_5Turbo,

        [Description("gpt-4-0613")]
        Gpt4,

        [Description("gpt-4-1106-preview")]
        Gpt4Turbo,

        [Description("gpt-4o-2024-08-06")]
        Gpt4o,

        [Description("gpt-4o-mini-2024-07-18")]
        Gpt4oMini
    }


    public class ChatRequest
    {
        public AIModel Model { get; set; }
        public string SystemMessage { get; set; } = string.Empty;
        public IList<Message> Messages { get; set; } = new List<Message>();
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public bool Stream { get; set; }


        public object ToChatGptRequestBody()
        {
            var messagesList = new List<object>();

            // Add the system message if it's not empty
            if (!string.IsNullOrEmpty(SystemMessage))
            {
                messagesList.Add(new { role = "system", content = SystemMessage });
            }

            // Add user messages
            foreach (var message in Messages)
            {
                messagesList.Add(new { role = message.Role, content = message.Content });
            }

            var requestBody = new
            {
                model = Model.ToDescription(),
                messages = messagesList.ToArray(),
                temperature = Temperature,
                max_tokens = MaxTokens,
                stream = Stream
            };

            return requestBody;
        }


        public object ToClaudeRequestBody()
        {
            var messagesList = new List<object>();

            // Add user messages
            foreach (var message in Messages)
            {
                messagesList.Add(new { role = message.Role, content = message.Content });
            }

            var requestBody = new
            {
                model = Model.ToDescription(),
                system = SystemMessage,
                messages = messagesList.ToArray(),
                temperature = Temperature,
                stream = Stream,
                max_tokens = MaxTokens
            };

            return requestBody;
        }
    }

    public class Message
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }



    public abstract class AIService
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;
        public AIModel Model { get; set; }

        public string SystemMessage { get; set; } = string.Empty;

        public float TopP { get; set; } = 1.0f;
        public float Temperature { get; set; } = 0.7f;
        public float FrequencyPenalty { get; set; } = 0.0f;
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
    }
}
