[CmdletBinding()]
param(
    [switch]$Offline,
    [switch]$AllowPartialResume
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-plan.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$plan = Get-ReleasePlan
if ($Offline -and $AllowPartialResume) { throw 'Partial resume requires online provenance verification.' }
& git -C $repoRoot cat-file -e "$($plan.PreviousReleaseCommit)^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Missing reviewed release baseline $($plan.PreviousReleaseCommit). Fetch full Git history before readiness validation." }

$projectDirectories = @{}
$workspaceProjects = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.csproj' -Recurse -File) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    if ((Get-ReleaseProjectProperty $xml 'IsPackable') -ne 'true') { continue }
    $id = Get-ReleaseProjectProperty $xml 'PackageId'
    $relative = $file.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $workspaceProjects[$id] = @{ Path = $relative; Xml = $xml; Version = (Get-ReleaseProjectProperty $xml 'Version') }
    $projectDirectories[$id] = $relative.Substring(0, $relative.LastIndexOf('/'))
}
$changed = @(& git -c core.safecrlf=false -C $repoRoot diff --name-only $plan.PreviousReleaseCommit -- src)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect production changes against the reviewed baseline.' }
$untracked = @(& git -C $repoRoot ls-files --others --exclude-standard -- src)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect untracked production files.' }
Assert-ReleaseChangeCoverage -Packages $plan.Packages -ProjectDirectories $projectDirectories -ChangedPaths @($changed + $untracked)
$releaseVersions = @{}
foreach ($package in $plan.Packages) { $releaseVersions[$package.Id] = $package.Version }
foreach ($package in $plan.Packages) {
    if (-not $workspaceProjects.ContainsKey($package.Id)) { throw "Release project is missing or not packable: $($package.Id)." }
    $project = $workspaceProjects[$package.Id]
    Assert-ReleaseProjectIdentity -Package $package -ProjectXml $project.Xml
    if ($project.Path -cne $package.Project -or $project.Version -cne $package.Version) {
        throw "$($package.Id) project path/version does not match the release plan ($($package.Version))."
    }
    $baselineProjectText = @(& git -C $repoRoot show "$($plan.PreviousReleaseCommit):$($package.Project)" 2>$null)
    if ($LASTEXITCODE -eq 0) {
        Assert-ReleaseVersionAdvance -PackageId $package.Id -Version $package.Version `
            -PreviousVersion (Get-ReleaseProjectProperty ([xml](($baselineProjectText -join "`n").TrimStart([char]0xfeff))) 'Version')
    }
    $required = @{
        TargetFramework = $package.TargetFramework
        PackageLicenseExpression = $package.LicenseExpression
        RepositoryUrl = 'https://github.com/AJ-comp/Mythosia.AI.git'
        RepositoryType = 'git'; PublishRepositoryUrl = 'true'; IncludeSymbols = 'true'
        SymbolPackageFormat = 'snupkg'; PackageReadmeFile = 'README.md'
        PackageProjectUrl = $package.ProjectUrl; Authors = 'JJW'
    }
    foreach ($name in $required.Keys) {
        if ((Get-ReleaseProjectProperty $project.Xml $name) -cne $required[$name]) {
            throw "$($package.Id) has incorrect $name metadata."
        }
    }
    foreach ($name in @('Description', 'PackageTags')) {
        $text = Get-ReleaseProjectProperty $project.Xml $name
        if ([string]::IsNullOrWhiteSpace($text) -or $text.Length -gt 4000) { throw "$($package.Id) has invalid $name metadata." }
    }
    Assert-ReleaseDescriptionVersion -PackageId $package.Id -Version $package.Version `
        -Description (Get-ReleaseProjectProperty $project.Xml 'Description')
    $notes = Get-Content -LiteralPath (Join-Path $repoRoot $package.ReleaseNotes) -Raw
    $firstHeading = [regex]::Match($notes, '(?m)^## ([^\r\n]+)').Groups[1].Value.Trim()
    if ($firstHeading -cne "v$($package.Version)") { throw "$($package.Id) release notes must lead with v$($package.Version), not '$firstHeading'." }
    $releaseNotesMetadata = Get-ReleaseProjectProperty $project.Xml 'PackageReleaseNotes'
    if ([string]::IsNullOrWhiteSpace($releaseNotesMetadata) -or $releaseNotesMetadata.Length -gt 35000 -or
        -not $releaseNotesMetadata.Contains("v$($package.Version)") -or
        -not $releaseNotesMetadata.Contains($package.ReleaseNotesUrl) -or
        $releaseNotesMetadata -match '(?i)next version (?:not|has not been) assigned') {
        throw "$($package.Id) package release notes do not identify the prepared version and its linked notes."
    }
    $readme = Get-Content -LiteralPath (Join-Path $repoRoot $package.Readme) -Raw
    if (-not $readme.Contains($package.ReleaseNotesUrl)) { throw "$($package.Id) README is missing the prepared release-notes link." }
    foreach ($file in @('README.md', 'RELEASE_NOTES.md')) {
        $items = @($project.Xml.Project.ItemGroup.None | Where-Object {
            [string]$_.Update -ceq $file -and [string]$_.Pack -ieq 'true' -and @('/', '\') -contains [string]$_.PackagePath
        })
        if ($items.Count -ne 1) { throw "$($package.Id) must pack $file at the package root." }
    }
    $expected = @{}
    foreach ($edge in $package.Dependencies.GetEnumerator()) { $expected[$edge.Key] = $releaseVersions[$edge.Value] }
    foreach ($edge in $package.FixedDependencies.GetEnumerator()) { $expected[$edge.Key] = $edge.Value }
    $actual = @{}
    foreach ($reference in @($project.Xml.Project.ItemGroup.PackageReference | Where-Object { $null -ne $_ })) {
        $actual[[string]$reference.Include] = [string]$reference.Version
    }
    foreach ($reference in @($project.Xml.Project.ItemGroup.ProjectReference | Where-Object { $null -ne $_ })) {
        $referencePath = [IO.Path]::GetFullPath((Join-Path (Split-Path (Join-Path $repoRoot $project.Path)) ([string]$reference.Include).Replace('\', '/')))
        [xml]$referenceXml = Get-Content -LiteralPath $referencePath -Raw
        $id = Get-ReleaseProjectProperty $referenceXml 'PackageId'
        $version = Get-ReleaseProjectProperty $referenceXml 'Version'
        $actual[$id] = $version
        if (-not $releaseVersions.ContainsKey($id)) {
            $relativeReference = $referencePath.Substring($repoRoot.Length + 1).Replace('\', '/')
            $baselineXmlText = @(& git -C $repoRoot show "$($plan.PreviousReleaseCommit):$relativeReference" 2>$null)
            if ($LASTEXITCODE -ne 0 -or
                (Get-ReleaseProjectProperty ([xml](($baselineXmlText -join "`n").TrimStart([char]0xfeff))) 'Version') -cne $version) {
                throw "$($package.Id) references a new workspace dependency version omitted from the release plan: $id $version."
            }
        }
    }
    if ($actual.Count -ne $expected.Count) { throw "$($package.Id) dependency count does not match the release plan." }
    foreach ($id in $actual.Keys) {
        if (-not $expected.ContainsKey($id) -or $actual[$id] -cne $expected[$id]) { throw "$($package.Id) dependency $id $($actual[$id]) does not match the release plan." }
    }
}

if (-not $Offline) {
    $head = [string](& git -C $repoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve release source commit.' }
    if ($AllowPartialResume -and @(& git -C $repoRoot status --porcelain --untracked-files=all).Count -ne 0) {
        throw 'Partial resume requires a clean source checkout before provenance verification.'
    }
    $readVersions = {
        param($id)
        try {
            $index = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/index.json" -TimeoutSec 30
            return @(Get-ValidatedNuGetVersions -Index $index)
        }
        catch {
            if ($null -ne $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return @() }
            throw "Could not establish NuGet availability for ${id}: $($_.Exception.Message)"
        }
    }
    $readSourceCommit = {
        param($id, $version)
        Add-Type -AssemblyName System.Net.Http
        Add-Type -AssemblyName System.IO.Compression
        $client = [System.Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromSeconds(60)
        try { $bytes = $client.GetByteArrayAsync("https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/$($version.ToLowerInvariant())/$($id.ToLowerInvariant()).$($version.ToLowerInvariant()).nupkg").GetAwaiter().GetResult() }
        finally { $client.Dispose() }
        $stream = [IO.MemoryStream]::new($bytes, $false)
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
        try {
            $entry = $zip.GetEntry("$id.nuspec")
            if ($null -eq $entry) { throw "Published package has no expected nuspec: $id." }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml]$nuspec = $reader.ReadToEnd(); return [string]$nuspec.package.metadata.repository.commit }
            finally { $reader.Dispose() }
        }
        finally { $zip.Dispose(); $stream.Dispose() }
    }
    Assert-ReleaseVersionsAvailable -Packages $plan.Packages -GetPublishedVersions $readVersions `
        -AllowPartialResume:$AllowPartialResume -ExpectedSourceCommit $head.Trim() -GetPublishedSourceCommit $readSourceCommit
}
Write-Host "Release readiness passed for $(@($plan.Packages).Count) explicit targets ($([string]$(if ($Offline) { 'offline structure and coverage only' } else { 'including NuGet version availability' })))."
