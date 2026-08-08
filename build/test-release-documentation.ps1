[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)
$issues = [System.Collections.Generic.List[string]]::new()

function Add-Issue {
    param([string]$Message)

    $issues.Add($Message)
}

function Get-RepositoryRelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($rootPrefix.Length).Replace("\", "/")
    }

    return $fullPath
}

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

$releasePackages = @(
    [pscustomobject]@{
        Id = "Mythosia.AI.Abstractions"
        Version = "3.0.0"
        Project = "src/core/Mythosia.AI.Abstractions/Mythosia.AI.Abstractions.csproj"
        Readme = "src/core/Mythosia.AI.Abstractions/README.md"
        ReleaseNotes = "src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v300"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI"
        Version = "7.0.0"
        Project = "src/core/Mythosia.AI/Mythosia.AI.csproj"
        Readme = "src/core/Mythosia.AI/README.md"
        ReleaseNotes = "src/core/Mythosia.AI/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v700"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI.Providers.Alibaba"
        Version = "2.0.0"
        Project = "src/core/Mythosia.AI.Providers.Alibaba/Mythosia.AI.Providers.Alibaba.csproj"
        Readme = "src/core/Mythosia.AI.Providers.Alibaba/README.md"
        ReleaseNotes = "src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v200"
    }
)

foreach ($package in $releasePackages) {
    $projectPath = Join-Path $repoRoot $package.Project
    $readmePath = Join-Path $repoRoot $package.Readme
    $releaseNotesPath = Join-Path $repoRoot $package.ReleaseNotes

    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        Add-Issue "Missing release project: $($package.Project)"
        continue
    }
    if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
        Add-Issue "Missing package README: $($package.Readme)"
        continue
    }
    if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
        Add-Issue "Missing package release notes: $($package.ReleaseNotes)"
        continue
    }

    [xml]$projectXml = Get-Content -Raw -LiteralPath $projectPath
    $packageId = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageId"
    $version = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "Version"
    $description = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "Description"
    $tags = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageTags"
    $packageReadme = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageReadmeFile"
    $packageReleaseNotes = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageReleaseNotes"

    if ($packageId -ne $package.Id) {
        Add-Issue "$($package.Project) declares PackageId '$packageId', expected '$($package.Id)'."
    }
    if ($version -ne $package.Version) {
        Add-Issue "$($package.Id) declares version '$version', expected '$($package.Version)'."
    }
    if ([string]::IsNullOrWhiteSpace($description) -or $description.Length -gt 4000) {
        Add-Issue "$($package.Id) needs a non-empty Description of at most 4,000 characters."
    }
    if ([string]::IsNullOrWhiteSpace($tags) -or $tags.Length -gt 4000) {
        Add-Issue "$($package.Id) needs non-empty PackageTags of at most 4,000 characters."
    }
    if ($packageReadme -ne "README.md") {
        Add-Issue "$($package.Id) must declare README.md as PackageReadmeFile."
    }
    if ([string]::IsNullOrWhiteSpace($packageReleaseNotes) -or
        $packageReleaseNotes.Length -gt 35000 -or
        -not $packageReleaseNotes.Contains("v$($package.Version)") -or
        -not $packageReleaseNotes.Contains($package.ReleaseNotesUrl)) {
        Add-Issue "$($package.Id) needs current PackageReleaseNotes metadata with an absolute full-notes URL."
    }

    $readmeText = Get-Content -Raw -LiteralPath $readmePath
    if (-not $readmeText.Contains($package.ReleaseNotesUrl)) {
        Add-Issue "$($package.Readme) must link to $($package.ReleaseNotesUrl)."
    }
    if ($readmeText -match '(?i)\]\(\s*RELEASE_NOTES\.md(?:[#?][^)]*)?\)') {
        Add-Issue "$($package.Readme) contains a relative release-notes link that breaks on NuGet.org."
    }

    $releaseNotesText = Get-Content -Raw -LiteralPath $releaseNotesPath
    if (-not $releaseNotesText.Contains("## v$($package.Version)")) {
        Add-Issue "$($package.ReleaseNotes) does not contain the current version heading."
    }
    if (-not $releaseNotesText.Contains("https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v7-migration.md")) {
        Add-Issue "$($package.ReleaseNotes) does not link to the v7 migration guide."
    }

    $packedReleaseNotes = @($projectXml.Project.ItemGroup.None | Where-Object {
        [string]$_.Update -eq "RELEASE_NOTES.md" -and
        [string]$_.Pack -ieq "true" -and
        @("/", "\") -contains [string]$_.PackagePath
    })
    if ($packedReleaseNotes.Count -ne 1) {
        Add-Issue "$($package.Project) must pack RELEASE_NOTES.md at the package root."
    }
}

$docfxPath = Join-Path $repoRoot "docfx.json"
$docfx = Get-Content -Raw -LiteralPath $docfxPath | ConvertFrom-Json
$metadata = @($docfx.metadata)[0]
$metadataSource = @($metadata.src)[0]
$configuredInputs = @($metadataSource.files | ForEach-Object {
    ([string]$_).Replace("\", "/")
} | Sort-Object)
$expectedInputs = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Filter "*.csproj" -File -Recurse |
ForEach-Object {
    [xml]$projectXml = Get-Content -Raw -LiteralPath $_.FullName
    $targetFramework = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "TargetFramework"
    $targetFrameworks = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "TargetFrameworks"
    $assemblyName = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "AssemblyName"

    if ([string]::IsNullOrWhiteSpace($targetFramework) -or
        -not [string]::IsNullOrWhiteSpace($targetFrameworks)) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $_.FullName) must declare exactly one TargetFramework for DocFX assembly metadata."
        return
    }
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = $_.BaseName
    }

    $projectDirectory = Get-RepositoryRelativePath -Path $_.DirectoryName
    "$projectDirectory/bin/Release/$targetFramework/$assemblyName.dll"
} | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)

if ([string]$metadataSource.src -ne ".") {
    Add-Issue "docfx.json metadata source root must be the repository root."
}
$pathComparer = if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$configuredInputSet = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
$expectedInputSet = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($input in $configuredInputs) {
    [void]$configuredInputSet.Add($input)
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $input) -PathType Leaf)) {
        Add-Issue "docfx.json references a missing compiled assembly: $input"
    }
}
foreach ($input in $expectedInputs) {
    [void]$expectedInputSet.Add($input)
}
foreach ($input in $expectedInputs) {
    if (-not $configuredInputSet.Contains($input)) {
        Add-Issue "docfx.json omits compiled source assembly: $input"
    }
}
foreach ($input in $configuredInputs) {
    if (-not $expectedInputSet.Contains($input)) {
        Add-Issue "docfx.json references an unexpected compiled assembly: $input"
    }
}

$configuredReferences = @($metadata.references | ForEach-Object {
    ([string]$_).Replace("\", "/")
})
if ($configuredReferences.Count -ne 1 -or
    $configuredReferences[0] -ne "artifacts/docfx-references/*.dll") {
    Add-Issue "docfx.json must resolve external API dependencies from the prepared DocFX reference directory."
}

$activeDocumentation = @(
    Get-Item -LiteralPath (Join-Path $repoRoot "README.md")
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs") -Filter "*.md" -File -Recurse
    $releasePackages | ForEach-Object { Get-Item -LiteralPath (Join-Path $repoRoot $_.Readme) }
)

$forbiddenPatterns = [ordered]@{
    'service.FunctionCallingPolicy assignment' = 'service\.FunctionCallingPolicy\s*='
    'direct assignment to the read-only Model property' = 'service\.Model\s*=\s*AIModels\.'
    'nonexistent AlibabaCloud endpoint enum' = 'EndpointPlatform\.AlibabaCloud'
    'removed FunctionCallingPolicy.Auto/Required/None value' = 'FunctionCallingPolicy\.(Auto|Required|None)'
    'removed generic AlibabaModels.Qwen3 constant' = 'AlibabaModels\.Qwen3\b'
    'nonexistent UseQwenMaxModel helper' = '\.UseQwenMaxModel\('
    'stale GitHub Wiki link' = 'https://github\.com/AJ-comp/Mythosia\.AI/wiki'
}

foreach ($document in $activeDocumentation) {
    $text = Get-Content -Raw -LiteralPath $document.FullName
    foreach ($pattern in $forbiddenPatterns.GetEnumerator()) {
        if ($text -match $pattern.Value) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument contains $($pattern.Key)."
        }
    }
}

$alibabaReadmePath = Join-Path $repoRoot "src/core/Mythosia.AI.Providers.Alibaba/README.md"
$alibabaReadme = Get-Content -Raw -LiteralPath $alibabaReadmePath
if ($alibabaReadme -match '(?s)StreamAsync\([^)]*\).*?chunk\.Content') {
    Add-Issue "The Alibaba README treats string streaming chunks as StreamingContent."
}

$linkDocuments = @(
    Get-Item -LiteralPath (Join-Path $repoRoot "README.md")
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs") -Filter "*.md" -File -Recurse
    $releasePackages | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $repoRoot $_.Readme)
        Get-Item -LiteralPath (Join-Path $repoRoot $_.ReleaseNotes)
    }
) | Sort-Object -Property FullName -Unique

$rootPrefix = $repoRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

foreach ($document in $linkDocuments) {
    $text = Get-Content -Raw -LiteralPath $document.FullName
    $matches = [regex]::Matches($text, '(?<!\!)\[[^\]]+\]\((?<target>[^)]+)\)')
    foreach ($match in $matches) {
        $target = $match.Groups["target"].Value.Trim()
        if ($target.StartsWith("<") -and $target.EndsWith(">")) {
            $target = $target.Substring(1, $target.Length - 2)
        }
        if ($target -match '^(?i:https?|mailto):' -or $target.StartsWith("#")) {
            continue
        }

        $target = ($target -split '\s+["'']', 2)[0]
        $targetPath = ($target -split '#', 2)[0]
        $targetPath = ($targetPath -split '\?', 2)[0]
        if ([string]::IsNullOrWhiteSpace($targetPath) -or $targetPath -match '[{}*]') {
            continue
        }

        $targetPath = [System.Uri]::UnescapeDataString($targetPath)
        if ($targetPath.StartsWith("/")) {
            $resolvedTarget = [System.IO.Path]::GetFullPath(
                (Join-Path $repoRoot $targetPath.TrimStart("/")))
        }
        else {
            $resolvedTarget = [System.IO.Path]::GetFullPath(
                (Join-Path $document.DirectoryName $targetPath))
        }

        if (-not $resolvedTarget.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $resolvedTarget.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument has a local link outside the repository: $target"
            continue
        }
        if (-not (Test-Path -LiteralPath $resolvedTarget)) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument has a missing local link target: $target"
        }
        elseif (Test-Path -LiteralPath $resolvedTarget -PathType Container) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument links to a directory instead of a document: $target"
        }
    }
}

if ($issues.Count -ne 0) {
    $details = $issues | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Release documentation validation failed:`n$($details -join [Environment]::NewLine)"
}

Write-Host "Release documentation and NuGet metadata validation passed."
Write-Host "Validated $($releasePackages.Count) release packages and $($linkDocuments.Count) Markdown files."
