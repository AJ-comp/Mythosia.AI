using Mythosia.AI.Models.Enums;
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
            var model = Model.ToLower();

            // Token parameter configuration
            ConfigureTokenParameter(requestBody, model);

            // Model-specific configurations (more specific models first)
            if (IsO3Model(model))
            {
                ConfigureO3Parameters(requestBody, model);
            }
            else if (IsGpt5_2Model(model))
            {
                ConfigureGpt5_2Parameters(requestBody, model);
            }
            else if (IsGpt5_1Model(model))
            {
                ConfigureGpt5_1Parameters(requestBody, model);
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
                requestBody["max_output_tokens"] = (int)GetEffectiveMaxTokens();

                // Remove other token parameters
                requestBody.Remove("max_tokens");
                requestBody.Remove("max_completion_tokens");
            }
            else
            {
                // Standard models use max_tokens
                requestBody["max_tokens"] = GetEffectiveMaxTokens();

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
        /// Configures GPT-5 specific parameters.
        /// GPT-5 family supports reasoning effort: minimal, low, medium, high.
        /// </summary>
        private void ConfigureGpt5Parameters(Dictionary<string, object> requestBody, string model)
        {
            // Use explicitly set reasoning effort, or default based on model variant
            var effort = (Gpt5ReasoningEffort == Gpt5Reasoning.Auto ? Gpt5Reasoning.Medium : Gpt5ReasoningEffort).ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            if (!requestBody.ContainsKey("text"))
            {
                requestBody["text"] = new { format = new { type = "text" } };
            }

            // GPT-5 uses max_output_tokens instead of max_tokens
            // IMPORTANT: reasoning tokens consume from the max_output_tokens budget.
            // If the limit is too low, GPT-5 may exhaust all tokens on reasoning
            // and produce no text output (status: "incomplete").
            const int Gpt5MinOutputTokens = 4096;

            if (requestBody.ContainsKey("max_tokens"))
            {
                requestBody.Remove("max_tokens");
            }

            if (requestBody.TryGetValue("max_output_tokens", out var currentMax) &&
                currentMax is int currentMaxInt && currentMaxInt < Gpt5MinOutputTokens)
            {
                Console.WriteLine($"[GPT-5] max_output_tokens {currentMaxInt} is too low for reasoning models. " +
                    $"Adjusting to {Gpt5MinOutputTokens} to ensure room for both reasoning and text output.");
                requestBody["max_output_tokens"] = Gpt5MinOutputTokens;
            }
            else if (!requestBody.ContainsKey("max_output_tokens"))
            {
                requestBody["max_output_tokens"] = Gpt5MinOutputTokens;
            }
        }

        /// <summary>
        /// Configures GPT-5.1 specific parameters.
        /// GPT-5.1 supports reasoning effort: none (default), low, medium, high.
        /// GPT-5.1 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_1Parameters(Dictionary<string, object> requestBody, string model)
        {
            var effort = (Gpt5_1ReasoningEffort == Gpt5_1Reasoning.Auto ? Gpt5_1Reasoning.None : Gpt5_1ReasoningEffort).ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5_1ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            if (!requestBody.ContainsKey("text"))
            {
                var verbosity = (Gpt5_1Verbosity ?? Verbosity.Medium).ToString().ToLowerInvariant();
                requestBody["text"] = new { format = new { type = "text" }, verbosity = verbosity };
            }

            // GPT-5.1 uses max_output_tokens instead of max_tokens
            const int Gpt5_1MinOutputTokens = 4096;

            if (requestBody.ContainsKey("max_tokens"))
            {
                requestBody.Remove("max_tokens");
            }

            if (requestBody.TryGetValue("max_output_tokens", out var currentMax) &&
                currentMax is int currentMaxInt && currentMaxInt < Gpt5_1MinOutputTokens)
            {
                Console.WriteLine($"[GPT-5.1] max_output_tokens {currentMaxInt} is too low for reasoning models. " +
                    $"Adjusting to {Gpt5_1MinOutputTokens} to ensure room for both reasoning and text output.");
                requestBody["max_output_tokens"] = Gpt5_1MinOutputTokens;
            }
            else if (!requestBody.ContainsKey("max_output_tokens"))
            {
                requestBody["max_output_tokens"] = Gpt5_1MinOutputTokens;
            }
        }

        /// <summary>
        /// Configures GPT-5.2 specific parameters.
        /// GPT-5.2 supports reasoning effort: none (default), low, medium, high, xhigh.
        /// GPT-5.2 Pro supports reasoning effort: medium, high, xhigh.
        /// GPT-5.2 Codex supports reasoning effort: low, medium (default), high, xhigh.
        /// GPT-5.2 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_2Parameters(Dictionary<string, object> requestBody, string model)
        {
            bool isCodex = IsGpt5_2CodexModel(model);
            var resolvedEffort = Gpt5_2ReasoningEffort;
            if (resolvedEffort == Gpt5_2Reasoning.Auto)
            {
                if (model.StartsWith("gpt-5.2-pro", StringComparison.OrdinalIgnoreCase))
                    resolvedEffort = Gpt5_2Reasoning.Medium;
                else if (isCodex)
                    resolvedEffort = Gpt5_2Reasoning.Medium;
                else
                    resolvedEffort = Gpt5_2Reasoning.None;
            }

            // GPT-5.2 Codex does not support 'none' reasoning effort
            if (isCodex && resolvedEffort == Gpt5_2Reasoning.None)
            {
                Console.WriteLine("[GPT-5.2 Codex] 'none' reasoning effort is not supported. Adjusting to 'low'.");
                resolvedEffort = Gpt5_2Reasoning.Low;
            }
            var effort = resolvedEffort.ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5_2ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            if (!requestBody.ContainsKey("text"))
            {
                var verbosity = (Gpt5_2Verbosity ?? Verbosity.Medium).ToString().ToLowerInvariant();
                requestBody["text"] = new { format = new { type = "text" }, verbosity = verbosity };
            }

            // GPT-5.2 uses max_output_tokens instead of max_tokens
            const int Gpt5_2MinOutputTokens = 4096;

            if (requestBody.ContainsKey("max_tokens"))
            {
                requestBody.Remove("max_tokens");
            }

            if (requestBody.TryGetValue("max_output_tokens", out var currentMax) &&
                currentMax is int currentMaxInt && currentMaxInt < Gpt5_2MinOutputTokens)
            {
                Console.WriteLine($"[GPT-5.2] max_output_tokens {currentMaxInt} is too low for reasoning models. " +
                    $"Adjusting to {Gpt5_2MinOutputTokens} to ensure room for both reasoning and text output.");
                requestBody["max_output_tokens"] = Gpt5_2MinOutputTokens;
            }
            else if (!requestBody.ContainsKey("max_output_tokens"))
            {
                requestBody["max_output_tokens"] = Gpt5_2MinOutputTokens;
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

            if (IsGpt5Family(model))
            {
                // GPT-5 family doesn't support these params
                unsupported.Add("frequency_penalty");
                unsupported.Add("presence_penalty");
            }

            return unsupported;
        }

        #region Model Detection Helpers

        /// <summary>
        /// Matches the entire GPT-5 family: gpt-5, gpt-5.1, gpt-5.2 and all variants.
        /// Used for shared behaviors like New API endpoint, unsupported parameters.
        /// </summary>
        private bool IsGpt5Family(string model)
        {
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches only GPT-5 base models: gpt-5, gpt-5-mini, gpt-5-nano, gpt-5-chat-latest, etc.
        /// Excludes gpt-5.1 and gpt-5.2 variants.
        /// </summary>
        private bool IsGpt5Model(string model)
        {
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
                && !model.StartsWith("gpt-5.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.1 models: gpt-5.1, gpt-5.1-chat-latest, etc.
        /// </summary>
        private bool IsGpt5_1Model(string model)
        {
            return model.StartsWith("gpt-5.1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.2 models: gpt-5.2, gpt-5.2-pro, gpt-5.2-codex, gpt-5.2-chat-latest, etc.
        /// </summary>
        private bool IsGpt5_2Model(string model)
        {
            return model.StartsWith("gpt-5.2", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.2 Codex models: gpt-5.2-codex and its snapshots.
        /// Codex supports reasoning effort: low, medium (default), high, xhigh (no 'none').
        /// </summary>
        private bool IsGpt5_2CodexModel(string model)
        {
            return model.StartsWith("gpt-5.2-codex", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsO3Model(string model)
        {
            return model.StartsWith("o3", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGpt4Model(string model)
        {
            return model.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("4o");
        }

        /// <summary>
        /// Determines if the model uses the Responses API (/v1/responses).
        /// All GPT-5 family, o3, and GPT-4.1 models use the new API.
        /// </summary>
        private bool IsNewApiModel(string model)
        {
            return IsGpt5Family(model) ||
                   IsO3Model(model) ||
                   model.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}