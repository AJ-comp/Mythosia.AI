#if false
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Tests.Modules;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.OpenAI.O3;

[TestClass] public class Core : CoreTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini; }
[TestClass] public class Streaming : StreamingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class FunctionCalling : FunctionCallingTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class Vision : VisionTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class Audio : AudioTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class ImageGeneration : ImageGenerationTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class StructuredOutput : StructuredOutputTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class StreamingMetadata : StreamingMetadataTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class Performance : PerformanceTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class CrossProvider : CrossProviderTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
[TestClass] public class ServiceSpecific : ServiceSpecificTestModule { protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.o3); }
#endif
