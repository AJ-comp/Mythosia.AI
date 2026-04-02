using System.Net.Http;

namespace Mythosia.AI.Services.Anthropic
{
    /// <summary>
    /// Obsolete: renamed to <see cref="AnthropicService"/>.
    /// </summary>
    [System.Obsolete("ClaudeService has been renamed to AnthropicService. Please update your code.")]
    public class ClaudeService : AnthropicService
    {
        public ClaudeService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient) { }

        public ClaudeService(string apiKey, string model, HttpClient httpClient)
            : base(apiKey, model, httpClient) { }
    }
}
