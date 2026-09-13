using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Google;

[TestClass]
[TestCategory("Live")]
[TestCategory("Google")]
[TestCategory("GeminiFlashLatest")]
[DoNotParallelize]
public class GeminiFlashLatestLiveTests
{
    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.Completion, ReasoningLevel.Low)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.Completion, ReasoningLevel.Low)]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.LegacyCallback, ReasoningLevel.Medium)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.LegacyCallback, ReasoningLevel.Medium)]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.Run, ReasoningLevel.High)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.Run, ReasoningLevel.High)]
    public async Task SupportedThinkingLevels_AnswerThroughEachPublicExecutionPath(
        string model, GeminiFlashExecutionMode mode, ReasoningLevel level)
    {
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        probe.Service.WithReasoning(level);
        var answer = await probe.ExecuteAsync(
            "A warehouse has 17 sealed cartons with 19 pieces each. It ships 8 cartons and then receives 37 loose pieces. " +
            "How many pieces remain? Reply with only the integer.", mode);
        StringAssert.Contains(answer.Text, "208");
        Assert.AreEqual(1, probe.Requests.Count);
        Assert.AreEqual(level.ToString().ToUpperInvariant(), Thinking(probe.Requests[0]));
        Assert.AreEqual(answer.Text, probe.Service.ActivateChat.Messages.Last().Content);
        if (mode == GeminiFlashExecutionMode.Run)
        {
            Assert.IsTrue(probe.Requests[0].Body["generationConfig"]?["thinkingConfig"]?["includeThoughts"]?.GetValue<bool>() == true);
            var providerThoughts = string.Concat(probe.Requests[0].ResponseParts()
                .Where(part => part["thought"]?.GetValue<bool>() == true).Select(part => part["text"]?.GetValue<string>()));
            var publicThoughts = string.Concat(answer.Events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content));
            Assert.AreEqual(providerThoughts, publicThoughts,
                "Any thought summaries the provider emits must reach the requested public reasoning stream.");
        }
        probe.AssertTransport(mode != GeminiFlashExecutionMode.Completion);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=completion-and-thinking mode={mode} level={level}");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.Completion)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.Completion)]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.Run)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.Run)]
    public async Task AutomaticFunctionRoundTrip_PreservesRealProviderIdAndThoughtSignature(
        string model, GeminiFlashExecutionMode mode)
    {
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        var reference = "SHIPMENT_" + Guid.NewGuid().ToString("N");
        var handlerCalls = 0;
        probe.Service.WithReasoning(ReasoningLevel.Medium);
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup_dispatch_reference",
            Description = "Looks up the current dispatch reference for the requested warehouse. The reference is available only from this tool.",
            Parameters = new FunctionParameters
            {
                Properties = new Dictionary<string, ParameterProperty>
                {
                    ["warehouse"] = new() { Type = "string", Description = "The warehouse name." }
                },
                Required = new List<string> { "warehouse" }
            },
            Handler = args =>
            {
                Assert.AreEqual("Seoul", args["warehouse"].ToString());
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(JsonSerializer.Serialize(new { dispatch_reference = reference }));
            }
        });
        var answer = await probe.ExecuteAsync(
            "Look up the current dispatch reference for the Seoul warehouse using lookup_dispatch_reference exactly once. " +
            "Reply with only the exact dispatch reference returned by that tool; it is not available from memory.", mode);
        StringAssert.Contains(answer.Text, reference);
        Assert.AreEqual(1, handlerCalls);
        Assert.AreEqual(2, probe.Requests.Count);
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(reference, StringComparison.Ordinal),
            "The opaque result must first enter the conversation through the tool handler.");
        var originalCall = probe.Requests[0].ResponseParts().Single(part => part["functionCall"] != null);
        var id = originalCall["functionCall"]?["id"]?.GetValue<string>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(id), "Gemini must issue a native call ID.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(originalCall["thoughtSignature"]?.GetValue<string>()),
            "This round trip must actually exercise a real signed function call.");
        var continuation = RequestParts(probe.Requests[1]).ToArray();
        var replayed = continuation.Single(part => part["functionCall"]?["id"]?.GetValue<string>() == id);
        Assert.IsTrue(JsonNode.DeepEquals(originalCall, replayed), "The native call and signature must be replayed unchanged.");
        var result = continuation.Single(part => part["functionResponse"] != null)["functionResponse"]!;
        Assert.AreEqual(id, result["id"]?.GetValue<string>());
        Assert.AreEqual("lookup_dispatch_reference", result["name"]?.GetValue<string>());
        StringAssert.Contains(result.ToJsonString(), reference);
        var publicCall = probe.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null).FunctionCallBatch!.Calls.Single();
        Assert.AreEqual(id, publicCall.Id);
        Assert.AreEqual(originalCall["thoughtSignature"]!.GetValue<string>(), publicCall.Metadata![MessageMetadataKeys.ThoughtSignature]);
        if (mode == GeminiFlashExecutionMode.Run)
        {
            Assert.AreEqual(1, answer.Events.Count(item => item.Type == StreamingContentType.FunctionCall));
            Assert.AreEqual(1, answer.Events.Count(item => item.Type == StreamingContentType.FunctionResult));
        }
        probe.AssertTransport(mode != GeminiFlashExecutionMode.Completion);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=native-function-replay mode={mode}");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, false)]
    [DataRow(AIModels.Google.Gemini3_8Flash, false)]
    [DataRow(AIModels.Google.Gemini3_7Flash, true)]
    [DataRow(AIModels.Google.Gemini3_8Flash, true)]
    public async Task StructuredOutput_ReturnsTypedObjectWithoutRepair(string model, bool streaming)
    {
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        const string prompt = "Return an object with City equal to Seoul and Packages equal to 17 times 19.";
        DispatchSummary result;
        if (!streaming)
            result = await probe.Service.GetCompletionAsync<DispatchSummary>(prompt).WaitAsync(TimeSpan.FromMinutes(4));
        else
        {
            var typed = probe.Service.BeginStream(prompt).As<DispatchSummary>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var observed = new StringBuilder();
            await foreach (var chunk in typed.Stream(timeout.Token)) observed.Append(chunk);
            result = await typed.Result.WaitAsync(timeout.Token);
            var streamedObject = JsonSerializer.Deserialize<DispatchSummary>(observed.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(streamedObject);
            Assert.AreEqual(result.City, streamedObject.City);
            Assert.AreEqual(result.Packages, streamedObject.Packages);
        }
        Assert.AreEqual("Seoul", result.City);
        Assert.AreEqual(323, result.Packages);
        Assert.AreEqual(1, probe.Requests.Count, "Native structured output must succeed without JSON-repair calls.");
        var format = probe.Requests[0].Body["generationConfig"]?["responseFormat"]?["text"];
        Assert.AreEqual("APPLICATION_JSON", format?["mimeType"]?.GetValue<string>());
        Assert.IsNotNull(format?["schema"]);
        probe.AssertTransport(streaming);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=typed-json streaming={streaming}");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash)]
    [DataRow(AIModels.Google.Gemini3_8Flash)]
    public async Task WebSearch_RunReturnsActualProviderCitations(string model)
    {
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        probe.Service.WithWebSearch();
        var answer = await probe.ExecuteAsync(
            $"Use Google Search now to find recent NASA Mars mission news as of {DateTime.UtcNow:yyyy-MM-dd}. " +
            "Summarize one item in one sentence and cite a source. Perform the search rather than answering from memory.",
            GeminiFlashExecutionMode.Run);
        Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Text));
        Assert.IsTrue(answer.Citations.Any(citation => Uri.TryCreate(citation.Url, UriKind.Absolute, out var url) &&
            url.Scheme is "https" or "http"), "An ordinary answer without provider citations does not verify search.");
        Assert.IsTrue(answer.Citations.All(citation => citation.Provider == "Google"));
        Assert.AreEqual(1, probe.Requests.Count);
        Assert.IsTrue(probe.Requests[0].Body["tools"]!.AsArray().OfType<JsonObject>().Any(tool => tool["googleSearch"] != null));
        probe.AssertTransport(streaming: true);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=web-search citations={answer.Citations.Count}");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.Completion)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.Completion)]
    [DataRow(AIModels.Google.Gemini3_7Flash, GeminiFlashExecutionMode.Run)]
    [DataRow(AIModels.Google.Gemini3_8Flash, GeminiFlashExecutionMode.Run)]
    public async Task FileSearch_RetrievesOnlyFixtureDataAndIdentifiesTheSource(string model, GeminiFlashExecutionMode mode)
    {
        var key = await LiveTestSecrets.GetAsync("gemini-secret");
        using var creation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var fixture = await HostedSearchLiveFixture.CreateAsync("Google", key, creation.Token);
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        probe.Service.WithFileSearch(fixture.Store);
        var answer = await probe.ExecuteAsync(fixture.Query, mode);
        StringAssert.Contains(answer.Text, fixture.VerificationToken);
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(fixture.VerificationToken, StringComparison.Ordinal));
        Assert.IsTrue(answer.Citations.Count > 0);
        Assert.IsTrue(answer.Citations.All(citation => citation.Provider == "Google"));
        Assert.IsTrue(answer.Citations.Any(citation =>
            (!string.IsNullOrWhiteSpace(fixture.FileId) &&
                (citation.FileId == fixture.FileId || citation.Url?.Contains(fixture.FileId, StringComparison.Ordinal) == true)) ||
            citation.Title?.Contains(fixture.DocumentTitle, StringComparison.Ordinal) == true),
            "A provider citation must identify this test's synthetic document.");
        Assert.AreEqual(1, probe.Requests.Count);
        var search = probe.Requests[0].Body["tools"]!.AsArray().OfType<JsonObject>().Single(tool => tool["fileSearch"] != null)["fileSearch"]!;
        Assert.AreEqual(fixture.Store.Id, search["fileSearchStoreNames"]!.AsArray().Single()!.GetValue<string>());
        probe.AssertTransport(mode != GeminiFlashExecutionMode.Completion);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=file-search mode={mode} citations={answer.Citations.Count}");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash)]
    [DataRow(AIModels.Google.Gemini3_8Flash)]
    public async Task ImageInput_RecognizesPixelsFromSyntheticInlineImage(string model)
    {
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        var bytes = CreateSolidRedPng();
        var message = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("Name the dominant color of the attached image. Reply with one English color word only."),
            new ImageContent(bytes, "image/png")
        });
        var answer = await probe.ExecuteAsync(message, GeminiFlashExecutionMode.Run);
        StringAssert.Contains(answer.Text.ToLowerInvariant(), "red");
        Assert.AreEqual(1, probe.Requests.Count);
        var image = RequestParts(probe.Requests[0]).Single(part => part["inlineData"] != null)["inlineData"]!;
        Assert.AreEqual("image/png", image["mimeType"]?.GetValue<string>());
        CollectionAssert.AreEqual(bytes, Convert.FromBase64String(image["data"]!.GetValue<string>()));
        probe.AssertTransport(streaming: true);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=image-input");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash)]
    [DataRow(AIModels.Google.Gemini3_8Flash)]
    public async Task RagQueryRewrite_UsesSupportedLowFloorAndRestoresFinalThinking(string model)
    {
        using var probe = await GeminiFlashLiveProbe.CreateAsync(model);
        probe.Service.ThinkingLevel = GeminiThinkingLevel.High;
        var reference = "DELIVERY_" + Guid.NewGuid().ToString("N");
        var rag = probe.Service.WithRag(builder => builder
            .AddText($"The Seoul warehouse delivery reference is {reference}. This is synthetic test data.", id: "synthetic-delivery")
            .UseLocalEmbedding(64).WithQueryRewriter());
        var answer = await rag.GetCompletionAsync("What is the exact delivery reference for the Seoul warehouse? Copy it verbatim.")
            .WaitAsync(TimeSpan.FromMinutes(4));
        StringAssert.Contains(answer, reference);
        Assert.AreEqual(2, probe.Requests.Count, "The request must include a real internal rewrite and one final model answer.");
        Assert.AreEqual("LOW", Thinking(probe.Requests[0]));
        Assert.AreEqual("HIGH", Thinking(probe.Requests[1]));
        Assert.AreEqual(GeminiThinkingLevel.High, probe.Service.ThinkingLevel);
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(reference, StringComparison.Ordinal));
        StringAssert.Contains(probe.Requests[1].Body.ToJsonString(), reference);
        probe.AssertTransport(streaming: false);
        Console.WriteLine($"LIVE_GOOGLE_FLASH_OK model={model} feature=rag-rewrite-low-floor-and-restore");
    }

    public sealed class DispatchSummary
    {
        public string City { get; set; } = string.Empty;
        public int Packages { get; set; }
    }

    private static string? Thinking(GeminiFlashLiveProbe.RequestRecord request) =>
        request.Body["generationConfig"]?["thinkingConfig"]?["thinkingLevel"]?.GetValue<string>();

    private static IEnumerable<JsonObject> RequestParts(GeminiFlashLiveProbe.RequestRecord request) =>
        request.Body["contents"]!.AsArray().OfType<JsonObject>().SelectMany(content => content["parts"]!.AsArray().OfType<JsonObject>());

    // A deterministic image fixture generated entirely in memory; no user files or external URLs.
    private static byte[] CreateSolidRedPng()
    {
        const int side = 64;
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), side);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), side);
        header[8] = 8;
        header[9] = 2;
        WritePngChunk(png, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + side * 3];
            for (var x = 0; x < side; x++) row[1 + x * 3] = 255;
            for (var y = 0; y < side; y++) zlib.Write(row);
        }
        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        stream.Write(word);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        stream.Write(word);
    }
}
