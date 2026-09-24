using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Images;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private static readonly HashSet<string> KnownOpenAIChatModels = new HashSet<string>(
            typeof(AIModels.OpenAI).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .Where(model => !model.StartsWith("gpt-image-", StringComparison.Ordinal)),
            StringComparer.OrdinalIgnoreCase);

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            var model = RequestModel;
            if (!IsKnownOpenAIChatModel(model))
                return new AIModelCapabilities(provider: Provider, model: model);

            var levels = Enum.GetValues(typeof(ReasoningLevel)).Cast<ReasoningLevel>()
                .Where(IsOpenAIReasoningLevelSupportedByAdapter).ToArray();
            var reasoning = levels.Length > 0 ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
            var nativeRun = IsKnownGpt6Model(model);
            var search = IsHostedSearchSupportedByAdapter(model);
            var gpt6Sampling = SupportsGpt6Sampling();
            var sampling = !IsNewApiModel(model) || gpt6Sampling
                ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
            // The legacy function-call body currently serializes temperature, but does not
            // serialize top_p or penalties. Report the captured request's actual adapter path.
            var extraSampling = sampling == CapabilitySupport.Supported && !ShouldUseFunctions
                ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
            var topP = gpt6Sampling ? CapabilitySupport.Supported : extraSampling;
            var penalties = IsNewApiModel(model) ? CapabilitySupport.Unsupported : extraSampling;
            return new AIModelCapabilities(provider: Provider, model: model,
                streaming: CapabilitySupport.Supported, functionCalling: CapabilitySupport.Supported,
                asyncFunctionCalling: nativeRun ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                steering: nativeRun ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                reasoning: reasoning, reasoningLevels: levels,
                nativeReasoning: reasoning, nativeReasoningLevels: levels,
                thinkingToggle: levels.Contains(ReasoningLevel.None) ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                webSearch: search ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                fileSearch: search ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                reasoningCachePreservation: nativeRun && RequestGpt6ReasoningMode == Gpt6ReasoningMode.Standard
                    ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                imageInput: CapabilitySupport.Supported, structuredOutput: CapabilitySupport.Supported,
                temperature: sampling, topP: topP, frequencyPenalty: penalties, presencePenalty: penalties,
                maxOutputTokens: GetModelMaxOutputTokens());
        }

        private static bool IsKnownOpenAIChatModel(string model)
        {
            if (KnownOpenAIChatModels.Contains(model)) return true;
            // A dated snapshot belongs to an explicitly known alias. Arbitrary suffixes and
            // future model families must not inherit capabilities by a broad prefix match.
            if (!HasOpenAISnapshotDate(model)) return false;
            var alias = model.Substring(0, model.Length - 11);
            return !HasOpenAISnapshotDate(alias) && KnownOpenAIChatModels.Contains(alias);
        }

        private static bool HasOpenAISnapshotDate(string model) =>
            model.Length > 11 && model[model.Length - 11] == '-' &&
            DateTime.TryParseExact(model.Substring(model.Length - 10), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        private bool IsHostedSearchSupportedByAdapter(string model) =>
            IsNewApiModel(model) && !model.Contains("nano", StringComparison.OrdinalIgnoreCase) &&
            !model.StartsWith("o3-mini", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override ImageModelCapabilities GetImageCapabilities(string? model = null)
            => ResolveOpenAIImageCapabilities(ResolveImageModel(model), Provider);

        private static ImageModelCapabilities ResolveOpenAIImageCapabilities(string model, string provider = nameof(AIProvider.OpenAI))
        {
            var image25 = IsImage25Model(model);
            var image2 = model == AIModels.OpenAI.GptImage2 || model == AIModels.OpenAI.GptImage2_260421;
            if (!image25 && !image2)
                return new ImageModelCapabilities(provider: provider, model: model);

            return new ImageModelCapabilities(provider: provider, model: model,
                generation: CapabilitySupport.Supported, editing: CapabilitySupport.Supported, mask: CapabilitySupport.Supported,
                qualities: image25
                    ? new[] { ImageQuality.Auto, ImageQuality.Low, ImageQuality.Medium, ImageQuality.High, ImageQuality.XHigh, ImageQuality.Max }
                    : new[] { ImageQuality.Auto, ImageQuality.Low, ImageQuality.Medium, ImageQuality.High },
                backgrounds: new[] { ImageBackground.Auto, ImageBackground.Opaque, ImageBackground.Transparent },
                outputFormats: new[] { ImageOutputFormat.Auto, ImageOutputFormat.Png, ImageOutputFormat.Jpeg, ImageOutputFormat.WebP },
                sizeKinds: new[] { ImageSizeKind.Auto, ImageSizeKind.Pixels },
                maxImages: 10, maxInputImages: image25 ? 16 : (int?)null);
        }
    }
}
