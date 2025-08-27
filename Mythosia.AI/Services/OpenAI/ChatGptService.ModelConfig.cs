using System;
using System.Collections.Generic;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        /// <summary>
        /// Applies model-specific parameter configurations to the request body
        /// </summary>
        private void ApplyModelSpecificParameters(Dictionary<string, object> requestBody)
        {
            var model = ActivateChat.Model.ToLower();

            // Token parameter configuration
            ConfigureTokenParameter(requestBody, model);

            // Model-specific configurations
            if (IsO3Model(model))
            {
                ConfigureO3Parameters(requestBody, model);
            }
            else if (IsGpt5Model(model))
            {
                ConfigureGpt5Parameters(requestBody, model);
            }
            else if (IsGpt4Model(model))
            {
                ConfigureGpt4Parameters(requestBody, model);
            }

            // Remove unsupported parameters for specific models
            RemoveUnsupportedParameters(requestBody, model);
        }

        /// <summary>
        /// Configures the token parameter name based on model
        /// </summary>
        private void ConfigureTokenParameter(Dictionary<string, object> requestBody, string model)
        {
            if (IsO3Model(model) || IsNewApiModel(model))
            {
                // o3 and new API models use max_output_tokens
                requestBody["max_output_tokens"] = (int)ActivateChat.MaxTokens;

                // Remove other token parameters
                requestBody.Remove("max_tokens");
                requestBody.Remove("max_completion_tokens");
            }
            else
            {
                // Standard models use max_tokens
                requestBody["max_tokens"] = ActivateChat.MaxTokens;

                // Remove new API parameters
                requestBody.Remove("max_output_tokens");
                requestBody.Remove("max_completion_tokens");
            }
        }

        /// <summary>
        /// Configures o3-specific parameters
        /// </summary>
        private void ConfigureO3Parameters(Dictionary<string, object> requestBody, string model)
        {
            // o3 models use nested reasoning object
            if (model == "o3-pro")
            {
                requestBody["reasoning"] = new { effort = "high" };
            }
            else if (model == "o3-mini")
            {
                requestBody["reasoning"] = new { effort = "low" };
            }
            else if (model == "o3")
            {
                requestBody["reasoning"] = new { effort = "medium" };
            }

            // Remove incorrect parameter if it exists
            requestBody.Remove("reasoning_effort");

            // o3 models don't support these parameters
            requestBody.Remove("frequency_penalty");
            requestBody.Remove("presence_penalty");
            requestBody.Remove("top_p");
            requestBody.Remove("temperature");  // o3 might not support temperature either
        }

        /// <summary>
        /// Configures GPT-5 specific parameters
        /// </summary>
        private void ConfigureGpt5Parameters(Dictionary<string, object> requestBody, string model)
        {
            // GPT-5 uses different parameter structure
            if (!requestBody.ContainsKey("reasoning"))
            {
                requestBody["reasoning"] = new { effort = "medium" };
            }

            if (!requestBody.ContainsKey("text"))
            {
                requestBody["text"] = new { verbosity = "medium" };
            }

            // GPT-5 uses max_output_tokens instead of max_tokens
            if (requestBody.ContainsKey("max_tokens"))
            {
                requestBody["max_output_tokens"] = requestBody["max_tokens"];
                requestBody.Remove("max_tokens");
            }
        }

        /// <summary>
        /// Configures GPT-4 specific parameters
        /// </summary>
        private void ConfigureGpt4Parameters(Dictionary<string, object> requestBody, string model)
        {
            // GPT-4 standard configuration
            // Most parameters are already correctly set

            if (model.Contains("vision") || model.Contains("4o"))
            {
                // Vision models might have specific requirements
                // Ensure image detail level is set if needed
            }
        }

        /// <summary>
        /// Removes parameters not supported by specific models
        /// </summary>
        private void RemoveUnsupportedParameters(Dictionary<string, object> requestBody, string model)
        {
            // Define unsupported parameters per model family
            var unsupportedParams = GetUnsupportedParameters(model);

            foreach (var param in unsupportedParams)
            {
                requestBody.Remove(param);
            }
        }

        /// <summary>
        /// Gets list of unsupported parameters for a model
        /// </summary>
        private List<string> GetUnsupportedParameters(string model)
        {
            var unsupported = new List<string>();

            if (IsO3Model(model))
            {
                // o3 models don't support these
                unsupported.Add("logit_bias");
                unsupported.Add("top_p"); // Some o3 models might not support this
            }

            if (IsGpt5Model(model))
            {
                // GPT-5 might have different unsupported params
                unsupported.Add("frequency_penalty");
                unsupported.Add("presence_penalty");
            }

            return unsupported;
        }

        #region Model Detection Helpers

        private bool IsO3Model(string model)
        {
            return model.StartsWith("o3", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGpt5Model(string model)
        {
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGpt4Model(string model)
        {
            return model.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("4o");
        }

        private bool IsNewApiModel(string model)
        {
            return IsGpt5Model(model) ||
                   IsO3Model(model) ||
                   model.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}