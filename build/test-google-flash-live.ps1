[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 24
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/google-flash-live"
$reportFileName = "google-flash-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~GeminiFlashLatestLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running Gemini 3.7 and 3.8 Flash live contracts using existing Key Vault credentials. Model, search, and indexing calls incur API charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Google Flash live tests failed (exit code $LASTEXITCODE)." }
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
    if ($total -ne $expectedTests -or $executed -ne $total -or $passed -ne $total -or
        $skipped -ne 0 -or $notExecuted -ne 0 -or $inconclusive -ne 0) {
        throw "Every expected live case must pass without skipping: expected=$expectedTests total=$total executed=$executed passed=$passed skipped=$skipped notExecuted=$notExecuted inconclusive=$inconclusive. Report: $reportPath"
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_GOOGLE_FLASH_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_GOOGLE_FLASH_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 30 -or @($requests | Where-Object { $_.StatusCode -ne 200 }).Count -ne 0) {
        throw "Expected exactly 30 successful real model requests, observed $($requests.Count). Report: $reportPath"
    }
    foreach ($model in @("gemini-3.7-flash", "gemini-3.8-flash")) {
        if (@($requests | Where-Object { $_.model -eq $model }).Count -ne 15) {
            throw "Expected 15 real requests to $model. Report: $reportPath"
        }
    }
    $created = @($lines | Where-Object { $_.StartsWith("LIVE_SEARCH_FIXTURE_READY provider=Google ") }).Count
    $deleted = @($lines | Where-Object { $_.StartsWith("LIVE_SEARCH_FIXTURE_DELETED provider=Google ") }).Count
    if ($created -ne 4 -or $deleted -ne 4) {
        throw "Expected four synthetic Google stores to be created and cleaned up: created=$created deleted=$deleted. Report: $reportPath"
    }
    Write-Host "Google Flash live validation passed: $passed/$total tests, 30 successful model requests, four synthetic stores cleaned up, none skipped."
}
finally { Pop-Location }
