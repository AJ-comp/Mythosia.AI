using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService
    {
        #region Function Calling

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var messages = GetLatestMessages().ToList();
            var p = CreateRequestParams(messages);
            var body = (Dictionary<string, object>)_protocol.BuildFunctionRequestBody(
                p,
                RequestFunctions,
                RequestFunctionCallMode);

            if (GetModelFamily() == GrokModelFamily.Grok4_6)
            {
                body["max_tokens"] = (int)p.MaxTokens;
                body["top_p"] = p.TopP;
            }

            var lastMessage = messages.LastOrDefault();
            var isFunctionContinuation =
                lastMessage?.Role == ActorRole.Function ||
                lastMessage?.FunctionCallResultBatch != null ||
                lastMessage?.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() ==
                    "function_result";

            if (!isFunctionContinuation &&
                RequestFunctionCallMode != FunctionCallMode.None &&
                !string.IsNullOrWhiteSpace(RequestForceFunctionName))
            {
                body["tool_choice"] = new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = RequestForceFunctionName
                    }
                };
            }

            return _protocol.CreateFunctionRequest(ApiKey, body);
        }

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            return _protocol.ExtractFunctionCalls(response);
        }

        #endregion
    }
}
