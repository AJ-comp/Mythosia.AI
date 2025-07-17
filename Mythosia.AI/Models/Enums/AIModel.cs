using System.ComponentModel;

namespace Mythosia.AI.Models.Enums
{
    public enum AIModel
    {
        // Anthropic Claude Models
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
        [Description("gpt-3.5-turbo-1106")]
        Gpt3_5Turbo,

        [Description("gpt-4-0613")]
        Gpt4,

        [Description("gpt-4-1106-preview")]
        Gpt4Turbo,

        [Description("gpt-4-vision-preview")]
        Gpt4Vision,

        [Description("chatgpt-4o-latest")]
        Gpt4oLatest,

        [Description("gpt-4o-2024-11-20")]
        Gpt4o241120,

        [Description("gpt-4o-2024-08-06")]
        Gpt4o240806,

        [Description("gpt-4o-mini-2024-07-18")]
        Gpt4oMini,

        // Google Gemini Models
        [Description("gemini-1.5-flash")]
        Gemini15Flash,

        [Description("gemini-1.5-pro")]
        Gemini15Pro,

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