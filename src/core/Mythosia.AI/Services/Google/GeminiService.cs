using System.Net.Http;

namespace Mythosia.AI.Services.Google
{
    /// <summary>
    /// Obsolete: renamed to <see cref="GoogleAIService"/>.
    /// </summary>
    [System.Obsolete("GeminiService has been renamed to GoogleAIService. Please update your code.")]
    public class GeminiService : GoogleAIService
    {
        public GeminiService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient) { }

        public GeminiService(string apiKey, string model, HttpClient httpClient)
            : base(apiKey, model, httpClient) { }
    }
}
