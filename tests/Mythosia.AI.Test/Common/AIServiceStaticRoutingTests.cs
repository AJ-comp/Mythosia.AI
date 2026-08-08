using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Reflection;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// AIService.QuickAskAsync 경로(GetProviderFromModel / CreateService)의 오프라인 단위테스트.
///
/// 배경: 이 경로는 v6.5.0까지 테스트 커버리지가 전혀 없어서
/// "대문자 접두사 비교로 모든 소문자 모델 ID가 ArgumentException" +
/// "요청 모델이 무시되고 provider 기본 모델로 실행" 버그가 잡히지 않았다.
/// 리플렉션 스윕으로 AIModels의 모든 상수가 라우팅 가능함을 강제한다 —
/// 새 모델 상수가 추가되면 자동으로 검증 대상이 된다.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class AIServiceStaticRoutingTests
{
    /// <summary>
    /// AIModels의 모든 모델 상수가 올바른 provider로 라우팅돼야 한다.
    /// 중첩 클래스 이름(OpenAI/Anthropic/Google/xAI/DeepSeek/Perplexity)이 기대 provider다.
    /// </summary>
    [TestMethod]
    public void EveryModelConstant_RoutesToItsProvider()
    {
        var failures = new List<string>();
        var swept = 0;

        foreach (var providerClass in typeof(AIModels).GetNestedTypes())
        {
            var expectedProvider = providerClass.Name;

            foreach (var field in providerClass.GetFields(BindingFlags.Public | BindingFlags.Static)
                                               .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                var model = (string)field.GetRawConstantValue()!;
                swept++;

                try
                {
                    var actual = AIService.GetProviderFromModel(model);
                    if (actual != expectedProvider)
                        failures.Add($"{providerClass.Name}.{field.Name} (\"{model}\") → {actual}, expected {expectedProvider}");
                }
                catch (ArgumentException)
                {
                    failures.Add($"{providerClass.Name}.{field.Name} (\"{model}\") → ArgumentException (unroutable)");
                }
            }
        }

        Assert.IsTrue(swept > 0, "Reflection sweep found no model constants — check AIModels structure");
        Assert.AreEqual(0, failures.Count,
            $"Unroutable or misrouted model constants:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// 회귀 테스트: 실제 소문자 모델 ID가 정상 라우팅돼야 한다
    /// (기존 버그: "Claude"/"Gpt" 대문자 접두사 비교로 전부 예외 발생).
    /// </summary>
    [TestMethod]
    [DataRow("claude-fable-5", nameof(AIProvider.Anthropic))]
    [DataRow("claude-mythos-5", nameof(AIProvider.Anthropic))]
    [DataRow("gpt-4o-mini", nameof(AIProvider.OpenAI))]
    [DataRow("gpt-5.6-sol", nameof(AIProvider.OpenAI))]
    [DataRow("o3-pro", nameof(AIProvider.OpenAI))]
    [DataRow("grok-4.3", nameof(AIProvider.xAI))]
    [DataRow("gemini-2.5-flash", nameof(AIProvider.Google))]
    [DataRow("gemini-3.6-flash", nameof(AIProvider.Google))]
    [DataRow("deepseek-chat", nameof(AIProvider.DeepSeek))]
    [DataRow("sonar-pro", nameof(AIProvider.Perplexity))]
    public void LowercaseModelIds_RouteCorrectly(string model, string expectedProvider)
    {
        Assert.AreEqual(expectedProvider, AIService.GetProviderFromModel(model));
    }

    /// <summary>
    /// 알 수 없는 모델은 명시적 ArgumentException이어야 한다 (조용한 기본값 금지).
    /// </summary>
    [TestMethod]
    public void UnknownModel_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => AIService.GetProviderFromModel("totally-unknown-model"));
    }

    /// <summary>
    /// 회귀 테스트: CreateService는 올바른 서비스 타입을 만들고
    /// 요청된 모델을 실제로 적용해야 한다 (기존 버그: provider 기본 모델로 무시).
    /// </summary>
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5, typeof(AnthropicService))]
    [DataRow(AIModels.Anthropic.ClaudeMythos5, typeof(AnthropicService))]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8, typeof(AnthropicService))]
    [DataRow(AIModels.OpenAI.Gpt4oMini, typeof(OpenAIService))]
    [DataRow(AIModels.OpenAI.O3, typeof(OpenAIService))]
    [DataRow(AIModels.Google.Gemini2_5Flash, typeof(GoogleAIService))]
    [DataRow(AIModels.Google.Gemini3_6Flash, typeof(GoogleAIService))]
    [DataRow(AIModels.xAI.Grok4_5, typeof(XAIService))]
    [DataRow(AIModels.DeepSeek.Chat, typeof(DeepSeekService))]
    [DataRow(AIModels.Perplexity.Sonar, typeof(PerplexityService))]
    public void CreateService_AppliesRequestedModel(string model, Type expectedServiceType)
    {
        using var httpClient = new HttpClient();

        var service = AIService.CreateService(model, "offline-test-key", httpClient);

        Assert.IsInstanceOfType(service, expectedServiceType);
        Assert.AreEqual(model, service.Model,
            "CreateService must apply the requested model, not the provider default");
    }
}
