using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Net.Http;
using System.Text;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// StructuredStreamRun&lt;T&gt; 유닛 테스트.
/// BeginStream(prompt).WithStructuredOutput(policy).As&lt;T&gt;()로 생성되는
/// 스트리밍 + 구조화 출력 파이프라인을 검증합니다.
/// </summary>
[TestClass]
public class StructuredStreamRunTests
{
    #region Test Models

    public class SimpleDto
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    #endregion

    #region Streaming Mock

    /// <summary>
    /// 스트리밍을 지원하는 MockAIService.
    /// StreamCompletionAsync에서 미리 정의된 응답을 청크 단위로 전달하고,
    /// GetCompletionAsync(Message)에서 repair용 응답을 순서대로 반환합니다.
    /// </summary>
    private class StreamingMockAIService : AIService
    {
        private readonly string _streamResponse;
        private readonly int _chunkSize;
        private readonly Queue<string> _repairResponses = new();
        public int StreamCallCount { get; private set; }
        public int RepairCallCount { get; private set; }
        public List<string> ReceivedRepairPrompts { get; } = new();

        public StreamingMockAIService(
            string streamResponse,
            int chunkSize = 5,
            params string[] repairResponses)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
            _streamResponse = streamResponse;
            _chunkSize = chunkSize;
            foreach (var r in repairResponses)
                _repairResponses.Enqueue(r);
        }

        public override AIProvider Provider => AIProvider.OpenAI;

        public override Task<string> GetCompletionAsync(Message message)
        {
            RepairCallCount++;
            ReceivedRepairPrompts.Add(message.Content);
            if (_repairResponses.Count == 0)
                throw new AIServiceException("No more mock repair responses");
            return Task.FromResult(_repairResponses.Dequeue());
        }

        public override async Task StreamCompletionAsync(
            Message message,
            Func<string, Task> messageReceivedAsync)
        {
            StreamCallCount++;
            for (int i = 0; i < _streamResponse.Length; i += _chunkSize)
            {
                var len = Math.Min(_chunkSize, _streamResponse.Length - i);
                var chunk = _streamResponse.Substring(i, len);
                await messageReceivedAsync(chunk);
            }
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    #endregion

    #region Stream + Result Basic Tests

    /// <summary>
    /// 유효한 JSON을 스트리밍한 뒤 Result가 정상 파싱되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamThenResult_ValidJson_DeserializesSuccessfully()
    {
        var json = "{\"Name\": \"Alice\", \"Value\": 42}";
        var mock = new StreamingMockAIService(json, chunkSize: 5);

        var run = mock.BeginStream("test prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        var buffer = new StringBuilder();
        await foreach (var chunk in run.Stream())
        {
            buffer.Append(chunk);
        }

        var dto = await run.Result;

        Assert.AreEqual(json, buffer.ToString(), "Buffer should contain the full streamed text");
        Assert.IsNotNull(dto);
        Assert.AreEqual("Alice", dto.Name);
        Assert.AreEqual(42, dto.Value);
        Assert.AreEqual(1, mock.StreamCallCount, "Should stream exactly once");
        Assert.AreEqual(0, mock.RepairCallCount, "No repair needed for valid JSON");
    }

    /// <summary>
    /// Stream()을 호출하지 않고 Result만 await해도 정상 동작하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task ResultOnly_WithoutStream_WorksViaNonStreamFallback()
    {
        var json = "{\"Name\": \"Bob\", \"Value\": 99}";
        var mock = new StreamingMockAIService(json, chunkSize: 10);

        var run = mock.BeginStream("test prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        // Do NOT call Stream() — just await Result directly
        var dto = await run.Result;

        Assert.IsNotNull(dto);
        Assert.AreEqual("Bob", dto.Name);
        Assert.AreEqual(99, dto.Value);
    }

    /// <summary>
    /// Stream()을 두 번 호출하면 InvalidOperationException이 발생하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamCalledTwice_ThrowsInvalidOperationException()
    {
        var json = "{\"Name\": \"X\", \"Value\": 1}";
        var mock = new StreamingMockAIService(json);

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        // First call — consume the stream
        await foreach (var _ in run.Stream()) { }

        // Second call — should throw
        InvalidOperationException? thrown = null;
        try
        {
            await foreach (var _ in run.Stream()) { }
            Assert.Fail("Expected InvalidOperationException on second Stream() call");
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        Assert.IsNotNull(thrown);
    }

    #endregion

    #region Stream Chunks Verification

    /// <summary>
    /// 스트리밍 청크들이 올바른 순서로 전달되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task Stream_DeliversChunksInOrder()
    {
        var json = "{\"Name\":\"Test\",\"Value\":7}";
        var mock = new StreamingMockAIService(json, chunkSize: 3);

        var run = mock.BeginStream("prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        var chunks = new List<string>();
        await foreach (var chunk in run.Stream())
        {
            chunks.Add(chunk);
        }

        // Verify chunks reassemble into the original JSON
        Assert.AreEqual(json, string.Join("", chunks));
        // Verify we got multiple chunks (chunkSize=3, json.Length > 3)
        Assert.IsTrue(chunks.Count > 1, $"Expected multiple chunks, got {chunks.Count}");
    }

    #endregion

    #region Repair Retry Tests

    /// <summary>
    /// 스트리밍된 JSON이 깨졌을 때 non-stream repair 호출로 복구하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamInvalidJson_RepairSucceeds_ReturnsRepairedResult()
    {
        var invalidJson = "This is not valid JSON at all";
        var validRepair = "{\"Name\": \"Repaired\", \"Value\": 77}";
        var mock = new StreamingMockAIService(invalidJson, chunkSize: 10, validRepair);
        mock.StructuredOutputMaxRetries = 2;

        var run = mock.BeginStream("test prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        // Consume the stream (invalid JSON)
        await foreach (var _ in run.Stream()) { }

        // Result should succeed via repair
        var dto = await run.Result;

        Assert.IsNotNull(dto);
        Assert.AreEqual("Repaired", dto.Name);
        Assert.AreEqual(77, dto.Value);
        Assert.AreEqual(1, mock.StreamCallCount, "Should stream once");
        Assert.AreEqual(1, mock.RepairCallCount, "Should repair once");
    }

    /// <summary>
    /// 스트리밍된 JSON과 모든 repair가 실패하면 StructuredOutputException이 발생하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamInvalidJson_AllRepairsFail_ThrowsStructuredOutputException()
    {
        var invalidJson = "broken stream output";
        var mock = new StreamingMockAIService(
            invalidJson, chunkSize: 10,
            "still broken 1",
            "still broken 2"
        );
        mock.StructuredOutputMaxRetries = 2;

        var run = mock.BeginStream("test prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        await foreach (var _ in run.Stream()) { }

        StructuredOutputException? ex = null;
        try
        {
            await run.Result;
            Assert.Fail("Expected StructuredOutputException");
        }
        catch (StructuredOutputException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual("SimpleDto", ex.TargetTypeName);
        Assert.AreEqual(invalidJson, ex.FirstRawResponse);
        Assert.AreEqual("still broken 2", ex.LastRawResponse);
        Assert.AreEqual(3, ex.AttemptCount, "1 initial + 2 retries = 3 attempts");
        Assert.IsFalse(string.IsNullOrEmpty(ex.ParseError));
        Assert.IsFalse(string.IsNullOrEmpty(ex.SchemaJson));
    }

    /// <summary>
    /// repair 시 correction prompt에 이전 응답과 에러 정보가 포함되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamRepair_CorrectionPromptContainsPreviousOutput()
    {
        var invalidJson = "BROKEN_STREAM_XYZ";
        var validRepair = "{\"Name\": \"Fixed\", \"Value\": 1}";
        var mock = new StreamingMockAIService(invalidJson, chunkSize: 50, validRepair);

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        await foreach (var _ in run.Stream()) { }
        await run.Result;

        Assert.AreEqual(1, mock.ReceivedRepairPrompts.Count);
        var correctionPrompt = mock.ReceivedRepairPrompts[0];
        Assert.IsTrue(correctionPrompt.Contains("BROKEN_STREAM_XYZ"),
            "Correction prompt should contain the streamed output");
        Assert.IsTrue(correctionPrompt.Contains("STRUCTURED OUTPUT CORRECTION"),
            "Correction prompt should be marked as correction");
    }

    #endregion

    #region Per-Call Policy Tests

    /// <summary>
    /// WithStructuredOutput 정책이 repair 횟수를 올바르게 제한하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task WithStructuredOutput_PolicyOverridesServiceDefault()
    {
        var invalidJson = "broken json";
        var mock = new StreamingMockAIService(invalidJson, chunkSize: 50);
        mock.StructuredOutputMaxRetries = 5; // 서비스 기본값은 높지만

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.NoRetry) // 정책: retry 없음
            .As<SimpleDto>();

        await foreach (var _ in run.Stream()) { }

        StructuredOutputException? ex = null;
        try
        {
            await run.Result;
            Assert.Fail("Expected StructuredOutputException");
        }
        catch (StructuredOutputException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual(1, ex.AttemptCount, "NoRetry → 1 attempt only");
        Assert.AreEqual(0, mock.RepairCallCount, "No repair calls with NoRetry");
    }

    /// <summary>
    /// Strict 정책이 최대 3회 repair를 허용하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task WithStructuredOutput_StrictPolicy_AllowsThreeRepairs()
    {
        var invalidJson = "broken";
        var mock = new StreamingMockAIService(
            invalidJson, chunkSize: 50,
            "still broken 1",
            "still broken 2",
            "{\"Name\": \"OK\", \"Value\": 3}"
        );

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.Strict) // MaxRepairAttempts = 3
            .As<SimpleDto>();

        await foreach (var _ in run.Stream()) { }
        var dto = await run.Result;

        Assert.IsNotNull(dto);
        Assert.AreEqual("OK", dto.Name);
        Assert.AreEqual(3, mock.RepairCallCount, "Strict allows up to 3 repair attempts");
    }

    /// <summary>
    /// WithStructuredOutput 없이 BeginStream().As&lt;T&gt;()만 호출하면 서비스 기본값이 사용되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task NoPolicy_FallsBackToServiceDefault()
    {
        var invalidJson = "broken";
        var mock = new StreamingMockAIService(
            invalidJson, chunkSize: 50,
            "{\"Name\": \"OK\", \"Value\": 1}"
        );
        mock.StructuredOutputMaxRetries = 1;

        // No WithStructuredOutput call
        var run = mock.BeginStream("test").As<SimpleDto>();

        await foreach (var _ in run.Stream()) { }
        var dto = await run.Result;

        Assert.IsNotNull(dto);
        Assert.AreEqual("OK", dto.Name);
        Assert.AreEqual(1, mock.RepairCallCount, "Service default MaxRetries=1 → 1 repair attempt");
    }

    #endregion

    #region Schema Cleanup Tests

    /// <summary>
    /// 스트리밍 완료 후 _structuredOutputSchemaJson이 null로 초기화되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task SchemaIsCleared_AfterStreamCompletes()
    {
        var json = "{\"Name\": \"A\", \"Value\": 1}";
        var mock = new StreamingMockAIService(json, chunkSize: 50);

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        await run.Result;

        Assert.IsNull(mock._structuredOutputSchemaJson,
            "Schema must be null after stream completes");
    }

    /// <summary>
    /// 스트리밍 + repair 후에도 _structuredOutputSchemaJson이 null인지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task SchemaIsCleared_AfterRepairCompletes()
    {
        var mock = new StreamingMockAIService(
            "broken", chunkSize: 50,
            "{\"Name\": \"OK\", \"Value\": 1}"
        );

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        await run.Result;

        Assert.IsNull(mock._structuredOutputSchemaJson,
            "Schema must be null after repair completes");
    }

    /// <summary>
    /// 모든 시도 실패 후에도 schema가 정리되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task SchemaIsCleared_AfterAllAttemptsFail()
    {
        var mock = new StreamingMockAIService("broken", chunkSize: 50);
        mock.StructuredOutputMaxRetries = 0;

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.NoRetry)
            .As<SimpleDto>();

        try { await run.Result; } catch (StructuredOutputException) { }

        Assert.IsNull(mock._structuredOutputSchemaJson,
            "Schema must be null even after failure");
    }

    #endregion

    #region Markdown JSON Extraction

    /// <summary>
    /// 마크다운 코드블록으로 감싼 JSON이 스트리밍되어도 정상 파싱되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamMarkdownWrappedJson_ParsesSuccessfully()
    {
        var markdown = "```json\n{\"Name\": \"MD\", \"Value\": 10}\n```";
        var mock = new StreamingMockAIService(markdown, chunkSize: 8);

        var run = mock.BeginStream("test")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<SimpleDto>();

        await foreach (var _ in run.Stream()) { }
        var dto = await run.Result;

        Assert.IsNotNull(dto);
        Assert.AreEqual("MD", dto.Name);
        Assert.AreEqual(10, dto.Value);
        Assert.AreEqual(0, mock.RepairCallCount, "Markdown JSON should parse without repair");
    }

    #endregion

    #region List<T> Streaming Tests

    /// <summary>
    /// List&lt;T&gt;를 스트리밍 + Result로 정상 파싱하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamListOfT_ValidArray_DeserializesSuccessfully()
    {
        var json = "[{\"Name\": \"A\", \"Value\": 1}, {\"Name\": \"B\", \"Value\": 2}]";
        var mock = new StreamingMockAIService(json, chunkSize: 8);

        var run = mock.BeginStream("list prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<List<SimpleDto>>();

        var buffer = new StringBuilder();
        await foreach (var chunk in run.Stream())
            buffer.Append(chunk);

        var list = await run.Result;

        Assert.AreEqual(json, buffer.ToString());
        Assert.IsNotNull(list);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("A", list[0].Name);
        Assert.AreEqual("B", list[1].Name);
        Assert.AreEqual(0, mock.RepairCallCount);
    }

    /// <summary>
    /// List&lt;T&gt; Result only (Stream 호출 없이) 동작 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamListOfT_ResultOnly_WorksWithoutStream()
    {
        var json = "[{\"Name\": \"Solo\", \"Value\": 7}]";
        var mock = new StreamingMockAIService(json, chunkSize: 50);

        var run = mock.BeginStream("list prompt")
            .As<List<SimpleDto>>();

        var list = await run.Result;

        Assert.IsNotNull(list);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("Solo", list[0].Name);
    }

    /// <summary>
    /// 깨진 배열 JSON 스트리밍 → repair로 유효한 배열 반환
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StreamStructuredOutput")]
    [TestMethod]
    public async Task StreamListOfT_InvalidThenRepair_Succeeds()
    {
        var mock = new StreamingMockAIService(
            "not an array",
            chunkSize: 50,
            "[{\"Name\": \"Fixed\", \"Value\": 3}]"
        );

        var run = mock.BeginStream("list prompt")
            .WithStructuredOutput(StructuredOutputPolicy.Default)
            .As<List<SimpleDto>>();

        await foreach (var _ in run.Stream()) { }
        var list = await run.Result;

        Assert.IsNotNull(list);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("Fixed", list[0].Name);
        Assert.AreEqual(1, mock.RepairCallCount);
    }

    #endregion
}
