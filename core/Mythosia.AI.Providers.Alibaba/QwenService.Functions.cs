using Mythosia.AI.Models.Functions;
using System.Net.Http;

namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService
    {
        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var p = CreateRequestParams(GetLatestMessages(), forFunctionCalling: true);
            var body = _protocol.BuildFunctionRequestBody(p, Functions, FunctionCallMode);
            return _protocol.CreateFunctionRequest(ApiKey, body);
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            var (content, functionCall) = _protocol.ExtractFunctionCall(response);
            return (content, functionCall!);
        }
    }
}
