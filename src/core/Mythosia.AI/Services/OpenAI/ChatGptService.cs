using System.Net.Http;

namespace Mythosia.AI.Services.OpenAI
{
    /// <summary>
    /// Obsolete: renamed to <see cref="OpenAIService"/>.
    /// </summary>
    [System.Obsolete("ChatGptService has been renamed to OpenAIService. Please update your code.")]
    public class ChatGptService : OpenAIService
    {
        public ChatGptService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient) { }

        public ChatGptService(string apiKey, string model, HttpClient httpClient)
            : base(apiKey, model, httpClient) { }
    }
}
