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
    "Mythosia.AI.Providers.Alibaba"
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
        if ($libraries -contains $unexpectedLibrary) {
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

try {
    $escapedArtifactsDir = [System.Security.SecurityElement]::Escape($artifactsDir)
    $escapedGlobalPackagesFolder = [System.Security.SecurityElement]::Escape($globalPackagesFolder)
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
      <package pattern="Mythosia.AI*" />
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
        public static bool CompileRepresentativeV7Surface()
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

            Task<ImageGenerationResult> generation = imageService.GenerateImagesAsync(
                new ImageGenerationRequest { Prompt = "package smoke test" });
            Task<ImageGenerationResult> edit = imageService.EditImagesAsync(
                new ImageEditRequest { Prompt = "package smoke test edit" });

            return clonedPolicy.ExecutionMode == FunctionExecutionMode.Parallel &&
                gpt56Models.Length == 4 &&
                generation != null &&
                edit != null;
        }
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
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Base;

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

Console.WriteLine("Core-only consumer smoke test passed.");
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
        public static bool CompileRepresentativeV7Surface()
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
        public static bool CompileRepresentativeV2Surface()
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
}
finally {
    $expectedSmokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) $smokeName
    if ($smokeRoot -eq $expectedSmokeRoot -and (Test-Path -LiteralPath $smokeRoot)) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}

Write-Host "All isolated package consumer smoke tests passed."
