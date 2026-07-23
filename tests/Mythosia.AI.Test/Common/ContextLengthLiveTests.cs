using System.Text;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using Mythosia.AI.Models;
using Mythosia.Azure;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// LIVE: sends a prompt that overruns the model's context window and confirms the provider's
/// real 2026 rejection is still recognised as <see cref="ContextLengthExceededException"/> rather
/// than a plain <see cref="AIServiceException"/>. This is the one thing the offline translation
/// tests cannot cover — that the wording our regexes key off of is the wording the API still emits.
///
/// Requires the mythosia-key-vault secrets (Azure login on the right tenant). Not run by the
/// default Unit filter. Recovery is switched off so the exception surfaces instead of being
/// absorbed by a compaction attempt.
/// </summary>
[TestClass]
public class ContextLengthLiveTests
{
    private const string Vault = "https://mythosia-key-vault.vault.azure.net/";

    private static async Task<string> KeyAsync(string secretName)
        => await new SecretFetcher(Vault, secretName).GetKeyValueAsync();

    /// <summary>Roughly <paramref name="approxTokens"/> tokens of filler (~4 chars/token).</summary>
    private static string Filler(int approxTokens)
    {
        var sb = new StringBuilder(approxTokens * 5);
        for (int i = 0; i < approxTokens; i++)
            sb.Append("word ");
        return sb.ToString();
    }

    /// <summary>Appends a line to CTXLEN_LOG when set, so a passing run's evidence survives MTP.</summary>
    private static void Record(string line)
    {
        var path = Environment.GetEnvironmentVariable("CTXLEN_LOG");
        if (!string.IsNullOrEmpty(path))
            lock (typeof(ContextLengthLiveTests))
                File.AppendAllText(path, line + Environment.NewLine);
    }

    private static async Task AssertOverflowDetected(AIService service, int approxTokens, string label)
    {
        // No policy + recovery off: the rejection must propagate untouched.
        service.ConversationPolicy = null;
        service.ContextRecoveryMaxRetries = 0;

        var prompt = "Summarize the following text.\n" + Filler(approxTokens);

        try
        {
            var answer = await service.GetCompletionAsync(new Message(ActorRole.User, prompt), null, null);

            // The provider accepted a prompt we meant to overrun the window — a bigger context than
            // assumed, or silent truncation. The detector was never reached, so this is inconclusive,
            // not a pass and not a defect.
            Record($"[{label}] INCONCLUSIVE — accepted oversized prompt, window not reached: " +
                   answer.Substring(0, Math.Min(120, answer.Length)));
            Assert.Inconclusive($"[{label}] provider returned a completion for an over-window prompt; " +
                                "context limit could not be provoked on this account/model.");
        }
        catch (ContextLengthExceededException ex)
        {
            // The real body was recognised. This is the assertion that matters.
            Record($"[{label}] OK — status={ex.StatusCode}, " +
                   $"max={ex.MaxContextTokens?.ToString() ?? "null"}, " +
                   $"requested={ex.RequestedTokens?.ToString() ?? "null"}");
            Record($"[{label}] body: {ex.ErrorDetails}");
        }
        catch (AIServiceException ex)
        {
            var body = ex.ErrorDetails ?? "";
            var msg = ex.Message ?? "";

            // A rate/quota rejection (429) is a different failure the detector deliberately ignores —
            // and on a metered account the per-minute token quota can bite before the context window
            // does. Not a wording drift; just can't be provoked here.
            bool rateOrQuota =
                msg.Contains("(429)") ||
                body.Contains("RESOURCE_EXHAUSTED") ||
                body.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0;

            if (rateOrQuota)
            {
                Record($"[{label}] INCONCLUSIVE — hit rate/quota before the window. body: {body}");
                Assert.Inconclusive($"[{label}] provider returned a rate/quota error, not a context " +
                                    "overflow; cannot exercise the detector on this account.");
            }

            // A 400/413 that we failed to type as an overflow IS the drift the offline tests cannot catch.
            Record($"[{label}] MISS — plain AIServiceException, wording drifted. msg: {msg} body: {body}");
            Assert.Fail($"[{label}] got AIServiceException but not ContextLengthExceededException — " +
                        $"the provider's wording no longer matches the detector.\nBody: {body}");
        }
    }

    [TestCategory("Integration")]
    [TestCategory("ContextLengthLive")]
    [TestMethod]
    public async Task OpenAI_RealOverflow_IsDetected()
    {
        var service = new OpenAIService(await KeyAsync("momedit-openai-secret"), new HttpClient());
        service.ChangeModel(AIModels.OpenAI.Gpt4oMini);      // 128K window
        await AssertOverflowDetected(service, approxTokens: 140_000, "OpenAI gpt-4o-mini");
    }

    [TestCategory("Integration")]
    [TestCategory("ContextLengthLive")]
    [TestMethod]
    public async Task xAI_RealOverflow_IsDetected()
    {
        var service = new XAIService(await KeyAsync("xai-secret"), new HttpClient());
        service.ChangeModel(AIModels.xAI.Grok3Mini);
        await AssertOverflowDetected(service, approxTokens: 300_000, "xAI grok-3-mini");
    }

    [TestCategory("Integration")]
    [TestCategory("ContextLengthLive")]
    [TestMethod]
    public async Task Anthropic_RealOverflow_IsDetected()
    {
        var service = new AnthropicService(await KeyAsync("momedit-antropic-secret"), new HttpClient());
        service.ChangeModel(AIModels.Anthropic.ClaudeHaiku4_5_251001);   // 200K window
        await AssertOverflowDetected(service, approxTokens: 220_000, "Anthropic claude-haiku-4.5");
    }

    [TestCategory("Integration")]
    [TestCategory("ContextLengthLive")]
    [TestMethod]
    public async Task Google_RealOverflow_IsDetected()
    {
        var service = new GoogleAIService(await KeyAsync("gemini-secret"), new HttpClient());
        service.ChangeModel(AIModels.Google.Gemini2_5Flash);            // ~1M window
        await AssertOverflowDetected(service, approxTokens: 1_100_000, "Google gemini-2.5-flash");
    }
}
