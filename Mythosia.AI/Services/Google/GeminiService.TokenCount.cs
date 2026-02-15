using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

            ApplySystemInstruction(generateContentRequest);

            return new Dictionary<string, object>
            {
                ["generateContentRequest"] = generateContentRequest
            };
        }

        private async Task<uint> GetTokenCountFromAPI(object requestBody)
        {
            var endpoint = $"v1beta/models/{Model}:countTokens?key={ApiKey}";

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            var responseString = await SendAndReadAsync(request);
            using var doc = JsonDocument.Parse(responseString);

            if (!doc.RootElement.TryGetProperty("totalTokens", out var totalTokensElem))
                return 0;

            return (uint)totalTokensElem.GetInt32();
        }

        #endregion
    }
}