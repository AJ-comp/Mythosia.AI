using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.Perplexity;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CapabilityReauditCoreTests
{
    [TestMethod]
    public void QueryDoesNotRunProfileOutputBudgetReservation()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new GoogleAIService("offline", "gemini-2.5-pro", client);
        var originalBudget = service.MaxTokens;
        var request = service.CreateRequest("inspect only").WithProfile(new AIRequestProfile
        {
            Purpose = AIRequestPurpose.Summarization,
            DisableReasoning = true,
            MaxTokens = uint.MaxValue
        });

        // Inspection should describe support even if execution will reject the requested budget.
        // Execution's hidden-thinking reservation would overflow this value if called here.
        var capabilities = request.GetCapabilities();
        Assert.AreEqual("gemini-2.5-pro", capabilities.Model);
        Assert.AreEqual(originalBudget, service.MaxTokens);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public void QueryDoesNotInvokeCustomExecutionProfileHooks()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProfileProbe(client);
        var request = service.CreateRequest("inspect only").WithProfile(new AIRequestProfile
        {
            Purpose = AIRequestPurpose.Summarization,
            DisableReasoning = true,
            Temperature = 0.2f
        });

        var capabilities = request.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Temperature);
        Assert.AreEqual(0, service.ExecutionProfileCalls,
            "A custom execution hook may prepare work or invoke application callbacks and is not an inspection hook.");
        Assert.AreEqual(0, service.ProviderExecutionProfileCalls);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.NativeReasoning,
            "The dedicated inspection hook still applies native profile flags within its local scope.");
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().NativeReasoning);
    }

    [TestMethod]
    public void ServiceQueryDoesNotSerializeFunctionDefaults()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProfileProbe(client);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "unused",
            Parameters = new FunctionParameters
            {
                Properties = new Dictionary<string, ParameterProperty>
                {
                    ["value"] = new ParameterProperty { Type = "object", Default = new ThrowingDefault() }
                }
            }
        });

        Assert.AreEqual("base-model", service.GetCapabilities().Model);
        Assert.AreEqual(1, service.Functions.Count);
    }

    [TestMethod]
    public void ServiceQueryDoesNotCaptureExecutionSettings()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProfileProbe(client) { ThrowOnSettingsCapture = true };
        Assert.AreEqual("base-model", service.GetCapabilities().Model);
    }

    [TestMethod]
    public void PerplexityServiceQueryDoesNotSerializeCyclicHostedToolParameters()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new PerplexityService("offline", client);
        var parameters = new Dictionary<string, object>();
        parameters["cycle"] = parameters;
        service.AgentOptions.Tools.Add(new PerplexityHostedTool { Type = "web_search", Parameters = parameters });

        Assert.AreEqual(AIModels.Perplexity.Sonar, service.GetCapabilities().Model);
        Assert.AreSame(parameters, service.AgentOptions.Tools.Single().Parameters);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public void NestedSuccessfulQueryRestoresEnclosingRequestState()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProfileProbe(client);
        var request = service.CreateRequest("inspect only").WithTemperature(0.8f)
            .WithReasoning(ReasoningLevel.Low)
            .WithProfile(new AIRequestProfile { Purpose = AIRequestPurpose.Summarization });

        service.InEnclosingRequest(() =>
        {
            var capabilities = request.GetCapabilities();
            Assert.AreEqual("base-model", capabilities.Model);
            Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Temperature);
            Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Reasoning);
            Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Steering);
        });

        Assert.AreEqual("outer-model", service.ModelAfterInspection);
        Assert.AreEqual(0.3f, service.TemperatureAfterInspection);
        Assert.AreEqual(ReasoningLevel.High, service.ReasoningAfterInspection);
        Assert.AreEqual("outer-native", service.ProviderOptionsAfterInspection);
        Assert.AreEqual("base-model", service.GetCapabilities().Model);
    }

    [TestMethod]
    public void NestedFailedQueryRestoresEnclosingRequestState()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProfileProbe(client);
        var request = service.CreateRequest("inspect only").WithTemperature(0.8f)
            .WithProfile(new AIRequestProfile { Purpose = AIRequestPurpose.Summarization });

        service.InEnclosingRequest(() =>
        {
            service.ThrowOnResolve = true;
            Assert.Throws<InvalidOperationException>(() => request.GetCapabilities());
            service.ThrowOnResolve = false;
        });

        Assert.AreEqual("outer-model", service.ModelAfterInspection);
        Assert.AreEqual(0.3f, service.TemperatureAfterInspection);
        Assert.AreEqual(ReasoningLevel.High, service.ReasoningAfterInspection);
        Assert.AreEqual("outer-native", service.ProviderOptionsAfterInspection);
        Assert.AreEqual("base-model", service.GetCapabilities().Model);
    }

    [TestMethod]
    public void FailedInspectionProfileRestoresEnclosingRequestState()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProfileProbe(client);
        var request = service.CreateRequest("inspect only").WithTemperature(0.8f)
            .WithProfile(new AIRequestProfile { Purpose = AIRequestPurpose.Summarization, DisableReasoning = true });

        service.InEnclosingRequest(() =>
        {
            service.ThrowOnInspectionProfile = true;
            Assert.Throws<InvalidOperationException>(() => request.GetCapabilities());
            service.ThrowOnInspectionProfile = false;
        });

        Assert.AreEqual("outer-model", service.ModelAfterInspection);
        Assert.AreEqual(0.3f, service.TemperatureAfterInspection);
        Assert.AreEqual(ReasoningLevel.High, service.ReasoningAfterInspection);
        Assert.AreEqual("outer-native", service.ProviderOptionsAfterInspection);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().NativeReasoning);
    }

    private sealed class ThrowingDefault
    {
        public string Value => throw new AssertFailedException("Capability queries must not invoke arbitrary parameter default getters.");
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("Capability inspection must not send HTTP.");
    }

    private sealed class ProfileProbe : AIService
    {
        public ProfileProbe(HttpClient client) : base("offline", "https://localhost/", client)
        {
            Model = "base-model";
            AddNewChat();
        }

        public override string Provider => "Custom";
        public int ExecutionProfileCalls { get; private set; }
        public int ProviderExecutionProfileCalls { get; private set; }
        public bool ThrowOnResolve { get; set; }
        public bool ThrowOnSettingsCapture { get; set; }
        public bool ThrowOnInspectionProfile { get; set; }
        public string? ModelAfterInspection { get; private set; }
        public float TemperatureAfterInspection { get; private set; }
        public ReasoningLevel? ReasoningAfterInspection { get; private set; }
        public object? ProviderOptionsAfterInspection { get; private set; }

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            ExecutionProfileCalls++;
            return base.ApplyRequestProfile(profile);
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ProviderExecutionProfileCalls++;
            return base.ApplyProviderSpecificRequestProfile(profile);
        }

        protected override object? CaptureProviderRequestOptions(Message message) => "outer-native";
        protected override void ValidateRequestFeatures(AIRequestFeatures features) { }

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            if (ThrowOnSettingsCapture) throw new AssertFailedException("Inspection must not run execution snapshot capture.");
            base.CaptureRequestSettings(settings);
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
        {
            if (profile.DisableReasoning == true) SetExecutionSetting("Probe.NativeReasoningDisabled", true);
            if (ThrowOnInspectionProfile) throw new InvalidOperationException("inspection profile failure");
        }

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            if (ThrowOnResolve) throw new InvalidOperationException("probe resolution failure");
            return new AIModelCapabilities(provider: Provider, model: RequestModel,
                temperature: RequestTemperature < 0.5f ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                reasoning: CurrentRequestFeatures.Reasoning != null ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                nativeReasoning: RequestSetting("Probe.NativeReasoningDisabled", false)
                    ? CapabilitySupport.Unsupported : CapabilitySupport.Supported,
                steering: CurrentProviderRequestOptions == null ? CapabilitySupport.Unsupported : CapabilitySupport.Supported);
        }

        public void InEnclosingRequest(Action inspect)
        {
            ConfigureRequestFeatures(new AIRequestFeatures
            {
                Reasoning = new ReasoningOptions { Level = ReasoningLevel.High }
            });
            using var scope = BeginRequestFeaturesScope(new Message(ActorRole.User, "outer request"));
            SetExecutionSetting(nameof(Model), "outer-model");
            SetExecutionSetting(nameof(Temperature), 0.3f);
            inspect();
            ModelAfterInspection = RequestModel;
            TemperatureAfterInspection = RequestTemperature;
            ReasoningAfterInspection = CurrentRequestFeatures.Reasoning?.Level;
            ProviderOptionsAfterInspection = CurrentProviderRequestOptions;
        }

        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult("plain");
        public override Task StreamCompletionAsync(Message message, Func<string, Task> callback) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No request expected");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No request expected");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new());
        protected override string ExtractResponseContent(string response) => response;
        protected override string StreamParseJson(string response) => response;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }
}
