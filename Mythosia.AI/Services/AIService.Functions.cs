using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        #region Function Calling Support

        /// <summary>
        /// Process function call
        /// </summary>
        protected virtual async Task<string> ProcessFunctionCallAsync(
            string functionName,
            Dictionary<string, object> arguments)
        {
            var function = ActivateChat.Functions
                .FirstOrDefault(f => f.Name == functionName);

            if (function?.Handler == null)
            {
                return $"Error: Function '{functionName}' not found";
            }

            try
            {
                return await function.Handler(arguments);
            }
            catch (Exception ex)
            {
                return $"Error executing function: {ex.Message}";
            }
        }

        /// <summary>
        /// Creates HTTP request with function definitions
        /// </summary>
        protected abstract HttpRequestMessage CreateFunctionMessageRequest();

        /// <summary>
        /// Extracts function call from API response
        /// </summary>
        protected abstract (string content, FunctionCall functionCall) ExtractFunctionCall(string response);

        #endregion
    }
}