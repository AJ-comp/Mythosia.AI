using System;
using System.ComponentModel;

namespace Mythosia.AI.Models.Enums
{
    public enum AIModel
    {
        // Anthropic Claude Models
        [Description("claude-opus-4-1-20250805")]
        ClaudeOpus4_1_250805,

        [Description("claude-opus-4-20250514")]
        ClaudeOpus4_250514,

        [Description("claude-sonnet-4-20250514")]
        ClaudeSonnet4_250514,

        [Description("claude-3-7-sonnet-latest")]
        Claude3_7SonnetLatest,

        [Description("claude-3-5-sonnet-20241022")]
        Claude3_5Sonnet241022,

        [Description("claude-3-5-haiku-20241022")]
        Claude3_5Haiku241022,

        [Description("claude-3-opus-20240229")]
        Claude3Opus240229,

        [Description("claude-3-haiku-20240307")]
        Claude3Haiku240307,


        // DeepSeek Models
        [Description("deepseek-chat")]
        DeepSeekChat,

        [Description("deepseek-reasoner")]
        DeepSeekReasoner,


        // OpenAI Models
        [Description("gpt-5")]
        Gpt5,

        [Description("gpt-5-mini")]
        Gpt5Mini,

        [Description("gpt-5-nano")]
        Gpt5Nano,

        [Description("gpt-5-chat-latest")]
        Gpt5ChatLatest,

        [Description("gpt-4.1")]
        Gpt4_1,

        [Description("gpt-4.1-mini")]
        Gpt4_1Mini,

        [Description("gpt-4.1-nano")]
        Gpt4_1Nano,

        [Description("gpt-4o")]
        Gpt4o,

        [Description("chatgpt-4o-latest")]
        Gpt4oLatest,

        [Description("gpt-4o-2024-11-20")]
        Gpt4o241120,

        [Description("gpt-4o-2024-08-06")]
        Gpt4o240806,

        [Description("gpt-4o-mini")]
        Gpt4oMini,

        [Description("gpt-4-vision-preview")]
        Gpt4Vision,


        // Google Gemini Models
        [Description("gemini-2.5-pro")]
        Gemini2_5Pro,

        [Description("gemini-2.5-flash")]
        Gemini2_5Flash,

        [Description("gemini-2.5-flash-lite")]
        Gemini2_5FlashLite,

        [Description("gemini-2.0-flash")]
        Gemini2_0Flash,

        [Description("gemini-1.5-pro")]
        Gemini1_5Pro,

        [Description("gemini-1.5-flash")]
        Gemini1_5Flash,

        [Description("gemini-1.5-pro")]
        [Obsolete("This value will be deprecated soon. Please use Gemini1_5Pro instead.")]
        Gemini15Pro,

        [Description("gemini-1.5-flash")]
        [Obsolete("This value will be deprecated soon. Please use Gemini1_5Flash instead.")]
        Gemini15Flash,

        [Description("gemini-pro")]
        GeminiPro,

        [Description("gemini-pro-vision")]
        GeminiProVision,

        // Perplexity Models
        [Description("sonar")]
        PerplexitySonar,

        [Description("sonar-pro")]
        PerplexitySonarPro,

        [Description("sonar-reasoning")]
        PerplexitySonarReasoning
    }

    public enum AIProvider
    {
        OpenAI,
        Anthropic,
        Google,
        DeepSeek,
        Perplexity
    }
}