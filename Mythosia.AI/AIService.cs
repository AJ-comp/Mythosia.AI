using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public enum AIModel
    {
        [Description("claude-3-5-sonnet-20241022")]
        Claude3_5Sonnet,

        [Description("claude-3-5-haiku-20241022")]
        Claude3_5Haiku,

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


    public abstract class AIService
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;

        protected HashSet<ChatBlock> _chatRequests = new HashSet<ChatBlock>();

        public IReadOnlyCollection<ChatBlock> ChatRequests => _chatRequests;

        public ChatBlock ActivateChat { get; private set; }


        protected AIService(string apiKey, string baseUrl, HttpClient httpClient)
        {
            ApiKey = apiKey;
            HttpClient = httpClient;
            httpClient.BaseAddress = new Uri(baseUrl); // BaseAddress 설정
        }

        public void AddNewChat(ChatBlock newChat)
        {
            _chatRequests.Add(newChat);

            ActivateChat = newChat;
        }

        public void SetActivateChat(string chatBlockId)
        {
            // 선택된 ChatBlock을 _chatRequests에서 찾기
            var selectedChatBlock = _chatRequests.FirstOrDefault(chat => chat.Id == chatBlockId);

            // 선택된 ChatBlock이 null이 아니면 ActivateRequest로 설정
            if (selectedChatBlock != null) ActivateChat = selectedChatBlock;
        }




        public virtual async Task<string> GetCompletionAsync(string prompt)
        {
            ActivateChat.Stream = false;
            ActivateChat.Messages.Add(new Message(ActorRole.User, prompt));

            // CreateRequest로 HttpRequestMessage 생성
            var request = CreateMessageRequest();

            // HttpClient를 사용해 요청 전송
            var response = await HttpClient.SendAsync(request);

            // 요청 성공 여부 확인
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = ExtractResponseContent(responseContent);

                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, ExtractResponseContent(responseContent)));
                return result;
            }
            else
            {
                throw new Exception($"API request failed: {response.ReasonPhrase}");
            }
        }

        protected abstract string StreamParseJson(string jsonData);


        private async Task StreamCompletionAsyncInternal(string prompt, Func<string, Task> messageHandler)
        {
            ActivateChat.Stream = true;
            ActivateChat.Messages.Add(new Message(ActorRole.User, prompt));

            var request = CreateMessageRequest();
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API request failed: {response.ReasonPhrase}");

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            var allContent = new StringBuilder();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]") break;

                try
                {
                    var content = StreamParseJson(jsonData);

                    if (!string.IsNullOrEmpty(content))
                    {
                        allContent.Append(content);
                        await messageHandler(content);
                    }
                }
                catch (JsonException ex)
                {
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
                    throw ex;
                }
            }

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
        }


        public virtual async Task StreamCompletionAsync(string prompt, Action<string> MessageReceived)
        {
            // Action<string>을 Func<string, Task>로 변환
            Func<string, Task> messageHandler = content =>
            {
                MessageReceived(content);
                return Task.CompletedTask;
            };

            await StreamCompletionAsyncInternal(prompt, messageHandler);
        }


        public virtual async Task StreamCompletionAsync(string prompt, Func<string, Task> MessageReceivedAsync)
        {
            await StreamCompletionAsyncInternal(prompt, MessageReceivedAsync);
        }


        public abstract Task<uint> GetInputTokenCountAsync();


        public abstract Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024");
        public abstract Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024");


        protected abstract HttpRequestMessage CreateMessageRequest();

        protected abstract string ExtractResponseContent(string responseContent);
    }
}
