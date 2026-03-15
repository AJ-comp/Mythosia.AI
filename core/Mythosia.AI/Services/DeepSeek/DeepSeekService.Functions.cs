using Mythosia.AI.Models.Functions;
using System.Collections.Generic;
using System.Net.Http;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        #region Function Calling

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            // DeepSeek doesn't support function calling yet
            // Return regular message request
            return CreateMessageRequest();
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            // DeepSeek doesn't support function calling
            // Extract regular response
            var content = ExtractResponseContent(response);
            return (content, null);
        }

        #endregion
    }
}