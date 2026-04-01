using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Services
{
    /// <summary>
    /// Marks an AI service as capable of accepting function (tool) registrations at runtime.
    /// Implemented by <c>AIService</c> in the core package.
    /// </summary>
    public interface IFunctionRegisterable
    {
        /// <summary>
        /// Registers a function definition so the LLM can call it during a ReAct loop.
        /// </summary>
        void AddFunction(FunctionDefinition function);
    }
}
