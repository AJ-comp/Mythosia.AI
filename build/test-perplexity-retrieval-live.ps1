[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 11
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/perplexity-retrieval-live"
$reportFileName = "perplexity-retrieval-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~PerplexityRetrievalLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running independent Perplexity Search and four embedding model contracts with the existing Perplexity Key Vault credential. These real API requests incur charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Perplexity retrieval live tests failed (exit code $LASTEXITCODE)." }
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
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_PERPLEXITY_RETRIEVAL_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_PERPLEXITY_RETRIEVAL_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 17 -or @($requests | Where-Object { $_.StatusCode -ne 200 }).Count -ne 0) {
        throw "Expected exactly seventeen successful real API requests, observed $($requests.Count). Report: $reportPath"
    }
    $search = @($requests | Where-Object { $_.api -eq "search" })
    if ($search.Count -ne 3 -or @($search | Where-Object { $_.searchType -eq "people" }).Count -ne 1 -or
        @($search | Where-Object { $_.searchType -eq "web" }).Count -ne 2) {
        throw "Expected three actual Search calls covering two web requests and one people request. Report: $reportPath"
    }
    $embedding = @($requests | Where-Object { $_.api -ne "search" })
    if ($embedding.Count -ne 14 -or @($embedding | Where-Object { $_.dimensions -ne 128 }).Count -ne 0) {
        throw "Expected fourteen actual embedding requests with 128 dimensions. Report: $reportPath"
    }
    $modelRequests = @{
        "pplx-embed-v1-0.6b" = 3
        "pplx-embed-v1-4b" = 3
        "pplx-embed-context-v1-0.6b" = 2
        "pplx-embed-context-v1-4b" = 2
    }
    foreach ($model in $modelRequests.Keys) {
        $floatRequests = @($embedding | Where-Object { $_.model -eq $model -and $_.format -eq "base64_int8" })
        $binaryRequests = @($embedding | Where-Object { $_.model -eq $model -and $_.format -eq "base64_binary" })
        if ($floatRequests.Count -ne $modelRequests[$model] -or $binaryRequests.Count -ne 1 -or $binaryRequests[0].count -ne 3) {
            throw "Missing actual int8/binary model coverage for $model. Report: $reportPath"
        }
        $expectedApi = if ($model.Contains("-context-")) { "context" } else { "standard" }
        if (@(@($floatRequests) + @($binaryRequests) | Where-Object { $_.api -ne $expectedApi }).Count -ne 0) {
            throw "Embedding model $model used the wrong API endpoint. Report: $reportPath"
        }
    }
    if (($embedding | Measure-Object count -Sum).Sum -ne 26) {
        throw "Expected twenty-six returned embeddings across all int8 and binary requests. Report: $reportPath"
    }
    $features = @($lines | Where-Object { $_.StartsWith("LIVE_PERPLEXITY_RETRIEVAL_OK ") })
    if ($features.Count -ne $expectedTests) { throw "Every strict case must report completed assertions. Report: $reportPath" }
    Write-Host "Perplexity retrieval live validation passed: $passed/$total tests, seventeen successful requests, four models with int8 and binary embeddings, real RAG retrieval and grouped context queries, none skipped."
}
finally { Pop-Location }
