using System.Collections.Generic;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
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
            var endpoint = $"v1beta/models/{Model}:countTokens";

            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            using var request = CreateGoogleRequest(HttpMethod.Post, endpoint, content);
            var policy = CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default;
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var timeoutSource = CreateRequestTimeoutCts(policy);

            string responseString;
            try
            {
                responseString = await SendAndReadAsync(request, timeoutSource.Token);
            }
            catch (OperationCanceledException exception)
            {
                throw new AIServiceException(
                    $"Gemini token-count request timeout after {timeoutSeconds} seconds",
                    exception);
            }

            using var doc = JsonDocument.Parse(responseString);

            if (!doc.RootElement.TryGetProperty("totalTokens", out var totalTokensElem))
                return 0;

            return (uint)totalTokensElem.GetInt32();
        }

        #endregion
    }
}
