using System.Net.Http;

namespace Mythosia.AI.Services.Perplexity
{
    /// <summary>
    /// Obsolete: renamed to <see cref="PerplexityService"/>.
    /// </summary>
    [System.Obsolete("SonarService has been renamed to PerplexityService. Please update your code.")]
    public class SonarService : PerplexityService
    {
        public SonarService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient) { }

        public SonarService(string apiKey, string model, HttpClient httpClient)
            : base(apiKey, model, httpClient) { }
    }
}
