using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        #region Stream Chunk Parsing

        protected override OpenAIStreamChunk ParseStreamChunk(string jsonData, StreamOptions options)
        {
            var chunk = new OpenAIStreamChunk();

            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                // Extract metadata if needed
                if (options.IncludeMetadata)
                {
                    chunk.Metadata = new Dictionary<string, object>();
                    if (root.TryGetProperty("model", out var m))
                    {
                        chunk.Model = m.GetString();
                        chunk.Metadata["model"] = chunk.Model;
                    }
                    if (root.TryGetProperty("id", out var id))
                        chunk.Metadata["response_id"] = id.GetString();
                }

                // New API format (o3, GPT-5, etc.)
                if (root.TryGetProperty("type", out var typeProp))
                {
                    ParseNewApiStreamChunk(root, typeProp.GetString(), chunk);
                }
                // Legacy format (GPT-4o, etc.)
                else if (root.TryGetProperty("choices", out var choices))
                {
                    ParseLegacyStreamChunk(choices, chunk);
                }

                // Legacy API sends usage in the final chunk (with empty choices) at root level
                if (root.TryGetProperty("usage", out var usage))
                    chunk.Usage = ParseOpenAICompatibleUsage(usage);
            }
            catch { }

            return chunk;
        }

        private void ParseNewApiStreamChunk(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            switch (type)
            {
                // 텍스트 델타
                case "response.output_text.delta":
                    ParseStreamTextDelta(root, chunk);
                    break;

                // 함수 호출 이벤트
                case "response.function_call":
                case "response.function_call_arguments.delta":
                case "response.function_call_arguments.done":
                case "response.function_call.arguments.delta":  // legacy compat
                case "response.function_call.arguments.done":   // legacy compat
                    ParseStreamFunctionCallEvent(root, type, chunk);
                    break;

                // 출력 아이템 이벤트 (텍스트 또는 함수 호출 포함)
                case "response.output_item.added":
                case "response.output_item.delta":
                case "response.output_item.done":
                    ParseStreamOutputItemEvent(root, chunk);
                    break;

                // 추론 요약 이벤트
                case "response.reasoning_summary_text.delta":
                case "response.reasoning_summary_text.done":
                case "response.reasoning_summary_part.added":
                case "response.reasoning_summary_part.done":
                case "response.reasoning_text.delta":
                case "response.reasoning_text.done":
                    ParseStreamReasoningEvent(root, type, chunk);
                    break;

                // 응답 생명주기 이벤트
                case "response.created":
                    ParseStreamCreatedEvent(root, chunk);
                    break;

                // 스트리밍 완료 이벤트
                case "response.done":
                case "response.completed":
                    ParseStreamCompletionEvent(root, chunk);
                    break;
            }
        }

        /// <summary>
        /// response.output_text.delta 파싱
        /// </summary>
        private void ParseStreamTextDelta(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (root.TryGetProperty("delta", out var delta))
            {
                chunk.Text = delta.ValueKind == JsonValueKind.String
                    ? delta.GetString()
                    : delta.TryGetProperty("text", out var t) ? t.GetString() : null;
            }
        }

        /// <summary>
        /// 함수 호출 관련 이벤트 파싱
        /// - response.function_call: 초기 함수 호출 정보
        /// - response.function_call_arguments.delta: 인자 스트리밍 델타
        /// - response.function_call_arguments.done: 인자 스트리밍 완료
        /// </summary>
        private void ParseStreamFunctionCallEvent(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            if (type == "response.function_call")
            {
                chunk.FunctionCall = new FunctionCall { Source = IdSource.OpenAI };
                if (root.TryGetProperty("function_call", out var fc))
                {
                    if (fc.TryGetProperty("name", out var n))
                        chunk.FunctionCall.Name = n.GetString();
                    if (fc.TryGetProperty("id", out var id))
                        chunk.FunctionCall.Id = id.GetString();
                }
            }
            else if (type.Contains("done"))
            {
                // response.function_call_arguments.done — 완성된 인자 JSON
                if (root.TryGetProperty("arguments", out var argsComplete))
                {
                    var argsStr = argsComplete.GetString();
                    if (!string.IsNullOrEmpty(argsStr))
                    {
                        chunk.FunctionCall = new FunctionCall
                        {
                            Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = argsStr
                            },
                            Source = IdSource.OpenAI
                        };
                    }
                }
            }
            else
            {
                // response.function_call_arguments.delta — 인자 스트리밍 델타
                if (root.TryGetProperty("delta", out var argDelta))
                {
                    chunk.FunctionCall = new FunctionCall
                    {
                        Arguments = new Dictionary<string, object>
                        {
                            ["_partial"] = argDelta.GetString()
                        },
                        Source = IdSource.OpenAI
                    };
                }
            }
        }

        /// <summary>
        /// 출력 아이템 이벤트 파싱 (response.output_item.added, response.output_item.delta)
        /// </summary>
        private void ParseStreamOutputItemEvent(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (!root.TryGetProperty("item", out var item))
                return;

            // function_call 타입 아이템
            if (item.TryGetProperty("type", out var itemType) &&
                itemType.GetString() == "function_call")
            {
                chunk.FunctionCall = new FunctionCall { Source = IdSource.OpenAI };

                if (item.TryGetProperty("name", out var fname))
                    chunk.FunctionCall.Name = fname.GetString();
                if (item.TryGetProperty("call_id", out var cid))
                    chunk.FunctionCall.Id = cid.GetString();
                if (item.TryGetProperty("arguments", out var args))
                {
                    chunk.FunctionCall.Arguments = new Dictionary<string, object>
                    {
                        ["_partial"] = args.GetString()
                    };
                }
                return;
            }

            // reasoning 타입 아이템에서 reasoning 요약/텍스트 추출
            if (item.TryGetProperty("type", out var reasoningItemType) &&
                (reasoningItemType.GetString() == "reasoning" || reasoningItemType.GetString() == "reasoning_summary"))
            {
                if (item.TryGetProperty("summary", out var summaryElem) && summaryElem.ValueKind == JsonValueKind.Array)
                {
                    var reasoningText = new StringBuilder();
                    foreach (var summaryItem in summaryElem.EnumerateArray())
                    {
                        if (summaryItem.TryGetProperty("text", out var summaryText))
                        {
                            reasoningText.Append(summaryText.GetString());
                        }
                    }

                    if (reasoningText.Length > 0)
                    {
                        chunk.Reasoning = reasoningText.ToString();
                        return;
                    }
                }

                if (item.TryGetProperty("text", out var itemText) && itemText.ValueKind == JsonValueKind.String)
                {
                    chunk.Reasoning = itemText.GetString();
                    return;
                }
            }

            // message 타입 아이템에서 텍스트 추출
            if (item.TryGetProperty("message", out var messageObj) &&
                messageObj.TryGetProperty("content", out var content))
            {
                chunk.Text = ExtractTextFromContent(content);
            }
        }

        /// <summary>
        /// 추론 요약 이벤트 파싱 (response.reasoning_summary_text.delta, response.reasoning_summary_part.*)
        /// </summary>
        private void ParseStreamReasoningEvent(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            if ((type == "response.reasoning_summary_text.delta" ||
                 type == "response.reasoning_text.delta") &&
                root.TryGetProperty("delta", out var reasoningDelta))
            {
                chunk.Reasoning = reasoningDelta.ValueKind == JsonValueKind.String
                    ? reasoningDelta.GetString()
                    : reasoningDelta.TryGetProperty("text", out var deltaText) ? deltaText.GetString() : null;

                return;
            }

            // done 이벤트에서 최종 텍스트가 제공되는 경우 처리
            if ((type == "response.reasoning_summary_text.done" ||
                 type == "response.reasoning_text.done") &&
                root.TryGetProperty("text", out var reasoningText) &&
                reasoningText.ValueKind == JsonValueKind.String)
            {
                chunk.Reasoning = reasoningText.GetString();
                return;
            }

            // 일부 이벤트는 summary 배열로 전달됨
            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var summaryItem in summary.EnumerateArray())
                {
                    if (summaryItem.TryGetProperty("text", out var sText))
                        sb.Append(sText.GetString());
                }

                if (sb.Length > 0)
                    chunk.Reasoning = sb.ToString();
            }

            // response.reasoning_summary_part.added / done 는 텍스트 필드가 없으면 무시
        }

        /// <summary>
        /// response.created 이벤트 파싱
        /// </summary>
        private void ParseStreamCreatedEvent(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (root.TryGetProperty("response", out var createdResp))
            {
                if (createdResp.TryGetProperty("model", out var createdModel))
                    chunk.Model = createdModel.GetString();
                if (createdResp.TryGetProperty("id", out var createdId))
                {
                    chunk.Metadata ??= new Dictionary<string, object>();
                    chunk.Metadata["response_id"] = createdId.GetString();
                }
            }
        }

        /// <summary>
        /// 스트리밍 완료 이벤트 파싱 (response.done, response.completed)
        /// </summary>
        private void ParseStreamCompletionEvent(JsonElement root, OpenAIStreamChunk chunk)
        {
            chunk.IsCompletion = true;
            if (root.TryGetProperty("response", out var doneResp))
            {
                chunk.Metadata ??= new Dictionary<string, object>();
                chunk.Metadata["finish_reason"] = "stop";
                if (doneResp.TryGetProperty("usage", out var usage))
                    chunk.Usage = ParseOpenAICompatibleUsage(usage);
                if (doneResp.TryGetProperty("model", out var doneModel))
                    chunk.Model = doneModel.GetString();
                if (doneResp.TryGetProperty("id", out var doneId))
                    chunk.Metadata["response_id"] = doneId.GetString();
            }
        }

        private void ParseLegacyStreamChunk(JsonElement choices, OpenAIStreamChunk chunk)
        {
            if (choices.GetArrayLength() == 0) return;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var legacyDelta)) return;

            if (legacyDelta.TryGetProperty("content", out var legacyContent))
            {
                var text = legacyContent.GetString();
                if (!string.IsNullOrEmpty(text))
                    chunk.Text = text;
            }

            if (legacyDelta.TryGetProperty("function_call", out var legacyFc))
            {
                chunk.FunctionCall = new FunctionCall
                {
                    Source = IdSource.OpenAI
                };

                if (legacyFc.TryGetProperty("name", out var name))
                {
                    chunk.FunctionCall.Name = name.GetString();
                    // Legacy API doesn't have call_id, generate one
                    chunk.FunctionCall.Id = $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                }

                if (legacyFc.TryGetProperty("arguments", out var args))
                {
                    var argsStr = args.GetString();
                    if (!string.IsNullOrEmpty(argsStr))
                    {
                        try
                        {
                            chunk.FunctionCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsStr);
                        }
                        catch
                        {
                            chunk.FunctionCall.Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = argsStr
                            };
                        }
                    }
                }
            }
        }

        // ExtractTextFromContent 메서드 제거 - Parsing.cs에 있는 것 사용

        #endregion
    }
}
