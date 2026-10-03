using Mythosia.AI.Models;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        // Profiles replace one immutable settings group in the execution frame. They never
        // partially edit the service's native defaults or the captured provider options.
        private sealed class ClaudeProfileThinking
        {
            public ClaudeProfileThinking(ClaudeReasoningEffort effort, ClaudeThinkingPrefixMismatchBehavior? binding)
            { Effort = effort; Binding = binding; }
            public ClaudeReasoningEffort Effort { get; }
            public ClaudeThinkingPrefixMismatchBehavior? Binding { get; }
        }

        private ClaudeProfileThinking? ProfileThinking => RequestSetting<ClaudeProfileThinking?>(nameof(ProfileThinking), null);
        private ClaudeThinkingPrefixMismatchBehavior? EffectiveClaudeBinding =>
            ProfileThinking is ClaudeProfileThinking profile ? profile.Binding : ClaudeOptions.Binding;

        // This is a wire decision, not mutable JSON. All generation paths serialize this
        // same decision; no later binding/display pass may turn disabled thinking back on.
        private sealed class ClaudeThinkingPlan
        {
            public ClaudeThinkingPlan(string? type = null, string? effort = null, string? display = null,
                int? budget = null, ClaudeThinkingPrefixMismatchBehavior? binding = null,
                uint? outputTokens = null, bool omitTemperature = false, bool unitTemperature = false)
            {
                Type = type; Effort = effort; Display = display; Budget = budget; Binding = binding;
                OutputTokens = outputTokens; OmitTemperature = omitTemperature; UnitTemperature = unitTemperature;
            }
            public string? Type { get; }
            public string? Effort { get; }
            public string? Display { get; }
            public int? Budget { get; }
            public ClaudeThinkingPrefixMismatchBehavior? Binding { get; }
            public uint? OutputTokens { get; }
            public bool OmitTemperature { get; }
            public bool UnitTemperature { get; }
            public bool Disabled => Type == "disabled" || Type == "between_tools";
            public bool Adaptive => Type == "adaptive";
            public ClaudeThinkingPlan WithControls(string? display, ClaudeThinkingPrefixMismatchBehavior? binding) =>
                new ClaudeThinkingPlan(Type, Effort, display, Budget, binding);
        }

        private ClaudeThinkingPlan ResolveClaudeThinkingPlan(bool validate = true, bool preserveConversation = false,
            bool tokenCount = false)
        {
            var plan = ResolveClaudeThinkingMode(validate, preserveConversation);
            uint? outputTokens = null;
            if (!tokenCount && plan.Budget.HasValue)
            {
                var budget = (uint)plan.Budget.Value;
                var modelMax = GetModelMaxOutputTokens();
                if (validate && budget >= modelMax)
                    throw new ArgumentOutOfRangeException(nameof(ThinkingBudget), plan.Budget.Value,
                        $"Claude manual thinking requires ThinkingBudget to be lower than the model's maximum output tokens ({modelMax}) so max_tokens can remain larger.");
                if (budget >= GetEffectiveMaxTokens()) outputTokens = Math.Min(budget + 1024, modelMax);
            }
            return new ClaudeThinkingPlan(plan.Type, plan.Effort, plan.Display, plan.Budget, plan.Binding,
                outputTokens, ModelRejectsCustomTemperature() || plan.Adaptive || plan.Type == "between_tools",
                plan.Budget.HasValue);
        }

        private ClaudeThinkingPlan ResolveClaudeThinkingMode(bool validate, bool preserveConversation)
        {
            // A preserved conversation owns its initial phase/effort. Select that baseline
            // before validating native defaults, which may no longer describe this conversation.
            if (preserveConversation && _claudeReasoningBaselines.TryGetValue(ActivateChat, out var baseline) &&
                baseline.PersistentEffort != null && baseline.Model == RequestModel && baseline.Plan != null)
            {
                var display = baseline.Plan.Display;
                if (baseline.Plan.Adaptive && baseline.DisplaySetting != RequestAdaptiveThinkingDisplay)
                    display = RequestAdaptiveThinkingDisplay.ToString().ToLowerInvariant();
                return ResolveClaudeThinkingControls(baseline.Plan.WithControls(display, null), validate);
            }
            var reasoning = CurrentRequestFeatures.Reasoning;
            ClaudeThinkingPlan plan;
            // Common non-Auto reasoning replaces the native mode before native validation.
            if (reasoning != null && reasoning.Level != ReasoningLevel.Auto)
            {
                if (reasoning.Level == ReasoningLevel.None)
                    plan = IsClaudeSonnet55Model()
                        ? new ClaudeThinkingPlan("between_tools", "high") : new ClaudeThinkingPlan("disabled");
                else if (ModelSupportsAdaptiveThinking())
                    plan = new ClaudeThinkingPlan("adaptive", reasoning.Level.ToString().ToLowerInvariant(),
                        RequestAdaptiveThinkingDisplay.ToString().ToLowerInvariant());
                else
                {
                    var native = ResolveNativeClaudeThinking(validate);
                    plan = new ClaudeThinkingPlan(native.Type, reasoning.Level.ToString().ToLowerInvariant(), native.Display, native.Budget);
                }
            }
            else
                plan = ResolveNativeClaudeThinking(validate);

            if (reasoning?.Cache == CachePreservation.Required && reasoning.Level == ReasoningLevel.Auto)
                plan = new ClaudeThinkingPlan("adaptive", plan.Effort);

            return ResolveClaudeThinkingControls(plan, validate);
        }

        private ClaudeThinkingPlan ResolveNativeClaudeThinking(bool validate)
        {
            // Validate only the native settings actually selected for this request. Common
            // reasoning overrides and isolated profiles may replace the service defaults.
            if (validate && !Enum.IsDefined(typeof(ClaudeReasoningEffort), RequestAdaptiveThinkingEffort))
                throw new ArgumentOutOfRangeException(nameof(AdaptiveThinkingEffort), RequestAdaptiveThinkingEffort,
                    "Select a defined Claude reasoning effort.");
            if (IsClaudeSonnet55Model())
            {
                if (UsesSonnet55BetweenTools())
                {
                    var effort = RequestAdaptiveThinkingEffort == ClaudeReasoningEffort.Auto
                        ? "high" : ResolveAdaptiveThinkingEffort(validate);
                    if (validate && (effort == "xhigh" || effort == "max"))
                        throw new NotSupportedException("Sonnet 5.5 between-tools thinking supports low, medium or high effort. Use adaptive thinking for xhigh or max.");
                    return new ClaudeThinkingPlan("between_tools", effort);
                }
                return new ClaudeThinkingPlan("adaptive", ResolveAdaptiveThinkingEffort(validate),
                    IsThinkingEnabled || RequestThinkingMode == ClaudeThinkingMode.Adaptive
                        ? RequestAdaptiveThinkingDisplay.ToString().ToLowerInvariant() : "omitted");
            }
            if (validate && IsAdaptiveThinkingExplicitlyRequested() && !ModelSupportsAdaptiveThinking())
                throw new NotSupportedException($"Claude model '{RequestModel}' does not support adaptive thinking. Use {nameof(WithThinkingParameters)} with a manual token budget instead.");
            if (!IsThinkingEnabled)
            {
                if (IsAlwaysOnAdaptiveThinkingModel())
                    return new ClaudeThinkingPlan("adaptive", IsClaudeOpus55Model() ? "medium" : "low");
                return new ClaudeThinkingPlan(ModelRequiresExplicitThinkingDisabled() ? "disabled" : null);
            }
            if (UsesAdaptiveThinkingForRequest())
                return new ClaudeThinkingPlan("adaptive", ResolveAdaptiveThinkingEffort(validate),
                    RequestAdaptiveThinkingDisplay.ToString().ToLowerInvariant());
            return new ClaudeThinkingPlan("enabled", budget: RequestThinkingBudget);
        }

        internal override void ValidateEffectiveRequestSettings()
            => ResolveClaudeThinkingPlan(preserveConversation: !RequestStatelessMode);

        private ClaudeThinkingPlan ResolveClaudeThinkingControls(ClaudeThinkingPlan plan, bool validate)
        {
            var binding = EffectiveClaudeBinding;
            var updates = RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates;
            if (plan.Type == "between_tools")
            {
                if (validate && binding.HasValue)
                    throw new NotSupportedException("Sonnet 5.5 between-tools thinking does not accept binding controls. Keep history unchanged or use adaptive thinking with an explicit binding policy.");
                return plan;
            }
            if (plan.Type == "disabled" && (binding.HasValue || updates) && validate)
                throw new NotSupportedException("Thinking binding controls require adaptive or enabled thinking.");
            if (plan.Type == null && (binding.HasValue || updates))
            {
                if (validate && ProfileThinking != null)
                    throw new NotSupportedException("A reasoning-disabled request cannot retain thinking binding controls. Use an isolated stateless auxiliary profile or keep thinking enabled.");
                if (ProfileThinking != null) return plan;
                if (validate && !ModelSupportsAdaptiveThinking())
                    throw new NotSupportedException("Enable manual thinking before using binding controls on this Claude model.");
                if (ModelSupportsAdaptiveThinking()) plan = new ClaudeThinkingPlan("adaptive", plan.Effort);
            }
            var display = plan.Adaptive && updates ? "updates" : plan.Display;
            return plan.WithControls(display, binding);
        }

        private void ApplyClaudeThinking(Dictionary<string, object> body, bool tokenCount = false, bool preserveConversation = true)
        {
            ValidateClaudeRequestOptions(ClaudeOptions);
            var plan = ResolveClaudeThinkingPlan(preserveConversation: preserveConversation, tokenCount: tokenCount);
            if (!tokenCount)
            {
                var baseline = _claudeReasoningBaselines.GetValue(ActivateChat, _ => new ClaudeReasoningBaseline());
                if (baseline.PersistentEffort == null || baseline.Model != RequestModel || baseline.Plan == null)
                {
                    baseline.Model = RequestModel;
                    baseline.Endpoint = HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty;
                    baseline.Plan = plan;
                    baseline.DisplaySetting = RequestAdaptiveThinkingDisplay;
                    baseline.Disabled = plan.Disabled;
                }
            }
            WriteClaudeThinking(body, plan, tokenCount);
        }

        private void WriteClaudeThinking(Dictionary<string, object> body, ClaudeThinkingPlan plan, bool tokenCount)
        {
            if (plan.Type != null)
            {
                var thinking = new Dictionary<string, object> { ["type"] = plan.Type };
                if (plan.Display != null) thinking["display"] = plan.Display;
                if (plan.Budget.HasValue) thinking["budget_tokens"] = plan.Budget.Value;
                if (plan.Binding.HasValue)
                    thinking["block_binding"] = new { prefix_mismatch_behavior = plan.Binding == ClaudeThinkingPrefixMismatchBehavior.Error ? "error" : "drop_block" };
                body["thinking"] = thinking;
            }
            if (tokenCount) return;
            if (plan.Effort != null) body["output_config"] = new Dictionary<string, object> { ["effort"] = plan.Effort };
            if (plan.OutputTokens.HasValue) body["max_tokens"] = plan.OutputTokens.Value;
            if (plan.UnitTemperature) body["temperature"] = 1.0f;
            if (plan.OmitTemperature) body.Remove("temperature");
        }
    }
}
