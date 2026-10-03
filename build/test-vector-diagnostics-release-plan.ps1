[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-plan.ps1')
. (Join-Path $PSScriptRoot 'vector-diagnostics-compatibility.ps1')

function Assert-DiagnosticsPlan {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-DiagnosticsPlanRejected {
    param([scriptblock]$Action, [string]$Message)
    $rejected = $false
    try { $null = & $Action }
    catch { $rejected = $true }
    Assert-DiagnosticsPlan $rejected $Message
}

$diagnosticsIds = @(
    'Mythosia.VectorDb.Abstractions',
    'Mythosia.AI.Rag.Abstractions',
    'Mythosia.VectorDb.InMemory',
    'Mythosia.AI.Rag'
)
$unrelatedId = 'Mythosia.DiagnosticsPlanFixture'

function New-DiagnosticsPlanFixture {
    param([string[]]$ReleaseIds)
    $plan = @{
        SchemaVersion = 1
        PreviousReleaseCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        BaselineNote = 'Synthetic offline diagnostics consumer regression fixture.'
        Packages = @($ReleaseIds | ForEach-Object {
            @{
                Id = $_
                Version = '99.0.0'
                Project = "src/fixture/$_/$_.csproj"
                TargetFramework = 'net10.0'
                LicenseExpression = 'MIT'
                ProjectUrl = 'https://example.invalid/diagnostics-fixture'
                ReleaseNotesUrl = 'https://example.invalid/RELEASE_NOTES.md#v9900'
                Dependencies = @{}
                FixedDependencies = @{}
            }
        })
        ConsumerOnlyPackages = @($diagnosticsIds | Where-Object { $ReleaseIds -notcontains $_ } |
            ForEach-Object { @{ Id = $_; Version = '98.0.0' } })
    }
    Assert-ReleasePlanStructure -Plan $plan
    return $plan
}

function New-DiagnosticsManifestFixture {
    param([System.Collections.IDictionary]$Plan)
    # Use the same object shapes as a deserialized release-manifest.json.
    return (@{
        schemaVersion = 3
        packages = @($Plan.Packages | ForEach-Object { @{ id = $_.Id; version = $_.Version } })
    } | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
}

function Write-DiagnosticsFixturePackage {
    param([string]$Directory, [string]$Id, [string]$Version, [string]$Source)
    $path = Join-Path $Directory "$Id.$Version.nupkg"
    $archive = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = @{
            "$Id.nuspec" = @"
<?xml version="1.0"?>
<package><metadata><id>$Id</id><version>$Version</version><authors>Regression fixture</authors><description>Offline source mapping fixture.</description></metadata></package>
"@
            'lib/net10.0/_._' = ''
            'source.txt' = $Source
        }
        foreach ($name in $files.Keys) {
            $writer = [System.IO.StreamWriter]::new($archive.CreateEntry($name).Open())
            try { $writer.Write($files[$name]) }
            finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function Test-DiagnosticsPlanRestore {
    param([string]$Name, [System.Collections.IDictionary]$Plan, [string]$Directory, [switch]$OmitReleasePackage)
    $inputs = Get-VectorDiagnosticsCompatibilityInputs -Plan $Plan -Manifest (New-DiagnosticsManifestFixture $Plan)
    $releaseIds = @($Plan.Packages | ForEach-Object { [string]$_.Id })
    Assert-DiagnosticsPlan (($inputs.RequiredIds -join '|') -ceq ($diagnosticsIds -join '|')) `
        "$Name must always exercise all four diagnostics packages."
    Assert-DiagnosticsPlan (($inputs.ReleaseIds -join '|') -ceq ($releaseIds -join '|')) `
        "$Name must preserve the complete publication set separately from the diagnostics consumer set."
    Assert-DiagnosticsPlan ($inputs.Versions.Count -eq (@($Plan.Packages).Count + @($Plan.ConsumerOnlyPackages).Count)) `
        "$Name must retain every planned and consumer-only version."
    foreach ($package in @($Plan.Packages) + @($Plan.ConsumerOnlyPackages)) {
        Assert-DiagnosticsPlan ($inputs.Versions[$package.Id] -ceq $package.Version) `
            "$Name must use the exact explicit version of $($package.Id)."
    }

    # Both feeds contain each exact ID/version, so a successful restore alone is
    # insufficient: inspect the marker of the package NuGet actually selected.
    $releaseFeed = Join-Path $Directory 'release & feed'
    $publishedFeed = Join-Path $Directory 'published-feed'
    New-Item -ItemType Directory -Path $releaseFeed, $publishedFeed -Force | Out-Null
    foreach ($id in $inputs.Versions.Keys) {
        if (-not $OmitReleasePackage -or $id -cne $releaseIds[0]) {
            Write-DiagnosticsFixturePackage $releaseFeed $id $inputs.Versions[$id] 'release'
        }
        Write-DiagnosticsFixturePackage $publishedFeed $id $inputs.Versions[$id] 'published'
    }

    [xml]$config = New-VectorDiagnosticsNuGetConfig -ArtifactsDirectory $releaseFeed -ReleaseIds $inputs.ReleaseIds
    $sources = @($config.configuration.packageSources.add)
    Assert-DiagnosticsPlan ($sources.Count -eq 2 -and
        @($sources | Where-Object { $_.key -ceq 'release' -and $_.value -ceq $releaseFeed }).Count -eq 1) `
        "$Name must retain the escaped local release source."
    $publishedSources = @($sources | Where-Object { $_.key -ceq 'nuget.org' })
    Assert-DiagnosticsPlan ($publishedSources.Count -eq 1 -and
        $publishedSources[0].value -ceq 'https://api.nuget.org/v3/index.json') `
        "$Name must use the official NuGet source for published packages."
    $releaseMappings = @($config.configuration.packageSourceMapping.packageSource | Where-Object { $_.key -ceq 'release' })
    $publishedMappings = @($config.configuration.packageSourceMapping.packageSource | Where-Object { $_.key -ceq 'nuget.org' })
    Assert-DiagnosticsPlan ($releaseMappings.Count -eq 1 -and $publishedMappings.Count -eq 1) `
        "$Name must define release and published package source mappings."
    $releasePatterns = @($releaseMappings[0].package | ForEach-Object { [string]$_.pattern })
    Assert-DiagnosticsPlan (($releasePatterns -join '|') -ceq ($releaseIds -join '|')) `
        "$Name must map only exact publication targets to the release feed."
    $publishedPatterns = @($publishedMappings[0].package | ForEach-Object { [string]$_.pattern })
    Assert-DiagnosticsPlan ($publishedPatterns.Count -eq 1 -and $publishedPatterns[0] -ceq '*') `
        "$Name must allow all remaining packages from the published feed."

    # Replace only the official source location for this offline restore; keep
    # the generated source keys and package mappings unchanged.
    $publishedSources[0].SetAttribute('value', $publishedFeed)
    $configPath = Join-Path $Directory 'NuGet.config'
    $config.Save($configPath)
    $references = ($inputs.Versions.Keys | Sort-Object | ForEach-Object {
        '<PackageReference Include="' + $_ + '" Version="[' + $inputs.Versions[$_] + ']"/>'
    }) -join "`n"
    $projectPath = Join-Path $Directory 'DiagnosticsRestore.csproj'
    [System.IO.File]::WriteAllText($projectPath, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>$references</ItemGroup>
</Project>
"@)
    $packagesDirectory = Join-Path $Directory 'packages'
    $restoreLog = Join-Path $Directory 'restore.log'
    $previousNativeExitCode = $global:LASTEXITCODE
    & dotnet restore $projectPath --configfile $configPath --packages $packagesDirectory `
        --no-http-cache -p:RestoreFallbackFolders= -p:RestoreIgnoreFailedSources=false *> $restoreLog
    $restoreExitCode = $LASTEXITCODE
    if ($OmitReleasePackage) {
        $restoreText = [System.IO.File]::ReadAllText($restoreLog)
        Assert-DiagnosticsPlan ($restoreExitCode -ne 0 -and $restoreText -match 'NU1101' -and
            $restoreText.Contains($releaseIds[0])) `
            "$Name must fail when a release target exists only in the published feed: $restoreText"
        # GitHub's pwsh wrapper propagates LASTEXITCODE after this script returns.
        # An asserted, expected native failure must not become the job exit code.
        $global:LASTEXITCODE = $previousNativeExitCode
        return
    }
    if ($restoreExitCode -ne 0) {
        throw "$Name offline restore failed: $([System.IO.File]::ReadAllText($restoreLog))"
    }
    $assets = Get-Content -LiteralPath (Join-Path $Directory 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name)
    Assert-DiagnosticsPlan ($libraries.Count -eq $inputs.Versions.Count) "$Name resolved an unexpected dependency set."
    foreach ($id in $inputs.Versions.Keys) {
        $version = $inputs.Versions[$id]
        Assert-DiagnosticsPlan ($libraries -ccontains "$id/$version") "$Name failed to restore exact version $id/$version."
        $expectedSource = if ($releaseIds -contains $id) { 'release' } else { 'published' }
        $marker = Join-Path $packagesDirectory ($id.ToLowerInvariant() + '/' + $version + '/source.txt')
        Assert-DiagnosticsPlan ((Get-Content -LiteralPath $marker -Raw) -ceq $expectedSource) `
            "$Name restored $id/$version from the wrong package source; expected $expectedSource."
    }
}

$fullPlan = New-DiagnosticsPlanFixture $diagnosticsIds
$ragPlan = New-DiagnosticsPlanFixture @('Mythosia.AI.Rag')
$unrelatedPlan = New-DiagnosticsPlanFixture @($unrelatedId)
$allTargetsPlan = New-DiagnosticsPlanFixture ($diagnosticsIds + @($unrelatedId))
$manifest = New-DiagnosticsManifestFixture $allTargetsPlan
$manifest.packages = @($manifest.packages | Where-Object { $_.id -cne $unrelatedId })
Assert-DiagnosticsPlanRejected { Get-VectorDiagnosticsCompatibilityInputs -Plan $allTargetsPlan -Manifest $manifest } `
    'Diagnostics validation must reject a missing unrelated publication target.'

foreach ($mutation in @('missing', 'duplicate', 'unexpected', 'extra', 'wrong-case', 'version', 'missing-version')) {
    $manifest = New-DiagnosticsManifestFixture $fullPlan
    switch ($mutation) {
        'missing' { $manifest.packages = @($manifest.packages | Select-Object -Skip 1) }
        'duplicate' { $manifest.packages[0] = $manifest.packages[1] }
        'unexpected' { $manifest.packages[0].id = $unrelatedId }
        'extra' { $manifest.packages = @($manifest.packages) + @([pscustomobject]@{ id = $unrelatedId; version = '99.0.0' }) }
        'wrong-case' { $manifest.packages[0].id = $manifest.packages[0].id.ToLowerInvariant() }
        'version' { $manifest.packages[0].version = '99.0.1' }
        'missing-version' { $manifest.packages[0].version = $null }
    }
    Assert-DiagnosticsPlanRejected { Get-VectorDiagnosticsCompatibilityInputs -Plan $fullPlan -Manifest $manifest } `
        "Diagnostics validation must reject the $mutation manifest fixture."
}
$missingConsumerPlan = New-DiagnosticsPlanFixture @($unrelatedId)
$missingConsumerPlan.ConsumerOnlyPackages = @($missingConsumerPlan.ConsumerOnlyPackages | Select-Object -Skip 1)
Assert-ReleasePlanStructure -Plan $missingConsumerPlan
Assert-DiagnosticsPlanRejected {
    Get-VectorDiagnosticsCompatibilityInputs -Plan $missingConsumerPlan -Manifest (New-DiagnosticsManifestFixture $missingConsumerPlan)
} 'Diagnostics validation must reject a missing required consumer-only version.'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $repoRoot ('artifacts/diagnostics-release-plan-' + [Guid]::NewGuid().ToString('N'))
foreach ($fixture in @(
    @{ Name = 'all-four'; Plan = $fullPlan },
    @{ Name = 'rag-only'; Plan = $ragPlan },
    @{ Name = 'unrelated-only'; Plan = $unrelatedPlan }
)) {
    Test-DiagnosticsPlanRestore -Name $fixture.Name -Plan $fixture.Plan -Directory (Join-Path $output $fixture.Name)
}
Test-DiagnosticsPlanRestore -Name 'missing-local-release' -Plan $ragPlan `
    -Directory (Join-Path $output 'missing-local-release') -OmitReleasePackage
Write-Host 'Vector diagnostics release-plan contracts passed (three offline restores and missing-release rejection).'
