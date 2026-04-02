using System.Net.Http;

namespace Mythosia.AI.Services.xAI
{
    /// <summary>
    /// Obsolete: renamed to <see cref="XAIService"/>.
    /// </summary>
    [System.Obsolete("GrokService has been renamed to XAIService. Please update your code.")]
    public class GrokService : XAIService
    {
        public GrokService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient) { }

        public GrokService(string apiKey, string model, HttpClient httpClient)
            : base(apiKey, model, httpClient) { }
    }
}
