using Mythosia.AI.Models.Functions;
using System.Collections.Generic;
using System.Net.Http;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        #region Function Calling Support

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            // Perplexity Sonar doesn't support function calling yet
            // Return regular message request
            return CreateMessageRequest();
        }

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            // Perplexity doesn't support function calling
            // Extract regular response
            var content = ExtractResponseContent(response);
            return (content, new FunctionCallBatch());
        }

        #endregion
    }
}
