[CmdletBinding()]
param(
    [string]$ArtifactsDirectory
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repoRoot "artifacts"
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repoRoot $ArtifactsDirectory
}

$artifactsDir = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
$manifestPath = Join-Path $artifactsDir "release-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Release manifest is missing: $manifestPath. Run publish-nuget.ps1 -Mode Pack first."
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ([int]$manifest.schemaVersion -ne 3 -or
    [string]::IsNullOrWhiteSpace([string]$manifest.sourceCommit) -or
    @("clean-release", "development-validation") -notcontains [string]$manifest.provenance) {
    throw "Release manifest schema or sourceCommit is invalid."
}

$gitOutput = @(& git -C $repoRoot rev-parse HEAD 2>$null)
$gitExitCode = $LASTEXITCODE
$currentCommit = ([string]($gitOutput | Select-Object -First 1)).Trim()
if ($gitExitCode -ne 0 -or $currentCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Could not determine the current source commit for package-consumer validation."
}
if (-not $currentCommit.Equals([string]$manifest.sourceCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release artifacts were not built from the current source commit. Repack them before consumer testing."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-PackageMetadata {
    param(
        [string]$PackagePath,
        [string]$ExpectedId,
        [string]$ExpectedVersion,
        [string]$ExpectedSourceCommit
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($requiredEntry in @("README.md", "RELEASE_NOTES.md")) {
            if ($entries -cnotcontains $requiredEntry) {
                throw "$ExpectedId package is missing root $requiredEntry."
            }
        }

        $nuspecEntry = @($archive.Entries | Where-Object {
            $_.FullName -ceq "$ExpectedId.nuspec"
        })
        if ($nuspecEntry.Count -ne 1) {
            throw "$ExpectedId package must contain exactly one root $ExpectedId.nuspec."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        if ([string]$nuspec.package.metadata.id -cne $ExpectedId -or
            [string]$nuspec.package.metadata.version -cne $ExpectedVersion -or
            [string]$nuspec.package.metadata.readme -cne "README.md" -or
            -not ([string]$nuspec.package.metadata.repository.commit).Equals(
                $ExpectedSourceCommit,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$ExpectedId package metadata does not match the release manifest."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$packages = @($manifest.packages)
$expectedIds = @(
    "Mythosia.AI.Abstractions",
    "Mythosia.AI",
    "Mythosia.AI.Providers.Alibaba",
    "Mythosia.VectorDb.Abstractions",
    "Mythosia.AI.Rag.Abstractions",
    "Mythosia.VectorDb.InMemory",
    "Mythosia.AI.Rag",
    "Mythosia.AI.Mcp",
    "Mythosia.AI.Serving.Vllm"
)
if ($packages.Count -ne $expectedIds.Count) {
    throw "Unexpected release manifest package count."
}

$versions = @{}
foreach ($package in $packages) {
    if (-not ($expectedIds -contains [string]$package.id)) {
        throw "Unexpected package in release manifest: $($package.id)"
    }

    $packagePath = Join-Path $artifactsDir ([string]$package.file)
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Package listed in the release manifest is missing: $packagePath"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash.ToLowerInvariant()
    if ($actualHash -ne ([string]$package.sha256).ToLowerInvariant()) {
        throw "Package checksum does not match the release manifest: $packagePath"
    }

    Assert-PackageMetadata `
        -PackagePath $packagePath `
        -ExpectedId ([string]$package.id) `
        -ExpectedVersion ([string]$package.version) `
        -ExpectedSourceCommit ([string]$manifest.sourceCommit)

    if ([string]::IsNullOrWhiteSpace([string]$package.symbolsFile) -or
        [string]::IsNullOrWhiteSpace([string]$package.symbolsSha256)) {
        throw "Release manifest is missing symbol package metadata for $($package.id)."
    }
    $symbolPackagePath = Join-Path $artifactsDir ([string]$package.symbolsFile)
    if (-not (Test-Path -LiteralPath $symbolPackagePath -PathType Leaf)) {
        throw "Symbol package listed in the release manifest is missing: $symbolPackagePath"
    }
    $actualSymbolsHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $symbolPackagePath).Hash.ToLowerInvariant()
    if ($actualSymbolsHash -ne ([string]$package.symbolsSha256).ToLowerInvariant()) {
        throw "Symbol package checksum does not match the release manifest: $symbolPackagePath"
    }

    $versions[[string]$package.id] = [string]$package.version
}

foreach ($expectedId in $expectedIds) {
    if (-not $versions.ContainsKey($expectedId)) {
        throw "Release manifest is missing $expectedId."
    }
}

$smokeName = "mythosia-ai-package-smoke-$([Guid]::NewGuid().ToString('N'))"
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) $smokeName
$globalPackagesFolder = Join-Path $smokeRoot "global-packages"
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
New-Item -ItemType Directory -Path $globalPackagesFolder | Out-Null

function Invoke-PackageConsumer {
    param(
        [string]$Name,
        [string]$PackageId,
        [string]$Version,
        [string]$Program,
        [string[]]$ExpectedLibraries,
        [string[]]$UnexpectedLibraries = @(),
        [string]$TargetFramework = "net10.0",
        [switch]$BuildOnly
    )

    $consumerRoot = Join-Path $smokeRoot $Name
    New-Item -ItemType Directory -Path $consumerRoot | Out-Null

    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>$(if ($BuildOnly) { "Library" } else { "Exe" })</OutputType>
    <TargetFramework>$TargetFramework</TargetFramework>
    <ImplicitUsings>$(if ($TargetFramework -eq "netstandard2.1") { "disable" } else { "enable" })</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$PackageId" Version="[$Version]" />
  </ItemGroup>
</Project>
"@
    $projectPath = Join-Path $consumerRoot "$Name.csproj"
    [System.IO.File]::WriteAllText(
        $projectPath,
        $project,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText(
        (Join-Path $consumerRoot "Program.cs"),
        $Program,
        [System.Text.UTF8Encoding]::new($false))

    & dotnet restore $projectPath `
        --configfile (Join-Path $smokeRoot "NuGet.config") `
        --packages $globalPackagesFolder `
        --no-cache `
        --force
    if ($LASTEXITCODE -ne 0) {
        throw "$Name package consumer restore failed."
    }

    $assetsPath = Join-Path $consumerRoot "obj/project.assets.json"
    $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name)
    foreach ($expectedLibrary in $ExpectedLibraries) {
        if (-not ($libraries -contains $expectedLibrary)) {
            throw "$Name did not resolve expected package $expectedLibrary."
        }
    }
    foreach ($unexpectedLibrary in $UnexpectedLibraries) {
        if (@($libraries | Where-Object { $_ -like $unexpectedLibrary }).Count -gt 0) {
            throw "$Name unexpectedly resolved package $unexpectedLibrary."
        }
    }

    $resolvedPackageFolders = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        [System.IO.Path]::GetFullPath($_.TrimEnd('\', '/')).TrimEnd('\', '/')
    })
    $expectedPackageFolder = [System.IO.Path]::GetFullPath($globalPackagesFolder).TrimEnd('\', '/')
    $pathComparison = if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if ($resolvedPackageFolders.Count -ne 1 -or
        -not $resolvedPackageFolders[0].Equals($expectedPackageFolder, $pathComparison)) {
        throw "$Name did not use the isolated global package folder."
    }

    if ($BuildOnly) {
        & dotnet build `
            $projectPath `
            --configuration Release `
            --no-restore
    }
    else {
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-restore
    }
    if ($LASTEXITCODE -ne 0) {
        throw "$Name package consumer build or execution failed."
    }
}

function Get-ReleasePackageSourceMapping {
    param([string[]]$PackageIds)

    # Pin the complete explicit release set locally, including changed RAG contracts.
    # Unchanged dependencies such as document loaders still resolve from nuget.org.
    return ($PackageIds | ForEach-Object {
        $escapedId = [System.Security.SecurityElement]::Escape($_)
        "      <package pattern=`"$escapedId`" />"
    }) -join [Environment]::NewLine
}

try {
    $escapedArtifactsDir = [System.Security.SecurityElement]::Escape($artifactsDir)
    $escapedGlobalPackagesFolder = [System.Security.SecurityElement]::Escape($globalPackagesFolder)
    $releasePackageSourceMapping = Get-ReleasePackageSourceMapping -PackageIds $expectedIds
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$escapedGlobalPackagesFolder" />
  </config>
  <packageSources>
    <clear />
    <add key="release-artifacts" value="$escapedArtifactsDir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="release-artifacts">
$releasePackageSourceMapping
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $smokeRoot "NuGet.config"),
        $nugetConfig,
        [System.Text.UTF8Encoding]::new($false))

    $abstractionsProgram = @'
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Services;
using System.Threading;
using System.Threading.Tasks;

namespace PackageSmoke
{
    public sealed class ImageGenerationServiceStub : IImageGenerationService
    {
        public string DefaultImageModel => AIModels.OpenAI.GptImage2;

        public Task<ImageGenerationResult> GenerateImagesAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResult
            {
                Model = request.Model ?? DefaultImageModel
            });

        public Task<ImageGenerationResult> EditImagesAsync(
            ImageEditRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResult
            {
                Model = request.Model ?? DefaultImageModel
            });
    }

    public static class PublicApiProbe
    {
        public static bool CompileRepresentativeV8Surface()
        {
            IImageGenerationService imageService = new ImageGenerationServiceStub();
            var policy = new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 2
            };
            var clonedPolicy = policy.Clone();
            var gpt56Models = new[]
            {
                AIModels.OpenAI.Gpt5_6,
                AIModels.OpenAI.Gpt5_6Sol,
                AIModels.OpenAI.Gpt5_6Terra,
                AIModels.OpenAI.Gpt5_6Luna
            };

            var pixels = ImageSize.Pixels(1536, 1024);
            var preset = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo);
            var runResult = new AIRunResult("package result");
            var capabilities = AIModelCapabilities.Unknown;
            Task<ImageGenerationResult> generation = imageService.GenerateImagesAsync(
                new ImageGenerationRequest
                {
                    Prompt = "package smoke test", Size = pixels,
                    Quality = ImageQuality.High, Background = ImageBackground.Transparent,
                    OutputFormat = ImageOutputFormat.Png
                });
            Task<ImageGenerationResult> edit = imageService.EditImagesAsync(
                new ImageEditRequest { Prompt = "package smoke test edit" });

            return clonedPolicy.ExecutionMode == FunctionExecutionMode.Parallel &&
                gpt56Models.Length == 4 &&
                preset.Kind == ImageSizeKind.Preset && runResult.Text == "package result" &&
                capabilities.Streaming == CapabilitySupport.Unknown &&
                generation != null &&
                edit != null;
        }

        public static Task<string> CompileCompletionCancellation(IAIService service, CancellationToken token)
            => service.GetCompletionAsync("package smoke", cancellationToken: token);

        public static Task<AIRunResult> CompileRunResult(AIRun run) => run.Result;
    }
}
'@
    Invoke-PackageConsumer `
        -Name "AbstractionsConsumer" `
        -PackageId "Mythosia.AI.Abstractions" `
        -Version $versions["Mythosia.AI.Abstractions"] `
        -Program $abstractionsProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -UnexpectedLibraries @(
            "Mythosia.AI/$($versions['Mythosia.AI'])") `
        -TargetFramework "netstandard2.1" `
        -BuildOnly

    $coreProgram = @'
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;

var policy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 2
};
var calls = new FunctionCallBatch(new[]
{
    new FunctionCall { Id = "first", Name = "first", Index = 0 },
    new FunctionCall { Id = "second", Name = "second", Index = 1 }
});

if (policy.ExecutionMode != FunctionExecutionMode.Parallel || calls.Calls.Count != 2)
    throw new InvalidOperationException("The transitive abstractions contract is not usable.");
if (typeof(AIService).Assembly.GetName().Name != "Mythosia.AI")
    throw new InvalidOperationException("The packaged core assembly did not load.");

using var http = new HttpClient(new RejectHttpHandler());
var service = new OpenAIService("package-smoke-no-key", http);
var basis = service.CreateRequest("package smoke");
var summary = basis.WithTemperature(0.2f);
var creative = basis.WithTemperature(0.8f);
if (ReferenceEquals(summary, creative) || ReferenceEquals(summary, basis))
    throw new InvalidOperationException("Request configuration branches must be independent.");
if (summary.GetCapabilities().Provider != "OpenAI")
    throw new InvalidOperationException("The packaged request capability API did not resolve the provider.");
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try
{
    await summary.GetCompletionAsync(cancellation.Token);
    throw new InvalidOperationException("An already-cancelled completion must not succeed.");
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
Func<AIRequestBuilder, CancellationToken, Task<AIRun>> start = (request, token)
    => request.StartRunAsync(cancellationToken: token);
Func<AIRun, Task<AIRunResult>> result = run => run.Result;
GC.KeepAlive(new Delegate[] { start, result });

Console.WriteLine("Core-only consumer smoke test passed.");

sealed class RejectHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        => throw new InvalidOperationException("A local package smoke test must not send provider HTTP requests.");
}
'@
    Invoke-PackageConsumer `
        -Name "CoreConsumer" `
        -PackageId "Mythosia.AI" `
        -Version $versions["Mythosia.AI"] `
        -Program $coreProgram `
        -ExpectedLibraries @(
            "Mythosia.AI/$($versions['Mythosia.AI'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])")

    $coreNetStandardProgram = @'
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Base;

namespace PackageSmoke
{
    public static class CorePublicApiProbe
    {
        public static bool CompileRepresentativeV8Surface()
        {
            var policy = new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Sequential,
                MaxConcurrency = 1
            };
            var calls = new FunctionCallBatch(new[]
            {
                new FunctionCall { Id = "first", Name = "first", Index = 0 }
            });

            return policy.ExecutionMode == FunctionExecutionMode.Sequential &&
                calls.Calls.Count == 1 &&
                typeof(AIService).Assembly.GetName().Name == "Mythosia.AI";
        }
    }
}
'@
    Invoke-PackageConsumer `
        -Name "CoreNetStandardConsumer" `
        -PackageId "Mythosia.AI" `
        -Version $versions["Mythosia.AI"] `
        -Program $coreNetStandardProgram `
        -ExpectedLibraries @(
            "Mythosia.AI/$($versions['Mythosia.AI'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -TargetFramework "netstandard2.1" `
        -BuildOnly

    $alibabaProgram = @'
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Base;

if (!typeof(OpenAICompatibleService).IsAssignableFrom(typeof(QwenService)))
    throw new InvalidOperationException("The Alibaba provider did not load against transitive Mythosia.AI.");

Console.WriteLine("Alibaba-only consumer smoke test passed.");
'@
    Invoke-PackageConsumer `
        -Name "AlibabaConsumer" `
        -PackageId "Mythosia.AI.Providers.Alibaba" `
        -Version $versions["Mythosia.AI.Providers.Alibaba"] `
        -Program $alibabaProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Providers.Alibaba/$($versions['Mythosia.AI.Providers.Alibaba'])",
            "Mythosia.AI/$($versions['Mythosia.AI'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])")

    $alibabaNetStandardProgram = @'
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Base;

namespace PackageSmoke
{
    public static class AlibabaPublicApiProbe
    {
        public static bool CompileRepresentativeV3Surface()
            => typeof(OpenAICompatibleService).IsAssignableFrom(typeof(QwenService));
    }
}
'@
    Invoke-PackageConsumer `
        -Name "AlibabaNetStandardConsumer" `
        -PackageId "Mythosia.AI.Providers.Alibaba" `
        -Version $versions["Mythosia.AI.Providers.Alibaba"] `
        -Program $alibabaNetStandardProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Providers.Alibaba/$($versions['Mythosia.AI.Providers.Alibaba'])",
            "Mythosia.AI/$($versions['Mythosia.AI'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -TargetFramework "netstandard2.1" `
        -BuildOnly

    $ragProgram = @'
using Mythosia.AI.Models;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(builder => builder
    .AddText("PACKAGE_SMOKE_SHIPPING takes three days.", id: "package-smoke")
    .UseLocalEmbedding(64)
    .UseInMemoryStore()
    .WithTopK(1));
var result = await store.QueryAsync("PACKAGE_SMOKE_SHIPPING");
if (!result.HasReferences || !result.RequestMessageContent.Contains("PACKAGE_SMOKE_SHIPPING takes three days."))
    throw new InvalidOperationException("The packaged RAG pipeline did not retrieve its local document.");

if (!store.UseKeywordSearch())
    throw new InvalidOperationException("The packaged RAG pipeline did not accept keyword retrieval.");
var keywordResult = await store.QueryAsync("PACKAGE_SMOKE_SHIPPING");
if (!keywordResult.HasReferences || !keywordResult.RequestMessageContent.Contains("PACKAGE_SMOKE_SHIPPING takes three days."))
    throw new InvalidOperationException("The packaged keyword retriever did not retrieve its local document.");
if (!store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.25f, CandidateMultiplier = 2, RrfK = 10 }))
    throw new InvalidOperationException("The packaged RAG pipeline did not accept configurable hybrid retrieval.");
var hybridResult = await store.QueryAsync("PACKAGE_SMOKE_SHIPPING");
if (!hybridResult.HasReferences || !hybridResult.RequestMessageContent.Contains("PACKAGE_SMOKE_SHIPPING takes three days."))
    throw new InvalidOperationException("The packaged hybrid retriever did not retrieve its local document.");

// Compile the new wrapper surface without invoking any hosted service.
Func<RagEnabledService, RagEnabledService> configure = service => service
    .WithReasoning(ReasoningLevel.High, CachePreservation.None)
    .WithWebSearch()
    .WithFileSearch(new FileSearchStore("OpenAI", "vs_package_smoke"));
Func<RagEnabledService, Task<AIRun>> start = service => service.StartRunAsync("package smoke");
Func<RagEnabledService, int> citationCount = service => service.LastCitations.Count;
Func<IRagRetriever, Task<IReadOnlyList<VectorSearchResult>>> retrieve = retriever =>
    retriever.RetrieveAsync(new RagRetrievalRequest("package smoke", topK: 1));
GC.KeepAlive(new Delegate[] { configure, start, citationCount, retrieve });
if (typeof(RagEnabledService).Assembly.GetName().Name != "Mythosia.AI.Rag")
    throw new InvalidOperationException("The packaged RAG assembly did not load.");

Console.WriteLine("RAG-only consumer dense, keyword, hybrid and public API smoke tests passed.");
'@
    Invoke-PackageConsumer `
        -Name "RagConsumer" `
        -PackageId "Mythosia.AI.Rag" `
        -Version $versions["Mythosia.AI.Rag"] `
        -Program $ragProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Rag/$($versions['Mythosia.AI.Rag'])",
            "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])",
            "Mythosia.AI.Rag.Abstractions/$($versions['Mythosia.AI.Rag.Abstractions'])",
            "Mythosia.VectorDb.InMemory/$($versions['Mythosia.VectorDb.InMemory'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Providers.Alibaba/*")

    $ragNetStandardProgram = @'
using Mythosia.AI.Models;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PackageSmoke
{
    public static class RagPublicApiProbe
    {
        public static Task<AIRun> CompileRunAndRequestFeatures(RagEnabledService service)
            => service.WithReasoning(ReasoningLevel.High, CachePreservation.None)
                .WithWebSearch()
                .WithFileSearch(new FileSearchStore("OpenAI", "vs_package_smoke"))
                .StartRunAsync("package smoke");

        public static IReadOnlyList<AICitation> ReadCitations(RagEnabledService service)
            => service.LastCitations;

        public static Task<IReadOnlyList<VectorSearchResult>> CompileRetriever(IRagRetriever retriever)
            => retriever.RetrieveAsync(new RagRetrievalRequest("package smoke", topK: 1));

        public static RagBuilder CompileRetrievalSelection(RagBuilder builder)
            => builder.UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.25f }).UseKeywordSearch();

        public static ITextSearchStore CompileTextStore(InMemoryVectorStore store) => store;

        public static IConfigurableHybridSearchStore CompileHybridStore(InMemoryVectorStore store) => store;
    }
}
'@
    Invoke-PackageConsumer `
        -Name "RagNetStandardConsumer" `
        -PackageId "Mythosia.AI.Rag" `
        -Version $versions["Mythosia.AI.Rag"] `
        -Program $ragNetStandardProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Rag/$($versions['Mythosia.AI.Rag'])",
            "Mythosia.VectorDb.Abstractions/$($versions['Mythosia.VectorDb.Abstractions'])",
            "Mythosia.AI.Rag.Abstractions/$($versions['Mythosia.AI.Rag.Abstractions'])",
            "Mythosia.VectorDb.InMemory/$($versions['Mythosia.VectorDb.InMemory'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Providers.Alibaba/*") `
        -TargetFramework "netstandard2.1" `
        -BuildOnly

    $mcpProgram = @'
using Mythosia.AI.Mcp;
using Mythosia.AI.Mcp.Transports;
using System.Text.Json;
using System.Threading.Channels;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var transport = new LocalMcpTransport();
await using var connection = new McpConnection(transport);
await connection.InitializeAsync(deadline.Token);
var tools = McpToolAdapter.ToFunctionDefinitions(connection);
if (tools.Count != 1 || tools[0].HandlerWithCancellation is null)
    throw new InvalidOperationException("The packaged MCP adapter must expose a cancellable function handler.");
var text = await tools[0].HandlerWithCancellation!(new Dictionary<string, object>(), deadline.Token);
if (text != "package-mcp-ok")
    throw new InvalidOperationException("The packaged MCP tool did not preserve its response text.");
try
{
    await connection.CallToolAsync("fail", cancellationToken: deadline.Token);
    throw new InvalidOperationException("MCP tool errors must raise McpException.");
}
catch (McpException) { }
await connection.DisposeAsync();
if (transport.IsConnected)
    throw new InvalidOperationException("MCP disposal did not close its owned transport.");
try
{
    await connection.CallToolAsync("echo", cancellationToken: deadline.Token);
    throw new InvalidOperationException("A disposed MCP connection must reject new calls.");
}
catch (ObjectDisposedException) { }
Console.WriteLine("MCP-only consumer tool, error and lifecycle smoke test passed.");

sealed class LocalMcpTransport : IMcpTransport
{
    private readonly Channel<string> _responses = Channel.CreateUnbounded<string>();
    public bool IsConnected { get; private set; } = true;

    public Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var requestId)) return Task.CompletedTask;
        var method = root.GetProperty("method").GetString();
        object result = method switch
        {
            "initialize" => new { protocolVersion = "2024-11-05", capabilities = new { }, serverInfo = new { name = "local-package-smoke", version = "1.0" } },
            "tools/list" => new { tools = new[] { new { name = "echo", description = "Local test", inputSchema = new { type = "object", properties = new { } } } } },
            "tools/call" => new { isError = root.GetProperty("params").GetProperty("name").GetString() == "fail", content = new[] { new { type = "text", text = "package-mcp-ok" } } },
            _ => throw new InvalidOperationException("Unexpected local MCP method: " + method)
        };
        if (!_responses.Writer.TryWrite(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId.GetInt32(), result })))
            throw new InvalidOperationException("Local MCP transport was closed.");
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!await _responses.Reader.WaitToReadAsync(cancellationToken)) return null;
        return await _responses.Reader.ReadAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _responses.Writer.TryComplete();
        return default;
    }
}
'@
    Invoke-PackageConsumer `
        -Name "McpConsumer" `
        -PackageId "Mythosia.AI.Mcp" `
        -Version $versions["Mythosia.AI.Mcp"] `
        -Program $mcpProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Mcp/$($versions['Mythosia.AI.Mcp'])",
            "Mythosia.AI/$($versions['Mythosia.AI'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -UnexpectedLibraries @("Mythosia.AI.Rag/*", "Mythosia.AI.Providers.Alibaba/*")

    $mcpNetStandardProgram = @'
using Mythosia.AI.Mcp;
using Mythosia.AI.Models.Functions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PackageSmoke
{
    public static class McpPublicApiProbe
    {
        public static List<FunctionDefinition> CompileToolRegistration(McpConnection connection)
            => McpToolAdapter.ToFunctionDefinitions(connection, namePrefix: "package_");

        public static Task<string> CompileToolCancellation(McpConnection connection, CancellationToken token)
            => connection.CallToolAsync("echo", cancellationToken: token);

        public static ValueTask CompileCleanup(McpConnection connection) => connection.DisposeAsync();
    }
}
'@
    Invoke-PackageConsumer `
        -Name "McpNetStandardConsumer" `
        -PackageId "Mythosia.AI.Mcp" `
        -Version $versions["Mythosia.AI.Mcp"] `
        -Program $mcpNetStandardProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Mcp/$($versions['Mythosia.AI.Mcp'])",
            "Mythosia.AI/$($versions['Mythosia.AI'])",
            "Mythosia.AI.Abstractions/$($versions['Mythosia.AI.Abstractions'])") `
        -UnexpectedLibraries @("Mythosia.AI.Rag/*", "Mythosia.AI.Providers.Alibaba/*") `
        -TargetFramework "netstandard2.1" `
        -BuildOnly

    $vllmProgram = @'
using Mythosia.AI.Serving.Vllm;
using System.Net;

using var handler = new LocalVllmHandler();
using var http = new HttpClient(handler);
var server = new VllmServer("https://vllm.invalid/v1", http, apiKey: "package-smoke-token");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
if (server.Endpoint.AbsoluteUri != "https://vllm.invalid/")
    throw new InvalidOperationException("The packaged vLLM client did not normalize its endpoint.");
var models = await server.GetModelsAsync(deadline.Token);
var model = await server.GetModelAsync("package-model", deadline.Token);
if (models.Count != 1 || model is null || model.DisplayModel != "example/package-model" || model.MaxModelLen != 8192)
    throw new InvalidOperationException("The packaged vLLM client did not parse model cards.");
if (await server.GetVersionAsync(deadline.Token) != "package-test-version")
    throw new InvalidOperationException("The packaged vLLM client did not parse the server version.");
if (!await server.IsHealthyAsync(deadline.Token))
    throw new InvalidOperationException("The packaged vLLM client did not recognize a healthy server.");
handler.HealthStatus = HttpStatusCode.ServiceUnavailable;
if ((await server.GetHealthAsync(deadline.Token)).Status != VllmHealthStatus.EngineDead)
    throw new InvalidOperationException("The packaged vLLM client did not classify an unavailable engine.");
handler.HealthStatus = HttpStatusCode.Unauthorized;
if ((await server.GetHealthAsync(deadline.Token)).Status != VllmHealthStatus.Unauthorized)
    throw new InvalidOperationException("The packaged vLLM client did not classify an authentication failure.");
var metrics = await server.GetMetricsAsync(deadline.Token);
if (metrics.RunningRequests != 2 || metrics.KvCacheUsage != 0.25 ||
    metrics.Families["vllm:num_requests_running"][0].Labels["model_name"] != "package-model")
    throw new InvalidOperationException("The packaged vLLM client did not preserve parsed metrics and labels.");
if (http.DefaultRequestHeaders.Authorization is not null)
    throw new InvalidOperationException("The vLLM client must not mutate shared HTTP default authentication headers.");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await server.GetHealthAsync(cancelled.Token);
    throw new InvalidOperationException("Caller cancellation must not become a health verdict.");
}
catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
Console.WriteLine("Standalone vLLM consumer model, health, metrics and cancellation smoke test passed.");

sealed class LocalVllmHandler : HttpMessageHandler
{
    public HttpStatusCode HealthStatus { get; set; } = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.RequestUri?.Host != "vllm.invalid" || request.Method != HttpMethod.Get ||
            request.Headers.Authorization?.Scheme != "Bearer" ||
            request.Headers.Authorization?.Parameter != "package-smoke-token")
            throw new InvalidOperationException("The local vLLM request did not preserve routing or authentication.");
        var path = request.RequestUri.AbsolutePath;
        var body = path switch
        {
            "/v1/models" => "{\"data\":[{\"id\":\"package-model\",\"root\":\"example/package-model\",\"max_model_len\":8192}]}",
            "/version" => "{\"version\":\"package-test-version\"}",
            "/health" => "",
            "/metrics" => "vllm:num_requests_running{model_name=\"package-model\"} 2\nvllm:kv_cache_usage_perc{model_name=\"package-model\"} 0.25\n",
            _ => throw new InvalidOperationException("Unexpected local vLLM route: " + path)
        };
        return Task.FromResult(new HttpResponseMessage(path == "/health" ? HealthStatus : HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        });
    }
}
'@
    Invoke-PackageConsumer `
        -Name "VllmConsumer" `
        -PackageId "Mythosia.AI.Serving.Vllm" `
        -Version $versions["Mythosia.AI.Serving.Vllm"] `
        -Program $vllmProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Serving.Vllm/$($versions['Mythosia.AI.Serving.Vllm'])",
            "Newtonsoft.Json/13.0.4") `
        -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*")

    $vllmNetStandardProgram = @'
using Mythosia.AI.Serving.Vllm;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PackageSmoke
{
    public static class VllmPublicApiProbe
    {
        public static VllmServer Create(HttpClient httpClient)
            => new VllmServer("https://vllm.invalid/v1", httpClient);

        public static Task<IReadOnlyList<VllmModelCard>> Models(VllmServer server, CancellationToken token)
            => server.GetModelsAsync(token);

        public static Task<VllmModelCard?> Model(VllmServer server, CancellationToken token)
            => server.GetModelAsync("package-model", token);

        public static Task<string?> Version(VllmServer server, CancellationToken token)
            => server.GetVersionAsync(token);

        public static Task<bool> IsHealthy(VllmServer server, CancellationToken token)
            => server.IsHealthyAsync(token);

        public static Task<VllmHealthReport> Health(VllmServer server, CancellationToken token)
            => server.GetHealthAsync(token);

        public static Task<VllmMetrics> Metrics(VllmServer server, CancellationToken token)
            => server.GetMetricsAsync(token);
    }
}
'@
    Invoke-PackageConsumer `
        -Name "VllmNetStandardConsumer" `
        -PackageId "Mythosia.AI.Serving.Vllm" `
        -Version $versions["Mythosia.AI.Serving.Vllm"] `
        -Program $vllmNetStandardProgram `
        -ExpectedLibraries @(
            "Mythosia.AI.Serving.Vllm/$($versions['Mythosia.AI.Serving.Vllm'])",
            "Newtonsoft.Json/13.0.4") `
        -UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*") `
        -TargetFramework "netstandard2.1" `
        -BuildOnly
}
finally {
    $expectedSmokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) $smokeName
    if ($smokeRoot -eq $expectedSmokeRoot -and (Test-Path -LiteralPath $smokeRoot)) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}

Write-Host "All isolated package consumer smoke tests passed."
