[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 11
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/perplexity-platform-live"
$reportFileName = "perplexity-platform-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~PerplexityPlatformLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running 11 Perplexity platform live cases with the existing sonar-secret2 credential."
Write-Host "Coverage: background, replay, cancellation, continuation, valid model-list selection, hosted tools, public MCP, inline skill, generated XLSX."
Write-Host "Not proven by this suite: failover after an actual provider outage; invalid model IDs are rejected before model selection."
Write-Host "Excluded pending user resource IDs: saved profiles, custom skills, connected accounts. These are not passed or skipped cases."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Perplexity platform live tests failed (exit code $LASTEXITCODE)." }
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
        throw "Every configured case must pass: expected=$expectedTests total=$total executed=$executed passed=$passed."
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_PERPLEXITY_PLATFORM_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_PERPLEXITY_PLATFORM_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -lt 21 -or @($requests | Where-Object { ($_.StatusCode -lt 200 -or $_.StatusCode -ge 300) -and -not ($_.StatusCode -eq 429 -and $_.recovered429) -or $_.cleanupFailed }).Count -ne 0) {
        throw "Expected successful platform requests and no uncleaned background jobs; observed $($requests.Count) requests."
    }
    for ($index = 0; $index -lt $requests.Count; $index++) {
        $request = $requests[$index]
        if ($request.StatusCode -ne 429) { continue }
        if ([string]::IsNullOrWhiteSpace($request.RequestResponseId) -or
            -not ($request.Method -eq "GET" -or ($request.Method -eq "POST" -and $request.Operation -eq "cancel"))) {
            throw "Only safe background reads and cancellation may retry a 429; submissions must not be retried."
        }
        $later = @($requests | Select-Object -Skip ($index + 1) | Where-Object {
            $_.scenario -eq $request.scenario -and $_.RequestResponseId -eq $request.RequestResponseId -and
            $_.Operation -eq $request.Operation -and $_.Method -eq $request.Method -and $_.StartingAfter -eq $request.StartingAfter -and
            $_.StatusCode -ge 200 -and $_.StatusCode -lt 300
        })
        if ($later.Count -eq 0) { throw "A throttled operation did not recover to 2xx for the same response ID." }
    }
    $successful = @($requests | Where-Object { $_.StatusCode -ge 200 -and $_.StatusCode -lt 300 })
    $submissions = @($requests | Where-Object { $_.Operation -eq "submit" })
    if ($submissions.Count -ne 12 -or @($submissions | Where-Object { [string]::IsNullOrWhiteSpace($_.responseId) }).Count -ne 0) {
        throw "Expected exactly 12 acknowledged Agent submissions with response IDs."
    }
    foreach ($scenario in @("background", "replay", "cancel", "continuation", "model-list", "fetch", "finance", "people", "mcp", "inline-skill", "xlsx")) {
        if (@($requests | Where-Object { $_.scenario -eq $scenario }).Count -lt 1) { throw "Missing scenario: $scenario" }
    }
    $replays = @($successful | Where-Object { $_.Operation -eq "replay" })
    if ($replays.Count -ne 2 -or @($replays | Where-Object { $null -eq $_.StartingAfter }).Count -ne 0 -or
        $replays[1].StartingAfter -le $replays[0].StartingAfter) {
        throw "Expected two durable GET streams with mandatory, strictly advancing cursors."
    }
    if (@($requests | Where-Object { $_.Operation -eq "cancel" -and $_.Method -eq "POST" }).Count -lt 1 -or
        @($requests | Where-Object { $_.scenario -eq "cancel" -and $_.status -eq "cancelled" }).Count -lt 1) {
        throw "A completed response does not prove cancellation; require the real cancel POST and cancelled state."
    }
    if (@($submissions | Where-Object { $_.scenario -eq "continuation" -and -not [string]::IsNullOrWhiteSpace($_.previousResponseId) }).Count -ne 1) {
        throw "Expected one server-state continuation."
    }
    foreach ($trace in @("fetch_url_results", "finance_results", "people_search_results", "mcp_call", "sandbox_results", "share_file")) {
        if (@($requests | Where-Object { $_.outputTypes -contains $trace }).Count -lt 1) { throw "Missing actual provider tool trace: $trace" }
    }
    foreach ($skill in @("inline", "builtin")) {
        if (@($submissions | Where-Object { $_.skillTypes -contains $skill }).Count -ne 1) { throw "Expected one $skill skill request." }
    }
    if (@($successful | Where-Object { $_.Operation -eq "files" }).Count -ne 1 -or
        @($successful | Where-Object { $_.Operation -eq "file-content" -and $_.bytes -gt 0 }).Count -ne 1) {
        throw "Expected one generated-file listing and one nonempty OOXML download."
    }
    $recoveredCount = @($requests | Where-Object { $_.StatusCode -eq 429 -and $_.recovered429 }).Count
    Write-Host "Perplexity platform live validation passed: $passed/$total cases; $($requests.Count) HTTP requests; 12 Agent submissions; $recoveredCount recorded and recovered 429 responses."
}
finally { Pop-Location }
