using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Live")]
[TestCategory("InferenceSpeed")]
[DoNotParallelize]
public class InferenceSpeedLiveTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var provider in new[] { "Anthropic", "OpenAI", "Google", "xAI" })
        foreach (var speed in Enum.GetValues<InferenceSpeed>())
        foreach (var run in new[] { false, true }) yield return new object[] { provider, speed, run };
    }

    // These paid calls use synthetic prompts only. Account/tier failures and server downgrades
    // deliberately fail the Fast case; they must never be mistaken for successful Fast validation.
    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task RealProvider_AcceptsMode_AndReportsActualProcessing(string provider, InferenceSpeed speed, bool useRun)
    {
        var secret = provider switch
        {
            "Anthropic" => "momedit-antropic-secret", "OpenAI" => "momedit-openai-secret",
            "Google" => "gemini-secret", "xAI" => "xai-secret", _ => throw new ArgumentException(provider)
        };
        var key = await LiveTestSecrets.GetAsync(secret);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        AIService service = provider switch
        {
            "Anthropic" => new AnthropicService(key, AIModels.Anthropic.ClaudeOpus5_5, http),
            "OpenAI" => new OpenAIService(key, AIModels.OpenAI.Gpt6Astra, http),
            "Google" => new GoogleAIService(key, AIModels.Google.Gemini3_8Flash, http),
            "xAI" => new XAIService(key, AIModels.xAI.Grok4_6, http), _ => throw new ArgumentException(provider)
        };
        service.MaxTokens = 1024;
        service.DefaultPolicy.TimeoutSeconds = 150;
        service.DefaultPolicy.MaxRounds = 2;
        var request = service.CreateRequest("Reply with exactly SPEED_OK.").WithSpeed(speed).WithStatelessMode();
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().GetSpeedSupport(speed));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string text;
        IReadOnlyList<AIProcessingInfo> observations;
        try
        {
            if (useRun)
            {
                await using var run = await request.StartRunAsync(cancellationToken: timeout.Token);
                var result = await run.Result.WaitAsync(timeout.Token);
                text = result.Text;
                observations = result.Processing;
            }
            else
            {
                text = await request.GetCompletionAsync(timeout.Token);
                observations = service.LastProcessing;
            }
        }
        catch (Exception error) when (provider == "Anthropic")
        {
            // Exception.ToString() omits ErrorDetails, which can distinguish a zero Fast
            // quota from temporary throttling. Keep the failure and original stack intact.
            Console.WriteLine($"LIVE_SPEED_FAILED provider={provider} path={(useRun ? "run" : "completion")} requested={speed} exception={error.GetType().Name}");
            for (Exception? current = error; current != null; current = current.InnerException)
            {
                if (current is AIServiceException serviceError && !string.IsNullOrWhiteSpace(serviceError.ErrorDetails))
                {
                    // Only the synthetic request's provider error is logged, never request headers.
                    var details = serviceError.ErrorDetails.Replace(key, "[REDACTED_API_KEY]", StringComparison.Ordinal);
                    Console.WriteLine($"LIVE_SPEED_PROVIDER_ERROR exception={current.GetType().Name} details={details}");
                }
            }
            throw;
        }
        StringAssert.Contains(text, "SPEED_OK");
        Assert.IsTrue(observations.Count > 0, "No transport processing observation was recorded.");
        foreach (var item in observations)
        {
            Console.WriteLine($"LIVE_SPEED provider={provider} path={(useRun ? "run" : "completion")} requested={speed} index={item.RequestIndex} actual={item.AppliedSpeed?.ToString() ?? "unknown"} raw={item.RawAppliedMode ?? "missing"}");
            Assert.AreEqual(speed, item.RequestedSpeed);
            // Claude's non-beta response may omit usage.speed. Omission must remain unknown,
            // and cannot prove either Standard or Fast. Explicit modes still require confirmation.
            if (provider == "Anthropic" && speed == InferenceSpeed.ProviderDefault && item.RawAppliedMode == null)
            {
                Assert.IsNull(item.AppliedSpeed);
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.ResponseId));
                continue;
            }
            Assert.IsNotNull(item.AppliedSpeed, "The real server must report a recognized actual mode to verify this contract.");
            if (speed != InferenceSpeed.ProviderDefault) Assert.AreEqual(speed, item.AppliedSpeed.Value,
                "A server downgrade is correctly observable, but does not prove successful Fast execution.");
        }
    }
}
