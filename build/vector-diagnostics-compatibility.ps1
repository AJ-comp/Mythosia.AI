# Shared preparation for the precompiled diagnostics consumer and its release-plan tests.
# The caller loads release-plan.ps1 before invoking these functions.
function Get-VectorDiagnosticsCompatibilityInputs {
    param([System.Collections.IDictionary]$Plan, [object]$Manifest)

    Assert-ReleasePlanStructure -Plan $Plan
    $requiredIds = @('Mythosia.VectorDb.Abstractions', 'Mythosia.AI.Rag.Abstractions', 'Mythosia.VectorDb.InMemory', 'Mythosia.AI.Rag')
    $versions = Get-ReleaseConsumerVersions -Plan $Plan
    Assert-ReleaseConsumerVersionCoverage -Versions $versions -RequiredIds $requiredIds

    # An unchanged package belongs in ConsumerOnlyPackages, not the publication manifest.
    # Still reject a missing or substituted publication artifact instead of falling back to NuGet.
    $releaseIds = @($Plan.Packages | ForEach-Object { [string]$_.Id })
    $entries = @($Manifest.packages)
    if ($entries.Count -ne $releaseIds.Count) {
        throw 'Diagnostics compatibility manifest does not match the publication target count.'
    }
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $id = [string]$entry.id
        if ($releaseIds -cnotcontains $id -or -not $seen.Add($id) -or
            [string]$entry.version -cne $versions[$id]) {
            throw "Diagnostics compatibility manifest has an unexpected, duplicate or mismatched package: $id."
        }
    }

    [pscustomobject]@{ RequiredIds = $requiredIds; Versions = $versions; ReleaseIds = $releaseIds }
}

function New-VectorDiagnosticsNuGetConfig {
    param([string]$ArtifactsDirectory, [string[]]$ReleaseIds)

    $escapedArtifacts = [System.Security.SecurityElement]::Escape($ArtifactsDirectory)
    $releasePatterns = ($ReleaseIds | ForEach-Object {
        '<package pattern="' + [System.Security.SecurityElement]::Escape($_) + '"/>'
    }) -join ''
    @"
<configuration>
  <packageSources><clear/><add key="release" value="$escapedArtifacts"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/>
    <packageSource key="release">$releasePatterns</packageSource>
    <packageSource key="nuget.org"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>
"@
}
