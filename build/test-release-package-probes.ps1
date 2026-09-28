# Invoked inside test-nuget-packages.ps1's isolated consumer scope. No hosted services.
$servingAbstractionsProgram = @'
using Mythosia.AI.Serving;
var model = new ServerModel("model", installationState: ModelInstallationState.Unknown,
    loadState: ModelLoadState.Unknown);
if (model.MemoryBytes != null || model.IsRemote != null) throw new Exception("Unknown serving observations became known values.");
var labels = new Dictionary<string, string> { ["model"] = "before" };
var metric = new ServerMetric("requests", 2, labels);
labels["model"] = "after";
var samples = new[] { metric };
var snapshot = new ServerMetrics(samples);
samples[0] = new ServerMetric("other", 3);
if (snapshot.Samples[0].Name != "requests" || snapshot.Samples[0].Labels["model"] != "before")
    throw new Exception("Serving metric snapshots did not preserve immutable observations.");
IModelServer server = new LocalServer();
if ((await server.GetInfoAsync()).Runtime != "fixture" ||
    (await server.GetModelsAsync()).Count != 0 ||
    (await server.GetCapabilitiesAsync()).ModelListing != ServingFeatureSupport.Unknown)
    throw new Exception("The independent serving contracts could not be implemented.");
Console.WriteLine("Dependency-free serving contracts and immutable snapshots passed.");
sealed class LocalServer : IModelServer {
    public Uri Endpoint => new("https://fixture.invalid/");
    public Task<ServerInfo> GetInfoAsync(CancellationToken token = default) => Task.FromResult(new ServerInfo("fixture", Endpoint));
    public Task<ServerHealth> GetHealthAsync(CancellationToken token = default) => Task.FromResult(new ServerHealth(ServerHealthStatus.Unexpected));
    public Task<IReadOnlyList<ServerModel>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ServerModel>>(Array.Empty<ServerModel>());
    public Task<ServingCapabilities> GetCapabilitiesAsync(CancellationToken token = default) => Task.FromResult(new ServingCapabilities());
}
'@
Invoke-PackageConsumer -Name "ServingAbstractionsConsumer" -PackageId "Mythosia.AI.Serving.Abstractions" `
    -Version $versions['Mythosia.AI.Serving.Abstractions'] -Program $servingAbstractionsProgram `
    -ExpectedLibraries @("Mythosia.AI.Serving.Abstractions/$($versions['Mythosia.AI.Serving.Abstractions'])") `
    -RequireExactLibraries

$ollamaProgram = @'
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;
using System.Net;
using System.Text.Json;
using var handler = new LocalOllamaHandler();
using var http = new HttpClient(handler);
var concrete = new OllamaServer("https://ollama.invalid/proxy/v1", http, "fixture-key");
IModelServer server = concrete;
if ((await server.GetInfoAsync()).Runtime != "ollama" || (await server.GetHealthAsync()).Status != ServerHealthStatus.Healthy)
    throw new Exception("Packaged Ollama management discovery failed.");
var models = await server.GetModelsAsync();
if (models.Count != 2 || models[0].InstallationState != ModelInstallationState.Installed ||
    models[0].LoadState != ModelLoadState.Loaded || models[1].IsRemote != true || models[1].LoadState != ModelLoadState.Unknown)
    throw new Exception("Packaged Ollama inventory mixed installed, loaded or remote state.");
if ((await server.GetCapabilitiesAsync()).ModelListing != ServingFeatureSupport.Supported || handler.Commands.Count != 0)
    throw new Exception("Ollama capability inspection performed a mutation or lost supported listing.");
IModelLifecycle lifecycle = concrete;
await lifecycle.LoadModelAsync("model:latest");
await lifecycle.UnloadModelAsync("model:latest");
IModelDownloader downloader = concrete;
await downloader.DownloadModelAsync("model:latest");
if (string.Join(",", handler.Commands) != "load,unload,pull" || http.DefaultRequestHeaders.Authorization != null)
    throw new Exception("Ollama lifecycle requests or shared HTTP authentication were changed.");
using var canceled = new CancellationTokenSource();
canceled.Cancel();
try { await server.GetModelsAsync(canceled.Token); throw new Exception("Cancellation was ignored."); }
catch (OperationCanceledException) { }
Console.WriteLine("Packaged Ollama common contracts, model state and explicit management operations passed.");
sealed class LocalOllamaHandler : HttpMessageHandler {
    public List<string> Commands { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if (request.RequestUri?.Host != "ollama.invalid" || request.Headers.Authorization?.Parameter != "fixture-key")
            throw new Exception("Ollama request routing or authentication was lost.");
        string body;
        if (request.Method == HttpMethod.Get) body = request.RequestUri.AbsolutePath switch {
            "/proxy/api/version" => "{\"version\":\"fixture\"}",
            "/proxy/api/tags" => "{\"models\":[{\"model\":\"model:latest\"},{\"model\":\"remote\",\"remote_model\":\"upstream\"}]}",
            "/proxy/api/ps" => "{\"models\":[{\"model\":\"model:latest\",\"size_vram\":100}]}",
            _ => throw new Exception("Unexpected Ollama discovery route.")
        };
        else {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (json.RootElement.GetProperty("model").GetString() != "model:latest") throw new Exception("Ollama model ID was lost.");
            if (request.RequestUri.AbsolutePath == "/proxy/api/generate") {
                if (json.RootElement.GetProperty("prompt").GetString() != "" || json.RootElement.GetProperty("stream").GetBoolean())
                    throw new Exception("Lifecycle requests must not generate a prompt.");
                var unload = json.RootElement.TryGetProperty("keep_alive", out var keepAlive);
                if (unload && keepAlive.GetInt32() != 0) throw new Exception("Incorrect unload lifetime.");
                Commands.Add(unload ? "unload" : "load");
                body = "{\"model\":\"model:latest\",\"done\":true}";
            } else if (request.RequestUri.AbsolutePath == "/proxy/api/pull") {
                Commands.Add("pull");
                body = "{\"status\":\"pulling manifest\"}\n{\"status\":\"success\"}\n";
            } else throw new Exception("Unexpected Ollama mutation route.");
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
'@
Invoke-PackageConsumer -Name "OllamaConsumer" -PackageId "Mythosia.AI.Serving.Ollama" `
    -Version $versions['Mythosia.AI.Serving.Ollama'] -Program $ollamaProgram `
    -ExpectedLibraries @("Mythosia.AI.Serving.Ollama/$($versions['Mythosia.AI.Serving.Ollama'])", "Mythosia.AI.Serving.Abstractions/$($versions['Mythosia.AI.Serving.Abstractions'])", "Newtonsoft.Json/13.0.4") `
    -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*") `
    -RequireExactLibraries

$llamaCppProgram = @'
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.LlamaCpp;
using System.Net;
using System.Text;
using System.Text.Json;
using var handler = new LocalLlamaHandler();
using var http = new HttpClient(handler);
var concrete = new LlamaCppServer("https://llama.invalid/v1", http, "fixture-key");
IModelServer server = concrete;
if ((await server.GetInfoAsync()).Mode != ServerMode.Router || (await server.GetHealthAsync()).Status != ServerHealthStatus.Healthy)
    throw new Exception("Packaged llama.cpp router discovery failed.");
var models = await server.GetModelsAsync();
if (models.Count != 1 || models[0].Id != "model" || models[0].LoadState != ModelLoadState.Unloaded ||
    models[0].InstallationState != ModelInstallationState.Installed)
    throw new Exception("Packaged llama.cpp model state was not retained.");
if ((await server.GetCapabilitiesAsync()).ModelLoading != ServingFeatureSupport.Supported || handler.Commands.Count != 0)
    throw new Exception("llama.cpp capability inspection performed a mutation or missed router lifecycle support.");
IModelLifecycle lifecycle = concrete;
await lifecycle.LoadModelAsync("model");
await lifecycle.UnloadModelAsync("model");
IModelDownloader downloader = concrete;
await downloader.DownloadModelAsync("model");
if (string.Join(",", handler.Commands) != "load,unload,download" || http.DefaultRequestHeaders.Authorization != null)
    throw new Exception("Packaged llama.cpp explicit lifecycle or authentication changed.");
if ((await concrete.GetMetricsAsync("model")).Samples.Count != 1 || !handler.SafeMetricsObserved)
    throw new Exception("Packaged router metrics must retain autoload=false.");
using var canceled = new CancellationTokenSource();
canceled.Cancel();
try { await server.GetHealthAsync(canceled.Token); throw new Exception("Cancellation was ignored."); }
catch (OperationCanceledException) { }
Console.WriteLine("Packaged llama.cpp common contracts, router lifecycle, download and non-loading metrics passed.");
sealed class LocalLlamaHandler : HttpMessageHandler {
    public List<string> Commands { get; } = new();
    public bool SafeMetricsObserved { get; private set; }
    private bool subscribed;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if (request.RequestUri?.Host != "llama.invalid" || request.Headers.Authorization?.Parameter != "fixture-key")
            throw new Exception("llama.cpp routing or authentication was lost.");
        var mediaType = "application/json";
        string body;
        var path = request.RequestUri.AbsolutePath;
        if (request.Method == HttpMethod.Get) {
            body = path switch {
                "/props" => "{\"role\":\"router\",\"build_info\":\"fixture\"}",
                "/health" => "{\"status\":\"ok\"}",
                "/v1/models" => "{\"data\":[{\"id\":\"model\",\"source\":\"cache\",\"status\":{\"value\":\"unloaded\"}}]}",
                "/models/sse" => "data: {\"model\":\"another\",\"event\":\"download_finished\"}\n\ndata: {\"model\":\"model\",\"event\":\"download_finished\"}\n\n",
                "/metrics" => "llamacpp:requests_processing 1\n",
                _ => throw new Exception("Unexpected llama.cpp discovery route.")
            };
            if (path == "/models/sse") { mediaType = "text/event-stream"; subscribed = true; }
            if (path == "/metrics") SafeMetricsObserved = request.RequestUri.Query == "?model=model&autoload=false";
        } else {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (json.RootElement.GetProperty("model").GetString() != "model") throw new Exception("llama.cpp model ID was lost.");
            if (path == "/models" && !subscribed) throw new Exception("Downloads require an event subscription first.");
            Commands.Add(path switch { "/models/load" => "load", "/models/unload" => "unload", "/models" => "download", _ => throw new Exception("Unexpected command.") });
            body = "{\"success\":true}";
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
    }
}
'@
Invoke-PackageConsumer -Name "LlamaCppConsumer" -PackageId "Mythosia.AI.Serving.LlamaCpp" `
    -Version $versions['Mythosia.AI.Serving.LlamaCpp'] -Program $llamaCppProgram `
    -ExpectedLibraries @("Mythosia.AI.Serving.LlamaCpp/$($versions['Mythosia.AI.Serving.LlamaCpp'])", "Mythosia.AI.Serving.Abstractions/$($versions['Mythosia.AI.Serving.Abstractions'])", "Newtonsoft.Json/13.0.4") `
    -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*") `
    -RequireExactLibraries

$servingNetStandardTemplate = @'
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.__RUNTIME__;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public static class ServingPublicApiProbe {
    public static __RUNTIME__Server Create(HttpClient client) => new __RUNTIME__Server("https://fixture.invalid/v1", client);
    public static IModelServer Common(__RUNTIME__Server server) => server;
    public static Task<ServerInfo> Info(IModelServer server, CancellationToken token) => server.GetInfoAsync(token);
    public static Task<ServerHealth> Health(IModelServer server, CancellationToken token) => server.GetHealthAsync(token);
    public static Task<IReadOnlyList<ServerModel>> Models(IModelServer server, CancellationToken token) => server.GetModelsAsync(token);
    public static Task<ServingCapabilities> Capabilities(IModelServer server, CancellationToken token) => server.GetCapabilitiesAsync(token);
    public static Task Load(__RUNTIME__Server server, string model, CancellationToken token) => ((IModelLifecycle)server).LoadModelAsync(model, token);
    public static Task Unload(__RUNTIME__Server server, string model, CancellationToken token) => ((IModelLifecycle)server).UnloadModelAsync(model, token);
    public static Task Download(__RUNTIME__Server server, string model, IProgress<ModelDownloadProgress> progress, CancellationToken token) => ((IModelDownloader)server).DownloadModelAsync(model, progress, token);
}
'@
Invoke-PackageConsumer -Name "OllamaNetStandardConsumer" -PackageId "Mythosia.AI.Serving.Ollama" `
    -Version $versions['Mythosia.AI.Serving.Ollama'] -Program $servingNetStandardTemplate.Replace('__RUNTIME__', 'Ollama') `
    -ExpectedLibraries @("Mythosia.AI.Serving.Ollama/$($versions['Mythosia.AI.Serving.Ollama'])", "Mythosia.AI.Serving.Abstractions/$($versions['Mythosia.AI.Serving.Abstractions'])", "Newtonsoft.Json/13.0.4") `
    -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*") `
    -RequireExactLibraries -TargetFramework 'netstandard2.1' -BuildOnly
Invoke-PackageConsumer -Name "LlamaCppNetStandardConsumer" -PackageId "Mythosia.AI.Serving.LlamaCpp" `
    -Version $versions['Mythosia.AI.Serving.LlamaCpp'] -Program $servingNetStandardTemplate.Replace('__RUNTIME__', 'LlamaCpp') `
    -ExpectedLibraries @("Mythosia.AI.Serving.LlamaCpp/$($versions['Mythosia.AI.Serving.LlamaCpp'])", "Mythosia.AI.Serving.Abstractions/$($versions['Mythosia.AI.Serving.Abstractions'])", "Newtonsoft.Json/13.0.4") `
    -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*") `
    -RequireExactLibraries -TargetFramework 'netstandard2.1' -BuildOnly

$loaderPrograms = @{
    'Mythosia.Documents.Office' = @'
using Mythosia.Documents;
using Mythosia.Documents.Elements;
using Mythosia.Documents.Office.Word;
using Mythosia.Documents.Office.Excel;
using Mythosia.Documents.Office.PowerPoint;
IDocumentLoader[] loaders = { new WordDocumentLoader(new Parser()), new ExcelDocumentLoader(new Parser()), new PowerPointDocumentLoader(new Parser()) };
string path = Path.GetTempFileName();
try {
    string relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
    foreach (var loader in loaders) {
        var documents = await loader.LoadAsync(relative);
        if (documents.Count != 1 || documents[0].Source != Path.GetFullPath(path)) throw new Exception("Packaged Office loader lost normalized document identity.");
    }
} finally { File.Delete(path); }
Console.WriteLine("Packaged Word, Excel and PowerPoint identity checks passed.");
sealed class Parser : IDocumentParser {
    public bool CanParse(string source) => true;
    public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default) => Task.FromResult(new DoclingDocument { Name = "package smoke" });
}
'@
    'Mythosia.Documents.Pdf' = @'
using Mythosia.Documents;
using Mythosia.Documents.Elements;
using Mythosia.Documents.Pdf;
string path = Path.GetTempFileName();
try {
    var loader = new PdfDocumentLoader(new Parser());
    var documents = await loader.LoadAsync(Path.GetRelativePath(Environment.CurrentDirectory, path));
    if (documents.Count != 1 || documents[0].Source != Path.GetFullPath(path)) throw new Exception("Packaged PDF loader lost normalized document identity.");
} finally { File.Delete(path); }
Console.WriteLine("Packaged PDF identity check passed.");
sealed class Parser : IDocumentParser {
    public bool CanParse(string source) => true;
    public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default) => Task.FromResult(new DoclingDocument { Name = "package smoke" });
}
'@
    'Mythosia.Documents.Hwp' = @'
using Mythosia.Documents;
using Mythosia.Documents.Elements;
using Mythosia.Documents.Hwp;
string path = Path.GetTempFileName();
try {
    var documents = await new HwpDocumentLoader(new Parser()).LoadAsync(path);
    if (documents.Count != 1 || documents[0].TableSerializer is not SemanticTableSerializer) throw new Exception("Packaged HWP loader lost semantic table serialization.");
} finally { File.Delete(path); }
Console.WriteLine("Packaged HWP loader and dependency checks passed.");
sealed class Parser : IDocumentParser {
    public bool CanParse(string source) => true;
    public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default) => Task.FromResult(new DoclingDocument { Name = "package smoke" });
}
'@
}
foreach ($id in $loaderPrograms.Keys | Sort-Object) {
    $expected = @("$id/$($versions[$id])", 'Mythosia.Documents.Abstractions/1.2.0')
    if ($id -eq 'Mythosia.Documents.Hwp') { $expected += @('HwpLibSharp/1.1.10.5', 'OpenMcdf/3.1.4') }
    Invoke-PackageConsumer -Name ($id.Replace('.', '') + 'Consumer') -PackageId $id -Version $versions[$id] `
        -Program $loaderPrograms[$id] -ExpectedLibraries $expected -UnexpectedLibraries @('Mythosia.AI/*', 'Mythosia.AI.Rag/*')
}

$postgresProgram = @'
using Mythosia.VectorDb;
using Mythosia.VectorDb.Postgres;
using var store = new PostgresStore(new PostgresOptions { ConnectionString = "Host=localhost;Database=package_smoke", Dimension = 2 });
ITextSearchStore text = store;
IConfigurableHybridSearchStore hybrid = store;
if ((await text.TextSearchAsync(" ")).Count != 0) throw new Exception("Empty PostgreSQL text search must stay local.");
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try {
    await hybrid.HybridSearchAsync(new[] { 1f, 0f }, "query", new HybridSearchOptions(), cancellationToken: cancellation.Token);
    throw new Exception("PostgreSQL hybrid search ignored cancellation.");
} catch (OperationCanceledException) { }
Console.WriteLine("Packaged PostgreSQL contracts and local cancellation checks passed.");
'@
Invoke-PackageConsumer -Name 'PostgresConsumer' -PackageId 'Mythosia.VectorDb.Postgres' `
    -Version $versions['Mythosia.VectorDb.Postgres'] -Program $postgresProgram `
    -ExpectedLibraries @("Mythosia.VectorDb.Postgres/$($versions['Mythosia.VectorDb.Postgres'])", "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])", 'Npgsql/10.0.2')

$qdrantProgram = @'
using Mythosia.VectorDb;
using Mythosia.VectorDb.Qdrant;
using var store = new QdrantStore(new QdrantOptions { Host = "localhost", CollectionName = "package_smoke", Dimension = 2 });
ITextSearchStore text = store;
IConfigurableHybridSearchStore hybrid = store;
if ((await text.TextSearchAsync(" ! ")).Count != 0) throw new Exception("Empty Qdrant text analysis must stay local.");
if ((await hybrid.HybridSearchAsync(Array.Empty<float>(), "!", new HybridSearchOptions { VectorWeight = 0 })).Count != 0) throw new Exception("Empty Qdrant sparse retrieval must stay local.");
Console.WriteLine("Packaged Qdrant text and hybrid contracts passed.");
'@
Invoke-PackageConsumer -Name 'QdrantConsumer' -PackageId 'Mythosia.VectorDb.Qdrant' `
    -Version $versions['Mythosia.VectorDb.Qdrant'] -Program $qdrantProgram `
    -ExpectedLibraries @("Mythosia.VectorDb.Qdrant/$($versions['Mythosia.VectorDb.Qdrant'])", "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])", 'Qdrant.Client/1.17.0', 'System.IO.Hashing/10.0.7')

$pineconeProgram = @'
using Mythosia.VectorDb.Pinecone;
using System.Net;
using var http = new HttpClient(new LocalHandler());
using var store = new PineconeStore(new PineconeOptions { IndexHost = "https://pinecone.invalid", ApiKey = "package-smoke" }, http);
if (await store.CountAsync() != 2) throw new Exception("Packaged Pinecone JSON response handling failed.");
Console.WriteLine("Packaged Pinecone local HTTP and JSON checks passed.");
sealed class LocalHandler : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        if (request.RequestUri?.AbsolutePath != "/describe_index_stats") throw new Exception("Unexpected Pinecone request.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"namespaces\":{\"\":{\"vectorCount\":2}}}") });
    }
}
'@
Invoke-PackageConsumer -Name 'PineconeConsumer' -PackageId 'Mythosia.VectorDb.Pinecone' `
    -Version $versions['Mythosia.VectorDb.Pinecone'] -Program $pineconeProgram `
    -ExpectedLibraries @("Mythosia.VectorDb.Pinecone/$($versions['Mythosia.VectorDb.Pinecone'])", "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])", 'System.IO.Hashing/10.0.7')

# .NET 10 supplies System.Text.Json and prunes its package dependency. Verify the
# declared package floor with a netstandard consumer while retaining the runtime probe.
$pineconeNetStandardProgram = @'
using Mythosia.VectorDb;
using Mythosia.VectorDb.Pinecone;
using System.Net.Http;
using System.Threading.Tasks;
namespace PackageSmoke
{
    public static class PineconePublicApiProbe
    {
        public static IVectorStore Create(PineconeOptions options, HttpClient client)
            => new PineconeStore(options, client);
        public static Task<long> CountAsync(PineconeStore store) => store.CountAsync();
    }
}
'@
Invoke-PackageConsumer -Name 'PineconeNetStandardConsumer' -PackageId 'Mythosia.VectorDb.Pinecone' `
    -Version $versions['Mythosia.VectorDb.Pinecone'] -Program $pineconeNetStandardProgram `
    -TargetFramework 'netstandard2.1' -BuildOnly `
    -ExpectedLibraries @("Mythosia.VectorDb.Pinecone/$($versions['Mythosia.VectorDb.Pinecone'])", "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])", 'System.Text.Json/10.0.7', 'System.IO.Hashing/10.0.7')

$pixieProgram = @'
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;
using var encoder = new PixieSparseEncoder(new PixieOptions { IntraOpThreads = 2 });
var vector = await encoder.EncodeQueryAsync("한국어 기술 문서를 검색합니다.");
if (vector.Indices.Count == 0) throw new Exception("No sparse output from the bundled model.");
var store = new PixieInMemoryStore(encoder);
await store.UpsertAsync(new VectorRecord("guide", Array.Empty<float>(), "한국어 기술 문서의 검색 방법입니다."));
var found = await store.TextSearchAsync("기술 문서 검색");
if (found.Count != 1 || found[0].Record.Id != "guide") throw new Exception("Bundled model search failed.");
Console.WriteLine("Packaged PIXIE inference and search passed.");
'@
Invoke-PackageConsumer -Name 'PixieConsumer' -PackageId 'Mythosia.AI.Rag.Search.Pixie' `
    -Version $versions['Mythosia.AI.Rag.Search.Pixie'] -Program $pixieProgram -Publish `
    -ExpectedLibraries @("Mythosia.AI.Rag.Search.Pixie/$($versions['Mythosia.AI.Rag.Search.Pixie'])", "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])", 'Microsoft.ML.OnnxRuntime/1.24.4')

# Prove buildTransitive content reaches an application through an actual NuGet edge.
$wrapperRoot = Join-Path $smokeRoot 'PixieWrapper'
$probeFeed = Join-Path $smokeRoot 'probe-feed'
New-Item -ItemType Directory -Path $wrapperRoot, $probeFeed | Out-Null
$wrapperProject = Join-Path $wrapperRoot 'PixieWrapper.csproj'
$wrapperId = 'Mythosia.Release.PixieConsumerProbe'
$wrapperVersion = '0.0.0'
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><PackageId>$wrapperId</PackageId><Version>$wrapperVersion</Version></PropertyGroup><ItemGroup><PackageReference Include="Mythosia.AI.Rag.Search.Pixie" Version="[$($versions['Mythosia.AI.Rag.Search.Pixie'])]" /></ItemGroup></Project>
"@ | Set-Content -LiteralPath $wrapperProject -Encoding utf8
@'
using Mythosia.AI.Rag.Search.Pixie;
public static class PixiePackageProbe {
    public static async Task RunAsync() {
        using var encoder = new PixieSparseEncoder(new PixieOptions { IntraOpThreads = 2 });
        var vector = await encoder.EncodeQueryAsync("간접 패키지 참조에서도 모델이 실행됩니다.");
        if (vector.Indices.Count == 0) throw new Exception("No transitive model output.");
        Console.WriteLine("Transitive packaged PIXIE model passed.");
    }
}
'@ | Set-Content -LiteralPath (Join-Path $wrapperRoot 'Wrapper.cs') -Encoding utf8
& dotnet restore $wrapperProject --configfile (Join-Path $smokeRoot 'NuGet.config') --packages $globalPackagesFolder --no-cache --force
if ($LASTEXITCODE -ne 0) { throw 'PIXIE transitive wrapper restore failed.' }
& dotnet pack $wrapperProject --configuration Release --no-restore --output $probeFeed
if ($LASTEXITCODE -ne 0) { throw 'PIXIE transitive wrapper packing failed.' }
[xml]$probeConfig = Get-Content -LiteralPath (Join-Path $smokeRoot 'NuGet.config') -Raw
$source = $probeConfig.CreateElement('add')
$source.SetAttribute('key', 'probe'); $source.SetAttribute('value', $probeFeed)
$null = $probeConfig.configuration.packageSources.AppendChild($source)
$mapping = $probeConfig.CreateElement('packageSource'); $mapping.SetAttribute('key', 'probe')
$pattern = $probeConfig.CreateElement('package'); $pattern.SetAttribute('pattern', $wrapperId)
$null = $mapping.AppendChild($pattern)
$null = $probeConfig.configuration.packageSourceMapping.AppendChild($mapping)
$probeConfigPath = Join-Path $smokeRoot 'NuGet.probe.config'
$probeConfig.Save($probeConfigPath)
Invoke-PackageConsumer -Name 'PixieTransitiveConsumer' -PackageId $wrapperId -Version $wrapperVersion `
    -Program 'await PixiePackageProbe.RunAsync();' -Publish -NuGetConfig $probeConfigPath `
    -ExpectedLibraries @("$wrapperId/$wrapperVersion", "Mythosia.AI.Rag.Search.Pixie/$($versions['Mythosia.AI.Rag.Search.Pixie'])", "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])")
