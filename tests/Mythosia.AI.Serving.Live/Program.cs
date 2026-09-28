using System.Text.Json;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.LlamaCpp;
using Mythosia.AI.Serving.Ollama;
using Mythosia.AI.Serving.Vllm;

var runtime = Environment.GetEnvironmentVariable("MYTHOSIA_SERVING_RUNTIME")?.ToLowerInvariant();
var endpoint = Environment.GetEnvironmentVariable("MYTHOSIA_SERVING_ENDPOINT");
var modelId = Environment.GetEnvironmentVariable("MYTHOSIA_SERVING_MODEL");
var configuredInventoryId = Environment.GetEnvironmentVariable("MYTHOSIA_SERVING_INVENTORY_MODEL");
var inventoryId = string.IsNullOrWhiteSpace(configuredInventoryId) ? modelId : configuredInventoryId;
var lifecycle = args.Contains("--lifecycle", StringComparer.Ordinal);
var download = args.Contains("--download", StringComparer.Ordinal);
var modelMetrics = args.Contains("--model-metrics", StringComparer.Ordinal);
if (args.Any(arg => arg != "--lifecycle" && arg != "--download" && arg != "--model-metrics") ||
    runtime is not ("ollama" or "llamacpp" or "vllm") ||
    !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
    (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps) ||
    !string.IsNullOrEmpty(endpointUri.UserInfo) || !string.IsNullOrEmpty(endpointUri.Query) || !string.IsNullOrEmpty(endpointUri.Fragment) ||
    ((lifecycle || download || modelMetrics) && string.IsNullOrWhiteSpace(modelId)) ||
    (!string.IsNullOrWhiteSpace(configuredInventoryId) && !download) ||
    (modelMetrics && (runtime != "llamacpp" || lifecycle || download)))
{
    Console.Error.WriteLine("Set MYTHOSIA_SERVING_RUNTIME (ollama|llamacpp|vllm) and an HTTP(S) MYTHOSIA_SERVING_ENDPOINT without user info, query or fragment. Optional --lifecycle / --download / --model-metrics require MYTHOSIA_SERVING_MODEL. MYTHOSIA_SERVING_INVENTORY_MODEL is only used with --download. --model-metrics is a separate llama.cpp check and cannot be combined with --lifecycle or --download. See README.md.");
    return 2;
}

var timeoutSeconds = 120;
var configuredTimeout = Environment.GetEnvironmentVariable("MYTHOSIA_SERVING_TIMEOUT_SECONDS");
if (configuredTimeout != null && (!int.TryParse(configuredTimeout, out timeoutSeconds) || timeoutSeconds < 1 || timeoutSeconds > 86400))
{
    Console.Error.WriteLine("MYTHOSIA_SERVING_TIMEOUT_SECONDS must be between 1 and 86400.");
    return 2;
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
var passed = new List<string>();
var observations = new Dictionary<string, object?>();
var failure = (string?)null;
var exitCode = 1;
try
{
    var apiKey = Environment.GetEnvironmentVariable("MYTHOSIA_SERVING_API_KEY");
    IModelServer server = runtime switch
    {
        "ollama" => new OllamaServer(endpoint, http, apiKey),
        "llamacpp" => new LlamaCppServer(endpoint, http, apiKey),
        _ => new VllmServer(endpoint, http, apiKey)
    };
    var ct = cancellation.Token;
    var health = await server.GetHealthAsync(ct);
    observations["health"] = health.Status.ToString();
    Require(health.Status == ServerHealthStatus.Healthy, "The server is not healthy.");
    passed.Add("health");
    var info = await server.GetInfoAsync(ct);
    observations["mode"] = info.Mode.ToString();
    passed.Add("runtime-info");
    var capabilities = await server.GetCapabilitiesAsync(ct);
    observations["capabilities"] = new
    {
        models = capabilities.ModelListing.ToString(),
        load = capabilities.ModelLoading.ToString(),
        unload = capabilities.ModelUnloading.ToString(),
        download = capabilities.ModelDownloading.ToString(),
        metrics = capabilities.Metrics.ToString()
    };
    passed.Add("read-only-capability-probe");
    var models = await server.GetModelsAsync(ct);
    observations["modelCount"] = models.Count;
    passed.Add("model-inventory");

    if (download)
    {
        Require(server is IModelDownloader, "The client has no download implementation.");
        // Unknown support still permits an explicit requested operation; discovery never runs it.
        Require(capabilities.ModelDownloading != ServingFeatureSupport.Unsupported, "Downloads are unsupported by this server.");
        var progress = new DownloadProgress();
        await ((IModelDownloader)server).DownloadModelAsync(modelId!, progress, ct);
        observations["downloadProgressEvents"] = progress.Count;
        passed.Add("download-terminal-success");
        models = await server.GetModelsAsync(ct);
    }

    if (!string.IsNullOrWhiteSpace(modelId))
    {
        Require(models.Any(model => model.Id == inventoryId), "The selected model is not in the inventory; use the exact reported ID. If a download ID is normalized by the server, supply MYTHOSIA_SERVING_INVENTORY_MODEL / -InventoryModel.");
        passed.Add("selected-model-present");
    }

    if (lifecycle)
    {
        Require(server is IModelLifecycle, "The client has no lifecycle implementation.");
        Require(capabilities.ModelLoading == ServingFeatureSupport.Supported &&
            capabilities.ModelUnloading == ServingFeatureSupport.Supported, "Lifecycle support has not been established.");
        Require(models.Single(model => model.Id == inventoryId).LoadState == ModelLoadState.Unloaded,
            "Lifecycle verification requires a dedicated, initially unloaded model.");
        var management = (IModelLifecycle)server;
        try
        {
            await management.LoadModelAsync(inventoryId!, ct);
            await WaitForStateAsync(server, inventoryId!, ModelLoadState.Loaded, ct);
            passed.Add("load-and-observe");
        }
        finally
        {
            // Explicit lifecycle mode authorizes cleanup even when load/observation fails.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await management.UnloadModelAsync(inventoryId!, cleanup.Token);
            await WaitForStateAsync(server, inventoryId!, ModelLoadState.Unloaded, cleanup.Token);
        }
        passed.Add("unload-and-observe");
    }

    if (capabilities.Metrics == ServingFeatureSupport.Supported && server is IModelMetricsProvider metrics)
    {
        var result = await metrics.GetMetricsAsync(ct);
        observations["metricSampleCount"] = result.Samples.Count;
        passed.Add("server-metrics");
    }
    else observations["metricsVerification"] = "not-run: unsupported or inconclusive capability";

    if (modelMetrics)
    {
        // Requires an already loaded model. This call explicitly disables router auto-loading.
        Require(info.Mode == ServerMode.Router, "Model-specific metrics require llama.cpp Router mode.");
        Require(models.Single(model => model.Id == inventoryId).LoadState == ModelLoadState.Loaded,
            "Model-specific metrics verification requires an already loaded model; sleeping or unknown state is not accepted.");
        var result = await ((LlamaCppServer)server).GetMetricsAsync(inventoryId!, ct);
        observations["metricSampleCount"] = result.Samples.Count;
        passed.Add("model-metrics-without-autoload");
    }
    exitCode = 0;
}
catch (VerificationFailure ex)
{
    // Only this runner creates these errors, using fixed diagnostic strings above.
    failure = ex.Message;
}
catch (OperationCanceledException)
{
    failure = "Cancelled or overall deadline exceeded; remote operations may still be running.";
}
catch (ServingException ex)
{
    // Legacy vLLM errors can retain the response body. Deliberately never print the exception.
    failure = $"Management request failed ({ex.FailureKind}; HTTP {ex.StatusCode?.ToString() ?? "n/a"}).";
}
catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
{
    failure = "Configuration, server state or an explicitly requested verification was not satisfied. Check the runtime and test model setup.";
}
catch (Exception)
{
    failure = "Unexpected client or transport failure. No credential, endpoint or response body was written to the report.";
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    status = exitCode == 0 ? "passed" : "failed",
    runtime,
    verificationMode = "live-server",
    healthyServerObserved = passed.Contains("health"),
    completedUtc = DateTimeOffset.UtcNow,
    passed,
    observations,
    failure
}, new JsonSerializerOptions { WriteIndented = true }));
return exitCode;

static void Require(bool condition, string message)
{
    if (!condition) throw new VerificationFailure(message);
}

static async Task WaitForStateAsync(IModelServer server, string modelId, ModelLoadState expected, CancellationToken cancellationToken)
{
    while (true)
    {
        var model = (await server.GetModelsAsync(cancellationToken)).SingleOrDefault(model => model.Id == modelId);
        if (model?.LoadState == expected) return;
        if (model?.LoadState == ModelLoadState.Failed) throw new VerificationFailure("The model entered a failed state.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
    }
}

sealed class DownloadProgress : IProgress<ModelDownloadProgress>
{
    public int Count { get; private set; }
    public void Report(ModelDownloadProgress value) => Count++;
}

sealed class VerificationFailure(string message) : Exception(message);
