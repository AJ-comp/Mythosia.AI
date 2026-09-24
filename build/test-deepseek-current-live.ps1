[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 14
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/deepseek-current-live"
$reportFileName = "deepseek-current-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~DeepSeekCurrentApiLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running DeepSeek current API live contracts: Flash and V4 Pro, Responses, Pro reasoning, and synthetic Files lifecycle."
Write-Host "Uses the existing DeepSeek Key Vault credential; 19 model calls and 4 Files calls are expected. TRX: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "DeepSeek current API live tests failed (exit code $LASTEXITCODE)." }
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
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_DEEPSEEK_CURRENT_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_DEEPSEEK_CURRENT_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 23 -or @($requests | Where-Object { $_.StatusCode -ne 200 }).Count -ne 0) {
        throw "Expected 23 successful real HTTP requests, observed $($requests.Count). Report: $reportPath"
    }
    $modelRequests = @($requests | Where-Object { $null -ne $_.model })
    $responses = @($modelRequests | Where-Object { $_.path -eq "/responses" })
    $chat = @($modelRequests | Where-Object { $_.path -eq "/chat/completions" })
    if ($modelRequests.Count -ne 19 -or $responses.Count -ne 15 -or $chat.Count -ne 4 -or
        @($responses | Where-Object { $_.terminal -ne "completed" }).Count -ne 0 -or
        @($chat | Where-Object { $_.terminal -ne "stop" }).Count -ne 0) {
        throw "Expected 15 completed Responses requests and 4 successful Chat requests. Report: $reportPath"
    }
    foreach ($model in @("deepseek-flash", "deepseek-v4-pro")) {
        if (@($responses | Where-Object { $_.model -eq $model -and $_.Streaming }).Count -lt 2 -or
            @($responses | Where-Object { $_.model -eq $model -and -not $_.Streaming }).Count -lt 1) {
            throw "Missing actual streaming and non-streaming Responses coverage for $model. Report: $reportPath"
        }
    }
    foreach ($effort in @("low", "high", "max")) {
        if (@($chat | Where-Object { $_.model -eq "deepseek-v4-pro" -and $_.effort -eq $effort }).Count -ne 1) {
            throw "Expected exactly one V4 Pro Chat request with effort=$effort. Report: $reportPath"
        }
    }
    if (($responses | Measure-Object nativeCalls -Sum).Sum -ne 2) {
        throw "Expected two actual provider-issued function calls, one for each model. Report: $reportPath"
    }
    $fileRequests = @($requests | Where-Object { $null -eq $_.model })
    if ($fileRequests.Count -ne 4 -or @($fileRequests | Where-Object { $_.Method -eq "POST" }).Count -ne 1 -or
        @($fileRequests | Where-Object { $_.Method -eq "GET" }).Count -ne 2 -or
        @($fileRequests | Where-Object { $_.Method -eq "DELETE" }).Count -ne 1) {
        throw "Expected upload, metadata retrieval, list, and successful cleanup deletion. Report: $reportPath"
    }
    Write-Host "DeepSeek current API live validation passed: $passed/$total tests, 19 model requests, complete Files lifecycle, none skipped."
}
finally { Pop-Location }
