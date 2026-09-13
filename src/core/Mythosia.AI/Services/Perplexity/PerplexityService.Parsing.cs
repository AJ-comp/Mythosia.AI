using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        protected override string ExtractResponseContent(string responseContent) => ParseAgentResponse(responseContent).Text;
        protected override string StreamParseJson(string jsonData)
        {
            using var document = JsonDocument.Parse(jsonData);
            return ReadAgentString(document.RootElement, "type") == "response.output_text.delta"
                ? ReadAgentString(document.RootElement, "delta") ?? string.Empty : string.Empty;
        }

        internal sealed class ParsedAgentResponse
        {
            public string? Id { get; set; }
            public string? Model { get; set; }
            public string? Status { get; set; }
            public string Text { get; set; } = string.Empty;
            public string Reasoning { get; set; } = string.Empty;
            public FunctionCallBatch Calls { get; set; } = new FunctionCallBatch();
            public TokenUsage? Usage { get; set; }
            public List<JsonElement> ReplayItems { get; } = new List<JsonElement>();
            public List<AICitation> Citations { get; } = new List<AICitation>();
        }

        internal static ParsedAgentResponse ParseAgentResponse(string json)
        {
            try { return ParseAgentResponseCore(json); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException || exception is OverflowException)
            {
                throw new AIServiceException("Invalid Perplexity Agent response.", exception);
            }
        }

        private static ParsedAgentResponse ParseAgentResponseCore(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new AIServiceException("Agent response must be an object.");
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new AIServiceException("Perplexity Agent returned an error.", error.GetRawText());
            if (ReadAgentString(root, "status") != "completed")
                throw new AIServiceException($"Perplexity Agent response did not complete (status={ReadAgentString(root, "status") ?? "missing"}).");
            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                throw new AIServiceException("Completed Agent response is missing its output array.");

            var parsed = new ParsedAgentResponse
            {
                Id = ReadAgentString(root, "id"),
                Model = ReadAgentString(root, "model"),
                Status = ReadAgentString(root, "status")
            };
            if (root.TryGetProperty("usage", out var usage)) parsed.Usage = ParseAgentUsage(usage);
            var text = new StringBuilder();
            var reasoning = new StringBuilder();
            var calls = new List<FunctionCall>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hasMessage = false;
            var index = 0;
            foreach (var item in output.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new AIServiceException("Agent output items must be objects.");
                var type = ReadAgentString(item, "type");
                if (type == "function_call")
                {
                    var status = ReadAgentString(item, "status");
                    if (status != null && status != "completed") throw new AIServiceException("Agent returned an incomplete function call.");
                    var id = ReadAgentString(item, "call_id");
                    var name = ReadAgentString(item, "name");
                    var arguments = ReadAgentString(item, "arguments");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id))
                        throw new AIServiceException("Agent function calls require a unique call_id and nonempty name.");
                    if (arguments == null) throw new AIServiceException("Agent function calls require complete JSON arguments.");
                    using var args = JsonDocument.Parse(arguments);
                    if (args.RootElement.ValueKind != JsonValueKind.Object) throw new AIServiceException("Agent function arguments must be a JSON object.");
                    var argumentNames = new HashSet<string>(StringComparer.Ordinal);
                    var values = new Dictionary<string, object>();
                    foreach (var property in args.RootElement.EnumerateObject())
                    {
                        if (!argumentNames.Add(property.Name)) throw new AIServiceException("Agent function arguments contain duplicate property names.");
                        values[property.Name] = property.Value.Clone();
                    }
                    var call = new FunctionCall { Id = id!, Name = name!, Index = index, Source = IdSource.Perplexity, Arguments = values };
                    if (item.TryGetProperty("thought_signature", out var signature) && signature.ValueKind != JsonValueKind.Null)
                        call.Metadata = new Dictionary<string, object> { ["thought_signature"] = signature.Clone() };
                    calls.Add(call);
                }
                else if (type == "message")
                {
                    var status = ReadAgentString(item, "status");
                    if (status != null && status != "completed") throw new AIServiceException("Agent returned an incomplete message.");
                    hasMessage = true;
                    if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                        throw new AIServiceException("Agent message content must be an array.");
                    var contentIndex = 0;
                    foreach (var part in content.EnumerateArray())
                    {
                        var partType = ReadAgentString(part, "type");
                        if (partType == "output_text" || partType == "text")
                            text.Append(ReadAgentString(part, "text") ?? string.Empty);
                        else if (partType == "refusal") text.Append(ReadAgentString(part, "refusal") ?? string.Empty);
                        if (part.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Array)
                            foreach (var annotation in annotations.EnumerateArray())
                            {
                                var citation = ParseAgentCitation(annotation, parsed.Id, index, contentIndex);
                                if (citation != null) parsed.Citations.Add(citation);
                            }
                        contentIndex++;
                    }
                }
                else if (type == "reasoning")
                {
                    if (item.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array)
                        foreach (var part in summary.EnumerateArray()) reasoning.Append(ReadAgentString(part, "text"));
                }
                foreach (var result in GetAgentToolSources(item))
                    {
                        var citation = ParseAgentCitation(result, parsed.Id, index, null);
                        if (citation != null) parsed.Citations.Add(citation);
                    }
                parsed.ReplayItems.Add(item.Clone());
                index++;
            }
            if (!hasMessage && calls.Count == 0) throw new AIServiceException("Completed Agent response contains neither an assistant message nor custom function calls.");
            parsed.Text = text.ToString();
            parsed.Reasoning = reasoning.ToString();
            parsed.Calls = new FunctionCallBatch(calls);
            return parsed;
        }

        internal static TokenUsage? ParseAgentUsage(JsonElement usage)
        {
            if (usage.ValueKind == JsonValueKind.Null) return null;
            if (usage.ValueKind != JsonValueKind.Object) throw new JsonException("Agent usage must be an object.");
            var input = ReadAgentInteger(usage, "input_tokens") ?? 0;
            var output = ReadAgentInteger(usage, "output_tokens") ?? 0;
            var result = new TokenUsage
            {
                InputTokens = input, OutputTokens = output,
                TotalTokens = ReadAgentInteger(usage, "total_tokens") ?? checked(input + output)
            };
            if (usage.TryGetProperty("input_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                result.CachedInputTokens = ReadAgentInteger(details, "cached_tokens") ?? ReadAgentInteger(details, "cache_read_input_tokens") ?? 0;
                result.CacheCreationTokens = ReadAgentInteger(details, "cache_creation_input_tokens") ?? 0;
            }
            if (usage.TryGetProperty("output_tokens_details", out var outputDetails) && outputDetails.ValueKind == JsonValueKind.Object)
                result.ReasoningTokens = ReadAgentInteger(outputDetails, "reasoning_tokens") ?? 0;
            return result;
        }

        internal static string? ReadAgentString(JsonElement value, string property)
        {
            if (!value.TryGetProperty(property, out var child) || child.ValueKind == JsonValueKind.Null) return null;
            if (child.ValueKind != JsonValueKind.String) throw new JsonException($"Agent {property} must be a string.");
            return child.GetString();
        }

        private static int? ReadAgentInteger(JsonElement value, string property)
        {
            if (!value.TryGetProperty(property, out var child) || child.ValueKind == JsonValueKind.Null) return null;
            if (!child.TryGetInt32(out var result) || result < 0) throw new JsonException($"Agent {property} must be a nonnegative integer.");
            return result;
        }

        internal static IEnumerable<JsonElement> GetAgentToolSources(JsonElement item)
        {
            if (item.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                foreach (var result in results.EnumerateArray()) yield return result;
            if (ReadAgentString(item, "type") == "fetch_url_results" &&
                item.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
                foreach (var result in contents.EnumerateArray()) yield return result;
        }

        internal static AICitation? ParseAgentCitation(JsonElement value, string? responseId, int? outputIndex, int? contentIndex)
        {
            if (value.ValueKind != JsonValueKind.Object) return null;
            var url = ReadAgentString(value, "url");
            var file = ReadAgentString(value, "file_id");
            if (url == null && file == null) return null;
            return new AICitation
            {
                Provider = nameof(AIProvider.Perplexity), Url = url, FileId = file,
                Title = ReadAgentString(value, "title") ?? ReadAgentString(value, "filename"),
                Text = ReadAgentString(value, "text") ?? ReadAgentString(value, "snippet"),
                ResponseId = responseId, OutputIndex = outputIndex, ContentIndex = contentIndex,
                StartIndex = ReadAgentInteger(value, "start_index"), EndIndex = ReadAgentInteger(value, "end_index")
            };
        }
    }
}
