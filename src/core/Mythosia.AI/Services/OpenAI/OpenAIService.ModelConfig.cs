using Mythosia.AI.Models;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
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
            else if (IsGpt5_6Model(model))
            {
                ConfigureGpt5_6Parameters(requestBody);
            }
            else if (IsGpt5_5Model(model))
            {
                ConfigureGpt5_5Parameters(requestBody, model);
            }
            else if (IsGpt5_4Model(model))
            {
                ConfigureGpt5_4Parameters(requestBody, model);
            }
            else if (IsGpt5_3Model(model))
            {
                ConfigureGpt5_3Parameters(requestBody, model);
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
            var resolvedEffort = Gpt5ReasoningEffort;
            if (resolvedEffort == Gpt5Reasoning.Auto)
                resolvedEffort = model == "o3-pro" ? Gpt5Reasoning.High : Gpt5Reasoning.Medium;
            else if (resolvedEffort == Gpt5Reasoning.Minimal)
                resolvedEffort = Gpt5Reasoning.Low;

            var effort = resolvedEffort.ToString().ToLowerInvariant();
            var summary = O3ReasoningSummary?.ToString().ToLowerInvariant();
            requestBody["reasoning"] = summary != null
                ? (object)new { effort = effort, summary = summary }
                : new { effort = effort };

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
            var resolvedEffort = Gpt5ReasoningEffort == Gpt5Reasoning.Auto ? Gpt5Reasoning.Medium : Gpt5ReasoningEffort;

            // gpt-5-pro only supports reasoning effort "high" (other values return HTTP 400).
            if (model.StartsWith("gpt-5-pro", StringComparison.OrdinalIgnoreCase))
                resolvedEffort = Gpt5Reasoning.High;

            var effort = resolvedEffort.ToString().ToLowerInvariant();

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

            SetTextVerbosity(requestBody, Gpt5_1Verbosity ?? Verbosity.Medium);
        }

        /// <summary>
        /// Configures GPT-5.2 specific parameters.
        /// GPT-5.2 supports reasoning effort: none (default), low, medium, high, xhigh.
        /// GPT-5.2 Pro supports reasoning effort: medium, high, xhigh.
        /// GPT-5.2 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_2Parameters(Dictionary<string, object> requestBody, string model)
        {
            var resolvedEffort = Gpt5_2ReasoningEffort;
            if (resolvedEffort == Gpt5_2Reasoning.Auto)
            {
                if (model.StartsWith("gpt-5.2-pro", StringComparison.OrdinalIgnoreCase))
                    resolvedEffort = Gpt5_2Reasoning.Medium;
                else
                    resolvedEffort = Gpt5_2Reasoning.None;
            }

            // GPT-5.2 Pro supports only medium/high/xhigh; 'none' and 'low' return HTTP 400.
            if (model.StartsWith("gpt-5.2-pro", StringComparison.OrdinalIgnoreCase) &&
                (resolvedEffort == Gpt5_2Reasoning.None || resolvedEffort == Gpt5_2Reasoning.Low))
            {
                Console.WriteLine("[GPT-5.2 Pro] effort 'none'/'low' is not supported. Adjusting to 'medium'.");
                resolvedEffort = Gpt5_2Reasoning.Medium;
            }

            var effort = resolvedEffort.ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5_2ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            SetTextVerbosity(requestBody, Gpt5_2Verbosity ?? Verbosity.Medium);
        }

        /// <summary>
        /// Configures GPT-5.3 specific parameters.
        /// GPT-5.3 Codex supports reasoning effort: low, medium (default), high, xhigh.
        /// GPT-5.3 Codex Spark uses simplified config with lower defaults.
        /// GPT-5.3 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_3Parameters(Dictionary<string, object> requestBody, string model)
        {
            bool isCodex = IsGpt5_3CodexModel(model);
            var resolvedEffort = Gpt5_3ReasoningEffort;
            if (resolvedEffort == Gpt5_3Reasoning.Auto)
            {
                if (isCodex)
                    resolvedEffort = Gpt5_3Reasoning.Medium;
                else
                    resolvedEffort = Gpt5_3Reasoning.None;
            }

            // GPT-5.3 Codex does not support 'none' reasoning effort
            if (isCodex && resolvedEffort == Gpt5_3Reasoning.None)
            {
                Console.WriteLine("[GPT-5.3 Codex] 'none' reasoning effort is not supported. Adjusting to 'low'.");
                resolvedEffort = Gpt5_3Reasoning.Low;
            }
            var effort = resolvedEffort.ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5_3ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            SetTextVerbosity(requestBody, Gpt5_3Verbosity ?? Verbosity.Medium);
        }

        /// <summary>
        /// Configures GPT-5.4 specific parameters.
        /// GPT-5.4 supports reasoning effort: none (default), low, medium, high, xhigh.
        /// GPT-5.4 Pro supports reasoning effort: medium, high, xhigh.
        /// GPT-5.4 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_4Parameters(Dictionary<string, object> requestBody, string model)
        {
            var resolvedEffort = Gpt5_4ReasoningEffort;
            if (resolvedEffort == Gpt5_4Reasoning.Auto)
            {
                if (model.StartsWith("gpt-5.4-pro", StringComparison.OrdinalIgnoreCase))
                    resolvedEffort = Gpt5_4Reasoning.Medium;
                else
                    resolvedEffort = Gpt5_4Reasoning.None;
            }

            // GPT-5.4 Pro supports only medium/high/xhigh; 'none' and 'low' return HTTP 400.
            if (model.StartsWith("gpt-5.4-pro", StringComparison.OrdinalIgnoreCase) &&
                (resolvedEffort == Gpt5_4Reasoning.None || resolvedEffort == Gpt5_4Reasoning.Low))
            {
                Console.WriteLine("[GPT-5.4 Pro] effort 'none'/'low' is not supported. Adjusting to 'medium'.");
                resolvedEffort = Gpt5_4Reasoning.Medium;
            }

            var effort = resolvedEffort.ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5_4ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            SetTextVerbosity(requestBody, Gpt5_4Verbosity ?? Verbosity.Medium);
        }

        /// <summary>
        /// Configures GPT-5.5 specific parameters.
        /// GPT-5.5 supports reasoning effort: none, low, medium (default), high, xhigh.
        /// GPT-5.5 Pro supports reasoning effort: medium, high (default), xhigh.
        /// GPT-5.5 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_5Parameters(Dictionary<string, object> requestBody, string model)
        {
            var resolvedEffort = Gpt5_5ReasoningEffort;
            if (resolvedEffort == Gpt5_5Reasoning.Auto)
            {
                resolvedEffort = model.StartsWith("gpt-5.5-pro", StringComparison.OrdinalIgnoreCase)
                    ? Gpt5_5Reasoning.High
                    : Gpt5_5Reasoning.Medium;
            }

            // GPT-5.5 Pro supports only medium/high/xhigh; 'none' and 'low' return HTTP 400.
            if (model.StartsWith("gpt-5.5-pro", StringComparison.OrdinalIgnoreCase) &&
                (resolvedEffort == Gpt5_5Reasoning.None || resolvedEffort == Gpt5_5Reasoning.Low))
            {
                Console.WriteLine("[GPT-5.5 Pro] effort 'none'/'low' is not supported. Adjusting to 'medium'.");
                resolvedEffort = Gpt5_5Reasoning.Medium;
            }

            var effort = resolvedEffort.ToString().ToLowerInvariant();

            if (!requestBody.ContainsKey("reasoning"))
            {
                var summary = Gpt5_5ReasoningSummary?.ToString().ToLowerInvariant();
                requestBody["reasoning"] = summary != null
                    ? (object)new { effort = effort, summary = summary }
                    : new { effort = effort };
            }

            SetTextVerbosity(requestBody, Gpt5_5Verbosity ?? Verbosity.Medium);
        }

        /// <summary>
        /// Configures GPT-5.6 specific parameters.
        /// GPT-5.6 supports reasoning effort: none, low, medium (default), high, xhigh, max.
        /// Pro is selected with reasoning.mode rather than a separate model ID.
        /// GPT-5.6 supports text verbosity: low, medium (default), high.
        /// </summary>
        private void ConfigureGpt5_6Parameters(Dictionary<string, object> requestBody)
        {
            var resolvedEffort = Gpt5_6ReasoningEffort == Gpt5_6Reasoning.Auto
                ? Gpt5_6Reasoning.Medium
                : Gpt5_6ReasoningEffort;

            if (!requestBody.ContainsKey("reasoning"))
            {
                var reasoning = new Dictionary<string, object>
                {
                    ["effort"] = resolvedEffort.ToString().ToLowerInvariant(),
                    // Mythosia reconstructs conversation history locally instead of using
                    // previous_response_id. Keep reasoning scoped to the active turn; tool
                    // continuations still replay every output item from that turn.
                    ["context"] = "current_turn"
                };

                var summary = Gpt5_6ReasoningSummary?.ToString().ToLowerInvariant();
                if (summary != null)
                    reasoning["summary"] = summary;

                if (Gpt5_6ReasoningMode == global::Mythosia.AI.Models.Gpt5_6ReasoningMode.Pro)
                    reasoning["mode"] = "pro";

                requestBody["reasoning"] = reasoning;
            }

            SetTextVerbosity(requestBody, Gpt5_6Verbosity ?? Verbosity.Medium);
        }

        private static void SetTextVerbosity(Dictionary<string, object> requestBody, Verbosity verbosity)
        {
            var serializedVerbosity = verbosity.ToString().ToLowerInvariant();
            if (requestBody.TryGetValue("text", out var existingText) &&
                existingText is IDictionary<string, object> text)
            {
                text["verbosity"] = serializedVerbosity;
            }
            else if (!requestBody.ContainsKey("text"))
            {
                requestBody["text"] = new Dictionary<string, object>
                {
                    ["format"] = new Dictionary<string, object> { ["type"] = "text" },
                    ["verbosity"] = serializedVerbosity
                };
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
        /// Matches the entire GPT-5 family, including versioned variants through GPT-5.6.
        /// Used for shared behaviors like New API endpoint, unsupported parameters.
        /// </summary>
        private bool IsGpt5Family(string model)
        {
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches only GPT-5 base models: gpt-5, gpt-5-mini, gpt-5-nano, and snapshots.
        /// Excludes decimal-versioned variants such as gpt-5.1 through gpt-5.6.
        /// </summary>
        private bool IsGpt5Model(string model)
        {
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
                && !model.StartsWith("gpt-5.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.1 models and snapshots.
        /// </summary>
        private bool IsGpt5_1Model(string model)
        {
            return model.StartsWith("gpt-5.1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.2 models: gpt-5.2, gpt-5.2-pro, and their snapshots.
        /// </summary>
        private bool IsGpt5_2Model(string model)
        {
            return model.StartsWith("gpt-5.2", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.3 models and snapshots.
        /// </summary>
        private bool IsGpt5_3Model(string model)
        {
            return model.StartsWith("gpt-5.3", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.4 models: gpt-5.4, gpt-5.4-pro, etc.
        /// </summary>
        private bool IsGpt5_4Model(string model)
        {
            return model.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.5 models: gpt-5.5, gpt-5.5-pro, and their dated snapshots.
        /// </summary>
        private bool IsGpt5_5Model(string model)
        {
            return model.StartsWith("gpt-5.5", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.6 alias and Sol, Terra, and Luna variants.
        /// </summary>
        private bool IsGpt5_6Model(string model)
        {
            return model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches GPT-5.3 Codex models: gpt-5.3-codex and its snapshots.
        /// Codex supports reasoning effort: low, medium (default), high, xhigh (no 'none').
        /// </summary>
        private bool IsGpt5_3CodexModel(string model)
        {
            return model.StartsWith("gpt-5.3-codex", StringComparison.OrdinalIgnoreCase);
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
