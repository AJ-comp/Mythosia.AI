[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-plan.ps1')

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    try { & $Action }
    catch {
        if ($_.Exception.Message -like $Message) { return }
        throw
    }
    throw "Expected readiness rejection: $Message"
}

$plan = Get-ReleasePlan
Assert-ReleasePlanStructure $plan
Assert-Rejected { Assert-ReleaseVersionAdvance 'Example' '4.0.1' '4.0.1' } '*must advance*'
Assert-Rejected { Assert-ReleaseVersionAdvance 'Example' '4.0.0' '4.0.1' } '*must advance*'
Assert-Rejected { Assert-ReleaseVersionAdvance 'Example' '1.0.0-preview.2' '1.0.0-preview.10' } '*must advance*'
Assert-ReleaseVersionAdvance 'Example' '4.1.0' '4.0.1'
Assert-ReleaseVersionAdvance 'Example' '1.0.0' '1.0.0-preview'
Assert-ReleaseVersionAdvance 'Example' '1.0.0-preview.10' '1.0.0-preview.2'

$directories = @{ 'Mythosia.Changed' = 'src/rag/Mythosia.Changed' }
$paths = @('src/rag/Mythosia.Changed/Retriever.cs', 'src/rag/Mythosia.Changed/models/config.json', 'src/rag/Mythosia.Changed/buildTransitive/Model.targets')
foreach ($path in $paths) {
    Assert-Rejected { Assert-ReleaseChangeCoverage @() $directories @($path) } '*Changed production packages are omitted*'
}
Assert-ReleaseChangeCoverage @() $directories @('src/rag/Mythosia.Changed/README.md', 'src/rag/Mythosia.Changed/docs/guide.yml')
Assert-ReleaseChangeCoverage @(@{ Id = 'Mythosia.Changed' }) $directories $paths

$entry = @{ Id = 'Mythosia.Example'; Version = '2.0.0'; TargetFramework = 'net8.0'; LicenseExpression = 'MIT' }
[xml]$project = '<Project><PropertyGroup><PackageId>Mythosia.Example</PackageId><Version>2.0.0</Version><TargetFramework>net8.0</TargetFramework><PackageLicenseExpression>MIT</PackageLicenseExpression></PropertyGroup></Project>'
Assert-ReleaseProjectIdentity $entry $project
$project.Project.PropertyGroup.Version = '1.0.0'
Assert-Rejected { Assert-ReleaseProjectIdentity $entry $project } '*project Version does not match*'
$project.Project.PropertyGroup.Version = '2.0.0'
$project.Project.PropertyGroup.PackageLicenseExpression = 'Apache-2.0'
Assert-Rejected { Assert-ReleaseProjectIdentity $entry $project } '*project PackageLicenseExpression does not match*'
Assert-ReleaseDescriptionVersion 'Mythosia.Example' '2.0.0' "What's New in v2.0.0: Added contracts."
Assert-ReleaseDescriptionVersion 'Mythosia.Example' '0.1.0-preview' "What’s New in v0.1.0-preview: Initial preview."
Assert-ReleaseDescriptionVersion 'Mythosia.Example' '2.0.0' 'Shared contracts without a version heading.'
Assert-Rejected { Assert-ReleaseDescriptionVersion 'Mythosia.Example' '2.0.0' "What's New in v1.0.0: Old release." } '*description advertises a different release version*'
Assert-Rejected { Assert-ReleaseDescriptionVersion 'Mythosia.Example' '2.0.0' "What's New in v2.0.0-preview: Wrong release channel." } '*description advertises a different release version*'

foreach ($index in @($null, 'not an object', 2, $false, @{}, @{ versions = $null }, @{ versions = '2.0.0' }, @{ versions = @() }, @{ versions = @(2) }, @{ versions = @('') }, @{ versions = @($null) }, @{ versions = @('1.0.0', 'invalid') }, @{ versions = @('1.0.0-preview..2') }, @{ versions = @('1.0.0+a..b') }, @{ versions = @('1.0.0-...') })) {
    Assert-Rejected { Get-ValidatedNuGetVersions $index } '*NuGet returned a malformed*'
}
Assert-Rejected {
    Get-ValidatedNuGetVersions -Index @([pscustomobject]@{ versions = @('1.0.0') }, [pscustomobject]@{ versions = @('2.0.0') })
} '*NuGet returned a malformed version index*'
Assert-Rejected {
    Get-ValidatedNuGetVersions -Index @([pscustomobject]@{ versions = @('2.0.0') })
} '*NuGet returned a malformed version index*'
$validatedVersions = @(Get-ValidatedNuGetVersions @{ versions = @('1.0.0', '2.0.0-PREVIEW') })
if (($validatedVersions -join '|') -cne '1.0.0|2.0.0-preview') { throw 'Valid NuGet version indexes must retain all normalized versions.' }
$jsonVersions = @(Get-ValidatedNuGetVersions ('{"versions":["1.0","1.0.0.1","2.0.0-preview.10+build-2"]}' | ConvertFrom-Json))
if (($jsonVersions -join '|') -cne '1.0|1.0.0.1|2.0.0-preview.10+build-2') { throw 'Valid legacy NuGet and SemVer version forms must be accepted.' }

function Test-NuGetReaderResponses {
    # Exercise the actual readiness transport wrapper with a local HTTP-command stub.
    # This checks its 404/error distinction without contacting NuGet or editing files.
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot 'test-release-readiness.ps1'), [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) { throw 'Readiness script must parse before its NuGet reader can be verified.' }
    $assignment = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'readVersions'
    }, $true)
    if ($null -eq $assignment) { throw 'The readiness NuGet reader must be covered by transport fixtures.' }
    $reader = & ([scriptblock]::Create($assignment.Right.Extent.Text))

    function Invoke-RestMethod {
        param([string]$Uri, [int]$TimeoutSec)
        if ($Uri -cne 'https://api.nuget.org/v3-flatcontainer/mythosia.example/index.json' -or $TimeoutSec -ne 30) {
            throw 'NuGet fixture expected the official index URI and a bounded timeout.'
        }
        if ($responseStatus -ne 200) {
            $response = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode]$responseStatus)
            try { throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('Fixture HTTP error', $response) }
            finally { $response.Dispose() }
        }
        return $indexResponse
    }

    $responseStatus = 200
    $indexResponse = '{"versions":["1.0.0","2.0.0-PREVIEW"]}' | ConvertFrom-Json
    if ((@(& $reader 'Mythosia.Example') -join '|') -cne '1.0.0|2.0.0-preview') {
        throw 'Readiness must accept and normalize a legitimate HTTP 200 NuGet index.'
    }
    foreach ($body in @('null', '{}', '{"versions":null}', '{"versions":"2.0.0"}', '{"versions":[]}', '{"versions":["invalid"]}')) {
        $indexResponse = $body | ConvertFrom-Json
        Assert-Rejected { & $reader 'Mythosia.Example' } '*Could not establish NuGet availability*'
    }
    foreach ($responseStatus in @(401, 403, 429, 500, 503)) {
        Assert-Rejected { & $reader 'Mythosia.Example' } '*Could not establish NuGet availability*'
    }
    $responseStatus = 404
    if (@(& $reader 'Mythosia.Example').Count -ne 0) { throw 'Only HTTP 404 may represent an unpublished package ID.' }
}
Test-NuGetReaderResponses

$targets = @(@{ Id = 'Mythosia.Example'; Version = '2.0.0' })
Assert-ReleaseVersionsAvailable $targets { param($id) @('1.0.0') }
Assert-Rejected { Assert-ReleaseVersionsAvailable $targets { param($id) @('2.0.0') } } '*already exists on NuGet.org*'
Assert-Rejected { Assert-ReleaseVersionsAvailable $targets { throw 'NuGet unavailable' } } 'NuGet unavailable'
$commit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
Assert-Rejected { Assert-ReleaseVersionsAvailable $targets { @('2.0.0') } -AllowPartialResume -ExpectedSourceCommit $commit -GetPublishedSourceCommit { 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' } } '*partial resume is forbidden*'
Assert-ReleaseVersionsAvailable $targets { @('2.0.0') } -AllowPartialResume -ExpectedSourceCommit $commit -GetPublishedSourceCommit { $commit }

$badPlan = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'release-plan.psd1')
$badPlan.Packages[1].Dependencies['Mythosia.AI.Abstractions'] = 'Mythosia.Missing'
Assert-Rejected { Assert-ReleasePlanStructure $badPlan } '*release dependency must name an earlier package*'

# A partial patch release must use published dependencies without silently republishing them.
$patchPlan = @{
    SchemaVersion = 1; PreviousReleaseCommit = $commit; BaselineNote = 'Fixture: preceding release is published.'
    Packages = @(
        @{ Id = 'Mythosia.Patch'; Version = '1.0.1'; Project = 'src/rag/Mythosia.Patch/Mythosia.Patch.csproj'
           TargetFramework = 'netstandard2.1'; LicenseExpression = 'MIT'; ProjectUrl = 'https://example.invalid/'
           ReleaseNotesUrl = 'https://example.invalid/RELEASE_NOTES.md#v101'; Dependencies = @{}
           FixedDependencies = @{ 'Mythosia.Unchanged' = '2.0.0' } }
        @{ Id = 'Mythosia.OtherPatch'; Version = '3.0.1'; Project = 'src/rag/Mythosia.OtherPatch/Mythosia.OtherPatch.csproj'
           TargetFramework = 'net10.0'; LicenseExpression = 'MIT'; ProjectUrl = 'https://example.invalid/'
           ReleaseNotesUrl = 'https://example.invalid/RELEASE_NOTES.md#v301'; Dependencies = @{}; FixedDependencies = @{} }
    )
    ConsumerOnlyPackages = @(@{ Id = 'Mythosia.Unchanged'; Version = '2.0.0' })
}
Assert-ReleasePlanStructure $patchPlan
$patchVersions = Get-ReleaseConsumerVersions $patchPlan
Assert-ReleaseConsumerVersionCoverage $patchVersions @('Mythosia.Patch', 'Mythosia.OtherPatch', 'Mythosia.Unchanged')
if ($patchVersions.Count -ne 3 -or $patchPlan.Packages.Count -ne 2) { throw 'Partial release consumer coverage changed publication scope.' }
Assert-Rejected { Assert-ReleaseConsumerVersionCoverage $patchVersions @('Mythosia.Missing') } '*missing an explicit version*'
$patchPlan.ConsumerOnlyPackages[0].Version = '2.1.0'
Assert-Rejected { Assert-ReleasePlanStructure $patchPlan } '*fixed dependency*must match its consumer-only version*'
$patchPlan.ConsumerOnlyPackages[0].Version = '2.0.0'
$patchPlan.ConsumerOnlyPackages += @{ Id = 'Mythosia.Patch'; Version = '1.0.0' }
Assert-Rejected { Assert-ReleasePlanStructure $patchPlan } '*Consumer-only package must be distinct*'
$patchPlan.ConsumerOnlyPackages = @(@{ Id = 'Mythosia.Unchanged'; Version = '2.0.0' })
$patchPlan.Packages[1].FixedDependencies['Mythosia.Patch'] = '1.0.0'
Assert-Rejected { Assert-ReleasePlanStructure $patchPlan } '*must use the release version*'
Write-Host 'Release readiness negative fixtures passed (omissions, old versions, metadata, unavailable feeds, and provenance).'
