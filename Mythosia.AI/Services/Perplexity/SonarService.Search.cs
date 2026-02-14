using Mythosia.AI.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class SonarService
    {
        #region Search Features

        /// <summary>
        /// Gets a completion with web search and citations
        /// </summary>
        public async Task<SonarSearchResponse> GetCompletionWithSearchAsync(
            string prompt,
            string[]? domainFilter = null,
            string recencyFilter = "month")
        {
            // Create a temporary chat block with search parameters
            var originalModel = Model;
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                messagesList.Add(new { role = "system", content = SystemMessage });
            }

            messagesList.Add(new { role = "user", content = prompt });

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = originalModel,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["max_tokens"] = GetEffectiveMaxTokens(),
                ["return_citations"] = true,
                ["search_recency_filter"] = recencyFilter
            };

            if (domainFilter != null && domainFilter.Length > 0)
            {
                requestBody["search_domain_filter"] = domainFilter;
            }

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return ParseSearchResponse(responseContent);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Search request failed: {response.ReasonPhrase}", error);
            }
        }

        #endregion

        #region Helper Classes

        public class SonarSearchResponse
        {
            public string Content { get; set; } = string.Empty;
            public List<Citation> Citations { get; set; } = new List<Citation>();
        }

        public class Citation
        {
            public string Url { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Snippet { get; set; } = string.Empty;
        }

        #endregion
    }
}