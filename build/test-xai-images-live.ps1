[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 8
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/xai-images-live"
$reportFileName = "xai-images-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~GrokImageLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running Grok Imagine Image 2.0 and OpenAI image contracts with existing Key Vault credentials. Eight calls request nine output images; these operations incur image API charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Image live tests failed (exit code $LASTEXITCODE)." }
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
        throw "Every image case must pass without skipping: expected=$expectedTests total=$total executed=$executed passed=$passed skipped=$skipped notExecuted=$notExecuted inconclusive=$inconclusive. Report: $reportPath"
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_GROK_IMAGE_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_GROK_IMAGE_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 8 -or @($requests | Where-Object { $_.StatusCode -ne 200 }).Count -ne 0) {
        throw "Expected exactly eight successful image model requests, observed $($requests.Count). Report: $reportPath"
    }
    $xai = @($requests | Where-Object { $_.provider -eq "xAI" -and $_.model -eq "grok-imagine-image-2.0" })
    $openai = @($requests | Where-Object { $_.provider -eq "OpenAI" -and $_.model -eq "gpt-image-2" })
    if ($xai.Count -ne 6 -or $openai.Count -ne 2 -or
        ($xai | Measure-Object returnedImages -Sum).Sum -ne 7 -or
        ($openai | Measure-Object returnedImages -Sum).Sum -ne 2) {
        throw "Expected six Imagine requests/seven images and two GPT Image requests/two images. Report: $reportPath"
    }
    if (@($xai | Where-Object { $_.Path -eq "/v1/images/edits" }).Count -ne 3 -or
        @($openai | Where-Object { $_.Path -eq "/v1/images/edits" }).Count -ne 1 -or
        ($requests | Measure-Object inputImages -Sum).Sum -ne 9) {
        throw "Expected single-, two-, and five-reference Imagine edits plus one OpenAI reference edit. Report: $reportPath"
    }
    $artifacts = @($lines | Where-Object { $_.StartsWith("LIVE_GROK_IMAGE_ARTIFACT ") } |
        ForEach-Object { $_.Substring("LIVE_GROK_IMAGE_ARTIFACT ".Length) | ConvertFrom-Json })
    if ($artifacts.Count -ne 9 -or @($artifacts.Path | Sort-Object -Unique).Count -ne 9) {
        throw "Expected nine distinct decoded output-image artifacts. Report: $reportPath"
    }
    $artifactRoot = [System.IO.Path]::GetFullPath($resultsDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($artifact in $artifacts) {
        $absolutePath = [System.IO.Path]::GetFullPath($artifact.Path)
        if (-not $absolutePath.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "An image artifact is outside the expected results directory. Report: $reportPath"
        }
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf) -or
            (Get-Item -LiteralPath $absolutePath).Length -ne [long]$artifact.Bytes -or
            [int]$artifact.Width -lt 512 -or [int]$artifact.Height -lt 512) {
            throw "A decoded image artifact is missing or inconsistent: $absolutePath"
        }
    }
    $imageDirectories = @($artifacts.Path | ForEach-Object { Split-Path -Parent $_ } | Sort-Object -Unique)
    Write-Host "Image live validation passed: $passed/$total tests, eight successful model requests, nine decoded images, none skipped."
    Write-Host "Review the actual returned images and synthetic references in: $($imageDirectories -join ', ')"
}
finally { Pop-Location }
