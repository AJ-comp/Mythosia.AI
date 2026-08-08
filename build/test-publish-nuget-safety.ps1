[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)
$publishScriptPath = Join-Path $repoRoot "build/publish-nuget.ps1"
$consumerScriptPath = Join-Path $repoRoot "build/test-nuget-packages.ps1"
$prepareDocfxScriptPath = Join-Path $repoRoot "build/prepare-docfx-references.ps1"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$tokens = $null
$parseErrors = $null
$publishAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $publishScriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw "publish-nuget.ps1 has PowerShell parse errors: $($parseErrors -join '; ')"
}

$publishText = [System.IO.File]::ReadAllText($publishScriptPath)
$consumerText = [System.IO.File]::ReadAllText($consumerScriptPath)
$prepareDocfxText = [System.IO.File]::ReadAllText($prepareDocfxScriptPath)
Assert-True ($publishText.Contains('Add-Type -AssemblyName System.Net.Http')) `
    "Windows PowerShell publication paths must load System.Net.Http before creating HttpClient."

$packStart = $publishText.IndexOf('if ($Mode -eq "Pack") {', [System.StringComparison]::Ordinal)
$packCommand = $publishText.IndexOf('& dotnet pack', $packStart, [System.StringComparison]::Ordinal)
$packCleanCheck = $publishText.IndexOf('Assert-CleanGitWorktree', $packStart, [System.StringComparison]::Ordinal)
Assert-True ($packStart -ge 0 -and $packCommand -gt $packStart) `
    "Could not locate the release pack branch."
Assert-True ($packCleanCheck -gt $packStart -and $packCleanCheck -lt $packCommand) `
    "Release packing must verify a clean worktree before invoking dotnet pack."
Assert-True ($publishText.Contains('-p:TreatWarningsAsErrors=true')) `
    "Release packing must fail when a package build emits a compiler or NuGet warning."
Assert-True ($publishText.Contains('[switch]$AllowDirtyValidationPack')) `
    "Development-only dirty packing must be an explicit switch."
Assert-True ($publishText.Contains('provenance = $packProvenance')) `
    "The release manifest must record pack provenance."
Assert-True ($publishText.Contains('schemaVersion = 3')) `
    "The publish script must emit release manifest schema 3."
Assert-True ($consumerText.Contains('[int]$manifest.schemaVersion -ne 3')) `
    "Consumer smoke tests must validate release manifest schema 3."
Assert-True ($consumerText.Contains('$gitExitCode = $LASTEXITCODE')) `
    "Consumer smoke tests must capture git's exit code before running a PowerShell pipeline."
Assert-True ($prepareDocfxText.Contains('"-p:TreatWarningsAsErrors=true"')) `
    "The DocFX reference build must reject compiler and analyzer warnings."

$functions = @($publishAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true))
$manifestFunction = $functions |
    Where-Object { $_.Name -eq "Assert-PublishableManifest" } |
    Select-Object -First 1
Assert-True ($null -ne $manifestFunction) `
    "The publishable-manifest guard is missing."
Invoke-Expression $manifestFunction.Extent.Text
$testCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
Assert-PublishableManifest -Manifest ([pscustomobject]@{
    schemaVersion = 3
    sourceCommit = $testCommit
    provenance = "clean-release"
}) -ExpectedSourceCommit $testCommit
$developmentManifestRejected = $false
try {
    Assert-PublishableManifest -Manifest ([pscustomobject]@{
        schemaVersion = 3
        sourceCommit = $testCommit
        provenance = "development-validation"
    }) -ExpectedSourceCommit $testCommit
}
catch {
    if ($_.Exception.Message -like "Release manifest is not publishable*") {
        $developmentManifestRejected = $true
    }
    else {
        throw
    }
}
Assert-True $developmentManifestRejected `
    "Push mode must reject development-validation manifests."

$symbolResumeFunction = $functions |
    Where-Object { $_.Name -eq "Push-VerifiedResumeSymbols" } |
    Select-Object -First 1
Assert-True ($null -ne $symbolResumeFunction) `
    "The provenance-gated symbol resume function is missing."
$symbolResumeText = $symbolResumeFunction.Extent.Text
Assert-True ($symbolResumeText.Contains('ResumeProvenanceVerified')) `
    "Symbol resume must require verified main-package provenance."
Assert-True ($symbolResumeText.Contains('--skip-duplicate')) `
    "Verified symbol resume must tolerate NuGet's pending-symbol 409 response."

$conflictFunction = $functions |
    Where-Object { $_.Name -eq "Test-IsNuGetConflictOutput" } |
    Select-Object -First 1
Assert-True ($null -ne $conflictFunction) `
    "NuGet conflict classification is missing."
Invoke-Expression $conflictFunction.Extent.Text
Assert-True (Test-IsNuGetConflictOutput -Output @(
    "Response status code does not indicate success: 409 (Conflict).")) `
    "A NuGet 409 must be recognized for provenance-gated resume recovery."
Assert-True (-not (Test-IsNuGetConflictOutput -Output @(
    "Response status code does not indicate success: 400 (Bad Request)."))) `
    "A non-conflict NuGet failure must not enter resume recovery."

$waitFunction = $functions |
    Where-Object { $_.Name -eq "Wait-ForVerifiedRemotePackage" } |
    Select-Object -First 1
Assert-True ($null -ne $waitFunction) `
    "Eventual-consistency provenance polling is missing."
Invoke-Expression $waitFunction.Extent.Text
$expectedRemoteCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
$remotePackage = [pscustomobject]@{ Id = "Safety.Test"; Version = "1.0.0" }
$script:indexChecks = 0
$script:packageReads = 0
function Test-PackageVersionExists {
    $script:indexChecks++
    return $true
}
function Get-RemotePackageSourceCommit {
    $script:packageReads++
    if ($script:packageReads -eq 1) {
        return $null
    }
    return $expectedRemoteCommit
}
Wait-ForVerifiedRemotePackage `
    -PackageBase "https://example.invalid/" `
    -Package $remotePackage `
    -ExpectedSourceCommit $expectedRemoteCommit `
    -MaxAttempts 2 `
    -DelaySeconds 0
Assert-True ($script:indexChecks -eq 2 -and $script:packageReads -eq 2) `
    "Provenance polling must retry when the index is visible before the remote nupkg is readable."

function Get-RemotePackageSourceCommit {
    return "cccccccccccccccccccccccccccccccccccccccc"
}
$mismatchedRemoteRejected = $false
try {
    Wait-ForVerifiedRemotePackage `
        -PackageBase "https://example.invalid/" `
        -Package $remotePackage `
        -ExpectedSourceCommit $expectedRemoteCommit `
        -MaxAttempts 1 `
        -DelaySeconds 0
}
catch {
    if ($_.Exception.Message -like "Published package*was not built from release manifest commit*") {
        $mismatchedRemoteRejected = $true
    }
    else {
        throw
    }
}
Assert-True $mismatchedRemoteRejected `
    "A resume-time remote package with different provenance must be rejected."

Invoke-Expression $symbolResumeFunction.Extent.Text
$AllowPartialResume = $true
$unverifiedState = [pscustomobject]@{
    Package = [pscustomobject]@{
        Id = "Safety.Test"
        Version = "1.0.0"
        SymbolPackagePath = "must-not-be-pushed.snupkg"
    }
    ResumeProvenanceVerified = $false
}
$unverifiedResumeRejected = $false
try {
    Push-VerifiedResumeSymbols -State $unverifiedState
}
catch {
    if ($_.Exception.Message -like "Symbol resume requires verified main-package provenance*") {
        $unverifiedResumeRejected = $true
    }
    else {
        throw
    }
}
Assert-True $unverifiedResumeRejected `
    "Symbol-only duplicate tolerance must reject unverified main-package provenance before invoking dotnet."

$commands = @($publishAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst]
}, $true))
$skipCommands = @($commands | Where-Object { $_.Extent.Text.Contains('--skip-duplicate') })
Assert-True ($skipCommands.Count -eq 1) `
    "--skip-duplicate must appear exactly once in the release script."
Assert-True ($skipCommands[0].Extent.StartOffset -ge $symbolResumeFunction.Extent.StartOffset -and
    $skipCommands[0].Extent.EndOffset -le $symbolResumeFunction.Extent.EndOffset) `
    "--skip-duplicate is permitted only inside provenance-verified symbol resume."

$mainPushCommands = @($commands | Where-Object {
    $_.GetCommandName() -eq "dotnet" -and
    $_.Extent.Text.Contains('$package.PackagePath')
})
Assert-True ($mainPushCommands.Count -eq 1) `
    "Expected exactly one main-package push command."
Assert-True (-not $mainPushCommands[0].Extent.Text.Contains('--skip-duplicate')) `
    "Main-package pushes must never skip a duplicate before provenance verification."

$conflictRecoveryStart = $publishText.IndexOf(
    'if ($AllowPartialResume -and (Test-IsNuGetConflictOutput -Output $pushOutput))',
    [System.StringComparison]::Ordinal)
$conflictVerification = $publishText.IndexOf(
    'Wait-ForVerifiedRemotePackage',
    $conflictRecoveryStart,
    [System.StringComparison]::Ordinal)
$conflictTrust = $publishText.IndexOf(
    '$state.ResumeProvenanceVerified = $true',
    $conflictRecoveryStart,
    [System.StringComparison]::Ordinal)
Assert-True ($conflictRecoveryStart -ge 0 -and
    $conflictVerification -gt $conflictRecoveryStart -and
    $conflictTrust -gt $conflictVerification) `
    "A resume-time main-package 409 must become trusted only after the remote nupkg commit is verified."

$expectedActions = [ordered]@{
    "actions/checkout" = "3d3c42e5aac5ba805825da76410c181273ba90b1"
    "actions/setup-dotnet" = "a98b56852c35b8e3190ac28c8c2271da59106c68"
    "actions/upload-artifact" = "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
    "actions/download-artifact" = "3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"
    "actions/upload-pages-artifact" = "fc324d3547104276b827a68afc52ff2a11cc49c9"
    "actions/deploy-pages" = "cd2ce8fcbc39b97be8ca5fce6e763baed58fa128"
}
$workflowPaths = @(
    (Join-Path $repoRoot ".github/workflows/ci.yml"),
    (Join-Path $repoRoot ".github/workflows/publish-nuget.yml"),
    (Join-Path $repoRoot ".github/workflows/docs.yml")
)
$workflowText = ($workflowPaths | ForEach-Object {
    [System.IO.File]::ReadAllText($_)
}) -join [Environment]::NewLine

$actionReferences = [regex]::Matches(
    $workflowText,
    'uses:\s+(actions/[A-Za-z0-9-]+)@([0-9a-f]{40})')
Assert-True ($actionReferences.Count -gt 0) `
    "No full-SHA GitHub Action references were found."
foreach ($reference in $actionReferences) {
    $action = $reference.Groups[1].Value
    $commit = $reference.Groups[2].Value
    Assert-True ($expectedActions.Contains($action)) `
        "Unexpected official action reference without a reviewed Node24 pin: $action"
    Assert-True ($expectedActions[$action] -eq $commit) `
        "Action $action is not pinned to its reviewed Node24 commit."
}
foreach ($expectedAction in $expectedActions.Keys) {
    Assert-True ($workflowText.Contains("$expectedAction@$($expectedActions[$expectedAction])")) `
        "Reviewed Node24 action pin is not used: $expectedAction"
}
$hardenedSolutionBuilds = [regex]::Matches(
    $workflowText,
    'dotnet build Mythosia\.AI\.slnx[^\r\n]*-p:TreatWarningsAsErrors=true')
Assert-True ($hardenedSolutionBuilds.Count -eq 3) `
    "CI, NuGet publication, and docs workflows must all reject solution build warnings."

Write-Host "NuGet publication safety contracts passed."
