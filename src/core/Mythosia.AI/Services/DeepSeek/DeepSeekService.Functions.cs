using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        protected override HttpRequestMessage CreateFunctionMessageRequest() => BuildDeepSeekRequest(true);

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            var result = _protocol.ExtractFunctionCalls(response);
            foreach (var call in result.functionCalls.Calls) call.Source = IdSource.DeepSeek;
            return result;
        }

        private void ValidateDeepSeekFunctionBatch(FunctionCallBatch batch)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var priorIds = new HashSet<string>(ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
                .SelectMany(message => message.FunctionCallBatch!.Calls).Select(call => call.Id), StringComparer.Ordinal);
            foreach (var call in batch.Calls)
            {
                if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name) ||
                    !ids.Add(call.Id) || priorIds.Contains(call.Id))
                    throw new AIServiceException("DeepSeek returned a missing, duplicate, or reused tool-call identity; no functions were executed.");
            }
        }
    }
}
