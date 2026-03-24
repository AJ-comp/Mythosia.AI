namespace Mythosia.AI.Models
{
    public static class AIModels
    {
        public static class OpenAI
        {
            public const string Gpt5 = "gpt-5";
            public const string Gpt5Mini = "gpt-5-mini";
            public const string Gpt5Nano = "gpt-5-nano";
            public const string Gpt5_250807 = "gpt-5-2025-08-07";
            public const string Gpt5Mini_250807 = "gpt-5-mini-2025-08-07";
            public const string Gpt5Nano_250807 = "gpt-5-nano-2025-08-07";
            public const string Gpt5ChatLatest = "gpt-5-chat-latest";
            public const string Gpt5_1 = "gpt-5.1";
            public const string Gpt5_2 = "gpt-5.2";
            public const string Gpt5_2Pro = "gpt-5.2-pro";
            public const string Gpt5_2Codex = "gpt-5.2-codex";
            public const string Gpt5_3Codex = "gpt-5.3-codex";
            public const string Gpt5_4 = "gpt-5.4";
            public const string Gpt5_4Mini = "gpt-5.4-mini";
            public const string Gpt5_4Nano = "gpt-5.4-nano";
            public const string Gpt5_4Pro = "gpt-5.4-pro";
            public const string O3Pro = "o3-pro";
            public const string O3 = "o3";
            public const string Gpt4_1 = "gpt-4.1";
            public const string Gpt4_1Mini = "gpt-4.1-mini";
            public const string Gpt4_1Nano = "gpt-4.1-nano";
            public const string Gpt4o = "gpt-4o";
            public const string Gpt4oLatest = "chatgpt-4o-latest";
            public const string Gpt4o241120 = "gpt-4o-2024-11-20";
            public const string Gpt4o240806 = "gpt-4o-2024-08-06";
            public const string Gpt4oMini = "gpt-4o-mini";
            public const string Gpt4Vision = "gpt-4-vision-preview";
        }

        public static class Anthropic
        {
            public const string ClaudeOpus4_6 = "claude-opus-4-6";
            public const string ClaudeSonnet4_6 = "claude-sonnet-4-6";
            public const string ClaudeOpus4_1_250805 = "claude-opus-4-1-20250805";
            public const string ClaudeOpus4_250514 = "claude-opus-4-20250514";
            public const string ClaudeOpus4_5_251101 = "claude-opus-4-5-20251101";
            public const string ClaudeSonnet4_5_250929 = "claude-sonnet-4-5-20250929";
            public const string ClaudeSonnet4_250514 = "claude-sonnet-4-20250514";
            public const string ClaudeHaiku4_5_251001 = "claude-haiku-4-5-20251001";
        }

        public static class Google
        {
            public const string Gemini2_5Pro = "gemini-2.5-pro";
            public const string Gemini2_5Flash = "gemini-2.5-flash";
            public const string Gemini2_5FlashLite = "gemini-2.5-flash-lite";
            public const string Gemini3FlashPreview = "gemini-3-flash-preview";
            public const string Gemini3ProPreview = "gemini-3-pro-preview";
        }

        public static class xAI
        {
            public const string Grok4 = "grok-4-0709";
            public const string Grok4_1Fast = "grok-4-1-fast";
            public const string Grok3 = "grok-3";
            public const string Grok3Mini = "grok-3-mini";
        }

        public static class DeepSeek
        {
            public const string Chat = "deepseek-chat";
            public const string Reasoner = "deepseek-reasoner";
        }

        public static class Perplexity
        {
            public const string Sonar = "sonar";
            public const string SonarPro = "sonar-pro";
            public const string SonarReasoning = "sonar-reasoning";
        }
    }

    public enum AIProvider
    {
        OpenAI,
        Anthropic,
        Google,
        xAI,
        DeepSeek,
        Perplexity,
    }
}