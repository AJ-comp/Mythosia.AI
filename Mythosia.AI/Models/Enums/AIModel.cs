using System;
using System.ComponentModel;

namespace Mythosia.AI.Models.Enums
{
    public enum AIModel
    {
        // Anthropic Claude Models
        [Description("claude-opus-4-6")]
        ClaudeOpus4_6,

        [Description("claude-opus-4-1-20250805")]
        ClaudeOpus4_1_250805,

        [Description("claude-opus-4-20250514")]
        ClaudeOpus4_250514,

        [Description("claude-opus-4-5-20251101")]
        ClaudeOpus4_5_251101,

        [Description("claude-sonnet-4-5-20250929")]
        ClaudeSonnet4_5_250929,

        [Description("claude-sonnet-4-20250514")]
        ClaudeSonnet4_250514,

        [Description("claude-haiku-4-5-20251001")]
        ClaudeHaiku4_5_251001,

        [Description("claude-3-7-sonnet-latest")]
        Claude3_7SonnetLatest,

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
        /// <summary>OpenAI GPT-5 - Most advanced general-purpose model</summary>
        [Description("gpt-5")]
        Gpt5,

        /// <summary>OpenAI GPT-5 Mini - Smaller, faster GPT-5 variant</summary>
        [Description("gpt-5-mini")]
        Gpt5Mini,

        /// <summary>OpenAI GPT-5 Nano - Smallest, most efficient GPT-5</summary>
        [Description("gpt-5-nano")]
        Gpt5Nano,

        /// <summary>OpenAI GPT-5 (Aug 2025 snapshot)</summary>
        [Description("gpt-5-2025-08-07")]
        Gpt5_250807,

        /// <summary>OpenAI GPT-5 Mini (Aug 2025 snapshot)</summary>
        [Description("gpt-5-mini-2025-08-07")]
        Gpt5Mini_250807,

        /// <summary>OpenAI GPT-5 Nano (Aug 2025 snapshot)</summary>
        [Description("gpt-5-nano-2025-08-07")]
        Gpt5Nano_250807,

        /// <summary>OpenAI GPT-5 Chat Latest - Latest chat-optimized GPT-5</summary>
        [Description("gpt-5-chat-latest")]
        Gpt5ChatLatest,

        // OpenAI GPT-5.1 Models
        /// <summary>OpenAI GPT-5.1 - Reasoning model (default effort: none, supports: none/low/medium/high)</summary>
        [Description("gpt-5.1")]
        Gpt5_1,

        // OpenAI GPT-5.2 Models
        /// <summary>OpenAI GPT-5.2 - Best model for complex, coding, and agentic tasks (default effort: none, supports: none/low/medium/high/xhigh)</summary>
        [Description("gpt-5.2")]
        Gpt5_2,

        /// <summary>OpenAI GPT-5.2 Pro - High-compute model for tough problems (supports: medium/high/xhigh)</summary>
        [Description("gpt-5.2-pro")]
        Gpt5_2Pro,

        /// <summary>OpenAI o3-pro - Top-tier reasoning model for hardest problems</summary>
        [Description("o3-pro")]
        o3_pro,

        /// <summary>OpenAI o3 - Advanced reasoning for complex tasks</summary>
        [Description("o3")]
        o3,

        /// <summary>OpenAI GPT-4.1 - Enhanced GPT-4 with improvements</summary>
        [Description("gpt-4.1")]
        Gpt4_1,

        /// <summary>OpenAI GPT-4.1 Mini - Smaller GPT-4.1 variant</summary>
        [Description("gpt-4.1-mini")]
        Gpt4_1Mini,

        /// <summary>OpenAI GPT-4.1 Nano - Smallest GPT-4.1 variant</summary>
        [Description("gpt-4.1-nano")]
        Gpt4_1Nano,

        /// <summary>OpenAI GPT-4o - Multimodal with vision capabilities</summary>
        [Description("gpt-4o")]
        Gpt4o,

        /// <summary>OpenAI ChatGPT-4o Latest - Latest ChatGPT variant</summary>
        [Description("chatgpt-4o-latest")]
        Gpt4oLatest,

        /// <summary>OpenAI GPT-4o (Nov 2024) - November 2024 release</summary>
        [Description("gpt-4o-2024-11-20")]
        Gpt4o241120,

        /// <summary>OpenAI GPT-4o (Aug 2024) - August 2024 release</summary>
        [Description("gpt-4o-2024-08-06")]
        Gpt4o240806,

        /// <summary>OpenAI GPT-4o Mini - Cost-effective, no vision support</summary>
        [Description("gpt-4o-mini")]
        Gpt4oMini,

        /// <summary>OpenAI GPT-4 Vision Preview - Deprecated, use GPT-4o</summary>
        [Description("gpt-4-vision-preview")]
        Gpt4Vision,


        // Google Gemini Models
        [Description("gemini-2.5-pro")]
        Gemini2_5Pro,

        [Description("gemini-2.5-flash")]
        Gemini2_5Flash,

        [Description("gemini-2.5-flash-lite")]
        Gemini2_5FlashLite,

        [Description("gemini-3-flash-preview")]
        Gemini3FlashPreview,

        [Description("gemini-3-pro-preview")]
        Gemini3ProPreview,

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