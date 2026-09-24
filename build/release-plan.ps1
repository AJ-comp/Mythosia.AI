# Shared, side-effect-free release-plan helpers. Importing this file never builds or publishes.
function Get-ReleasePlan {
    param([string]$Path = (Join-Path $PSScriptRoot 'release-plan.psd1'))
    $plan = Import-PowerShellDataFile -LiteralPath $Path
    Assert-ReleasePlanStructure -Plan $plan
    foreach ($package in $plan.Packages) {
        $directory = $package.Project.Substring(0, $package.Project.LastIndexOf('/'))
        $package.Assembly = $package.Id + '.dll'
        $package.Readme = $directory + '/README.md'
        $package.ReleaseNotes = $directory + '/RELEASE_NOTES.md'
    }
    return $plan
}

function Assert-ReleasePlanStructure {
    param([System.Collections.IDictionary]$Plan)
    if ($Plan.SchemaVersion -ne 1 -or $Plan.PreviousReleaseCommit -notmatch '^[a-f0-9]{40}$' -or
        [string]::IsNullOrWhiteSpace($Plan.BaselineNote) -or @($Plan.Packages).Count -eq 0) {
        throw 'Release plan must declare schema 1, a full reviewed baseline commit, its scope, and publication targets.'
    }
    $allIds = @($Plan.Packages | ForEach-Object { [string]$_.Id })
    $seen = @{}
    foreach ($package in $Plan.Packages) {
        foreach ($field in @('Id', 'Version', 'Project', 'TargetFramework', 'LicenseExpression', 'ProjectUrl', 'ReleaseNotesUrl')) {
            if ([string]::IsNullOrWhiteSpace([string]$package[$field])) { throw "Release plan package is missing $field." }
        }
        if ($package.Id -notmatch '^Mythosia\.[A-Za-z0-9.]+$' -or $seen.ContainsKey($package.Id) -or
            $package.Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or
            $package.Project -notmatch '^src/[^/]+/[^/]+/[^/]+\.csproj$' -or $package.Project.Contains('..') -or
            $package.ReleaseNotesUrl -notmatch ('/RELEASE_NOTES\.md#v' + [regex]::Escape($package.Version.Replace('.', '')) + '$')) {
            throw "Invalid, duplicate, or unversioned release plan entry: $($package.Id)."
        }
        if ($package.Dependencies -isnot [System.Collections.IDictionary] -or
            $package.FixedDependencies -isnot [System.Collections.IDictionary]) {
            throw "$($package.Id) must declare explicit dependency maps."
        }
        foreach ($edge in $package.Dependencies.GetEnumerator()) {
            if ($edge.Key -cne $edge.Value -or -not $seen.ContainsKey($edge.Value)) {
                throw "$($package.Id) release dependency must name an earlier package: $($edge.Key)."
            }
        }
        foreach ($id in $package.FixedDependencies.Keys) {
            if ($allIds -contains $id) { throw "$($package.Id) must use the release version of $id, not a fixed dependency." }
        }
        $seen[$package.Id] = $true
    }
    foreach ($consumer in @($Plan.ConsumerOnlyPackages)) {
        if ($seen.ContainsKey($consumer.Id) -or [string]::IsNullOrWhiteSpace($consumer.Version)) {
            throw "Consumer-only package must be distinct from publication targets: $($consumer.Id)."
        }
        $seen[$consumer.Id] = $true
    }
}

function Get-ReleaseProjectProperty {
    param([xml]$ProjectXml, [string]$Name)
    $nodes = @($ProjectXml.SelectNodes('/Project/PropertyGroup/' + $Name) | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.InnerText)
    })
    if ($nodes.Count -gt 0) { return $nodes[0].InnerText.Trim() }
    return $null
}

function Assert-ReleaseVersionAdvance {
    param([string]$PackageId, [string]$Version, [string]$PreviousVersion)
    if ([string]::IsNullOrWhiteSpace($PreviousVersion)) { return }
    $currentParts = $Version.Split('-', 2)
    $previousParts = $PreviousVersion.Split('-', 2)
    $comparison = ([version]$currentParts[0]).CompareTo([version]$previousParts[0])
    if ($comparison -eq 0) {
        if ($currentParts.Count -eq 1 -and $previousParts.Count -gt 1) { $comparison = 1 }
        elseif ($currentParts.Count -gt 1 -and $previousParts.Count -eq 1) { $comparison = -1 }
        elseif ($currentParts.Count -gt 1) {
            $currentLabels = $currentParts[1].Split('.')
            $previousLabels = $previousParts[1].Split('.')
            for ($i = 0; $i -lt [Math]::Min($currentLabels.Count, $previousLabels.Count); $i++) {
                $currentNumeric = $currentLabels[$i] -match '^\d+$'
                $previousNumeric = $previousLabels[$i] -match '^\d+$'
                if ($currentNumeric -and $previousNumeric) { $comparison = ([long]$currentLabels[$i]).CompareTo([long]$previousLabels[$i]) }
                elseif ($currentNumeric -ne $previousNumeric) { $comparison = $(if ($currentNumeric) { -1 } else { 1 }) }
                else { $comparison = [string]::CompareOrdinal($currentLabels[$i], $previousLabels[$i]) }
                if ($comparison -ne 0) { break }
            }
            if ($comparison -eq 0) { $comparison = $currentLabels.Count.CompareTo($previousLabels.Count) }
        }
    }
    if ($comparison -le 0) { throw "$PackageId release version $Version must advance beyond baseline version $PreviousVersion." }
}

function Assert-ReleaseProjectIdentity {
    param([System.Collections.IDictionary]$Package, [xml]$ProjectXml)
    foreach ($entry in @{
        PackageId = $Package.Id; Version = $Package.Version
        TargetFramework = $Package.TargetFramework; PackageLicenseExpression = $Package.LicenseExpression
    }.GetEnumerator()) {
        if ((Get-ReleaseProjectProperty $ProjectXml $entry.Key) -cne $entry.Value) {
            throw "$($Package.Id) project $($entry.Key) does not match the release plan."
        }
    }
}

function Assert-ReleaseDescriptionVersion {
    param([string]$PackageId, [string]$Version, [string]$Description)
    foreach ($match in [regex]::Matches($Description, "(?i)what['’]s new in v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)")) {
        if ($match.Groups['version'].Value -cne $Version) {
            throw "$PackageId description advertises a different release version: $($match.Groups['version'].Value)."
        }
    }
}

function Get-ValidatedNuGetVersions {
    param([object]$Index)
    # PowerShell enumerates members on arrays: an invalid top-level array of objects
    # can otherwise appear to have a valid .versions property. Require one JSON object.
    if (($Index -isnot [System.Collections.IDictionary] -and $Index -isnot [pscustomobject]) -or
        $Index -is [array] -or $Index.versions -isnot [array] -or $Index.versions.Count -eq 0) {
        throw 'NuGet returned a malformed version index; availability cannot be established.'
    }
    foreach ($version in $Index.versions) {
        if ($version -isnot [string] -or
            $version -notmatch '^[0-9]+(?:\.[0-9]+){1,3}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
            throw 'NuGet returned a malformed version value; availability cannot be established.'
        }
    }
    # Validate the complete response before emitting any accepted versions.
    foreach ($version in $Index.versions) {
        $version.ToLowerInvariant()
    }
}

function Assert-ReleaseChangeCoverage {
    param([object[]]$Packages, [System.Collections.IDictionary]$ProjectDirectories, [string[]]$ChangedPaths)
    $planned = @($Packages | ForEach-Object { [string]$_.Id })
    $missing = @{}
    foreach ($path in $ChangedPaths) {
        $normalized = $path.Replace('\', '/')
        # Documentation content alone does not require a package release. Model/tokenizer
        # data, build targets, licenses, source and project metadata do require coverage.
        if ($normalized -match '(?i)\.md$|(^|/)(docs|documentation)/|(^|/)\.gitignore$') { continue }
        foreach ($id in $ProjectDirectories.Keys) {
            if ($normalized.StartsWith($ProjectDirectories[$id] + '/', [StringComparison]::Ordinal) -and
                $planned -notcontains $id) { $missing[$id] = $true }
        }
    }
    if ($missing.Count -gt 0) {
        throw "Changed production packages are omitted from the release plan: $((@($missing.Keys) | Sort-Object) -join ', ')."
    }
}

function Assert-ReleaseVersionsAvailable {
    param(
        [object[]]$Packages,
        [scriptblock]$GetPublishedVersions,
        [switch]$AllowPartialResume,
        [string]$ExpectedSourceCommit,
        [scriptblock]$GetPublishedSourceCommit
    )
    foreach ($package in $Packages) {
        $published = @(& $GetPublishedVersions $package.Id)
        if ($published -contains $package.Version.ToLowerInvariant()) {
            if (-not $AllowPartialResume) {
                throw "Release target already exists on NuGet.org: $($package.Id) $($package.Version). Assign a new version before committing or publishing."
            }
            if ([string]::IsNullOrWhiteSpace($ExpectedSourceCommit) -or $null -eq $GetPublishedSourceCommit -or
                (& $GetPublishedSourceCommit $package.Id $package.Version) -cne $ExpectedSourceCommit) {
                throw "Existing release target $($package.Id) $($package.Version) does not match this source commit; partial resume is forbidden."
            }
        }
    }
}
