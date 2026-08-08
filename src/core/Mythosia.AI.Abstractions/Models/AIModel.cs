namespace Mythosia.AI.Models
{
    public static class AIModels
    {
        public static class OpenAI
        {
            public const string Gpt5 = "gpt-5";
            public const string Gpt5Mini = "gpt-5-mini";
            public const string Gpt5Nano = "gpt-5-nano";
            public const string Gpt5Pro = "gpt-5-pro";
            public const string Gpt5_1 = "gpt-5.1";
            public const string Gpt5_2 = "gpt-5.2";
            public const string Gpt5_2Pro = "gpt-5.2-pro";
            public const string Gpt5_3Codex = "gpt-5.3-codex";
            public const string Gpt5_4 = "gpt-5.4";
            public const string Gpt5_4Mini = "gpt-5.4-mini";
            public const string Gpt5_4Nano = "gpt-5.4-nano";
            public const string Gpt5_4Pro = "gpt-5.4-pro";
            public const string Gpt5_5 = "gpt-5.5";
            public const string Gpt5_5_260423 = "gpt-5.5-2026-04-23";
            public const string Gpt5_5Pro = "gpt-5.5-pro";
            public const string Gpt5_5Pro_260423 = "gpt-5.5-pro-2026-04-23";
            public const string Gpt5_6 = "gpt-5.6";
            public const string Gpt5_6Sol = "gpt-5.6-sol";
            public const string Gpt5_6Terra = "gpt-5.6-terra";
            public const string Gpt5_6Luna = "gpt-5.6-luna";
            public const string GptImage2 = "gpt-image-2";
            public const string GptImage2_260421 = "gpt-image-2-2026-04-21";
            public const string O3Pro = "o3-pro";
            public const string O3 = "o3";
            public const string Gpt4_1 = "gpt-4.1";
            public const string Gpt4_1Mini = "gpt-4.1-mini";
            public const string Gpt4o = "gpt-4o";
            public const string Gpt4o241120 = "gpt-4o-2024-11-20";
            public const string Gpt4o240806 = "gpt-4o-2024-08-06";
            public const string Gpt4oMini = "gpt-4o-mini";
        }

        public static class Anthropic
        {
            public const string ClaudeFable5 = "claude-fable-5";
            public const string ClaudeMythos5 = "claude-mythos-5";
            public const string ClaudeOpus5 = "claude-opus-5";
            public const string ClaudeSonnet5 = "claude-sonnet-5";
            public const string ClaudeOpus4_8 = "claude-opus-4-8";
            public const string ClaudeOpus4_7 = "claude-opus-4-7";
            public const string ClaudeOpus4_6 = "claude-opus-4-6";
            public const string ClaudeSonnet4_6 = "claude-sonnet-4-6";
            public const string ClaudeOpus4_5_251101 = "claude-opus-4-5-20251101";
            public const string ClaudeSonnet4_5_250929 = "claude-sonnet-4-5-20250929";
            public const string ClaudeHaiku4_5_251001 = "claude-haiku-4-5-20251001";
        }

        public static class Google
        {
            public const string Gemini2_5Pro = "gemini-2.5-pro";
            public const string Gemini2_5Flash = "gemini-2.5-flash";
            public const string Gemini2_5FlashLite = "gemini-2.5-flash-lite";
            public const string Gemini3FlashPreview = "gemini-3-flash-preview";
            public const string Gemini3_1ProPreview = "gemini-3.1-pro-preview";
            public const string Gemini3_1FlashLite = "gemini-3.1-flash-lite";
            public const string Gemini3_5Flash = "gemini-3.5-flash";
            public const string Gemini3_5FlashLite = "gemini-3.5-flash-lite";
            public const string Gemini3_6Flash = "gemini-3.6-flash";

            public static class Images
            {
                public const string Gemini3_1FlashImage = "gemini-3.1-flash-image";
                public const string Gemini3_1FlashLiteImage = "gemini-3.1-flash-lite-image";
                public const string Gemini3ProImage = "gemini-3-pro-image";
            }
        }

        public static class xAI
        {
            public const string Grok4_5 = "grok-4.5";
            public const string Grok4_5Latest = "grok-4.5-latest";
            public const string GrokBuildLatest = "grok-build-latest";
            public const string Grok4_3 = "grok-4.3";
            public const string Grok4_3Latest = "grok-4.3-latest";
            public const string GrokLatest = "grok-latest";
            public const string Grok4_20Reasoning = "grok-4.20-0309-reasoning";
            public const string Grok4_20NonReasoning = "grok-4.20-0309-non-reasoning";
            public const string GrokBuild0_1 = "grok-build-0.1";
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
            public const string SonarReasoningPro = "sonar-reasoning-pro";
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
