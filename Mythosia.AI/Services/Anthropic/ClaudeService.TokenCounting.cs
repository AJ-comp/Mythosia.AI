using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var requestBody = BuildTokenCountRequestBody();
            return await GetTokenCountFromAPI(requestBody);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var messagesList = new List<object>
            {
                new { role = ActorRole.User.ToDescription(), content = prompt }
            };

            var requestBody = new
            {
                model = ActivateChat.Model,
                messages = messagesList
            };

            return await GetTokenCountFromAPI(requestBody);
        }

        private object BuildTokenCountRequestBody()
        {
            var messagesList = new List<object>();

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForClaude(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["messages"] = messagesList
            };

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                requestBody["system"] = ActivateChat.SystemMessage;
            }

            return requestBody;
        }

        private async Task<uint> GetTokenCountFromAPI(object requestBody)
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "messages/count_tokens")
            {
                Content = content
            };

            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("anthropic-beta", "token-counting-2024-11-01");

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Token count request failed: {response.StatusCode}", errorJson);
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenCountResponse>(jsonString);

            return tokenResponse?.InputTokens ?? 0;
        }

        #endregion
    }
}