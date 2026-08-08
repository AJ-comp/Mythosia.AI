[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release",

    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)
$docfxPath = Join-Path $repoRoot "docfx.json"
$solutionPath = Join-Path $repoRoot "Mythosia.AI.slnx"
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$referenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "docfx-references"))

if (-not $referenceDirectory.StartsWith(
    $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare DocFX references outside the repository artifact directory."
}

$buildArguments = @(
    "build",
    $solutionPath,
    "--configuration",
    $Configuration,
    "-p:CopyLocalLockFileAssemblies=true",
    "-p:TreatWarningsAsErrors=true"
)
if ($NoRestore) {
    $buildArguments += "--no-restore"
}

& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "The solution build required for DocFX reference preparation failed with exit code $LASTEXITCODE."
}

$docfx = Get-Content -Raw -LiteralPath $docfxPath | ConvertFrom-Json
$metadata = @($docfx.metadata)[0]
$metadataSource = @($metadata.src)[0]
$inputPaths = @($metadataSource.files | ForEach-Object {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$_)))
})
$sourceAssemblyNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

foreach ($inputPath in $inputPaths) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Missing compiled DocFX input assembly: $inputPath"
    }

    [void]$sourceAssemblyNames.Add([System.IO.Path]::GetFileName($inputPath))
}

$candidates = foreach ($inputPath in $inputPaths) {
    Get-ChildItem -LiteralPath (Split-Path -Parent $inputPath) -Filter "*.dll" -File |
        Where-Object { -not $sourceAssemblyNames.Contains($_.Name) }
}

$references = foreach ($candidate in $candidates) {
    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($candidate.FullName)
    $publicKeyTokenBytes = $assemblyName.GetPublicKeyToken()
    $publicKeyToken = if ($null -eq $publicKeyTokenBytes -or $publicKeyTokenBytes.Length -eq 0) {
        "unsigned"
    }
    else {
        [System.BitConverter]::ToString($publicKeyTokenBytes).Replace("-", "").ToLowerInvariant()
    }

    [pscustomobject]@{
        File = $candidate
        SimpleName = $assemblyName.Name
        Version = $assemblyName.Version.ToString()
        PublicKeyToken = $publicKeyToken
        Identity = "$($assemblyName.Name)|$($assemblyName.Version)|$publicKeyToken"
        Preference = if ($candidate.FullName -match '[\\/]net10\.0[\\/]') { 2 } else { 1 }
    }
}

if (Test-Path -LiteralPath $referenceDirectory) {
    Remove-Item -LiteralPath $referenceDirectory -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $referenceDirectory)

$selectedReferences = @($references |
    Group-Object -Property Identity |
    ForEach-Object {
        $_.Group | Sort-Object -Property @(
            @{ Expression = "Preference"; Descending = $true },
            @{ Expression = { $_.File.FullName }; Descending = $false }
        ) | Select-Object -First 1
    })

foreach ($simpleNameGroup in @($selectedReferences | Group-Object -Property SimpleName)) {
    $hasMultipleIdentities = $simpleNameGroup.Count -gt 1
    foreach ($reference in $simpleNameGroup.Group) {
        $destinationName = if ($hasMultipleIdentities) {
            "$($reference.SimpleName).$($reference.Version).$($reference.PublicKeyToken).dll"
        }
        else {
            "$($reference.SimpleName).dll"
        }

        Copy-Item -LiteralPath $reference.File.FullName -Destination (Join-Path $referenceDirectory $destinationName)
    }
}

Write-Host "Prepared $($selectedReferences.Count) unique DocFX reference assemblies in $referenceDirectory."
