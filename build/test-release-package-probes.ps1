# Invoked inside test-nuget-packages.ps1's isolated consumer scope. No hosted services.
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
