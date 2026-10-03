[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactsDirectory,
    [string]$OutputDirectory,
    [string]$ReleasePlanPath = (Join-Path $PSScriptRoot 'release-plan.psd1')
)

$ErrorActionPreference = 'Stop'
$artifacts = (Resolve-Path -LiteralPath $ArtifactsDirectory).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifacts ('diagnostics-compat-' + [Guid]::NewGuid().ToString('N'))
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $artifacts 'release-manifest.json') -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot 'release-plan.ps1')
. (Join-Path $PSScriptRoot 'vector-diagnostics-compatibility.ps1')
$inputs = Get-VectorDiagnosticsCompatibilityInputs -Plan (Get-ReleasePlan -Path $ReleasePlanPath) -Manifest $manifest
$requiredIds = $inputs.RequiredIds
$versions = $inputs.Versions

function Write-Utf8([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Diagnostics compatibility command failed: dotnet $($Arguments -join ' ')" }
}

$legacyDirectory = Join-Path $output 'legacy'
$consumerDirectory = Join-Path $output 'consumer'
New-Item -ItemType Directory -Path $legacyDirectory, $consumerDirectory -Force | Out-Null
$legacyConfig = Join-Path $legacyDirectory 'NuGet.config'
Write-Utf8 $legacyConfig @'
<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
'@
$legacyProject = Join-Path $legacyDirectory 'LegacyDiagnosticsFixture.csproj'
Write-Utf8 $legacyProject @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup><PackageReference Include="Mythosia.AI.Rag.Abstractions" Version="[6.4.0]"/></ItemGroup>
</Project>
'@
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/vector-diagnostics/Legacy.cs') -Destination (Join-Path $legacyDirectory 'Legacy.cs')
Invoke-DotNet @('restore', $legacyProject, '--configfile', $legacyConfig, '--packages', (Join-Path $output 'legacy-packages'))
Invoke-DotNet @('build', $legacyProject, '-c', 'Release', '--no-restore')
$legacyAssets = Get-Content -Raw -LiteralPath (Join-Path $legacyDirectory 'obj/project.assets.json') | ConvertFrom-Json
if ($legacyAssets.libraries.PSObject.Properties.Name -cnotcontains 'Mythosia.AI.Rag.Abstractions/6.4.0') {
    throw 'The legacy fixture was not compiled against the published 6.4.0 interface.'
}
$legacyDll = Join-Path $legacyDirectory 'bin/Release/netstandard2.1/LegacyDiagnosticsFixture.dll'
$legacyHash = (Get-FileHash -LiteralPath $legacyDll -Algorithm SHA256).Hash

$consumerConfig = Join-Path $consumerDirectory 'NuGet.config'
Write-Utf8 $consumerConfig (New-VectorDiagnosticsNuGetConfig -ArtifactsDirectory $artifacts -ReleaseIds $inputs.ReleaseIds)
$references = ($requiredIds | ForEach-Object { '<PackageReference Include="' + $_ + '" Version="[' + $versions[$_] + ']"/>' }) -join "`n"
$escapedDll = [System.Security.SecurityElement]::Escape($legacyDll)
$consumerProject = Join-Path $consumerDirectory 'DiagnosticsCompatibility.csproj'
Write-Utf8 $consumerProject @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>$references
    <Reference Include="LegacyDiagnosticsFixture"><HintPath>$escapedDll</HintPath></Reference>
  </ItemGroup>
</Project>
"@
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/vector-diagnostics/Consumer.cs') -Destination (Join-Path $consumerDirectory 'Program.cs')
Invoke-DotNet @('restore', $consumerProject, '--configfile', $consumerConfig, '--packages', (Join-Path $output 'current-packages'))
Invoke-DotNet @('build', $consumerProject, '-c', 'Release', '--no-restore')
Invoke-DotNet @('run', '--project', $consumerProject, '-c', 'Release', '--no-build', '--no-restore')
$currentAssets = Get-Content -Raw -LiteralPath (Join-Path $consumerDirectory 'obj/project.assets.json') | ConvertFrom-Json
foreach ($id in $requiredIds) {
    if ($currentAssets.libraries.PSObject.Properties.Name -cnotcontains "$id/$($versions[$id])") {
        throw "The consumer did not resolve the exact release package: $id/$($versions[$id])"
    }
}
$copiedLegacyDll = Join-Path $consumerDirectory 'bin/Release/net10.0/LegacyDiagnosticsFixture.dll'
if ((Get-FileHash -LiteralPath $copiedLegacyDll -Algorithm SHA256).Hash -cne $legacyHash) {
    throw 'The old binary was replaced or recompiled instead of used unchanged.'
}
Write-Host 'Vector diagnostics precompiled compatibility checks passed.'
