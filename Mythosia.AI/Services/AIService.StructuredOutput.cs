using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Utilities;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        /// <summary>
        /// JSON schema string for structured output mode. Null when not in structured output mode.
        /// Set temporarily during GetCompletionAsync&lt;T&gt;() and cleared in finally block.
        /// </summary>
        internal string? _structuredOutputSchemaJson;

        /// <summary>
        /// Maximum number of auto-correction retries when the LLM produces invalid JSON
        /// for structured output. Default is 2.
        /// This is NOT a network/rate-limit retry — it is an "output quality/format correction" retry
        /// that sends a correction prompt asking the model to fix its JSON output.
        /// </summary>
        public int StructuredOutputMaxRetries { get; set; } = 2;

        /// <summary>
        /// Per-call structured output policy override.
        /// Set via <see cref="Extensions.AIServiceExtensions.WithStructuredOutputPolicy"/> and
        /// consumed (then cleared) by <see cref="GetCompletionAsync{T}(string)"/>.
        /// </summary>
        internal StructuredOutputPolicy? _currentStructuredOutputPolicy;

        /// <summary>
        /// Sends a prompt and deserializes the LLM response to the specified type.
        /// Internally generates a JSON schema from T, instructs the LLM to respond in that format,
        /// and deserializes the JSON response.
        /// If the LLM produces invalid JSON, sends an auto-correction prompt and retries
        /// up to <see cref="StructuredOutputMaxRetries"/> times.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the response to. Must have public properties.</typeparam>
        /// <param name="prompt">The user prompt.</param>
        /// <returns>The deserialized response object.</returns>
        /// <exception cref="StructuredOutputException">Thrown when deserialization fails after all retry attempts.</exception>
        public async Task<T> GetCompletionAsync<T>(string prompt) where T : class
        {
            _structuredOutputSchemaJson = JsonSchemaGenerator.Generate(typeof(T));
            var policy = _currentStructuredOutputPolicy;
            _currentStructuredOutputPolicy = null;
            var effectiveRetries = policy?.MaxRepairAttempts ?? StructuredOutputMaxRetries;
            var maxAttempts = 1 + Math.Max(0, effectiveRetries);
            string? firstRawResponse = null;
            string? lastRawResponse = null;
            string? lastParseError = null;

            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    string rawResult;

                    if (attempt == 1)
                    {
                        rawResult = await GetCompletionAsync(prompt);
                    }
                    else
                    {
                        var correctionPrompt = BuildCorrectionPrompt(lastRawResponse!, lastParseError!);
                        rawResult = await GetCompletionAsync(correctionPrompt);
                    }

                    if (attempt == 1) firstRawResponse = rawResult;
                    lastRawResponse = rawResult;

                    var extracted = ExtractJsonFromResponse(rawResult);

                    try
                    {
                        var result = JsonSerializer.Deserialize<T>(extracted, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (result != null) return result;
                        lastParseError = $"Deserialization returned null for type {typeof(T).Name}";
                    }
                    catch (JsonException ex)
                    {
                        lastParseError = ex.Message;
                    }
                }

                throw new StructuredOutputException(
                    typeof(T).Name,
                    firstRawResponse ?? "",
                    lastRawResponse ?? "",
                    lastParseError ?? "Unknown error",
                    maxAttempts,
                    _structuredOutputSchemaJson);
            }
            finally
            {
                _structuredOutputSchemaJson = null;
            }
        }

        /// <summary>
        /// Builds an auto-correction prompt that asks the LLM to fix its JSON output.
        /// Includes the raw response and parse error so the model can self-correct.
        /// </summary>
        private string BuildCorrectionPrompt(string rawResponse, string parseError)
        {
            return "[STRUCTURED OUTPUT CORRECTION] Your previous response was not valid JSON " +
                   "conforming to the required schema.\n\n" +
                   $"Your output was:\n{rawResponse}\n\n" +
                   $"Parse error: {parseError}\n\n" +
                   "Output ONLY valid JSON that strictly conforms to the schema. " +
                   "No markdown code blocks, no explanation, no text before or after the JSON.";
        }

        /// <summary>
        /// Returns the structured output instruction to append to system messages.
        /// Returns null if not in structured output mode.
        /// </summary>
        internal string? GetStructuredOutputInstruction()
        {
            if (_structuredOutputSchemaJson == null) return null;

            return "\n\n[STRUCTURED OUTPUT] You MUST respond with ONLY valid JSON. " +
                   "No markdown code blocks, no explanation, no text before or after the JSON. " +
                   $"The JSON must conform to this schema:\n{_structuredOutputSchemaJson}";
        }

        /// <summary>
        /// Extracts JSON from LLM response, handling markdown code blocks and extra text.
        /// </summary>
        internal static string ExtractJsonFromResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var trimmed = text.Trim();

            // Handle markdown code blocks: ```json ... ``` or ``` ... ```
            if (trimmed.StartsWith("```"))
            {
                var firstNewLine = trimmed.IndexOf('\n');
                if (firstNewLine > 0)
                    trimmed = trimmed.Substring(firstNewLine + 1);

                var lastFence = trimmed.LastIndexOf("```");
                if (lastFence > 0)
                    trimmed = trimmed.Substring(0, lastFence);

                return trimmed.Trim();
            }

            // Try to find JSON object or array boundaries
            var jsonStart = trimmed.IndexOfAny(new[] { '{', '[' });
            var jsonEnd = trimmed.LastIndexOfAny(new[] { '}', ']' });

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                return trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            return trimmed;
        }
    }
}
