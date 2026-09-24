[CmdletBinding()]
param([string]$ModelDirectory, [switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $ModelDirectory) { $ModelDirectory = Join-Path $repoRoot 'src/rag/Mythosia.AI.Rag.Search.Pixie/models/pixie' }
$reportDirectory = Join-Path $repoRoot 'artifacts/test-results/pixie-local-model'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$reportName = 'pixie-' + [Guid]::NewGuid().ToString('N') + '.trx'
$previousModelDirectory = $env:MYTHOSIA_PIXIE_MODEL_DIR
$testArgs = @('test', '--project', (Join-Path $repoRoot 'tests/Mythosia.AI.Rag.Search.Pixie.Tests/Mythosia.AI.Rag.Search.Pixie.Tests.csproj'),
    '--configuration', 'Release', '--filter', 'TestCategory=LocalModel', '--minimum-expected-tests', '5',
    '--report-trx', '--report-trx-filename', $reportName, '--results-directory', $reportDirectory)
if ($NoBuild) { $testArgs += '--no-build' }
try {
    $env:MYTHOSIA_PIXIE_MODEL_DIR = [System.IO.Path]::GetFullPath($ModelDirectory)
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'PIXIE real-model tests failed.' }
}
finally { $env:MYTHOSIA_PIXIE_MODEL_DIR = $previousModelDirectory }
[xml]$report = Get-Content -LiteralPath (Join-Path $reportDirectory $reportName) -Raw
$counts = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
if ($counts.Count -ne 1) { throw 'Missing PIXIE test counters.' }
$total = [int]$counts[0].GetAttribute('total')
if ($total -lt 5 -or [int]$counts[0].GetAttribute('passed') -ne $total -or
    [int]$counts[0].GetAttribute('executed') -ne $total) { throw 'Real-model verification requires all tests to execute and pass; skipped tests are not a success.' }
Write-Host "Verified $total real PIXIE model tests with none skipped. Report: $(Join-Path $reportDirectory $reportName)"
