namespace Mythosia.AI.Providers.Alibaba
{
    public static class AlibabaProvider
    {
        public const string Name = "Alibaba";
    }

    public static class AlibabaModels
    {
        public const string QwenMax = "qwen-max-latest";
        public const string QwenPlus = "qwen-plus-latest";
        public const string QwenTurbo = "qwen-turbo-latest";
        public const string Qwen3_235B = "qwen3-235b-a22b";
        public const string Qwen3_30B = "qwen3-30b-a3b";
        public const string Qwen3_32B = "qwen3-32b";
        public const string Qwen3_14B = "qwen3-14b";
        public const string Qwen3_8B = "qwen3-8b";
        public const string Qwen3_4B = "qwen3-4b";
        public const string Qwen3_1_7B = "qwen3-1.7b";
        public const string Qwen3_0_6B = "qwen3-0.6b";
        public const string Qwen3_5_397B = "qwen3.5-397b-a17b";
        public const string Qwen3_5_122B = "qwen3.5-122b-a10b";
        public const string Qwen3_5_35B = "qwen3.5-35b-a3b";
        public const string Qwen3_5_27B = "qwen3.5-27b";
        public const string Qwen3_5_9B = "qwen3.5-9b";
        public const string Qwen3_5_4B = "qwen3.5-4b";
        public const string Qwen3_5_2B = "qwen3.5-2b";
        public const string Qwen3_5_0_8B = "qwen3.5-0.8b";
    }

    public enum QwenThinking
    {
        Off,
        On
    }

    public enum EndpointPlatform
    {
        DashScope = 0,
        Ollama = 1,
        Vllm = 2,
    }
}
