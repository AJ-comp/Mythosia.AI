using Mythosia.AI.Models;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Fluent builder for streaming structured output.
    /// Created by <see cref="AIService.BeginStream(string)"/>.
    /// <code>
    /// var run = service.BeginStream(prompt)
    ///     .WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = 2 })
    ///     .As&lt;MyDto&gt;();
    /// </code>
    /// </summary>
    public class StreamBuilder
    {
        internal AIService Service { get; }
        internal string Prompt { get; }
        internal StructuredOutputPolicy? Policy { get; private set; }

        internal StreamBuilder(AIService service, string prompt)
        {
            Service = service;
            Prompt = prompt;
        }

        /// <summary>
        /// Configure structured output policy for this streaming run.
        /// If not called, the service-level <see cref="AIService.StructuredOutputMaxRetries"/> is used.
        /// </summary>
        public StreamBuilder WithStructuredOutput(StructuredOutputPolicy policy)
        {
            Policy = policy;
            return this;
        }

        /// <summary>
        /// Begin a streaming run that will deserialize the final result to <typeparamref name="T"/>.
        /// The stream starts immediately in the background.
        /// </summary>
        /// <typeparam name="T">The target POCO type to deserialize to.</typeparam>
        public StructuredStreamRun<T> As<T>() where T : class
        {
            return new StructuredStreamRun<T>(Service, Prompt, Policy);
        }
    }
}
