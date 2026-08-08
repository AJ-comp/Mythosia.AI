using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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
        /// <param name="context">Optional per-call request context (e.g. dynamic system message prefix/suffix).</param>
        /// <returns>The final text response from the LLM after completing the goal</returns>
        /// <exception cref="AgentMaxStepsExceededException">
        /// Thrown when maxSteps is exceeded without a final answer.
        /// The exception contains a PartialResponse property with the last assistant message, if any.
        /// </exception>
        public virtual async Task<string> RunAgentAsync(string goal, int maxSteps = 10, AIRequestContext? context = null)
        {
            var agentPolicy = (DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            agentPolicy.MaxRounds = maxSteps;

            CurrentPolicy = agentPolicy;

            try
            {
                return await GetCompletionAsync(goal, context: context);
            }
            catch (AIServiceException ex) when (ex.Message.Contains("Maximum rounds"))
            {
                throw CreateAgentMaxStepsExceededException(maxSteps);
            }
        }

        /// <summary>
        /// Runs the ReAct agent loop using the streaming pipeline.
        /// <para>
        /// This is the streaming counterpart to <see cref="RunAgentAsync(string, int, AIRequestContext?)"/>.
        /// Function calling is forced on for this request so the agent can act, and
        /// <see cref="StreamOptions.TextOnly"/> is disabled so the stream can emit a
        /// final <see cref="StreamingContentType.Completion"/> event.
        /// </para>
        /// </summary>
        /// <param name="goal">The goal or task for the agent to accomplish.</param>
        /// <param name="maxSteps">Maximum number of agent steps (LLM round-trips). Default is 10.</param>
        /// <param name="options">Optional streaming options. Function calling will be enabled automatically.</param>
        /// <param name="context">Optional per-call request context (e.g. dynamic system message prefix/suffix).</param>
        /// <param name="cancellationToken">Cancellation token for the streaming operation.</param>
        /// <returns>A stream of agent events including text, function calls, and function results.</returns>
        /// <exception cref="AgentMaxStepsExceededException">
        /// Thrown when maxSteps is exceeded without a final completion event.
        /// The exception contains a PartialResponse property with the last assistant message, if any.
        /// </exception>
        public virtual async IAsyncEnumerable<StreamingContent> RunAgentStreamAsync(
            string goal,
            int maxSteps = 10,
            StreamOptions? options = null,
            AIRequestContext? context = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var agentPolicy = (DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            agentPolicy.MaxRounds = maxSteps;

            var agentOptions = (options ?? StreamOptions.WithFunctions).Clone();
            agentOptions.IncludeFunctionCalls = true;
            agentOptions.TextOnly = false;

            CurrentPolicy = agentPolicy;

            var completed = false;
            var sawError = false;

            var goalMessage = new Message(ActorRole.User, goal);

            await using var enumerator = StreamAsync(goalMessage, agentOptions, context, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                StreamingContent content;

                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                }
                catch (AIServiceException ex) when (ex.Message.Contains("Maximum rounds"))
                {
                    throw CreateAgentMaxStepsExceededException(maxSteps);
                }

                content = enumerator.Current;

                if (content.Type == StreamingContentType.Completion)
                    completed = true;
                else if (content.Type == StreamingContentType.Error)
                    sawError = true;

                yield return content;
            }

            if (!completed && !sawError && !cancellationToken.IsCancellationRequested)
                throw CreateAgentMaxStepsExceededException(maxSteps);
        }

        private AgentMaxStepsExceededException CreateAgentMaxStepsExceededException(int maxSteps)
        {
            var lastResponse = ActivateChat.Messages
                .LastOrDefault(m => m.Role == ActorRole.Assistant &&
                                   !string.IsNullOrEmpty(m.Content) &&
                                   m.FunctionCallBatch == null &&
                                   m.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() != "function_call");

            return new AgentMaxStepsExceededException(maxSteps, lastResponse?.Content);
        }

        #endregion
    }
}
