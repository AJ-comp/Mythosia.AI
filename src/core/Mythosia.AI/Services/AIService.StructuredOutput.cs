using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        /// <summary>
        /// JSON schema string for structured output mode. Null when not in structured output mode.
        /// Legacy provider default; request execution uses RequestStructuredOutputSchemaJson.
        /// </summary>
        protected internal string? _structuredOutputSchemaJson;

        /// <summary>
        /// Maximum number of auto-correction retries when the LLM produces invalid JSON
        /// for structured output. Default is 2.
        /// Values below zero disable repairs; Int32.MaxValue is rejected before provider work
        /// because the initial attempt must also fit in the reported attempt count.
        /// This is NOT a network/rate-limit retry — it is an "output quality/format correction" retry
        /// that sends a correction prompt asking the model to fix its JSON output.
        /// </summary>
        public int StructuredOutputMaxRetries { get; set; } = 2;

        /// <summary>
        /// Per-call structured output policy override.
        /// Set via <see cref="Extensions.AIServiceExtensions.WithStructuredOutputPolicy"/> and
        /// consumed (then cleared) by <see cref="GetCompletionAsync{T}(string, CancellationToken)"/>.
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
        /// <param name="cancellationToken">Cancels this call, including summaries and JSON repair requests.</param>
        /// <returns>The deserialized response object.</returns>
        /// <exception cref="StructuredOutputException">Thrown when deserialization fails after all retry attempts.</exception>
        public async Task<T> GetCompletionAsync<T>(string prompt, CancellationToken cancellationToken = default) where T : class
        {
            using var cancellationScope = BeginRequestCancellationScope(cancellationToken);
            var requestMessage = new Message(ActorRole.User, prompt);
            using var requestScope = BeginRequestSettingsScope();
            using var settingsScope = UseRequestSettings(_requestExecution.Value!.Settings);
            var schemaJson = JsonSchemaGenerator.Generate(typeof(T));
            SetExecutionSetting(nameof(_structuredOutputSchemaJson), schemaJson);
            var policy = _currentStructuredOutputPolicy;
            _currentStructuredOutputPolicy = null;
            var effectiveRetries = policy?.MaxRepairAttempts ?? RequestSetting(nameof(StructuredOutputMaxRetries), StructuredOutputMaxRetries);
            string? firstRawResponse = null;
            string? lastRawResponse = null;
            string? lastParseError = null;

            using var featureScope = BeginRequestFeaturesScope(requestMessage);
            // Capture one-call options even when the retry budget is rejected, so they
            // cannot leak into a later request. No provider execution has started yet.
            var maxAttempts = ResolveStructuredOutputAttemptLimit(effectiveRetries);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                RequestCancellationToken.ThrowIfCancellationRequested();
                string rawResult;

                if (attempt == 0)
                {
                    await ApplySummaryPolicyIfNeededAsync();
                    rawResult = await GetCompletionAsync(requestMessage, profile: null, context: null);
                }
                else
                {
                    var correctionPrompt = BuildCorrectionPrompt(lastRawResponse!, lastParseError!);
                    rawResult = await GetCompletionAsync(correctionPrompt);
                }

                RequestCancellationToken.ThrowIfCancellationRequested();
                if (attempt == 0) firstRawResponse = rawResult;
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
                schemaJson);
        }

        internal static int ResolveStructuredOutputAttemptLimit(int maxRepairAttempts)
        {
            if (maxRepairAttempts == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxRepairAttempts),
                    "The initial attempt plus repair attempts must fit in Int32.");

            return 1 + Math.Max(0, maxRepairAttempts);
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
        protected internal string? GetStructuredOutputInstruction()
        {
            if (RequestStructuredOutputSchemaJson == null) return null;

            return "\n\n[STRUCTURED OUTPUT] You MUST respond with ONLY valid JSON. " +
                   "No markdown code blocks, no explanation, no text before or after the JSON. " +
                   $"The JSON must conform to this schema:\n{RequestStructuredOutputSchemaJson}";
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
