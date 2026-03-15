namespace Mythosia.AI.Models
{
    public enum AIRequestPurpose
    {
        Default,
        QueryRewrite,
        Summarization
    }

    public sealed class AIRequestProfile
    {
        public AIRequestPurpose Purpose { get; set; } = AIRequestPurpose.Default;
        public bool? Stateless { get; set; }
        public bool? DisableFunctions { get; set; }
        public bool? DisableReasoning { get; set; }
        public float? Temperature { get; set; }
        public uint? MaxTokens { get; set; }
    }

    public static class RequestProfiles
    {
        public static AIRequestProfile QueryRewrite => new AIRequestProfile
        {
            Purpose = AIRequestPurpose.QueryRewrite,
            Stateless = true,
            DisableFunctions = true,
            DisableReasoning = true,
            Temperature = 0.1f,
            MaxTokens = 128
        };

        public static AIRequestProfile Summarization => new AIRequestProfile
        {
            Purpose = AIRequestPurpose.Summarization,
            Stateless = true,
            DisableFunctions = true,
            DisableReasoning = true,
            Temperature = 0.2f,
            MaxTokens = 256
        };
    }
}
