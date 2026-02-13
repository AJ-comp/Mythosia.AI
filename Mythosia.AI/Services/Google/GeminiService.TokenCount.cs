using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokenSharp;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Messages;

namespace Mythosia.AI.Services.Google
{
    public partial class GeminiService
    {
        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var requestBody = BuildTokenCountRequestBody();
            return await GetTokenCountFromAPI(requestBody);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                }
            };

            return await GetTokenCountFromAPI(requestBody);
        }

        private object BuildTokenCountRequestBody()
        {
            var contentsList = new List<object>();

            foreach (var message in GetLatestMessages())
            {
                contentsList.Add(ConvertMessageForGemini(message));
            }

            var generateContentRequest = new Dictionary<string, object>
            {
                ["model"] = $"models/{Model}",
                ["contents"] = contentsList
            };

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                generateContentRequest["systemInstruction"] = new
                {
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                };
            }

            return new Dictionary<string, object>
            {
                ["generateContentRequest"] = generateContentRequest
            };
        }

        private async Task<uint> GetTokenCountFromAPI(object requestBody)
        {
            var modelName = Model;
            var endpoint = $"v1beta/models/{modelName}:countTokens?key={ApiKey}";

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Gemini token count request failed: {response.ReasonPhrase}", errorMsg);
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
    }
}