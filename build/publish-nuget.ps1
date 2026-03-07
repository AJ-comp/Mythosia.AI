$ErrorActionPreference = "Stop"

if (-not $env:NUGET_API_KEY) {
    throw "NUGET_API_KEY secret is missing."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactsDir = Join-Path $repoRoot "artifacts"

if (Test-Path $artifactsDir) {
    Remove-Item $artifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsDir | Out-Null

$nugetSource = "https://api.nuget.org/v3/index.json"

Write-Host "Loading NuGet service index..."
$serviceIndex = Invoke-RestMethod -Uri $nugetSource -Method Get

$packageBase = $serviceIndex.resources |
    Where-Object { $_.'@type' -like 'PackageBaseAddress*' } |
    Select-Object -First 1 -ExpandProperty '@id'

if (-not $packageBase) {
    throw "Could not find PackageBaseAddress resource from NuGet service index."
}

function Get-ProjectPropertyValue {
    param(
        [xml]$ProjectXml,
        [string]$Name
    )

    foreach ($pg in $ProjectXml.Project.PropertyGroup) {
        $node = $pg.$Name
        if ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.'#text')) {
            return $node.'#text'.Trim()
        }
        elseif ($null -ne $node -and $node -is [string] -and -not [string]::IsNullOrWhiteSpace($node)) {
            return $node.Trim()
        }
    }

    return $null
}

function Test-PackageVersionExists {
    param(
        [string]$PackageId,
        [string]$Version
    )

    $lowerId = $PackageId.ToLowerInvariant()
    $lowerVersion = $Version.ToLowerInvariant()
    $indexUrl = "$packageBase$lowerId/index.json"

    try {
        $result = Invoke-RestMethod -Uri $indexUrl -Method Get
        return $result.versions -contains $lowerVersion
    }
    catch {
        return $false
    }
}

$projects = Get-ChildItem -Path $repoRoot -Recurse -Filter *.csproj |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

foreach ($project in $projects) {
    Write-Host ""
    Write-Host "--------------------------------------------------"
    Write-Host "Checking project: $($project.FullName)"

    [xml]$xml = Get-Content $project.FullName

    $isPackable = Get-ProjectPropertyValue -ProjectXml $xml -Name "IsPackable"
    if ($isPackable -and $isPackable.ToLowerInvariant() -eq "false") {
        Write-Host "Skip: IsPackable=false"
        continue
    }

    $packageId = Get-ProjectPropertyValue -ProjectXml $xml -Name "PackageId"
    if (-not $packageId) {
        $packageId = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
    }

    $version = Get-ProjectPropertyValue -ProjectXml $xml -Name "PackageVersion"
    if (-not $version) {
        $version = Get-ProjectPropertyValue -ProjectXml $xml -Name "Version"
    }
    if (-not $version) {
        $version = Get-ProjectPropertyValue -ProjectXml $xml -Name "VersionPrefix"
    }

    if (-not $version) {
        Write-Host "Skip: no version found"
        continue
    }

    Write-Host "PackageId: $packageId"
    Write-Host "Version:   $version"

    $exists = Test-PackageVersionExists -PackageId $packageId -Version $version

    if ($exists) {
        Write-Host "Skip: already exists on NuGet.org -> $packageId $version"
        continue
    }

    Write-Host "Packing..."
    dotnet pack $project.FullName `
        -c Release `
        --output $artifactsDir `
        -p:PackageVersion=$version

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $($project.FullName)"
    }

    $pattern = "$packageId.$version*.nupkg"
    $packages = Get-ChildItem -Path $artifactsDir -Filter $pattern |
        Where-Object { $_.Name -notlike "*.snupkg" }

    if (-not $packages) {
        throw "Packed package not found for $packageId $version"
    }

    foreach ($pkg in $packages) {
        Write-Host "Pushing $($pkg.Name)..."
        dotnet nuget push $pkg.FullName `
            --source $nugetSource `
            --api-key $env:NUGET_API_KEY `
            --skip-duplicate

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet nuget push failed for $($pkg.FullName)"
        }
    }
}

Write-Host ""
Write-Host "Done."