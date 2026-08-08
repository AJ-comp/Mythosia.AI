using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class GoogleCurrentContractTests
{
    private const string OfflineApiKey = "offline-google-contract-key";

    [TestMethod]
    public void ModelConstants_AndDefaultModel_MatchCurrentGoogleCatalogue()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "gemini-2.5-pro",
                "gemini-2.5-flash",
                "gemini-2.5-flash-lite",
                "gemini-3-flash-preview",
                "gemini-3.1-pro-preview",
                "gemini-3.1-flash-lite",
                "gemini-3.5-flash",
                "gemini-3.5-flash-lite",
                "gemini-3.6-flash"
            },
            new[]
            {
                AIModels.Google.Gemini2_5Pro,
                AIModels.Google.Gemini2_5Flash,
                AIModels.Google.Gemini2_5FlashLite,
                AIModels.Google.Gemini3FlashPreview,
                AIModels.Google.Gemini3_1ProPreview,
                AIModels.Google.Gemini3_1FlashLite,
                AIModels.Google.Gemini3_5Flash,
                AIModels.Google.Gemini3_5FlashLite,
                AIModels.Google.Gemini3_6Flash
            });

        CollectionAssert.AreEqual(
            new[]
            {
                "gemini-3.1-flash-image",
                "gemini-3.1-flash-lite-image",
                "gemini-3-pro-image"
            },
            new[]
            {
                AIModels.Google.Images.Gemini3_1FlashImage,
                AIModels.Google.Images.Gemini3_1FlashLiteImage,
                AIModels.Google.Images.Gemini3ProImage
            });

        var service = new GoogleAIService(
            OfflineApiKey,
            new HttpClient(new CaptureHandler()));

        Assert.AreEqual(AIModels.Google.Gemini3_6Flash, service.Model);
        Assert.AreEqual(8192u, service.MaxTokens);
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_6Flash)]
    [DataRow(AIModels.Google.Gemini3_5FlashLite)]
    public async Task LatestModels_OmitLegacySamplingAndCandidateCount(string model)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.Temperature = 0.25f;
        service.TopP = 0.75f;
        service.ThinkingLevel = GeminiThinkingLevel.Medium;

        Assert.AreEqual("ok", await service.GetCompletionAsync("hello"));

        var request = AssertSingleRequest(handler);
        Assert.AreEqual($"/v1beta/models/{model}:generateContent", request.Uri.AbsolutePath);
        var generationConfig = ParseGenerationConfig(request);

        Assert.IsFalse(generationConfig.TryGetProperty("temperature", out _));
        Assert.IsFalse(generationConfig.TryGetProperty("topP", out _));
        Assert.IsFalse(generationConfig.TryGetProperty("topK", out _));
        Assert.IsFalse(generationConfig.TryGetProperty("candidateCount", out _));
        Assert.AreEqual(
            "MEDIUM",
            generationConfig.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini2_5Pro, 128)]
    [DataRow(AIModels.Google.Gemini2_5Flash, 0)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 512)]
    public async Task Gemini25_PreservesLegacySamplingCandidateCountAndThinkingBudget(
        string model,
        int thinkingBudget)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.Temperature = 0.25f;
        service.TopP = 0.75f;
        service.ThinkingBudget = thinkingBudget;

        await service.GetCompletionAsync("legacy-compatible request");

        var generationConfig = ParseGenerationConfig(AssertSingleRequest(handler));
        Assert.AreEqual(0.25, generationConfig.GetProperty("temperature").GetDouble(), 0.0001);
        Assert.AreEqual(0.75, generationConfig.GetProperty("topP").GetDouble(), 0.0001);
        Assert.AreEqual(40, generationConfig.GetProperty("topK").GetInt32());
        Assert.AreEqual(1, generationConfig.GetProperty("candidateCount").GetInt32());
        Assert.AreEqual(
            thinkingBudget,
            generationConfig.GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        Assert.IsFalse(
            generationConfig.GetProperty("thinkingConfig").TryGetProperty("thinkingLevel", out _));
    }

    [TestMethod]
    [DataRow(nameof(GeminiThinkingLevel.Minimal), "MINIMAL")]
    [DataRow(nameof(GeminiThinkingLevel.Low), "LOW")]
    [DataRow(nameof(GeminiThinkingLevel.Medium), "MEDIUM")]
    [DataRow(nameof(GeminiThinkingLevel.High), "HIGH")]
    public async Task Gemini3_SerializesEverySupportedThinkingLevel(
        string levelName,
        string expected)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.ThinkingLevel = Enum.Parse<GeminiThinkingLevel>(levelName);

        await service.GetCompletionAsync("think");

        var thinkingConfig = ParseGenerationConfig(AssertSingleRequest(handler))
            .GetProperty("thinkingConfig");
        Assert.AreEqual(expected, thinkingConfig.GetProperty("thinkingLevel").GetString());
        Assert.IsFalse(thinkingConfig.TryGetProperty("thinkingBudget", out _));
    }

    [TestMethod]
    public async Task Gemini3_AutoThinking_UsesProviderDefaultWithoutSendingThinkingConfig()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.ThinkingLevel = GeminiThinkingLevel.Auto;

        await service.GetCompletionAsync("use the model default");

        Assert.IsFalse(
            ParseGenerationConfig(AssertSingleRequest(handler))
                .TryGetProperty("thinkingConfig", out _));
    }

    [TestMethod]
    public async Task Gemini3Pro_RejectsMinimalThinkingBeforeSendingRequest()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_1ProPreview);
        service.ThinkingLevel = GeminiThinkingLevel.Minimal;

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.GetCompletionAsync("unsupported thinking level"));

        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini2_5Pro, -1)]
    [DataRow(AIModels.Google.Gemini2_5Pro, 128)]
    [DataRow(AIModels.Google.Gemini2_5Pro, 32768)]
    [DataRow(AIModels.Google.Gemini2_5Flash, -1)]
    [DataRow(AIModels.Google.Gemini2_5Flash, 0)]
    [DataRow(AIModels.Google.Gemini2_5Flash, 24576)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, -1)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 0)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 512)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 24576)]
    public async Task Gemini25_AcceptsDocumentedThinkingBudgetBounds(string model, int budget)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.ThinkingBudget = budget;

        await service.GetCompletionAsync("valid budget");

        Assert.AreEqual(
            budget,
            ParseGenerationConfig(AssertSingleRequest(handler))
                .GetProperty("thinkingConfig")
                .GetProperty("thinkingBudget")
                .GetInt32());
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini2_5Pro, 0)]
    [DataRow(AIModels.Google.Gemini2_5Pro, 127)]
    [DataRow(AIModels.Google.Gemini2_5Pro, 32769)]
    [DataRow(AIModels.Google.Gemini2_5Flash, -2)]
    [DataRow(AIModels.Google.Gemini2_5Flash, 24577)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 1)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 511)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite, 24577)]
    public async Task Gemini25_RejectsOutOfRangeThinkingBudgetsBeforeSending(
        string model,
        int budget)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.ThinkingBudget = budget;

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.GetCompletionAsync("invalid budget"));

        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task StructuredOutput_UsesNativeResponseFormatTextSchema()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.SetStructuredOutputSchema("""
            {
              "type": "object",
              "properties": {
                "answer": { "type": "string" },
                "score": { "type": "integer", "minimum": 0 }
              },
              "required": ["answer"],
              "additionalProperties": false
            }
            """);

        await service.GetCompletionAsync("return JSON");

        var generationConfig = ParseGenerationConfig(AssertSingleRequest(handler));
        var responseFormat = generationConfig.GetProperty("responseFormat");
        var textFormat = responseFormat.GetProperty("text");

        Assert.AreEqual("APPLICATION_JSON", textFormat.GetProperty("mimeType").GetString());
        var schema = textFormat.GetProperty("schema");
        Assert.AreEqual("object", schema.GetProperty("type").GetString());
        Assert.AreEqual("string", schema.GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
        Assert.AreEqual(0, schema.GetProperty("properties").GetProperty("score").GetProperty("minimum").GetInt32());
        Assert.IsFalse(generationConfig.TryGetProperty("responseSchema", out _));
        Assert.IsFalse(generationConfig.TryGetProperty("responseJsonSchema", out _));
        Assert.IsFalse(generationConfig.TryGetProperty("responseMimeType", out _));
    }

    [TestMethod]
    public async Task Gemini3_InternalSummarizationProfile_ReservesMandatoryThinkingTokens()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_1ProPreview);

        await service.GetCompletionAsync("summarize", RequestProfiles.Summarization);

        var generationConfig = ParseGenerationConfig(AssertSingleRequest(handler));
        Assert.AreEqual(1024, generationConfig.GetProperty("maxOutputTokens").GetInt32());
        Assert.AreEqual(
            "LOW",
            generationConfig.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.AreEqual(8192u, service.MaxTokens, "The one-shot request profile must be restored.");
        Assert.AreEqual(GeminiThinkingLevel.Auto, service.ThinkingLevel);
    }

    [TestMethod]
    public async Task Gemini25Pro_InternalQueryRewriteProfile_ReservesMinimumThinkingBudget()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini2_5Pro);

        await service.GetCompletionAsync("rewrite", RequestProfiles.QueryRewrite);

        var generationConfig = ParseGenerationConfig(AssertSingleRequest(handler));
        Assert.AreEqual(256, generationConfig.GetProperty("maxOutputTokens").GetInt32());
        Assert.AreEqual(
            128,
            generationConfig.GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        Assert.AreEqual(8192u, service.MaxTokens, "The one-shot request profile must be restored.");
        Assert.AreEqual(-1, service.ThinkingBudget);
    }

    [TestMethod]
    public async Task FunctionRequest_PreservesNativeSchemaAndSafetySettings()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.SetStructuredOutputSchema("""
            {
              "type": "object",
              "properties": { "answer": { "type": "string" } },
              "required": ["answer"]
            }
            """);
        service.HateSpeechSafetyThreshold = GeminiSafetyThreshold.BlockOnlyHigh;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Description = "Looks up a value.",
            Handler = _ => Task.FromResult("unused")
        });

        await service.GetCompletionAsync("return structured data or use the tool");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var root = document.RootElement;
        var textFormat = root
            .GetProperty("generationConfig")
            .GetProperty("responseFormat")
            .GetProperty("text");
        Assert.AreEqual("APPLICATION_JSON", textFormat.GetProperty("mimeType").GetString());
        Assert.AreEqual("object", textFormat.GetProperty("schema").GetProperty("type").GetString());

        var safety = root.GetProperty("safetySettings");
        Assert.AreEqual(1, safety.GetArrayLength());
        Assert.AreEqual("HARM_CATEGORY_HATE_SPEECH", safety[0].GetProperty("category").GetString());
        Assert.AreEqual("BLOCK_ONLY_HIGH", safety[0].GetProperty("threshold").GetString());
    }

    [TestMethod]
    public async Task Authentication_UsesHeaderAndNeverExposesApiKeyInUrl()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);

        await service.GetCompletionAsync("authenticate offline");

        var request = AssertSingleRequest(handler);
        AssertHeaderAuthentication(request);
    }

    [TestMethod]
    public async Task Authentication_UsesHeaderForFunctionStreamingAndTokenCountRequests()
    {
        var functionHandler = new CaptureHandler();
        var functionService = CreateService(functionHandler, AIModels.Google.Gemini3_6Flash);
        functionService.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Description = "Looks up a value.",
            Handler = _ => Task.FromResult("unused")
        });
        await functionService.GetCompletionAsync("function-capable request");
        AssertHeaderAuthentication(AssertSingleRequest(functionHandler));

        var streamHandler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}]}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        var streamService = CreateService(streamHandler, AIModels.Google.Gemini3_6Flash);
        await foreach (var _ in streamService.StreamAsync("streaming request"))
        {
        }
        var streamRequest = AssertSingleRequest(streamHandler);
        AssertHeaderAuthentication(streamRequest);
        Assert.AreEqual("?alt=sse", streamRequest.Uri.Query);

        var tokenHandler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"totalTokens\":7}", Encoding.UTF8, "application/json")
        });
        var tokenService = CreateService(tokenHandler, AIModels.Google.Gemini3_6Flash);
        Assert.AreEqual(7u, await tokenService.GetInputTokenCountAsync("count me"));
        var tokenRequest = AssertSingleRequest(tokenHandler);
        AssertHeaderAuthentication(tokenRequest);
        StringAssert.EndsWith(tokenRequest.Uri.AbsolutePath, ":countTokens");
    }

    [TestMethod]
    public async Task SafetySettings_AreOmittedWhenProviderDefaultsAreSelected()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);

        await service.GetCompletionAsync("default safety");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        Assert.IsFalse(document.RootElement.TryGetProperty("safetySettings", out _));
    }

    [TestMethod]
    [DataRow(nameof(GeminiSafetyThreshold.Off), "OFF")]
    [DataRow(nameof(GeminiSafetyThreshold.BlockNone), "BLOCK_NONE")]
    [DataRow(nameof(GeminiSafetyThreshold.BlockOnlyHigh), "BLOCK_ONLY_HIGH")]
    [DataRow(nameof(GeminiSafetyThreshold.BlockMediumAndAbove), "BLOCK_MEDIUM_AND_ABOVE")]
    [DataRow(nameof(GeminiSafetyThreshold.BlockLowAndAbove), "BLOCK_LOW_AND_ABOVE")]
    public async Task SafetySettings_SerializeSelectedThreshold(
        string thresholdName,
        string expected)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.HarassmentSafetyThreshold = Enum.Parse<GeminiSafetyThreshold>(thresholdName);

        await service.GetCompletionAsync("custom safety");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var settings = document.RootElement.GetProperty("safetySettings");
        Assert.AreEqual(1, settings.GetArrayLength());
        Assert.AreEqual("HARM_CATEGORY_HARASSMENT", settings[0].GetProperty("category").GetString());
        Assert.AreEqual(expected, settings[0].GetProperty("threshold").GetString());
    }

    [TestMethod]
    public async Task ForceFunctionName_SerializesAnyModeWithSingleAllowedFunction()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets the current weather.",
            Handler = _ => Task.FromResult("sunny")
        });
        service.ForceFunctionName = "get_weather";

        await service.GetCompletionAsync("Call the weather function.");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var root = document.RootElement;
        var declaration = root
            .GetProperty("tools")[0]
            .GetProperty("functionDeclarations")[0];
        Assert.AreEqual("get_weather", declaration.GetProperty("name").GetString());

        var functionConfig = root
            .GetProperty("toolConfig")
            .GetProperty("functionCallingConfig");
        Assert.AreEqual("ANY", functionConfig.GetProperty("mode").GetString());

        var allowedNames = functionConfig.GetProperty("allowedFunctionNames");
        Assert.AreEqual(1, allowedNames.GetArrayLength());
        Assert.AreEqual("get_weather", allowedNames[0].GetString());
    }

    [TestMethod]
    public async Task ForceFunctionName_AppliesOnlyToInitialToolRound()
    {
        const string functionResponse = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [{
                    "functionCall": {
                      "name": "get_weather",
                      "args": { "city": "Seoul" }
                    }
                  }]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        const string finalResponse = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [{ "text": "It is sunny." }]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        var responses = new Queue<string>(new[] { functionResponse, finalResponse });
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler, AIModels.Google.Gemini2_5Pro);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets the current weather.",
            Handler = _ => Task.FromResult("sunny")
        });
        service.ForceFunctionName = "get_weather";

        Assert.AreEqual("It is sunny.", await service.GetCompletionAsync("Call get_weather once, then answer."));
        Assert.AreEqual(2, handler.Requests.Count);

        using var initialDocument = JsonDocument.Parse(handler.Requests[0].Body);
        var initialConfig = initialDocument.RootElement
            .GetProperty("toolConfig")
            .GetProperty("functionCallingConfig");
        Assert.AreEqual("ANY", initialConfig.GetProperty("mode").GetString());
        Assert.AreEqual(
            "get_weather",
            initialConfig.GetProperty("allowedFunctionNames")[0].GetString());

        using var continuationDocument = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.IsFalse(
            continuationDocument.RootElement.TryGetProperty("toolConfig", out _),
            "Gemini 2.5 continuation must return to provider AUTO mode after the forced first call.");
        Assert.AreEqual(ActorRole.Assistant, service.ActivateChat.Messages.Last().Role);
    }

    [TestMethod]
    public async Task ForcedInitialFunction_ContinuationRetainsEveryFunctionDeclaration()
    {
        const string functionResponse = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [{
                    "functionCall": {
                      "name": "get_user_id",
                      "args": { "username": "john_doe" }
                    }
                  }]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        const string finalResponse = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [{ "text": "The user is John Doe." }]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        var responses = new Queue<string>(new[] { functionResponse, finalResponse });
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler, AIModels.Google.Gemini2_5Flash);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_user_id",
            Description = "Gets a user ID from a username.",
            Handler = _ => Task.FromResult("user_123")
        });
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_user_details",
            Description = "Gets user details from a user ID.",
            Handler = _ => Task.FromResult("John Doe")
        });
        service.ForceFunctionName = "get_user_id";

        Assert.AreEqual(
            "The user is John Doe.",
            await service.GetCompletionAsync("Resolve john_doe, then get that user's details."));
        Assert.AreEqual(2, handler.Requests.Count);

        using var continuationDocument = JsonDocument.Parse(handler.Requests[1].Body);
        var declarations = continuationDocument.RootElement
            .GetProperty("tools")[0]
            .GetProperty("functionDeclarations")
            .EnumerateArray()
            .Select(declaration => declaration.GetProperty("name").GetString())
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "get_user_id", "get_user_details" },
            declarations);
        Assert.IsFalse(
            continuationDocument.RootElement.TryGetProperty("toolConfig", out _),
            "Gemini 2.5 continuation should retain every declaration in provider AUTO mode.");
    }

    [TestMethod]
    public async Task Gemini3_AutoFunctionCalling_UsesValidatedSchemaMode()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini3_1ProPreview);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "book_flight",
            Description = "Books a flight.",
            Handler = _ => Task.FromResult("unused")
        });

        await service.GetCompletionAsync("Book a flight if appropriate.");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var functionConfig = document.RootElement
            .GetProperty("toolConfig")
            .GetProperty("functionCallingConfig");
        Assert.AreEqual("VALIDATED", functionConfig.GetProperty("mode").GetString());
        Assert.IsFalse(functionConfig.TryGetProperty("allowedFunctionNames", out _));
    }

    [TestMethod]
    public async Task Gemini25_AutoFunctionCalling_UsesProviderDefaultMode()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.Google.Gemini2_5Flash);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "book_flight",
            Description = "Books a flight.",
            Handler = _ => Task.FromResult("unused")
        });

        await service.GetCompletionAsync("Book a flight if appropriate.");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        Assert.IsFalse(document.RootElement.TryGetProperty("toolConfig", out _));
    }

    [TestMethod]
    public async Task FunctionContinuation_PreservesProviderIdSignatureAndUserResponseRole()
    {
        const string functionResponse = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "text": "checking", "thought": true },
                    {
                      "functionCall": {
                        "id": "google-call-1",
                        "name": "get_weather",
                        "args": { "city": "Seoul" }
                      },
                      "thoughtSignature": "signature-1"
                    }
                  ]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        const string finalResponse = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "text": "final reasoning", "thought": true },
                    { "text": "It is sunny." }
                  ]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        var responses = new Queue<string>(new[] { functionResponse, finalResponse });
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler, AIModels.Google.Gemini3_6Flash);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets weather.",
            Handler = _ => Task.FromResult("sunny")
        });

        Assert.AreEqual("It is sunny.", await service.GetCompletionAsync("Weather?"));
        Assert.AreEqual("final reasoning", service.LastThinkingContent);
        Assert.AreEqual(2, handler.Requests.Count);

        using var document = JsonDocument.Parse(handler.Requests[1].Body);
        var contents = document.RootElement.GetProperty("contents").EnumerateArray().ToArray();
        var callContent = contents.Single(content =>
            content.GetProperty("parts").EnumerateArray().Any(part =>
                part.TryGetProperty("functionCall", out _)));
        Assert.AreEqual("model", callContent.GetProperty("role").GetString());
        var callPart = callContent.GetProperty("parts").EnumerateArray().Single(part =>
            part.TryGetProperty("functionCall", out _));
        Assert.AreEqual("signature-1", callPart.GetProperty("thoughtSignature").GetString());
        Assert.AreEqual(
            "google-call-1",
            callPart.GetProperty("functionCall").GetProperty("id").GetString());

        var resultContent = contents.Single(content =>
            content.GetProperty("parts")[0].TryGetProperty("functionResponse", out _));
        Assert.AreEqual("user", resultContent.GetProperty("role").GetString());
        var result = resultContent.GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.AreEqual("google-call-1", result.GetProperty("id").GetString());
        Assert.AreEqual("get_weather", result.GetProperty("name").GetString());
        Assert.AreEqual("sunny", result.GetProperty("response").GetProperty("content").GetString());
    }

    [TestMethod]
    public void ChatUiHelpers_ExposeCurrentGoogleModelsAndCapabilities()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var models = catalogue
            .EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "Google")
            .GetProperty("models")
            .EnumerateArray()
            .ToDictionary(
                model => model.GetProperty("name").GetString()!,
                model => model);

        Assert.AreEqual(
            AIModels.Google.Gemini3_6Flash,
            models[nameof(AIModels.Google.Gemini3_6Flash)].GetProperty("description").GetString());
        Assert.AreEqual(
            AIModels.Google.Gemini3_5FlashLite,
            models[nameof(AIModels.Google.Gemini3_5FlashLite)].GetProperty("description").GetString());
        Assert.AreEqual(
            65536u,
            models[nameof(AIModels.Google.Gemini3_6Flash)].GetProperty("maxOutputTokens").GetUInt32());

        var latestSampling = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetSamplingControls(AIModels.Google.Gemini3_6Flash));
        Assert.IsFalse(latestSampling.GetProperty("temperature").GetBoolean());
        Assert.IsFalse(latestSampling.GetProperty("topP").GetBoolean());

        var gemini25Sampling = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetSamplingControls(AIModels.Google.Gemini2_5Flash));
        Assert.IsTrue(gemini25Sampling.GetProperty("temperature").GetBoolean());
        Assert.IsTrue(gemini25Sampling.GetProperty("topP").GetBoolean());

        var proReasoning = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetReasoningLevels(AIModels.Google.Gemini3_1ProPreview));
        CollectionAssert.AreEqual(
            new[] { "Auto", "Low", "Medium", "High" },
            proReasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    public void ChatUiReasoningSettings_MapGemini3AndGemini25Contracts()
    {
        var gemini3 = new GoogleAIService(OfflineApiKey, new HttpClient(new CaptureHandler()));
        gemini3.ChangeModel(AIModels.Google.Gemini3_6Flash);
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            gemini3,
            CreateSettingsRequest(true, "High", "gemini3"));

        Assert.AreEqual(GeminiThinkingLevel.High, gemini3.ThinkingLevel);
        Assert.AreEqual(-1, gemini3.ThinkingBudget);

        var gemini25 = new GoogleAIService(OfflineApiKey, new HttpClient(new CaptureHandler()));
        gemini25.ChangeModel(AIModels.Google.Gemini2_5FlashLite);
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            gemini25,
            CreateSettingsRequest(true, "512", "gemini25"));

        Assert.AreEqual(GeminiThinkingLevel.Auto, gemini25.ThinkingLevel);
        Assert.AreEqual(512, gemini25.ThinkingBudget);

        ChatUiSettingsHelpers.ApplyReasoningSettings(
            gemini25,
            CreateSettingsRequest(false, null, "gemini25"));

        Assert.AreEqual(GeminiThinkingLevel.Auto, gemini25.ThinkingLevel);
        Assert.AreEqual(0, gemini25.ThinkingBudget);
    }

    private static RequestProbeService CreateService(CaptureHandler handler, string model)
    {
        var service = new RequestProbeService(OfflineApiKey, new HttpClient(handler));
        service.ChangeModel(model);
        return service;
    }

    private static CapturedRequest AssertSingleRequest(CaptureHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count);
        return handler.Requests[0];
    }

    private static void AssertHeaderAuthentication(CapturedRequest request)
    {
        Assert.AreEqual(OfflineApiKey, request.ApiKeyHeader);
        Assert.IsNull(request.AuthorizationHeader);
        Assert.IsFalse(
            request.Uri.AbsoluteUri.Contains(OfflineApiKey, StringComparison.Ordinal),
            "The Gemini API key must never appear in the request URL.");
        Assert.IsFalse(
            request.Uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase),
            "The Gemini API key must be sent through x-goog-api-key, not a query parameter.");
    }

    private static JsonElement ParseGenerationConfig(CapturedRequest request)
    {
        using var document = JsonDocument.Parse(request.Body);
        return document.RootElement.GetProperty("generationConfig").Clone();
    }

    private static SettingsRequest CreateSettingsRequest(
        bool reasoningEnabled,
        string? reasoningLevel,
        string reasoningType)
        => new(
            Temperature: null,
            TopP: null,
            MaxTokens: null,
            FrequencyPenalty: null,
            PresencePenalty: null,
            StatelessMode: null,
            SystemMessage: null,
            ReasoningEnabled: reasoningEnabled,
            ReasoningLevel: reasoningLevel,
            ReasoningType: reasoningType);

    private sealed class RequestProbeService : GoogleAIService
    {
        public RequestProbeService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient)
        {
        }

        public void SetStructuredOutputSchema(string schema)
        {
            _structuredOutputSchemaJson = schema;
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private const string SuccessfulResponse =
            "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"ok\"}]}," +
            "\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":1," +
            "\"candidatesTokenCount\":1,\"totalTokenCount\":2}}";

        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

        public CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null)
        {
            _responseFactory = responseFactory;
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("x-goog-api-key", out var apiKeyValues);
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                await request.Content!.ReadAsStringAsync(cancellationToken),
                apiKeyValues == null ? null : string.Join(",", apiKeyValues),
                request.Headers.Authorization?.ToString()));

            return _responseFactory?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessfulResponse, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(
        Uri Uri,
        string Body,
        string? ApiKeyHeader,
        string? AuthorizationHeader);
}
