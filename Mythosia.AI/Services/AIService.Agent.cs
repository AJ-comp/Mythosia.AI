using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        #region Agent Methods

        /// <summary>
        /// Runs a ReAct (Reasoning + Acting) agent loop that repeatedly calls the LLM
        /// and executes function calls until the goal is achieved or maxSteps is exceeded.
        /// <para>
        /// Reuses existing function calling infrastructure registered via WithFunction.
        /// The loop terminates when the LLM returns a text response without any function calls,
        /// or when maxSteps is exceeded.
        /// </para>
        /// </summary>
        /// <param name="goal">The goal or task for the agent to accomplish</param>
        /// <param name="maxSteps">Maximum number of agent steps (LLM round-trips) to prevent infinite loops. Default is 10.</param>
        /// <returns>The final text response from the LLM after completing the goal</returns>
        /// <exception cref="AgentMaxStepsExceededException">
        /// Thrown when maxSteps is exceeded without a final answer.
        /// The exception contains a PartialResponse property with the last assistant message, if any.
        /// </exception>
        public virtual async Task<string> RunAgentAsync(string goal, int maxSteps = 10)
        {
            var agentPolicy = (DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            agentPolicy.MaxRounds = maxSteps;

            CurrentPolicy = agentPolicy;

            try
            {
                return await GetCompletionAsync(goal);
            }
            catch (AIServiceException ex) when (ex.Message.Contains("Maximum rounds"))
            {
                var lastResponse = ActivateChat.Messages
                    .LastOrDefault(m => m.Role == ActorRole.Assistant &&
                                       !string.IsNullOrEmpty(m.Content) &&
                                       m.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() != "function_call");

                throw new AgentMaxStepsExceededException(maxSteps, lastResponse?.Content);
            }
        }

        #endregion
    }
}
