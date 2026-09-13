[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 24
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/perplexity-agent-live"
$reportFileName = "perplexity-agent-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~PerplexityAgentLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running Perplexity Agent live contracts with the existing sonar-secret2 Key Vault credential. These real API calls incur charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Perplexity Agent live tests failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing live test report: $reportPath" }
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $nodes = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($nodes.Count -ne 1) { throw "Expected one TRX counter summary." }
    $counts = $nodes[0]
    $total = [int]$counts.GetAttribute("total")
    $executed = [int]$counts.GetAttribute("executed")
    $passed = [int]$counts.GetAttribute("passed")
    if ($total -ne $expectedTests -or $executed -ne $total -or $passed -ne $total -or
        [int]$counts.GetAttribute("skipped") -ne 0 -or [int]$counts.GetAttribute("notExecuted") -ne 0 -or
        [int]$counts.GetAttribute("inconclusive") -ne 0) {
        throw "Every case must pass without skipping: expected=$expectedTests total=$total executed=$executed passed=$passed."
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_PERPLEXITY_AGENT_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_PERPLEXITY_AGENT_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 30 -or @($requests | Where-Object { $_.StatusCode -ne 200 -or $_.status -ne "completed" -or [string]::IsNullOrWhiteSpace($_.responseId) }).Count -ne 0) {
        throw "Expected 30 successful, completed real Agent requests; observed $($requests.Count)."
    }
    foreach ($preset in @("fast", "low", "medium", "high", "xhigh", "wide-research")) {
        if (@($requests | Where-Object { $_.preset -eq $preset }).Count -lt 1) { throw "Missing successful preset $preset." }
    }
    foreach ($effort in @("minimal", "low", "medium", "high", "xhigh", "max")) {
        if (@($requests | Where-Object { $_.reasoningEffort -eq $effort -and $_.model -eq "openai/gpt-5.6-luna" }).Count -ne 1) {
            throw "Expected one successful explicit reasoning request for $effort."
        }
    }
    if (($requests | Measure-Object customCallCount -Sum).Sum -ne 2) { throw "Expected exactly two real custom function calls." }
    if (($requests | Measure-Object imageInputs -Sum).Sum -ne 1) { throw "Expected one real PNG input." }
    if (@($requests | Where-Object { $_.responseFormat -eq "json_schema" }).Count -ne 2) { throw "Expected two schema requests without repair." }
    if (@($requests | Where-Object { $_.maxOutputTokens -eq 512 }).Count -ne 1) { throw "Expected one actual RAG rewrite." }
    Write-Host "Perplexity Agent live validation passed: $passed/$total cases, 30 completed requests, no skipped cases."
}
finally { Pop-Location }
