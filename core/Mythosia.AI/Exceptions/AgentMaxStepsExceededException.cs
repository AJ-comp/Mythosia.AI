namespace Mythosia.AI.Exceptions
{
    /// <summary>
    /// Exception thrown when a ReAct agent exceeds the maximum number of steps
    /// without producing a final answer.
    /// </summary>
    public class AgentMaxStepsExceededException : AIServiceException
    {
        /// <summary>
        /// The maximum number of steps that was configured.
        /// </summary>
        public int MaxSteps { get; }

        /// <summary>
        /// The last partial response from the agent, if any was produced during execution.
        /// </summary>
        public string? PartialResponse { get; }

        public AgentMaxStepsExceededException(int maxSteps, string? partialResponse = null)
            : base(BuildMessage(maxSteps, partialResponse))
        {
            MaxSteps = maxSteps;
            PartialResponse = partialResponse;
        }

        private static string BuildMessage(int maxSteps, string? partialResponse)
        {
            var msg = $"Agent exceeded maximum steps ({maxSteps}) without producing a final answer.";
            if (partialResponse != null)
                msg += $" Last partial response: {partialResponse}";
            return msg;
        }
    }
}
