using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            ValidateClaudeRequestOptions(ClaudeOptions);
            using var attempt = new ClaudeReasoningAttempt(this);
            var requestBody = BuildTokenCountRequestBody();
            return await GetTokenCountFromAPI(requestBody);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            ValidateClaudeRequestOptions(ClaudeOptions);
            var messagesList = new List<object>
            {
                new { role = ActorRole.User.ToDescription(), content = prompt }
            };

            var requestBody = new
            {
                model = RequestModel,
                messages = messagesList
            };

            return await GetTokenCountFromAPI(requestBody);
        }

        private object BuildTokenCountRequestBody()
        {
            // Counting an existing conversation does not generate an assistant continuation.
            var messagesList = UsesClaudeWireHistory ? BuildPreservedClaudeMessages(validateGenerationPrefill: false) : new List<object>();

            foreach (var message in UsesClaudeWireHistory ? Array.Empty<Mythosia.AI.Models.Messages.Message>() : GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForClaude(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = RequestModel,
                ["messages"] = messagesList
            };

            ApplySystemMessage(requestBody);
            ApplyClaudeRequestOptions(requestBody);

            return requestBody;
        }

        private async Task<uint> GetTokenCountFromAPI(object requestBody)
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            RequestCancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Post, "messages/count_tokens")
            {
                Content = content
            };

            AddClaudeHeaders(request, "token-counting-2024-11-01");

            var policy = GetExecutionPolicy();
            using var timeout = CreateRequestTimeoutCts(policy);
            try
            {
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var jsonString = await ReadCompletionResponseBodyAsync(response, timeout.Token);

                if (!response.IsSuccessStatusCode)
                    throw new AIServiceException($"Token count request failed: {response.StatusCode}", jsonString);

                timeout.Token.ThrowIfCancellationRequested();
                var tokenResponse = JsonSerializer.Deserialize<TokenCountResponse>(jsonString);
                return tokenResponse?.InputTokens ?? 0;
            }
            catch (TaskCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested &&
                exception.InnerException is TimeoutException)
            {
                throw new AIServiceException("The HTTP token-count request timed out.", exception);
            }
            catch (OperationCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new AIServiceException($"Claude token-count request timeout after {ResolveRequestTimeoutSeconds(policy)} seconds", exception);
            }
        }

        #endregion
    }
}
