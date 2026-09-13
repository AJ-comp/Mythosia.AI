[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
if ([Environment]::GetEnvironmentVariable("MYTHOSIA_OPENAI_IMAGE25_LIVE") -cne "1") {
    throw "Set MYTHOSIA_OPENAI_IMAGE25_LIVE=1 explicitly before running this script. It makes four billable image API requests."
}
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedTests = 4
$resultsDirectory = Join-Path $repoRoot "artifacts/test-results/openai-images-live"
$reportName = "openai-image25-live-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))-$([Guid]::NewGuid().ToString('N')).trx"
$reportPath = Join-Path $resultsDirectory $reportName
$testArguments = @(
    "test", "--project", (Join-Path $repoRoot "tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj"),
    "--configuration", "Release", "--settings", (Join-Path $repoRoot "tests/Mythosia.AI.Test/serial.runsettings"),
    "--filter", "FullyQualifiedName~OpenAIImage25LiveTests&TestCategory=Live",
    "--minimum-expected-tests", [string]$expectedTests,
    "--output", "Detailed", "--report-trx", "--report-trx-filename", $reportName,
    "--results-directory", $resultsDirectory
)
if ($NoBuild) { $testArguments += "--no-build" }
Write-Host "Running four GPT Image 2.5 cases using the existing OpenAI Key Vault credential. Sunburst and Flare each generate and edit one low-quality 1024x1024 PNG."
Write-Host "These four requests incur image API charges. No automatic retries or image URL downloads are performed."
Write-Host "TRX report: $reportPath"
Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "GPT Image 2.5 live tests failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing image live report: $reportPath" }
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $counterNodes = @($report.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($counterNodes.Count -ne 1) { throw "Expected exactly one TRX counter summary." }
    $counts = $counterNodes[0]
    if ([int]$counts.total -ne $expectedTests -or [int]$counts.executed -ne $expectedTests -or
        [int]$counts.passed -ne $expectedTests -or [int]$counts.failed -ne 0 -or
        [int]$counts.skipped -ne 0 -or [int]$counts.notExecuted -ne 0 -or [int]$counts.inconclusive -ne 0) {
        throw "All four GPT Image 2.5 live cases must pass without skipped or inconclusive results. Report: $reportPath"
    }
    $lines = @($report.SelectNodes("//*[local-name()='UnitTestResult']/*[local-name()='Output']/*[local-name()='StdOut']") |
        ForEach-Object { $_.InnerText -split "`r?`n" })
    $requests = @($lines | Where-Object { $_.StartsWith("LIVE_OPENAI_IMAGE25_REQUEST ") } |
        ForEach-Object { $_.Substring("LIVE_OPENAI_IMAGE25_REQUEST ".Length) | ConvertFrom-Json })
    if ($requests.Count -ne 4 -or @($requests | Where-Object {
        $_.StatusCode -ne 200 -or $_.count -ne "1" -or $_.returnedImages -ne 1 -or
        $_.size -ne "1024x1024" -or $_.quality -ne "low" -or $_.format -ne "png" -or
        $_.hasRequestId -ne $true -or $_.InputTokens -le 0 -or $_.OutputTokens -le 0 -or
        $_.TotalTokens -ne ($_.InputTokens + $_.OutputTokens)
    }).Count -ne 0) {
        throw "Expected four successful bounded image requests with request IDs, actual inline images and matching token usage. Report: $reportPath"
    }
    foreach ($model in @("gpt-image-2.5-sunburst", "gpt-image-2.5-flare")) {
        $generation = @($requests | Where-Object { $_.model -eq $model -and $_.Path -eq "/v1/images/generations" })
        $editing = @($requests | Where-Object { $_.model -eq $model -and $_.Path -eq "/v1/images/edits" })
        if ($generation.Count -ne 1 -or $editing.Count -ne 1 -or $generation[0].ContentType -ne "application/json" -or
            $editing[0].ContentType -ne "multipart/form-data" -or $generation[0].inputImages -ne 0 -or $editing[0].inputImages -ne 1) {
            throw "Expected one JSON generation and one multipart single-reference edit for $model. Report: $reportPath"
        }
    }
    $completed = @($lines | Where-Object { $_.StartsWith("LIVE_OPENAI_IMAGE25_OK ") } |
        ForEach-Object { $_.Substring("LIVE_OPENAI_IMAGE25_OK ".Length) | ConvertFrom-Json })
    $artifacts = @($lines | Where-Object { $_.StartsWith("LIVE_OPENAI_IMAGE25_ARTIFACT ") } |
        ForEach-Object { $_.Substring("LIVE_OPENAI_IMAGE25_ARTIFACT ".Length) | ConvertFrom-Json })
    if ($completed.Count -ne 4 -or @($completed | ForEach-Object { "$($_.model)/$($_.editing)" } | Sort-Object -Unique).Count -ne 4 -or
        $artifacts.Count -ne 4 -or @($artifacts.Path | Sort-Object -Unique).Count -ne 4) {
        throw "Expected four distinct verified model/operation cases and four decoded output-image artifacts. Report: $reportPath"
    }
    $artifactRoot = [System.IO.Path]::GetFullPath($resultsDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($artifact in $artifacts) {
        $absolutePath = [System.IO.Path]::GetFullPath($artifact.Path)
        if (-not $absolutePath.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "An image artifact is outside the expected results directory."
        }
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf) -or
            (Get-Item -LiteralPath $absolutePath).Length -ne [long]$artifact.Bytes -or [long]$artifact.Bytes -le 0 -or
            [int]$artifact.Width -ne 1024 -or [int]$artifact.Height -ne 1024 -or $artifact.MediaType -ne "image/png") {
            throw "A fully decoded PNG artifact is missing or inconsistent: $absolutePath"
        }
    }
    Write-Host "GPT Image 2.5 live validation passed: 4/4 cases, four HTTP 200 model requests and four fully decoded PNG images; none skipped."
    Write-Host "Actual image outputs and synthetic edit references: $(@($artifacts.Path | ForEach-Object { Split-Path -Parent $_ } | Sort-Object -Unique) -join ', ')"
}
finally { Pop-Location }
