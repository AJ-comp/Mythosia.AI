using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// Anthropic 요청 본문(JSON)을 fake HttpMessageHandler로 가로채 모델별 API 계약을 검증하는
/// 오프라인 단위테스트. API 키/네트워크 없이 동작한다.
///
/// 배경: 라이브 통합 테스트는 "응답이 비어있지 않음"만 단언하므로
/// 잘못된 max_tokens 캡핑, 모델 무시 같은 조용한 회귀를 잡지 못한다.
/// 이 테스트들은 요청 형태 자체를 고정(pin)한다.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class AnthropicRequestShapeTests
{
    private const string CannedResponse =
        "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

    #region Test Infrastructure

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        public List<string> CapturedBodies { get; } = new();
        public List<IReadOnlyDictionary<string, string[]>> CapturedHeaders { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedHeaders.Add(request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));

            if (request.Content != null)
            {
                CapturedBodies.Add(await request.Content.ReadAsStringAsync());
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CannedResponse, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (AnthropicService service, CaptureHttpMessageHandler handler) CreateService(string model)
    {
        var handler = new CaptureHttpMessageHandler();
        var service = new AnthropicService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(model);
        return (service, handler);
    }

    private static async Task<JsonElement> CaptureRequestBodyAsync(AnthropicService service, CaptureHttpMessageHandler handler)
    {
        await service.GetCompletionAsync("test prompt");
        Assert.AreEqual(1, handler.CapturedBodies.Count, "Exactly one request should have been sent");
        return JsonSerializer.Deserialize<JsonElement>(handler.CapturedBodies[0]);
    }

    #endregion

    #region GA client-tools contract

    [TestMethod]
    public async Task UserDefinedToolRequest_UsesGaContractWithoutLegacyBetaHeader()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets the weather for a location.",
            Handler = _ => Task.FromResult("sunny")
        });

        var body = await CaptureRequestBodyAsync(service, handler);
        var headers = handler.CapturedHeaders.Single();

        Assert.IsTrue(body.TryGetProperty("tools", out var tools));
        Assert.AreEqual(1, tools.GetArrayLength());
        Assert.AreEqual("offline-test-key", headers["x-api-key"].Single());
        Assert.AreEqual("2023-06-01", headers["anthropic-version"].Single());
        Assert.IsFalse(
            headers.ContainsKey("anthropic-beta"),
            "GA user-defined tools must not send the retired tools-2024-04-04 beta header.");
    }

    #endregion

    #region Adaptive-thinking models (Claude 5 / Fable 5 / Opus 4.7 / 4.8)

    /// <summary>
    /// Claude 5/Fable 5/Opus 4.7+는 커스텀 temperature를 거부(400)하므로 요청에서 생략돼야 한다.
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_7)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8)]
    public async Task AdaptiveModels_OmitTemperature(string model)
    {
        var (service, handler) = CreateService(model);
        service.Temperature = 0.7f;

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.IsFalse(body.TryGetProperty("temperature", out _),
            $"{model}: 'temperature' must be omitted (the API rejects it with HTTP 400)");
    }

    /// <summary>
    /// Fable 5는 명시적 thinking.type=disabled도 거부(400)하므로,
    /// thinking이 꺼져 있으면 thinking 파라미터 자체가 없어야 한다.
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_7)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8)]
    public async Task AdaptiveModels_ThinkingDisabled_OmitsThinkingParameter(string model)
    {
        var (service, handler) = CreateService(model);
        // ThinkingBudget 기본값(-1) = thinking 비활성

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.IsFalse(body.TryGetProperty("thinking", out _),
            $"{model}: 'thinking' must be omitted entirely when disabled (Fable 5 rejects type=disabled)");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5)]
    public async Task AlwaysOnModels_DefaultUseLowestThinkingWithoutDisplay(string model)
    {
        var (service, handler) = CreateService(model);

        var body = await CaptureRequestBodyAsync(service, handler);

        var thinking = body.GetProperty("thinking");
        Assert.AreEqual("adaptive", thinking.GetProperty("type").GetString());
        Assert.IsFalse(thinking.TryGetProperty("display", out _),
            $"{model} cannot disable thinking; the default should omit readable reasoning instead");
        Assert.AreEqual("low", body.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5)]
    public async Task Claude5_ThinkingDisabled_SendsExplicitDisabled(string model)
    {
        var (service, handler) = CreateService(model);

        var body = await CaptureRequestBodyAsync(service, handler);

        var thinking = body.GetProperty("thinking");
        Assert.AreEqual("disabled", thinking.GetProperty("type").GetString(),
            $"{model}: ThinkingBudget=-1 must preserve the library's reasoning-off behavior");
        Assert.IsFalse(body.TryGetProperty("output_config", out _),
            $"{model}: disabled thinking must not send output_config.effort");
    }

    /// <summary>
    /// Claude 5/Fable 5/Opus 4.7+는 budget_tokens 방식을 거부하므로 adaptive + output_config.effort로 변환돼야 한다.
    /// ThinkingBudget→effort 매핑: &lt;32768 → high, ≥32768 → xhigh, ≥100000 → max.
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 8192, "high")]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 32768, "xhigh")]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 100000, "max")]
    [DataRow(AIModels.Anthropic.ClaudeMythos5, 32768, "xhigh")]
    [DataRow(AIModels.Anthropic.ClaudeOpus5, 8192, "high")]
    [DataRow(AIModels.Anthropic.ClaudeOpus5, 100000, "max")]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5, 8192, "high")]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5, 32768, "xhigh")]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_7, 8192, "high")]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8, 32768, "xhigh")]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8, 100000, "max")]
    public async Task AdaptiveModels_ThinkingEnabled_UsesAdaptiveModeWithEffort(string model, int budget, string expectedEffort)
    {
        var (service, handler) = CreateService(model);
        service.ThinkingBudget = budget;

        var body = await CaptureRequestBodyAsync(service, handler);

        var thinking = body.GetProperty("thinking");
        Assert.AreEqual("adaptive", thinking.GetProperty("type").GetString(),
            $"{model}: thinking.type must be 'adaptive'");
        Assert.IsFalse(thinking.TryGetProperty("budget_tokens", out _),
            $"{model}: 'budget_tokens' must never be sent (the API rejects it with HTTP 400)");
        Assert.AreEqual("summarized", thinking.GetProperty("display").GetString(),
            $"{model}: explicitly enabled adaptive thinking must request readable summarized reasoning");
        Assert.AreEqual(expectedEffort, body.GetProperty("output_config").GetProperty("effort").GetString(),
            $"{model}: ThinkingBudget {budget} must map to effort '{expectedEffort}'");
    }

    [TestMethod]
    [DataRow(ClaudeReasoningEffort.Low, "low")]
    [DataRow(ClaudeReasoningEffort.Medium, "medium")]
    [DataRow(ClaudeReasoningEffort.High, "high")]
    [DataRow(ClaudeReasoningEffort.XHigh, "xhigh")]
    [DataRow(ClaudeReasoningEffort.Max, "max")]
    public async Task AdaptiveModels_ExposeEveryOfficialEffort(
        ClaudeReasoningEffort effort,
        string expected)
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeOpus5);
        service.AdaptiveThinkingEffort = effort;

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.AreEqual("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual("summarized", body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.AreEqual(expected, body.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    public async Task Claude46_ExplicitAdaptiveMethod_UsesAdaptiveRequestShape(string model)
    {
        var (service, handler) = CreateService(model);
        service.Temperature = 0.3f;
        service.WithAdaptiveThinkingParameters(
            ClaudeReasoningEffort.Medium,
            ClaudeThinkingDisplay.Omitted);

        var body = await CaptureRequestBodyAsync(service, handler);

        var thinking = body.GetProperty("thinking");
        Assert.AreEqual("adaptive", thinking.GetProperty("type").GetString());
        Assert.IsFalse(thinking.TryGetProperty("budget_tokens", out _));
        Assert.AreEqual("omitted", thinking.GetProperty("display").GetString());
        Assert.AreEqual("medium", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.IsFalse(body.TryGetProperty("temperature", out _),
            "Adaptive thinking must omit a caller-supplied custom temperature.");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    public async Task Claude46_DirectAdaptiveEffort_UsesAdaptiveRequestShape(string model)
    {
        var (service, handler) = CreateService(model);
        service.AdaptiveThinkingEffort = ClaudeReasoningEffort.Low;

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.AreEqual("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual("low", body.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    public async Task Claude46_XHighAdaptiveEffort_IsRejectedBeforeSending(string model)
    {
        var (service, handler) = CreateService(model);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.XHigh);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.GetCompletionAsync("test prompt"));

        Assert.AreEqual(nameof(AnthropicService.AdaptiveThinkingEffort), exception.ParamName);
        StringAssert.Contains(exception.Message, "does not support xhigh effort");
        Assert.AreEqual(0, handler.CapturedBodies.Count, "Invalid effort must not reach HTTP.");
    }

    [TestMethod]
    public async Task Claude46_LegacyThinkingMethod_AfterAdaptiveMethod_RestoresManualMode()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);
        service.WithThinkingParameters(4096);
        service.MaxTokens = 16384;

        var body = await CaptureRequestBodyAsync(service, handler);

        var thinking = body.GetProperty("thinking");
        Assert.AreEqual("enabled", thinking.GetProperty("type").GetString());
        Assert.AreEqual(4096, thinking.GetProperty("budget_tokens").GetInt32());
        Assert.IsFalse(body.TryGetProperty("output_config", out _));
    }

    [TestMethod]
    public async Task ManualOnlyModel_ExplicitAdaptiveThinking_IsRejectedBeforeSending()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeHaiku4_5_251001);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Medium);

        var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.GetCompletionAsync("test prompt"));

        StringAssert.Contains(exception.Message, "does not support adaptive thinking");
        Assert.AreEqual(0, handler.CapturedBodies.Count, "Unsupported adaptive mode must not reach HTTP.");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5)]
    public async Task AlwaysOnModels_DisableReasoningProfileUsesLowOmittedAndRestoresSettings(string model)
    {
        var (service, handler) = CreateService(model);
        service.AdaptiveThinkingEffort = ClaudeReasoningEffort.Max;
        service.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Summarized;

        await service.GetCompletionAsync(
            "test prompt",
            new AIRequestProfile { DisableReasoning = true });

        var body = JsonSerializer.Deserialize<JsonElement>(handler.CapturedBodies.Single());
        Assert.AreEqual("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual(
            "omitted",
            body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.AreEqual("low", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.AreEqual(ClaudeReasoningEffort.Max, service.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Summarized, service.AdaptiveThinkingDisplay);
    }

    [TestMethod]
    public async Task SummarizationProfile_ReservesClaudeOutputBudgetAndRestoresCallerSetting()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.MaxTokens = 4096;

        await service.GetCompletionAsync("Summarize this conversation.", RequestProfiles.Summarization);

        Assert.AreEqual(1, handler.CapturedBodies.Count);
        var body = JsonSerializer.Deserialize<JsonElement>(handler.CapturedBodies.Single());
        Assert.AreEqual(1024u, body.GetProperty("max_tokens").GetUInt32(),
            "Claude needs more than the common 256-token cap to finish a library-owned summary reliably.");
        Assert.AreEqual(4096u, service.MaxTokens,
            "The internal summary budget must not leak into the caller's subsequent requests.");
    }

    /// <summary>
    /// 회귀 테스트: Claude 5/Opus 4.7/4.8/Fable 5는 128K 출력을 지원하므로 MaxTokens가 32K로 캡핑되면 안 된다.
    /// (v6.5.0까지 generic opus-4 분기(32768)로 떨어지는 버그가 있었음)
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_7)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8)]
    public async Task HighCeilingModels_MaxTokensNotCappedAt32K(string model)
    {
        var (service, handler) = CreateService(model);
        service.MaxTokens = 120000;

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.AreEqual(120000u, body.GetProperty("max_tokens").GetUInt32(),
            $"{model}: max_tokens 120000 must pass through (128K ceiling), not be capped at 32768");
    }

    #endregion

    #region Manual-thinking models (Opus 4.6 / Sonnet 4.x / Haiku 4.5)

    /// <summary>
    /// 수동 thinking 모델은 기존 budget_tokens 방식을 유지하고 temperature는 1.0으로 강제된다.
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeHaiku4_5_251001)]
    public async Task ManualThinkingModels_UseBudgetTokens(string model)
    {
        var (service, handler) = CreateService(model);
        service.ThinkingBudget = 4096;
        service.MaxTokens = 16384;

        var body = await CaptureRequestBodyAsync(service, handler);

        var thinking = body.GetProperty("thinking");
        Assert.AreEqual("enabled", thinking.GetProperty("type").GetString());
        Assert.AreEqual(4096, thinking.GetProperty("budget_tokens").GetInt32());
        Assert.AreEqual(1.0f, body.GetProperty("temperature").GetSingle(), 0.0001f,
            "Manual thinking requires temperature=1 (Claude requirement)");
        Assert.IsFalse(body.TryGetProperty("output_config", out _),
            "Manual-thinking models must not receive output_config.effort");
    }

    /// <summary>
    /// budget_tokens >= max_tokens일 때 max_tokens가 budget+1024로 자동 조정돼야 한다.
    /// </summary>
    [TestMethod]
    public async Task ManualThinkingModels_AutoAdjustMaxTokens_WhenBudgetExceedsMax()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.MaxTokens = 8192;
        service.ThinkingBudget = 8192;

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.AreEqual(9216u, body.GetProperty("max_tokens").GetUInt32(),
            "max_tokens must auto-adjust to ThinkingBudget + 1024");
    }

    [TestMethod]
    public async Task ManualThinkingModels_RejectBudgetAtModelOutputCeilingBeforeSending()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.MaxTokens = 128000;
        service.ThinkingBudget = 128000;

        var exception = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.GetCompletionAsync("test prompt"));

        Assert.AreEqual(nameof(AnthropicService.ThinkingBudget), exception.ParamName);
        StringAssert.Contains(exception.Message, "lower than the model's maximum output tokens (128000)");
        Assert.AreEqual(0, handler.CapturedBodies.Count, "Invalid thinking budgets must not reach HTTP.");
    }

    /// <summary>
    /// 수동 모델은 thinking이 꺼져 있으면 사용자 temperature를 그대로 보낸다.
    /// </summary>
    [TestMethod]
    public async Task ManualModels_PreserveCustomTemperature_WhenThinkingDisabled()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.Temperature = 0.3f;

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.AreEqual(0.3f, body.GetProperty("temperature").GetSingle(), 0.0001f);
    }

    [TestMethod]
    public async Task VeryCreativePreset_StaysWithinAnthropicTemperatureRange()
    {
        var (service, handler) = CreateService(AIModels.Anthropic.ClaudeSonnet4_6);
        service.WithTemperaturePreset(TemperaturePreset.VeryCreative);

        var body = await CaptureRequestBodyAsync(service, handler);

        Assert.AreEqual(1.0f, body.GetProperty("temperature").GetSingle(), 0.0001f);
    }

    #endregion

    #region Model gates (max-output ceiling / thinking support / vision)

    private sealed class GateProbe : AnthropicService
    {
        public GateProbe() : base("offline-test-key", new HttpClient(new CaptureHttpMessageHandler())) { }

        public uint MaxOutputTokensFor(string model)
        {
            ChangeModel(model);
            return GetModelMaxOutputTokens();
        }
    }

    /// <summary>
    /// 모델별 최대 출력 토큰 테이블을 고정한다. 새 모델 추가 시 이 테이블도 갱신해야 한다.
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_7, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_5_250929, 64000u)]
    [DataRow(AIModels.Anthropic.ClaudeHaiku4_5_251001, 64000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_5_251101, 64000u)]
    public void ModelMaxOutputTokens_Table(string model, uint expected)
    {
        Assert.AreEqual(expected, new GateProbe().MaxOutputTokensFor(model), $"wrong ceiling for {model}");
    }

    /// <summary>
    /// 모든 Anthropic 모델 상수는 extended thinking을 지원해야 한다.
    /// 새 모델이 게이트(IsExtendedThinkingModel)에 누락되면 여기서 잡힌다.
    /// </summary>
    [TestMethod]
    public void AllAnthropicModels_SupportExtendedThinking()
    {
        foreach (var model in AllAnthropicModelConstants())
        {
            var (service, _) = CreateService(model);
            Assert.IsTrue(service.SupportsExtendedThinking,
                $"{model}: SupportsExtendedThinking is false — IsExtendedThinkingModel() gate is missing this model");
        }
    }

    /// <summary>
    /// 회귀 테스트: 비전 호출이 요청 모델을 다른 모델로 조용히 교체하면 안 된다.
    /// (v6.5.0까지 비전 게이트가 fable-5/sonnet-4-x/haiku-4-x를 누락해 Sonnet 4.6으로 교체했음)
    /// </summary>
    [TestMethod]
    public async Task VisionCall_DoesNotSilentlySwapModel()
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"mythosia-test-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            foreach (var model in AllAnthropicModelConstants())
            {
                var (service, handler) = CreateService(model);

                await service.GetCompletionWithImageAsync("describe", imagePath);

                Assert.AreEqual(model, service.Model,
                    $"{model}: GetCompletionWithImageAsync silently changed the model to '{service.Model}'");

                var body = JsonSerializer.Deserialize<JsonElement>(handler.CapturedBodies.Single());
                Assert.AreEqual(model, body.GetProperty("model").GetString(),
                    $"{model}: the outgoing request used a different model");
            }
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    private static IEnumerable<string> AllAnthropicModelConstants()
    {
        return typeof(AIModels.Anthropic)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
    }

    #endregion
}
