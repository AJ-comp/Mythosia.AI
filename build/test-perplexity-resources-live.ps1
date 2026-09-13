[CmdletBinding()]
param(
    [ValidateSet("All", "Profile", "CustomSkill", "Connector")]
    [string]$Resource = "All",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$selected = if ($Resource -eq "All") { @("Profile", "CustomSkill", "Connector") } else { @($Resource) }
$required = @{
    Profile = @("PROFILE_ID", "PROFILE_EXPECTED_TEXT")
    CustomSkill = @("CUSTOM_SKILL_ID", "CUSTOM_SKILL_EXPECTED_TEXT")
    Connector = @("CONNECTOR_ID", "CONNECTOR_SERVER_LABEL", "CONNECTOR_TOOL", "CONNECTOR_PROMPT", "CONNECTOR_EXPECTED_TEXT")
}
$missing = @($selected | ForEach-Object { $required[$_] } | ForEach-Object {
    $name = "MYTHOSIA_PERPLEXITY_$_"
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { $name }
})
if ($missing.Count -gt 0) {
    throw "Configure registered test resources before any API call. Missing environment variables: $($missing -join ', '). See tests/Mythosia.AI.Test/README.md."
}

$expectedTests = $selected.Count
$filter = "FullyQualifiedName~PerplexityResourceLiveTests&TestCategory=Live"
if ($Resource -ne "All") { $filter += "&FullyQualifiedName~$($Resource)_" }
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/perplexity-resources-live"
$reportName = "perplexity-resources-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", $filter, "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running $expectedTests registered-resource cases with the existing sonar-secret2 credential. Real API calls incur charges."
Write-Host "Only configured resources are referenced; no profile, skill, or connector is created."
Write-Host "TRX report: $reportPath"
$previousOptIn = [Environment]::GetEnvironmentVariable("MYTHOSIA_PERPLEXITY_RESOURCE_LIVE", "Process")
Push-Location -LiteralPath $repoRoot
try {
    [Environment]::SetEnvironmentVariable("MYTHOSIA_PERPLEXITY_RESOURCE_LIVE", "1", "Process")
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Perplexity registered-resource live tests failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing live report: $reportPath" }
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $counterNodes = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($counterNodes.Count -ne 1) { throw "Expected one TRX counter summary." }
    $counts = $counterNodes[0]
    if ([int]$counts.total -ne $expectedTests -or [int]$counts.executed -ne $expectedTests -or
        [int]$counts.passed -ne $expectedTests -or [int]$counts.failed -ne 0 -or
        [int]$counts.skipped -ne 0 -or [int]$counts.notExecuted -ne 0 -or [int]$counts.inconclusive -ne 0) {
        throw "Every selected resource case must pass with no skips: expected=$expectedTests."
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $evidence = @($lines | Where-Object { $_.StartsWith("LIVE_PERPLEXITY_RESOURCE_RESULT ") } |
        ForEach-Object { $_.Substring("LIVE_PERPLEXITY_RESOURCE_RESULT ".Length) | ConvertFrom-Json })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_PERPLEXITY_PLATFORM_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_PERPLEXITY_PLATFORM_REQUEST ".Length) | ConvertFrom-Json })
    if ($evidence.Count -ne $expectedTests -or $requests.Count -lt $expectedTests) { throw "Missing real execution evidence." }
    $scenarioNames = @{ Profile = "profile"; CustomSkill = "custom-skill"; Connector = "connector" }
    foreach ($item in $selected) {
        $scenario = $scenarioNames[$item]
        $verified = @($evidence | Where-Object { $_.scenario -eq $scenario -and $_.completed -and
            $_.requestReferenceVerified -and $_.executionVerified -and -not [string]::IsNullOrWhiteSpace($_.responseId) })
        $submits = @($requests | Where-Object { $_.scenario -eq $scenario -and $_.Operation -eq "submit" })
        if ($verified.Count -ne 1 -or $submits.Count -ne 1 -or $submits[0].StatusCode -ne 200 -or
            $submits[0].responseId -ne $verified[0].responseId) {
            throw "Expected one acknowledged submission and verified resource execution for $scenario."
        }
    }
    if (@($requests | Where-Object { $_.cleanupFailed -or
        (($_.StatusCode -lt 200 -or $_.StatusCode -ge 300) -and -not ($_.StatusCode -eq 429 -and $_.recovered429)) }).Count -ne 0) {
        throw "HTTP failures or unfinished background cleanup invalidate live verification."
    }
    Write-Host "Perplexity registered-resource live validation passed: $expectedTests/$expectedTests; $($requests.Count) HTTP requests."
}
finally {
    [Environment]::SetEnvironmentVariable("MYTHOSIA_PERPLEXITY_RESOURCE_LIVE", $previousOptIn, "Process")
    Pop-Location
}
