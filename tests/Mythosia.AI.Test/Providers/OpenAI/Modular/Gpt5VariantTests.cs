#if false
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Tests.Modules;

using Microsoft.VisualStudio.TestTools.UnitTesting;

// ── GPT-5 Mini ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5Mini
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); protected override bool SupportsReasoning() => true; }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Mini); }
}

// ── GPT-5 Nano ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5Nano
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); protected override bool SupportsReasoning() => true; }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5Nano); }
}

// ── GPT-5.1 ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5_1
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); protected override bool SupportsReasoning() => true; }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_1); }
}

// ── GPT-5.2 ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5_2
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); protected override bool SupportsReasoning() => true; }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2); }
}

// ── GPT-5.2 Pro ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5_2Pro
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); protected override bool SupportsReasoning() => true; }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Pro); }
}

// ── GPT-5.2 Codex ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5_2Codex
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); protected override bool SupportsReasoning() => true; }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_2Codex); }
}

// ── GPT-5.3 Codex ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5_3Codex
{
    [TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; protected override bool SupportsReasoning() => true; }
    [TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class Reasoning : ReasoningTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); protected override bool SupportsReasoning() => true; protected override void SetupReasoningEffort() => ((ChatGptService)AI).WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.Low); }
    [TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
    [TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex); }
}
#endif
