using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        protected override HttpRequestMessage CreateFunctionMessageRequest() => CreateAgentRequest(true);
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            var parsed = ParseAgentResponse(response);
            return (parsed.Text, parsed.Calls);
        }

        private static Dictionary<string, object> BuildAgentFunctionTool(FunctionDefinition function)
            => new Dictionary<string, object>
            {
                ["type"] = "function", ["name"] = function.Name, ["description"] = function.Description,
                ["parameters"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = function.Parameters.Properties.ToDictionary(pair => pair.Key, pair => AgentParameter(pair.Value)),
                    ["required"] = function.Parameters.Required
                },
                // Preserve optionality and nested object semantics of the common schema.
                ["strict"] = false
            };

        private static Dictionary<string, object> AgentParameter(ParameterProperty property)
        {
            var result = new Dictionary<string, object> { ["type"] = string.IsNullOrWhiteSpace(property.Type) ? "string" : property.Type };
            if (property.Description != null) result["description"] = property.Description;
            if (property.Default != null) result["default"] = property.Default;
            if (property.Enum != null) result["enum"] = property.Enum;
            if (property.Items != null) result["items"] = AgentParameter(property.Items);
            return result;
        }

        private void ValidateAgentFunctionBatch(FunctionCallBatch calls, bool useFunctions)
        {
            if (calls.Calls.Count == 0) return;
            if (!useFunctions || RequestFunctionCallMode == FunctionCallMode.None)
                throw new AIServiceException("Perplexity returned custom function calls when client function execution was disabled.");
            var ids = new HashSet<string>(ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
                .SelectMany(message => message.FunctionCallBatch!.Calls).Select(call => call.Id), StringComparer.Ordinal);
            foreach (var call in calls.Calls)
            {
                if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name) || !ids.Add(call.Id))
                    throw new AIServiceException("Perplexity returned a missing or reused function-call ID/name.");
            }
        }
    }
}
