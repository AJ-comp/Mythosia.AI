[CmdletBinding()]
param(
    [ValidateSet("Pack", "Push")]
    [string]$Mode = "Pack",

    [string]$ArtifactsDirectory,

    [switch]$AllowPartialResume,

    [switch]$AllowDirtyValidationPack
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

if ($AllowPartialResume -and $Mode -ne "Push") {
    throw "AllowPartialResume is valid only with -Mode Push."
}
if ($AllowDirtyValidationPack -and $Mode -ne "Pack") {
    throw "AllowDirtyValidationPack is valid only with -Mode Pack."
}

$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)

function Test-IsReparsePoint {
    param([System.IO.FileSystemInfo]$Item)

    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        return $true
    }

    $linkTypeProperty = $Item.PSObject.Properties["LinkType"]
    return $null -ne $linkTypeProperty -and -not [string]::IsNullOrWhiteSpace([string]$linkTypeProperty.Value)
}

function Assert-SafeArtifactsDirectory {
    param(
        [string]$RepositoryRoot,
        [string]$CandidatePath
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $trimCharacters = [char[]]@(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedRoot = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd($trimCharacters)
    $normalizedCandidate = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd($trimCharacters)
    $allowedBase = [System.IO.Path]::GetFullPath(
        (Join-Path $normalizedRoot "artifacts")).TrimEnd($trimCharacters)
    $comparison = if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }

    $rootWithSeparator = $normalizedRoot + $separator
    $allowedBaseWithSeparator = $allowedBase + $separator
    if (-not $normalizedCandidate.Equals($allowedBase, $comparison) -and
        -not $normalizedCandidate.StartsWith($allowedBaseWithSeparator, $comparison)) {
        throw "ArtifactsDirectory must be the repository artifacts directory or one of its descendants: $allowedBase"
    }

    $filesystemRoot = [System.IO.Path]::GetPathRoot($normalizedCandidate).TrimEnd($trimCharacters)
    if ($normalizedCandidate.Equals($filesystemRoot, $comparison) -or
        $normalizedCandidate.Equals($normalizedRoot, $comparison)) {
        throw "ArtifactsDirectory cannot be a filesystem or repository root."
    }

    $rootItem = Get-Item -LiteralPath $normalizedRoot -Force
    if (Test-IsReparsePoint -Item $rootItem) {
        throw "Repository root must not be a reparse point during package publication."
    }

    $relativePath = $normalizedCandidate.Substring($rootWithSeparator.Length)
    $segments = $relativePath.Split(
        $trimCharacters,
        [System.StringSplitOptions]::RemoveEmptyEntries)
    $currentPath = $normalizedRoot
    foreach ($segment in $segments) {
        $currentPath = Join-Path $currentPath $segment
        if (-not (Test-Path -LiteralPath $currentPath)) {
            break
        }

        $item = Get-Item -LiteralPath $currentPath -Force
        if (Test-IsReparsePoint -Item $item) {
            throw "ArtifactsDirectory path contains a reparse point: $currentPath"
        }
    }

    if (Test-Path -LiteralPath $normalizedCandidate) {
        $candidateItem = Get-Item -LiteralPath $normalizedCandidate -Force
        if (-not $candidateItem.PSIsContainer) {
            throw "ArtifactsDirectory must be a directory: $normalizedCandidate"
        }

        $nestedReparsePoint = Get-ChildItem -LiteralPath $normalizedCandidate -Force -Recurse |
            Where-Object { Test-IsReparsePoint -Item $_ } |
            Select-Object -First 1
        if ($null -ne $nestedReparsePoint) {
            throw "ArtifactsDirectory contains a reparse point and cannot be deleted safely: $($nestedReparsePoint.FullName)"
        }
    }
}

function Get-VerifiedSourceCommit {
    $headOutput = & git -C $repoRoot rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "Could not resolve the repository HEAD commit."
    }

    $headCommit = [string](@($headOutput | Where-Object { $_ -match '^[0-9a-fA-F]{40,64}$' })[-1])
    if ([string]::IsNullOrWhiteSpace($headCommit)) {
        throw "Git did not return a valid full HEAD commit."
    }
    $headCommit = $headCommit.ToLowerInvariant()

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        $githubCommit = $env:GITHUB_SHA.Trim().ToLowerInvariant()
        if ($githubCommit -notmatch '^[0-9a-f]{40,64}$' -or $githubCommit -ne $headCommit) {
            throw "GITHUB_SHA does not match the checked-out HEAD commit."
        }
    }

    return $headCommit
}

function Assert-CleanGitWorktree {
    $status = @(& git -C $repoRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the Git worktree before publication."
    }
    if ($status.Count -ne 0) {
        throw "NuGet publication requires a clean Git worktree so package provenance matches sourceCommit."
    }
}

if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repoRoot "artifacts"
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repoRoot $ArtifactsDirectory
}

$artifactsDir = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
Assert-SafeArtifactsDirectory -RepositoryRoot $repoRoot -CandidatePath $artifactsDir

$manifestPath = Join-Path $artifactsDir "release-manifest.json"
$nugetSource = "https://api.nuget.org/v3/index.json"
$sourceCommit = Get-VerifiedSourceCommit

# This is intentionally an explicit, dependency-ordered release set. Do not replace it
# with recursive project discovery: a version edit in an unrelated project must never
# cause that package to be published by this workflow.
$releasePackages = @(
    [pscustomobject]@{
        Id = "Mythosia.AI.Abstractions"
        Project = "src/core/Mythosia.AI.Abstractions/Mythosia.AI.Abstractions.csproj"
        Assembly = "Mythosia.AI.Abstractions.dll"
        ProjectUrl = "https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Abstractions"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v300"
        Dependencies = @{}
        FixedDependencies = @{
            "Mythosia" = "1.4.0"
        }
    },
    [pscustomobject]@{
        Id = "Mythosia.AI"
        Project = "src/core/Mythosia.AI/Mythosia.AI.csproj"
        Assembly = "Mythosia.AI.dll"
        ProjectUrl = "https://github.com/AJ-comp/Mythosia.AI"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v700"
        Dependencies = @{
            "Mythosia.AI.Abstractions" = "Mythosia.AI.Abstractions"
        }
        FixedDependencies = @{
            "Azure.AI.OpenAI" = "2.1.0"
            "Newtonsoft.Json" = "13.0.4"
            "NJsonSchema" = "11.6.1"
            "System.Threading.Channels" = "10.0.10"
            "TiktokenSharp" = "1.2.1"
        }
    },
    [pscustomobject]@{
        Id = "Mythosia.AI.Providers.Alibaba"
        Project = "src/core/Mythosia.AI.Providers.Alibaba/Mythosia.AI.Providers.Alibaba.csproj"
        Assembly = "Mythosia.AI.Providers.Alibaba.dll"
        ProjectUrl = "https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Providers.Alibaba"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v200"
        Dependencies = @{
            "Mythosia.AI" = "Mythosia.AI"
        }
        FixedDependencies = @{
            "TiktokenSharp" = "1.2.1"
        }
    }
)

function Get-ProjectPropertyValue {
    param(
        [xml]$ProjectXml,
        [string]$Name
    )

    foreach ($propertyGroup in $ProjectXml.Project.PropertyGroup) {
        $node = $propertyGroup.$Name
        if ($null -eq $node) {
            continue
        }

        $value = [string]$node
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return $null
}

function Get-ReleasePackageMetadata {
    param([pscustomobject]$Definition)

    $projectPath = Join-Path $repoRoot $Definition.Project
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Release project does not exist: $projectPath"
    }

    [xml]$projectXml = Get-Content -Raw -LiteralPath $projectPath
    $packageId = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageId"
    $isPackable = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "IsPackable"
    $version = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageVersion"
    if (-not $version) {
        $version = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "Version"
    }
    if (-not $version) {
        $version = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "VersionPrefix"
    }

    if ($packageId -ne $Definition.Id) {
        throw "Expected PackageId '$($Definition.Id)' in $projectPath, but found '$packageId'."
    }
    if ($isPackable -ne "true") {
        throw "Release project must declare IsPackable=true: $projectPath"
    }
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Release project has no package version: $projectPath"
    }

    return [pscustomobject]@{
        Id = $packageId
        Version = $version
        Project = $Definition.Project
        ProjectPath = $projectPath
        Assembly = $Definition.Assembly
        ProjectUrl = $Definition.ProjectUrl
        ReleaseNotesUrl = $Definition.ReleaseNotesUrl
        Dependencies = $Definition.Dependencies
        FixedDependencies = $Definition.FixedDependencies
        PackagePath = Join-Path $artifactsDir "$packageId.$version.nupkg"
        SymbolPackagePath = Join-Path $artifactsDir "$packageId.$version.snupkg"
    }
}

function Get-NuspecMetadata {
    param([string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like "*.nuspec" })
        if ($nuspecEntries.Count -ne 1) {
            throw "Expected exactly one nuspec in $PackagePath, found $($nuspecEntries.Count)."
        }

        $stream = $nuspecEntries[0].Open()
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }

        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
        $textEntries = @{}
        foreach ($entryName in @("README.md", "RELEASE_NOTES.md")) {
            $matchingEntries = @($archive.Entries | Where-Object {
                $_.FullName.Replace("\", "/") -eq $entryName
            })
            if ($matchingEntries.Count -eq 1) {
                $entryStream = $matchingEntries[0].Open()
                $entryReader = [System.IO.StreamReader]::new($entryStream)
                try {
                    $textEntries[$entryName] = $entryReader.ReadToEnd()
                }
                finally {
                    $entryReader.Dispose()
                    $entryStream.Dispose()
                }
            }
        }

        return [pscustomobject]@{
            Metadata = $nuspec.package.metadata
            Entries = $entries
            ReadmeText = $textEntries["README.md"]
            ReleaseNotesText = $textEntries["RELEASE_NOTES.md"]
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Test-PackageArtifact {
    param(
        [pscustomobject]$Package,
        [hashtable]$VersionsById,
        [string]$ExpectedSourceCommit
    )

    if (-not (Test-Path -LiteralPath $Package.PackagePath -PathType Leaf)) {
        throw "Expected package was not produced: $($Package.PackagePath)"
    }

    $packageFiles = @(Get-ChildItem -LiteralPath $artifactsDir -Filter "$($Package.Id).$($Package.Version)*.nupkg" |
        Where-Object { $_.Name -notlike "*.snupkg" })
    if ($packageFiles.Count -ne 1 -or $packageFiles[0].FullName -ne $Package.PackagePath) {
        throw "Expected one exact package artifact for $($Package.Id) $($Package.Version)."
    }

    $nuspec = Get-NuspecMetadata -PackagePath $Package.PackagePath
    if ([string]$nuspec.Metadata.id -ne $Package.Id) {
        throw "Package ID mismatch in $($Package.PackagePath)."
    }
    if ([string]$nuspec.Metadata.version -ne $Package.Version) {
        throw "Package version mismatch in $($Package.PackagePath)."
    }
    if ([string]$nuspec.Metadata.license.type -ne "expression" -or
        [string]$nuspec.Metadata.license.'#text' -ne "MIT") {
        throw "Package $($Package.Id) must declare the MIT license expression."
    }
    if ([string]$nuspec.Metadata.repository.type -ne "git" -or
        [string]$nuspec.Metadata.repository.url -ne "https://github.com/AJ-comp/Mythosia.AI.git") {
        throw "Package $($Package.Id) has incorrect repository metadata."
    }
    if ([string]$nuspec.Metadata.repository.commit -ne $ExpectedSourceCommit) {
        throw "Package $($Package.Id) repository commit does not match $ExpectedSourceCommit."
    }
    if ([string]$nuspec.Metadata.authors -ne "JJW") {
        throw "Package $($Package.Id) must declare JJW as its author."
    }

    $description = [string]$nuspec.Metadata.description
    if ([string]::IsNullOrWhiteSpace($description) -or $description.Length -gt 4000) {
        throw "Package $($Package.Id) must have a non-empty description of at most 4,000 characters."
    }

    $tags = [string]$nuspec.Metadata.tags
    if ([string]::IsNullOrWhiteSpace($tags) -or $tags.Length -gt 4000) {
        throw "Package $($Package.Id) must have non-empty tags of at most 4,000 characters."
    }

    if ([string]$nuspec.Metadata.projectUrl -ne $Package.ProjectUrl) {
        throw "Package $($Package.Id) has an incorrect project URL."
    }

    $releaseNotes = [string]$nuspec.Metadata.releaseNotes
    if ([string]::IsNullOrWhiteSpace($releaseNotes) -or $releaseNotes.Length -gt 35000 -or
        -not $releaseNotes.Contains("v$($Package.Version)") -or
        -not $releaseNotes.Contains($Package.ReleaseNotesUrl)) {
        throw "Package $($Package.Id) must have current, linked release-notes metadata."
    }

    if ([string]$nuspec.Metadata.readme -ne "README.md" -or
        -not ($nuspec.Entries -contains "README.md")) {
        throw "Package $($Package.Id) must contain its declared root README.md."
    }
    if (-not ($nuspec.Entries -contains "RELEASE_NOTES.md") -or
        [string]::IsNullOrWhiteSpace([string]$nuspec.ReleaseNotesText)) {
        throw "Package $($Package.Id) must contain a non-empty root RELEASE_NOTES.md."
    }
    if (-not ([string]$nuspec.ReleaseNotesText).Contains("## v$($Package.Version)")) {
        throw "Package $($Package.Id) release notes do not describe v$($Package.Version)."
    }
    if ([string]::IsNullOrWhiteSpace([string]$nuspec.ReadmeText) -or
        -not ([string]$nuspec.ReadmeText).Contains($Package.ReleaseNotesUrl)) {
        throw "Package $($Package.Id) README must link to its absolute release-notes URL."
    }
    if ([string]$nuspec.ReadmeText -match '(?i)\]\(\s*RELEASE_NOTES\.md(?:[#?][^)]*)?\)') {
        throw "Package $($Package.Id) README contains a relative release-notes link that breaks on NuGet.org."
    }

    $assemblyEntry = "lib/netstandard2.1/$($Package.Assembly)"
    if (-not ($nuspec.Entries -contains $assemblyEntry)) {
        throw "Package $($Package.Id) is missing $assemblyEntry."
    }

    $dependencyNodes = @()
    if ($null -ne $nuspec.Metadata.dependencies) {
        $dependencyNodes += @($nuspec.Metadata.dependencies.dependency)
        foreach ($group in @($nuspec.Metadata.dependencies.group)) {
            if ($null -ne $group) {
                $dependencyNodes += @($group.dependency)
            }
        }
    }

    # A bare nuspec dependency version is NuGet's inclusive minimum version,
    # not an exact pin. Validate the intended minimum entries verbatim.
    $expectedMinimumVersions = @{}
    foreach ($dependency in $Package.Dependencies.GetEnumerator()) {
        $dependencyId = [string]$dependency.Key
        $releasePackageId = [string]$dependency.Value
        $expectedMinimumVersions[$dependencyId] = [string]$VersionsById[$releasePackageId]
    }
    foreach ($dependency in $Package.FixedDependencies.GetEnumerator()) {
        $expectedMinimumVersions[[string]$dependency.Key] = [string]$dependency.Value
    }

    $actualDependencies = @($dependencyNodes | Where-Object {
        $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string]$_.id)
    })
    if ($actualDependencies.Count -ne $expectedMinimumVersions.Count) {
        throw "Package $($Package.Id) dependency count mismatch. Expected $($expectedMinimumVersions.Count), found $($actualDependencies.Count)."
    }

    $seenDependencies = @{}
    foreach ($actualDependency in $actualDependencies) {
        $dependencyId = [string]$actualDependency.id
        $actualVersion = [string]$actualDependency.version
        if ($seenDependencies.ContainsKey($dependencyId)) {
            throw "Package $($Package.Id) contains duplicate dependency $dependencyId."
        }
        $seenDependencies[$dependencyId] = $true

        if (-not $expectedMinimumVersions.ContainsKey($dependencyId)) {
            throw "Package $($Package.Id) contains unexpected dependency $dependencyId $actualVersion."
        }

        $expectedMinimumVersion = [string]$expectedMinimumVersions[$dependencyId]
        if ($actualVersion -ne $expectedMinimumVersion) {
            throw "Package $($Package.Id) must declare $dependencyId minimum version $expectedMinimumVersion, found $actualVersion."
        }
    }

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Package.PackagePath).Hash.ToLowerInvariant()
}

function Test-SymbolPackageArtifact {
    param(
        [pscustomobject]$Package,
        [string]$ExpectedSourceCommit
    )

    if (-not (Test-Path -LiteralPath $Package.SymbolPackagePath -PathType Leaf)) {
        throw "Expected symbol package was not produced: $($Package.SymbolPackagePath)"
    }

    $symbolFiles = @(Get-ChildItem -LiteralPath $artifactsDir -Filter "$($Package.Id).$($Package.Version)*.snupkg")
    if ($symbolFiles.Count -ne 1 -or $symbolFiles[0].FullName -ne $Package.SymbolPackagePath) {
        throw "Expected one exact symbol package artifact for $($Package.Id) $($Package.Version)."
    }

    $nuspec = Get-NuspecMetadata -PackagePath $Package.SymbolPackagePath
    if ([string]$nuspec.Metadata.id -ne $Package.Id) {
        throw "Symbol package ID mismatch in $($Package.SymbolPackagePath)."
    }
    if ([string]$nuspec.Metadata.version -ne $Package.Version) {
        throw "Symbol package version mismatch in $($Package.SymbolPackagePath)."
    }
    if ([string]$nuspec.Metadata.repository.type -ne "git" -or
        [string]$nuspec.Metadata.repository.url -ne "https://github.com/AJ-comp/Mythosia.AI.git" -or
        [string]$nuspec.Metadata.repository.commit -ne $ExpectedSourceCommit) {
        throw "Symbol package $($Package.Id) has incorrect source provenance metadata."
    }

    $packageTypes = @($nuspec.Metadata.packageTypes.packageType | Where-Object { $null -ne $_ })
    if ($packageTypes.Count -ne 1 -or
        [string]$packageTypes[0].name -ne "SymbolsPackage") {
        throw "Symbol package $($Package.Id) must declare the SymbolsPackage package type."
    }

    $pdbName = [System.IO.Path]::GetFileNameWithoutExtension($Package.Assembly) + ".pdb"
    $expectedPdbEntry = "lib/netstandard2.1/$pdbName"
    if (-not ($nuspec.Entries -contains $expectedPdbEntry)) {
        throw "Symbol package $($Package.Id) is missing $expectedPdbEntry."
    }

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Package.SymbolPackagePath).Hash.ToLowerInvariant()
}

function Get-PackageBaseAddress {
    $serviceIndex = Invoke-RestMethod -Uri $nugetSource -Method Get
    $packageBase = $serviceIndex.resources |
        Where-Object { $_.'@type' -like 'PackageBaseAddress*' } |
        Select-Object -First 1 -ExpandProperty '@id'

    if (-not $packageBase) {
        throw "Could not find PackageBaseAddress in the NuGet service index."
    }

    return $packageBase
}

function Test-PackageVersionExists {
    param(
        [string]$PackageBase,
        [string]$PackageId,
        [string]$Version
    )

    $indexUrl = "$PackageBase$($PackageId.ToLowerInvariant())/index.json"
    try {
        $result = Invoke-RestMethod -Uri $indexUrl -Method Get
        return @($result.versions) -contains $Version.ToLowerInvariant()
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 404) {
            return $false
        }

        throw "Failed to query NuGet for ${PackageId}: $($_.Exception.Message)"
    }
}

function Get-RemotePackageSourceCommit {
    param(
        [string]$PackageBase,
        [string]$PackageId,
        [string]$Version
    )

    $lowerId = $PackageId.ToLowerInvariant()
    $lowerVersion = $Version.ToLowerInvariant()
    $packageUrl = "$PackageBase$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
    $client = [System.Net.Http.HttpClient]::new()
    try {
        $response = $client.GetAsync($packageUrl).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -eq 404) {
                return $null
            }

            $response.EnsureSuccessStatusCode() | Out-Null
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }

    $stream = [System.IO.MemoryStream]::new($bytes, $false)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like "*.nuspec" })
        if ($nuspecEntries.Count -ne 1) {
            throw "Published package $PackageId $Version has an invalid nuspec layout."
        }

        $entryStream = $nuspecEntries[0].Open()
        $reader = [System.IO.StreamReader]::new($entryStream)
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $entryStream.Dispose()
        }

        if ([string]$nuspec.package.metadata.id -ne $PackageId -or
            [string]$nuspec.package.metadata.version -ne $Version) {
            throw "Published package identity does not match $PackageId $Version."
        }

        return ([string]$nuspec.package.metadata.repository.commit).ToLowerInvariant()
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

function Test-IsNuGetConflictOutput {
    param([object[]]$Output)

    $text = (@($Output) | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    return $text -match '(?i)(\b409\b|conflict)'
}

function Wait-ForVerifiedRemotePackage {
    param(
        [string]$PackageBase,
        [pscustomobject]$Package,
        [string]$ExpectedSourceCommit,
        [int]$MaxAttempts = 12,
        [int]$DelaySeconds = 10
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if (Test-PackageVersionExists `
            -PackageBase $PackageBase `
            -PackageId $Package.Id `
            -Version $Package.Version) {
            $remoteCommit = Get-RemotePackageSourceCommit `
                -PackageBase $PackageBase `
                -PackageId $Package.Id `
                -Version $Package.Version
            if (-not [string]::IsNullOrWhiteSpace($remoteCommit) -and
                $remoteCommit -ne $ExpectedSourceCommit) {
                throw "Published package $($Package.Id) $($Package.Version) was not built from release manifest commit $ExpectedSourceCommit. Resume refused."
            }
            if ($remoteCommit -eq $ExpectedSourceCommit) {
                return
            }
        }

        if ($attempt -lt $MaxAttempts) {
            Write-Host "$($Package.Id) $($Package.Version) is not fully readable from PackageBaseAddress yet; retrying provenance verification ($attempt/$MaxAttempts)..."
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    throw "Remote package $($Package.Id) $($Package.Version) did not become readable for provenance verification. Resume refused."
}

function Push-VerifiedResumeSymbols {
    param([pscustomobject]$State)

    $package = $State.Package
    if (-not $AllowPartialResume -or -not $State.ResumeProvenanceVerified) {
        throw "Symbol resume requires verified main-package provenance for $($package.Id) $($package.Version)."
    }

    Write-Host "Verified existing main package; ensuring symbols for $($package.Id) $($package.Version)..."
    # NuGet.org's SymbolPackagePublish contract accepts repeat submissions after
    # publication and returns 409 only while a prior symbol submission for the same
    # ID/version is still being validated. --skip-duplicate is therefore safe only
    # in this provenance-verified symbol-only resume function. Main packages are never
    # pushed with this option.
    & dotnet nuget push $package.SymbolPackagePath `
        --source $nugetSource `
        --api-key $env:NUGET_API_KEY `
        --skip-duplicate

    if ($LASTEXITCODE -ne 0) {
        throw "Symbol-only resume failed for $($package.SymbolPackagePath)"
    }
}

function Assert-PublishableManifest {
    param(
        [pscustomobject]$Manifest,
        [string]$ExpectedSourceCommit
    )

    if ([int]$Manifest.schemaVersion -ne 3) {
        throw "Unsupported release manifest schema."
    }
    if ([string]$Manifest.sourceCommit -ne $ExpectedSourceCommit) {
        throw "Release manifest sourceCommit does not match the checked-out source commit."
    }
    if ([string]$Manifest.provenance -ne "clean-release") {
        throw "Release manifest is not publishable. Repack from a clean worktree without -AllowDirtyValidationPack."
    }
}

$packages = @($releasePackages | ForEach-Object { Get-ReleasePackageMetadata -Definition $_ })
$versionsById = @{}
foreach ($package in $packages) {
    $versionsById[$package.Id] = $package.Version
}

if ($Mode -eq "Pack") {
    $packProvenance = "clean-release"
    if ($AllowDirtyValidationPack) {
        $packProvenance = "development-validation"
        Write-Warning "Packing from a potentially dirty worktree for development validation only. This manifest cannot be published."
    }
    else {
        Assert-CleanGitWorktree
    }

    if (Test-Path -LiteralPath $artifactsDir) {
        Remove-Item -LiteralPath $artifactsDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $artifactsDir | Out-Null

    foreach ($package in $packages) {
        Write-Host "Packing $($package.Id) $($package.Version)..."
        & dotnet pack $package.ProjectPath `
            --configuration Release `
            --no-restore `
            --output $artifactsDir `
            -p:ContinuousIntegrationBuild=true `
            -p:TreatWarningsAsErrors=true

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet pack failed for $($package.ProjectPath)"
        }
    }

    $manifestPackages = @()
    foreach ($package in $packages) {
        $sha256 = Test-PackageArtifact `
            -Package $package `
            -VersionsById $versionsById `
            -ExpectedSourceCommit $sourceCommit
        $symbolsSha256 = Test-SymbolPackageArtifact `
            -Package $package `
            -ExpectedSourceCommit $sourceCommit
        $manifestPackages += [ordered]@{
            id = $package.Id
            version = $package.Version
            file = [System.IO.Path]::GetFileName($package.PackagePath)
            sha256 = $sha256
            symbolsFile = [System.IO.Path]::GetFileName($package.SymbolPackagePath)
            symbolsSha256 = $symbolsSha256
        }
    }

    $manifest = [ordered]@{
        schemaVersion = 3
        generatedUtc = [DateTime]::UtcNow.ToString("O")
        sourceCommit = $sourceCommit
        provenance = $packProvenance
        packages = $manifestPackages
    }
    $manifest | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $manifestPath -Encoding utf8

    Write-Host ""
    Write-Host "All release packages were packed and validated."
    Write-Host "Manifest: $manifestPath"
    if ($AllowDirtyValidationPack) {
        Write-Host "No package was pushed. This development-validation manifest is intentionally not publishable."
    }
    else {
        Write-Host "No package was pushed. Run consumer smoke tests before invoking -Mode Push."
    }
    exit 0
}

Assert-CleanGitWorktree

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Release manifest is missing: $manifestPath. Run -Mode Pack first."
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
Assert-PublishableManifest -Manifest $manifest -ExpectedSourceCommit $sourceCommit

$manifestPackages = @($manifest.packages)
if ($manifestPackages.Count -ne $packages.Count) {
    throw "Release manifest package count does not match the explicit release set."
}

for ($index = 0; $index -lt $packages.Count; $index++) {
    $package = $packages[$index]
    $entry = $manifestPackages[$index]
    if ($entry.id -ne $package.Id -or $entry.version -ne $package.Version -or
        $entry.file -ne [System.IO.Path]::GetFileName($package.PackagePath) -or
        $entry.symbolsFile -ne [System.IO.Path]::GetFileName($package.SymbolPackagePath)) {
        throw "Release manifest order or package metadata does not match the release set."
    }

    $actualHash = Test-PackageArtifact `
        -Package $package `
        -VersionsById $versionsById `
        -ExpectedSourceCommit $sourceCommit
    if ($actualHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "Package checksum changed after validation: $($package.PackagePath)"
    }
    $actualSymbolsHash = Test-SymbolPackageArtifact `
        -Package $package `
        -ExpectedSourceCommit $sourceCommit
    if ($actualSymbolsHash -ne ([string]$entry.symbolsSha256).ToLowerInvariant()) {
        throw "Symbol package checksum changed after validation: $($package.SymbolPackagePath)"
    }
}

if (-not $env:NUGET_API_KEY) {
    throw "NUGET_API_KEY secret is missing."
}

$packageBase = Get-PackageBaseAddress
$publicationState = @()
foreach ($package in $packages) {
    $publicationState += [pscustomobject]@{
        Package = $package
        Exists = Test-PackageVersionExists `
            -PackageBase $packageBase `
            -PackageId $package.Id `
            -Version $package.Version
        ResumeProvenanceVerified = $false
    }
}

$publishedCount = @($publicationState | Where-Object { $_.Exists }).Count
if ($publishedCount -gt 0 -and -not $AllowPartialResume) {
    $published = $publicationState | Where-Object { $_.Exists } |
        ForEach-Object { "$($_.Package.Id) $($_.Package.Version)" }
    throw "A target package version already exists ($($published -join ', ')). Publication refuses to skip existing versions unless -AllowPartialResume is explicitly enabled."
}
if ($AllowPartialResume) {
    foreach ($state in @($publicationState | Where-Object { $_.Exists })) {
        Wait-ForVerifiedRemotePackage `
            -PackageBase $packageBase `
            -Package $state.Package `
            -ExpectedSourceCommit $sourceCommit
        $state.ResumeProvenanceVerified = $true
    }
}

foreach ($state in $publicationState) {
    $package = $state.Package
    if ($state.Exists) {
        Push-VerifiedResumeSymbols -State $state
        continue
    }

    Write-Host "Pushing $($package.Id) $($package.Version)..."
    # NuGet.org's dotnet push contract uploads the matching .snupkg found beside
    # the .nupkg. Both artifacts and their checksums were validated above.
    $pushOutput = @(& dotnet nuget push $package.PackagePath `
        --source $nugetSource `
        --api-key $env:NUGET_API_KEY `
        --force-english-output 2>&1)
    $pushExitCode = $LASTEXITCODE
    $pushOutput | ForEach-Object { Write-Host $_ }

    if ($pushExitCode -ne 0) {
        if ($AllowPartialResume -and (Test-IsNuGetConflictOutput -Output $pushOutput)) {
            # The flat-container index can lag behind an accepted package push. Never
            # turn that 409 into an unverified skip: wait until the remote nupkg is
            # readable, then prove its repository.commit matches this manifest.
            Wait-ForVerifiedRemotePackage `
                -PackageBase $packageBase `
                -Package $package `
                -ExpectedSourceCommit $sourceCommit
            $state.Exists = $true
            $state.ResumeProvenanceVerified = $true
            Push-VerifiedResumeSymbols -State $state
            continue
        }

        throw "dotnet nuget push failed for $($package.PackagePath)"
    }
}

Write-Host ""
Write-Host "Dependency-ordered NuGet publication completed."
