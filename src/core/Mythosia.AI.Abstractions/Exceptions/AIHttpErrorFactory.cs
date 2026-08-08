using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Exceptions
{
    /// <summary>
    /// Translates a provider's HTTP failure into the library's exception vocabulary.
    /// <para>
    /// Every provider words its errors differently, so detection lives here rather than in the
    /// core or in the consuming app — the app should only ever learn <i>that</i> the context
    /// overflowed, never how a particular vendor phrases it.
    /// </para>
    /// <para>
    /// Detection is deliberately narrow. A false positive is worse than a miss: it makes the
    /// core compact the conversation (which deletes messages irreversibly) in response to an
    /// unrelated 400. When in doubt this returns a plain <see cref="AIServiceException"/>.
    /// </para>
    /// </summary>
    public static class AIHttpErrorFactory
    {
        /// <summary>Metadata key set on a streaming error chunk when the body says the context overflowed.</summary>
        public const string ContextLengthExceededKey = "context_length_exceeded";

        /// <summary>Metadata key carrying the model's context window, when the provider reported it.</summary>
        public const string MaxContextTokensKey = "max_context_tokens";

        /// <summary>Metadata key carrying the rejected prompt's size, when the provider reported it.</summary>
        public const string RequestedTokensKey = "requested_tokens";

        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // OpenAI's machine-readable code. Unambiguous — no number to extract from it directly.
        private static readonly Regex OpenAiCode = new Regex(@"""code""\s*:\s*""context_length_exceeded""", Opts);

        // vLLM / OpenAI wording: "This model's maximum context length is 50000 tokens."
        private static readonly Regex MaxContextPhrase = new Regex(@"maximum\s+context\s+length\s+is\s+(\d+)\s+tokens", Opts);

        // vLLM: "your prompt contains at least 50001 input tokens"  /  OpenAI: "resulted in 50001 tokens"
        private static readonly Regex RequestedPhrase = new Regex(
            @"(?:prompt\s+contains\s+(?:at\s+least\s+)?(\d+)\s+(?:input\s+)?tokens|resulted\s+in\s+(\d+)\s+tokens)", Opts);

        // Anthropic: "prompt is too long: 250000 tokens > 200000 maximum"
        private static readonly Regex AnthropicTooLong = new Regex(
            @"prompt\s+is\s+too\s+long\s*:\s*(\d+)\s+tokens\s*>\s*(\d+)", Opts);

        // Anthropic, combined form: "input length and `max_tokens` exceed context limit:
        // 186433 + 20000 > 200000, decrease input length or `max_tokens` and try again".
        // This is the band that actually gets hit: max_tokens rides on every request, so the sum
        // crosses the window before the input does on its own, and the wording above never appears.
        private static readonly Regex AnthropicCombined = new Regex(
            @"input\s+length\s+and\s+.?max_tokens.?\s+exceed\s+context\s+limit\s*:\s*(\d+)\s*\+\s*(\d+)\s*>\s*(\d+)", Opts);

        // Google: "The input token count (1050000) exceeds the maximum number of tokens allowed (1048576)"
        private static readonly Regex GoogleTokenCount = new Regex(
            @"input\s+token\s+count\s*\((\d+)\).*?exceeds.*?\((\d+)\)", Opts | RegexOptions.Singleline);

        // xAI: "This model's maximum prompt length is 256000 but the request contains 280193 tokens."
        private static readonly Regex XaiPromptLength = new Regex(
            @"maximum\s+prompt\s+length\s+is\s+(\d+)\s+but\s+the\s+request\s+contains\s+(\d+)\s+tokens", Opts);

        /// <summary>
        /// Does this HTTP failure mean "the prompt did not fit"? Only 400 and 413 are considered —
        /// 429/5xx are transport or quota problems that compaction cannot fix.
        /// </summary>
        public static bool IsContextLengthExceeded(
            int statusCode, string? errorBody, out int? maxContextTokens, out int? requestedTokens)
        {
            maxContextTokens = null;
            requestedTokens = null;

            if (statusCode != 400 && statusCode != 413) return false;
            if (string.IsNullOrEmpty(errorBody)) return false;

            var body = errorBody!;

            var anthropic = AnthropicTooLong.Match(body);
            if (anthropic.Success)
            {
                requestedTokens = ParseOrNull(anthropic.Groups[1].Value);
                maxContextTokens = ParseOrNull(anthropic.Groups[2].Value);
                return true;
            }

            var anthropicCombined = AnthropicCombined.Match(body);
            if (anthropicCombined.Success)
            {
                // Report the sum the server rejected, not just the input half — the input alone
                // would look like it fits, which is exactly why this form is confusing.
                var input = ParseOrNull(anthropicCombined.Groups[1].Value);
                var output = ParseOrNull(anthropicCombined.Groups[2].Value);
                requestedTokens = input.HasValue && output.HasValue ? input + output : input;
                maxContextTokens = ParseOrNull(anthropicCombined.Groups[3].Value);
                return true;
            }

            var google = GoogleTokenCount.Match(body);
            if (google.Success)
            {
                requestedTokens = ParseOrNull(google.Groups[1].Value);
                maxContextTokens = ParseOrNull(google.Groups[2].Value);
                return true;
            }

            var xai = XaiPromptLength.Match(body);
            if (xai.Success)
            {
                maxContextTokens = ParseOrNull(xai.Groups[1].Value);
                requestedTokens = ParseOrNull(xai.Groups[2].Value);
                return true;
            }

            var max = MaxContextPhrase.Match(body);
            var hasCode = OpenAiCode.IsMatch(body);
            if (!max.Success && !hasCode) return false;

            if (max.Success)
                maxContextTokens = ParseOrNull(max.Groups[1].Value);

            var requested = RequestedPhrase.Match(body);
            if (requested.Success)
            {
                var raw = requested.Groups[1].Success ? requested.Groups[1].Value : requested.Groups[2].Value;
                requestedTokens = ParseOrNull(raw);
            }

            return true;
        }

        /// <summary>
        /// Builds the exception for a failed chat request. Returns
        /// <see cref="ContextLengthExceededException"/> when the body says the prompt did not fit,
        /// otherwise a plain <see cref="AIServiceException"/>.
        /// <para>
        /// The message keeps the historical
        /// <c>"API request failed ({status}): {reason}"</c> shape so existing log scraping and
        /// user-facing strings do not change.
        /// </para>
        /// </summary>
        public static AIServiceException FromHttp(
            int statusCode, string? reasonPhrase, string? errorBody, string messagePrefix = "API request failed")
            => FromHttp(statusCode, reasonPhrase, errorBody, messagePrefix, false);

        /// <summary>
        /// Builds the exception for a failed request and optionally includes the provider body in
        /// the exception message. Providers can opt in when their HTTP reason phrase is generic and
        /// callers would otherwise lose the actionable diagnostic when logging only
        /// <see cref="System.Exception.Message"/>. The body is always retained in
        /// <see cref="AIServiceException.ErrorDetails"/>.
        /// </summary>
        public static AIServiceException FromHttp(
            int statusCode,
            string? reasonPhrase,
            string? errorBody,
            string messagePrefix,
            bool includeErrorBodyInMessage)
        {
            var body = errorBody ?? string.Empty;
            var detail = string.IsNullOrEmpty(reasonPhrase) ? body : reasonPhrase!;
            if (includeErrorBodyInMessage &&
                !string.IsNullOrWhiteSpace(body) &&
                !string.Equals(detail, body, System.StringComparison.Ordinal))
            {
                detail = $"{detail} - {body}";
            }
            var message = $"{messagePrefix} ({statusCode}): {detail}";

            return IsContextLengthExceeded(statusCode, body, out var max, out var requested)
                ? new ContextLengthExceededException(message, body, statusCode, max, requested)
                : new AIServiceException(message, body);
        }

        /// <summary>
        /// Builds the metadata dictionary for a streaming error chunk.
        /// <para>
        /// Streaming keeps yielding an error <i>chunk</i> rather than throwing — that contract is
        /// relied on by agent loops and by consumers that render the error as text. This only adds
        /// keys, so existing readers are unaffected; the core uses the added flag to decide whether
        /// the failure is recoverable.
        /// </para>
        /// </summary>
        public static Dictionary<string, object> BuildErrorMetadata(int statusCode, string? errorBody)
        {
            var metadata = new Dictionary<string, object>
            {
                ["error"] = errorBody ?? string.Empty,
                ["status_code"] = statusCode,
            };

            if (IsContextLengthExceeded(statusCode, errorBody, out var max, out var requested))
            {
                metadata[ContextLengthExceededKey] = true;
                if (max.HasValue) metadata[MaxContextTokensKey] = max.Value;
                if (requested.HasValue) metadata[RequestedTokensKey] = requested.Value;
            }

            return metadata;
        }

        private static int? ParseOrNull(string value)
            => int.TryParse(value, out var parsed) ? parsed : (int?)null;
    }
}
