[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 6
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/google-image-model-options-live"
$reportFileName = "google-image-model-options-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportFileName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~GoogleImageModelOptionsLiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportFileName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running six Gemini image option cases with the existing Google Key Vault credential. These real model calls incur API charges."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Google image option live tests failed (exit code $LASTEXITCODE)." }
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
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_GOOGLE_IMAGE_OPTIONS_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_GOOGLE_IMAGE_OPTIONS_REQUEST ".Length) | ConvertFrom-Json })
    $artifacts = @($lines | Where-Object { $_.StartsWith("LIVE_GOOGLE_IMAGE_OPTIONS_ARTIFACT ") } |
        ForEach-Object { $_.Substring("LIVE_GOOGLE_IMAGE_OPTIONS_ARTIFACT ".Length) | ConvertFrom-Json })
    $successes = @($lines | Where-Object { $_.StartsWith("LIVE_GOOGLE_IMAGE_OPTIONS_OK ") } |
        ForEach-Object { $_.Substring("LIVE_GOOGLE_IMAGE_OPTIONS_OK ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 6 -or $artifacts.Count -ne 6 -or $successes.Count -ne 6 -or
        @($requests | Where-Object { $_.statusCode -ne 200 -or $_.mimeType -ne "IMAGE_JPEG" }).Count -ne 0) {
        throw "Expected exactly six successful HTTP requests and six decoded JPEG artifacts, without retries or fallbacks. Report: $reportPath"
    }
    $cases = @(
        @{ Model = "gemini-3.1-flash-image"; Resolution = "IMAGE_SIZE_FIVE_TWELVE"; Ratio = "ASPECT_RATIO_FOUR_BY_ONE" },
        @{ Model = "gemini-3.1-flash-lite-image"; Resolution = "IMAGE_SIZE_ONE_K"; Ratio = "ASPECT_RATIO_SIXTEEN_BY_NINE" },
        @{ Model = "gemini-3-pro-image"; Resolution = "IMAGE_SIZE_TWO_K"; Ratio = "ASPECT_RATIO_FOUR_BY_THREE" }
    )
    foreach ($case in $cases) {
        foreach ($operation in @("generate", "edit")) {
            $matching = @($requests | Where-Object {
                $_.model -eq $case.Model -and $_.operation -eq $operation -and
                $_.path -eq "/v1/models/$($case.Model):generateContent" -and
                $_.resolution -eq $case.Resolution -and $_.aspectRatio -eq $case.Ratio -and
                $_.inputImages -eq $(if ($operation -eq "edit") { 1 } else { 0 })
            })
            $images = @($artifacts | Where-Object {
                $_.model -eq $case.Model -and $_.operation -eq $operation -and $_.mimeType -eq "image/jpeg" -and
                $_.resolution -eq $case.Resolution -and $_.aspectRatio -eq $case.Ratio
            })
            $completed = @($successes | Where-Object { $_.model -eq $case.Model -and $_.operation -eq $operation })
            if ($matching.Count -ne 1 -or $images.Count -ne 1 -or $completed.Count -ne 1) {
                throw "Missing exact request/output evidence for $($case.Model) $operation. Report: $reportPath"
            }
            if (-not (Test-Path -LiteralPath $images[0].path -PathType Leaf)) {
                throw "Missing generated image artifact: $($images[0].path)"
            }
        }
    }
    Write-Host "Google image option live validation passed: $passed/$total tests, six HTTP 200 requests, three models with generation/editing, decoded resolution and aspect ratio verified, none skipped."
}
finally { Pop-Location }
