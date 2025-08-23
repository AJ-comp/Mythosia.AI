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
        #region Audio Features

        /// <summary>
        /// Generates speech audio from text using OpenAI's TTS model
        /// </summary>
        public async Task<byte[]> GetSpeechAsync(string inputText, string voice = "alloy", string model = "tts-1")
        {
            var requestBody = new
            {
                model = model,
                voice = voice,
                input = inputText
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Speech generation failed: {response.ReasonPhrase}", error);
            }
        }

        /// <summary>
        /// Transcribes audio to text using OpenAI's Whisper model
        /// </summary>
        public async Task<string> TranscribeAudioAsync(byte[] audioData, string fileName, string? language = null)
        {
            using var form = new MultipartFormDataContent();

            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
            form.Add(audioContent, "file", fileName);
            form.Add(new StringContent("whisper-1"), "model");

            if (!string.IsNullOrEmpty(language))
            {
                form.Add(new StringContent(language), "language");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
            {
                Content = form
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                return responseJson.GetProperty("text").GetString() ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Audio transcription failed: {response.ReasonPhrase}", error);
            }
        }

        #endregion
    }
}