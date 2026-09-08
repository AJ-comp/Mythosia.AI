[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$testProject = Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"
$settingsPath = Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"
$minimumExpectedTests = 6
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/openai-async-live"
$reportFileName = "openai-async-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test",
    "--project", $testProject,
    "--configuration", "Release",
    "--settings", $settingsPath,
    "--filter", "FullyQualifiedName~OpenAIAsyncToolCallingLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$minimumExpectedTests,
    "--output", "Detailed",
    "--report-trx",
    "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) {
    $testArguments += "--no-build"
}

Write-Host "Running six OpenAI asynchronous tool-calling live cases. These requests incur API charges."
Write-Host "Authentication uses the existing LiveTestSecrets Key Vault configuration."
Write-Host "TRX report: $reportPath"

Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) {
        throw "OpenAI asynchronous tool-calling live tests failed (exit code $testExitCode)."
    }

    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "The live test run did not produce its expected TRX report: $reportPath"
    }

    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $counterNodes = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($counterNodes.Count -ne 1) {
        throw "The live test TRX report must contain exactly one result counter summary: $reportPath"
    }

    $counters = $counterNodes[0]
    $total = [int]$counters.GetAttribute("total")
    $executed = [int]$counters.GetAttribute("executed")
    $passed = [int]$counters.GetAttribute("passed")
    $skipped = [int]$counters.GetAttribute("skipped")
    $notExecuted = [int]$counters.GetAttribute("notExecuted")
    $inconclusive = [int]$counters.GetAttribute("inconclusive")
    if ($total -lt $minimumExpectedTests -or $executed -ne $total -or $passed -ne $total -or
        $skipped -ne 0 -or $notExecuted -ne 0 -or $inconclusive -ne 0) {
        throw (("Live validation requires at least {0} tests and every test passed with none skipped or inconclusive. " +
            "Observed total={1}, executed={2}, passed={3}, skipped={4}, notExecuted={5}, inconclusive={6}. Report: {7}") -f
            $minimumExpectedTests, $total, $executed, $passed, $skipped, $notExecuted, $inconclusive, $reportPath)
    }

    Write-Host "OpenAI asynchronous tool-calling live validation passed: $passed/$total tests, none skipped."
}
finally {
    Pop-Location
}
