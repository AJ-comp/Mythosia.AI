#if false
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Tests.Modules;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.OpenAI.Gpt4oMini;

// GPT-4o Mini: Core, Streaming, FunctionCalling, Vision, Audio, ImageGen,
//              StructuredOutput, StreamingMetadata, Performance, CrossProvider, ServiceSpecific
// Reasoning: 미지원 (GPT-4o 계열은 reasoning 미지원)

[TestClass] public class Core : CoreTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
    protected override AIModel? GetAlternativeModel() => AIModel.Gpt4o;
}

[TestClass] public class Streaming : StreamingTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class FunctionCalling : FunctionCallingTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class Vision : VisionTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class Audio : AudioTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class ImageGeneration : ImageGenerationTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class StructuredOutput : StructuredOutputTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class StreamingMetadata : StreamingMetadataTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class Performance : PerformanceTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class CrossProvider : CrossProviderTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}

[TestClass] public class ServiceSpecific : ServiceSpecificTestModule
{
    protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt4oMini);
}
#endif
