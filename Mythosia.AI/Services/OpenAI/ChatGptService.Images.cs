using Mythosia.AI.Exceptions;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        #region Image Generation

        public override async Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "b64_json"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

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
                throw new AIServiceException($"Image generation failed: {response.ReasonPhrase}", error);
            }
        }

        public override async Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "url"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageUrl = responseJson.GetProperty("data")[0].GetProperty("url").GetString();

                return imageUrl ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Image generation failed: {response.ReasonPhrase}", error);
            }
        }

        #endregion
    }
}