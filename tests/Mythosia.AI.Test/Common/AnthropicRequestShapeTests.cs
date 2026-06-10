using Mythosia.AI.Models;
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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

    #region Adaptive-thinking models (Fable 5 / Opus 4.7 / 4.8)

    /// <summary>
    /// Fable 5/Opus 4.7+는 커스텀 temperature를 거부(400)하므로 요청에서 생략돼야 한다.
    /// </summary>
    [DataTestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
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
    [DataTestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
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

    /// <summary>
    /// Fable 5/Opus 4.7+는 budget_tokens 방식을 거부하므로 adaptive + output_config.effort로 변환돼야 한다.
    /// ThinkingBudget→effort 매핑: &lt;32768 → high, ≥32768 → xhigh, ≥100000 → max.
    /// </summary>
    [DataTestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 8192, "high")]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 32768, "xhigh")]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 100000, "max")]
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
        Assert.AreEqual(expectedEffort, body.GetProperty("output_config").GetProperty("effort").GetString(),
            $"{model}: ThinkingBudget {budget} must map to effort '{expectedEffort}'");
    }

    /// <summary>
    /// 회귀 테스트: Opus 4.7/4.8/Fable 5는 128K 출력을 지원하므로 MaxTokens가 32K로 캡핑되면 안 된다.
    /// (v6.5.0까지 generic opus-4 분기(32768)로 떨어지는 버그가 있었음)
    /// </summary>
    [DataTestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
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
    [DataTestMethod]
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
    [DataTestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_7, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6, 128000u)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6, 65536u)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_5_250929, 65536u)]
    [DataRow(AIModels.Anthropic.ClaudeHaiku4_5_251001, 65536u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_5_251101, 64000u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_1_250805, 32768u)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_250514, 32768u)]
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
