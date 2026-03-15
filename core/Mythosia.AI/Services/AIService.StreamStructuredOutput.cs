using Mythosia.AI.Models.Streaming;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        /// <summary>
        /// Begin a streaming structured output run.
        /// Returns a <see cref="StreamBuilder"/> for fluent configuration.
        /// <para>
        /// <b>Example:</b>
        /// <code>
        /// var run = service.BeginStream(prompt)
        ///     .WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = 2 })
        ///     .As&lt;MyDto&gt;();
        ///
        /// await foreach (var chunk in run.Stream(ct))
        ///     Console.Write(chunk);
        ///
        /// MyDto dto = await run.Result;
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="prompt">The user prompt to send to the LLM.</param>
        /// <returns>A <see cref="StreamBuilder"/> for fluent configuration.</returns>
        public StreamBuilder BeginStream(string prompt)
        {
            return new StreamBuilder(this, prompt);
        }
    }
}
