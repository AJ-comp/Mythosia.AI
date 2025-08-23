using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Base;
using System.Threading.Tasks;

namespace Mythosia.AI.Extensions
{
    public static class AIServiceFCExtensions
    {
        /// <summary>
        /// Temporarily disable functions (like StatelessMode)
        /// </summary>
        public static AIService WithoutFunctions(this AIService service, bool disable = true)
        {
            service.FunctionsDisabled = disable;
            return service;
        }

        /// <summary>
        /// Enable/disable functions for the chat
        /// </summary>
        public static AIService WithFunctionsEnabled(this AIService service, bool enabled = true)
        {
            service.ActivateChat.EnableFunctions = enabled;
            return service;
        }

        /// <summary>
        /// Execute with functions temporarily disabled
        /// </summary>
        public static async Task<string> AskWithoutFunctionsAsync(this AIService service, string prompt)
        {
            var backup = service.FunctionsDisabled;
            service.FunctionsDisabled = true;
            try
            {
                return await service.GetCompletionAsync(prompt);
            }
            finally
            {
                service.FunctionsDisabled = backup;
            }
        }

        /// <summary>
        /// Force a specific function to be called
        /// </summary>
        public static async Task<string> CallFunctionAsync(
            this AIService service,
            string functionName,
            string prompt)
        {
            var backupMode = service.ActivateChat.FunctionCallMode;
            var backupForce = service.ActivateChat.ForceFunctionName;

            service.ActivateChat.FunctionCallMode = FunctionCallMode.Force;
            service.ActivateChat.ForceFunctionName = functionName;

            try
            {
                return await service.GetCompletionAsync(prompt);
            }
            finally
            {
                service.ActivateChat.FunctionCallMode = backupMode;
                service.ActivateChat.ForceFunctionName = backupForce;
            }
        }
    }
}