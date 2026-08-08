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

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            return _protocol.ExtractFunctionCalls(response);
        }
    }
}
