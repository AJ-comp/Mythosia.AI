[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 16
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/xai-grok-live"
$reportFileName = "xai-grok-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~Grok46LiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running Grok 4.6 live contracts using the existing xAI Key Vault credential. These real model calls incur API charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Grok 4.6 live tests failed (exit code $LASTEXITCODE)." }
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
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_GROK46_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_GROK46_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 20 -or @($requests | Where-Object { $_.StatusCode -ne 200 -or $_.model -ne "grok-4.6" }).Count -ne 0) {
        throw "Expected exactly 20 successful real grok-4.6 requests, observed $($requests.Count). Report: $reportPath"
    }
    $expectedEfforts = @{ low = 6; medium = 6; high = 4; xhigh = 2 }
    foreach ($effort in $expectedEfforts.Keys) {
        $observed = @($requests | Where-Object { $_.reasoningEffort -eq $effort }).Count
        if ($observed -ne $expectedEfforts[$effort]) {
            throw "Expected $($expectedEfforts[$effort]) real requests with reasoning effort $effort, observed $observed. Report: $reportPath"
        }
    }
    if (@($requests | Where-Object { $null -eq $_.reasoningEffort }).Count -ne 2) {
        throw "Expected two real Auto requests with the reasoning_effort field omitted. Report: $reportPath"
    }
    if (($requests | Measure-Object nativeCallIds -Sum).Sum -ne 2) {
        throw "Expected two actual provider-issued client-tool call IDs. Report: $reportPath"
    }
    Write-Host "Grok 4.6 live validation passed: $passed/$total tests, 20 successful model requests, every supported reasoning effort verified, none skipped."
}
finally { Pop-Location }
