using Mythosia.AI.Exceptions;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        #region Image Generation

        public override async Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            // dall-e-3 was retired (2026-03-04). gpt-image models return base64 by default and
            // do NOT accept the legacy 'response_format' parameter.
            var requestBody = new
            {
                model = "gpt-image-1",
                prompt = prompt,
                n = 1,
                size = size
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            using var cts = CreateRequestTimeoutCts(CurrentPolicy ?? DefaultPolicy);
            var response = await HttpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageData = responseJson.GetProperty("data")[0].GetProperty("b64_json").GetString();

                if (string.IsNullOrEmpty(imageData))
                {
                    throw new AIServiceException("Image generation returned empty data");
                }

                return Convert.FromBase64String(imageData);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException(
                    $"Image generation failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? error : response.ReasonPhrase)}",
                    error);
            }
        }

        /// <summary>
        /// Generates an image and returns it as a usable image source string.
        /// gpt-image models do not provide hosted URLs (dall-e-3 was retired 2026-03-04), so this
        /// returns a base64 <c>data:</c> URI (e.g. usable directly in an &lt;img&gt; src).
        /// </summary>
        public override async Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "gpt-image-1",
                prompt = prompt,
                n = 1,
                size = size
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            using var cts = CreateRequestTimeoutCts(CurrentPolicy ?? DefaultPolicy);
            var response = await HttpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageBase64 = responseJson.GetProperty("data")[0].GetProperty("b64_json").GetString();

                return string.IsNullOrEmpty(imageBase64)
                    ? string.Empty
                    : $"data:image/png;base64,{imageBase64}";
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException(
                    $"Image generation failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? error : response.ReasonPhrase)}",
                    error);
            }
        }

        #endregion
    }
}