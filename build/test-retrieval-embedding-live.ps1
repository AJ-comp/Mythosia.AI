[CmdletBinding()]
param(
    [ValidateSet("All", "Voyage", "Gemini")]
    [string]$Provider = "All",
    [ValidateRange(0, 120)]
    [int]$VoyageRequestIntervalSeconds = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
if ([Environment]::GetEnvironmentVariable("MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE") -cne "1") {
    throw "Set MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE=1 explicitly before running this script. It makes billable embedding API requests."
}
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = switch ($Provider) { "Voyage" { 4 } "Gemini" { 3 } default { 7 } }
$selectedProviders = if ($Provider -eq "All") { @("Voyage", "Gemini") } else { @($Provider) }
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/retrieval-embedding-live"
$buildArtifactsDirectory = Join-Path $repoRoot "artifacts/retrieval-embedding-build"
$reportFileName = "retrieval-embedding-live-$($Provider.ToLowerInvariant())-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$filter = "FullyQualifiedName~RetrievalEmbeddingLiveTests&TestCategory=Live"
if ($Provider -ne "All") { $filter += "&TestCategory=$Provider" }
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--artifacts-path", $buildArtifactsDirectory,
    "--filter", $filter, "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running $Provider retrieval embedding live validation with synthetic TXT, Markdown and PDF documents. These API requests incur charges."
Write-Host "Voyage: VOYAGE_API_KEY, MYTHOSIA_VOYAGE_API_KEY, or MYTHOSIA_VOYAGE_SECRET_NAME for an existing vault secret."
Write-Host "Gemini: GEMINI_API_KEY, GOOGLE_API_KEY, MYTHOSIA_GEMINI_API_KEY, or existing gemini-secret Key Vault credential."
Write-Host "Voyage request-start interval: $VoyageRequestIntervalSeconds seconds (test-only pacing; no retries)."
Write-Host "TRX report: $reportPath"
$previousVoyageInterval = [Environment]::GetEnvironmentVariable("MYTHOSIA_VOYAGE_REQUEST_INTERVAL_SECONDS", "Process")
Push-Location -LiteralPath $repoRoot
try {
    [Environment]::SetEnvironmentVariable("MYTHOSIA_VOYAGE_REQUEST_INTERVAL_SECONDS",
        $VoyageRequestIntervalSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), "Process")
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Retrieval embedding live tests failed (exit code $LASTEXITCODE). Report: $reportPath" }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing live test report: $reportPath" }
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $nodes = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($nodes.Count -ne 1) { throw "Expected exactly one TRX counter summary." }
    $counts = $nodes[0]
    $total = [int]$counts.GetAttribute("total")
    $executed = [int]$counts.GetAttribute("executed")
    $passed = [int]$counts.GetAttribute("passed")
    if ($total -ne $expectedTests -or $executed -ne $total -or $passed -ne $total -or
        [int]$counts.GetAttribute("skipped") -ne 0 -or [int]$counts.GetAttribute("notExecuted") -ne 0 -or
        [int]$counts.GetAttribute("inconclusive") -ne 0) {
        throw "Every expected live case must execute and pass without skips or inconclusive results: expected=$expectedTests total=$total executed=$executed passed=$passed. Report: $reportPath"
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_RETRIEVAL_EMBEDDING_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_RETRIEVAL_EMBEDDING_REQUEST ".Length) | ConvertFrom-Json })
    $features = @($lines | Where-Object { $_.StartsWith("LIVE_RETRIEVAL_EMBEDDING_OK ") })
    if ($features.Count -ne $expectedTests) { throw "Every case must report completed retrieval assertions. Report: $reportPath" }
    $expectedRequests = 0
    foreach ($selected in $selectedProviders) {
        $voyage = $selected -eq "Voyage"
        $requestCount = if ($voyage) { 11 } else { 9 }
        $documentCount = if ($voyage) { 7 } else { 6 }
        $queryCount = if ($voyage) { 4 } else { 3 }
        $dimensions = if ($voyage) { 1024 } else { 1536 }
        $model = if ($voyage) { "voyage-context-4" } else { "gemini-embedding-2" }
        $providerRequests = @($requests | Where-Object { $_.provider -eq $selected })
        $expectedRequests += $requestCount
        if ($providerRequests.Count -ne $requestCount -or
            @($providerRequests | Where-Object { $_.StatusCode -ne 200 -or $_.dimensions -ne $dimensions -or $_.model -ne $model }).Count -ne 0 -or
            @($providerRequests | Where-Object { $_.operation -eq "document" }).Count -ne $documentCount -or
            @($providerRequests | Where-Object { $_.operation -eq "query" }).Count -ne $queryCount) {
            throw "Missing successful $selected document/query requests with model $model and $dimensions dimensions. Report: $reportPath"
        }
        foreach ($extension in @(".txt", ".md", ".pdf")) {
            if (@($features | Where-Object { $_ -eq "LIVE_RETRIEVAL_EMBEDDING_OK provider=$selected feature=extract-index-query extension=$extension" }).Count -ne 1) {
                throw "Missing $selected extraction and retrieval coverage for $extension. Report: $reportPath"
            }
        }
        if ($voyage -and (@($providerRequests | Where-Object { $_.operation -eq "document" -and $_.chunks -eq 101 }).Count -ne 1 -or
            @($features | Where-Object { $_ -eq "LIVE_RETRIEVAL_EMBEDDING_OK provider=Voyage feature=101-chunks-one-document" }).Count -ne 1)) {
            throw "Expected one real Voyage request retaining all 101 tiny document chunks. Report: $reportPath"
        }
    }
    if ($requests.Count -ne $expectedRequests) { throw "Unexpected real API request count. Report: $reportPath" }
    Write-Host "Retrieval embedding live validation passed: $passed/$total tests, $expectedRequests successful requests, TXT/Markdown/PDF retrieval verified, none skipped."
}
finally {
    [Environment]::SetEnvironmentVariable("MYTHOSIA_VOYAGE_REQUEST_INTERVAL_SECONDS", $previousVoyageInterval, "Process")
    Pop-Location
}
