[CmdletBinding()]
param(
    [switch]$NoBuild,
    [ValidateSet("All", "OpenAI", "Anthropic", "Google")]
    [string]$Provider = "All"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$minimumExpectedTests = switch ($Provider) {
    "OpenAI" { 6 }
    "Anthropic" { 4 }
    "Google" { 6 }
    default { 16 }
}
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/request-features-live"
$reportFileName = "request-features-live-$($Provider.ToLowerInvariant())-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$filter = "FullyQualifiedName~RequestFeaturesLiveTests&TestCategory=Live"
if ($Provider -ne "All") { $filter += "&TestCategory=$Provider" }
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release",
    "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", $filter,
    "--minimum-expected-tests", [string]$minimumExpectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }

Write-Host "Running $Provider request-feature live tests with existing Key Vault credentials. These tests incur model/search/indexing charges and delete the synthetic resources they create."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Request-feature live tests failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing live test report: $reportPath" }
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $nodes = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($nodes.Count -ne 1) { throw "Expected exactly one TRX counter summary: $reportPath" }
    $counts = $nodes[0]
    $total = [int]$counts.GetAttribute("total")
    $executed = [int]$counts.GetAttribute("executed")
    $passed = [int]$counts.GetAttribute("passed")
    $skipped = [int]$counts.GetAttribute("skipped")
    $notExecuted = [int]$counts.GetAttribute("notExecuted")
    $inconclusive = [int]$counts.GetAttribute("inconclusive")
    if ($total -lt $minimumExpectedTests -or $executed -ne $total -or $passed -ne $total -or
        $skipped -ne 0 -or $notExecuted -ne 0 -or $inconclusive -ne 0) {
        throw "Every selected live case must pass without skipping. total=$total executed=$executed passed=$passed skipped=$skipped notExecuted=$notExecuted inconclusive=$inconclusive. Report: $reportPath"
    }
    Write-Host "Request-feature live validation passed for ${Provider}: $passed/$total tests, none skipped."
}
finally { Pop-Location }
