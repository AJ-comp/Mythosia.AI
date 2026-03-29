using System;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Unified token usage information across all AI providers.
    /// Always populated from the official API response data.
    /// </summary>
    public class TokenUsage
    {
        /// <summary>
        /// Number of tokens in the input/prompt
        /// </summary>
        public int InputTokens { get; set; }

        /// <summary>
        /// Number of tokens in the output/completion
        /// </summary>
        public int OutputTokens { get; set; }

        /// <summary>
        /// Total tokens used (input + output)
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Input tokens served from cache (reduced cost).
        /// Mapped from: Anthropic cache_read_input_tokens, OpenAI cached_tokens,
        /// DeepSeek prompt_cache_hit_tokens, Gemini cachedContentTokenCount.
        /// </summary>
        public int CachedInputTokens { get; set; }

        /// <summary>
        /// Tokens written to cache for future reuse (Anthropic only).
        /// Mapped from: Anthropic cache_creation_input_tokens.
        /// </summary>
        public int CacheCreationTokens { get; set; }

        /// <summary>
        /// Tokens used for internal reasoning/thinking.
        /// Mapped from: OpenAI reasoning_tokens, Gemini thoughtsTokenCount.
        /// </summary>
        public int ReasoningTokens { get; set; }

        #region Computed Properties

        /// <summary>
        /// Input tokens that were NOT served from cache.
        /// </summary>
        public int NonCachedInputTokens => InputTokens - CachedInputTokens;

        /// <summary>
        /// Cache hit ratio (0.0 to 1.0). Returns 0 if no input tokens.
        /// </summary>
        public double CacheHitRatio => InputTokens > 0
            ? (double)CachedInputTokens / InputTokens
            : 0.0;

        /// <summary>
        /// Whether any cache activity occurred (hit or creation).
        /// </summary>
        public bool HasCacheActivity => CachedInputTokens > 0 || CacheCreationTokens > 0;

        /// <summary>
        /// Output tokens excluding reasoning (visible output only).
        /// </summary>
        public int VisibleOutputTokens => Math.Max(0, OutputTokens - ReasoningTokens);

        #endregion
    }
}
