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
                ["model"] = $"models/{RequestModel}",
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
            var endpoint = $"v1beta/models/{RequestModel}:countTokens";

            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            using var request = CreateGoogleRequest(HttpMethod.Post, endpoint, content);
            var policy = GetExecutionPolicy();
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var timeoutSource = CreateRequestTimeoutCts(policy);

            string responseString;
            try
            {
                responseString = await SendAndReadAsync(request, timeoutSource.Token);
            }
            catch (TaskCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested &&
                exception.InnerException is TimeoutException)
            {
                throw new AIServiceException("The HTTP token-count request timed out.", exception);
            }
            catch (OperationCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
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
