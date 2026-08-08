using Mythosia.AI.Exceptions;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// 공급자 에러 본문 → ContextLengthExceededException 번역 테스트.
///
/// 오탐이 가장 위험하다. 무관한 400 을 컨텍스트 초과로 오판하면 코어가 대화를 압축하고,
/// 압축은 메시지를 되돌릴 수 없게 지운다. 그래서 "안 걸리는 것" 을 검증하는 테스트가
/// "걸리는 것" 만큼 중요하다.
/// </summary>
[TestClass]
public class ContextLengthErrorTranslationTests
{
    // 운영 vLLM 이 실제로 돌려준 본문.
    private const string VllmBody =
        """{"error":{"message":"This model's maximum context length is 50000 tokens. However, you requested 0 output tokens and your prompt contains at least 50001 input tokens, for a total of at least 50001 tokens. Please reduce the length of the input prompt or the number of requested output tokens.","type":"BadRequestError","param":"input_tokens","code":400}}""";

    private const string OpenAiBody =
        """{"error":{"message":"This model's maximum context length is 128000 tokens. However, your messages resulted in 130000 tokens.","type":"invalid_request_error","code":"context_length_exceeded"}}""";

    private const string AnthropicBody =
        """{"type":"error","error":{"type":"invalid_request_error","message":"prompt is too long: 250000 tokens > 200000 maximum"}}""";

    private const string GoogleBody =
        """{"error":{"code":400,"message":"The input token count (1050000) exceeds the maximum number of tokens allowed (1048576).","status":"INVALID_ARGUMENT"}}""";

    private const string XaiBody =
        """{"code":"invalid-argument","error":"This model's maximum prompt length is 256000 but the request contains 280193 tokens."}""";

    #region 감지되어야 하는 것

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void Vllm400_DetectedWithBothTokenCounts()
    {
        var detected = AIHttpErrorFactory.IsContextLengthExceeded(400, VllmBody, out var max, out var requested);

        Assert.IsTrue(detected);
        Assert.AreEqual(50000, max);
        Assert.AreEqual(50001, requested);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void OpenAi400_DetectedViaErrorCode()
    {
        var detected = AIHttpErrorFactory.IsContextLengthExceeded(400, OpenAiBody, out var max, out var requested);

        Assert.IsTrue(detected);
        Assert.AreEqual(128000, max);
        Assert.AreEqual(130000, requested);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void Anthropic_PromptTooLong_Detected()
    {
        var detected = AIHttpErrorFactory.IsContextLengthExceeded(400, AnthropicBody, out var max, out var requested);

        Assert.IsTrue(detected);
        Assert.AreEqual(200000, max);
        Assert.AreEqual(250000, requested);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void Google_InputTokenCount_Detected()
    {
        var detected = AIHttpErrorFactory.IsContextLengthExceeded(400, GoogleBody, out var max, out var requested);

        Assert.IsTrue(detected);
        Assert.AreEqual(1048576, max);
        Assert.AreEqual(1050000, requested);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void Xai_MaximumPromptLength_Detected()
    {
        var detected = AIHttpErrorFactory.IsContextLengthExceeded(400, XaiBody, out var max, out var requested);

        Assert.IsTrue(detected);
        Assert.AreEqual(256000, max);
        Assert.AreEqual(280193, requested);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void Anthropic_CombinedInputAndMaxTokens_Detected()
    {
        // 이 라이브러리는 Anthropic 요청에 max_tokens 를 항상 싣는다. 그래서 입력이 창을 '단독'으로
        // 넘기 전에 이 결합 형태로 먼저 거절당한다 — 즉 실제로 부딪히는 건 거의 이쪽이다.
        const string body =
            """{"type":"error","error":{"type":"invalid_request_error","message":"input length and `max_tokens` exceed context limit: 186433 + 20000 > 200000, decrease input length or `max_tokens` and try again"}}""";

        var detected = AIHttpErrorFactory.IsContextLengthExceeded(400, body, out var max, out var requested);

        Assert.IsTrue(detected);
        Assert.AreEqual(200000, max);
        Assert.AreEqual(206433, requested, "서버가 거절한 건 합계다 — 입력만 보면 창에 들어가는 것처럼 보인다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void Status413_IsAlsoConsidered()
    {
        Assert.IsTrue(AIHttpErrorFactory.IsContextLengthExceeded(413, AnthropicBody, out _, out _));
    }

    #endregion

    #region 감지되면 안 되는 것 (오탐 방지)

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void UnrelatedBadRequest_NotDetected()
    {
        const string body =
            """{"error":{"message":"Invalid value for 'temperature': must be <= 2","type":"invalid_request_error"}}""";

        Assert.IsFalse(AIHttpErrorFactory.IsContextLengthExceeded(400, body, out _, out _));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void MaxTokensComplaint_NotDetected()
    {
        // 출력 토큰 상한 이야기지 컨텍스트 초과가 아니다. 압축해도 안 고쳐진다.
        const string body =
            """{"error":{"message":"max_tokens is too large: 200000. This model supports at most 4096 completion tokens","type":"invalid_request_error"}}""";

        Assert.IsFalse(AIHttpErrorFactory.IsContextLengthExceeded(400, body, out _, out _));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void RateLimit429_NeverDetected()
    {
        // 상태코드 게이트. 429 본문에 우연히 비슷한 문구가 있어도 압축은 해법이 아니다.
        Assert.IsFalse(AIHttpErrorFactory.IsContextLengthExceeded(429, VllmBody, out _, out _));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ServerError500_NeverDetected()
    {
        Assert.IsFalse(AIHttpErrorFactory.IsContextLengthExceeded(500, VllmBody, out _, out _));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void EmptyBody_NotDetected()
    {
        Assert.IsFalse(AIHttpErrorFactory.IsContextLengthExceeded(400, "", out _, out _));
        Assert.IsFalse(AIHttpErrorFactory.IsContextLengthExceeded(400, null, out _, out _));
    }

    #endregion

    #region 예외 생성 / 메시지 계약

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void FromHttp_ContextOverflow_ReturnsTypedException()
    {
        var ex = AIHttpErrorFactory.FromHttp(400, "Bad Request", VllmBody);

        var typed = ex as ContextLengthExceededException;
        Assert.IsNotNull(typed);
        Assert.AreEqual(400, typed!.StatusCode);
        Assert.AreEqual(50000, typed.MaxContextTokens);
        Assert.AreEqual(VllmBody, typed.ErrorDetails, "본문은 진단용으로 보존돼야 한다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void FromHttp_UnrelatedError_ReturnsPlainException()
    {
        var ex = AIHttpErrorFactory.FromHttp(400, "Bad Request", """{"error":{"message":"nope"}}""");

        Assert.IsInstanceOfType(ex, typeof(AIServiceException));
        Assert.IsNotInstanceOfType(ex, typeof(ContextLengthExceededException));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void FromHttp_PreservesLegacyMessageShape()
    {
        // 기존 메시지 포맷에 의존하는 로그/화면이 깨지면 안 된다.
        var ex = AIHttpErrorFactory.FromHttp(400, "Bad Request", VllmBody);

        StringAssert.StartsWith(ex.Message, "API request failed (400):");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void FromHttp_CustomPrefix_IsHonored()
    {
        var ex = AIHttpErrorFactory.FromHttp(400, "Bad Request", VllmBody, "Gemini API request failed");

        StringAssert.StartsWith(ex.Message, "Gemini API request failed (400):");
    }

    #endregion

    #region 스트리밍 메타데이터

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void BuildErrorMetadata_ContextOverflow_SetsFlagAndCounts()
    {
        var metadata = AIHttpErrorFactory.BuildErrorMetadata(400, VllmBody);

        Assert.AreEqual(true, metadata[AIHttpErrorFactory.ContextLengthExceededKey]);
        Assert.AreEqual(50000, metadata[AIHttpErrorFactory.MaxContextTokensKey]);
        Assert.AreEqual(50001, metadata[AIHttpErrorFactory.RequestedTokensKey]);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void BuildErrorMetadata_AlwaysKeepsLegacyKeys()
    {
        // 기존 소비자는 error / status_code 를 읽는다. 추가만 하고 빼지 않는다.
        var metadata = AIHttpErrorFactory.BuildErrorMetadata(400, """{"error":{"message":"nope"}}""");

        Assert.IsTrue(metadata.ContainsKey("error"));
        Assert.AreEqual(400, metadata["status_code"]);
        Assert.IsFalse(metadata.ContainsKey(AIHttpErrorFactory.ContextLengthExceededKey));
    }

    #endregion
}
