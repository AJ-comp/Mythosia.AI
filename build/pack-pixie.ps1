[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipConsumerTest
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/pixie-package' }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$project = Join-Path $repoRoot 'src/rag/Mythosia.AI.Rag.Search.Pixie/Mythosia.AI.Rag.Search.Pixie.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$packageId = 'Mythosia.AI.Rag.Search.Pixie'
$packagePath = Join-Path $output "$packageId.$version.nupkg"

# Validation against the current source contracts. This script does not publish any package.
& dotnet pack (Join-Path $repoRoot 'src/vectordb/Mythosia.VectorDb.Abstractions/Mythosia.VectorDb.Abstractions.csproj') --configuration Release --output $output
if ($LASTEXITCODE -ne 0) { throw 'Failed to pack vector database contracts.' }
& dotnet pack $project --configuration Release --output $output -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { throw 'Failed to pack PIXIE. Prepare the verified model assets first.' }
if (-not (Test-Path -LiteralPath $packagePath)) { throw 'PIXIE package was not generated.' }
# Use a conservative decimal cap, below NuGet.org's approximate 250 MB limit.
if ((Get-Item -LiteralPath $packagePath).Length -ge 250000000) { throw 'PIXIE package exceeds the 250 MB distribution budget.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $required = @('README.md', 'RELEASE_NOTES.md', 'NOTICE.txt', 'LICENSE',
        'lib/net8.0/Mythosia.AI.Rag.Search.Pixie.dll', 'contentFiles/any/any/models/pixie/model.int8.onnx',
        'contentFiles/any/any/models/pixie/tokenizer.json', 'contentFiles/any/any/models/pixie/manifest.json',
        'contentFiles/any/any/models/pixie/LICENSE', 'buildTransitive/Mythosia.AI.Rag.Search.Pixie.targets')
    foreach ($name in $required) {
        if ($null -eq $archive.GetEntry($name)) { throw "Required package entry missing: $name" }
    }
    if (@($archive.Entries | Where-Object { $_.FullName -match '(^|/)model\.onnx$|\.safetensors$' }).Count -gt 0) {
        throw 'Original unquantized model weights must not be included in the distributable package.'
    }
}
finally { $archive.Dispose() }

if (-not $SkipConsumerTest) {
    # Unique fresh cache prevents a globally cached package with the same version from masking errors.
    $consumer = Join-Path $output ('consumer-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $consumer | Out-Null
    $feedXml = [System.Security.SecurityElement]::Escape($output)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="validation" value="$feedXml" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources><config><add key="globalPackagesFolder" value="./packages" /></config></configuration>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'NuGet.Config') -Encoding utf8
    @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="$packageId" Version="$version" /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.csproj') -Encoding utf8
    @'
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;
using var encoder = new PixieSparseEncoder(new PixieOptions { IntraOpThreads = 4 });
var vector = await encoder.EncodeQueryAsync("한국어 기술 문서를 검색합니다.");
if (vector.Indices.Count == 0) throw new Exception("No sparse output from the bundled model.");
var store = new PixieInMemoryStore(encoder);
await store.UpsertAsync(new VectorRecord("guide", Array.Empty<float>(), "한국어 기술 문서의 검색 방법입니다."));
var found = await store.TextSearchAsync("기술 문서 검색");
if (found.Count != 1 || found[0].Record.Id != "guide") throw new Exception("Bundled model search failed.");
Console.WriteLine($"Bundled PIXIE package passed: {vector.Indices.Count} sparse dimensions, one indexed document.");
'@ | Set-Content -LiteralPath (Join-Path $consumer 'Program.cs') -Encoding utf8
    & dotnet run --project (Join-Path $consumer 'Consumer.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Fresh NuGet consumer failed. Model assets must be copied to the default runtime directory.' }
    & dotnet publish (Join-Path $consumer 'Consumer.csproj') --configuration Release --no-restore --output (Join-Path $consumer 'published')
    if ($LASTEXITCODE -ne 0) { throw 'NuGet consumer publish failed.' }
    & dotnet (Join-Path $consumer 'published/Consumer.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Published consumer could not load and run the bundled model.' }

    # A downstream app often depends on an application library, not directly on PIXIE.
    # Verify that the model assets propagate across a real NuGet dependency edge too.
    $transitive = Join-Path $output ('transitive-' + [Guid]::NewGuid().ToString('N'))
    $wrapper = Join-Path $transitive 'wrapper'
    $app = Join-Path $transitive 'app'
    New-Item -ItemType Directory -Path $wrapper, $app -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $consumer 'NuGet.Config') -Destination (Join-Path $transitive 'NuGet.Config')
    @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><PackageId>Mythosia.Pixie.PackageProbe</PackageId><Version>0.0.0</Version></PropertyGroup><ItemGroup><PackageReference Include="$packageId" Version="$version" /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $wrapper 'Wrapper.csproj') -Encoding utf8
    @'
using Mythosia.AI.Rag.Search.Pixie;
public static class PixiePackageProbe
{
    public static async Task RunAsync()
    {
        using var encoder = new PixieSparseEncoder(new PixieOptions { IntraOpThreads = 4 });
        var result = await encoder.EncodeQueryAsync("간접 패키지 참조에서도 모델이 실행됩니다.");
        if (result.Indices.Count == 0) throw new Exception("No transitive model output.");
        Console.WriteLine("Transitive bundled PIXIE model passed.");
    }
}
'@ | Set-Content -LiteralPath (Join-Path $wrapper 'Wrapper.cs') -Encoding utf8
    & dotnet pack (Join-Path $wrapper 'Wrapper.csproj') --configuration Release --output $output
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the transitive NuGet probe.' }
    @'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="Mythosia.Pixie.PackageProbe" Version="0.0.0" /></ItemGroup></Project>
'@ | Set-Content -LiteralPath (Join-Path $app 'App.csproj') -Encoding utf8
    @'
try { await PixiePackageProbe.RunAsync(); }
catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
'@ | Set-Content -LiteralPath (Join-Path $app 'Program.cs') -Encoding utf8
    & dotnet run --project (Join-Path $app 'App.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Indirect package references do not propagate the PIXIE model.' }
    & dotnet publish (Join-Path $app 'App.csproj') --configuration Release --no-restore --output (Join-Path $app 'published')
    if ($LASTEXITCODE -ne 0) { throw 'Transitive consumer publish failed.' }
    & dotnet (Join-Path $app 'published/App.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Transitive published consumer could not run PIXIE.' }
}

$manifest = [ordered]@{
    packageId = $packageId
    version = $version
    bytes = (Get-Item -LiteralPath $packagePath).Length
    sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    consumerValidated = -not $SkipConsumerTest
    transitiveConsumerValidated = -not $SkipConsumerTest
    publication = 'Not published. Validate and version the dependency release set before publication.'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'pixie-package-validation.json') -Encoding utf8
Write-Host "Validated PIXIE package: $packagePath ($($manifest.bytes) bytes). No package was published."
