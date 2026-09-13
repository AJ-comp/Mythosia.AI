[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 14
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/deepseek-flash-live"
$reportFileName = "deepseek-flash-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~DeepSeekFlashLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running DeepSeek Flash live contracts using the existing DeepSeek Key Vault credential. These real model calls incur API charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "DeepSeek Flash live tests failed (exit code $LASTEXITCODE)." }
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
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_DEEPSEEK_FLASH_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_DEEPSEEK_FLASH_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 21 -or @($requests | Where-Object { $_.StatusCode -ne 200 -or $_.model -ne "deepseek-flash" }).Count -ne 0) {
        throw "Expected exactly 21 successful real deepseek-flash requests, observed $($requests.Count). Report: $reportPath"
    }
    $expectedEfforts = @{ low = 3; high = 11; max = 1 }
    foreach ($effort in $expectedEfforts.Keys) {
        $observed = @($requests | Where-Object { $_.reasoningEffort -eq $effort }).Count
        if ($observed -ne $expectedEfforts[$effort]) {
            throw "Expected $($expectedEfforts[$effort]) real requests with reasoning effort $effort, observed $observed. Report: $reportPath"
        }
    }
    if (@($requests | Where-Object { $null -eq $_.reasoningEffort }).Count -ne 6) {
        throw "Expected five non-thinking requests and one native Auto request with reasoning_effort omitted. Report: $reportPath"
    }
    if (($requests | Measure-Object nativeCallIds -Sum).Sum -ne 4) {
        throw "Expected four actual provider-issued client-tool call IDs. Report: $reportPath"
    }
    if (@($requests | Where-Object { $_.maxTokens -eq 16384 -and $_.reasoningEffort -eq "max" }).Count -ne 1 -or
        @($requests | Where-Object { $_.maxTokens -eq 512 -and $_.thinking -eq "disabled" }).Count -ne 1 -or
        @($requests | Where-Object { $_.maxTokens -eq 4096 }).Count -ne 19) {
        throw "Expected one actual 16,384-token Max request, one 512-token rewrite, and nineteen 4,096-token requests. Report: $reportPath"
    }
    if (@($requests | Where-Object { $_.thinking -eq "disabled" }).Count -ne 5 -or
        @($requests | Where-Object { $_.thinking -eq "enabled" }).Count -ne 16 -or
        ($requests | Measure-Object imageInputs -Sum).Sum -ne 2) {
        throw "Expected five non-thinking requests, sixteen thinking requests, and two actual inline image inputs. Report: $reportPath"
    }
    Write-Host "DeepSeek Flash live validation passed: $passed/$total tests, 21 successful model requests, every supported reasoning effort verified, none skipped."
}
finally { Pop-Location }
