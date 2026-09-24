using Mythosia.AI.Models;
using Mythosia.AI.Services.Google;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Live")]
[TestCategory("InferenceSpeedDiagnostics")]
[DoNotParallelize]
public class InferenceSpeedWireDiagnosticLiveTests
{
    // Resolves the official REST guide's service_tier spelling against the protobuf reference's
    // serviceTier spelling. This diagnostic reports actual processing; it does not certify Fast access.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Gemini_CompareRestFieldSpellings(bool snakeCase)
    {
        using var handler = new RestSpellingHandler(snakeCase);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        var service = new GoogleAIService(await LiveTestSecrets.GetAsync("gemini-secret"), AIModels.Google.Gemini3_8Flash, http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var text = await service.CreateRequest("Reply with exactly SPEED_OK.").WithSpeed(InferenceSpeed.Fast)
            .WithMaxTokens(1024).GetCompletionAsync(timeout.Token);
        StringAssert.Contains(text, "SPEED_OK");
        var observation = service.LastProcessing.Single();
        Console.WriteLine($"GEMINI_SPEED_WIRE spelling={(snakeCase ? "service_tier" : "serviceTier")} requested=priority header={handler.ActualHeader ?? "missing"} usage={handler.ActualUsage ?? "missing"} normalized={observation.AppliedSpeed}");
        Assert.IsNotNull(observation.AppliedSpeed);
    }

    private sealed class RestSpellingHandler(bool snakeCase) : DelegatingHandler(new HttpClientHandler())
    {
        public string? ActualHeader;
        public string? ActualUsage;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Assert.AreEqual("priority", body["serviceTier"]!.GetValue<string>());
            if (snakeCase)
            {
                body.Remove("serviceTier");
                body["service_tier"] = "priority";
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            var response = await base.SendAsync(request, cancellationToken);
            ActualHeader = response.Headers.TryGetValues("x-gemini-service-tier", out var values) ? values.FirstOrDefault() : null;
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonNode.Parse(responseText);
            ActualUsage = root?["usageMetadata"]?["serviceTier"]?.GetValue<string>();
            return response;
        }
    }
}
