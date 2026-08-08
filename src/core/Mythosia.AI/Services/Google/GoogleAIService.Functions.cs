using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private const int DefaultTopK = 40;
        private const string GeminiResponsePartsMetadataKey = "gemini_response_parts";
        private const string GeminiPartIndexMetadataKey = "gemini_part_index";
        private const string GeminiProviderCallIdMetadataKey = "gemini_provider_call_id";

        #region Function Calling Support

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            return CreateFunctionMessageRequest(includeThoughts: false);
        }

        internal HttpRequestMessage CreateFunctionMessageRequest(bool includeThoughts)
        {
            var endpoint = Stream
                ? $"v1beta/models/{Model}:streamGenerateContent?alt=sse"
                : $"v1beta/models/{Model}:generateContent";

            var requestBody = BuildRequestBodyWithFunctions(includeThoughts);
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return CreateGoogleRequest(HttpMethod.Post, endpoint, content);
        }

        private object BuildRequestBodyWithFunctions(bool includeThoughts = false)
        {
            var contentsList = BuildFunctionContentsList();

            var generationConfig = new Dictionary<string, object>();
            ApplyTextGenerationConfig(
                generationConfig,
                includeCandidateCount: false,
                includeThoughts);

            var requestBody = new Dictionary<string, object>
            {
                ["contents"] = contentsList,
                ["generationConfig"] = generationConfig
            };

            ApplyFunctionDeclarations(requestBody);
            ApplySystemInstruction(requestBody);
            ApplySafetySettings(requestBody);

            return requestBody;
        }

        private List<object> BuildFunctionContentsList()
        {
            var contentsList = new List<object>();
            var messages = GetLatestMessages().ToList();
            EnsureUserFirstMessage(messages);

            foreach (var message in messages)
            {
                if (message.FunctionCallBatch != null || IsFunctionCallMessage(message))
                    contentsList.Add(BuildFunctionCallContent(message));
                else if (message.FunctionCallResultBatch != null || message.Role == ActorRole.Function)
                    contentsList.Add(BuildFunctionResponseContent(message));
                else
                    contentsList.Add(ConvertMessageForGemini(message));
            }

            return contentsList;
        }

        private static bool IsFunctionCallMessage(Models.Messages.Message message)
        {
            return message.Role == ActorRole.Assistant &&
                   message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call";
        }

        private static Dictionary<string, object> BuildFunctionCallContent(Models.Messages.Message message)
        {
            if (message.FunctionCallBatch != null)
                return BuildFunctionCallBatchContent(message);

            return BuildLegacyFunctionCallContent(message);
        }

        private static Dictionary<string, object> BuildFunctionCallBatchContent(Models.Messages.Message message)
        {
            var batch = message.FunctionCallBatch!;
            var parts = new List<object>();

            if (batch.Metadata?.TryGetValue(GeminiResponsePartsMetadataKey, out var responsePartsObject) == true &&
                responsePartsObject is IReadOnlyList<JsonElement> responseParts)
            {
                var nextCallIndex = 0;
                foreach (var responsePart in responseParts)
                {
                    if (responsePart.ValueKind == JsonValueKind.Object &&
                        responsePart.TryGetProperty("functionCall", out _))
                    {
                        if (nextCallIndex >= batch.Calls.Count)
                            throw new InvalidOperationException("Gemini continuation metadata contains more function-call parts than the batch.");

                        nextCallIndex++;
                    }

                    // Gemini thought signatures and future provider-owned fields belong to the
                    // original response part. Replaying the cloned part avoids silently dropping
                    // signed or newly introduced fields while reconstructing a continuation.
                    parts.Add(responsePart);
                }

                if (nextCallIndex != batch.Calls.Count)
                    throw new InvalidOperationException("Gemini continuation metadata does not contain every function call in the batch.");
            }
            else
            {
                if (!string.IsNullOrEmpty(message.Content))
                    parts.Add(new Dictionary<string, object> { ["text"] = message.Content });

                foreach (var functionCall in batch.Calls)
                    parts.Add(BuildFunctionCallPart(functionCall));
            }

            return new Dictionary<string, object>
            {
                ["role"] = "model",
                ["parts"] = parts
            };
        }

        private static Dictionary<string, object> BuildFunctionCallPart(FunctionCall functionCall)
        {
            var functionPayload = new Dictionary<string, object>
            {
                ["name"] = functionCall.Name ?? string.Empty,
                ["args"] = functionCall.Arguments ?? new Dictionary<string, object>()
            };
            if (!string.IsNullOrWhiteSpace(functionCall.Id))
                functionPayload["id"] = functionCall.Id;

            var part = new Dictionary<string, object>
            {
                ["functionCall"] = functionPayload
            };

            if (functionCall.Metadata?.TryGetValue(MessageMetadataKeys.ThoughtSignature, out var signatureObject) == true &&
                signatureObject != null)
            {
                part["thoughtSignature"] = signatureObject.ToString()!;
            }

            return part;
        }

        private static Dictionary<string, object> BuildLegacyFunctionCallContent(Models.Messages.Message message)
        {
            var metadata = message.Metadata
                ?? throw new InvalidOperationException("Legacy function-call messages require metadata.");
            var funcName = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "";
            var functionId = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
            var argsJson = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson) ?? new Dictionary<string, object>();

            var functionCall = new Dictionary<string, object>
            {
                ["name"] = funcName,
                ["args"] = args
            };
            if (!string.IsNullOrWhiteSpace(functionId))
                functionCall["id"] = functionId!;

            var functionCallPart = new Dictionary<string, object>
            {
                ["functionCall"] = functionCall
            };

            if (metadata.TryGetValue(MessageMetadataKeys.ThoughtSignature, out var sigObj) && sigObj != null)
            {
                functionCallPart["thoughtSignature"] = sigObj.ToString()!;
            }

            return new Dictionary<string, object>
            {
                ["role"] = "model",
                ["parts"] = new[] { functionCallPart }
            };
        }

        private static Dictionary<string, object> BuildFunctionResponseContent(Models.Messages.Message message)
        {
            if (message.FunctionCallResultBatch != null)
            {
                var resultParts = message.FunctionCallResultBatch.Results
                    .Select(result => (object)BuildFunctionResponsePart(result.Call, result.Content))
                    .ToList();

                return new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["parts"] = resultParts
                };
            }

            var functionId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString() ?? string.Empty;
            var functionName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function";

            return new Dictionary<string, object>
            {
                ["role"] = "user",
                ["parts"] = new[] { BuildFunctionResponsePart(functionId, functionName, message.Content) }
            };
        }

        private static Dictionary<string, object> BuildFunctionResponsePart(
            FunctionCall functionCall,
            string content)
        {
            var providerSuppliedId = true;
            if (functionCall.Metadata?.TryGetValue(
                    GeminiProviderCallIdMetadataKey,
                    out var providerIdObject) == true &&
                providerIdObject is bool hasProviderId)
            {
                providerSuppliedId = hasProviderId;
            }

            return BuildFunctionResponsePart(
                providerSuppliedId ? functionCall.Id : string.Empty,
                functionCall.Name,
                content);
        }

        private static Dictionary<string, object> BuildFunctionResponsePart(
            string functionId,
            string? functionName,
            string content)
        {
            var functionResponse = new Dictionary<string, object>
            {
                ["name"] = functionName ?? "function",
                ["response"] = new Dictionary<string, object> { ["content"] = content }
            };
            if (!string.IsNullOrWhiteSpace(functionId))
                functionResponse["id"] = functionId;

            return new Dictionary<string, object>
            {
                ["functionResponse"] = functionResponse
            };
        }

        private void ApplyFunctionDeclarations(Dictionary<string, object> requestBody)
        {
            if (!ShouldUseFunctions) return;

            requestBody["tools"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["functionDeclarations"] = Functions.Select(f => new Dictionary<string, object>
                    {
                        ["name"] = f.Name,
                        ["description"] = f.Description,
                        ["parameters"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = f.Parameters.Properties.ToDictionary(
                                kvp => kvp.Key,
                                kvp => (object)ConvertParameterProperty(kvp.Value)),
                            ["required"] = f.Parameters.Required
                        }
                    }).ToList()
                }
            };

            if (FunctionCallMode == FunctionCallMode.None)
            {
                requestBody["toolConfig"] = new Dictionary<string, object>
                {
                    ["functionCallingConfig"] = new Dictionary<string, object> { ["mode"] = "NONE" }
                };
            }
            else if (!IsFunctionContinuation() &&
                     !string.IsNullOrWhiteSpace(ForceFunctionName))
            {
                requestBody["toolConfig"] = new Dictionary<string, object>
                {
                    ["functionCallingConfig"] = new Dictionary<string, object>
                    {
                        ["mode"] = "ANY",
                        ["allowedFunctionNames"] = new[] { ForceFunctionName }
                    }
                };
            }
            else if (IsGemini3Model())
            {
                // VALIDATED keeps AUTO semantics (the model may answer directly) while requiring
                // any emitted Gemini 3 function call to conform to its declaration schema.
                requestBody["toolConfig"] = new Dictionary<string, object>
                {
                    ["functionCallingConfig"] = new Dictionary<string, object>
                    {
                        ["mode"] = "VALIDATED"
                    }
                };
            }
        }

        private bool IsFunctionContinuation()
        {
            var lastMessage = GetLatestMessages().LastOrDefault();
            return lastMessage?.Role == ActorRole.Function ||
                   lastMessage?.FunctionCallResultBatch != null ||
                   lastMessage?.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() ==
                       "function_result";
        }

        private void ApplySystemInstruction(Dictionary<string, object> requestBody)
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

            if (string.IsNullOrEmpty(systemMsg)) return;

            requestBody["systemInstruction"] = new
            {
                parts = new[] { new { text = systemMsg } }
            };
        }

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            var (content, _, functionCalls, _) = ExtractFunctionCallsWithSignature(response);
            return (content, functionCalls);
        }

        private Dictionary<string, object> ConvertParameterProperty(ParameterProperty prop)
        {
            var result = new Dictionary<string, object>
            {
                ["type"] = prop.Type ?? "string"
            };

            if (!string.IsNullOrEmpty(prop.Description))
                result["description"] = prop.Description;

            if (prop.Enum != null && prop.Enum.Count > 0)
                result["enum"] = prop.Enum;

            if (prop.Items != null)
                result["items"] = ConvertParameterProperty(prop.Items);

            return result;
        }

        #endregion
    }
}
