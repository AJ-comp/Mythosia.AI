namespace Mythosia.AI.Models
{
    /// <summary>Reasoning effort for DeepSeek thinking mode. Enable thinking separately with ThinkingEnabled.</summary>
    public enum DeepSeekReasoning
    {
        /// <summary>Use the provider default (High) without sending reasoning_effort.</summary>
        Auto,
        /// <summary>Use less reasoning for shorter response times.</summary>
        Low,
        /// <summary>Use the standard reasoning effort.</summary>
        High,
        /// <summary>Use maximum reasoning effort for complex tasks.</summary>
        Max
    }
}
