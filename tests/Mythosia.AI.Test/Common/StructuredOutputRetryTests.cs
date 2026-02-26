using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// Structured Output 자동 복구 retry 로직 검증 (API 키 불필요, 프로바이더 무관)
/// MockAIService를 사용하여 GetCompletionAsync&lt;T&gt;()의 retry 동작을 테스트
/// </summary>
[TestClass]
public class StructuredOutputRetryTests
{
    #region Test Models

    public class SimpleResult
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    #endregion

    #region Mock AIService

    /// <summary>
    /// 테스트용 AIService 구현. GetCompletionAsync(string)를 override하여
    /// 미리 정의된 응답을 순서대로 반환한다.
    /// </summary>
    private class MockAIService : AIService
    {
        private readonly Queue<string> _responses = new();
        public int CallCount { get; private set; }
        public List<string> ReceivedPrompts { get; } = new();

        public MockAIService(params string[] responses)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
            foreach (var r in responses)
                _responses.Enqueue(r);
        }

        public override AIProvider Provider => AIProvider.OpenAI;

        public override Task<string> GetCompletionAsync(Message message)
        {
            CallCount++;
            ReceivedPrompts.Add(message.Content);
            if (_responses.Count == 0)
                throw new AIServiceException("No more mock responses");
            return Task.FromResult(_responses.Dequeue());
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

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    #endregion

    #region Retry Logic Tests

    /// <summary>
    /// 첫 시도에서 유효한 JSON이 반환되면 retry 없이 바로 성공
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_ValidJsonOnFirstAttempt_ReturnsWithoutRetry()
    {
        var mock = new MockAIService("{\"Name\": \"Alice\", \"Value\": 42}");

        var result = await mock.GetCompletionAsync<SimpleResult>("test prompt");

        Assert.IsNotNull(result);
        Assert.AreEqual("Alice", result.Name);
        Assert.AreEqual(42, result.Value);
        Assert.AreEqual(1, mock.CallCount, "Should call LLM only once");
    }

    /// <summary>
    /// 첫 시도가 깨진 JSON → 자동 수정 프롬프트 → 두 번째 시도 성공
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_InvalidThenValid_RetriesAndSucceeds()
    {
        var mock = new MockAIService(
            "This is not valid JSON at all",
            "{\"Name\": \"Bob\", \"Value\": 99}"
        );

        var result = await mock.GetCompletionAsync<SimpleResult>("test prompt");

        Assert.IsNotNull(result);
        Assert.AreEqual("Bob", result.Name);
        Assert.AreEqual(99, result.Value);
        Assert.AreEqual(2, mock.CallCount, "Should call LLM twice (1 initial + 1 retry)");
    }

    /// <summary>
    /// 첫 시도 실패 → 두 번째 실패 → 세 번째 성공 (MaxRetries=2)
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_TwoFailsThenSuccess_UsesAllRetries()
    {
        var mock = new MockAIService(
            "broken json 1",
            "still broken {{{",
            "{\"Name\": \"Charlie\", \"Value\": 7}"
        );
        mock.StructuredOutputMaxRetries = 2;

        var result = await mock.GetCompletionAsync<SimpleResult>("test prompt");

        Assert.IsNotNull(result);
        Assert.AreEqual("Charlie", result.Name);
        Assert.AreEqual(3, mock.CallCount, "Should call LLM 3 times (1 initial + 2 retries)");
    }

    /// <summary>
    /// 모든 시도 실패 → StructuredOutputException 발생, 풍부한 컨텍스트 포함
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_AllAttemptsFail_ThrowsStructuredOutputException()
    {
        var mock = new MockAIService(
            "not json 1",
            "not json 2",
            "not json 3"
        );
        mock.StructuredOutputMaxRetries = 2;

        StructuredOutputException? ex = null;
        try
        {
            await mock.GetCompletionAsync<SimpleResult>("my prompt");
            Assert.Fail("Expected StructuredOutputException was not thrown");
        }
        catch (StructuredOutputException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual("SimpleResult", ex.TargetTypeName);
        Assert.AreEqual("not json 1", ex.FirstRawResponse);
        Assert.AreEqual("not json 3", ex.LastRawResponse);
        Assert.AreEqual(3, ex.AttemptCount);
        Assert.IsFalse(string.IsNullOrEmpty(ex.ParseError));
        Assert.IsFalse(string.IsNullOrEmpty(ex.SchemaJson));

        Console.WriteLine($"[Exception] {ex.Message}");
        Console.WriteLine($"[FirstRaw] {ex.FirstRawResponse}");
        Console.WriteLine($"[LastRaw] {ex.LastRawResponse}");
        Console.WriteLine($"[ParseError] {ex.ParseError}");
        Console.WriteLine($"[Attempts] {ex.AttemptCount}");
    }

    /// <summary>
    /// MaxRetries=0이면 retry 없이 즉시 실패
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_MaxRetriesZero_NoRetryOnFailure()
    {
        var mock = new MockAIService("broken json");
        mock.StructuredOutputMaxRetries = 0;

        StructuredOutputException? ex = null;
        try
        {
            await mock.GetCompletionAsync<SimpleResult>("test");
            Assert.Fail("Expected StructuredOutputException was not thrown");
        }
        catch (StructuredOutputException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual(1, ex.AttemptCount);
        Assert.AreEqual(1, mock.CallCount, "Should call LLM only once when MaxRetries=0");
    }

    /// <summary>
    /// retry 시 correction prompt에 이전 응답과 파싱 에러가 포함되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_CorrectionPrompt_ContainsPreviousResponseAndError()
    {
        var mock = new MockAIService(
            "BROKEN_OUTPUT_XYZ",
            "{\"Name\": \"Fixed\", \"Value\": 1}"
        );

        await mock.GetCompletionAsync<SimpleResult>("original prompt");

        Assert.AreEqual(2, mock.ReceivedPrompts.Count);
        Assert.AreEqual("original prompt", mock.ReceivedPrompts[0]);

        var correctionPrompt = mock.ReceivedPrompts[1];
        Assert.IsTrue(correctionPrompt.Contains("BROKEN_OUTPUT_XYZ"),
            "Correction prompt should contain the previous raw response");
        Assert.IsTrue(correctionPrompt.Contains("STRUCTURED OUTPUT CORRECTION"),
            "Correction prompt should be clearly marked as a correction");
    }

    /// <summary>
    /// 마크다운 코드블록으로 감싼 JSON도 retry 전에 추출 후 파싱 성공
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_MarkdownWrappedJson_ParsesWithoutRetry()
    {
        var mock = new MockAIService("```json\n{\"Name\": \"MD\", \"Value\": 10}\n```");

        var result = await mock.GetCompletionAsync<SimpleResult>("test");

        Assert.AreEqual("MD", result.Name);
        Assert.AreEqual(1, mock.CallCount, "Should not retry when markdown JSON is parseable");
    }

    /// <summary>
    /// _structuredOutputSchemaJson이 finally에서 항상 null로 초기화되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_SchemaIsCleared_AfterSuccessOrFailure()
    {
        // Success case
        var mockSuccess = new MockAIService("{\"Name\": \"A\", \"Value\": 1}");
        await mockSuccess.GetCompletionAsync<SimpleResult>("test");
        Assert.IsNull(mockSuccess._structuredOutputSchemaJson, "Schema must be null after success");

        // Failure case
        var mockFail = new MockAIService("broken");
        mockFail.StructuredOutputMaxRetries = 0;
        try { await mockFail.GetCompletionAsync<SimpleResult>("test"); } catch { }
        Assert.IsNull(mockFail._structuredOutputSchemaJson, "Schema must be null after failure");
    }

    #endregion

    #region Per-Call Policy Tests

    /// <summary>
    /// WithStructuredOutputPolicy로 설정한 MaxRepairAttempts가 서비스 기본값을 오버라이드하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task WithStructuredOutputPolicy_OverridesServiceDefault()
    {
        // 서비스 기본값은 2 (총 3회 시도), 정책으로 0 (총 1회)으로 오버라이드
        var mock = new MockAIService("broken json", "should not reach");
        mock.StructuredOutputMaxRetries = 2;
        mock._currentStructuredOutputPolicy = new Mythosia.AI.Models.StructuredOutputPolicy
        {
            MaxRepairAttempts = 0
        };

        StructuredOutputException? ex = null;
        try
        {
            await mock.GetCompletionAsync<SimpleResult>("test");
            Assert.Fail("Expected StructuredOutputException");
        }
        catch (StructuredOutputException caught) { ex = caught; }

        Assert.IsNotNull(ex);
        Assert.AreEqual(1, ex.AttemptCount, "Policy MaxRepairAttempts=0 → only 1 attempt");
        Assert.AreEqual(1, mock.CallCount);
    }

    /// <summary>
    /// per-call 정책이 호출 후 자동으로 초기화되는지 검증 (일회성)
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task WithStructuredOutputPolicy_IsConsumedAfterOneCall()
    {
        var mock = new MockAIService(
            "{\"Name\": \"A\", \"Value\": 1}",
            "broken",
            "broken",
            "broken"
        );
        mock.StructuredOutputMaxRetries = 2;
        mock._currentStructuredOutputPolicy = new Mythosia.AI.Models.StructuredOutputPolicy
        {
            MaxRepairAttempts = 0
        };

        // 1차 호출: 정책 적용 (성공)
        var result = await mock.GetCompletionAsync<SimpleResult>("first call");
        Assert.AreEqual("A", result.Name);

        // 정책이 소비되었는지 확인
        Assert.IsNull(mock._currentStructuredOutputPolicy,
            "Policy must be null after consumption");

        // 2차 호출: 서비스 기본값(MaxRetries=2) 복귀 → 3번째 응답 "broken"에서 3회 시도
        StructuredOutputException? ex = null;
        try
        {
            await mock.GetCompletionAsync<SimpleResult>("second call");
            Assert.Fail("Expected StructuredOutputException");
        }
        catch (StructuredOutputException caught) { ex = caught; }

        Assert.IsNotNull(ex);
        Assert.AreEqual(3, ex.AttemptCount,
            "After policy consumed, should fall back to service default (MaxRetries=2 → 3 attempts)");
    }

    /// <summary>
    /// per-call 정책의 MaxRepairAttempts가 null이면 서비스 기본값 사용
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task WithStructuredOutputPolicy_NullMaxRepair_UsesServiceDefault()
    {
        var mock = new MockAIService(
            "broken 1",
            "{\"Name\": \"OK\", \"Value\": 5}"
        );
        mock.StructuredOutputMaxRetries = 1;
        mock._currentStructuredOutputPolicy = new Mythosia.AI.Models.StructuredOutputPolicy
        {
            MaxRepairAttempts = null  // 서비스 기본값 사용
        };

        var result = await mock.GetCompletionAsync<SimpleResult>("test");
        Assert.AreEqual("OK", result.Name);
        Assert.AreEqual(2, mock.CallCount, "null policy → service default (MaxRetries=1 → 2 attempts max)");
    }

    #endregion

    #region List<T> Support Tests

    /// <summary>
    /// List&lt;T&gt;를 T로 사용해 유효한 JSON 배열을 첫 시도에 파싱 성공
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_ListOfT_ValidArrayOnFirstAttempt()
    {
        var mock = new MockAIService(
            "[{\"Name\": \"Alice\", \"Value\": 1}, {\"Name\": \"Bob\", \"Value\": 2}]"
        );

        var result = await mock.GetCompletionAsync<List<SimpleResult>>("list prompt");

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Alice", result[0].Name);
        Assert.AreEqual(1, result[0].Value);
        Assert.AreEqual("Bob", result[1].Name);
        Assert.AreEqual(2, result[1].Value);
        Assert.AreEqual(1, mock.CallCount, "Should call LLM only once");
    }

    /// <summary>
    /// 깨진 배열 JSON → repair로 유효한 배열 반환 시 성공
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_ListOfT_InvalidThenValidArray_Succeeds()
    {
        var mock = new MockAIService(
            "not a json array",
            "[{\"Name\": \"Fixed\", \"Value\": 99}]"
        );

        var result = await mock.GetCompletionAsync<List<SimpleResult>>("list prompt");

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Fixed", result[0].Name);
        Assert.AreEqual(2, mock.CallCount, "Should retry once");
    }

    /// <summary>
    /// 빈 배열도 정상 파싱되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_ListOfT_EmptyArray_Succeeds()
    {
        var mock = new MockAIService("[]");

        var result = await mock.GetCompletionAsync<List<SimpleResult>>("empty list");

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
        Assert.AreEqual(1, mock.CallCount);
    }

    /// <summary>
    /// 마크다운 코드블록으로 감싼 JSON 배열도 정상 추출/파싱
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task GetCompletionAsyncT_ListOfT_MarkdownWrappedArray_Succeeds()
    {
        var mock = new MockAIService(
            "```json\n[{\"Name\": \"MD\", \"Value\": 5}]\n```"
        );

        var result = await mock.GetCompletionAsync<List<SimpleResult>>("markdown list");

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("MD", result[0].Name);
        Assert.AreEqual(1, mock.CallCount);
    }

    #endregion
}
