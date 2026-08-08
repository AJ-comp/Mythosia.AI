[CmdletBinding()]
param(
    [string]$Solution = "Mythosia.AI.slnx"
)

$ErrorActionPreference = "Stop"

$reportJson = @(& dotnet list $Solution package `
    --vulnerable `
    --include-transitive `
    --no-restore `
    --format json `
    --output-version 1)
if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability audit failed to run."
}

$report = ($reportJson -join [Environment]::NewLine) | ConvertFrom-Json
$findings = @()
foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($packageKind in @("topLevelPackages", "transitivePackages")) {
            foreach ($package in @($framework.$packageKind)) {
                $vulnerabilities = @($package.vulnerabilities)
                if ($null -eq $package -or $vulnerabilities.Count -eq 0) {
                    continue
                }

                $advisories = $vulnerabilities | ForEach-Object {
                    "$($_.severity):$($_.advisoryurl)"
                }
                $findings += "$($project.path): $($package.id) $($package.resolvedVersion) [$($advisories -join ', ')]"
            }
        }
    }
}

if ($findings.Count -gt 0) {
    throw "Vulnerable NuGet packages detected:`n$($findings -join [Environment]::NewLine)"
}

Write-Host "NuGet vulnerability audit passed: no known vulnerable direct or transitive packages."
